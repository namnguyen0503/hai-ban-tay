"""Read-only diagnostic for FBX export determinism."""
from pathlib import Path
from io_scene_fbx import parse_fbx
ROOT=Path(__file__).resolve().parents[2]
for name in ('NhaRong','SaigonStreet','Marseille'):
    first,_=parse_fbx.parse(str(ROOT/'Staging/_unity_scratch/Assets/Environments'/name/(name+'.fbx')))
    second,_=parse_fbx.parse(str(ROOT/'Staging/art_env'/name/(name+'.fbx')))
    def section(root,id):
        return next(e for e in root.elems if e.id==id)
    a,b=section(first,b'Objects'),section(second,b'Objects')
    keys=lambda section:[(e.id,tuple(e.props[:2])) for e in section.elems]
    print(name,'same ordered IDs:',keys(a)==keys(b),'same ID set:',set(keys(a))==set(keys(b)),flush=True)
    amap={e.props[0]:e for e in a.elems}; bmap={e.props[0]:e for e in b.elems}
    def diff(a,b,path):
        if a.id!=b.id or list(a.props)!=list(b.props):
            return path+' '+str(a.props[:2])[:110]+' / '+str(b.props[:2])[:110]
        if len(a.elems)!=len(b.elems): return path+' child-count'
        for x,y in zip(a.elems,b.elems):
            d=diff(x,y,path+'/'+x.id.decode())
            if d:return d
    print('header difference:',diff(section(first,b'FBXHeaderExtension'),section(second,b'FBXHeaderExtension'),'header'),flush=True)
    shared=[key for key in amap if key in bmap]
    changes=[diff(amap[key],bmap[key],str(key)) for key in shared]
    print('object content differences:',[d for d in changes if d][:6],flush=True)
    print('connections equal:',section(first,b'Connections')==section(second,b'Connections'),flush=True)
