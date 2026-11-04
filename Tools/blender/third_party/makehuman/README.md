# Nguồn chân trần feet12

`base.obj` (basemesh hm08) lấy từ MakeHuman chính thức, khóa commit trong `provenance.json`; giấy phép CC0 1.0 trong `LICENSE.ASSETS.md`. Các tệp tải có URL, SHA-256 và dung lượng để kiểm nguồn. Không cần cài MakeHuman/MPFB hoặc package Unity.

`prepare_crowd.py --feet12` chỉ đọc phần body, bỏ helper, dùng nửa chân trái rồi đối xứng, cắt chân tại 39,2 cm để giấu trong cuff, fit mắt cá/knee vào skeleton Rocketbox hiện có. Chân giữ gót, vòm, mu và ngón nối liền từ mesh nguồn; giảm riêng mesh chân còn 60%, blend LowerLeg/Foot/Toes. UV lấy từ đúng da tay trong atlas Body từng phu. Texture và skeleton vẫn là Rocketbox MIT.

Tái sinh từ gốc worktree (Blender nền, một tiến trình):

```bash
blender -b --factory-startup --threads 1 --python-exit-code 1 -P Tools/blender/prepare_crowd.py -- --feet12
python3 Tools/blender/finalize_crowd.py --feet12
./Tools/import_characters.sh
```

Bản sinh ở `Tools/blender/generated/Characters`, bằng chứng Blender ở `Logs/feet12_blender`. CharacterImporter ưu tiên bản này cho các vai đã sửa, giữ đường dẫn đích/GUID, dựng prefab và LOD bằng API Unity. Chạy rebuild+bake cảnh và Feet12Shots sau import; lệnh kiểm đầy đủ ghi trong `Logs/feet12_report.md`. Không chạy pipeline mặc định để ghi vào Staging symlink.
