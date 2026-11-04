#!/usr/bin/env bash
# Nhập 20 vai × 3 LOD, 31 clip (Rocketbox MIT, KayKit CC0); giữ GUID controller và Prefabs/NPC/{vai}.prefab.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.Import.CharacterImporter.Run \
  -logFile "$ROOT/Logs/import-characters.log"
grep -E "^CharacterImporter" "$ROOT/Logs/import-characters.log" || true
