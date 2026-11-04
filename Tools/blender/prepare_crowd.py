"""Extend the cast without rewriting any original model, clip or skeleton.

Blender 4.2: -b --factory-startup --python-exit-code 1 -P ... -- --pass 1
"""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
import prepare_cast as cast

SCRATCH = cast.SOURCES / "_scratch/crowd"
CHARACTERS = cast.CHARACTERS
ROLES = {
    "Mai": "Male_Adult_14",
    "Le": "Male_Adult_02",
    "Captain": "Business_Male_01",
    "Purser": "Business_Male_04",
    "PorterVN_A": "Male_Adult_08",
    "PorterVN_B": "Male_Adult_02",
    "PorterVN_C": "Male_Adult_14",
    "VendorVN_F": "Chef_Female_01",
    "CivilianVN": "Male_Adult_14",
    "PassengerFR_M1": "Business_Male_01",
    "PassengerFR_M2": "Business_Male_01",
    "PassengerFR_F": "Chef_Female_01",
    "SailorFR": "Male_Adult_02",
    "DockerMRS": "Wood_Male_01",
    "Waiter": "Business_Male_04",
}
VN = {"Mai", "Le", "PorterVN_A", "PorterVN_B", "PorterVN_C", "VendorVN_F", "CivilianVN"}
PORTERS = {"PorterVN_A", "PorterVN_B", "PorterVN_C"}
BARE_LEG_TOP = .392
NOTES = {
    "Mai": "Mature shirt base, white buttoned dining-service tunic, dark trousers; added short black hair. Interpretive costume, not a portrait.",
    "Le": "White student outfit chosen from the requested alternatives: youthful base and clear silhouette distinction from the ao-dai civilian. No claim of an exact school uniform or portrait.",
    "Captain": "Navy double-breasted front, two rows of brass buttons, peaked captain kepi, authored beard.",
    "Purser": "Dark coat over waistcoat, pale shirt, cloth sleeve protectors, round metal spectacles.",
    "PorterVN_A": "Slim brown work blouse, rolled trousers, modeled bare calves/feet/toes, shallow conical non la.",
    "PorterVN_B": "Slim indigo work blouse, rolled trousers, modeled bare calves/feet/toes, wrapped headcloth.",
    "PorterVN_C": "Slim faded brown work blouse, rolled trousers, modeled bare calves/feet/toes, uncovered short hair.",
    "VendorVN_F": "Female base; chef hat/apron replaced by black hair, ba-ba blouse, black trousers, shallow non la and scarf.",
    "CivilianVN": "Dark the-style long tunic with side slits, white trousers and wrapped khan dong.",
    "PassengerFR_M1": "Light tropical suit, plain light shirt and shallow pith helmet, for Saigon.",
    "PassengerFR_M2": "Dark travel suit, plain shirt and felt fedora, for Marseille.",
    "PassengerFR_F": "Female base, high-neck ivory blouse, long flared 1910-style skirt, broad-brim hat with ribbon.",
    "SailorFR": "Horizontal navy/ivory jersey stripes, navy trousers, naval beret with red pompom; distinct from Jacques.",
    "DockerMRS": "Workwear base, blue cloth jacket, brown ribbed corduroy trousers, cloth peaked cap and neck scarf.",
    "Waiter": "Black waistcoat, white shirt, ankle-length white waist apron with back ties and black bow tie.",
}


