"""Nhận diện riêng faces13; chỉ ghi generated và Logs, giữ nguồn feet12 bất biến."""
import hashlib
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
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import prepare_cast as cast
import prepare_crowd as crowd

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Tools/blender/generated/Characters"
SCRATCH = ROOT / "Logs/faces13_blender"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def surface(body, suffix):
    body.data.calc_loop_triangles()
    points = [v.co.copy() for v in body.data.vertices]
    faces = [tuple(p.vertices) for p in body.data.loop_triangles
             if body.data.materials[p.material_index].name.endswith(suffix)]
    assert faces, (body.name, suffix)
    return BVHTree.FromPolygons(points, faces, all_triangles=True)


def lower_fingerprint(body):
    return sorted((body.data.materials[p.material_index].name,
                   tuple(sorted((tuple(round(c, 6) for c in body.data.vertices[i].co),
                                 tuple(sorted((body.vertex_groups[g.group].name, round(g.weight, 6))
                                              for g in body.data.vertices[i].groups))) for i in p.vertices)))
                  for p in body.data.polygons if max(body.data.vertices[i].co.z for i in p.vertices) < .50)


def load(source_role, role):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source = SCRATCH / "Captain_source" if role == "Captain" else OUTPUT / source_role
    out = OUTPUT / role; out.mkdir(exist_ok=True)
    bpy.ops.import_scene.fbx(filepath=str(source / (source_role + ".fbx")), use_anim=False)
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rig.data.pose_position = "REST"
    body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    cast.select([body]); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for mat in body.data.materials:
        mat.name = mat.name.replace(source_role + "_", role + "_", 1)
    for image in bpy.data.images:
        if image.source != "FILE": continue
        filename = Path(image.filepath.replace(chr(92), "/")).name
        original = source / filename
        assert original.is_file(), original
        target = out / filename.replace(source_role + "_", role + "_", 1)
        if original != target: shutil.copy2(original, target)
        image.filepath = str(target)
    return rig, body, out


def head_atlas(body, role, out, youthful=False):
    mat = next(m for m in body.data.materials if m.name.endswith("_Head"))
    node = next(n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and "BaseColor" in n.image.name)
    pixels = cast.image_pixels(node.image)
    h, w, _ = pixels.shape
    yy, xx = np.mgrid[0:h, 0:w]; x = xx / w; y = 1 - yy / h
    rgb = pixels[:, :, :3]
    # Xóa tóc dài trong atlas ngoài vùng mắt/mày/môi; tóc mới có mesh ôm sọ riêng.
    scalp = (y < .53) & ((x < .34) | (x > .66) | (y < .12))
    ears = (y > .30) & (y < .46) & (((x > .20) & (x < .30)) | ((x > .70) & (x < .80)))
    scalp &= ~ears
    skin = np.median(rgb[(x > .40) & (x < .46) & (y > .31) & (y < .39)], axis=0)
    rgb[scalp] = skin * (.98 + .015 * np.sin(xx[scalp, None] * .73 + yy[scalp, None] * .91))
    if youthful:
        chin = (x > .36) & (x < .64) & (y > .39) & (y < .58)
        t = np.clip((y - .39) / .025, 0, 1) * np.clip((.58 - y) / .025, 0, 1)
        rgb[chin] = rgb[chin] * (1 - .55 * t[chin, None]) + skin * (.55 * t[chin, None])
    hair_patch = (x > .96) & (y > .94)
    grain = .82 + .14 * np.sin(xx * 1.7 + yy * .31) + .04 * np.sin(yy * 2.1)
    rgb[hair_patch] = (grain[:, :, None] * np.array([.022, .017, .012]))[hair_patch]
    node.image = cast.save_pixels(role + "_Head_BaseColor", pixels, out)
    for normal in mat.node_tree.nodes:
        if normal.type == "TEX_IMAGE" and "Normal" in normal.image.name:
            values = cast.image_pixels(normal.image)
            assert values.shape == pixels.shape
            values[scalp, :3] = (.5, .5, 1)
            normal.image = cast.save_pixels(role + "_Head_Normal", values, out, True)
    return mat


