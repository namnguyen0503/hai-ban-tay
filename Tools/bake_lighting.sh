#!/usr/bin/env bash
# Bake lightmap, light probe, reflection probe và occlusion culling cho 01–03 (Progressive CPU; không dùng -nographics).
# Chạy lại sau mỗi lần ./Tools/rebuild_scenes.sh.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Không có màn hình (runner CI trong sandbox bwrap, máy không phiên đồ họa) mà có Xvfb: chạy lại trong màn hình ảo. Bake cần đồ họa
# (không -nographics); thiếu màn hình thì Unity dừng "Error opening default X display" (CI 01/11).
# Biến DISPLAY có thể kế thừa từ phiên đăng nhập mà socket không tới được (sandbox có /tmp riêng): xét socket X thật.
x_socket() { local n="${DISPLAY:-}"; n="${n#*:}"; n="${n%%.*}"; [[ -n "$n" && -S "/tmp/.X11-unix/X$n" ]]; }
if [[ -z "${HBT_XVFB:-}" ]] && ! x_socket && command -v xvfb-run >/dev/null; then
  exec env -u WAYLAND_DISPLAY -u DISPLAY HBT_XVFB=1 xvfb-run -a -s "-screen 0 1280x720x24" "$0" "$@"
fi
set +e
"$ROOT/Tools/unity.sh" -quit \
  -executeMethod HaiBanTay.Editor.Lighting.LightingBaker.BakeAll \
  -logFile "$ROOT/Logs/bake.log"
CODE=$?
set -e
grep -E "^LightingBaker:" "$ROOT/Logs/bake.log" || true
exit "$CODE"
