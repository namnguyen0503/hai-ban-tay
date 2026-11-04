#!/usr/bin/env bash
# Dùng: ./Tools/run_tests.sh EditMode | PlayMode [bộ lọc]   (không dùng -quit cùng -runTests)
# Bộ lọc (tùy chọn) chuyển thẳng cho -testFilter, ví dụ: ./Tools/run_tests.sh PlayMode ShipWalkPlayTests
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLATFORM="${1:-EditMode}"
FILTER="${2:-}"
RESULTS="$ROOT/Logs/tests-$PLATFORM.xml"
mkdir -p "$ROOT/Logs"
export HBT_BUILD_ID="${HBT_BUILD_ID:-$(python3 "$ROOT/Tools/build_id.py" show)}"
echo "$HBT_BUILD_ID" > "$ROOT/Logs/tests-$PLATFORM.build_id"
EXTRA=()
if [[ -n "$FILTER" ]]; then
  EXTRA=(-testFilter "$FILTER")
fi
set +e
"$ROOT/Tools/unity.sh" -nographics -runTests -testPlatform "$PLATFORM" "${EXTRA[@]}" \
  -testResults "$RESULTS" -logFile "$ROOT/Logs/tests-$PLATFORM.log"
CODE=$?
set -e
if [[ -f "$RESULTS" ]]; then
  python3 - "$RESULTS" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
print(f"TESTS {r.get('total')} total, {r.get('passed')} passed, {r.get('failed')} failed")
PY
  echo "BUILD_ID $HBT_BUILD_ID"
else
  echo "Không có file kết quả. Xem Logs/tests-$PLATFORM.log"
fi
exit "$CODE"
