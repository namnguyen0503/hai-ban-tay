#!/usr/bin/env bash
# Đưa vật liệu vật mỏng/hở sang shader hai mặt (HaiBanTay/LitTwoSided) và nhập lại FBX nhân vật + tàu; sau đó chạy rebuild_scenes.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.Setup.TwoSidedMaterials.Run \
  -logFile "$ROOT/Logs/two-sided.log"
grep -E "^TWO_SIDED|error CS" "$ROOT/Logs/two-sided.log" || true
