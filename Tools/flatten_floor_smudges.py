#!/usr/bin/env python3
"""Làm phẳng các mảng bẩn sẫm lớn trong dirty_tiles_Color_2K.jpg (sàn bếp).

Ảnh gốc có các vệt tối cỡ nửa mét in sẵn, lát lặp mỗi 1,5 m nên sàn trông bẩn đều theo lưới và khó phân biệt với vệt cần lau
(quad SweepSpot). Script lấy nền sáng-tối tần số thấp (làm mờ lát 3x3 để liền mạch), trừ phần lớn của nó đi, giữ lại vết xước,
ron gạch, hạt mịn và chút khác biệt giữa các viên.

Dùng: python3 -I Tools/flatten_floor_smudges.py <ảnh gốc> <ảnh ra> [mức 0..1, mặc định 0.85]
"""
import sys

from PIL import Image, ImageChops, ImageFilter, ImageStat

SIGMA = 10  # px ở 1/4 cỡ ảnh, tức ~40 px ở ảnh gốc: nhỏ hơn mảng bẩn nhưng lớn hơn ron và vết xước


def main(src, dst, strength=0.85):
    img = Image.open(src).convert("RGB")
    w, h = img.size
    small = img.resize((w // 4, h // 4), Image.LANCZOS)
    sw, sh = small.size
    tiled = Image.new("RGB", (sw * 3, sh * 3))
    for ty in range(3):
        for tx in range(3):
            tiled.paste(small, (tx * sw, ty * sh))
    blur = tiled.filter(ImageFilter.GaussianBlur(SIGMA)).crop((sw, sh, sw * 2, sh * 2)).resize((w, h), Image.BICUBIC)
    mean = tuple(int(round(m)) for m in ImageStat.Stat(blur).mean)
    mean_img = Image.new("RGB", (w, h), mean)
    flat = Image.blend(mean_img, blur, strength)        # phần tần số thấp cần bỏ đi
    diff = ImageChops.subtract(img, flat, 1.0, 128)      # img - flat + 128
    out = ImageChops.add(diff, mean_img, 1.0, -128)      # + mean - 128
    out.save(dst, quality=95, subsampling=0)
    print("Đã ghi", dst, "mean", mean)


if __name__ == "__main__":
    a = sys.argv[1:]
    sys.exit(main(a[0], a[1], float(a[2]) if len(a) > 2 else 0.85))
