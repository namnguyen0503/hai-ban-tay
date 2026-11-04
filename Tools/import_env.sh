#!/usr/bin/env bash
# Import 3 environments + unchanged layouts; no scene edits here.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.Import.EnvImporter.Run \
  -logFile "$ROOT/Logs/import-env.log"
grep -E "^EnvImporter" "$ROOT/Logs/import-env.log" || true
