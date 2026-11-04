"""Read-only source geometry inspection; report only into crowd scratch."""
import json
import sys
from pathlib import Path
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
import prepare_cast as cast

report = {}
for base in ["Male_Adult_02", "Male_Adult_08", "Male_Adult_14", "Business_Male_01", "Business_Male_04", "Wood_Male_01", "Chef_Female_01"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = cast.source_file(base)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    r, a, t, src = cast.make_rig(rig)
    cast.remap_mesh(body, rig, r, a, t, src)
    materials = []
    for i, mat in enumerate(body.data.materials):
        polygons = [p for p in body.data.polygons if p.material_index == i]
        coords = [body.data.vertices[j].co for p in polygons for j in p.vertices]
        bounds = [[min(v[j] for v in coords), max(v[j] for v in coords)] for j in range(3)] if coords else []
        materials.append({"index": i, "name": mat.name, "faces": len(polygons), "bounds": bounds,
            "images": [n.image.filepath for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]})
    bands = []
    for lo, hi in [(0, .16), (.16, .36), (.36, .5), (.5, .9), (.9, 1.1), (1.1, 1.4), (1.4, 1.6), (1.6, 1.8), (1.8, 2.3)]:
        faces = [p for p in body.data.polygons if lo <= p.center.z < hi]
        bands.append({"range": [lo, hi], "faces": len(faces), "slots": {str(i): sum(p.material_index == i for p in faces) for i in range(len(materials))}})
    report[base] = {"materials": materials, "bands": bands,
        "height": max(v.co.z for v in body.data.vertices),
        "head_max": max(v.co.z for v in body.data.vertices if any(body.vertex_groups[g.group].name == "Head" and g.weight > .5 for g in v.groups))}
scratch = cast.SOURCES / "_scratch/crowd"
scratch.mkdir(parents=True, exist_ok=True)
(scratch / "source_geometry.json").write_text(json.dumps(report, indent=2))
print(json.dumps({k: {"height": v["height"], "head_max": v["head_max"], "materials": [(m["index"], m["name"], m["faces"], m["bounds"][2]) for m in v["materials"]]} for k,v in report.items()}), flush=True)
