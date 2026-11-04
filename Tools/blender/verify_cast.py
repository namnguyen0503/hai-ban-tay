"""Re-import delivery FBXs and assert skeleton, skin, texture and loop contracts."""
import json
import math
import shutil
import sys
from pathlib import Path

sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix, Vector
import prepare_cast as cast


def signature(rig):
    return {b.name:(b.parent.name if b.parent else None, rig.matrix_world @ b.matrix_local) for b in rig.data.bones}


def same_rig(reference, actual, label):
    assert reference.keys()==actual.keys(),label
    for name,(parent,mat) in reference.items():
        assert actual[name][0]==parent,(label,name,"parent")
        assert max(abs(v-w) for a,b in zip(mat,actual[name][1]) for v,w in zip(a,b))<.00005,(label,name,"bind")


def main():
    result={"models":{},"clips":{},"passed":False}
    reference=None
    stats=json.loads((cast.CHARACTERS/"cast_stats.json").read_text())
    manifest=json.loads((cast.ANIMS/"clip_manifest.json").read_text())
    for role in cast.CAST:
        relocated=cast.SCRATCH/"portability"/role
        shutil.copytree(cast.CHARACTERS/role,relocated,dirs_exist_ok=True)
        path=relocated/(role+".fbx")
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(path),use_anim=True)
        rigs=[o for o in bpy.data.objects if o.type=="ARMATURE"]
        meshes=[o for o in bpy.data.objects if o.type=="MESH"]
        assert len(rigs)==1 and meshes,role
        assert all(o.type in {"ARMATURE","MESH"} for o in bpy.data.objects),role
        assert not bpy.data.actions,(role,"animation in character")
        rig=rigs[0]
        assert len(rig.data.bones)==52,role
        current=signature(rig)
        if reference is None: reference=current
        else: same_rig(reference,current,role)
        assert current["LeftUpperLeg"][0]=="Hips" and current["LeftShoulder"][0]=="UpperChest"
        for label in ["Left","Right"]:
            arm=[current[label+p][1].translation for p in ["UpperArm","LowerArm","Hand"]]
            assert max(v.z for v in arm)-min(v.z for v in arm)<.0001,(role,"T pose")
        for mesh in meshes:
            mods=[m for m in mesh.modifiers if m.type=="ARMATURE"]
            assert len(mods)==1 and mods[0].object==rig,(role,mesh.name,"skin")
            for v in mesh.data.vertices:
                assert all(math.isfinite(x) for x in v.co),role
                positive=[g for g in v.groups if g.weight>1e-6]
                assert 1<=len(positive)<=4,(role,mesh.name,v.index,"influences",len(positive))
                assert abs(sum(g.weight for g in positive)-1)<.0001,(role,mesh.name,v.index,"weight sum")
                assert all(mesh.vertex_groups[g.group].name in current for g in positive),role
        textures=[]
        for image in bpy.data.images:
            if image.source=="FILE":
                assert Path(bpy.path.abspath(image.filepath)).is_file(),(role,image.name,image.filepath)
                assert Path(bpy.path.abspath(image.filepath)).parent==relocated,(role,"nonportable texture",image.filepath)
                image.reload()
                assert len(image.pixels)>0,(role,image.name,"pixel buffer")
                assert image.has_data and 0<max(image.size)<=2048,(role,image.name,image.size[:])
                textures.append({"file":Path(image.filepath).name,"size":list(image.size)})
        triangles=sum(len(p.vertices)-2 for m in meshes for p in m.data.polygons)
        assert triangles==stats[role]["triangles"],(role,triangles,stats[role])
        zero_area=0
        for obj in meshes:
            obj.data.calc_loop_triangles()
            for tri in obj.data.loop_triangles:
                a,b,c=[obj.data.vertices[i].co for i in tri.vertices]
                zero_area+=((b-a).cross(c-a).length==0)
        result["models"][role]={"triangles":triangles,"zero_area_triangles":zero_area,"bones":52,"textures":textures,"skin_normalized":True,"T_pose":True,"relocated_textures_loaded":True}
        print("FBX_MODEL_OK",role,triangles,flush=True)
    for name,entry in manifest.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(cast.ANIMS/(name+".fbx")),use_anim=True)
        assert len(bpy.data.objects)==1 and bpy.data.objects[0].type=="ARMATURE",(name,"armature only")
        rig=bpy.data.objects[0]
        same_rig(reference,signature(rig),name)
        assert len(bpy.data.actions)==1,(name,"one action",[a.name for a in bpy.data.actions])
        action=rig.animation_data.action
        start,end=(round(x) for x in action.frame_range)
        assert end-start+1==entry["frames"],(name,start,end,entry["frames"])
        assert bpy.context.scene.render.fps==30,(name,"fps")
        hips=[]
        poses=[]
        object_matrices=[]
        moving=set()
        first=None
        for frame in range(start,end+1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            matrices={pb.name:(rig.matrix_world @ pb.matrix).copy() for pb in rig.pose.bones}
            hips.append(matrices["Hips"].translation.copy())
            object_matrices.append(rig.matrix_world.copy())
            if first is None: first=matrices
            for bone,matrix in matrices.items():
                assert all(math.isfinite(v) for row in matrix for v in row),(name,bone,"finite")
                if max(abs(v-w) for a,b in zip(first[bone],matrix) for v,w in zip(a,b))>.0002: moving.add(bone)
            if frame in [start,end]: poses.append(matrices)
        planar=max((Vector((p.x,p.y,0))-Vector((hips[0].x,hips[0].y,0))).length for p in hips)
        object_drift=max(abs(v-w) for m in object_matrices for a,b in zip(m,object_matrices[0]) for v,w in zip(a,b))
        assert planar<.0001 and object_drift<.0001,(name,"root drift",planar,object_drift)
        assert moving,(name,"static clip")
        seam_position=max((poses[0][b].translation-poses[1][b].translation).length for b in poses[0])
        seam_rotation=max(poses[0][b].to_quaternion().rotation_difference(poses[1][b].to_quaternion()).angle for b in poses[0])
        if entry["loop"]:
            assert seam_position<.0001 and seam_rotation<.002,(name,"loop seam",seam_position,seam_rotation)
        result["clips"][name]={"frames":entry["frames"],"fps":30,"loop":entry["loop"],"moving_bones":len(moving),
            "planar_hips_drift_m":planar,"loop_position_error_m":seam_position,"loop_rotation_error_rad":seam_rotation}
        print("FBX_CLIP_OK",name,entry["frames"],flush=True)
    result["passed"]=True
    (cast.CHARACTERS/"blender_verification.json").write_text(json.dumps(result,indent=2))
    print("FBX_ALL_VERIFIED",flush=True)


if __name__=="__main__":
    main()