def sculpt_head(body, role):
    ids = {i for p in body.data.polygons if body.data.materials[p.material_index].name.endswith("_Head") for i in p.vertices}
    for index in ids:
        p = body.data.vertices[index].co
        neck_blend = min(1, max(0, (p.z - 1.52) / .055))
        if role == "Ba":
            jaw = math.exp(-((p.z - 1.615) / .065) ** 2)
            p.x *= 1 - neck_blend * (.025 + .095 * jaw)
            nose = math.exp(-(p.x / .026) ** 2 - ((p.z - 1.687) / .026) ** 2)
            if p.y < -.095: p.y += .006 * nose
            p.z += .003 * neck_blend * math.exp(-((p.z - 1.60) / .04) ** 2)
        elif role == "Chieu":
            p.x *= 1 + .15 * neck_blend * math.exp(-((p.z - 1.635) / .080) ** 2)
            p.y -= .004 * neck_blend * math.exp(-((p.z - 1.63) / .05) ** 2)
        else:
            p.x *= 1 + .07 * neck_blend * math.exp(-((p.z - 1.67) / .07) ** 2)
            if p.y < -.095: p.y += .009 * math.exp(-(p.x / .028) ** 2 - ((p.z - 1.686) / .029) ** 2)
    body.data.update()


def hair(body, rig, role, mat):
    tree = surface(body, "_Head")
    center = Vector((0, .006, 1.714))
    verts, faces = [], []
    segments, rows = 64, 16
    for j in range(rows + 1):
        t = (j + .08) / (rows + .08)
        for i in range(segments + 1):
            a = math.tau * i / segments
            # Front cao, gáy cắt sát; tóc Chiêu chải lệch với ngôi nông.
            limit = 1.31 + .85 * (1 - math.cos(a)) / 2 + .34 * abs(math.sin(a))
            if role == "Chieu": limit -= .08
            theta = t * limit
            direction = Vector((math.sin(theta) * math.sin(a), -math.sin(theta) * math.cos(a), math.cos(theta)))
            hit, normal, _, _ = tree.ray_cast(center, direction, .30)
            assert hit is not None, (role, "Thiếu bề mặt sọ", j, i)
            volume = .0025 + (.007 if role == "Chieu" else .003) * (1 - t) ** 2
            if role == "Chieu": volume += .005 * max(0, math.sin(a)) * (1 - t)
            verts.append(hit + normal * volume)
    for j in range(rows):
        for i in range(segments):
            k = j * (segments + 1) + i
            faces.append((k, k + segments + 1, k + segments + 2, k + 1))
    faces.append(tuple(range(segments)))
    uv = [(.962 + (i % (segments + 1)) / segments * .034, .006 + (i // (segments + 1)) / rows * .045) for i in range(len(verts))]
    return cast.mesh(role + "_CroppedHeadHair", verts, faces, uv, mat, rig,
                     [{"Head": 1}] * len(verts))


def moustache(body, rig, role, mat, light=False):
    tree = surface(body, "_Head"); verts, faces = [], []
    for j in range(7):
        t = j / 6
        for i in range(33):
            x = -.033 + .066 * i / 32
            arch = .002 * math.sin(math.pi * i / 32)
            taper = max(.05, 1 - (x / .033) ** 2) ** .5
            z = 1.630 + arch + (.0045 if light else .006) * (t - .5) * taper
            hit, normal, _, _ = tree.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)), 2)
            assert hit is not None
            fade = math.sin(math.pi * t) * math.sin(math.pi * i / 32)
            verts.append(hit + normal * (.0004 + (.002 if light else .004) * fade))
    for j in range(6):
        for i in range(32):
            k = j * 33 + i; faces.append((k, k + 1, k + 34, k + 33))
    return cast.mesh(role + "_FittedMoustache", verts, faces, [(.98, .03)] * len(verts), mat, rig,
                     [{"Head": 1}] * len(verts))


