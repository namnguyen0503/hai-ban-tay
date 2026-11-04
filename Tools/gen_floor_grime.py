#!/usr/bin/env python3
"""Vẽ lại Gen_FloorGrime.png: vết bẩn muội/ẩm mốc trên sàn gạch bếp (quad SweepSpot, vật liệu FloorGrime nhân màu nâu đen).

Chạy: python3 -I Tools/gen_floor_grime.py   (cần Pillow, không cần numpy). Unity tự nhập lại ảnh, giữ nguyên .meta.
Hình theo ảnh mẫu của người chơi: vệt dài chéo, lõi gần đen xanh ô liu dày hạt, đậm ở một đầu rồi thưa dần thành quầng
nâu có chấm li ti; mép không có đường viền mà tan thành hạt. Alpha chạm 0 trước mép ảnh nên không lộ viền vuông.
"""
import math
import os
import random
import sys

from PIL import Image

SIZE = 512
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Game", "Assets", "_Project", "Art",
                   "Textures", "Gen_FloorGrime.png")
ANGLE = math.radians(-22.0)  # trục dài của vệt


def make_noise(seed, cells):
    rng = random.Random(seed)
    grid = [[rng.random() for _ in range(cells + 1)] for _ in range(cells + 1)]
    for i in range(cells + 1):  # lặp liền mạch
        grid[i][cells] = grid[i][0]
        grid[cells][i] = grid[0][i]

    def sample(u, v):
        x, y = (u % 1.0) * cells, (v % 1.0) * cells
        x0, y0 = int(x), int(y)
        fx, fy = x - x0, y - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = grid[y0][x0] + (grid[y0][x0 + 1] - grid[y0][x0]) * fx
        b = grid[y0 + 1][x0] + (grid[y0 + 1][x0 + 1] - grid[y0 + 1][x0]) * fx
        return a + (b - a) * fy

    return sample


def smooth(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def main():
    cells = [4, 8, 16, 32, 64, 128, 256]
    octaves = [make_noise(300 + i, c) for i, c in enumerate(cells)]
    weights = [0.5, 0.3, 0.2, 0.12, 0.07, 0.04, 0.02]
    wsum = sum(weights)

    def fbm(u, v, upto=7):
        return sum(w * n(u, v) for w, n, _ in zip(weights, octaves, range(upto))) / sum(weights[:upto])

    cs, sn = math.cos(ANGLE), math.sin(ANGLE)
    img = Image.new("RGBA", (SIZE, SIZE))
    px = img.load()
    for y in range(SIZE):
        v = y / SIZE
        for x in range(SIZE):
            u = x / SIZE
            # Méo toạ độ cho dạng lông tơ, không đối xứng.
            wu = u + 0.07 * (fbm(u + 3.1, v + 7.7, 5) - 0.5)
            wv = v + 0.07 * (fbm(u + 9.4, v + 1.3, 5) - 0.5)
            dx, dy = wu - 0.5, wv - 0.5
            t = (dx * cs + dy * sn) / 0.62   # dọc trục dài, -1..1
            w = (-dx * sn + dy * cs) / 0.62  # ngang trục
            # Lõi đậm lệch về đầu âm của trục (t < 0), đuôi thưa kéo sang đầu dương và loe rộng dần.
            width = 0.22 + 0.2 * (t + 1.0) * 0.5
            across = math.exp(-(w / width) ** 2 * 2.2)
            along_core = math.exp(-((t + 0.3) / 0.8) ** 2 * 2.0)
            along_halo = math.exp(-((t - 0.1) / 0.85) ** 2 * 1.6)
            core = across * along_core
            halo = across * along_halo
            blotch = fbm(u * 1.8 + 4.0, v * 1.8 + 2.0, 4)  # mảng lớn làm lõi không đều
            n = 0.55 * fbm(u * 16.0, v * 16.0, 7) + 0.45 * fbm(u * 5.0, v * 5.0, 7)                    # hạt tinh, chủ yếu quanh 0,3-0,7
            n2 = octaves[6](u * 3.0 + 1.7, v * 3.0 + 0.4)   # chấm li ti
            # Mật độ muội: lõi dày + đuôi thưa. Hạt hiện nơi mật độ vượt ngưỡng nhiễu nên mép tan thành chấm.
            d_core = core * (0.7 + 0.6 * blotch)
            d_tail = halo * 0.55 * (0.6 + 0.8 * blotch)
            d = max(d_core, d_tail)
            soot = smooth(0.0, 0.26, 0.27 + 0.55 * d - n) * (0.82 + 0.18 * n2)
            dots = smooth(0.0, 0.12, 0.1 + 0.45 * halo - n2) * 0.7 * smooth(0.03, 0.18, halo)
            haze = halo * 0.38 * (0.55 + 0.9 * blotch)
            a = min(1.0, max(soot, dots, haze))
            edge = min(u, v, 1 - u, 1 - v)
            a *= smooth(0.0, 0.05, edge)
            # Màu: lõi gần đen ô liu, quầng nâu. Vật liệu nhân thêm nâu sẫm nên giữ RGB sáng ở quầng.
            k = smooth(0.2, 0.9, soot * (0.5 + 0.5 * min(1.0, d)))
            gl = 0.55 + 0.45 * octaves[5](u * 3.0, v * 3.0)  # hạt sáng lấm tấm trong lõi
            r = (1.0 - k) * 1.0 + k * 0.22 * gl
            g = (1.0 - k) * 0.82 + k * 0.25 * gl
            b = (1.0 - k) * 0.58 + k * 0.17 * gl
            px[x, y] = (int(255 * min(r, 1.0)), int(255 * min(g, 1.0)), int(255 * min(b, 1.0)), int(255 * a))

    img.save(OUT)
    print("Đã ghi", os.path.normpath(OUT))


if __name__ == "__main__":
    sys.exit(main())
