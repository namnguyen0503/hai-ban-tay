"""Prepare only Staging/_unity_scratch, after final Blender exports exist."""
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'Staging/art_env'
SCRATCH = ROOT/'Staging/_unity_scratch'


def main():
    assert (SCRATCH/'ProjectSettings/ProjectVersion.txt').is_file(), 'Create scratch Unity project first'
    version=(ROOT/'Game/ProjectSettings/ProjectVersion.txt').read_text()
    assert (SCRATCH/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0]==version.splitlines()[0]
    manifest=SCRATCH/'Packages/manifest.json'
    packages=json.loads(manifest.read_text())
    packages['dependencies']['com.unity.render-pipelines.universal']='17.3.0'
    manifest.write_text(json.dumps(packages,indent=2)+'\n')
    scenes=[]
    tiles={}
    for name in ('NhaRong','SaigonStreet','Marseille'):
        source=ART/name
        stats=json.loads((source/'stats.json').read_text())
        layout=json.loads((source/'layout.json').read_text())
        dst=SCRATCH/'Assets/Environments'/name
        dst.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source/(name+'.fbx'),dst/(name+'.fbx'))
        shutil.copyfile(source/'layout.json',dst/'layout.json')
        colliders=layout['blockers']+layout['collision_surfaces']
        model={key:layout[key] for key in ('scene','player_spawn','fbx_transform','ship') if key in layout}
        model['name']=name
        model['objects']=stats['objects_manifest']
        model['colliders']=colliders
        model['floors']=[c['mesh_name'] for c in layout['collision_surfaces']]
        model['anchors']=[{'name':n,'pos':p,'role':layout['anchor_roles'][n]} for n,p in layout['anchors'].items()]
        routes=[]
        for p in layout['crowd']['paths']:
            routes.append({'name':p['name'],'points':[{'pos':q} for q in p['points']+([p['points'][0]] if p['loop'] else [])]})
        for p in layout['beat_routes']:
            routes.append({'name':p['name'],'points':[{'pos':layout['anchors'][n]} for n in p['anchors']]})
        for p in layout['crowd']['spots']:
            if 'points' in p:
                routes.append({'name':p['name'],'points':[{'pos':q} for q in p['points']]})
        model['routes']=routes
        scenes.append(model)
        tiles.update(stats['surface_tiles_m'])
    texture_dst=SCRATCH/'Assets/Surfaces'
    shutil.copytree(ART/'_textures',texture_dst,dirs_exist_ok=True)
    ship_dst=SCRATCH/'Assets/ReferenceShip'
    ship_dst.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(ROOT/'Game/Assets/_Project/Art/Models/Ship/AmiralLatoucheTreville_LOD1.fbx',ship_dst/'Ship.fbx')
    contract={'scenes':scenes,'tiles':[{'name':n,'metres':m} for n,m in sorted(tiles.items())]}
    (SCRATCH/'Assets/import_contract.json').write_text(json.dumps(contract,indent=2)+'\n')
    hashes=json.loads((ART/'read_only_inputs.json').read_text())
    assert all(hashlib.sha256((ROOT/n).read_bytes()).hexdigest()==sha for n,sha in hashes.items()),'Read-only input changed'
    print('Scratch import contract prepared:',[(s['name'],len(s['objects'])) for s in scenes])


if __name__=='__main__':
    main()
