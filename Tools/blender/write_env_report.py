"""Write delivery documentation from actual final exports and passed checks."""
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Staging/art_env'
NAMES=('NhaRong','SaigonStreet','Marseille')
LABELS={'NhaRong':'Bến Nhà Rồng · trưa 5/6/1911',
        'SaigonStreet':'Phố Sài Gòn · tối 1911',
        'Marseille':'Marseille · bình minh 6/7/1911'}
SCRIPTS={'NhaRong':'make_nharong.py','SaigonStreet':'make_saigon_street.py','Marseille':'make_marseille.py'}


def load(p):
    return json.loads(p.read_text())


def main():
    unity=load(ART/'unity_verification.json')
    deterministic=load(ART/'determinism.json')
    assert unity['status']=='PASS' and deterministic['status']=='PASS'
    inputs=load(ART/'read_only_inputs.json')
    assert all(hashlib.sha256((ROOT/n).read_bytes()).hexdigest()==sha for n,sha in inputs.items())
    native={r['scene']:r for r in unity['scenes']}
    title='# Ba môi trường 1911 — Hai Bàn Tay\n\n'
    title+='Tất cả file giao nằm trong `Staging/art_env/`; scripts mới nằm trong `Tools/blender/`. Không mở project `Game/`, không sửa builder hay `make_ship.py`, không chạy git.\n\n'
    title+='| Khu | FBX | Layout | Xem trước / số đo / anchors | Tam giác (gồm COL) | Mesh objects | Vật liệu | NPC dự kiến |\n|---|---|---|---|---:|---:|---:|---:|\n'
    preview_names=('preview_overview.png','preview_spawn.png','preview_beat.png','preview_top.png')
    all_mats={}
    dimensions='\n## Số đo chính\n\n| Khu | Phần | X × Y × Z (m) | Cơ sở |\n|---|---|---|---|\n'
    anchor_tables='\n## Danh sách anchors đầy đủ\n\nMọi tọa độ dưới đây là Unity world, mét, Y lên. `ship`/`ship_task` tham chiếu tàu có sẵn.\n\n'
    preview_links='\n## Đường dẫn ảnh xem trước\n\n| Khu | Toàn cảnh | Spawn | Beat chính | Mặt bằng |\n|---|---|---|---|---|\n'
    for name in NAMES:
        d=ART/name
        stats=load(d/'stats.json'); layout=load(d/'layout.json'); result=native[layout['scene']]
        assert result['fbxSha256']==hashlib.sha256((d/(name+'.fbx')).read_bytes()).hexdigest(), 'Unity did not check final FBX'
        assert load(d/'fbx_roundtrip.json')['status']=='PASS'
        assert not stats['checks']['route_obstruction_failures']
        assert all((d/p).is_file() for p in preview_names)
        title+=f"| {LABELS[name]} | [{name}.fbx]({name}/{name}.fbx) | [layout.json]({name}/layout.json) | [README]({name}/README.md) | {stats['triangles']:,} | {stats['objects']} | {stats['material_count']} | {layout['crowd']['capacity']} |\n"
        preview_links+=f"| {name} | "+' | '.join(f'[{label}]({name}/{file})' for label,file in zip(('overview','spawn','beat','top'),preview_names))+' |\n'
        for m in layout['measurements']:
            dimensions+=f"| {name} | `{m['name']}` | "+' × '.join(f'{v:g}' for v in m['size_m'])+f" | {m['basis']} |\n"
        anchor_tables+=f"### {name} ({len(layout['anchors'])} anchors)\n\n| Tên | Unity world [X,Y,Z] m | Vai trò |\n|---|---|---|\n"
        for n,p in layout['anchors'].items():
            anchor_tables+=f"| `{n}` | `[{', '.join(f'{v:g}' for v in p)}]` | {layout['anchor_roles'][n]} |\n"
        anchor_tables+='\n'
        text=f'# {LABELS[name]}\n\n'
        text+=f"[{name}.fbx]({name}.fbx) · [layout.json](layout.json) · [{name}.blend]({name}.blend) · [stats.json](stats.json)\n\n"
        text+='Blender `.blend` chứa riêng môi trường; tàu, cầu thang và ánh sáng xem trước không nằm trong FBX hoặc blend giao.\n\n'
        text+='## Ảnh cuối\n\n'
        for label,filename in zip(('Toàn cảnh','Tầm mắt tại spawn','Tầm mắt tại beat chính','Mặt bằng: anchors, crowd paths, blockers'),preview_names):
            text+=f'[{label}]({filename})\n\n![{label}]({filename})\n\n'
        text+='Ảnh tàu là FBX LOD1 hiện có đặt đúng ShipConfig để đối chiếu, chỉ có trong render. Không phải phần môi trường xuất. Mặt bằng lấy projection từ camera Blender, +X sang phải, +Z lên trên ảnh; mọi số là Unity world metres.\n\n'
        text+='## Kích thước chính\n\n| Phần | X × Y × Z (m) | Cơ sở |\n|---|---|---|\n'
        for m in layout['measurements']:
            size=' × '.join(f'{n:g}' for n in m['size_m'])
            text+=f"| `{m['name']}` | {size} | {m['basis']} |\n"
        text+='\nChiều cao âm là tọa độ thế giới, không phải lỗi scale. Số đo kiến trúc là ước lượng từ tỷ lệ ảnh; kích thước gameplay và chỗ tàu là quy ước scene.\n\n'
        text+=f"Spawn: `{layout['player_spawn']['pos']}`, yaw `{layout['player_spawn']['yaw']}°`.\n\n"
        if 'ship' in layout:
            ship=layout['ship']
            text+=f"Tàu: `{ship['position']}`, yaw `{ship['yaw']}°`, dài 124,1 m, rộng 15,2 m. Gangway: `{ship['gangway_top']}` → `{ship['gangway_foot']}`. Kè ở mạn phải (−Z).\n\n"
        text+='## Anchors\n\n| Tên | Unity world [X,Y,Z] m | Vai trò |\n|---|---|---|\n'
        for n,p in layout['anchors'].items():
            text+=f"| `{n}` | `[{', '.join(f'{v:g}' for v in p)}]` | {layout['anchor_roles'][n]} |\n"
        text+='\n`walk` là điểm đứng; `seat` là chân ghế; `object` là điểm đặt/nhìn vật. `ship`/`ship_task` là tham chiếu đồ trên tàu có sẵn, không thêm đồ bếp hoặc đồ phát lương vào FBX môi trường.\n\n'
        if 'ship_anchor_sources' in layout:
            text+='Anchors bếp dùng ma trận `deck_zones.galley_coordinate_transform`, cộng ShipConfig. Các trạm lấy từ `GalleySceneBuilder.cs`, `ShipInterior.cs`; thang hầm lấy đúng endpoints của `hold_stairs`. `galley_storm_pot_*` ghi offset cục bộ dự kiến trên bàn nồi; không xác nhận task runtime hiện tại.\n\n'
        text+='## Crowd và mặt đi\n\n'
        text+=f"Capacity khai báo: **{layout['crowd']['capacity']} NPC**. Đây là chỗ/tuyến để hệ crowd tiêu thụ, không phải NPC được nhúng vào FBX.\n\n"
        for p in layout['crowd']['paths']:
            text+=f"- `{p['name']}`: {p['count']} người, {'loop' if p['loop'] else 'shuttle / một chiều'}, {len(p['points'])} điểm.\n"
        text+='\n'
        for p in layout['crowd']['spots']:
            text+=f"- `{p['name']}`: {p['count']} người.\n"
        text+='\nNền collider liên tục; sidewalk/hiên chỉ cao 0,10–0,15 m. Biên có hàng rào nhìn thấy, nhà, kho hoặc xe/kiện hàng. `COL_*` chỉ box; phải tắt renderer và tạo BoxCollider từ local mesh bounds. Không bật collider tự động cho toàn bộ mesh hiển thị.\n\n'
        text+='## Thống kê và kiểm\n\n'
        text+=f"{stats['objects']} mesh objects + 1 empty root; {stats['visual_objects']} hiển thị, {stats['collider_objects']} COL. {stats['visual_triangles']:,} tam giác hiển thị + {stats['collider_triangles']:,} tam giác COL = **{stats['triangles']:,}**. {stats['material_count']} tên vật liệu.\n\n"
        text+=f"Linked mesh duplicates: `{json.dumps(stats['linked_meshes'])}`. Unity giữ data theo import của từng mesh; chưa thiết lập GPU instancing/LOD runtime. Phông xa được giảm hình học; chưa cần file LOD riêng với tổng hiện tại.\n\n"
        text+=f"Unity **{result['unityVersion']}**: PASS; sai số bounds lớn nhất `{result['maxBoundsErrorMetres']:.7f} m`, {result['colliders']} BoxColliders, {result['physicsRouteSegments']} đoạn capsule cast, {result['floorRaycasts']} raycast nền. [Kết quả Unity](../unity_verification.json), [FBX roundtrip](fbx_roundtrip.json), [tất định](../determinism.json).\n\n"
        text+='Các cast dùng bán kính 0,30 m, chiều cao 1,75 m; raycast nền cách tối đa 0,5 m trên các tuyến beat/crowd khai báo. Tàu và ramp đối chiếu được import/dựng riêng trong scratch. Kết quả này không phải playtest narrative hoặc chứng nhận toàn bộ nội thất tàu.\n\n'
        text+='## Unity transform, UV, vật liệu\n\n'
        text+='Đặt root FBX tại **position (0,0,0), rotation (0,180,0), scale (1,1,1)**. ModelImporter: globalScale=1, useFileUnits=true, bakeAxisConversion=false; giữ import rotation mặc định trước khi prepend yaw 180°. Toàn bộ layout đã là world coords; không xoay/scale các anchors theo root lần nữa.\n\n'
        text+='Một UV unit = 1 m. Với texture lặp một ô `t` m, đặt BaseMap/NormalMap tiling `(1/t,1/t)`. Không auto-unwrap hoặc normalize UV vào 0–1. Bảng vật liệu trong [README chung](../README.md).\n\n'
        text+='[Phân loại 20 ảnh và phỏng đoán](../REFERENCES.md) · [hai vòng sửa bằng ảnh](../VISUAL_REVIEW.md).\n\n'
        if name=='NhaRong':
            text+='Ước lượng: Nhà Rồng 34 × 15 m, hiên 9 nhịp, hai tầng + vent mái; vị trí kho/cây/xe/kè được sắp cho gameplay. Rồng là điêu khắc procedural, chưa phải bản sao scan. Ảnh hiện đại chỉ lấy khối tòa nhà. Bờ đối diện/nhà sàn/tàu xa là proxy.\n'
        elif name=='SaigonStreet':
            text+='Toàn bộ mặt bằng, địa chỉ quán, cửa hàng, cây và phương tiện là phỏng đoán, không nhận đây là một đoạn Catinat/Charner được đo đạc. Đèn điện hỗ trợ S16; không thêm tàu điện cho phố Sài Gòn. Chữ màn chiếu là runtime; chỉ giao anchor `screen_facade`.\n'
        else:
            text+='Cảng thương mại Joliette và mặt tiền Vieux-Port được phối hợp cho tuyến E3 → E4 → E5. Quán thấp nằm trong khoảng nhìn ra nhà thờ; các nhà còn lại 4–5 tầng. Đồi/nhà thờ đặt theo bố cục sân khấu, không theo khoảng cách địa lý. Tàu điện di chuyển, waiter, đồ phát lương và NPC do runtime/ship dựng; môi trường giao ray và anchors.\n'
        (d/'README.md').write_text(text)
        all_mats.update(stats['surface_tiles_m'])
    title+=preview_links+dimensions+anchor_tables
    title+='\n## Đặt trong Unity\n\n'
    title+='**Root position `(0,0,0)`, rotation Euler `(0,180,0)`, scale `(1,1,1)`** cho cả ba FBX. Import globalScale=1, useFileUnits=true, bakeAxisConversion=false. Prepend yaw 180° vào rotation mặc định của prefab import, như model tàu. Layout đã là Unity world, Y lên, mét.\n\n'
    title+='Blender authoring `(x,y,z) = (Unity.X,Unity.Z,Unity.Y)` có reflection, đã đảo winding khi dựng mesh. Export giữ đúng flags của `make_ship.py`: selection, MESH/EMPTY, RELATIVE, apply_unit_scale, bake_space_transform, −Z forward, Y up, modifiers, no leaf bones, no embedded textures. Yaw 180° bù hướng X/Z của Unity importer; không đổi scale.\n\n'
    title+='Scene kiểm thật: `Staging/_unity_scratch/Assets/VerifiedScenes/00_Prologue.unity`, `01_NhaRong.unity`, `03_Marseille.unity`. Chỉ mở project scratch. Editor đúng bản trong `Tools/unity.sh`: **6000.3.25f1**, URP 17.3.0. [Kết quả máy đọc](unity_verification.json), [log Unity](unity_import.log).\n\n'
    title+='Mỗi COL là box có mesh hiển thị đối ứng; validator thêm BoxCollider, tắt renderer COL và chạy Unity Physics. Kiểm tất cả bounds từng object với dung sai 3 mm, số tam giác, tên slot, normals/UV, mặt nền các tuyến, điểm chân cầu thang mạn phải. Không chỉ kiểm qua Blender. Các builder trong `Game/` vẫn chưa được tích hợp các asset này.\n\n'
    title+='## Rebuild\n\n```bash\n'
    title+='export PYTHONDONTWRITEBYTECODE=1 PYTHONHASHSEED=0 OMP_NUM_THREADS=1\n'
    for name in NAMES:
        title+=f'blender -b --factory-startup --python-exit-code 1 -P Tools/blender/{SCRIPTS[name]} -- --pass 2\n'
    title+='```\n\n'
    title+='`--pass 0`/`--pass 1` lưu vào `_iterations/pass_0`/`pass_1`; default là pass 2. `--geometry-only` bỏ render, vẫn xuất FBX/layout/stats. `--verify-fbx` import lại file FBX thật vào Blender, kiểm world bounds/slot/UV/tam giác. Mỗi lượt final render Cycles CPU một luồng, seed 1911, 24 samples cố định, 1280×720; bản đồ có phần chú giải riêng.\n\n'
    title+='Sau export: `python3 Tools/blender/prepare_env_unity.py`; rồi chạy editor đúng bản với `-batchmode -nographics -projectPath <repo>/Staging/_unity_scratch -executeMethod VerifyEnvironments.Run -logFile <repo>/Staging/art_env/unity_import.log`. Không chạy `Tools/unity.sh` vì script đó trỏ project chính.\n\n'
    title+='[determinism.json](determinism.json) ghi SHA-256 của 27 file FBX, layout, stats và ảnh được so sau hai lần chạy final độc lập. Timestamp FBX cố định, ID FBX dùng SHA-256 từ tên ổn định; loại metadata thời gian render khỏi PNG nhưng giữ nguyên pixels. RNG local và Cycles seed 1911; không network trong rebuild. `.blend` là bản chỉnh sửa tiện dụng, kiểm geometry fingerprint; không cam kết byte identity của container Blender.\n\n'
    title+='Để kiểm lại: sau lần build đầy đủ thứ nhất, chạy `python3 Tools/blender/verify_env_determinism.py --snapshot`; build lại cả ba bằng cùng lệnh, rồi chạy script không có `--snapshot`. Script so toàn bộ 27 SHA-256 và dừng nếu khác.\n\n'
    title+='## Vật liệu và texture\n\n'
    title+='Slot tên trùng thư mục surface có sẵn. Texture Color/NormalGL 2K được **copy** từ Game sang `_textures/`; không tải texture mới. Shader preview/scratch sử dụng color, normal, roughness hằng số và metallic cho sắt/đồng. Có UV mét nên cần material tiling dưới đây khi tích hợp. `ArtImporter.cs` hiện chỉ remap nhóm ship; môi trường cần bổ sung importer ở write phase khác.\n\n'
    title+='| Slot | Một ô texture (m) | Tiling | Nguồn CC0 của texture có sẵn |\n|---|---:|---:|---|\n'
    sources={}
    for line in (ROOT/'Staging/art_hq/MANIFEST.md').read_text().splitlines():
        if line.startswith('| Surface |'):
            cols=[p.strip() for p in line.split('|')]
            sources[cols[2]]=cols[5]
    for name,tile in sorted(all_mats.items()):
        title+=f'| {name} | {tile:g} | {1/tile:g} | [{name}]({sources[name]}) |\n'
    title+=f"| DeckPlanks (tàu/gangway đối chiếu) | 2 | 0.5 | [DeckPlanks]({sources['DeckPlanks']}) |\n"
    title+='\nNguồn license dựa trên manifest asset đã có; dữ liệu dùng CC0 Poly Haven/ambientCG. Không đưa ảnh tham chiếu hoặc watermark vào texture. Mesh procedural do script tạo.\n\n'
    title+='Tên vật liệu mới (màu/procedural, không texture ngoài): `Foliage`, `FoliageLight`, `WindowDark`, `CanvasRed`, `CanvasIvory`, `WaterRiver`, `WaterHarbor`, `LampGlow`, `StatueGold`, `CafeGlass`, `HorseCoat`, `Collision`. Water shader là preview bump đơn giản; Unity scratch dùng vật liệu URP Lit proxy. `Collision` tắt renderer.\n\n'
    title+='## Tham chiếu, phỏng đoán, giới hạn\n\n'
    title+='[REFERENCES.md](REFERENCES.md) phân loại từng ảnh: #9/#10 là sau 2000 và chỉ dùng hình khối Nhà Rồng; #5 là ảnh hiện đại chụp bảng có ảnh cũ; #8 ghi 1882; #20 được LOC ghi 1890–1900. Các ảnh khác phần lớn chưa xác minh năm, không gán bừa vào 1900–1915. Cầu transbordeur chỉ cho giới hạn 1905–1944.\n\n'
    title+='Các số đo và vị trí ngoài tàu là ước lượng gameplay. Phố Sài Gòn là giả định; Marseille phối Joliette/Vieux-Port, đồi/nhà thờ được nén cho silhouette. Lá cây, ngựa, tàu xa là mô hình stylized; không phải photogrammetry. Mặt tiền dựng để nhìn/đi ngoài hiên, không có nội thất cửa hàng/tòa nhà.\n\n'
    title+='Đã kiểm asset import và tuyến khai báo; chưa playtest toàn game, bake lighting, NavMesh, đo FPS/GPU instancing trên máy demo, hoặc chuyển NPC/beat runtime. Tàu/cầu thang là owner scene, không nằm trong FBX môi trường; cần giữ chức năng thu gangway/nhổ neo. Đồ phát lương và trạm bếp chỉ là anchors tham chiếu. Nội thất tàu nằm ngoài asset import này.\n\n'
    title+='[VISUAL_REVIEW.md](VISUAL_REVIEW.md) lưu hai vòng sửa sau ảnh; `_iterations/` giữ ảnh cũ để đối chiếu. Không dùng subagent. Root thực hiện toàn bộ code/export/test; không đụng `Staging/art_hq_fbx/` hoặc `Staging/characters/`.\n'
    (ART/'README.md').write_text(title)
    print('Wrote README.md and three per-environment reports.')


if __name__=='__main__':
    main()