def canonical_rig():
    skeleton = json.loads((CHARACTERS / "skeleton.json").read_text())
    rig = bpy.data.objects.new("Armature", bpy.data.armatures.new("HaiBanTay_Humanoid"))
    bpy.context.collection.objects.link(rig)
    cast.select([rig])
    bpy.ops.object.mode_set(mode="EDIT")
    for entry in skeleton["bones"]:
        bone = rig.data.edit_bones.new(entry["name"])
        # A zero-length edit bone cannot retain the direction from its matrix.
        bone.length = entry["length"]
        bone.matrix = Matrix(entry["rest"])
        if entry["parent"]:
            bone.parent = rig.data.edit_bones[entry["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    for entry in skeleton["bones"]:
        actual = rig.data.bones[entry["name"]].matrix_local
        assert max(abs(a-b) for row, ref in zip(actual, entry["rest"]) for a,b in zip(row,ref)) < .00001, entry["name"]
    return rig


def prune_weights(obj):
    for vertex in obj.data.vertices:
        positive = sorted(((g.group, g.weight) for g in vertex.groups if g.weight > 1e-6), key=lambda p: -p[1])[:4]
        assert positive, (obj.name, vertex.index, "unweighted")
        total = sum(w for _, w in positive)
        for group in [g.group for g in vertex.groups]:
            obj.vertex_groups[group].remove([vertex.index])
        for group, weight in positive:
            obj.vertex_groups[group].add([vertex.index], weight / total, "REPLACE")


def delete_faces(body, predicate):
    data = bmesh.new()
    data.from_mesh(body.data)
    faces = [f for f in data.faces if predicate(f)]
    bmesh.ops.delete(data, geom=faces, context="FACES")
    loose = [v for v in data.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(data, geom=loose, context="VERTS")
    data.to_mesh(body.data)
    data.free()
    body.data.update()
    return len(faces)


def crop_below(body, height):
    data=bmesh.new();data.from_mesh(body.data)
    bmesh.ops.bisect_plane(data,geom=list(data.verts)+list(data.edges)+list(data.faces),
        dist=.000001,plane_co=(0,0,height),plane_no=(0,0,1),clear_inner=True,clear_outer=False)
    loose=[v for v in data.verts if not v.link_faces]
    if loose:bmesh.ops.delete(data,geom=loose,context="VERTS")
    data.to_mesh(body.data);data.free();body.data.update()


def crop_above(body, height):
    data=bmesh.new();data.from_mesh(body.data)
    bmesh.ops.bisect_plane(data,geom=list(data.verts)+list(data.edges)+list(data.faces),
        dist=.000001,plane_co=(0,0,height),plane_no=(0,0,1),clear_inner=False,clear_outer=True)
    loose=[v for v in data.verts if not v.link_faces]
    if loose:bmesh.ops.delete(data,geom=loose,context="VERTS")
    data.to_mesh(body.data);data.free();body.data.update()


def plain(name, color, roughness=.85, metal=0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metal
    return material


def rings(name, sections, mat, rig, weights, segments=40, thickness=.002, closed=True):
    """Elliptical sections (z, rx, ry, cy), skinned per section/angle."""
    vertices, uv, skin = [], [], []
    for j, (z, rx, ry, cy) in enumerate(sections):
        for i in range(segments + 1):
            a = i / segments * math.tau
            vertices.append((rx * math.sin(a), cy - ry * math.cos(a), z))
            uv.append((i / segments, j / (len(sections) - 1)))
            skin.append(weights(z, a) if callable(weights) else weights)
    faces = [(j*(segments+1)+i, j*(segments+1)+i+1, (j+1)*(segments+1)+i+1, (j+1)*(segments+1)+i)
             for j in range(len(sections)-1) for i in range(segments)]
    if closed:
        # The UV seam duplicates vertex 0 at index segments. Including both
        # coincident points in an n-gon makes Unity reject the cap as crossing.
        faces.append(tuple(range(segments-1, -1, -1)))
        faces.append(tuple((len(sections)-1)*(segments+1)+i for i in range(segments)))
    if sections[-1][0] < sections[0][0]:
        faces = [tuple(reversed(face)) for face in faces]
    return cast.mesh(name, vertices, faces, uv, mat, rig, skin, thickness)


def ellipsoid(name, center, scale, mat, rig, bone="Head", seg=20, rows=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rows, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    cast.select([obj])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    cast.skin(obj, rig, [{bone: 1}] * len(obj.data.vertices))
    return obj


def tube(name, points, radius, mat, rig, bone="Head", segments=10):
    pts = [Vector(p) for p in points]
    verts, coords = [], []
    for j, point in enumerate(pts):
        direction = (pts[min(j+1,len(pts)-1)] - pts[max(0,j-1)]).normalized()
        e1 = direction.cross(Vector((0,0,1)))
        if e1.length < .001:
            e1 = direction.cross(Vector((0,1,0)))
        e1.normalize()
        e2 = direction.cross(e1).normalized()
        for i in range(segments):
            a = i/segments*math.tau
            verts.append(point + radius*(e1*math.cos(a)+e2*math.sin(a)))
            coords.append((i/segments,j/max(1,len(pts)-1)))
    faces = [(j*segments+i,j*segments+(i+1)%segments,(j+1)*segments+(i+1)%segments,(j+1)*segments+i)
             for j in range(len(pts)-1) for i in range(segments)]
    return cast.mesh(name, verts, faces, coords, mat, rig, [{bone:1}]*len(verts))


def torso_weight(z, a=0):
    if z < 1.055:
        return {"Hips": max(.1, (1.055-z)/.16), "Spine": max(.1, (z-.895)/.16)}
    if z < 1.21:
        t = (z-1.055)/.155
        return {"Spine": 1-t, "Chest": t}
    t = min(1, (z-1.21)/.17)
    return {"Chest": 1-t, "UpperChest": t}


def skirt_weight(z, a):
    leg = min(1, max(0, (1.02-z)/.20))
    left = min(1, max(0, .5 + math.sin(a)*1.6))
    # All lower hems follow thighs; cross-leg wide poses remain a cloth limitation.
    values = {"Hips": 1-leg, "LeftUpperLeg": leg*left, "RightUpperLeg": leg*(1-left)}
    return {n:w for n,w in values.items() if w > 1e-6}


def panel(name, rig, mat, top, bottom, halfwidth, front_y, skirt=False, rows=18, cols=12):
    verts, uv, weights = [], [], []
    for j in range(rows+1):
        t = j/rows
        z = top+(bottom-top)*t
        for i in range(cols+1):
            u = i/cols
            x = (2*u-1)*halfwidth*(1+.1*t if skirt else 1)
            y = front_y + .035*(abs(x)/halfwidth)**2 + (.004*math.sin(u*math.tau*6)*t if skirt else 0)
            verts.append((x,y,z)); uv.append((u,1-t))
            weights.append(skirt_weight(z, math.asin(max(-1,min(1,x/halfwidth)))) if skirt else torso_weight(z))
    faces = [(j*(cols+1)+i,(j+1)*(cols+1)+i,(j+1)*(cols+1)+i+1,j*(cols+1)+i+1) for j in range(rows) for i in range(cols)]
    return cast.mesh(name,verts,faces,uv,mat,rig,weights,.003)


def shirt_surface(body, excluded_materials=()):
    """Tam giác áo trong bind pose; bỏ cúc để ray không tự trúng cúc cũ."""
    body.data.calc_loop_triangles()
    points = [body.matrix_world @ v.co for v in body.data.vertices]
    triangles = [tuple(t.vertices) for t in body.data.loop_triangles
                 if t.material_index not in excluded_materials]
    return points, triangles, BVHTree.FromPolygons(points, triangles, all_triangles=True)


def button_contact(body, surface, center):
    points, triangles, tree = surface
    hit, _, triangle, _ = tree.ray_cast(Vector((center.x, -1, center.z)), Vector((0, 1, 0)), 2)
    assert hit is not None, ("Không tìm được mặt áo", tuple(center))
    corners = triangles[triangle]
    bary = barycentric_transform(hit, *(points[i] for i in corners),
                                 Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
    weights = {}
    for index, factor in zip(corners, bary):
        for group in body.data.vertices[index].groups:
            name = body.vertex_groups[group.group].name
            weights[name] = weights.get(name, 0) + group.weight * max(0, factor)
    weights = dict(sorted(weights.items(), key=lambda item: -item[1])[:4])
    total = sum(weights.values())
    assert total > 0, "Tam giác áo không có skin weights"
    return hit + Vector((0, -.002, 0)), {name: w/total for name, w in weights.items()}


def button_row(role, rig, mat, x, zs, y=-.191, body=None):
    objects = []
    surface = shirt_surface(body) if body is not None else None
    for i,z in enumerate(zs):
        center, weights = button_contact(body, surface, Vector((x, y, z))) if body is not None else (Vector((x,y,z)), torso_weight(z))
        obj = ellipsoid(f"{role}_Button{x}_{i}",center,(.009,.004,.009),mat,rig,"Spine",seg=12,rows=8)
        obj.vertex_groups.clear()
        obj.modifiers.clear()
        cast.skin(obj,rig,[weights]*len(obj.data.vertices))
        objects.append(obj)
    return objects


def headwear(role, kind, rig, mat, accent):
    z = 1.766
    objects = []
    if kind == "nonla":
        sections = [(z-.028,.285,.285,-.003),(z-.014,.286,.286,-.003),(z+.036,.198,.198,-.003),
                    (z+.087,.107,.107,-.003),(z+.145,.003,.003,-.003)]
        objects.append(rings(role+"_NonLa",sections,mat,rig,{"Head":1},segments=64,closed=False,thickness=.004))
        for radius,height in [(.275,z-.012),(.20,z+.033),(.10,z+.091)]:
            path=[(radius*math.sin(a),-.003-radius*math.cos(a),height) for a in np.linspace(0,math.tau,65)]
            objects.append(tube(role+"_NonLaRib"+str(radius),path,.0028,accent,rig,segments=6))
        path=[(-.09,-.008,z-.01),(-.07,-.018,1.603),(0,-.063,1.581),(.07,-.018,1.603),(.09,-.008,z-.01)]
        objects.append(tube(role+"_ChinTie",path,.004,accent,rig,segments=8))
    elif kind in {"khandong","headcloth"}:
        sections=[(z-.037,.101,.106,0),(z-.02,.108,.116,0),(z+.017,.11,.115,0),(z+.05,.10,.108,0),(z+.074,.004,.004,0)]
        objects.append(rings(role+"_Headwrap",sections,mat,rig,{"Head":1},segments=48,thickness=.003))
        for j in range(4):
            path=[(.109*math.sin(a),-.117*math.cos(a),z-.016+j*.013+.006*math.sin(a)) for a in np.linspace(0,math.tau,65)]
            objects.append(tube(role+"_WrapFold"+str(j),path,.005,mat,rig,segments=6))
        if kind == "headcloth":
            objects.append(cast.ribbon(role+"_WrapTail",[(.10,.05,z),(.115,.06,1.68),(.13,.07,1.59)],.045,mat,rig,"Head"))
    elif kind == "kepi":
        objects.append(rings(role+"_Kepi",[(z-.034,.102,.108,0),(z+.035,.111,.12,0),(z+.086,.113,.123,0),
            (z+.102,.106,.117,0),(z+.104,.001,.001,0)],mat,rig,{"Head":1},segments=48))
        objects.append(rings(role+"_CapBand",[(z-.025,.104,.111,0),(z+.005,.104,.111,0)],accent,rig,{"Head":1},closed=False))
        verts=[(-.088,-.04,z-.027),(.088,-.04,z-.027),(.084,-.138,z-.049),(.055,-.173,z-.06),
               (0,-.184,z-.065),(-.055,-.173,z-.06),(-.084,-.138,z-.049)]
        objects.append(cast.mesh(role+"_Visor",verts,[tuple(range(7))],[(.5+v[0]*4,.5+v[1]*2) for v in verts],accent,rig,[{"Head":1}]*7,.004))
        path=[(.106*math.sin(a),-.12*math.cos(a),z+.012) for a in np.linspace(-1.2,1.2,25)]
        objects.append(tube(role+"_GoldCapCord",path,.004,plain(role+"_CapBrass",(.44,.31,.09),.5,.5),rig))
    elif kind in {"pith","widehat","fedora"}:
        wide = kind == "widehat"
        brim = .26 if wide else .18 if kind == "pith" else .173
        sections=[(z-.005,brim,brim*.89,0),(z+.002,brim,brim*.89,0),(z+.013,.105,.119,0),
            (z+.075,.11,.12,0),(z+.125,.081,.094,0),(z+.153,.003,.003,0)]
        if kind == "fedora":
            sections=[(z-.02,brim,brim*.87,0),(z-.008,brim,brim*.87,0),(z+.003,.108,.116,0),
                (z+.074,.108,.111,0),(z+.107,.095,.10,0),(z+.119,.004,.06,0)]
        objects.append(rings(role+"_"+kind,sections,mat,rig,{"Head":1},segments=64,closed=False))
        objects.append(rings(role+"_HatRibbon",[(z+.023,.112,.121,0),(z+.046,.112,.121,0)],accent,rig,{"Head":1},closed=False))
    elif kind in {"beret","flatcap"}:
        objects.append(rings(role+"_"+kind,[(z-.035,.10,.105,0),(z-.014,.122,.131,0),(z+.015,.134,.141,-.014),
            (z+.045,.114,.126,-.023),(z+.062,.003,.003,-.017)],mat,rig,{"Head":1},segments=48))
        objects.append(rings(role+"_CapBand",[(z-.04,.101,.109,0),(z-.022,.104,.112,0)],accent,rig,{"Head":1},closed=False))
        if kind == "beret":
            objects.append(ellipsoid(role+"_Pompom",(-.015,-.019,z+.083),(.035,.035,.033),plain(role+"_PompomRed",(.48,.017,.025)),rig))
        else:
            vertices=[(-.085,-.06,z-.019),(.085,-.06,z-.019),(.08,-.146,z-.035),(0,-.174,z-.041),(-.08,-.146,z-.035)]
            objects.append(cast.mesh(role+"_ClothPeak",vertices,[(0,1,2,3,4)],[(0,0),(1,0),(1,1),(.5,1),(0,1)],mat,rig,[{"Head":1}]*5,.004))
    return objects


def short_hair(role, rig, mat):
    bottom=1.723 if role in {"VendorVN_F","PassengerFR_F"} else 1.742
    return rings(role+"_ShortHair",[(bottom,.090,.094,.008),(1.766,.091,.092,.006),(1.791,.069,.075,.003),
        (1.808,.036,.044,.001),(1.816,.001,.001,0)],mat,rig,{"Head":1},segments=48,closed=False,thickness=.001)


def trousers(role, rig, mat, bottom=.125):
    objects = []
    for label,sign in [("Left",1),("Right",-1)]:
        sections=[(1.065,.098,.132,.004),(.94,.091,.118,.004),(.85,.093,.102,0),(.72,.083,.089,-.005),(.52,.072,.083,-.012),
                  (.43,.067,.075,-.01),(bottom,.057,.061,.006)]
        def weight(z,a):
            if z > .86: return {"Hips":.25,label+"UpperLeg":.75}
            t=min(1,max(0,(.58-z)/.16))
            ankle=min(1,max(0,(.20-z)/.09))
            ankle=ankle*ankle*(3-2*ankle)
            return {label+"UpperLeg":1-t,label+"LowerLeg":t*(1-ankle),label+"Foot":t*ankle}
        obj=rings(role+"_"+label+"Trousers",sections,mat,rig,weight,segments=32,closed=False)
        for v in obj.data.vertices:
            v.co.x += sign*(.096 if v.co.z>.6 else .111)
        objects.append(obj)
    return objects


def makehuman_body():
    """Chỉ đọc bề mặt body CC0; bỏ toàn bộ helper và lồng khớp của MakeHuman."""
    path = cast.ROOT / "Tools/blender/third_party/makehuman/base.obj"
    vertices, faces, groups = [], [], {}
    group = ""
    for line in path.read_text().splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            vertices.append(Vector(tuple(float(x) for x in fields[1:])))
        elif fields[0] == "g":
            group = fields[1]
        elif fields[0] == "f":
            face = tuple(int(x.split("/")[0]) - 1 for x in fields[1:])
            groups.setdefault(group, []).append(face)
            if group == "body":
                faces.append(face)
    joints = {name: sum((vertices[i] for i in sorted({i for f in fs for i in f})), Vector()) /
              len({i for f in fs for i in f}) for name, fs in groups.items() if name.startswith("joint-")}
    return vertices, faces, joints


def hand_skin(body, role):
    """Da chân và tay dùng cùng atlas, cùng vật liệu; không bị bảng màu áo nhuộm lệch."""
    uv = body.data.uv_layers.active.data
    candidates = []
    body.data.update()
    for polygon in body.data.polygons:
        if not (abs(polygon.center.x) > .79 and 1.36 < polygon.center.z < 1.55):
            continue
        material = body.data.materials[polygon.material_index]
        if material.name.endswith("_Body"):
            candidates.append(polygon)
    assert candidates, (role, "Thiếu bề mặt da tay để lấy atlas")
    polygon = max(candidates, key=lambda p: p.area)
    skin = body.data.materials[polygon.material_index].copy()
    skin.name = role + "_ForearmSkin"
    corners = [uv[i].uv.copy() for i in polygon.loop_indices[:3]]
    center = sum(corners, Vector((0, 0))) / 3
    slot = len(body.data.materials)
    body.data.materials.append(skin)
    for p in candidates:
        p.material_index = slot
    return skin, (center, corners[1] - corners[0], corners[2] - corners[0])


def barelegs(role, rig, skinmat, cuffmat, skin_uv=None, body=None):
    objects = []
    source, faces, joints = makehuman_body()
    floor = min(source[i].y for f in faces for i in f)
    for label,sign in [("Left",1),("Right",-1)]:
        ankle = joints["joint-l-ankle"]
        knee = joints["joint-l-knee"]
        foot = rig.data.bones[label + "Foot"].head_local
        calf = rig.data.bones[label + "LowerLeg"].head_local
        def profile(height):
            points = []
            if body:
                for edge in body.data.edges:
                    a, b = [body.data.vertices[i].co for i in edge.vertices]
                    if min(a.z,b.z) <= height <= max(a.z,b.z) and abs(b.z-a.z) > 1e-7:
                        p = a.lerp(b, (height-a.z)/(b.z-a.z))
                        if p.x * sign > 0: points.append(p)
                points += [v.co for v in body.data.vertices if abs(v.co.z-height)<.00002 and v.co.x*sign>0]
                assert points, (role, label, "Thiếu mặt cắt ống quần", height)
                cx=(min(p.x for p in points)+max(p.x for p in points))*.5
                cy=(min(p.y for p in points)+max(p.y for p in points))*.5
                return cx,cy,max(abs(p.x-cx) for p in points),max(abs(p.y-cy) for p in points)
            return sign*.112,-.008,.057,.065
        cx,cy,rx,ry=profile(.387)
        verts, tex, weights, indices = [], [], [], {}
        for i, point in enumerate(source):
            if point.x <= 0 or point.y > knee.y:
                continue
            t = max(0, (point.y - ankle.y) / (knee.y - ankle.y))
            z = (.002 + (point.y - floor) / (ankle.y - floor) * (foot.z - .002)
                 if point.y < ankle.y else foot.z + t * (calf.z - foot.z))
            x = sign * (abs(foot.x) + t * (abs(calf.x) - abs(foot.x)) +
                        (point.x - (ankle.x + t * (knee.x - ankle.x))) * .095)
            y = foot.y + t * (calf.y - foot.y) - (point.z - (ankle.z + t * (knee.z - ankle.z))) * .078
            # Bắp chân đi vào trong miệng quần, không ló mặt cắt da trên cuff.
            fit=min(1,max(0,(z-.335)/.030)); fit=fit*fit*(3-2*fit)
            radius=math.hypot((x-cx)/(rx*.82),(y-cy)/(ry*.82))
            if radius>1:
                scale=1-fit+fit/radius
                x=cx+(x-cx)*scale; y=cy+(y-cy)*scale
            indices[i] = len(verts); verts.append((x, y, z))
            if skin_uv:
                center, u, v = skin_uv
                # UV nằm trong tam giác da tay, không chạm vùng vải của atlas.
                tex.append(center + u * ((y + .06) / .30 - .5) * .20 + v * (z / .50 - .5) * .20)
            else:
                tex.append(((point.x - ankle.x) * .25 + .5, z / .5))
            blend = min(1, max(0, (.17 - z) / .105))
            blend = blend * blend * (3 - 2 * blend)
            toe = min(1, max(0, (-y - .085) / .085))
            toe = toe * toe * (3 - 2 * toe) * blend
            weights.append({label + "LowerLeg": 1 - blend, label + "Foot": blend - toe, label + "Toes": toe})
        fs = [tuple(indices[i] for i in (f if sign == 1 else tuple(reversed(f))))
              for f in faces if all(i in indices for i in f)]
        leg = cast.mesh(role + "_" + label + "BareLegFoot", verts, fs, tex, skinmat, rig, weights)
        crop_above(leg, BARE_LEG_TOP)
        cast.select([leg])
        reduce=leg.modifiers.new("Giảm chi tiết chân cho Laptop", "DECIMATE")
        reduce.ratio=.60; reduce.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=reduce.name)
        objects.append(leg)
        # Mép xắn ôm bắp chân; đầu trên lấy đúng bán kính ống quần đã cắt.
        top_cx,top_cy,top_rx,top_ry=profile(.405)
        sections = [(.365, rx*.90, ry*.90, cy), (.372, rx+.006, ry+.006, cy),
                    (.387, rx+.007, ry+.007, cy), (.405, top_rx+.004, top_ry+.004, top_cy)]
        cuff=rings(role+"_"+label+"TrouserRoll",sections,cuffmat,rig,{label+"LowerLeg":1},segments=32,closed=False)
        for v in cuff.data.vertices: v.co.x += top_cx if v.co.z>.40 else cx
        objects.append(cuff)
    return objects


def scarf(role, rig, mat):
    obj=rings(role+"_NeckScarf",[(1.476,.068,.063,0),(1.502,.072,.065,0),(1.52,.067,.06,0)],mat,rig,{"UpperChest":1},segments=32,closed=False)
    tails=[]
    for x in [-.042,.042]:
        tails.append(cast.ribbon(role+"_ScarfTail"+str(x),[(x,-.083,1.487),(x*1.4,-.13,1.405),(x*1.55,-.183,1.265)],.062,mat,rig,"UpperChest"))
    return [obj]+tails


def spectacles(role, rig):
    mat=plain(role+"_SpectacleWire",(.18,.135,.075),.35,.7)
    objects=[]
    for sign in [-1,1]:
        path=[(sign*.040+.027*math.cos(a),-.137,1.707+.025*math.sin(a)) for a in np.linspace(0,math.tau,49)]
        objects.append(tube(role+"_Spectacles"+str(sign),path,.0024,mat,rig,segments=8))
        objects.append(tube(role+"_Temple"+str(sign),[(sign*.067,-.137,1.715),(sign*.093,-.075,1.715),(sign*.089,.016,1.715)],.0023,mat,rig))
    objects.append(tube(role+"_Bridge",[(-.014,-.137,1.711),(0,-.149,1.719),(.014,-.137,1.711)],.0024,mat,rig))
    return objects


def beard(role, rig, mat, body):
    verts,uv,faces=[],[],[]
    body.data.update()
    for p in body.data.polygons:
        center=p.center
        if not body.data.materials[p.material_index].name.endswith("_Head"):continue
        if not (1.56<center.z<1.646 and center.y<-.022):continue
        if center.z>1.618 and abs(center.x)<.035:continue
        face=[]
        for index in p.vertices:
            v=body.data.vertices[index]
            face.append(len(verts));verts.append(v.co+v.normal*.0018);uv.append((v.co.x*6+.5,(v.co.z-1.56)*10))
        faces.append(tuple(face))
    assert faces,"No beard surface"
    objects=[cast.mesh(role+"_Beard",verts,faces,uv,mat,rig,[{"Head":1}]*len(verts))]
    for sign in [-1,1]:
        objects.append(ellipsoid(role+"_Moustache"+str(sign),(sign*.018,-.125,1.641),(.021,.005,.006),mat,rig,seg=16,rows=8))
    return objects


def body_atlas(body, role, out, upper_color, lower_color):
    """Recolor UV islands instead of assigning colors across arbitrary triangles."""
    mat=next(m for m in body.data.materials if m.name.endswith("_Body"))
    node=next(n for n in mat.node_tree.nodes if n.type=="TEX_IMAGE" and "BaseColor" in n.image.name)
    pix=cast.image_pixels(node.image);h,w,_=pix.shape
    yy,xx=np.mgrid[0:h,0:w];x=xx/w;y=1-yy/h
    lum=pix[:,:,:3]@np.array([.2126,.7152,.0722])
    torso=(x>.29)&(x<.71)&(y<.87)
    sleeves=((x<.29)|(x>.71))&(y>.435)&(y<.845)
    pants=((x<.29)|(x>.71))&(y<.435)
    def paint(mask,color):
        useful=lum[mask&(lum>.015)]
        mean=float(np.median(useful)) if useful.size else .1
        shade=np.clip(1+(lum-mean)/max(mean,.08)*.35,.55,1.45)
        pix[:,:,:3][mask]=(shade[:,:,None]*np.array(color))[mask]
    upper=torso|sleeves
    original=pix[:,:,:3].copy()
    paint(upper,upper_color);paint(pants,lower_color)
    if role in {"Captain","PassengerFR_M1","PassengerFR_M2"}:
        white=upper&(lum>.32)
        paint(white,(.78,.765,.70))
        tie=(original[:,:,0]>original[:,:,1]*1.5)&(original[:,:,0]>original[:,:,2]*1.5)&(x>.30)&(x<.70)&(y>.84)
        paint(tie,(.055,.055,.05))
    if role in {"Purser","Waiter"}:
        shirt=((x<.29)|(x>.71))&(y>.435)&(y<.845)
        if role=="Waiter":paint(shirt,(.78,.765,.70))
        center=(x>.435)&(x<.56)&(y>.50)&(y<.68)
        paint(center,(.78,.765,.70))
    shoe=(x>.23)&(x<.77)&(y>.875)
    pix[:,:,:3][shoe]=(.035,.026,.018)
    pix[:,:,:3][pants&(y<.033)]=lower_color
    node.image=cast.save_pixels(role+"_Body_BaseColor",pix,out)


def costume(role, body, rig, out, female):
    colors={"Mai":(.78,.76,.70),"Le":(.80,.79,.72),"Captain":(.025,.039,.072),"Purser":(.075,.065,.055),
        "PorterVN_A":(.25,.15,.085),"PorterVN_B":(.062,.106,.145),"PorterVN_C":(.35,.25,.15),
        "VendorVN_F":(.30,.17,.105),"CivilianVN":(.036,.045,.055),"PassengerFR_M1":(.70,.65,.52),
        "PassengerFR_M2":(.065,.073,.079),"PassengerFR_F":(.80,.77,.65),"SailorFR":(.78,.77,.69),
        "DockerMRS":(.09,.17,.245),"Waiter":(.022,.026,.032)}
    trouser_color=(.026,.032,.036) if role!="Le" and role!="CivilianVN" else (.77,.76,.68)
    if role=="Captain": trouser_color=(.025,.039,.072)
    if role=="PassengerFR_M1": trouser_color=colors[role]
    if role=="DockerMRS": trouser_color=(.17,.115,.065)
    cloth=cast.fabric(role,out,colors[role],name="Cloth")
    pants=cast.fabric(role,out,trouser_color,name="Trousers")
    shirt=cast.fabric(role,out,(.79,.775,.71),name="Shirt")
    skin_color=(.22,.105,.058) if role in VN else (.38,.20,.13)
    skinmat=plain(role+"_Skin",skin_color,.85)
    leather=plain(role+"_Leather",(.025,.018,.013),.85)
    hair=plain(role+"_BlackHair",(.013,.009,.006),.96)
    if role in VN:
        for material in body.data.materials:
            for node in material.node_tree.nodes:
                if node.type=="TEX_IMAGE" and node.image and "BaseColor" in node.image.name:
                    pixels=cast.image_pixels(node.image)
                    pixels[:,:,:3]*=np.array([.82,.76,.67])
                    node.image=cast.save_pixels(node.image.name.split('.')[0],pixels,out)
    slots=[]
    for mat in [cloth,pants,shirt,skinmat,leather]:
        slots.append(len(body.data.materials)); body.data.materials.append(mat)
    c,p,s,skin,shoe=slots
    body.data.update()
    original_body=next(i for i,m in enumerate(body.data.materials) if m.name.endswith("_Body"))
    if not female:body_atlas(body,role,out,colors[role],trouser_color)
    for polygon in body.data.polygons:
        center=polygon.center
        if polygon.material_index!=original_body: continue
        if not female:
            if center.z<.145:polygon.material_index=shoe
            continue
        if abs(center.x)>.735:
            polygon.material_index=skin
        elif center.z<.165:
            polygon.material_index=shoe
        elif center.z<.95:
            polygon.material_index=p
        else:
            polygon.material_index=c
            if role in {"Waiter","Purser","PassengerFR_M2"} and abs(center.x)>.225:
                polygon.material_index=s if role=="Waiter" else c
            if role in {"Captain","PassengerFR_M1","PassengerFR_M2"} and abs(center.x)<.085 and center.z>1.23 and center.y<-.07:
                polygon.material_index=s
    objects=[body]
    if role in PORTERS:
        crop_below(body,.387)
        # Narrow flesh/cloth around the fixed leg and arm joints; bone transforms stay identical.
        for vertex in body.data.vertices:
            weights={body.vertex_groups[g.group].name:g.weight for g in vertex.groups}
            if vertex.co.z<1.02:
                anchor=Vector((.105 if vertex.co.x>0 else -.105,0,vertex.co.z))
                vertex.co=anchor+(vertex.co-anchor)*Vector((.87,.9,1))
            elif vertex.co.z<1.47 and abs(vertex.co.x)<.24:
                vertex.co.x*=.88; vertex.co.y*=.91
        skinmat, skin_uv = hand_skin(body, role)
        objects+=barelegs(role,rig,skinmat,pants,skin_uv,body)
    if female:
        # The source apron belt/ties overlap the lower blouse; replace the whole waist.
        deleted=delete_faces(body,lambda f: .17<sum(v.co.z for v in f.verts)/len(f.verts)<1.145)
        assert deleted>200,(role,"chef costume crop")
        headmat=next(m for m in body.data.materials if m.name.endswith("_Head"))
        image=next(n.image for n in headmat.node_tree.nodes if n.type=="TEX_IMAGE" and "BaseColor" in n.image.name)
        pixels=cast.image_pixels(image);hh,ww,_=pixels.shape
        remove=set();layer=body.data.uv_layers.active.data
        for polygon in body.data.polygons:
            if not body.data.materials[polygon.material_index].name.endswith("_Head") or polygon.center.z<1.724:continue
            uvs=[layer[i].uv for i in polygon.loop_indices]
            u=sum(v.x for v in uvs)/len(uvs);v=sum(v.y for v in uvs)/len(uvs)
            color=pixels[min(hh-1,int(v*hh)),min(ww-1,int(u*ww)),:3]
            if float(color.mean())>.34 and float(color.max()-color.min())<.14:remove.add(polygon.index)
        delete_faces(body,lambda f:f.index in remove)
        crop_above(body,1.726)
        objects+=trousers(role,rig,pants)
        objects.append(rings(role+"_BlouseWaist",[(1.17,.175,.154,0),(1.11,.197,.169,0),(1.045,.215,.181,0),(1.01,.217,.181,0)],cloth,rig,torso_weight,closed=False))
        objects.append(short_hair(role,rig,hair))
    if role in {"Mai","CivilianVN","PorterVN_C"}:
        objects.append(short_hair(role,rig,hair))
    if role in {"Mai","Le","VendorVN_F"}:
        objects+=button_row(role,rig,plain(role+"_LinenButtons",(.55,.51,.42)),.015,[1.36,1.29,1.22,1.15,1.08],y=-.181,body=body)
        objects.append(rings(role+"_StandingCollar",[(1.474,.060,.054,.008),(1.514,.056,.049,.009)],shirt,rig,{"UpperChest":1},closed=False))
    if role=="CivilianVN":
        sections=[(1.055,.195,.16,0),(1.01,.196,.163,0),(.90,.213,.171,0),(.78,.229,.178,0),(.62,.24,.18,0),(.42,.25,.182,0)]
        tunic=rings(role+"_AoDai",sections,cloth,rig,skirt_weight,segments=48,closed=False)
        # Open small vertical side slits; frontal/back panels remain thigh-skinned.
        delete_faces(tunic,lambda f: sum(v.co.z for v in f.verts)/len(f.verts)<.78 and abs(sum(v.co.x for v in f.verts)/len(f.verts))>.212)
        objects.append(tunic)
        objects.append(rings(role+"_HighCollar",[(1.477,.06,.053,.008),(1.52,.056,.049,.008)],cloth,rig,{"UpperChest":1},closed=False))
        objects+=headwear(role,"khandong",rig,cloth,cloth)
    if role in {"PorterVN_A","VendorVN_F"}:
        straw=cast.fabric(role,out,(.63,.45,.23),name="Straw")
        objects+=headwear(role,"nonla",rig,straw,plain(role+"_BambooRibs",(.29,.19,.08)))
    if role=="PorterVN_B":
        wrap=cast.fabric(role,out,(.34,.31,.245),name="Headcloth")
        objects+=headwear(role,"headcloth",rig,wrap,wrap)
    if role=="VendorVN_F":
        objects+=scarf(role,rig,cast.fabric(role,out,(.49,.43,.30),name="Scarf"))
    if role=="Captain":
        objects.append(panel(role+"_DoubleBreastedFront",rig,cloth,1.35,.965,.172,-.205))
        brass=plain(role+"_Brass",(.45,.31,.09),.45,.55)
        for x in [-.083,.083]: objects+=button_row(role,rig,brass,x,[1.27,1.16,1.05],y=-.212)
        objects+=headwear(role,"kepi",rig,cloth,leather)
        objects+=beard(role,rig,plain(role+"_BeardHair",(.038,.023,.013)),body)
    if role=="Purser":
        # Back/side coat panels leave the original waistcoat visible in front.
        coat=rings(role+"_Coat",[(.95,.219,.184,0),(1.08,.22,.192,0),(1.26,.222,.197,0),(1.38,.195,.17,0)],cloth,rig,torso_weight,closed=False)
        delete_faces(coat,lambda f: abs(sum(v.co.x for v in f.verts)/len(f.verts))<.105 and sum(v.co.y for v in f.verts)/len(f.verts)<0)
        objects.append(coat)
        objects+=spectacles(role,rig)
        guard=cast.fabric(role,out,(.22,.215,.20),name="SleeveGuard")
        for label,sign in [("Left",1),("Right",-1)]:
            pts=[(sign*x,.0236,1.4594) for x in [.54,.59,.65,.715]]
            objects.append(tube(role+"_"+label+"SleeveGuard",pts,.047,guard,rig,label+"LowerArm",segments=24))
    if role=="PassengerFR_M1":
        objects+=headwear(role,"pith",rig,shirt,cloth)
    if role=="PassengerFR_M2":
        objects+=headwear(role,"fedora",rig,cast.fabric(role,out,(.20,.18,.155),name="Felt"),leather)
    if role=="PassengerFR_F":
        skirtmat=cast.fabric(role,out,(.20,.27,.25),name="Skirt")
        sections=[(1.045,.181,.145,0),(.98,.198,.16,0),(.88,.221,.174,0),(.72,.244,.187,0),(.52,.273,.207,0),(.30,.31,.227,0),(.12,.337,.243,0)]
        objects.append(rings(role+"_LongSkirt",sections,skirtmat,rig,skirt_weight,segments=64,closed=False))
        objects.append(rings(role+"_HighBlouseCollar",[(1.476,.060,.054,.008),(1.522,.055,.049,.008)],shirt,rig,{"UpperChest":1},closed=False))
        objects+=headwear(role,"widehat",rig,shirt,skirtmat)
        objects+=button_row(role,rig,plain(role+"_PearlButtons",(.63,.59,.49)),0,[1.37,1.30,1.23,1.16],y=-.175)
    if role=="SailorFR":
        size=1024
        pix=np.ones((size,size,4),dtype=np.float32)
        bands=(np.arange(size)//29)%2
        pix[:,:,:3]=np.where(bands[:,None,None]==0,np.array([.78,.77,.69]),np.array([.027,.065,.105]))
        stripes=cast.material(role+"_StripedJersey",cast.save_pixels(role+"_Stripes_BaseColor",pix,out))
        stripe_slot=len(body.data.materials);body.data.materials.append(stripes)
        uv=body.data.uv_layers.active.data
        for polygon in body.data.polygons:
            cu=sum(uv[i].uv.x for i in polygon.loop_indices)/len(polygon.loop_indices)
            cv=1-sum(uv[i].uv.y for i in polygon.loop_indices)/len(polygon.loop_indices)
            is_shirt=(.29<cu<.71 and cv<.90) or ((cu<.29 or cu>.71) and .435<cv<.845)
            if polygon.material_index==original_body and is_shirt and polygon.center.z>.90:
                polygon.material_index=stripe_slot
                for loop in polygon.loop_indices:
                    point=body.data.vertices[body.data.loops[loop].vertex_index].co
                    # Cloth sleeve bands continue along sleeve length when arms lower.
                    height=point.z if abs(point.x)<.215 else 1.47-(abs(point.x)-.215)
                    uv[loop].uv=(point.x, (height-.95)*1.0)
        objects+=headwear(role,"beret",rig,shirt,plain(role+"_NavyBand",(.021,.04,.075)))
    if role=="DockerMRS":
        # Remove source baseball crown/peak above forehead; replace by cloth flat cap.
        delete_faces(body,lambda f: min(v.co.z for v in f.verts)>1.739)
        objects+=headwear(role,"flatcap",rig,cloth,cloth)
        objects+=scarf(role,rig,cast.fabric(role,out,(.39,.26,.145),name="Scarf"))
        image=next(n.image for n in pants.node_tree.nodes if n.type=="TEX_IMAGE")
        pixels=cast.image_pixels(image)
        pixels[:,:,:3]*=(.84+.16*np.cos(np.arange(image.size[0])*math.tau/8))[None,:,None]
        for node in pants.node_tree.nodes:
            if node.type=="TEX_IMAGE":node.image=cast.save_pixels(role+"_Trousers_BaseColor",pixels,out)
    if role=="Waiter":
        objects.append(panel(role+"_LongWhiteApron",rig,shirt,1.055,.14,.235,-.228,skirt=True,rows=28,cols=16))
        path=[(.212*math.sin(a),-.177*math.cos(a),1.055) for a in np.linspace(0,math.tau,49)]
        objects.append(cast.ribbon(role+"_ApronWaistBand",path,.03,shirt,rig,"Hips",vertical=True))
        for sign in [-1,1]:
            objects.append(cast.ribbon(role+"_ApronTie"+str(sign),[(0,.18,1.055),(sign*.075,.198,1.01),(sign*.07,.2,.84)],.025,shirt,rig,"Hips"))
            bow=ellipsoid(role+"_Bow"+str(sign),(sign*.027,-.105,1.475),(.035,.012,.018),cloth,rig,"UpperChest",seg=16,rows=8)
            objects.append(bow)
    return objects


def join_meshes(objects, name):
    # FBX chooses the first UV channel. Joining meshes with unlike UV layer names
    # leaves zero UVs on the other mesh's layer, which turns atlas skins black.
    for obj in objects:
        active=obj.data.uv_layers.active
        if active:
            for layer in list(obj.data.uv_layers):
                if layer!=active:obj.data.uv_layers.remove(layer)
            active.name="UVMap"
    cast.select(objects)
    bpy.ops.object.join()
    obj=bpy.context.object;obj.name=name
    prune_weights(obj)
    return obj


def triangles(obj):
    return sum(len(p.vertices)-2 for p in obj.data.polygons)


def prepare_role(role):
    base=ROLES[role]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src=cast.source_file(base)
    bpy.ops.import_scene.fbx(filepath=str(src),use_anim=False)
    source=next(o for o in bpy.data.objects if o.type=="ARMATURE")
    body=next(o for o in bpy.data.objects if o.type=="MESH")
    temp,a_rest,_,src_world=cast.make_rig(source)
    rig=canonical_rig()
    bind={b.name:b.matrix_local.copy() for b in rig.data.bones}
    cast.remap_mesh(body,source,rig,a_rest,bind,src_world)
    for obj in list(bpy.data.objects):
        if obj not in [rig,body]:bpy.data.objects.remove(obj,do_unlink=True)
    rig.name="Armature"
    for action in list(bpy.data.actions):bpy.data.actions.remove(action)
    body.name=role+"_Body"
    out=CHARACTERS/role;out.mkdir(exist_ok=True)
    cast.materials(body,role,src,out)
    objects=costume(role,body,rig,out,base=="Chef_Female_01")
    body=join_meshes(objects,role+"_LOD0_Mesh")
    cast.select([body])
    triangulate=body.modifiers.new("Explicit FBX triangles","TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    body.data.validate(clean_customdata=False)
    scene=bpy.context.scene
    scene.unit_settings.system="METRIC";scene.unit_settings.scale_length=1;scene.render.fps=30
    rig.data.pose_position="REST"
    cast.export_fbx(out/(role+".fbx"),[rig,body])
    bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH/(role+".blend")),check_existing=False)
    textures=sorted(p.name for p in out.glob('*.png'))
    return {"base":base,"source":str(src.relative_to(cast.SOURCES)),"triangles":triangles(body),"bones":52,
        "mesh_objects":[body.name],"textures":textures,"texture_count":len(textures),"notes":NOTES[role],
        "asian_base_available":False if role in VN else None,"lod_triangles":{"LOD0":triangles(body)}}


def lod_role(role, entry):
    out=CHARACTERS/role
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(out/(role+".fbx")),use_anim=False)
    rig=next(o for o in bpy.data.objects if o.type=="ARMATURE")
    body=join_meshes([o for o in bpy.data.objects if o.type=="MESH"],role+"_LOD0_WorkMesh")
    base_triangles=triangles(body)
    assert base_triangles==entry["triangles"],(role,base_triangles,entry["triangles"])
    counts={"LOD0":base_triangles}
    for lod,ratio in [(1,.50),(2,.175)]:
        mesh=body.copy();mesh.data=body.data.copy();mesh.name=role+f"_LOD{lod}_Mesh"
        bpy.context.collection.objects.link(mesh)
        cast.select([mesh])
        triangulate=mesh.modifiers.new("Triangulate before collapse","TRIANGULATE")
        bpy.ops.object.modifier_apply(modifier=triangulate.name)
        collapse=mesh.modifiers.new("Crowd reduction","DECIMATE");collapse.ratio=ratio
        collapse.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=collapse.name)
        # Collapse can produce duplicate triangles on thin double-sided cloth.
        # Remove them before counting/exporting, just as FBX import would do.
        mesh.data.validate(clean_customdata=False)
        prune_weights(mesh)
        count=triangles(mesh);counts[f"LOD{lod}"]=count
        assert abs(count/base_triangles-ratio)<.025,(role,lod,count,base_triangles)
        cast.export_fbx(out/(role+f"_LOD{lod}.fbx"),[rig,mesh])
        body.hide_render=True;body.hide_set(True)
        other=[o for o in bpy.data.objects if o.type=="MESH" and o!=mesh]
        for obj in other:obj.hide_render=True
        bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH/"lods"/(role+f"_LOD{lod}.blend")),check_existing=False)
        bpy.data.objects.remove(mesh,do_unlink=True)
    entry["lod_triangles"]=counts
    entry["lod_ratios"]={k:round(v/base_triangles,5) for k,v in counts.items()}
    entry["lod_skeleton_bones"]=52
    if "textures" not in entry:
        entry["textures"]=sorted(p.name for p in out.glob('*.png'));entry["texture_count"]=len(entry["textures"])
    print("CROWD_LODS",role,counts,flush=True)


def load_role(path):
    with bpy.data.libraries.load(str(path),link=False) as (src,dst):
        dst.objects=list(src.objects)
    objects=[o for o in dst.objects if o]
    for obj in objects:bpy.context.collection.objects.link(obj)
    rig=next(o for o in objects if o.type=="ARMATURE")
    return rig,[o for o in objects if o.type=="MESH"]


def standing(rig):
    rig.data.pose_position="POSE"
    for label,sign in [("Left",1),("Right",-1)]:
        bone=rig.pose.bones[label+"UpperArm"]
        bone.rotation_mode="QUATERNION"
        rest=bone.bone.matrix_local.to_quaternion()
        delta=(rest @ Vector((0,1,0))).rotation_difference(Vector((sign*.13,0,-1)))
        bone.rotation_quaternion=rest.conjugated() @ delta @ rest
    bpy.context.view_layer.update()


def previews(pass_number):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    roles=list(ROLES)
    for i,role in enumerate(roles):
        rig,meshes=load_role(SCRATCH/(role+".blend"))
        standing(rig)
        rig.location.x=(i-(len(roles)-1)/2)*.79
        cast.caption(role,rig.location.x,-.16,.075)
    cast.caption("Hai Ban Tay | 1911 crowd | 15 new roles | Shared 52-bone bind pose",0,2.17,.105)
    path=SCRATCH/f"crowd_preview_pass{pass_number}.png"
    cast.studio(path,(0,0,1.01),12.3,2.8,(6600,1500),samples=20)
    shutil.copyfile(path,CHARACTERS/"crowd_preview.png")
    # Three larger panels preserve inspectable face, garment and foot detail.
    for group in range(3):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        for j,role in enumerate(roles[group*5:group*5+5]):
            rig,meshes=load_role(SCRATCH/(role+".blend"));standing(rig)
            rig.location.x=(j-2)*.86;cast.caption(role,rig.location.x,-.16,.075)
        path=SCRATCH/f"crowd_detail{group+1}_pass{pass_number}.png"
        cast.studio(path,(0,0,1.00),4.6,2.7,(2300,1350),samples=20)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for row,role in enumerate(["Cook","PorterVN_A","PassengerFR_F"]):
        for lod in [0,1,2]:
            path=CHARACTERS/role/(role+(f"_LOD{lod}" if lod else "")+".fbx")
            before=set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
            objects=set(bpy.data.objects)-before
            rig=next(o for o in objects if o.type=="ARMATURE")
            rig.location.x=(lod-1)*1.22;rig.location.z=(2-row)*2.35
            standing(rig)
            count=json.loads((CHARACTERS/"cast_stats.json").read_text())[role]["lod_triangles"][f"LOD{lod}"]
            cast.caption(f"{role} | LOD{lod} | {count:,} tris",rig.location.x,rig.location.z-.19,.076)
    cast.caption("Same skeleton | 100% / 50% / 17.5% triangles",0,6.99,.10)
    path=SCRATCH/f"lod_preview_pass{pass_number}.png"
    cast.studio(path,(0,0,3.31),7.8,7.55,(1665,2700),samples=24)
    shutil.copyfile(path,CHARACTERS/"lod_preview.png")


def repair_mai_buttons(role="Mai"):
    """Xuất lại ba FBX sinh hiện có; không ghi vào Staging hoặc .meta/GUID. Cúc dán lên mặt áo, weights theo tam giác áo bên dưới
    (04/11: Lê, VendorVN_F cũng có hàng cúc lơ lửng khi xoay thân)."""
    import hashlib
    root = Path(__file__).resolve().parents[2]
    folder = root / "Game/Assets/_Project/Art/Characters" / role
    report = []
    for path in [folder / (role + suffix + ".fbx") for suffix in ["", "_LOD1", "_LOD2"]]:
        meta = path.with_suffix(".fbx.meta")
        guid_hash = hashlib.sha256(meta.read_bytes()).hexdigest()
        before_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
        rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
        body = next(o for o in bpy.data.objects if o.type == "MESH")
        bind = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        assert len(bind) == 52, (path, len(bind))
        slots = {i for i,m in enumerate(body.data.materials) if "LinenButtons" in m.name}
        assert slots, "Thiếu vật liệu cúc " + role
        selected = {i for p in body.data.polygons if p.material_index in slots for i in p.vertices}
        adjacency = {i: set() for i in selected}
        for edge in body.data.edges:
            a, b = edge.vertices
            if a in selected and b in selected: adjacency[a].add(b); adjacency[b].add(a)
        islands = []
        while selected:
            pending = [selected.pop()]; island = set(pending)
            while pending:
                for other in adjacency[pending.pop()]:
                    if other in selected: selected.remove(other); island.add(other); pending.append(other)
            islands.append(island)
        assert len(islands) == 5, (path, "Số cúc", len(islands))
        surface = shirt_surface(body, slots)
        inverse = body.matrix_world.inverted()
        for island in islands:
            center = sum((body.matrix_world @ body.data.vertices[i].co for i in island), Vector()) / len(island)
            contact, weights = button_contact(body, surface, center)
            delta = contact - center
            for i in island:
                vertex = body.data.vertices[i]
                vertex.co = inverse @ (body.matrix_world @ vertex.co + delta)
                for group in list(vertex.groups): body.vertex_groups[group.group].remove([i])
                for name, weight in weights.items(): body.vertex_groups[name].add([i], weight, "REPLACE")
                assert abs(sum(g.weight for g in vertex.groups) - 1) < 1e-5
        assert all(max(abs(a-b) for row_a,row_b in zip(rig.data.bones[name].matrix_local, matrix)
                       for a,b in zip(row_a,row_b)) < 1e-6 for name,matrix in bind.items())
        cast.export_fbx(path, [rig, body])
        assert hashlib.sha256(meta.read_bytes()).hexdigest() == guid_hash
        report.append({"path": str(path.relative_to(root)), "buttons": len(islands), "bones": len(bind),
                       "guid_unchanged": True, "before_sha256": before_hash,
                       "after_sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    (root / ("Logs/buttons_blender_%s.json" % role)).write_text(json.dumps(report, ensure_ascii=False, indent=2))


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--pass",dest="pass_number",type=int,default=1)
    parser.add_argument("--roles",nargs="*")
    parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--lods-only",action="store_true")
    parser.add_argument("--repair-mai-buttons",action="store_true")
    parser.add_argument("--repair-buttons",nargs="*")
    parser.add_argument("--feet12",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.repair_mai_buttons:
        repair_mai_buttons(); return
    if args.repair_buttons:
        for role in args.repair_buttons: repair_mai_buttons(role)
        return
    if args.feet12:
        import feet12_pipeline
        feet12_pipeline.prepare()
        return
    SCRATCH.mkdir(parents=True,exist_ok=True);(SCRATCH/"lods").mkdir(exist_ok=True)
    bpy.context.preferences.filepaths.save_version=0
    if args.preview_only:
        previews(args.pass_number);return
    stats=json.loads((CHARACTERS/"cast_stats.json").read_text())
    roles=args.roles or list(ROLES)
    if not args.lods_only:
        for role in roles:
            assert role in ROLES,role
            print("PREPARE_CROWD",role,ROLES[role],flush=True)
            stats[role]=prepare_role(role)
            (CHARACTERS/"cast_stats.json").write_text(json.dumps(stats,indent=2))
    lod_roles=roles if args.roles else list(stats)
    for role in lod_roles:
        lod_role(role,stats[role])
        (CHARACTERS/"cast_stats.json").write_text(json.dumps(stats,indent=2))
    if not args.no_preview:previews(args.pass_number)
    print("CROWD_COMPLETE",len(stats),flush=True)


if __name__=="__main__":
    main()
