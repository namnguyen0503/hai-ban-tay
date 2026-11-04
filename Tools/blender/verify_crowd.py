"""Re-import all 60 character FBXs and 31 clips; preserve the old delivery."""
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
import prepare_cast as cast
import prepare_crowd as crowd
from verify_cast import signature,same_rig


def main():
    baseline=json.loads((crowd.SCRATCH/'baseline_sha256.json').read_text())
    mutable={str(cast.CHARACTERS/'README.md'),str(cast.CHARACTERS/'cast_stats.json'),
        str(cast.CHARACTERS/'delivery_sha256.json'),str(cast.ANIMS/'clip_manifest.json')}
    mutable={str(Path(p).relative_to(cast.ROOT)) for p in mutable}
    preserved=[]
    for name,digest in baseline.items():
        if name in mutable:continue
        assert hashlib.sha256((cast.ROOT/name).read_bytes()).hexdigest()==digest,(name,'original changed')
        preserved.append(name)
    stats=json.loads((cast.CHARACTERS/'cast_stats.json').read_text())
    assert set(stats)==set(cast.CAST)|set(crowd.ROLES),(len(stats),'roles')
    result={'models':{},'clips':{},'original_files_preserved':preserved,'passed':False}
    reference=None
    for role,entry in stats.items():
        relocated=crowd.SCRATCH/'portability'/role
        shutil.copytree(cast.CHARACTERS/role,relocated,dirs_exist_ok=True)
        for lod in [0,1,2]:
            name=role+(f'_LOD{lod}' if lod else '')
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(relocated/(name+'.fbx')),use_anim=True)
            rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];meshes=[o for o in bpy.data.objects if o.type=='MESH']
            assert len(rigs)==1 and meshes and all(o.type in {'MESH','ARMATURE'} for o in bpy.data.objects),name
            assert not bpy.data.actions,(name,'character animation')
            rig=rigs[0];assert rig.name=='Armature',(name,rig.name)
            current=signature(rig)
            if reference is None:reference=current
            same_rig(reference,current,name)
            assert len(current)==52,name
            for side in ['Left','Right']:
                points=[current[side+p][1].translation for p in ['UpperArm','LowerArm','Hand']]
                assert max(v.z for v in points)-min(v.z for v in points)<.0001,(name,'T-pose')
            triangles=sum(len(p.vertices)-2 for obj in meshes for p in obj.data.polygons)
            assert triangles==entry['lod_triangles'][f'LOD{lod}'],(name,'triangles',triangles,entry['lod_triangles'])
            zero=0
            for obj in meshes:
                assert len(obj.data.uv_layers)==1 or lod==0 and role in cast.CAST,(name,'UV layers')
                skin=[m for m in obj.modifiers if m.type=='ARMATURE']
                assert len(skin)==1 and skin[0].object==rig,(name,'skin')
                for vertex in obj.data.vertices:
                    values=[g for g in vertex.groups if g.weight>1e-6]
                    assert 1<=len(values)<=4 and abs(sum(g.weight for g in values)-1)<.0001,(name,vertex.index,'weights')
                    assert all(obj.vertex_groups[g.group].name in current for g in values),name
                    assert all(math.isfinite(v) for v in vertex.co),name
                obj.data.calc_loop_triangles()
                for tri in obj.data.loop_triangles:
                    a,b,c=[obj.data.vertices[i].co for i in tri.vertices]
                    zero+=((b-a).cross(c-a).length<1e-10)
            if lod>0 or role in crowd.ROLES:assert len(meshes)==1,(name,'one joined skin')
            textures=[]
            for image in bpy.data.images:
                if image.source!='FILE':continue
                path=Path(bpy.path.abspath(image.filepath))
                assert path.is_file() and path.parent==relocated,(name,'relative texture',str(path))
                image.reload()
                assert len(image.pixels)>0 and image.has_data and 0<max(image.size)<=2048,(name,image.name,list(image.size))
                textures.append({'file':path.name,'size':list(image.size)})
            ratio=triangles/entry['triangles']
            if lod==1:assert .47<ratio<.53,(name,ratio)
            if lod==2:assert .15<=ratio<=.20,(name,ratio)
            result['models'][name]={'triangles':triangles,'near_zero_area_triangles':zero,'bones':52,'textures':textures,
                'texture_files':entry['texture_count'],'meshes':len(meshes),'skin_normalized':True,'common_bind':True,'relocated_textures_loaded':True}
            print('CROWD_MODEL_OK',name,triangles,flush=True)
    manifest=json.loads((cast.ANIMS/'clip_manifest.json').read_text());assert len(manifest)==31,len(manifest)
    for name,entry in manifest.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(cast.ANIMS/(name+'.fbx')),use_anim=True)
        assert len(bpy.data.objects)==1 and bpy.data.objects[0].type=='ARMATURE',(name,'armature only')
        rig=bpy.data.objects[0];same_rig(reference,signature(rig),name)
        assert rig.name=='Armature' and len(bpy.data.actions)==1,(name,'one action, canonical object')
        action=rig.animation_data.action;start,end=[round(x) for x in action.frame_range]
        assert end-start+1==entry['frames'] and bpy.context.scene.render.fps==30,(name,'timing')
        hips=[];object_matrices=[];first=None;last=None;moving=set()
        for frame in range(start,end+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
            world={p.name:(rig.matrix_world@p.matrix).copy() for p in rig.pose.bones}
            hips.append(world['Hips'].translation.copy());object_matrices.append(rig.matrix_world.copy())
            if first is None:first=world
            for bone,matrix in world.items():
                assert all(math.isfinite(v) for row in matrix for v in row),(name,bone,'finite')
                if max(abs(v-w) for row,ref in zip(matrix,first[bone]) for v,w in zip(row,ref))>.0002:moving.add(bone)
            last=world
        planar=max(Vector((p.x-hips[0].x,p.y-hips[0].y,0)).length for p in hips)
        drift=max(abs(a-b) for mat in object_matrices for row,ref in zip(mat,object_matrices[0]) for a,b in zip(row,ref))
        assert planar<.0001 and drift<.0001 and moving,(name,'drift/static',planar,drift)
        seam_p=max((first[b].translation-last[b].translation).length for b in first)
        seam_r=max(first[b].to_quaternion().rotation_difference(last[b].to_quaternion()).angle for b in first)
        if entry['loop']:assert seam_p<.0001 and seam_r<.002,(name,'loop',seam_p,seam_r)
        result['clips'][name]={'frames':entry['frames'],'fps':30,'loop':entry['loop'],'moving_bones':len(moving),
            'planar_hips_drift_m':planar,'object_drift':drift,'loop_position_error_m':seam_p,'loop_rotation_error_rad':seam_r}
        print('CROWD_CLIP_OK',name,entry['frames'],flush=True)
    result['passed']=True
    (cast.CHARACTERS/'crowd_blender_verification.json').write_text(json.dumps(result,indent=2))
    print('CROWD_BLENDER_VERIFIED',len(result['models']),len(result['clips']),flush=True)


if __name__=='__main__':main()
