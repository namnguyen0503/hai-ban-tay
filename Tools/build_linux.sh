#!/usr/bin/env bash
# Dùng: ./Tools/build_linux.sh          → bản release (kiểm nội dung chặt)
#       ./Tools/build_linux.sh --dev    → Development Build, cho phép TODO (có watermark)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "${1:-}" == "--dev" ]]; then
  python3 "$ROOT/Tools/validate_content.py"
  EXTRA=(-hbtDev)
else
  python3 "$ROOT/Tools/validate_content.py" --release
  EXTRA=()
fi
"$ROOT/Tools/unity.sh" -quit -buildTarget Linux64 \
  -executeMethod HaiBanTay.Editor.CI.BuildScript.BuildLinux "${EXTRA[@]}" \
  -logFile "$ROOT/Logs/build-linux.log"
du -sh "$ROOT/Builds/Linux"
