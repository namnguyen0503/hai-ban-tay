#!/usr/bin/env python3
"""Dựng trailer 2–3 phút từ các lượt BotVideo quay cảnh (BOT_VIDEO=1 BOT_VIDEO_FPS=30 BOT_VIDEO_HD=1 BOT_VIDEO_UI=0).

Dùng: python3 Tools/make_trailer.py [Logs/trailer/runs.txt]
  runs.txt mỗi dòng: <tên đoạn> <thư mục Logs/video/<run>> (Logs/trailer/record.sh ghi).
Ra Logs/trailer/: trailer_1080p.mp4 (chiếu lớp), trailer_small.mp4 (< 30 MB, 720p), trailer.ass (phụ đề + thẻ chữ).

Mỗi cảnh neo theo câu thoại hoặc beat trong frames.txt, không theo giây cố định: quay lại lượt khác vẫn cắt đúng chỗ.
Tiếng: file VO của chính câu đó đặt đúng lúc câu bắt đầu trong lượt quay; nền nhạc game (menu / voyage / ending) nhỏ dưới thoại.
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTENT = os.path.join(ROOT, 'Game', 'Assets', '_Project', 'Resources', 'Content')
AUDIO = os.path.join(ROOT, 'Game', 'Assets', '_Project', 'Audio')
FONTS = os.path.join(ROOT, 'Game', 'Assets', '_Project', 'Fonts')
OUT = os.environ.get('TRAILER_OUT') or os.path.join(ROOT, 'Logs', 'trailer')
FPS = 60
# Bản gốc 2560×1440 (quay 2K); bản 1080p thu từ bản gốc. Phụ đề viết theo lưới 1920×1080, libass tự phóng.
W, H = 2560, 1440
# Khung 20:9 kiểu phim: hình 2560×1152 giữa hai dải đen 144 px (108 px ở 1080p), phụ đề nằm trong dải dưới.
SCOPE_H = 1152
BAR = (H - SCOPE_H) // 2
# Màu kiểu phim: tương phản nhẹ, bóng ngả xanh, sáng ngả cam, viền tối, hạt phim.
# Không hạt phim: hạt làm bản 1440p60 ~33 Mb/s, VLC giải mã không kịp, trông giật (người dùng 04/11).
GRADE = ('eq=contrast=1.07:saturation=0.9:gamma=0.97,colorbalance=rs=-0.04:bs=0.05:rh=0.05:bh=-0.04,'
         'vignette=angle=PI/5')
FIELDS = ('beat', 'day', 'pos', 'holding', 'line', 'prompt', 'guide')

# ---------- Kịch bản cắt ----------
# ('card', giây, [(style, chữ), ...], nhạc)  ·  ('clip', đoạn, neo đầu, neo cuối, nhạc[, tốc độ[, giữ khung cuối giây]])
# Tốc độ < 1: quay chậm (chỉ cảnh không lời; câu thoại trong cảnh chậm bị bỏ).
# Neo: ('line', id, lệch giây) = lúc câu bắt đầu; ('end', id, lệch) = lúc câu kết thúc; ('beat', tên, lệch); ('t', giây trong lượt).
CUTS = [
    ('card', 3.0, [('Place', 'Sài Gòn, 1911')], 'menu'),
    # Anh Lê nói xong thì cắt thẳng sang lúc Ba đứng nhìn chớp bóng, giữ tới hết câu (người dùng 06/11: bỏ đoạn đi bộ giữa hai câu).
    # Hết câu Lê thì tối dần rồi hiện dần ngọn đèn (người dùng 06/11), không chớp tối nhanh.
    ('clip', 'prologue', ('line', 'pro_le_show', -0.6), ('end', 'pro_le_show', 0.4), 'menu', 1.0, 0.0, 'soft'),
    ('clip', 'prologue', ('line', 'pro_ba_light', -0.4), ('end', 'pro_ba_light', 0.15), 'menu'),
    ('clip', 'prologue', ('end', 'pro_le_buy_ice', -0.1), ('end', 'pro_ba_ice', 0.6), 'menu'),
    ('clip', 'prologue', ('line', 'pro_ba_abroad', -0.3), ('line', 'pro_ba_invite', -0.15), 'menu'),
    ('clip', 'prologue', ('line', 'pro_le_money', -0.3), ('end', 'pro_ba_work2', 0.8), 'menu'),
    # Nhạc menu lặp lại từ đầu ở thẻ tựa (menu2: khúc mới, mở dần) thay vì vòng lặp cứng giữa chừng.
    ('card', 3.0, [('Title', 'HAI BÀN TAY')], 'menu2'),
    ('card', 2.0, [('Place', 'Bến Nhà Rồng, 5/6/1911')], 'menu2'),
    ('clip', 'prologue', ('line', 'job_ba_ask', -0.4), ('end', 'job_staff_no', 0.5), 'menu2'),
    # Neo cuối +0.4: câu nhận việc của thuyền trưởng bắt đầu +0.48 sau "bất cứ việc gì".
    ('clip', 'prologue', ('line', 'job_captain_work', -0.4), ('end', 'job_ba_any', 0.4), 'menu2'),
    # Hai cảnh không lời ngắn lại, cắt trước cú quay ngoắt của máy quay (+46.0 và +63.15 trong B1.3).
    ('clip', 'prologue', ('beat', 'B1.3', 43.5), ('beat', 'B1.3', 45.9), 'menu2', 0.6),
    ('clip', 'prologue', ('beat', 'B1.3', 63.25), ('beat', 'B1.3', 65.4), 'menu2'),
    ('card', 3.0, [('Place', 'Sáu ngày làm phụ bếp'), ('Small', 'trên tàu Amiral Latouche-Tréville')], 'voyage'),
    # Chỉ câu đầu của Jacques: hai câu sau rơi vào lúc Ba đã ra boong tối, xuống hầm (lượt quay 04/11).
    ('clip', 'day4', ('line', 'jacques_d4_1', -0.6), ('end', 'jacques_d4_1', 0.4), 'voyage'),
    ('clip', 'day4', ('beat', 'D4.haul', 48.0), ('beat', 'D4.haul', 50.0), 'voyage'),
    ('clip', 'day4', ('beat', 'D4.haul', 37.0), ('beat', 'D4.haul', 39.8), 'voyage', 0.6),
    ('clip', 'day6', ('line', 'chieu_1', -0.8), ('end', 'ba_chieu', 1.5), 'voyage'),
    ('card', 3.0, [('Place', 'Marseille, 6/7/1911')], 'ending'),
    # Đoạn cuối chậm lại (người dùng 04/11: nhịp cuối quá nhanh): mỗi cảnh có chỗ thở trước và sau câu nói.
    ('clip', 'marseille', ('line', 'mrs_arrival', -0.5), ('end', 'mrs_arrival', 1.4), 'ending'),
    ('clip', 'marseille', ('line', 'mrs_ba_poor', -1.2), ('end', 'mrs_ba_poor', 1.0), 'ending'),
    # Quán: chỉ lúc bồi bàn đặt tách (máy quay nhìn xuống bàn là đúng cảnh); câu ông Mai sau đó rơi vào khung chỉ thấy mặt bàn
    # trong lượt quay cũ nên bỏ (quay lại Marseille với Cinematic thì có thể mở lại tới mrs_mai_monsieur2).
    ('clip', 'marseille', ('line', 'mrs_waiter_voila', -1.0), ('end', 'mrs_ba_thanks', 0.9), 'ending'),
    # Câu chốt: ông Mai nói tiếp sau 0,4 s nên cắt ngay sau câu, giữ khung cuối 1,8 s rồi tối dần sang thẻ kết.
    ('clip', 'marseille', ('line', 'mrs_ba_why1', -1.2), ('end', 'mrs_ba_why2', 0.35), 'ending', 1.0, 1.8),
    ('card', 8.0, [('Title', 'HAI BÀN TAY'),
                   ('Small', 'Theo Trần Dân Tiên, "Những mẩu chuyện về đời hoạt động của Hồ Chủ tịch"'),
                   ('Tiny', None)], 'ending'),
]
MUSIC = {'menu': 'Music/menu.ogg', 'menu2': 'Music/menu.ogg', 'voyage': 'Music/voyage.ogg', 'ending': 'Music/ending.ogg'}
FADE = 0.35
# Hai cảnh quay liền nhau (không qua thẻ chữ): chớp tối ngắn, không dìm đen 0,7 s giữa mọi cặp cảnh.
FADE_CUT = 0.12
# Cảnh nối mềm ('soft' ở trường thứ 8): tối dần rồi hiện dần.
FADE_SOFT = 0.5
# Khung thiếu ở đầu/cuối cảnh tới chừng này giây thì giữ khung (đứng hình ngắn trong lúc mờ dần), quá thì co cảnh.
PAD_MAX = 0.6


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit('Lỗi: ' + ' '.join(cmd[:6]) + '…\n' + r.stderr[-3000:])
    return r.stdout


def load_content():
    sub = json.load(open(os.path.join(CONTENT, 'subtitles.vi.json'), encoding='utf-8'))
    words = json.load(open(os.path.join(CONTENT, 'french_words.vi.json'), encoding='utf-8'))
    ui = json.load(open(os.path.join(CONTENT, 'ui_strings.vi.json'), encoding='utf-8'))
    meaning = {p['id']: p['vi'] for p in words['pairs']}
    lines = {l['id']: l for l in sub['lines']}
    strings = ui.get('strings', ui)
    return lines, sub['speakers'], meaning, strings


def render_vi(text, meaning):
    # Trailer coi như từ đã học: {w:id} hiện nghĩa tiếng Việt (như cuối chuyến đi trong game).
    return re.sub(r'\{w:([a-z_]+)\}', lambda m: meaning.get(m.group(1), m.group(1)), text)


def load_run(folder):
    frames, starts = [], []
    last = ''
    with open(os.path.join(folder, 'frames.txt'), encoding='utf-8') as f:
        for raw in f:
            p = raw.rstrip('\n').split('\t')
            if len(p) < 2 or not p[0].isdigit():
                continue
            path = os.path.join(folder, 'f_%06d.jpg' % int(p[0]))
            if not os.path.exists(path):
                continue
            meta = dict(zip(FIELDS, p[2:] + [''] * len(FIELDS)))
            t = float(p[1])
            frames.append((t, path, meta))
            lid = meta['line'].split(' · ')[0] if meta['line'] else ''
            if lid and lid != last:
                starts.append((t, lid))
            last = lid
    return frames, starts


def anchor(run_data, a, lines):
    frames, starts = run_data
    kind = a[0]
    if kind == 't':
        return frames[0][0] + a[1]
    if kind == 'line':
        for t, lid in starts:
            if lid == a[1]:
                return t + a[2]
    elif kind == 'end':
        for t, lid in starts:
            if lid == a[1]:
                return t + lines[a[1]]['duration'] + a[2]
    elif kind == 'beat':
        for t, _, m in frames:
            if m['beat'].endswith(' ' + a[1]) or m['beat'] == a[1]:
                return t + a[2]
    return None


def ass_time(t):
    cs = int(round(t * 100))
    return '%d:%02d:%02d.%02d' % (cs // 360000, cs // 6000 % 60, cs // 100 % 60, cs % 100)


def ass_escape(s):
    return s.replace('\\', '\\\\').replace('{', '(').replace('}', ')').replace('\n', '\\N')


def main():
    os.makedirs(OUT, exist_ok=True)
    runs_file = sys.argv[1] if len(sys.argv) > 1 else os.path.join(OUT, 'runs.txt')
    runs = {}
    for raw in open(runs_file, encoding='utf-8'):
        parts = raw.split()
        if len(parts) == 2 and os.path.isdir(parts[1]):
            runs[parts[0]] = load_run(parts[1])
    lines, speakers, meaning, strings = load_content()
    tmp = os.path.join(OUT, 'parts')
    os.makedirs(tmp, exist_ok=True)

    segs, events, vo = [], [], []   # segs: (mp4, dur, music); events: ass; vo: (global t, wav)
    clock = 0.0
    for i, cut in enumerate(CUTS):
        if cut[0] == 'card':
            _, dur, texts, music = cut
            mp4 = os.path.join(tmp, 'p%02d.mp4' % i)
            run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'lavfi', '-i', 'color=c=black:s=%dx%d:r=%d:d=%.3f' % (W, H, FPS, dur),
                 '-c:v', 'libx264', '-crf', '16', '-preset', 'fast', '-pix_fmt', 'yuv420p', mp4])
            y = {1: [540], 2: [500, 600], 3: [470, 575, 960]}[len(texts)]
            for k, ((style, text), yy) in enumerate(zip(texts, y)):
                if text is None:
                    text = strings.get('credits.disclaimer', '')
                fade = '{\\fad(500,500)\\pos(960,%d)}' % yy
                # Dòng sau hiện chậm hơn dòng trên một nhịp (tựa → nguồn → lời thông báo).
                events.append((clock + 0.15 + 0.5 * k, clock + dur - 0.1, style, fade + ass_escape(text)))
            print('Thẻ  %2d %6.2f → %6.2f  %s' % (i, clock, clock + dur, texts[0][1]))
            segs.append((mp4, dur, music))
            clock += dur
            continue
        _, name, a0, a1, music = cut[:5]
        speed = cut[5] if len(cut) > 5 else 1.0
        hold = cut[6] if len(cut) > 6 else 0.0
        soft = len(cut) > 7 and cut[7] == 'soft'
        soft_before = i > 0 and len(CUTS[i - 1]) > 7 and CUTS[i - 1][7] == 'soft'
        fade_in = FADE_SOFT if soft_before else FADE if i == 0 or CUTS[i - 1][0] == 'card' else FADE_CUT
        fade_out = FADE_SOFT if soft else FADE if i + 1 >= len(CUTS) or CUTS[i + 1][0] == 'card' else FADE_CUT
        if hold:
            fade_out = min(1.2, hold)
        if name not in runs:
            print('Bỏ cảnh %d: chưa quay đoạn %s' % (i, name))
            continue
        frames, starts = runs[name]
        t0, t1 = anchor(runs[name], a0, lines), anchor(runs[name], a1, lines)
        if t0 is None or t1 is None or t1 <= t0:
            print('Bỏ cảnh %d (%s): không tìm thấy neo %s / %s' % (i, name, a0, a1))
            continue
        pick = [(t, p) for t, p, _ in frames if t0 <= t <= t1]
        if len(pick) < 5:
            print('Bỏ cảnh %d (%s): ít khung' % (i, name))
            continue
        # Neo rơi ra ngoài beat đã quay (BOT_VIDEO_BEATS) thì không có khung: cảnh co về đúng đoạn có hình. Không co thì hình ngắn hơn
        # đồng hồ tiếng/chữ, mọi cảnh sau trễ dần (bản 04/11: lệch 6,4 s, thẻ kết đen 6 s rồi mới hiện chữ).
        # Thiếu ít (≤ PAD_MAX) thì giữ khung đầu/cuối cho đủ chỗ thở (khuất trong fade); thiếu nhiều thì co cảnh.
        step = 1.0 / FPS
        lead, tail = pick[0][0] - t0, t1 - pick[-1][0] - step
        pad_in = lead if step < lead <= PAD_MAX else 0.0
        pad_out = tail if step < tail <= PAD_MAX else 0.0
        if lead > PAD_MAX or tail > PAD_MAX:
            print('  Cảnh %d (%s): thiếu khung đầu %.2f s / cuối %.2f s, co cảnh — quay thêm beat kề nếu cần chỗ thở'
                  % (i, name, lead, tail))
        if lead > step and not pad_in:
            t0 = pick[0][0]
        if tail > step and not pad_out:
            t1 = pick[-1][0] + step
        hold += pad_out
        gaps = [(a, b - a) for (a, _), (b, _) in zip(pick, pick[1:]) if b - a > 0.25]
        if gaps:
            sys.exit('Cảnh %d (%s): hình đứt giữa cảnh %s — quay lại với đủ beat' % (i, name, ', '.join('%.1f s ở %.1f' % (g, a) for a, g in gaps)))
        concat = os.path.join(tmp, 'p%02d.txt' % i)
        with open(concat, 'w', encoding='utf-8') as f:
            for k, (t, p) in enumerate(pick):
                nxt = pick[k + 1][0] if k + 1 < len(pick) else t + 1.0 / FPS
                f.write("file '%s'\nduration %.4f\n" % (p, min(max(nxt - t, 0.005), 0.5)))
            f.write("file '%s'\n" % pick[-1][1])
        dur = (t1 - t0) / speed + hold - pad_out
        mp4 = os.path.join(tmp, 'p%02d.mp4' % i)
        # Quay chậm: nội suy chuyển động ra đủ 60 khung (nhân khung thì 36 → 60 giật nhịp).
        retime = ('setpts=PTS/%.4f,minterpolate=fps=%d:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1' % (speed, FPS)
                  if speed != 1.0 else 'fps=%d' % FPS)
        if hold or pad_in:
            retime += ',tpad=start_mode=clone:start_duration=%.3f:stop_mode=clone:stop_duration=%.3f' % (pad_in, hold)
        vf = ('%s,scale=%d:%d,crop=%d:%d:0:%d,%s,pad=%d:%d:0:%d:black,'
              'fade=t=in:st=0:d=%.2f,fade=t=out:st=%.3f:d=%.2f'
              % (retime, W, H, W, SCOPE_H, BAR, GRADE, W, H, BAR, fade_in, max(dur - fade_out, 0), fade_out))
        run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', concat, '-vf', vf,
             '-t', '%.3f' % dur, '-c:v', 'libx264', '-crf', '16', '-preset', 'fast', '-pix_fmt', 'yuv420p', mp4])
        # Đồng hồ tiếng/chữ theo độ dài thật của phần hình vừa dựng (không theo độ dài dự tính).
        real = float(run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', mp4]).strip())
        if abs(real - dur) > 0.5 / FPS:
            print('  Cảnh %d (%s): hình dài %.3f s, dự tính %.3f s' % (i, name, real, dur))
        dur = real
        for t, lid in (starts if speed == 1.0 else []):
            if lid not in lines or not (t0 - 0.2 <= t < t1 - 0.3):
                continue
            line = lines[lid]
            at = clock + max(t - t0, 0.0)
            # Chỉ câu nói trọn trong cảnh: câu của người kế tiếp vừa bắt đầu ở cuối cảnh thì tiếng và chữ tràn sang cảnh sau.
            if at + line['duration'] > clock + dur + 0.4:
                print('  Cảnh %d (%s): câu %s bắt đầu %.2f s trước khi hết cảnh nhưng không trọn, bỏ tiếng/chữ — kiểm neo cuối'
                      % (i, name, lid, clock + dur - at))
                continue
            end = min(at + line['duration'] + 0.3, clock + dur)
            who = speakers.get(line['speaker'], '')
            # Một dòng: tên người nói (vàng nhạt) rồi câu; câu Pháp nghiêng nhỏ ở dòng trên. Gọn trong dải đen 108 px.
            text = '{\\c&H7FD8FF&\\fs34}%s  {\\c&HFFFFFF&\\fs44}%s' % (ass_escape(who), ass_escape(render_vi(line['vi'], meaning)))
            if line.get('fr'):
                text = '{\\i1\\fs30\\c&HC8C8C8&}%s{\\i0\\r}\\N' % ass_escape(line['fr']) + text
            events.append((at, end, 'Sub', text))
            wav = os.path.join(AUDIO, line['audio'] + '.wav') if line.get('audio') else ''
            if os.path.exists(wav):
                vo.append((at, wav))
        segs.append((mp4, dur, music))
        print('Cảnh %2d %6.2f → %6.2f  %-9s nguồn %6.1f → %6.1f (%4.1f s)' % (i, clock, clock + dur, name, t0, t1, dur))
        clock += dur

    total = clock
    # Hai câu liền nhau không chồng chữ: câu trước tắt ngay trước khi câu sau hiện.
    subs = sorted([e for e in events if e[2] == 'Sub'], key=lambda e: e[0])
    trimmed = {}
    for a, b in zip(subs, subs[1:]):
        if a[1] > b[0] - 0.05:
            trimmed[id(a)] = (a[0], max(a[0] + 0.3, b[0] - 0.05), a[2], a[3])
    events = [trimmed.get(id(e), e) for e in events]
    # Phụ đề + thẻ chữ (libass, font của game).
    ass = os.path.join(OUT, 'trailer.ass')
    with open(ass, 'w', encoding='utf-8') as f:
        f.write('[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nWrapStyle: 0\nScaledBorderAndShadow: yes\n\n')
        f.write('[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, '
                'Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n')
        f.write('Style: Sub,Be Vietnam Pro,44,&H00FFFFFF,&H00FFFFFF,&H00000000,&H96000000,0,0,0,0,100,100,0,0,1,2,0,2,120,120,30,1\n')
        f.write('Style: Title,Noto Serif,120,&H00E8F4FF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,12,0,1,0,0,5,0,0,0,1\n')
        f.write('Style: Place,Noto Serif,84,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,2,0,1,0,0,5,0,0,0,1\n')
        f.write('Style: Small,Be Vietnam Pro,42,&H00C8C8C8,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,200,200,0,1\n')
        f.write('Style: Tiny,Be Vietnam Pro,28,&H00A0A0A0,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,260,260,0,1\n\n')
        f.write('[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n')
        for a, b, style, text in events:
            f.write('Dialogue: 0,%s,%s,%s,,0,0,0,,%s\n' % (ass_time(a), ass_time(b), style, text))

    # Hình: nối các phần (cùng codec), chưa có chữ.
    lst = os.path.join(tmp, 'video.txt')
    with open(lst, 'w', encoding='utf-8') as f:
        for mp4, _, _ in segs:
            f.write("file '%s'\n" % mp4)
    silent = os.path.join(tmp, 'video.mp4')
    run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c', 'copy', silent])

    # Nhạc: mỗi khúc nhạc liền nhau thành một đoạn, mở/tắt dần.
    groups = []
    at = 0.0
    for _, dur, music in segs:
        if groups and groups[-1][0] == music:
            groups[-1][2] += dur
        else:
            groups.append([music, at, dur])
        at += dur
    inputs, filters, labels = [], [], []
    for k, (music, start, dur) in enumerate(groups):
        inputs += ['-stream_loop', '-1', '-i', os.path.join(AUDIO, MUSIC[music])]
        filters.append('[%d:a]atrim=0:%.3f,asetpts=PTS-STARTPTS,afade=t=in:d=1.2,afade=t=out:st=%.3f:d=1.5,volume=0.32,'
                       'adelay=%d|%d,aformat=sample_rates=48000:channel_layouts=stereo[m%d]'
                       % (k, dur, max(dur - 1.5, 0), int(start * 1000), int(start * 1000), k))
        labels.append('[m%d]' % k)
    base = len(groups)
    for k, (t, wav) in enumerate(vo):
        inputs += ['-i', wav]
        filters.append('[%d:a]adelay=%d|%d,aformat=sample_rates=48000:channel_layouts=stereo,volume=1.6[v%d]'
                       % (base + k, int(t * 1000), int(t * 1000), k))
    music_mix = '%samix=inputs=%d:normalize=0[music]' % (''.join(labels), len(labels))
    filters.append(music_mix)
    if vo:
        # Key sidechain phải dài bằng cả trailer: hết câu thoại cuối mà key dừng thì nhạc tắt theo (bản 04/11: im 2,6 s trước thẻ kết).
        filters.append('%samix=inputs=%d:normalize=0,apad=whole_dur=%.3f,asplit=2[vo][key]' % (''.join('[v%d]' % k for k in range(len(vo))), len(vo), total))
        filters.append('[music][key]sidechaincompress=threshold=0.03:ratio=6:attack=20:release=400[duck]')
        filters.append('[duck][vo]amix=inputs=2:normalize=0,apad,atrim=0:%.3f,alimiter=limit=0.95[a]' % total)
    else:
        filters.append('[music]apad,atrim=0:%.3f[a]' % total)
    audio = os.path.join(tmp, 'audio.wav')
    run(['ffmpeg', '-y', '-loglevel', 'error'] + inputs + ['-filter_complex', ';'.join(filters), '-map', '[a]', audio])

    sub_filter = "ass='%s':fontsdir='%s'" % (ass.replace("'", r"\'"), FONTS)
    master = os.path.join(OUT, 'trailer_1440p.mp4')
    run(['ffmpeg', '-y', '-loglevel', 'error', '-i', silent, '-i', audio, '-vf', sub_filter, '-c:v', 'libx264', '-crf', '19',
         '-preset', 'slow', '-tune', 'film', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-t', '%.3f' % total, '-movflags', '+faststart', master])
    # Máy chiếu lớp thường 1080p: thu từ bản 2K (lanczos) cho cạnh mượt hơn quay thẳng 1080p.
    big = os.path.join(OUT, 'trailer_1080p.mp4')
    run(['ffmpeg', '-y', '-loglevel', 'error', '-i', master, '-vf', 'scale=1920:1080:flags=lanczos', '-c:v', 'libx264', '-crf', '18',
         '-preset', 'slow', '-pix_fmt', 'yuv420p', '-c:a', 'copy', '-movflags', '+faststart', big])
    # Bản gửi qua chat: dưới 30 MB, 720p, hai lượt mã hóa theo dung lượng.
    kbps = int(27.5 * 8 * 1024 / total) - 128
    small = os.path.join(OUT, 'trailer_small.mp4')
    log = os.path.join(tmp, 'x264pass')
    common = ['-vf', 'scale=1280:720', '-c:v', 'libx264', '-b:v', '%dk' % kbps, '-preset', 'slow', '-pix_fmt', 'yuv420p', '-passlogfile', log]
    run(['ffmpeg', '-y', '-loglevel', 'error', '-i', big] + common + ['-pass', '1', '-an', '-f', 'mp4', os.devnull])
    run(['ffmpeg', '-y', '-loglevel', 'error', '-i', big] + common + ['-pass', '2', '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', small])
    print('%s · %.0f s · %d cảnh · %d câu thoại' % (master, total, len(segs), len(vo)))
    print(big)
    print('%s · %.1f MB' % (small, os.path.getsize(small) / 1048576))


if __name__ == '__main__':
    main()
