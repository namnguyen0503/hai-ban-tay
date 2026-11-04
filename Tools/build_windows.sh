#!/usr/bin/env bash
# Dùng: ./Tools/build_windows.sh          → bản release (kiểm nội dung chặt)
#       ./Tools/build_windows.sh --dev    → Development Build, cho phép TODO (có watermark)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "${1:-}" == "--dev" ]]; then
  python3 "$ROOT/Tools/validate_content.py"
  EXTRA=(-hbtDev)
else
  python3 "$ROOT/Tools/validate_content.py" --release
  EXTRA=()
fi
"$ROOT/Tools/unity.sh" -quit -buildTarget Win64 \
  -executeMethod HaiBanTay.Editor.CI.BuildScript.BuildWindows "${EXTRA[@]}" \
  -logFile "$ROOT/Logs/build-windows.log"
du -sh "$ROOT/Builds/Windows"
