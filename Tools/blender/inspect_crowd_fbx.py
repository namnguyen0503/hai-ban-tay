"""Compare raw FBX polygons with Blender imports; do not edit delivery."""
import json
import sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
from io_scene_fbx import parse_fbx as parse
import prepare_cast as cast
import prepare_crowd as crowd


def raw_geometry(path):
    root,_=parse.parse(str(path))
    objects=next(e for e in root.elems if e.id==b'Objects')
    triangles=zero=repeated=duplicates=0
    for mesh in objects.elems:
        if mesh.id!=b'Geometry' or mesh.props[-1]!=b'Mesh':continue
        values=next(e for e in mesh.elems if e.id==b'Vertices').props[0]
        vertices=[Vector(values[i:i+3]) for i in range(0,len(values),3)]
        indices=next(e for e in mesh.elems if e.id==b'PolygonVertexIndex').props[0]
        face=[];seen=set()
        for value in indices:
            face.append(value if value>=0 else -value-1)
            if value>=0:continue
            triangles+=len(face)-2
            repeated+=len(set(face))!=len(face)
            key=tuple(sorted(face))
            duplicates+=key in seen;seen.add(key)
            if len(face)==3:
                a,b,c=[vertices[i] for i in face]
                zero+=((b-a).cross(c-a).length<1e-10)
            face=[]
    return {'triangles':triangles,'zero_area_triangles':zero,'repeated_index_faces':repeated,'duplicate_faces':duplicates}


def main():
    stats=json.loads((cast.CHARACTERS/'cast_stats.json').read_text());results={}
    for role,entry in stats.items():
        for lod in [0,1,2]:
            name=role+(f'_LOD{lod}' if lod else '')
            path=cast.CHARACTERS/role/(name+'.fbx')
            raw=raw_geometry(path)
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
            imported=sum(crowd.triangles(o) for o in bpy.data.objects if o.type=='MESH')
            raw.update({'pre_export_stat':entry['lod_triangles'][f'LOD{lod}'],'blender_imported':imported})
            results[name]=raw
            if raw['pre_export_stat']!=imported or raw['triangles']!=imported:print('FBX_COUNT_DIFFERENCE',name,raw,flush=True)
    (crowd.SCRATCH/'raw_fbx_counts.json').write_text(json.dumps(results,indent=2))
    print('RAW_FBX_INSPECTED',len(results),flush=True)


if __name__=='__main__':main()
