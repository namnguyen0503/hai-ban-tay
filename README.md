# Hai Bàn Tay

Game Unity 3D góc nhìn thứ nhất, làm cho bài tập môn Tư tưởng Hồ Chí Minh. Người chơi theo hành trình của anh Ba năm 1911: trò chuyện với anh Lê ở Sài Gòn, xuống tàu Amiral Latouche-Tréville ở bến Nhà Rồng, làm phụ bếp và học tiếng Pháp qua sáu ngày trên biển, rồi tới Marseille.

Cốt truyện dựa theo đoạn kể về chuyến tàu trong *Những mẩu chuyện về đời hoạt động của Hồ Chủ tịch* (Trần Dân Tiên). Game có hư cấu và dàn dựng; lời thông báo nằm ở màn **Chơi mới** và credits.

*A first-person Unity game (Vietnamese) about the young Hồ Chí Minh working as a kitchen hand on the voyage from Saigon to Marseille in 1911.*

## Trailer

[Xem trailer (1080p, 2 phút 5 giây)](https://github.com/namnguyen0503/hai-ban-tay/releases/download/v1.0/HaiBanTay-Trailer-1080p.mp4): quay từ chính game, do bot tự chơi theo kịch bản rồi `Tools/make_trailer.py` dựng tự động.

## Tải game

Vào [Releases](../../releases), tải bản mới nhất:

- **Windows**: giải nén `HaiBanTay-Windows.zip`, mở `HaiBanTay.exe` (giữ nguyên cả thư mục).
- **Linux**: giải nén `HaiBanTay-Linux.tar.gz`, chạy `./HaiBanTay.x86_64`.
- **Android**: tải `HaiBanTay.apk` về điện thoại, cho phép cài từ nguồn ngoài rồi cài (ARM64, Vulkan, màn hình ngang).

Máy tham chiếu: laptop i3-1005G1, Intel UHD, Windows 10, 1080p. Bản build khoảng 1 GB.

## Điều khiển

| Phím | Tác dụng |
|---|---|
| WASD / mũi tên | Đi |
| Chuột | Nhìn |
| E / chuột trái | Tương tác, nhặt, đặt |
| Giữ chuột trái | Dùng dụng cụ, xúc than, kéo rổ; cọ nồi thì giữ và rê chuột |
| Giữ Space | Giơ hai bàn tay khi được yêu cầu |
| C | Ngồi nghỉ |
| Tab | Mở/đóng sổ |
| Esc | Tạm dừng, cài đặt |

Trên Android: kéo nửa trái màn hình để đi, kéo nửa phải để nhìn; chạm nửa phải hoặc nút **Làm** để tương tác; giữ **Dùng** / **Giơ tay**; các nút **Ngồi**, **Sổ**, **Dừng**.

## Mã nguồn

- Unity **6000.3.25f1**, URP. Mở thư mục `Game/` bằng Unity Hub.
- Scene được dựng bằng code trong `Game/Assets/_Project/Scripts/Editor/SceneBuilders`; dựng lại bằng `./Tools/rebuild_scenes.sh`, bake ánh sáng bằng `./Tools/bake_lighting.sh`.
- Toàn bộ chữ hiển thị nằm trong `Game/Assets/_Project/Resources/Content/*.vi.json`.
- Kiểm nội dung: `python3 Tools/validate_content.py`. Test: `./Tools/run_tests.sh EditMode` / `PlayMode` (gồm bot tự chơi hết game, `PlaythroughTests`).
- Build: `./Tools/build_windows.sh`, `./Tools/build_linux.sh` (đóng Unity Editor trước; `Tools/unity.sh` tìm Editor theo phiên bản project hoặc biến `UNITY_PATH`).
- Model, animation và mesh môi trường được chuẩn bị bằng Blender 4.2 qua các script trong `Tools/blender/` (đặt `blender` trong PATH).

## Giấy phép

- Mã nguồn (C#, script trong `Tools/`): [MIT](LICENSE).
- Nội dung do nhóm tạo (truyện, thoại, giọng đọc, model và âm thanh tự dựng): [CC BY-NC 4.0](LICENSE-CONTENT.md).
- Tài nguyên bên thứ ba (Microsoft Rocketbox, Poly Haven, Kenney, ambientCG, OpenGameArt, BigSoundBank, poly.pizza, font Be Vietnam Pro và Noto Serif) giữ giấy phép gốc của tác giả; danh sách đầy đủ ở [Docs/CREDITS.md](Docs/CREDITS.md).
