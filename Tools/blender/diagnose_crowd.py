"""Read-only weights/bind diagnostics for the first crowd revision."""
import json
import sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix
import prepare_crowd as crowd
import prepare_cast as cast

skeleton=json.loads((cast.CHARACTERS/'skeleton.json').read_text())
report={}
for role in ['Mai','Captain','VendorVN_F','Cook']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(cast.CHARACTERS/role/(role+'.fbx')),use_anim=False)
    rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    meshes=[o for o in bpy.data.objects if o.type=='MESH']
    weights=[sum(g.weight for g in v.groups) for o in meshes for v in o.data.vertices]
    bad=[{'mesh':o.name,'index':v.index,'co':list(v.co),'weights':[(o.vertex_groups[g.group].name,g.weight) for g in v.groups]}
         for o in meshes for v in o.data.vertices if abs(sum(g.weight for g in v.groups)-1)>.0001]
    bind=max(abs(x-y) for b in skeleton['bones'] for row,ref in zip(rig.matrix_world@rig.data.bones[b['name']].matrix_local,Matrix(b['rest'])) for x,y in zip(row,ref))
    report[role]={'rig':rig.name,'weight_min':min(weights),'weight_max':max(weights),'bad_count':len(bad),'bad_examples':bad[:5],'bind_error':bind,
        'bind_examples':{n:[list(row) for row in rig.matrix_world@rig.data.bones[n].matrix_local] for n in ['Hips','LeftUpperArm','LeftUpperLeg']},
        'poses':{p.name:[list(row) for row in p.matrix_basis] for p in rig.pose.bones if max(abs(x-y) for row,ref in zip(p.matrix_basis,Matrix.Identity(4)) for x,y in zip(row,ref))>.00001}}
print(json.dumps(report,indent=2),flush=True)
(crowd.SCRATCH/'diagnosis_pass1.json').write_text(json.dumps(report,indent=2))
