#!/usr/bin/env python3
"""Tóm tắt CSV hiệu năng (F8) theo cảnh/beat, đánh dấu chỗ vi phạm ngân sách PLAN 3.3.

Dùng: python3 Tools/analyze_perf.py perf_*.csv
Ngân sách: FPS trung bình >= 30, FPS thấp nhất của beat >= 28, SetPass tối đa <= 150.
CSV W7: mỗi dòng là một lượt beat, có duration_s/frames. 1% low gộp hiển thị lượt thấp nhất, không coi là percentile toàn phiên.
"""
import csv
import sys
from collections import OrderedDict

TARGET_AVG = 30.0
TARGET_MIN = 28.0


def load(paths):
    rows = []
    for path in paths:
        with open(path, newline="", encoding="utf-8") as f:
            for r in csv.DictReader(f):
                r["_file"] = path
                rows.append(r)
    return rows


def summarize(rows):
    seconds = sum(float(r.get("duration_s", 1)) for r in rows)
    frames = sum(float(r["frames"]) if "frames" in r else float(r["fps_avg"]) for r in rows)
    counters = [float(r.get("setpass_max", -1)) for r in rows]
    lows = [float(r["fps_1pct_low"]) for r in rows if "fps_1pct_low" in r]
    return seconds, frames / seconds, min(float(r["fps_min"]) for r in rows), min(lows) if lows else None, max(counters)


def main(argv):
    if len(argv) < 2:
        print(__doc__.strip())
        return 2
    rows = load(argv[1:])
    if not rows:
        print("Không có dòng dữ liệu.")
        return 1

    groups = OrderedDict()
    for r in rows:
        key = (r.get("scene", "?"), r.get("beat", "") or "-", r.get("tier", "?"))
        groups.setdefault(key, []).append(r)

    print(f"{'Cảnh':<14} {'Beat':<18} {'Tầng':<7} {'Giây':>7} {'FPS TB':>7} {'FPS min':>8} {'1% low*':>8} {'SetPass':>8}  Ghi chú")
    worst = None
    failures = 0
    for (scene, beat, tier), rs in groups.items():
        seconds, avg, low, low1, setpass = summarize(rs)
        note = []
        if avg < TARGET_AVG:
            note.append("TB < 30")
        if low < TARGET_MIN:
            note.append("min < 28")
        if setpass > 150:
            note.append("SetPass > 150")
        if setpass < 0:
            note.append("SetPass UNVERIFIED")
        failures += bool(note)
        if worst is None or avg < worst[0]:
            worst = (avg, scene, beat)
        low_text = f"{low1:.1f}" if low1 is not None else "—"
        sp_text = f"{setpass:.0f}" if setpass >= 0 else "—"
        print(f"{scene:<14} {beat:<18} {tier:<7} {seconds:>7.1f} {avg:>7.1f} {low:>8.1f} {low_text:>8} {sp_text:>8}  {', '.join(note)}")

    print()
    print(f"Beat nặng nhất: {worst[1]} {worst[2]} ({worst[0]:.1f} fps TB)")
    print("* 1% low thấp nhất giữa các lượt cùng beat/tầng. -1 trong CSV = bộ đếm không khả dụng.")
    print("Thứ tự tối ưu (PLAN M6): render scale → đèn/bóng → hậu kỳ → số vật thể/tam giác → texture.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