def fitted_beard(body, rig):
    # Bề mặt/UV/weights lấy trực tiếp từ mặt; độ dày giảm về 0 ở viền râu.
    head = next(m for m in body.data.materials if m.name.endswith("_Head"))
    node = next(n for n in head.node_tree.nodes if n.type == "TEX_IMAGE" and "BaseColor" in n.image.name)
    pixels = cast.image_pixels(node.image); h, w, _ = pixels.shape
    layer = body.data.uv_layers.active.data
    body.data.calc_loop_triangles()
    body.data.update()
    def density(x, y, z):
        edge = np.abs(x) / .074
        low = 1.560 + .045 * edge ** 1.6
        high = 1.609 + .065 * edge ** 1.4
        return np.clip(np.minimum.reduce([(z-low)/.006, (high-z)/.006,
                                           (1-edge)/.08, (-.025-y)/.012]), 0, 1)
    verts, faces, uvs, weights = [], [], [], []
    for face in body.data.loop_triangles:
        if body.data.materials[face.material_index] != head: continue
        points = np.array([tuple(body.data.vertices[i].co) for i in face.vertices])
        center = points.mean(axis=0)
        if not (1.54 < center[2] < 1.70 and center[1] < -.02 and abs(center[0]) < .09): continue
        uv = np.array([tuple(layer[i].uv) for i in face.loops]) * (w-1,h-1)
        lower = np.maximum(0,np.floor(uv.min(axis=0)).astype(int)-1)
        upper = np.minimum((w-1,h-1),np.ceil(uv.max(axis=0)).astype(int)+1)
        yy, xx = np.mgrid[lower[1]:upper[1]+1,lower[0]:upper[0]+1]
        a,b,c = uv; determinant = np.cross(b-a,c-a)
        if abs(determinant) < 1e-5: continue
        u = ((xx-a[0])*(c[1]-a[1])-(yy-a[1])*(c[0]-a[0]))/determinant
        v = ((b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0]))/determinant
        inside = (u >= -.005) & (v >= -.005) & (u+v <= 1.005)
        world = points[0] + u[:,:,None]*(points[1]-points[0]) + v[:,:,None]*(points[2]-points[0])
        opacity = density(world[:,:,0],world[:,:,1],world[:,:,2])*inside
        if opacity.max() <= 0: continue
        strand = .82 + .15*np.sin(world[:,:,0]*4500+world[:,:,2]*83) + .03*np.sin(xx*1.7+yy*.4)
        patch = pixels[lower[1]:upper[1]+1,lower[0]:upper[0]+1,:3]
        patch[:] = patch*(1-opacity[:,:,None]) + strand[:,:,None]*np.array([.075,.052,.033])*opacity[:,:,None]
        first = len(verts)
        for index, loop in zip(face.vertices,face.loops):
            vertex = body.data.vertices[index]
            amount = float(density(*vertex.co))
            verts.append(vertex.co + vertex.normal*(.00015+.005*amount))
            uvs.append(tuple(layer[loop].uv))
            weights.append({body.vertex_groups[g.group].name:g.weight for g in vertex.groups})
        faces.append(tuple(range(first,first+3)))
    assert faces, "Thiếu bề mặt râu trên mặt"
    node.image = cast.save_pixels("Captain_Head_BaseColor",pixels,OUTPUT/"Captain")
    beard = cast.mesh("Captain_VolumeBeard",verts,faces,uvs,head,rig,weights)
    # Các tam giác dùng UV riêng nhưng phải chung đỉnh hình học để normal mềm liên tục.
    data = bmesh.new(); data.from_mesh(beard.data)
    bmesh.ops.remove_doubles(data, verts=list(data.verts), dist=.00001)
    bmesh.ops.recalc_face_normals(data, faces=list(data.faces))
    for face in data.faces: face.smooth = True
    data.to_mesh(beard.data); data.free(); beard.data.update()
    return beard


def child_point(p):
    head = min(1, max(0, (p.z - 1.50) / .055))
    return Vector((p.x * (.66 + .23 * head), p.y * (.66 + .23 * head),
                   p.z * .64 + max(0, p.z - 1.52) * .25))


def child_rig(body, rig):
    for v in body.data.vertices: v.co = child_point(v.co)
    # Đổi bind xương và skin cùng phép biến dạng, không dùng scale root người lớn.
    cast.select([rig]); bpy.ops.object.mode_set(mode="EDIT")
    original = {b.name:(b.matrix.copy(),b.head.copy(),b.tail.copy()) for b in rig.data.edit_bones}
    for bone in rig.data.edit_bones: bone.use_connect = False
    for bone in rig.data.edit_bones:
        matrix, old_head, old_tail = original[bone.name]
        old = matrix.copy(); head = child_point(old_head); tail = child_point(old_tail)
        old.translation = head; bone.matrix = old; bone.length = (tail - head).length
    bpy.ops.object.mode_set(mode="OBJECT")
    assert all((b.head_local-child_point(original[b.name][1])).length < 1e-5 for b in rig.data.bones), "Bind trẻ bị đổi hai lần"
    body.data.update()


def weld_head(body):
    data = bmesh.new(); data.from_mesh(body.data)
    faces = [f for f in data.faces if body.data.materials[f.material_index].name.endswith("_Head")]
    bmesh.ops.remove_doubles(data, verts=list({v for f in faces for v in f.verts}), dist=.00001)
    data.to_mesh(body.data); data.free()
    for p in body.data.polygons:
        if body.data.materials[p.material_index].name.endswith("_Head"): p.use_smooth = True
    if body.data.has_custom_normals:
        cast.select([body]); bpy.ops.mesh.customdata_custom_splitnormals_clear()
    body.data.update()


