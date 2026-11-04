#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="$ROOT/Logs/compile.log"
mkdir -p "$ROOT/Logs"
set +e
"$ROOT/Tools/unity.sh" -nographics -quit -logFile "$LOG"
CODE=$?
set -e
if grep -qE "error CS[0-9]+" "$LOG"; then
  grep -E "error CS[0-9]+" "$LOG" | sort -u
  echo "COMPILE: FAIL"
  exit 1
fi
if [[ $CODE -ne 0 ]]; then
  echo "Unity thoát với mã $CODE. Xem $LOG"
  exit "$CODE"
fi
echo "COMPILE: OK"
