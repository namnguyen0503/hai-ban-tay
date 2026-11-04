"""Prepare the staged Rocketbox cast. Run with Blender 4.2 in background mode."""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / "Staging/characters"
SCRATCH = SOURCES / "_scratch/cast"
CHARACTERS = ROOT / "Staging/art_hq_fbx/Characters"
ANIMS = ROOT / "Staging/art_hq_fbx/CharacterAnims"
CAST = {
    "Cook": "Business_Male_04",
    "Helper": "Male_Adult_08",
    "SoldierA": "Business_Male_01",
    "SoldierB": "Male_Adult_02",
    "Jacques": "Male_Adult_14",
}

# Order is parent-first, and identical in every model and animation FBX.
MAP = {"Bip01 Pelvis": "Hips", "Bip01 Spine": "Spine", "Bip01 Spine1": "Chest",
       "Bip01 Spine2": "UpperChest", "Bip01 Neck": "Neck", "Bip01 Head": "Head"}
PARENTS = {"Hips": None, "Spine": "Hips", "Chest": "Spine", "UpperChest": "Chest",
           "Neck": "UpperChest", "Head": "Neck"}
for side, label in [("L", "Left"), ("R", "Right")]:
    for old, new, parent in [("Clavicle", "Shoulder", "UpperChest"),
                             ("UpperArm", "UpperArm", label + "Shoulder"),
                             ("Forearm", "LowerArm", label + "UpperArm"),
                             ("Hand", "Hand", label + "LowerArm"),
                             ("Thigh", "UpperLeg", "Hips"),
                             ("Calf", "LowerLeg", label + "UpperLeg"),
                             ("Foot", "Foot", label + "LowerLeg"),
                             ("Toe0", "Toes", label + "Foot")]:
        MAP[f"Bip01 {side} {old}"] = label + new
        PARENTS[label + new] = parent
    for digit, finger in enumerate(["Thumb", "Index", "Middle", "Ring", "Little"]):
        for j, part in enumerate(["Proximal", "Intermediate", "Distal"]):
            old = f"Bip01 {side} Finger{digit}" + (str(j) if j else "")
            name = label + finger + part
            MAP[old] = name
            PARENTS[name] = label + "Hand" if j == 0 else label + finger + ["Proximal", "Intermediate"][j - 1]


def select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def export_fbx(path, objects, animation=False):
    select(objects)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={"MESH", "ARMATURE"}, path_mode="RELATIVE", embed_textures=False,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", add_leaf_bones=False, use_armature_deform_only=True,
        bake_anim=animation, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1, bake_anim_simplify_factor=0,
        use_mesh_modifiers=True)


def make_rig(source):
    """Anatomical axes; reparent without changing bind-space mesh coordinates."""
    src_world = {n: source.matrix_world @ source.data.bones[n].matrix_local for n in MAP}
    heads = {MAP[n]: src_world[n].translation for n in MAP}
    rig = bpy.data.objects.new("Armature", bpy.data.armatures.new("HaiBanTay_Humanoid"))
    bpy.context.collection.objects.link(rig)
    select([rig])
    bpy.ops.object.mode_set(mode="EDIT")
    for name, parent in PARENTS.items():
        bone = rig.data.edit_bones.new(name)
        bone.head = heads[name]
        children = [n for n, p in PARENTS.items() if p == name]
        # Use the anatomical continuation, not Biped's arbitrary display tails.
        preferred = {"Hips": "Spine", "UpperChest": "Neck", "LeftHand": "LeftMiddleProximal",
                     "RightHand": "RightMiddleProximal"}.get(name)
        if children:
            bone.tail = heads[preferred or children[0]]
        elif name == "Head":
            bone.tail = bone.head + Vector((0, 0, .19))
        elif name.endswith("Toes"):
            bone.tail = bone.head + Vector((0, -.08, 0))
        else:
            bone.tail = bone.head + (bone.head - heads[parent]) * .7
        bone.align_roll(Vector((0, -1, 0)) if not name.endswith(("Foot", "Toes")) else Vector((0, 0, 1)))
        if parent:
            bone.parent = rig.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    a_rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    # Rotate each arm segment into a flat T, retaining a natural open hand.
    for label, sign in [("Left", 1), ("Right", -1)]:
        for part, end in [("Shoulder", "UpperArm"), ("UpperArm", "LowerArm"), ("LowerArm", "Hand")]:
            pb = rig.pose.bones[label + part]
            bpy.context.view_layer.update()
            direction = rig.pose.bones[label + end].head - pb.head
            rotation = direction.rotation_difference(Vector((sign, 0, 0)))
            mat = rotation.to_matrix().to_4x4() @ pb.matrix
            mat.translation = pb.head
            pb.matrix = mat
    bpy.context.view_layer.update()
    t_pose = {pb.name: pb.matrix.copy() for pb in rig.pose.bones}
    return rig, a_rest, t_pose, src_world


