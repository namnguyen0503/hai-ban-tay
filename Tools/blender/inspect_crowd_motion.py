"""Measure source seating coordinates before changing contact targets."""
import json
import sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
import prepare_cast as cast
import export_anims as anim
import export_crowd_anims as crowd_anim
bpy.ops.wm.open_mainfile(filepath=str(cast.SCRATCH/'Cook.blend'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
skeleton=json.loads((cast.CHARACTERS/'skeleton.json').read_text())
report={}
for name in ['SitChair','Drink']:
    folder,stem,trim=crowd_anim.SPECS[name]
    samples,source=anim.retarget(rig,skeleton,anim.ROCKET/('all_animations_max_motextr_'+folder)/(stem+'.max.fbx'),frame_range=trim)
    report[name]=[{n:list(anim.world_pose(rig,p)[n].translation) for n in ['Hips','Head','LeftUpperArm','RightUpperArm','LeftFoot','RightFoot']} for p in [samples[0],samples[len(samples)//2],samples[-1]]]
print(json.dumps(report,indent=2),flush=True)
(crowd_anim.SCRATCH/'motion_coordinates.json').write_text(json.dumps(report,indent=2))
