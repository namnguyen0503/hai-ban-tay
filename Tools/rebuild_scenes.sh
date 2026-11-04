#!/usr/bin/env bash
# Dựng lại mọi scene từ SceneBuilders (xóa lightmap cũ; cần bake lại trước khi build release)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/Tools/unity.sh" -nographics -quit \
  -executeMethod HaiBanTay.Editor.SceneBuilders.SceneBuildAll.Run \
  -logFile "$ROOT/Logs/rebuild-scenes.log"