def paint_coat_front(body, out):
    mat = next(m for m in body.data.materials if m.name.endswith("_Body"))
    node = next(n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and "BaseColor" in n.image.name)
    pix = cast.image_pixels(node.image); h, w, _ = pix.shape
    layer = body.data.uv_layers.active.data
    body.data.calc_loop_triangles()
    for face in body.data.loop_triangles:
        if body.data.materials[face.material_index] != mat: continue
        center = sum((body.data.vertices[i].co for i in face.vertices), Vector()) / 3
        if not (.95 < center.z < 1.365 and center.y < -.03 and abs(center.x) < .23): continue
        uv = np.array([tuple(layer[i].uv) for i in face.loops]) * (w - 1, h - 1)
        low = np.maximum(0, np.floor(uv.min(axis=0)).astype(int) - 2)
        high = np.minimum((w - 1, h - 1), np.ceil(uv.max(axis=0)).astype(int) + 2)
        yy, xx = np.mgrid[low[1]:high[1]+1, low[0]:high[0]+1]
        a, b, c = uv; den = np.cross(b - a, c - a)
        if abs(den) < 1e-5: continue
        u = ((xx-a[0])*(c[1]-a[1])-(yy-a[1])*(c[0]-a[0])) / den
        v = ((b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0])) / den
        mask = (u > -.03) & (v > -.03) & (u+v < 1.03)
        patch = pix[low[1]:high[1]+1, low[0]:high[0]+1, :3]
        shade = np.clip(patch.mean(axis=2) * .9 + .8, .8, 1.2)
        patch[mask] = (shade[:, :, None] * np.array([.026, .040, .071]))[mask]
    node.image = cast.save_pixels("Captain_Body_BaseColor", pix, out)


