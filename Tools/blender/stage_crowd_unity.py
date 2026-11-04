"""Copy verified delivery into the isolated crowd Unity project."""
import json
import shutil
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
CHARACTERS=ROOT/'Staging/art_hq_fbx/Characters'
ANIMS=ROOT/'Staging/art_hq_fbx/CharacterAnims'
PROJECT=ROOT/'Staging/characters/_scratch/crowd/unity_validation'


def main():
    evidence=json.loads((CHARACTERS/'crowd_blender_verification.json').read_text())
    assert evidence['passed'] and len(evidence['models'])==60 and len(evidence['clips'])==31
    assert (PROJECT/'ProjectSettings/ProjectVersion.txt').is_file()
    stats=json.loads((CHARACTERS/'cast_stats.json').read_text())
    manifest=json.loads((ANIMS/'clip_manifest.json').read_text())
    contract={'models':[],'clips':[]}
    for role,entry in stats.items():
        target=PROJECT/'Assets/Cast'/role
        target.mkdir(parents=True,exist_ok=True)
        for file in (CHARACTERS/role).iterdir():
            if file.suffix in {'.fbx','.png'}:shutil.copy2(file,target/file.name)
        contract['models'].append({'role':role,'triangles':[entry['lod_triangles'][f'LOD{lod}'] for lod in range(3)],
            'zeroTriangles':[evidence['models'][role+(f'_LOD{lod}' if lod else '')]['near_zero_area_triangles'] for lod in range(3)]})
    target=PROJECT/'Assets/CharacterAnims'
    target.mkdir(parents=True,exist_ok=True)
    for name,entry in manifest.items():
        shutil.copy2(ANIMS/(name+'.fbx'),target/(name+'.fbx'))
        contract['clips'].append({'name':name,'frames':entry['frames'],'loop':entry['loop']})
    target=PROJECT/'Assets/Editor'
    target.mkdir(parents=True,exist_ok=True)
    shutil.copy2(ROOT/'Tools/blender/ValidateCrowd.cs',target/'ValidateCrowd.cs')
    (target/'validation_contract.json').write_text(json.dumps(contract,indent=2))
    print('CROWD_UNITY_STAGED',len(contract['models'])*3,len(contract['clips']),PROJECT)


if __name__=='__main__':main()
