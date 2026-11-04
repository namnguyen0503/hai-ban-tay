#!/usr/bin/env bash
# Cổng dữ liệu sinh lại (PRODUCTION_PLAN W1.4, cổng G1), một lệnh:
#   kiểm nội dung → dựng lại scene → bake ánh sáng → tạo mã bản dựng → smoke build Linux dev mang mã đó.
# Ảnh, log bot, kết quả test sau đó gắn mã này (Tools/run_tests.sh, Tools/screenshots.sh, bot). Builder hay scene đổi sau đó
# thì mã thành "+stale" (Tools/build_id.py show): ảnh/test của bản cũ không dùng làm bằng chứng, chạy lại cổng.
# Từ W5: NavMesh bake trong bước dựng lại/bake.
# Dùng: ./Tools/regen_gate.sh [--no-smoke]   (--no-smoke: bỏ smoke build khi chỉ cần scene mới; mã mang "+nosmoke")
# Unity Editor phải đóng (các bước thoát mã 3 nếu project đang mở).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SMOKE=1
[[ "${1:-}" == "--no-smoke" ]] && SMOKE=0
step() { echo "== $1 $(date +%T)"; }

step "kiểm nội dung"
python3 "$ROOT/Tools/validate_content.py"
step "dựng lại scene"
"$ROOT/Tools/rebuild_scenes.sh"
step "bake ánh sáng"
"$ROOT/Tools/bake_lighting.sh"
ID="$(python3 "$ROOT/Tools/build_id.py" new --steps validate,rebuild,bake)"
if [[ "$SMOKE" == 1 ]]; then
  step "smoke build Linux dev ($ID)"
  if HBT_BUILD_ID="$ID" "$ROOT/Tools/build_linux.sh" --dev; then
    python3 "$ROOT/Tools/build_id.py" smoke ok
  else
    python3 "$ROOT/Tools/build_id.py" smoke failed || true
    echo "Smoke build hỏng; xem Logs/build-linux.log" >&2
    exit 1
  fi
fi
step "xong"
echo "BUILD_ID $(python3 "$ROOT/Tools/build_id.py" show)"
