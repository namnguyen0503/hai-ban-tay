#!/usr/bin/env python3
"""Ghép khung BotVideo (Logs/video/<run>/f_######.jpg + frames.txt) thành MP4 để xem lại lượt bot chơi.

Dùng: python3 Tools/bot_video.py [thư mục run]   (bỏ trống: run mới nhất trong Logs/video)
Ra (cạnh thư mục run):
  <run>.mp4             H.264 8 khung/giây theo thời gian thật của game; chương theo cảnh/beat (mpv/VLC nhảy chương);
                        track phụ đề bật/tắt được: cảnh, beat, ngày, giờ ca, vị trí Ba, đồ cầm, câu thoại, prompt, hướng dẫn.
  <run>.srt             cùng phụ đề, để VLC/mpv tự nạp hoặc mở bằng trình soạn thảo.
  <run>_transcript.txt  mỗi câu thoại một dòng: giờ trong video, cảnh/beat, ngày/giờ ca, id · người nói: chữ.
frames.txt mỗi dòng: số khung, giây (unscaledTime), cảnh+beat, ngày+giờ, vị trí, đồ cầm, câu thoại, prompt, hướng dẫn (tab).
"""
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIDEO = os.path.join(ROOT, 'Logs', 'video')
FIELDS = ('beat', 'day', 'pos', 'holding', 'line', 'prompt', 'guide')


def stamp(t, sep=','):
    ms = int(round(t * 1000))
    return '%02d:%02d:%02d%s%03d' % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, sep, ms % 1000)


def main():
    if len(sys.argv) > 1:
        folder = os.path.abspath(sys.argv[1]).rstrip('/')
    else:
        runs = [os.path.join(VIDEO, d) for d in os.listdir(VIDEO) if os.path.isdir(os.path.join(VIDEO, d))]
        folder = max(runs, key=os.path.getmtime)
    frames = []
    with open(os.path.join(folder, 'frames.txt'), encoding='utf-8') as f:
        for raw in f:
            parts = raw.rstrip('\n').split('\t')
            if len(parts) < 2 or not parts[0].isdigit():
                continue
            path = os.path.join(folder, 'f_%06d.jpg' % int(parts[0]))
            if os.path.exists(path):
                meta = dict(zip(FIELDS, parts[2:] + [''] * len(FIELDS)))
                frames.append((path, float(parts[1]), meta))
    if not frames:
        sys.exit('Không có khung nào trong ' + folder)

    # Thời lượng mỗi khung theo giờ game thật (kẹp 0,01–2 s: tải cảnh lâu không thành khoảng đen dài); vị trí trên video = cộng dồn.
    starts, clock = [], 0.0
    for i, (_, t, _) in enumerate(frames):
        nxt = frames[i + 1][1] if i + 1 < len(frames) else t + 0.125
        starts.append((clock, min(max(nxt - t, 0.01), 2.0)))
        clock += starts[-1][1]

    concat = os.path.join(folder, 'concat.txt')
    with open(concat, 'w', encoding='utf-8') as f:
        for (path, _, _), (_, dur) in zip(frames, starts):
            f.write("file '%s'\nduration %.3f\n" % (path, dur))
        f.write("file '%s'\n" % frames[-1][0])

    # Chương: mỗi lần cảnh/beat đổi.
    meta = os.path.join(folder, 'chapters.txt')
    chapters = []
    for (_, _, m), (at, _) in zip(frames, starts):
        if not chapters or chapters[-1][1] != m['beat']:
            chapters.append((at, m['beat']))
    with open(meta, 'w', encoding='utf-8') as f:
        f.write(';FFMETADATA1\n')
        for i, (start, label) in enumerate(chapters):
            end = chapters[i + 1][0] if i + 1 < len(chapters) else clock
            if end - start < 0.5:
                continue
            title = ''.join(' ' if c in '=;#\\' else c for c in label) or '(đang tải)'
            f.write('[CHAPTER]\nTIMEBASE=1/1000\nSTART=%d\nEND=%d\ntitle=%s\n' % (start * 1000, end * 1000, title))

    # Phụ đề: gộp các khung liền nhau có cùng nội dung (vị trí làm tròn 0,1 m nên Ba đứng yên thì một cue).
    def text(m):
        rows = [' · '.join(x for x in (m['beat'], m['day'].strip(), ('Ba ' + m['pos']) if m['pos'] else '',
                                       ('cầm ' + m['holding']) if m['holding'] else '') if x)]
        if m['line']:
            rows.append('🗨 ' + m['line'])
        hint = ' | '.join(x for x in (('Prompt: ' + m['prompt']) if m['prompt'] else '', ('HD: ' + m['guide']) if m['guide'] else '') if x)
        if hint:
            rows.append(hint)
        return '\n'.join(rows)

    cues = []
    for (_, _, m), (at, dur) in zip(frames, starts):
        t = text(m)
        if cues and cues[-1][2] == t and abs(cues[-1][1] - at) < 1e-6:
            cues[-1][1] = at + dur
        else:
            cues.append([at, at + dur, t])
    out = folder + '.mp4'
    srt = folder + '.srt'
    with open(srt, 'w', encoding='utf-8') as f:
        for i, (a, b, t) in enumerate(cues, 1):
            f.write('%d\n%s --> %s\n%s\n\n' % (i, stamp(a), stamp(b), t))

    # Transcript: mỗi lần câu thoại đổi.
    transcript = folder + '_transcript.txt'
    with open(transcript, 'w', encoding='utf-8') as f:
        last = ''
        for (_, _, m), (at, _) in zip(frames, starts):
            if m['line'] and m['line'] != last:
                f.write('%s  %-22s %-16s %s\n' % (stamp(at, '.')[:8], m['beat'], m['day'].strip(), m['line']))
            last = m['line']

    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', concat, '-i', meta, '-i', srt,
                    '-map', '0:v', '-map', '2:s', '-map_metadata', '1', '-map_chapters', '1', '-vf', 'fps=8',
                    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '26', '-pix_fmt', 'yuv420p',
                    '-c:s', 'mov_text', '-metadata:s:s:0', 'language=vie', '-metadata:s:s:0', 'title=Bot debug',
                    '-movflags', '+faststart', out], check=True)
    print('%s · %d khung · %.0f phút · %d chương · %d cue phụ đề' % (out, len(frames), clock / 60, len(chapters), len(cues)))
    print(srt)
    print(transcript)


if __name__ == '__main__':
    main()
