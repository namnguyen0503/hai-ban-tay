#!/usr/bin/env bash
# Nhập model CC0 từ Staging/art/ vào Art/Models và sinh Prefabs/Art/{slot}.prefab; sau đó chạy rebuild_scenes.sh rồi bake_lighting.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.Import.ArtImporter.Run \
  -logFile "$ROOT/Logs/import-art.log"
grep -E "^ArtImporter" "$ROOT/Logs/import-art.log" || true
