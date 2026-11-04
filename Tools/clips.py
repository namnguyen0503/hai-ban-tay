#!/usr/bin/env python3
"""Ghép clip KitchenMotionClips (Logs/shots/clips/<tên>/f_###.jpg) thành MP4 và tấm lưới khung để duyệt chuyển động.

Dùng: python3 Tools/clips.py [tên clip ...]   (không có tên thì ghép mọi clip)
Ra: Logs/shots/clips/<tên>.mp4 (H.264, đúng nhịp ghi trong fps.txt) và <tên>_sheet.jpg (16 khung cách đều, ghi giây).
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIPS = os.path.join(ROOT, 'Logs', 'shots', 'clips')


def sheet(folder, frames, fps, out, cols=4, rows=4, width=480):
    picks = [frames[round(i * (len(frames) - 1) / (cols * rows - 1))] for i in range(cols * rows)] if len(frames) > 1 else frames
    first = Image.open(os.path.join(folder, picks[0]))
    height = round(width * first.height / first.width)
    grid = Image.new('RGB', (cols * width, rows * height), 'black')
    draw = ImageDraw.Draw(grid)
    for i, name in enumerate(picks):
        im = Image.open(os.path.join(folder, name)).convert('RGB').resize((width, height))
        x, y = (i % cols) * width, (i // cols) * height
        grid.paste(im, (x, y))
        t = int(name[2:5]) / fps
        draw.rectangle((x, y, x + 74, y + 20), fill='black')
        draw.text((x + 4, y + 4), f'{t:5.2f} s', fill='white')
    grid.save(out, quality=85)


def main(names):
    if not os.path.isdir(CLIPS):
        sys.exit('Chưa có clip: chạy ./Tools/screenshots.sh KitchenMotionClips')
    names = names or sorted(d for d in os.listdir(CLIPS) if os.path.isdir(os.path.join(CLIPS, d)))
    for name in names:
        folder = os.path.join(CLIPS, name)
        frames = sorted(f for f in os.listdir(folder) if f.startswith('f_') and f.endswith('.jpg'))
        if not frames:
            print(f'{name}: không có khung')
            continue
        fps_file = os.path.join(folder, 'fps.txt')
        fps = int(open(fps_file).read().strip()) if os.path.exists(fps_file) else 15
        mp4 = os.path.join(CLIPS, name + '.mp4')
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', str(fps), '-i', os.path.join(folder, 'f_%03d.jpg'),
                        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '24', '-movflags', '+faststart', mp4], check=True)
        sheet(folder, frames, fps, os.path.join(CLIPS, name + '_sheet.jpg'))
        print(f'{name}: {len(frames)} khung, {len(frames) / fps:.1f} s @ {fps} fps → {os.path.getsize(mp4) // 1024} KB')


if __name__ == '__main__':
    main(sys.argv[1:])