def skin(obj, rig, weights):
    obj.parent = rig
    for v, weight in zip(obj.data.vertices, weights):
        total = sum(weight.values())
        assert total > 0, (obj.name, v.index)
        for name, value in weight.items():
            group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
            group.add([v.index], value / total, "REPLACE")
    mod = obj.modifiers.new("Humanoid skin", "ARMATURE")
    mod.object = rig


def remap_mesh(obj, source, rig, a_rest, t_pose, src_world):
    groups = {g.index: MAP.get(g.name, "Head") for g in obj.vertex_groups}
    deform = {n: t_pose[n] @ a_rest[n].inverted() for n in PARENTS}
    weights = []
    for v in obj.data.vertices:
        w = {}
        for g in v.groups:
            name = groups[g.group]
            w[name] = w.get(name, 0) + g.weight
        w = dict(sorted(w.items(), key=lambda item: -item[1])[:4])
        total = sum(w.values())
        assert total > 0
        w = {n: x / total for n, x in w.items()}
        point = obj.matrix_world @ v.co
        v.co = sum((deform[n] @ point * value for n, value in w.items()), Vector())
        weights.append(w)
    obj.modifiers.clear()
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)
    obj.vertex_groups.clear()
    skin(obj, rig, weights)


def image_pixels(image):
    w, h = image.size
    values = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(values)
    return values.reshape((h, w, 4))


def save_pixels(name, pixels, out, noncolor=False):
    h, w, _ = pixels.shape
    image = bpy.data.images.new(name, width=w, height=h, alpha=True)
    image.colorspace_settings.name = "Non-Color" if noncolor else "sRGB"
    image.pixels.foreach_set(np.asarray(pixels, dtype=np.float32).ravel())
    image.filepath_raw = str(out / (name + ".png"))
    image.file_format = "PNG"
    image.save()
    image.filepath = image.filepath_raw
    image.source = "FILE"
    image.reload()
    return image


def recolor(pixels, role):
    h, w, _ = pixels.shape
    y, x = np.mgrid[0:h, 0:w]
    x, y = x / w, 1 - y / h  # Source atlas coordinates measured from the top.
    rgb = pixels[:, :, :3]
    lum = rgb @ np.array([.2126, .7152, .0722])
    def paint(mask, color, mean, contrast=1):
        shade = np.clip(1 + (lum - mean) / max(mean, .08) * contrast, .45, 1.55)
        rgb[mask] = (shade[:, :, None] * np.array(color))[mask]
    torso = (x > .29) & (x < .71) & (y < .90)
    pants = ((x < .29) | (x > .71)) & (y < .43)
    if role == "Cook":
        paint(torso & (lum > .015), (.095, .052, .026), .10, .6)
        sleeves = ((x < .29) | (x > .71)) & (y > .435) & (y < .845)
        shirt = torso & (x > .435) & (x < .56) & (y > .50) & (y < .68)
        paint(sleeves | shirt, (.73, .70, .63), .23, .55)
        paint(pants, (.105, .085, .065), .065, .4)
    elif role == "Helper":
        sleeves = ((x < .29) | (x > .71)) & (y > .445) & (y < .725)
        paint(torso | sleeves, (.68, .71, .71), .44, .75)
        paint(pants, (.095, .105, .11), .065, .4)
    elif role == "SoldierA":
        cloth = (lum < .22) & (y < .86)
        paint(cloth, (.17, .15, .125), .027, .22)
        tie = (rgb[:, :, 0] > rgb[:, :, 1] * 1.5) & (rgb[:, :, 0] > rgb[:, :, 2] * 1.5) & (x > .3) & (x < .7) & (y > .84)
        paint(tie, (.76, .74, .70), .13, .25)
    elif role == "SoldierB":
        sleeves = ((x < .29) | (x > .71)) & (y > .435) & (y < .85)
        paint(torso | sleeves, (.54, .50, .43), .48, .75)
        paint(pants, (.105, .095, .08), .075, .5)
    elif role == "Jacques":
        sleeves = ((x < .29) | (x > .71)) & (y > .435) & (y < .845)
        paint(torso | sleeves, (.76, .745, .68), .26, .65)
        paint(pants, (.055, .075, .115), .43, .55)
        belt = ((x < .29) | (x > .71)) & (y < .033)
        rgb[belt] = (.06, .04, .025)
    pixels[:, :, :3] = np.clip(rgb, 0, 1)
    return pixels


