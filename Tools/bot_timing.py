#!/usr/bin/env python3
"""Thời lượng từ log bot (PRODUCTION_PLAN W2 nghiệm thu: cả game ~35–45 phút, mỗi ngày ≤ 7 phút; bot chơi tối ưu nên là cận dưới).

Đọc Logs/playthrough/run_log_*.jsonl (mặc định file mới nhất), in giờ game theo cảnh/ngày và theo từng beat, cùng số phát hiện
theo mức. Giờ = gameTime (Time.time của bot, 1 giây game = 1 giây người chơi ở tốc độ thường).

Dùng: python3 Tools/bot_timing.py [run_log.jsonl] [--beats]
"""
import json
import sys
from collections import Counter, OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def main(argv):
    paths = [a for a in argv[1:] if not a.startswith("--")]
    path = Path(paths[0]) if paths else max((ROOT / "Logs" / "playthrough").glob("run_log_*.jsonl"), key=lambda p: p.stat().st_mtime)
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        try:
            rows.append(json.loads(line))
        except ValueError:
            pass
    timed = [r for r in rows if isinstance(r.get("gameTime"), (int, float))]
    if not timed:
        print("Không có dòng có gameTime.")
        return 1
    # Đoạn: cảnh (00, 01, 03) hoặc ngày trên tàu (02 theo cột day).
    spans = OrderedDict()
    for r in timed:
        scene = r.get("scene", "")
        key = f"{scene} ngày {r.get('day')}" if scene.startswith("02_") and r.get("day") else scene
        if not key or key.startswith("InitTestScene"):
            continue
        a, b = spans.get(key, (r["gameTime"], r["gameTime"]))
        spans[key] = (min(a, r["gameTime"]), max(b, r["gameTime"]))
    total = timed[-1]["gameTime"] - timed[0]["gameTime"]
    print(f"{path.name}: {total / 60:.1f} phút game, {len(rows)} dòng")
    for key, (a, b) in spans.items():
        mins = (b - a) / 60
        flag = "  > 7 phút" if " ngày " in key and mins > 7 else ""
        print(f"  {key:<28} {mins:5.1f} phút{flag}")
    if "--beats" in argv:
        beats = [(r["gameTime"], r["step"]) for r in timed if r.get("type") == "timeline" and r.get("step", "").startswith("beat_")]
        for (t, step), nxt in zip(beats, beats[1:] + [(timed[-1]["gameTime"], "")]):
            print(f"    {step:<48} {(nxt[0] - t):6.0f} s")
    sev = Counter(r.get("severity") for r in rows if r.get("severity"))
    print("  phát hiện:", ", ".join(f"{k} {v}" for k, v in sev.most_common()) or "không")
    last = rows[-1]
    print(f"  dòng cuối: {last.get('type')} {last.get('scene')} {last.get('beat')} {last.get('step')}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
