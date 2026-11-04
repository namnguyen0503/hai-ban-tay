"""Write delivery documentation/hashes only after both import checks pass."""
import hashlib
import json
import shutil
import struct
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
CHARACTERS=ROOT/'Staging/art_hq_fbx/Characters'
ANIMS=ROOT/'Staging/art_hq_fbx/CharacterAnims'
SCRATCH=ROOT/'Staging/characters/_scratch/crowd'
OLD=['Cook','Helper','SoldierA','SoldierB','Jacques']
NEW_CLIPS=['ShoulderCarry','ShoulderIdle','BagWalk','BagIdle','Documents','HandOver','Wave','LeanRail',
    'Squat','SitChair','Drink','Talk2','Listen','TrayCarry','Sweep','Pull']
OLD_NOTES={
    'Cook':'Original vest, rolled cuffs, long bib apron and soft cap; LOD0 preserved.',
    'Helper':'Original work shirt and waist apron; LOD0 preserved.',
    'SoldierA':'Original dark suit; LOD0 preserved, including two source zero-area triangles.',
    'SoldierB':'Original oatmeal top and brown cloth cap; LOD0 preserved.',
    'Jacques':'Original pale shirt, navy trousers and watch cap; LOD0 preserved.',
}
SCRIPTS=['prepare_cast.py','export_anims.py','verify_cast.py','ValidateCast.cs',
    'prepare_crowd.py','export_crowd_anims.py','verify_crowd.py','ValidateCrowd.cs','stage_crowd_unity.py',
    'finalize_crowd.py','run_crowd_unity.py','inspect_crowd.py','diagnose_crowd.py','inspect_crowd_motion.py','inspect_crowd_fbx.py']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    if "--feet12" in sys.argv:
        # Luồng chân chỉ xác nhận bản sinh trong worktree, tuyệt đối không ghi Staging.
        output = ROOT / "Tools/blender/generated/Characters"
        report = json.loads((ROOT / "Logs/feet12_blender/verification.json").read_text())
        assert report["passed"] and all(role in report["roles"] for role in ["PorterVN_A", "PorterVN_B", "PorterVN_C"])
        files = {}
        for role, entry in report["roles"].items():
            if "legs" in entry:
                assert entry["after_triangles"] < entry["before_triangles"]
                assert len(entry["legs"]) == 2 and all(x["connected_components"] == 1 and x["ankle_holes"] == 0 for x in entry["legs"])
            else:
                assert entry["after_triangles"] <= entry["before_triangles"] + 256
            for lod in ["", "_LOD1", "_LOD2"]:
                path = output / role / (role + lod + ".fbx")
                assert path.stat().st_size > 1000
            for path in (output / role).iterdir():
                files[str(path.relative_to(ROOT))] = digest(path)
        (ROOT / "Logs/feet12_delivery_sha256.json").write_text(json.dumps(files, indent=2))
        print("FEET12_FINALIZED", len(files), "tệp; bằng chứng Unity sẽ ghi trong feet12_report.md")
        return
    blender=json.loads((CHARACTERS/'crowd_blender_verification.json').read_text())
    unity=json.loads((SCRATCH/'unity_validation.json').read_text())
    process=json.loads((SCRATCH/'unity_process.json').read_text())
    assert blender['passed'] and unity['passed']
    assert process['exit_code']==0 and process['fresh_report'] and process['report_passed']
    assert process['report_sha256']==digest(SCRATCH/'unity_validation.json')
    assert len(blender['models'])==len(unity['models'])==60
    assert len(blender['clips'])==len(unity['clips'])==31
    assert unity['sharedAvatarPlaybackSamples']==20*31*9*3
    assert unity['lodGroups']==unity['commonSkeletons']==20
    shutil.copy2(SCRATCH/'unity_validation.json',CHARACTERS/'crowd_unity_verification.json')
    shutil.copy2(SCRATCH/'unity_process.json',CHARACTERS/'crowd_unity_process.json')
    stats=json.loads((CHARACTERS/'cast_stats.json').read_text())
    manifest=json.loads((ANIMS/'clip_manifest.json').read_text())
    stats['PassengerFR_M2']['notes']='Dark travel suit, plain shirt and felt fedora, for Marseille.'
    all_textures={}
    for role,entry in stats.items():
        entry['unity_lod_triangles']={f"LOD{m['lod']}":m['triangles'] for m in unity['models'] if m['role']==role}
        sizes={}
        for filename in entry['textures']:
            data=(CHARACTERS/role/filename).read_bytes()
            assert data[:8]==b'\x89PNG\r\n\x1a\n'
            size=list(struct.unpack('>II',data[16:24]));assert 0<max(size)<=2048,(role,filename,size)
            sizes[filename]=size
            all_textures[f'{role}/{filename}']=size
        entry['texture_sizes']=sizes
    (CHARACTERS/'cast_stats.json').write_text(json.dumps(stats,indent=2))
    reviews={
        'character_passes':[
            {'pass':1,'inspected':['crowd_preview','lod_preview'],'finding':'Canonical edit-bone direction was lost when assigning a matrix to a zero-length bone; meshes deformed badly.',
                'correction':'Set bone length before matrix; assert all 52 canonical rest matrices.'},
            {'pass':2,'inspected':['crowd_detail1','crowd_detail2','crowd_detail3','lod_preview'],
                'finding':'Bind fixed; polygon-center recoloring left jagged cloth edges and source chef parts remained.',
                'correction':'Recolor source UV islands, replace female chef clothing, fit captain beard to source face; expand preview framing.'},
            {'pass':3,'inspected':['crowd_detail1','crowd_detail2','crowd_detail3','lod_preview'],
                'finding':'Cloth atlas improved; joined old LODs showed wrong UV colors, barefoot models lacked clear toes, female waist/cap remnants remained.',
                'correction':'Unify active UV layer names before joining, add bare toes, rebuild female waist/trousers and crop source cap.'},
            {'pass':4,'inspected':['lod_preview','crowd_detail2','crowd_detail3'],
                'finding':'Cook LOD colors now consistent; a thin white chef-cap band still showed on female heads.',
                'correction':'Lower source cap crop to 1.726 m and fit replacement black hair.'},
            {'pass':5,'inspected':['crowd_preview','crowd_detail1','crowd_detail2','crowd_detail3','lod_preview'],
                'finding':'Final costumes/UVs inspected; female cap band removed. LOD2 loses small face/cloth detail, with minor cloth intersections visible at close distance.',
                'correction':'Use LOD2 at crowd distance; record linear-skinning/cloth limits.'},
            {'pass':6,'inspected':['crowd_preview','crowd_detail1','crowd_detail2','crowd_detail3','lod_preview'],
                'finding':'Unity rejected cap n-gons that contained coincident UV-seam endpoints; no successful Unity evidence was claimed at that point.',
                'correction':'Exclude duplicate endpoint from cap polygons and triangulate the affected new LOD0 meshes before FBX export; rebuild LODs and check images again.'},
        ],
        'animation_passes':[
            {'pass':1,'inspected':['anim_preview2'],'finding':'Seated source bind height needed an explicit vertical offset; squat looked up, Drink cup appeared too high.',
                'correction':'Restore seated pelvis offset, replace squat head/neck with Idle, lower Drink sip wrist, add ledger desk and pen guides.'},
            {'pass':2,'inspected':['anim_preview2'],'finding':'Squat and desk contacts improved; Drink still too high and middle Talk2/Listen poses hid gestures.',
                'correction':'Make sip wrist track a posed mouth point; select strongest conversation/nod frames for the contact sheet.'},
            {'pass':3,'inspected':['anim_preview2'],'finding':'Final cup meets mouth; Talk2 gesture and Listen nod visible; all 16 requested behaviors represented.',
                'correction':'Keep final export and preview; independently check every FBX frame and shared-avatar playback.'},
            {'pass':4,'inspected':['anim_preview2'],'finding':'Refresh final pose/contact sheet after the cap topology repair; animation FBXs unchanged from pass 3.',
                'correction':'Retain the same sixteen motions; verify final model appearances in these poses.'},
        ],
        'final_images':{},'texture_files_checked':len(all_textures),'texture_max_dimension':max(max(v) for v in all_textures.values()),
        'review_scope':'Blender images plus sampled Unity Humanoid playback, not exhaustive cloth collision testing.',
    }
    for path in [CHARACTERS/'crowd_preview.png',CHARACTERS/'lod_preview.png',ANIMS/'anim_preview2.png']:
        reviews['final_images'][str(path.relative_to(ROOT))]=digest(path)
    (CHARACTERS/'crowd_visual_review.json').write_text(json.dumps(reviews,indent=2))
    for i in range(1,4):shutil.copy2(SCRATCH/f'crowd_detail{i}_pass6.png',CHARACTERS/f'crowd_detail{i}.png')

    current=(CHARACTERS/'README.md').read_text()
    header='| Output | Frames | Loop | Source file / action / source frames | Authored or edited behavior |'
    old_table=current[current.index(header):].split('Rocketbox rotations are retargeted')[0].strip()
    if '\n## ' in old_table:old_table=old_table.split('\n## ')[0].strip()
    lines=['# Hai Bàn Tay — 1911 cast, crowd LODs and shared Humanoid clips','',
        f"Blender 4.2.23 LTS / Unity {unity['unityVersion']}. Delivered: **20 roles × 3 LODs (60 character FBXs), 31 clips**. This expansion adds 15 roles, 40 LOD FBXs and 16 clips. The five original LOD0 FBXs, their textures, the fifteen original clip FBXs, skeleton.json and four original pipeline scripts retain their baseline SHA-256 hashes.",
        '', 'Characters contain one canonical Armature, skinned meshes and no animation. Clips contain only the same Armature and one baked action. All use the unchanged 52-bone T-pose, meters, FBX Y-up / -Z forward and the original export helper/flags. PNG textures stay beside the FBXs, relative paths, at most 2048×2048.',
        '', '## Roles and LOD counts','',
        'Bases below resolve to the exact source paths in [cast_stats.json](cast_stats.json), relative to `Staging/characters/`. All bases are Microsoft Rocketbox, MIT; retain [the MIT notice](LICENSE_Rocketbox_MIT.txt). The [staged source manifest](../../characters/MANIFEST.md) records pack provenance.',
        '', '| Role | Rocketbox base | LOD0 tris | LOD1 tris | LOD2 tris | PNG files | Costume / notes |',
        '|---|---|---:|---:|---:|---:|---|']
    for role,entry in stats.items():
        c=entry['lod_triangles'];note=entry.get('notes',OLD_NOTES.get(role,''))
        lines.append(f"| [{role}]({role}/{role}.fbx) | `{entry['base']}` | {c['LOD0']:,} | {c['LOD1']:,} | {c['LOD2']:,} | {entry['texture_count']} | {note} |")
    lines+=['', 'Counts are triangles in the delivered FBXs / Blender re-import. PNG count is distinct delivered texture files, shared by all three LODs; some source/procedural atlases remain unused in a particular material slot. Unity may discard inherited zero-area triangles; exact imported counts appear in `unity_lod_triangles` and [crowd_unity_verification.json](crowd_unity_verification.json). Duplicate triangles produced by Decimate on Purser/Waiter LOD2 were removed before export.',
        '', 'LOD1 is about 50%, LOD2 about 17.5%. All LODs retain all 52 bones, including fingers. New LOD0s and every LOD1/LOD2 have one joined SkinnedMeshRenderer. Original five LOD0s retain their original mesh objects. Active UV channels are unified before joining so the old apron/skin atlases remain correct.',
        '', 'The seven staged bases contain no verified Vietnamese/Asian male base. Vietnamese roles use plain darker skin where appropriate and black hair; facial geometry is still derived from the selected Rocketbox base. Both female roles use Chef_Female_01 transferred onto the identical canonical skeleton, with chef cap/apron replaced. Mai/Le are interpretive game representations, not authenticated portraits or exact age likenesses. Le uses the requested white student option to read distinctly from CivilianVN’s dark áo dài; this does not assert a documented Chasseloup-Laubat uniform.',
        '', 'New apron, áo dài panels, blouse waist and long skirt use normalized torso/pelvis/thigh skin weights, at most four positive influences per vertex; trousers also use calf weights. Headwear, nón lá, beard, glasses and hair follow Head. Nón lá is a shallow cone; scarves follow UpperChest. Source belts/logos/modern shoe trim are recolored or assigned plain leather; no new logos or insignia.',
        '', '## New sixteen clips','',
        'Every clip is 30 fps, frames 1..N inclusive; duration `(N-1)/30` seconds. All are in place: armature and planar Hips drift removed. Vertical body movement remains. Loops have blended seams and exactly duplicate first/last poses. HandOver and Wave are one-shots.',
        '', 'All new sources are **Microsoft Rocketbox, MIT**. `R-static` = `MicrosoftRocketbox_Animations/Assets/Animations/all_animations_max_motextr_static/`; `R-xy` = the corresponding `all_animations_max_motextr_xy/` directory. Filenames below include source frame ranges; [clip_manifest.json](../CharacterAnims/clip_manifest.json) records full paths, imported action names, licenses and edits.',
        '', '| New output | Frames | Loop | Rocketbox sources / source frames | Adaptation |',
        '|---|---:|---|---|---|']
    for name in NEW_CLIPS:
        entry=manifest[name]
        source=[]
        for item in entry['sources']:
            folder='R-xy' if '_motextr_xy/' in item['file'] else 'R-static'
            a,b=item['source_frames'];source.append(f"{folder} / `{Path(item['file']).name}` {a}..{b}")
        lines.append(f"| [{name}.fbx](../CharacterAnims/{name}.fbx) | {entry['frames']} | {'Yes' if entry['loop'] else 'No'} | {' + '.join(source)} | {entry['edits'][1]} |")
    lines+=['', 'Retargeting and export reuse `export_anims.py` / `prepare_cast.py`: canonical character bind axes, local quaternion keys, neutral authoring frame zero excluded from delivery, one active action, no NLA/extra takes, bake step 1, simplification 0, deform bones only, no leaf bones, -Z forward / Y up, FBX_SCALE_UNITS, bake_space_transform, relative external texture paths.',
        '', 'The contact sheet includes separate gray/light prop and table/chair/rail guides. **No sack, suitcase, document, cup, tray, broom, rope, table or chair mesh is included in animation FBXs.** Place game props against the authored contacts or add gameplay IK. Guideline heights: standing ledger table 1.065 m, rail 1.045 m, cafe tabletop 0.785 m, bag wrist 0.963 m, tray wrist 1.445 m. Deep-squat pelvis is about 0.343 m; seated clips restore the static source bind height by lowering pelvis 0.319 m.',
        '', '## Original fifteen clips','',
        'These original FBXs are unchanged. `R-xyz` resolves to `all_animations_max_motextr_xyz/`. `K` is `KayKit_CharacterAnimations_1.1/Animations/fbx/Rig_Medium/`, CC0-1.0. Both [MIT](../CharacterAnims/LICENSE_Rocketbox_MIT.txt) and [CC0](../CharacterAnims/LICENSE_KayKit_CC0.txt) notices remain included.',
        '',old_table,'', '## Skeleton','',
        '`Armature` is the shared object name, not an extra bone. Hips is the root. Source Biped helper/facial bones are excluded; facial skin is merged into Head. [skeleton.json](skeleton.json) retains the original canonical bind matrices, source-name map and retarget bind axes.',
        '', '| Bone | Parent |','|---|---|']
    skeleton=json.loads((CHARACTERS/'skeleton.json').read_text())
    lines += [f"| {b['name']} | {b['parent'] or '—'} |" for b in skeleton['bones']]
    lines+=['', '## Unity import and common LOD hierarchy','',
        '**Preserve Hierarchy** must be enabled for character and animation imports. Rig: Humanoid. Characters: Create From This Model, Import Animation off. Clips: Copy From Other Avatar, Cook Avatar, Import Animation on, compression off during validation. Enable Loop Time according to the manifest; disable it for PickUp, Point, SitDown, StandUp, HandOver and Wave.',
        '', 'Use one Cook Avatar / Animator on the LOD0 root. For each LOD1/LOD2 renderer, assign its mesh/materials, then rebind `bones` and `rootBone` by exact name to the LOD0 hierarchy and discard the imported LOD armature/Animator. Put the renderer arrays in one LODGroup. The isolated Unity validator builds this hierarchy for all 20 roles and animates all three LODs from the same skeleton. The old LOD0s may contain multiple renderers; list all of them in LOD0’s renderer array.',
        '', 'Suggested initial screen-height thresholds used in the test: 0.50 / 0.20 / 0.04. Tune those for game cameras and crowd density. Mesh reduction retains skeletons and material slots; it reduces geometry, not bone evaluation or draw-call count. Assign project URP/Lit materials on integration: BaseColor sRGB, Normal maps marked normal, rough cloth/leather, appropriate hair alpha. No project-specific URP materials/prefabs are written here.',
        '', '## Evidence','',
        '- [crowd_preview.png](crowd_preview.png): all fifteen new roles side by side. [Detail 1](crowd_detail1.png), [detail 2](crowd_detail2.png), [detail 3](crowd_detail3.png) retain readable costume/face detail.',
        '- [lod_preview.png](lod_preview.png): Cook, PorterVN_A, PassengerFR_F at LOD0/1/2.',
        '- [anim_preview2.png](../CharacterAnims/anim_preview2.png): all sixteen new poses, with separate contact guides.',
        '- [crowd_visual_review.json](crowd_visual_review.json): six character image passes / four clip image passes, findings and fixes. Final images tied by SHA-256. Iteration images and logs remain in `_scratch/crowd/`.',
        f"- [crowd_blender_verification.json](crowd_blender_verification.json): 60 character imports / 31 clip imports; all 52 bind matrices and names match, T-pose, skin normalization/max4, relocated textures, exact frame counts/fps, finite every-frame transforms, root drift and loop seam checks; {len(blender['original_files_preserved'])} original files/scripts retained their hashes.",
        f"- [crowd_unity_verification.json](crowd_unity_verification.json): Unity {unity['unityVersion']}, 60 valid auto Humanoid Avatars with the same mapping, 31 Humanoid clips using Cook Avatar, 20 assembled LODGroups / shared skeletons, {unity['sharedAvatarPlaybackSamples']:,} sampled role/clip/time/LOD combinations. Skins finite/bounded; walk variants and Pull have moving feet.",
        '- [crowd_unity_process.json](crowd_unity_process.json): native Unity exit code, fresh-report check and report hash. The launcher uses a separate process session to isolate Editor shutdown from the parent terminal.',
        '- [authoring_checks2.json](../CharacterAnims/authoring_checks2.json): analytic wrist/foot reach and guide heights; thresholds retained during fixes.',
        '- [delivery_sha256.json](delivery_sha256.json): current delivered assets, previews, scripts and evidence hashes. `cast_preview.png`, `anim_preview.png`, `blender_verification.json` and `unity_verification.json` remain evidence from the original five-role batch.',
        '', 'The isolated project is `Staging/characters/_scratch/crowd/unity_validation/`. No `Game/` or `Staging/art_env/` file was written; no git command or subagent was used.',
        '', '## Limits','',
        'Game-distance wardrobe adaptations retain source body/suit/shoe silhouettes and approximate ages/ethnicity. New garment geometry is simple: visible planar coat/apron/blouse sections and low-detail bare feet; close-up historical tailoring and distinct facial likenesses need dedicated art work. LOD2 visibly simplifies faces, hats and fabric and can expose small waist/apron intersections; use it at distance.',
        '', 'Cloth uses linear skinning, without cloth simulation/collision. Skirts/aprons follow thighs and calves, so deep squat, wide steps or seated legs can stretch/intersect fabric. No facial/lip animation, gameplay transitions or dynamic prop/contact IK. Loops close exactly using short seam blends, not full mocap phase matching. Root-neutral walking needs controller translation; seated/squat motions need appropriately placed contacts.',
        '', 'Blender checks every clip frame numerically. Unity checks nine times per clip, per role, per LOD; this establishes Humanoid/skeleton compatibility and bounded skinning, not exhaustive garment collision, prop contact or live URP visual/performance profiling.',
        '', '## Reproduce crowd expansion','',
        'Run from the repository root against the existing, preserved original delivery. Crowd scripts reuse the original helpers without modifying them. Do not rerun the original five-role builders for this expansion.',
        '', '```bash',
        'blender -b --factory-startup --python-exit-code 1 -P Tools/blender/prepare_crowd.py -- --pass 6',
        'blender -b --factory-startup --python-exit-code 1 -P Tools/blender/export_crowd_anims.py -- --pass 3',
        'blender -b --factory-startup --python-exit-code 1 -P Tools/blender/verify_crowd.py',
        'python3 Tools/blender/stage_crowd_unity.py',
        'python3 Tools/blender/run_crowd_unity.py',
        'python3 Tools/blender/finalize_crowd.py','```','',
        'The scratch Unity project was cloned from the previous isolated project’s ProjectSettings/Packages/Library (Unity 6000.3.25f1), with fresh Assets and only ValidateCrowd. `stage_crowd_unity.py` requires a successful Blender report; finalization requires both successful reports. Validators exit nonzero on a failed contract. Crowd working blends, retained renders and failure/repair logs stay under `_scratch/crowd/`. Scripts live in `Tools/blender/`.','']
    (CHARACTERS/'README.md').write_text('\n'.join(lines))
    baseline=json.loads((SCRATCH/'baseline_sha256.json').read_text())
    mutable={str(p.relative_to(ROOT)) for p in [CHARACTERS/'README.md',CHARACTERS/'cast_stats.json',CHARACTERS/'delivery_sha256.json',ANIMS/'clip_manifest.json']}
    for name,value in baseline.items():
        if name not in mutable:assert digest(ROOT/name)==value,(name,'original changed')
    files=[p for folder in [CHARACTERS,ANIMS] for p in folder.rglob('*') if p.is_file() and p.name!='delivery_sha256.json']
    files += [ROOT/'Tools/blender'/name for name in SCRIPTS]
    hashes={str(path.relative_to(ROOT)):digest(path) for path in sorted(files)}
    (CHARACTERS/'delivery_sha256.json').write_text(json.dumps(hashes,indent=2)+'\n')
    print('CROWD_DELIVERY_FINALIZED',len(stats),len(manifest),len(hashes),'files hashed;',len(all_textures),'PNG files <=2K')


if __name__=='__main__':main()
