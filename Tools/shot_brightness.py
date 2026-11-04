#!/usr/bin/env python3
"""Độ sáng PNG gameplay: ảnh xám PIL, resize 320×180, trung bình /255."""
import argparse
from pathlib import Path
from PIL import Image, ImageStat


def brightness(path):
    with Image.open(path) as image:
        return ImageStat.Stat(image.convert("L").resize((320, 180))).mean[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("after", type=Path)
    parser.add_argument("--before", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--minimum", type=float, default=38)
    args = parser.parse_args()
    rows = ["| Ảnh | Trước | Sau | ≥38 |", "|---|---:|---:|:---:|"]
    for shot in sorted(args.after.glob("*.png")):
        if not (shot.name.startswith("02") or shot.stem in ("03b2_final_sweep", "03b3_final_wash")):
            continue
        before = args.before / shot.name if args.before else None
        previous = f"{brightness(before):.2f}" if before and before.exists() else "—"
        value = brightness(shot)
        rows.append(f"| {shot.name} | {previous} | {value:.2f} | {'Đạt' if value >= args.minimum else 'Dưới ngưỡng'} |")
    report = "\n".join(rows) + "\n"
    if args.output:
        args.output.write_text(report, encoding="utf-8")
    else:
        print(report, end="")


if __name__ == "__main__":
    main()
