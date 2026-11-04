"""Đọc chân/cổ chân trong FBX hiện có, chỉ ghi bằng chứng vào Logs của worktree."""
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
import prepare_cast as cast
import prepare_crowd as crowd

root = Path(__file__).resolve().parents[2]
report = {}
for folder in sorted((root / "Game/Assets/_Project/Art/Characters").iterdir()):
    path = folder / (folder.name + ".fbx")
    if not path.is_file():
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    entry = {"bones": {n: list(rig.data.bones[n].head_local) for n in
             ["LeftLowerLeg", "LeftFoot", "LeftToes", "RightLowerLeg", "RightFoot", "RightToes"]}, "meshes": []}
    for obj in [o for o in bpy.data.objects if o.type == "MESH"]:
        cast.select([obj])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        obj.data.update()
        low = [v for v in obj.data.vertices if v.co.z < .43]
        materials = {obj.data.materials[p.material_index].name for p in obj.data.polygons if p.center.z < .43}
        hands = [p for p in obj.data.polygons if abs(p.center.x) > .78 and 1.38 < p.center.z < 1.53]
        entry["meshes"].append({"name": obj.name, "triangles": crowd.triangles(obj),
            "low_vertices": len(low), "ankle_materials": sorted(materials),
            "hand_materials": sorted({obj.data.materials[p.material_index].name for p in hands}),
            "low_bounds": [[min(v.co[i] for v in low), max(v.co[i] for v in low)] for i in range(3)] if low else []})
    report[folder.name] = entry
    print("FEET_AUDIT", folder.name, entry, flush=True)
(root / "Logs/feet12_fbx_before.json").write_text(json.dumps(report, indent=2))