def material(name, color_image, normal_image=None, roughness=.78, alpha=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Specular IOR Level"].default_value = .25
    tex = tree.nodes.new("ShaderNodeTexImage")
    tex.image = color_image
    tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha:
        tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        mat.surface_render_method = "DITHERED"
        mat.use_transparent_shadow = True
    if normal_image:
        tex = tree.nodes.new("ShaderNodeTexImage")
        tex.image = normal_image
        normal = tree.nodes.new("ShaderNodeNormalMap")
        normal.inputs["Strength"].default_value = .6
        tree.links.new(tex.outputs["Color"], normal.inputs["Color"])
        tree.links.new(normal.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def materials(obj, role, source_path, out):
    texture_dir = source_path.parent.parent / "Textures"
    slots = [p.material_index for p in obj.data.polygons]
    new = []
    for old in obj.data.materials:
        nodes = [n for n in old.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]
        color_path = next(Path(n.image.filepath.replace("\\", "/")).name for n in nodes if "color" in n.image.filepath)
        color = bpy.data.images.load(str(texture_dir / color_path), check_existing=False)
        if max(color.size) > 2048:
            factor = 2048 / max(color.size)
            color.scale(round(color.size[0] * factor), round(color.size[1] * factor))
        pixels = image_pixels(color)
        part = "Body" if "body" in old.name else "Head" if "head" in old.name else "Hair"
        if part == "Body":
            pixels = recolor(pixels, role)
        color = save_pixels(role + "_" + part + "_BaseColor", pixels, out)
        norm = None
        if part != "Hair":
            n_path = texture_dir / color_path.replace("color", "normal")
            assert n_path.is_file(), n_path
            norm = bpy.data.images.load(str(n_path), check_existing=False)
            if max(norm.size) > 2048:
                norm.scale(2048, 2048)
            norm.colorspace_settings.name = "Non-Color"
            norm = save_pixels(role + "_" + part + "_Normal", image_pixels(norm), out, True)
        new.append(material(role + "_" + part, color, norm, alpha=part == "Hair"))
    obj.data.materials.clear()
    for mat in new:
        obj.data.materials.append(mat)
    for polygon, slot in zip(obj.data.polygons, slots):
        polygon.material_index = slot
    assert [p.material_index for p in obj.data.polygons] == slots


def fabric(role, out, color, stained=False, name="Cotton"):
    size = 1024
    y, x = np.mgrid[0:size, 0:size] / size
    weave = .025 * np.sin(x * math.tau * 250) + .018 * np.sin(y * math.tau * 230)
    shade = 1 + weave + .035 * np.cos(x * math.tau * 7) * np.sin(y * math.tau * 5)
    if stained:
        for cx, cy, radius in [(.25, .22, .08), (.68, .35, .11), (.55, .12, .05)]:
            shade -= .13 * np.exp(-((x - cx)**2 + (y - cy)**2) / radius**2)
    pixels = np.ones((size, size, 4), dtype=np.float32)
    pixels[:, :, :3] = np.clip(shade[:, :, None] * np.array(color), 0, 1)
    return material(role + "_" + name, save_pixels(role + "_" + name + "_BaseColor", pixels, out), roughness=.92)


def mesh(name, vertices, faces, uv, mat, rig, weights, thickness=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.materials.append(mat)
    layer = data.uv_layers.new(name="UVMap")
    for poly in data.polygons:
        for loop in poly.loop_indices:
            layer.data[loop].uv = uv[data.loops[loop].vertex_index]
        poly.use_smooth = True
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    skin(obj, rig, weights)
    if thickness:
        mod = obj.modifiers.new("Cloth thickness", "SOLIDIFY")
        mod.thickness = thickness
        select([obj])
        bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def apron(role, rig, cotton):
    objects = []
    top, bottom = 1.035, .38 if role == "Cook" else .49
    cols, rows = 18, 24
    vertices, weights, uv = [], [], []
    for j in range(rows + 1):
        t = j / rows
        z = top + (bottom - top) * t
        for i in range(cols + 1):
            u = i / cols
            x = (u * 2 - 1) * (.195 + t * .052)
            y = -.185 - .040 * t + .020 * (abs(x) / .25)**2 + .006 * t * math.cos(u * math.tau * 6)
            vertices.append((x, y, z))
            left = min(1, max(0, .5 + x / .16))
            # Lower panel follows thigh rotation early enough to rest on a lap.
            leg = min(1, max(0, (top-z)/.17))
            spine = .12 * max(0, 1-leg)
            calf = min(.75, max(0, (.515-z)/.18))
            weights.append({"Hips": 1-leg-spine, "Spine": spine,
                            "LeftUpperLeg": leg*left*(1-calf), "RightUpperLeg": leg*(1-left)*(1-calf),
                            "LeftLowerLeg": leg*left*calf, "RightLowerLeg": leg*(1-left)*calf})
            uv.append((u, 1 - t))
    faces = [(j*(cols+1)+i, j*(cols+1)+i+1, (j+1)*(cols+1)+i+1, (j+1)*(cols+1)+i)
             for j in range(rows) for i in range(cols)]
    objects.append(mesh(role + "_ApronSkirt", vertices, faces, uv, cotton, rig, weights, .003))
    if role == "Cook":
        verts, uv, weights = [], [], []
        for j in range(13):
            t = j / 12
            z = top + .355 * t
            for i in range(9):
                u = i / 8
                verts.append(((u * 2 - 1) * (.19 - t * .052), -.176 - .025 * math.sin(t * math.pi), z))
                uv.append((u, t))
                weights.append({"Spine": max(0, 1 - 2*t), "Chest": 1 - abs(2*t - 1), "UpperChest": max(0, 2*t - 1)})
        faces = [(j*9+i, j*9+i+1, (j+1)*9+i+1, (j+1)*9+i) for j in range(12) for i in range(8)]
        objects.append(mesh("Cook_ApronBib", verts, faces, uv, cotton, rig, weights, .003))
        # A low neck loop, clear of the head/neck deformation seam.
        path = [(-.12,-.19,1.39),(-.085,-.12,1.48),(-.075,.03,1.50),(0,.07,1.50),
                (.075,.03,1.50),(.085,-.12,1.48),(.12,-.19,1.39)]
        objects.append(ribbon("Cook_NeckStrap", path, .018, cotton, rig, "UpperChest"))
    # Waist band and two visible ties/tails on the back, all bound to pelvis.
    path = [(.197*math.sin(a), -.146*math.cos(a), top) for a in np.linspace(0, math.tau, 49)]
    objects.append(ribbon(role + "_WaistBand", path, .028, cotton, rig, "Hips", vertical=True))
    for sign in [-1, 1]:
        path = [(0,.152,top), (sign*.07,.17,top-.035), (sign*.065,.174,top-.18)]
        objects.append(ribbon(role + "_WaistTie" + str(sign), path, .023, cotton, rig, "Hips"))
    return objects


def ribbon(name, path, width, mat, rig, bone, vertical=False):
    points = [Vector(p) for p in path]
    verts, uv = [], []
    for i, point in enumerate(points):
        tangent = points[min(i+1, len(points)-1)] - points[max(0,i-1)]
        across = Vector((0,0,1)) if vertical else tangent.cross(Vector((0,1,0))).normalized()
        verts += [point - across*width/2, point + across*width/2]
        uv += [(0,i/(len(points)-1)), (1,i/(len(points)-1))]
    faces = [(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(points)-1)]
    return mesh(name, verts, faces, uv, mat, rig, [{bone:1}]*len(verts), .003)


def cap(role, rig, mat, body):
    head_vertices = [v.co for v in body.data.vertices if any(body.vertex_groups[g.group].name == "Head" and g.weight>.5 for g in v.groups)]
    highest = max(v.z for v in head_vertices)
    base = highest - (.055 if role == "Jacques" else .045)
    cy = -.003
    if role == "Cook":
        rings = [(base,.093,.097),(base+.024,.104,.111),(base+.067,.124,.13),(base+.105,.091,.096),(base+.117,0,0)]
    elif role == "Jacques":
        rings = [(base,.096,.103),(base+.038,.101,.108),(base+.071,.092,.095),(base+.105,.07,.074),(base+.123,0,0)]
    else:
        rings = [(base,.098,.106),(base+.028,.106,.116),(base+.064,.119,.132),(base+.092,.078,.1),(base+.103,0,0)]
    verts, uv = [], []
    n = 48
    for j,(z,rx,ry) in enumerate(rings[:-1]):
        for i in range(n):
            a=i/n*math.tau
            soft=1+.018*math.cos(7*a)+.012*math.sin(5*a)
            verts.append((rx*math.cos(a)*soft,cy+ry*math.sin(a)*soft,z+.002*math.cos(6*a)))
            uv.append((i/n,j/(len(rings)-1)))
    pole=len(verts)
    verts.append((0,cy,rings[-1][0]))
    uv.append((.5,1))
    faces = [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-2) for i in range(n)]
    faces += [((len(rings)-2)*n+i,(len(rings)-2)*n+(i+1)%n,pole) for i in range(n)]
    objects=[mesh(role+"_SoftCap",verts,faces,uv,mat,rig,[{"Head":1}]*len(verts),.002)]
    if role == "SoldierB":
        verts=[(-.079,-.07,base+.014),(.079,-.07,base+.014),(.068,-.153,base+.006),(0,-.178,base),(-.068,-.153,base+.006)]
        objects.append(mesh("SoldierB_ClothPeak",verts,[(0,1,2,3,4)],[(0,0),(1,0),(1,1),(.5,1),(0,1)],mat,rig,[{"Head":1}]*5,.004))
    return objects


def rolled_sleeves(body, rig, linen, skinmat):
    """Re-use forearm topology, then add physical folded cloth at each elbow."""
    result=[]
    slot=len(body.data.materials)
    body.data.materials.append(skinmat)
    uv=body.data.uv_layers.active.data
    for label in ["Left","Right"]:
        elbow=rig.data.bones[label+"LowerArm"].head_local
        wrist=rig.data.bones[label+"Hand"].head_local
        axis=(wrist-elbow).normalized()
        length=(wrist-elbow).length
        affected=set()
        for p in body.data.polygons:
            center=sum((body.data.vertices[i].co for i in p.vertices),Vector())/len(p.vertices)
            t=(center-elbow).dot(axis)/length
            weights=[sum(g.weight for g in body.data.vertices[i].groups if body.vertex_groups[g.group].name==label+"LowerArm") for i in p.vertices]
            atlas_y = 1 - sum(uv[loop].uv.y for loop in p.loop_indices) / len(p.loop_indices)
            if .18<t<1.14 and (max(weights)>.15 or atlas_y<.845) and p.material_index==0:
                p.material_index=slot
                affected.update(p.vertices)
                for loop in p.loop_indices:
                    v=body.data.vertices[body.data.loops[loop].vertex_index].co
                    uv[loop].uv=((v.y+.12)/.15,(v-elbow).dot(axis)/length)
        for i in affected:
            v=body.data.vertices[i]
            t=(v.co-elbow).dot(axis)
            center=elbow+axis*t
            radial=v.co-center
            v.co=center+radial*(.83 if t/length<.92 else .96)
        center=elbow+(wrist-elbow)*.18
        e1=Vector((0,1,0)); e2=axis.cross(e1).normalized()
        verts,coords=[],[]
        for j in range(4):
            axial=(j/3-.5)*.032
            radius=.040+.007*math.sin(j/3*math.pi)
            for i in range(32):
                a=i/32*math.tau
                verts.append(center+axis*axial+radius*(e1*math.cos(a)+e2*math.sin(a)))
                coords.append((i/32,j/3))
        faces=[(j*32+i,j*32+(i+1)%32,(j+1)*32+(i+1)%32,(j+1)*32+i) for j in range(3) for i in range(32)]
        result.append(mesh("Cook_"+label+"RolledCuff",verts,faces,coords,linen,rig,[{label+"LowerArm":1}]*len(verts),.003))
        assert affected, "No sleeve geometry converted: " + label
    return result


def aim(obj, point):
    obj.rotation_euler=(Vector(point)-obj.location).to_track_quat("-Z","Y").to_euler()


def studio(path, center, width, height, resolution, samples=24):
    scene=bpy.context.scene
    scene.render.engine="CYCLES"
    scene.cycles.samples=samples
    scene.cycles.use_denoising=True
    scene.cycles.seed=1911
    scene.render.threads_mode="FIXED"
    scene.render.threads=12
    scene.world=bpy.data.worlds.new("Neutral backdrop")
    scene.world.use_nodes=True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value=(.75,.73,.69,1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value=.65
    bpy.ops.object.camera_add(location=Vector(center)+Vector((0,-12,0)))
    camera=bpy.context.object
    camera.data.type="ORTHO"
    camera.data.ortho_scale=width
    aim(camera,center)
    scene.camera=camera
    for offset,power,size in [((-3,-4,5),1600,5),((4,-3,3),1000,5),((0,3,5),1800,4)]:
        bpy.ops.object.light_add(type="AREA",location=Vector(center)+Vector(offset))
        light=bpy.context.object
        light.data.energy=power
        light.data.shape="DISK"
        light.data.size=size
        aim(light,center)
    scene.view_settings.view_transform="AgX"
    scene.render.resolution_x,scene.render.resolution_y=resolution
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)


def caption(text, x, z, size=.1):
    data=bpy.data.curves.new(text,"FONT")
    data.body=text
    data.align_x="CENTER"
    data.size=size
    obj=bpy.data.objects.new(text,data)
    bpy.context.collection.objects.link(obj)
    obj.location=(x,-.6,z)
    obj.rotation_euler=(math.pi/2,0,0)
    mat=bpy.data.materials.get("Ink") or bpy.data.materials.new("Ink")
    mat.diffuse_color=(.06,.05,.04,1)
    data.materials.append(mat)


def prepare(pass_number):
    for folder in [SCRATCH,CHARACTERS,ANIMS]:
        folder.mkdir(parents=True,exist_ok=True)
    stats={}
    signature=None
    for role,base in CAST.items():
        print("PREPARE",role,flush=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        src=source_file(base)
        bpy.ops.import_scene.fbx(filepath=str(src),use_anim=False)
        source=next(o for o in bpy.data.objects if o.type=="ARMATURE")
        body=next(o for o in bpy.data.objects if o.type=="MESH")
        rig,a_rest,t_pose,src_world=make_rig(source)
        remap_mesh(body,source,rig,a_rest,t_pose,src_world)
        select([rig])
        bpy.ops.object.mode_set(mode="POSE")
        bpy.ops.pose.armature_apply(selected=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        bpy.data.objects.remove(source,do_unlink=True)
        for obj in list(bpy.data.objects):
            if obj not in [body,rig]: bpy.data.objects.remove(obj,do_unlink=True)
        body.name=role+"_Body"
        for action in list(bpy.data.actions): bpy.data.actions.remove(action)
        out=CHARACTERS/role
        out.mkdir(exist_ok=True)
        materials(body,role,src,out)
        if role in ["SoldierB","Jacques"]:
            leather=bpy.data.materials.new(role+"_PlainDarkLeather")
            leather.use_nodes=True
            shader=leather.node_tree.nodes.get("Principled BSDF")
            shader.inputs["Base Color"].default_value=(.045,.029,.018,1)
            shader.inputs["Roughness"].default_value=.85
            shoe_slot=len(body.data.materials)
            body.data.materials.append(leather)
            shoe_faces=[p for p in body.data.polygons if max(body.data.vertices[i].co.z for i in p.vertices)<.17]
            assert shoe_faces,(role,"missing shoe faces")
            for p in shoe_faces: p.material_index=shoe_slot
        objects=[body]
        if role in ["Cook","Helper"]:
            cotton=fabric(role,out,(.79,.765,.69),stained=role=="Helper")
            objects+=apron(role,rig,cotton)
        if role=="Cook":
            skinmat=fabric(role,out,(.65,.39,.26),name="ForearmSkin")
            objects+=rolled_sleeves(body,rig,cotton,skinmat)
        if role in ["Cook","SoldierB","Jacques"]:
            cloth=cotton if role=="Cook" else fabric(role,out,(.052,.071,.11) if role=="Jacques" else (.16,.135,.105),name="CapCloth")
            objects+=cap(role,rig,cloth,body)
        bpy.context.scene.unit_settings.system="METRIC"
        bpy.context.scene.unit_settings.scale_length=1
        bpy.context.scene.render.fps=30
        rig.data.pose_position="REST"
        bones=[{"name":b.name,"parent":b.parent.name if b.parent else None,"rest":[list(r) for r in b.matrix_local],"length":b.length} for b in rig.data.bones]
        current=[(b["name"],b["parent"],[round(v,5) for r in b["rest"] for v in r]) for b in bones]
        if signature is None:
            signature=current
            skeleton={"bones":bones,"source_map":MAP,"a_rest":{n:[list(r) for r in m] for n,m in a_rest.items()},
                      "rocketbox_rest":{n:[list(r) for r in m] for n,m in src_world.items()}}
            (CHARACTERS/"skeleton.json").write_text(json.dumps(skeleton,indent=2))
        else:
            # Source bind differences are only floating-point export noise.
            assert [(n,p) for n,p,_ in current]==[(n,p) for n,p,_ in signature]
            for (_,_,r),(_,_,s) in zip(current,signature): assert max(abs(v-w) for v,w in zip(r,s))<.00002
        export_fbx(out/(role+".fbx"),[rig]+objects)
        select([rig]+objects)
        bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH/(role+".blend")),check_existing=False)
        stats[role]={"base":base,"triangles":sum(len(p.vertices)-2 for o in objects for p in o.data.polygons),"bones":len(bones),
                     "mesh_objects":[o.name for o in objects],"source":str(src.relative_to(SOURCES))}
    (CHARACTERS/"cast_stats.json").write_text(json.dumps(stats,indent=2))
    license_path=SOURCES/"MicrosoftRocketbox_Characters/LICENSE.md"
    shutil.copyfile(license_path,CHARACTERS/"LICENSE_Rocketbox_MIT.txt")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i,role in enumerate(CAST):
        with bpy.data.libraries.load(str(SCRATCH/(role+".blend")),link=False) as (src,dst):
            dst.objects=[name for name in src.objects]
        loaded=[o for o in dst.objects if o]
        for obj in loaded:
            bpy.context.collection.objects.link(obj)
        rig=next(o for o in loaded if o.type=="ARMATURE")
        rig.location.x=(i-2)*1.86
        caption(role,rig.location.x,-.20,.13)
    caption("Hai Ban Tay  |  1911 galley cast  |  Humanoid T-pose",0,2.18,.12)
    preview=SCRATCH/f"cast_preview_pass{pass_number}.png"
    studio(preview,(0,0,.99),9.4,2.8,(2350,750))
    shutil.copyfile(preview,CHARACTERS/"cast_preview.png")
    print("CAST_COMPLETE",json.dumps(stats),flush=True)


def source_file(name):
    paths = list((SOURCES / "MicrosoftRocketbox_Characters/Assets/Avatars").glob(f"*/{name}/Export/{name}.fbx"))
    assert len(paths) == 1, (name, paths)
    return paths[0]


def inspect():
    SCRATCH.mkdir(parents=True, exist_ok=True)
    report = {}
    files = [source_file(n) for n in CAST.values()]
    files += [SOURCES / "MicrosoftRocketbox_Animations/Assets/Animations/all_animations_max_motextr_static/m_work_table.max.fbx",
              SOURCES / "KayKit_CharacterAnimations_1.1/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx"]
    for path in files:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
        entry = {"fps": bpy.context.scene.render.fps, "objects": [], "actions": []}
        for obj in bpy.data.objects:
            data = {"name": obj.name, "type": obj.type, "world": [list(r) for r in obj.matrix_world]}
            if obj.type == "ARMATURE":
                data["bones"] = [{"name": b.name, "parent": b.parent.name if b.parent else None,
                                  "head": list(obj.matrix_world @ b.head_local),
                                  "tail": list(obj.matrix_world @ b.tail_local),
                                  "world": [list(r) for r in obj.matrix_world @ b.matrix_local]}
                                 for b in obj.data.bones]
            if obj.type == "MESH":
                data["bounds"] = [list(obj.matrix_world @ Vector(c)) for c in obj.bound_box]
                data["materials"] = [{"name": m.name, "images": [n.image.filepath for n in m.node_tree.nodes
                                         if n.type == "TEX_IMAGE" and n.image]} for m in obj.data.materials if m]
                data["groups"] = [g.name for g in obj.vertex_groups]
            entry["objects"].append(data)
        entry["actions"] = [{"name": a.name, "range": list(a.frame_range), "curves": len(a.fcurves)} for a in bpy.data.actions]
        report[path.stem] = entry
    (SCRATCH / "source_inspection.json").write_text(json.dumps(report, indent=2))
    print("INSPECTION", SCRATCH / "source_inspection.json", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--inspect", action="store_true")
    parser.add_argument("--pass", dest="pass_number", type=int, default=1)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.inspect:
        inspect()
    else:
        prepare(args.pass_number)