def build(role, source_role):
    rig, body, out = load(source_role, role)
    baseline = lower_fingerprint(body)
    bind = {b.name: [list(row) for row in b.matrix_local] for b in rig.data.bones}
    source_hash = digest((SCRATCH / "Captain_source" if role == "Captain" else OUTPUT / source_role) / (source_role + ".fbx"))
    if role == "Captain":
        crowd.delete_faces(body, lambda f: body.data.materials[f.material_index].name.endswith(("_Cloth", "_Brass", "_CapBrass", "_BeardHair", "_Hair")) or
                           (body.data.materials[f.material_index].name.endswith("_Leather") and min(v.co.z for v in f.verts) > 1.70))
        weld_head(body)
        head_mat = head_atlas(body, role, out)
        paint_coat_front(body, out)
        navy = crowd.plain("Captain_KepiNavy", (.003, .005, .009))
        leather = next(m for m in body.data.materials if m.name.endswith("_Leather"))
        ids = {i for p in body.data.polygons if body.data.materials[p.material_index].name.endswith("_Head") for i in p.vertices}
        skull = [body.data.vertices[i].co for i in ids if body.data.vertices[i].co.z > 1.737]
        cy = (min(v.y for v in skull) + max(v.y for v in skull)) / 2
        rx = max(abs(v.x) for v in skull) + .006
        ry = max(abs(v.y - cy) for v in skull) + .006
        fit = max(1, max(math.hypot(v.x / rx, (v.y - cy) / ry) for v in skull)) + .025
        rx *= fit; ry *= fit
        assert all(math.hypot(v.x / rx, (v.y - cy) / ry) < 1 for v in skull), "Mũ phải bao toàn bộ sọ"
        hat = crowd.rings("Captain_FittedKepi", [(1.738,rx,ry,cy),(1.758,rx+.001,ry+.001,cy),
                          (1.806,rx+.001,ry+.001,cy),(1.824,rx*.9,ry*.9,cy),(1.827,.001,.001,cy)], navy, rig, {"Head":1}, segments=48)
        band = crowd.rings("Captain_FittedCapBand", [(1.740,rx+.001,ry+.001,cy),(1.760,rx+.002,ry+.002,cy)], leather, rig, {"Head":1}, closed=False)
        verts = [(-.087,-.07,1.743),(.087,-.07,1.743),(.081,-.136,1.731),(.044,-.169,1.727),
                 (0,-.176,1.726),(-.044,-.169,1.727),(-.081,-.136,1.731)]
        visor = cast.mesh("Captain_FittedVisor", verts, [tuple(range(7))], [(.5,.5)]*7, leather, rig, [{"Head":1}]*7, .004)
        brass = crowd.plain("Captain_FittedButtons", (.45,.31,.09), .45,.55)
        cropped = hair(body, rig, role, head_mat)
        # Mép cắt phẳng nằm trong band mũ, giữ UV/weights nội suy.
        data = bmesh.new(); data.from_mesh(cropped.data)
        bmesh.ops.bisect_plane(data, geom=list(data.verts)+list(data.edges)+list(data.faces),
                              plane_co=(0,0,1.745), plane_no=(0,0,1), dist=.000001, clear_outer=True)
        data.to_mesh(cropped.data); data.free()
        objects = [body, hat, band, visor, fitted_beard(body, rig), cropped]
        beard_mat = crowd.plain("Captain_MoustacheHair", (.048,.034,.026), .94)
        objects.append(moustache(body, rig, role, beard_mat))
        for x in [-.074,.074]: objects += crowd.button_row(role, rig, brass, x, [1.28,1.17,1.06], body=body)
        # Áo trước dùng topology Body nguồn; cúc lấy tiếp xúc và weights từng tam giác áo.
        assert lower_fingerprint(body) == baseline, "Captain: đã chạm chân feet12"
    else:
        crowd.delete_faces(body, lambda f: body.data.materials[f.material_index].name.endswith(("_Hair", "_Felt", "_LinenButtons")) or
                           (body.data.materials[f.material_index].name.endswith("_Leather") and min(v.co.z for v in f.verts) > 1.70))
        sculpt_head(body, role)
        weld_head(body)
        mat = head_atlas(body, role, out, youthful=role != "Chieu")
        objects = [body, hair(body, rig, role, mat)]
        if role == "Chieu":
            for v in body.data.vertices:
                t = max(0, 1 - abs(v.co.z - 1.16) / .36)
                if abs(v.co.x) < .28: v.co.x *= 1 + .12*t; v.co.y *= 1 + .19*t
            objects.append(moustache(body, rig, role, mat, light=True))
        elif role == "ChieuChild":
            crowd.body_atlas(body, role, out, (.69,.68,.62), (.035,.055,.085))
            buttons = crowd.plain(role + "_SmallButtons", (.12,.105,.075))
            objects += crowd.button_row(role, rig, buttons, 0, [1.35,1.24,1.13], body=body)
        assert lower_fingerprint(body) == baseline, (role, "Đã chạm chân feet12 trước đổi tỉ lệ trẻ")
    body = crowd.join_meshes(objects, role + "_LOD0_Mesh")
    if role == "ChieuChild": child_rig(body, rig)
    else:
        assert {b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones} == bind, (role, "Bind bị đổi")
    cast.select([body]); tri = body.modifiers.new("Faces13 triangles", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=tri.name)
    body.data.validate(clean_customdata=False); crowd.prune_weights(body)
    assert len(rig.data.bones) == 52
    assert all(abs(sum(g.weight for g in v.groups)-1) < 1e-5 for v in body.data.vertices)
    bpy.context.scene.unit_settings.system = "METRIC"; bpy.context.scene.unit_settings.scale_length = 1
    cast.export_fbx(out / (role + ".fbx"), [rig, body])
    bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH / (role + ".blend")), check_existing=False)
    stats = {"triangles":crowd.triangles(body), "bones":52, "source_role":source_role, "source_sha256":source_hash,
             "lower_body_preserved_before_child_proportion":True, "canonical_bind_unchanged":role != "ChieuChild"}
    crowd.lod_role(role, stats)
    return stats


def main():
    SCRATCH.mkdir(exist_ok=True, parents=True); (SCRATCH / "lods").mkdir(exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    crowd.CHARACTERS = OUTPUT; crowd.SCRATCH = SCRATCH
    # Giữ nguồn feet12 Captain để chạy lại không tích lũy sửa hình.
    original = SCRATCH / "Captain_source"
    if not original.exists():
        original.mkdir()
        for path in (OUTPUT / "Captain").iterdir():
            if path.is_file(): shutil.copy2(path, original / path.name)
    unchanged = {str(p.relative_to(ROOT)):digest(p) for role in ["Le","Mai"] for p in (OUTPUT/role).iterdir() if p.is_file()}
    report = {"passed":False, "roles":{}}
    (SCRATCH / "verification.json").write_text(json.dumps(report))
    for role, source in [("Ba","Le"),("Chieu","PassengerFR_M2"),("ChieuChild","Le"),("Captain","Captain")]:
        report["roles"][role] = build(role, source)
    assert all(digest(ROOT/name) == value for name,value in unchanged.items()), "Le/Mai bị thay đổi"
    report.update(passed=True, preserved_sha256=unchanged)
    (SCRATCH / "verification.json").write_text(json.dumps(report, indent=2, ensure_ascii=False))
    print("FACES13_PREPARED", json.dumps(report["roles"], ensure_ascii=False), flush=True)


if __name__ == "__main__": main()
