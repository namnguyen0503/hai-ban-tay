#!/usr/bin/env bash
# Chạy Unity ở batchmode với đúng phiên bản ghi trong ProjectVersion.txt.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/Game"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_PATH:-$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity}"
if [[ ! -x "$UNITY" ]]; then
  echo "Không tìm thấy Unity $VERSION tại $UNITY. Đặt biến UNITY_PATH." >&2
  exit 2
fi
if [[ -f "$PROJECT/Temp/UnityLockfile" ]] && pgrep -fi "unity.*projectpath.*$PROJECT" >/dev/null; then
  echo "Project đang mở trong Unity Editor. Đóng Editor rồi chạy lại." >&2
  exit 3
fi
mkdir -p "$ROOT/Logs" "$ROOT/Builds"
# Mã bản dựng (Tools/build_id.py): test, ảnh, log bot, bản build đọc HBT_BUILD_ID; "+stale" = dữ liệu đã đổi từ lần sinh lại.
export HBT_BUILD_ID="${HBT_BUILD_ID:-$(python3 "$ROOT/Tools/build_id.py" show 2>/dev/null || echo none)}"
exec "$UNITY" -batchmode -projectPath "$PROJECT" "$@"
