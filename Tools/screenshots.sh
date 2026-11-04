#!/usr/bin/env bash
# Chụp ảnh các beat chính của 3 cảnh (camera + UI, 1920×1080) vào Logs/shots/ để soát bằng mắt.
# Cần GPU (không dùng -nographics). Dùng: ./Tools/screenshots.sh [tên test, ví dụ Shots02Rooms]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS="$ROOT/Logs/tests-Screenshots.xml"
FILTER="${1:-${HBT_SCREENSHOT_FILTER:-}}"
EXTRA=()
if [[ -n "$FILTER" ]]; then
  EXTRA=(-testFilter "$FILTER")
fi
mkdir -p "$ROOT/Logs/shots"
export HBT_BUILD_ID="${HBT_BUILD_ID:-$(python3 "$ROOT/Tools/build_id.py" show)}"
echo "$HBT_BUILD_ID" > "$ROOT/Logs/shots/BUILD_ID.txt"
set +e
"$ROOT/Tools/unity.sh" -runTests -testPlatform PlayMode -testCategory Screenshots \
  "${EXTRA[@]}" -testResults "$RESULTS" -logFile "$ROOT/Logs/screenshots.log"
CODE=$?
set -e
ls -1 "$ROOT/Logs/shots" 2>/dev/null | sed 's/^/  /'
echo "BUILD_ID $HBT_BUILD_ID"
exit "$CODE"
