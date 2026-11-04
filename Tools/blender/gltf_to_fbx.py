"""Blender (chạy nền): đổi một file .glb/.gltf sang .fbx, ghi texture nhúng ra PNG cạnh file FBX.

blender -b --factory-startup -P Tools/blender/gltf_to_fbx.py -- <vào.glb> <ra.fbx>
"""
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = os.path.abspath(args[0]), os.path.abspath(args[1])
out_dir = os.path.dirname(dst)
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

base = os.path.splitext(os.path.basename(dst))[0]
for i, img in enumerate(bpy.data.images):
    if img.source != "FILE" and not img.packed_file:
        continue
    name = bpy.path.clean_name(img.name or f"tex{i}")
    path = os.path.join(out_dir, f"{base}_{name}.png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=False,
    object_types={"MESH", "ARMATURE", "EMPTY"},
    path_mode="RELATIVE",
    embed_textures=False,
    apply_unit_scale=True,
    bake_space_transform=True,
    add_leaf_bones=False,
)
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
print(f"GLTF2FBX OK {os.path.basename(dst)} tris={tris} images={len(bpy.data.images)}")
