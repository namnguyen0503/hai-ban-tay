#!/usr/bin/env bash
# Chỉ nhập tay FP và cấu hình khung chung; không sinh prefab ArtSlot.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.Import.HandsImporter.Run \
  -logFile "$ROOT/Logs/import-hands.log"
