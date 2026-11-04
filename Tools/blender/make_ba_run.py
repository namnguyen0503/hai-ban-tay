"""Retarget Rocketbox Run cho thân anh Ba; Staging chỉ đọc, mọi đầu ra trong worktree."""
import json
import statistics
import sys
from pathlib import Path
sys.dont_write_bytecode = True
import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))
import export_anims as anims
import prepare_cast as cast

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Game/Assets/_Project/Art/PlayerBody"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT / "Game/Assets/_Project/Art/Characters/Anims/Walk.fbx"))
rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
for obj in list(bpy.context.scene.objects):
    if obj != rig:
        bpy.data.objects.remove(obj, do_unlink=True)
rig.animation_data_clear()
skeleton = json.loads((ROOT / "Staging/art_hq_fbx/Characters/skeleton.json").read_text())
source = anims.ROCKET / "all_animations_max_motextr_xy/m_run_neutral.max.fbx"
samples, provenance = anims.retarget(rig, skeleton, source)
anims.cyclic(samples, width=3)
action = anims.bake(rig, "Run", samples)
rig.animation_data.action = action
scene = bpy.context.scene
scene.render.fps = 30
scene.frame_start = 1
scene.frame_end = len(samples)
feet = []
for f in range(1, len(samples) + 1):
    scene.frame_set(f)
    feet.append([rig.matrix_world @ rig.pose.bones[n].head for n in ["LeftFoot", "RightFoot"]])
heights = sorted(p.z for row in feet for p in row)
cut = heights[len(heights) // 3]
speeds = [abs(feet[i+1][side].y-feet[i][side].y)*30 for i in range(len(feet)-1) for side in [0, 1]
          if feet[i][side].z <= cut and feet[i+1][side].z <= cut]
assert speeds, "Run không có mẫu chân chạm đất"
stride = (len(samples)-1)/30 * statistics.median(speeds) * .95
assert 1 < stride < 5, stride
scene.frame_set(0)
OUT.mkdir(parents=True, exist_ok=True)
cast.export_fbx(OUT / "Run.fbx", [rig], animation=True)
report = {"source": provenance, "frames": len(samples), "fps": 30, "stride_scaled_m": stride,
          "edits": ["Retarget bằng helper NPC hiện có", "Root X/Y trung hòa, giữ bob Z", "Loop seam 3 frame", "Không ghi Staging"]}
(ROOT / "Logs/body_run.json").write_text(json.dumps(report, ensure_ascii=False, indent=2))
print("BA_RUN", json.dumps(report, ensure_ascii=False), flush=True)
