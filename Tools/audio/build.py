"""Build the Hai Ban Tay staging pack. Python 3 + NumPy + ffmpeg/ffprobe only.

All generated audio and original processing contributions are dedicated to CC0.
No Unity, git, network credentials, or writes outside the two approved folders.
"""
import argparse
import hashlib
import html
import json
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

import numpy as np
from download import download, ROOT

OUT = ROOT.parents[1] / "Staging" / "audio"
SR = 48000
RNG = np.random.default_rng(19110605)
SOURCES = {}
CACHE = {}
MANIFEST = []
GENERATED = "CC0 (generated)"
GEN_URL = "../../Tools/audio/build.py"


def command(args, data=None):
    return subprocess.run(args, input=data, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, check=True)


def pcm(x):
    return np.asarray(x, dtype="<f4").tobytes()


def decode(data, filters=None, channels=2):
    args = ["ffmpeg", "-v", "error", "-i", "pipe:0"]
    if filters:
        args += ["-af", filters]
    args += ["-ar", str(SR), "-ac", str(channels), "-f", "f32le", "pipe:1"]
    return np.frombuffer(command(args, data).stdout, dtype="<f4").reshape(-1, channels).copy()


def source(name, member=None):
    tag = (name, member)
    if tag not in CACHE:
        path = ROOT / "sources" / (name + Path(SOURCES[name]["url"]).suffix)
        if member:
            with zipfile.ZipFile(path) as z:
                data = z.read("Audio/" + member + ".ogg")
        else:
            data = path.read_bytes()
        CACHE[tag] = decode(data)
    return CACHE[tag].copy()


def mono(x):
    return x.mean(axis=1) if x.ndim == 2 else x


def unit(x):
    return x / max(float(np.max(np.abs(x))), 1e-10)


def fade(x, seconds=.006):
    x = x.copy()
    n = min(int(SR * seconds), len(x) // 2)
    w = np.linspace(0, 1, n)
    if x.ndim == 2:
        w = w[:, None]
    x[:n] *= w
    x[-n:] *= w[::-1]
    return x


def trim(x):
    x = mono(x).astype(np.float64)
    x -= x.mean()
    block = int(.005 * SR)
    levels = np.sqrt(np.array([np.mean(x[i:i+block]**2) for i in range(0,len(x),block)]))
    active = np.flatnonzero(levels > np.max(levels) * .004)
    assert len(active), "Silent SFX"
    x = x[active[0]*block:min(len(x),(active[-1]+1)*block)]
    return fade(x, .003)


def stretch(x, speed):
    indices = np.arange(0, len(x) - 1, speed)
    if x.ndim == 1:
        return np.interp(indices, np.arange(len(x)), x)
    return np.stack([np.interp(indices, np.arange(len(x)), c) for c in x.T], axis=1)


def noise(seconds, low, high):
    n = int(seconds * SR)
    f = np.fft.rfftfreq(n, 1 / SR)
    z = np.fft.rfft(RNG.standard_normal(n))
    shape = (1 - np.exp(-(f / max(low, 1)) ** 4)) * np.exp(-(f / high) ** 4)
    shape[0] = 0
    y = np.fft.irfft(z * shape, n)
    return y / max(y.std(), 1e-9)


def resonance(seconds, frequencies, decay=.4):
    t = np.arange(int(seconds * SR)) / SR
    y = sum(np.sin(2 * np.pi * f * t + RNG.uniform(0, .2)) *
            np.exp(-t / (decay / (1 + i * .28))) / (1 + i)
            for i, f in enumerate(frequencies))
    return fade(y, .002)


def place(dst, sound, second, gain=1, pan=0):
    pos = int(second * SR)
    end = min(len(dst), pos + len(sound))
    if pos < 0 or end <= pos:
        return
    sound = sound[:end-pos]
    if dst.ndim == 1:
        dst[pos:end] += mono(sound) * gain
    elif sound.ndim == 2:
        dst[pos:end] += sound * gain
    else:
        dst[pos:end, 0] += sound * gain * np.sqrt((1-pan)/2)
        dst[pos:end, 1] += sound * gain * np.sqrt((1+pan)/2)


def mixed(seconds, events):
    out = np.zeros(int(seconds * SR))
    for second, sound, gain in events:
        place(out, sound, second, gain)
    return out


def excerpt(name, seconds, index=0, band=None):
    """Choose separated high-energy events; log exact source offset in notes."""
    x = source(name)
    if band:
        x = decode_bytes_filter(x, f"highpass=f={band[0]},lowpass=f={band[1]}")
    m = mono(x)
    step = int(.02 * SR)
    energy = np.array([np.mean(m[i:i+step] ** 2) for i in range(0, len(m), step)])
    candidates = []
    for i in np.argsort(energy)[::-1]:
        second = i * .02
        if second < .05 or second + seconds > len(x) / SR:
            continue
        if all(abs(second - s) > seconds + .3 for s in candidates):
            candidates.append(second)
        if len(candidates) >= index + 1:
            break
    assert len(candidates) > index, f"Not enough events: {name}"
    start = max(0, candidates[index] - .06)
    end = start + seconds
    return x[int(start*SR):int(end*SR)], f"Trích {start:.3f}–{end:.3f}s từ bản nguồn."


def decode_bytes_filter(x, af):
    args = ["ffmpeg", "-v", "error", "-f", "f32le", "-ar", str(SR), "-ac", "2",
            "-i", "pipe:0", "-af", af, "-f", "f32le", "pipe:1"]
    return np.frombuffer(command(args, pcm(x)).stdout, dtype="<f4").reshape(-1, 2).copy()


def metadata(ids, notes):
    ids = list(dict.fromkeys(ids))
    if not ids:
        return GEN_URL, "Hai Bàn Tay — tổng hợp NumPy", GENERATED, notes
    return ("; ".join(SOURCES[i]["page"] for i in ids),
            "; ".join(dict.fromkeys(SOURCES[i]["author"] for i in ids)),
            "CC0", notes + " Phần phối/tổng hợp bổ sung được hiến CC0.")


def write(key, x, kind="SFX", ids=(), notes=""):
    if kind == "SFX":
        x = unit(trim(x)) * 10 ** (-3/20)
        suffix, codec = ".wav", ["-c:a", "pcm_s16le"]
    else:
        x -= x.mean(axis=0)
        x *= 10 ** ((-20 - loudness(x))/20)
        assert np.max(np.abs(x)) < .97, f"Loudness target clips: {key}"
        suffix, codec = ".ogg", ["-c:a", "libvorbis", "-q:a", "5"]
    path = OUT / kind / (key + suffix)
    channels = 1 if x.ndim == 1 else x.shape[1]
    args = ["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(SR),
            "-ac", str(channels), "-i", "pipe:0", *codec, str(path)]
    command(args, pcm(x))
    url, author, license_, notes = metadata(ids, notes)
    MANIFEST.append(dict(key=key, file=f"{kind}/{key}{suffix}", kind=kind,
                         duration_s=round(len(x)/SR, 6), loop=kind != "SFX",
                         source_url=url, author=author, license=license_, notes=notes))
    print(key, round(len(x)/SR, 3), flush=True)


def loudness(x):
    channels = 1 if x.ndim == 1 else x.shape[1]
    args = ["ffmpeg", "-hide_banner", "-f", "f32le", "-ar", str(SR), "-ac", str(channels),
            "-i", "pipe:0", "-af", "ebur128=peak=true", "-f", "null", "-"]
    log = command(args, pcm(x)).stderr.decode()
    return float(re.findall(r"I:\s+(-?[\d.]+) LUFS", log)[-1])


def seamless(x, crossfade_s=4):
    """Overlap the extra tail with the head; new loop origin follows head."""
    c = int(crossfade_s * SR)
    w = (.5 - .5 * np.cos(np.linspace(0, np.pi, c)))[:, None]
    y = np.concatenate([x[c:-c], x[-c:] * (1-w) + x[:c] * w])
    # Tiny boundary correction prevents a sample discontinuity after overlap.
    n = 192
    y[-n:] += (.5-.5*np.cos(np.linspace(0,np.pi,n)))[:,None] * (y[0]-y[-1])
    return y


def bed(seconds, low, high, amount=.1):
    a = noise(seconds, low, high)
    b = noise(seconds, low, high)
    t = np.arange(len(a)) / SR
    mod = .7 + .17*np.sin(2*np.pi*t/17) + .1*np.sin(2*np.pi*t/7.3)
    return np.stack([a, .55*a+.84*b], axis=1) * mod[:,None] * amount


def repeat_source(name, seconds, gain=1):
    x = source(name)
    # Crossfade every internal repetition as well as the final loop boundary.
    c = min(SR, len(x)//8)
    w = np.linspace(0, 1, c)[:,None]
    out = x.copy()
    while len(out) < seconds*SR:
        out = np.concatenate([out[:-c], out[-c:]*(1-w)+x[:c]*w, x[c:]])
    return out[:int(seconds*SR)] * gain


def build_sfx():
    pot = mono(source("rpg", "metalPot1"))
    metal = mono(source("impact", "impactMetal_light_001"))
    soft = mono(source("impact", "impactSoft_heavy_001"))
    wood = mono(source("impact", "impactWood_heavy_001"))
    mining = mono(source("impact", "impactMining_001"))
    cloth = mono(source("rpg", "cloth2"))
    write("metal_clank", pot, ids=["rpg"], notes="Audio/metalPot1.ogg; va nồi kim loại.")
    write("sack_thud", mixed(1.5, [(0,stretch(soft,.8),.7),(.01,wood,.12),(.03,cloth,.22)]),
          ids=["impact","rpg"], notes="impactSoft_heavy_001 + impactWood_heavy_001 + cloth2; phối bao vải đặt trên gỗ.")
    for key, length, count, base, speed in [
        ("coal_pour",1.8,24,mining,.8), ("potatoes",1.5,11,soft,1.5),
        ("coin",.9,5,mono(source("rpg","handleCoins")),1.3)]:
        times = np.sort(RNG.uniform(.02,length-.35,count))
        events = [(s,stretch(base,speed*RNG.uniform(.8,1.2)),RNG.uniform(.2,.6)) for s in times]
        write(key,mixed(length+.5,events),ids=["impact" if key != "coin" else "rpg"],
              notes={"coal_pour":"impactMining_001; phối hạt va khô mô phỏng than, không phải bản thu than.",
                     "potatoes":"impactSoft_heavy_001; phối vật mềm đổ rời vào thùng gỗ, mô phỏng khoai.",
                     "coin":"handleCoins; vài cụm xu nhỏ, làm mềm va để gợi lòng bàn tay."}[key])
    for key, duration, count, freqs in [
        ("tram_bell",2.8,2,[780,1211,1947,2740]),
        ("cup_saucer",.7,2,[2350,3605,5150]),
        ("pot_place",1.1,1,[390,677,1120])]:
        events=[(i*.19,resonance(duration-i*.19,freqs,.6 if key=="tram_bell" else .16),1/(i+1)) for i in range(count)]
        x=mixed(duration,events)
        if key=="pot_place":
            place(x,unit(pot),0,.65)
        write(key,x,ids=["rpg"] if key=="pot_place" else [],
              notes="Tổng hợp cộng hưởng vật liệu; chuông cơ hai nhịp / sứ / nồi. Không phải bản thu thiết bị năm 1911.")
    for key,name,length in [("sweep","sweep",1.35),("rope","rope",1.8),
                            ("pencil_write","pencil",2.5),("service_bell","service",2.8)]:
        x,note=excerpt(name,length)
        write(key,x,ids=[name],notes=note)
    write("page_turn",source("page"),ids=["page"],notes="Cắt im lặng từ bản thu lật trang sách.")
    write("chain_rattle",source("chain"),ids=["chain"],notes="Xích nặng; chuyển mono và cắt im lặng.")
    write("sizzle",fade(mono(source("sizzle"))[SR:SR*7//2],.035),ids=["sizzle"],notes="Trích 1–3.5s từ chảo dầu nóng.")
    write("door_open",stretch(source("rpg","doorOpen_1"),.85),ids=["rpg"],notes="Audio/doorOpen_1.ogg; hạ tốc độ 0.85 để gợi cửa nặng.")
    write("door_close",mixed(2,[(0,stretch(source("rpg","doorClose_1"),.85),1),(.08,wood,.12)]),
          ids=["rpg","impact"],notes="Audio/doorClose_1.ogg + impactWood_heavy_001; phối cửa gỗ nặng.")
    for material, name, count in [("wood","woodsteps",4),("stone","stonesteps",4)]:
        for i in range(count):
            x,note=excerpt(name,.36,i)
            write(f"footstep_{material}_{i+1}",x,ids=[name],notes=note)
    for i in range(3):
        base=mono(source("impact",f"impactMetal_heavy_{i:03d}"))
        x=mixed(.8,[(0,stretch(soft,1.1+i*.05),.7),(.012,stretch(base,1.3+i*.06),.33)])
        write(f"footstep_metal_{i+1}",x,ids=["impact"],notes=f"impactSoft_heavy_001 + impactMetal_heavy_{i:03d}; phối bước chân trên tấm kim loại.")
    for key,secs,low,high,decay in [("ash_pour",1.6,400,8500,1.2),("leaf",.5,1100,11000,.12),
                                   ("scrape",1.1,700,7000,.45),("rake",1.4,180,5000,.6),
                                   ("shovel",1.35,100,4200,.5)]:
        t=np.arange(int(secs*SR))/SR
        y=noise(secs,low,high)*np.sin(np.pi*np.minimum(t/.12,1)/2)*np.exp(-t/decay)
        if key in ["scrape","rake","shovel"]:
            y*= .5+.5*np.sin(2*np.pi*(11 if key=="rake" else 23)*t)**2
            y+=resonance(secs,[337,816,1450],.13)*.35
        write(key,y,notes="Âm thiết kế NumPy: nhiễu lọc, nhịp ma sát, cộng hưởng; mô phỏng vật liệu, không phải thu hiện trường.")
    t=np.arange(4*SR)/SR
    f=62 + .3*np.sin(2*np.pi*.7*t)
    phase=2*np.pi*np.cumsum(f)/SR
    env=np.clip(t/.35,0,1)*np.clip((4-t)/.65,0,1)
    horn=sum(np.sin(phase*h+.03*h)/(h**1.4) for h in range(1,11))
    horn+=.5*np.sin(2*np.pi*93*t)+.07*noise(4,200,2400)
    write("ship_horn",horn*env,notes="Còi hơi thiết kế 62/93 Hz, họa âm và hơi thoát; 4s. Không gắn nhãn bản thu tàu lịch sử.")
    t=np.arange(int(1.6*SR))/SR
    whistle=(np.sin(2*np.pi*2230*t)+.55*np.sin(2*np.pi*2670*t))
    env=np.minimum(t/.08,1)*np.minimum((1.6-t)/.14,1)
    env*=1-.75*np.exp(-((t-.62)/.06)**2)
    write("whistle_bosun",(whistle+.12*noise(1.6,1600,6000))*env,notes="Còi thủy thủ hai cao độ; tổng hợp, không dùng giọng người.")
    y=noise(2.1,100,3200)*.3
    for s in RNG.uniform(.05,1.9,16):
        place(y,resonance(.2,[RNG.uniform(300,750),1400],.04),s,.25)
    write("water_wash",fade(y,.08),notes="Tổng hợp nước khuấy/cọ chậu: nhiễu thấp và bọt cộng hưởng.")
    wave,note=excerpt("sea",3.2)
    write("wave_crash",wave,ids=["sea"],notes=note+" Sóng bờ được phối làm sóng đập boong; không phải bản thu trên tàu.")
    for i in range(2):
        gull,note=excerpt("gulls",1.3,i,band=(1200,7500))
        write(f"gull_{i+1}",gull,ids=["gulls"],notes=note+" Lọc 1.2–7.5kHz; còn lớp nền cảng nhẹ.")
    for key,member,speed in [("ui_click","bookPlace1",1.8),("ui_open","bookOpen",1.2),("ui_close","bookClose",1.15)]:
        write(key,stretch(source("rpg",member),speed),ids=["rpg"],notes=f"Audio/{member}.ogg; UI giấy/gỗ. Peak −3dBFS; dùng volume thấp khi tích hợp.")
    t=np.arange(4*SR)/SR
    y=noise(4,100,1800)*(.07+.03*np.sin(2*np.pi*6*t)**2)
    for s in np.arange(0,3.9,.24):
        place(y,stretch(unit(wood),RNG.uniform(1.2,1.6)),s,.23)
    write("cart_wheels",fade(y,.12),ids=["impact"],notes="impactWood_heavy_001 + nhiễu đá; thiết kế bánh xe gỗ lăn trên đá, không có động cơ.")


def build_ambience():
    seconds=64
    for key in ["engine","storm","marseille_street","nharong_quay","saigon_evening",
                "sea_deck","galley","harbor_water","hold_cold"]:
        t=np.arange(seconds*SR)/SR
        ids=[]
        if key=="engine":
            y=bed(seconds,32,700,.055)
            pulse=.55+.2*np.sin(2*np.pi*1.6*t)+.12*np.sin(2*np.pi*3.2*t)
            motor=sum(np.sin(2*np.pi*f*t)/a for f,a in [(48,1),(96,2.5),(144,4),(288,6)])*pulse*.13
            y+=motor[:,None]
            note="Máy hơi nước thiết kế: chu kỳ piston 1.6Hz, rung thân tàu 48/96/144Hz; không phải động cơ diesel."
        elif key in ["sea_deck","harbor_water","storm"]:
            y=repeat_source("sea",seconds,.35 if key=="storm" else .5)
            ids=["sea"]
            y+=bed(seconds,80,1900,.08 if key=="storm" else .018)
            note="Sóng biển thu ở bờ, phối gió để gợi thân/boong tàu; không phải bản thu năm 1911."
            if key=="storm":
                y+=bed(seconds,1700,10000,.055)
                for s in [14,43]:
                    thunder=noise(5,35,700)*np.exp(-np.arange(5*SR)/SR/1.7)
                    place(y,fade(thunder,.05),s,.17,RNG.uniform(-.5,.5))
                note+=" Gió, mưa và sấm thấp tổng hợp; hai đợt sấm xa."
            if key=="harbor_water":
                g=unit(mono(source("gulls")))
                for s in [9,31,50]:
                    place(y,g[10*SR:13*SR],s,.025,-.45)
                ids.append("gulls")
                note+=" Mòng biển xa từ bản thu cảng."
        elif key in ["marseille_street","nharong_quay","saigon_evening"]:
            y=bed(seconds,180,1300,.006)
            ids=["impact"]
            rolling=mono(source("impact","impactWood_light_001"))
            for s in np.arange(5,seconds-1,.46):
                place(y,rolling,s,.045*(.5+.5*np.sin(s/8)),np.sin(s/9)*.5)
            if key=="marseille_street":
                crowd=decode_bytes_filter(source("french"),"highpass=f=200,lowpass=f=3200")
                y+=repeat_array(crowd,seconds)*.22
                y+=repeat_source("horse",seconds,.1)
                birds=unit(mono(source("gulls")))
                for s in [7,27,48]:
                    place(y,birds[15*SR:17*SR],s,.025,.5)
                bell=resonance(2.8,[780,1211,1947],.7)
                place(y,bell,38,.015,-.6)
                ids += ["french","horse","gulls"]
                note="Tiếng Pháp xa (Outside Talks #6), vó ngựa, bánh xe gỗ thiết kế, chim biển và chuông tram cơ. Tái dựng cảnh Marseille, không phải bản thu địa điểm/năm 1911."
            else:
                # ponytail: nonlinguistic texture, replace with licensed Vietnamese walla when available.
                for i in range(8):
                    murmur=noise(seconds,140+i*25,700+i*80)
                    syllables=(.4+.6*np.sin(2*np.pi*(2.3+i*.17)*t+i)**2)
                    phrases=np.maximum(0,np.sin(2*np.pi*t/(5+i*.5)+i))**2
                    y+=np.stack([murmur,murmur],axis=1)*(syllables*phrases*.006)[:,None]
                note="Phối phố/bến: âm nền người không ngôn ngữ bằng nhiễu lọc; KHÔNG có tiếng nói/rao Việt xác thực. Không giọng AI hay mô phỏng danh tính."
                if key=="nharong_quay":
                    y+=repeat_source("sea",seconds,.14)
                    ids.append("sea")
                    for s in [16,40]:
                        call=noise(2,250,1600)*np.sin(np.linspace(0,np.pi,2*SR))**2
                        place(y,call,s,.013,-.5)
                    note+=" Sóng biển nhẹ làm nước vỗ sông; lớp gọi xa chỉ là texture vô ngôn ngữ, cần thay nếu muốn rao rõ."
                else:
                    for s in np.arange(0,seconds,.8):
                        chirp_t=np.arange(int(.11*SR))/SR
                        chirp=np.sin(2*np.pi*4400*chirp_t)*np.sin(np.linspace(0,np.pi,len(chirp_t)))**2
                        place(y,chirp,s,.014,.6)
                    note+=" Côn trùng tổng hợp 4.4kHz; xe kéo thiết kế, không có xe máy/ô tô."
        elif key=="galley":
            y=bed(seconds,150,4200,.018)
            boil=bed(seconds,80,1300,.014)
            y+=boil
            for s in RNG.uniform(0,seconds-1,220):
                place(y,fade(noise(.035,1800,12000),.002),s,RNG.uniform(.008,.035),RNG.uniform(-.8,.8))
            for s in [8,23,41,56]:
                place(y,mono(source("rpg","metalPot2")),s,.09,-.4)
            ids=["rpg"]
            note="Lửa than lách tách và nước sôi tổng hợp, metalPot2 xa; không quạt điện hay bếp gas."
        else:
            y=bed(seconds,35,400,.05)
            y+=.016*np.sin(2*np.pi*57*t)[:,None]
            for s in [3,9,18,29,37,49,58]:
                drop=resonance(.4,[710,1210],.055)
                place(y,drop,s,.035,RNG.uniform(-.7,.7))
            note="Hầm lạnh thiết kế: ù thấp, nước nhỏ giọt, không tiếng máy nén lạnh hiện đại."
        write(key,seamless(y),kind="Ambience",ids=ids,notes=note+" Loop 60s, crossfade tròn 4s, stereo 48kHz, −20 LUFS.")


def repeat_array(x, seconds):
    return np.tile(x,(int(np.ceil(seconds*SR/len(x))),1))[:int(seconds*SR)]


def build_music():
    for key,name,start,length in [("menu","menu",0,49),
                                  ("voyage","piano",0,64),
                                  ("ending","piano",12,64)]:
        x=source(name)[int(start*SR):int((start+length)*SR)]
        assert len(x)==length*SR
        # Four-second circular overlap; no silent padding at the seam.
        y=seamless(x,4)
        write(key,y,"Music",[name],f"Piano solo không lời; trích {start}–{start+length}s, crossfade tròn 4s. Voyage/ending dùng cùng chủ đề ở hai đoạn khác nhau. −20 LUFS; giảm volume trong Unity khi có thoại.")


def docs():
    (OUT/"manifest.json").write_text(json.dumps(MANIFEST,ensure_ascii=False,indent=2)+"\n")
    rows=["| Key | Nguồn / tác giả | Giấy phép | Giây | Lặp |", "|---|---|---|---:|:---:|"]
    for m in MANIFEST:
        rows.append(f"| `{m['key']}` | {m['author']} — {m['source_url']} | {m['license']} | {m['duration_s']:.3f} | {'Có' if m['loop'] else 'Không'} |")
    readme="""# Hai Bàn Tay — audio staging (bước K)

Chỉ asset, chưa import Unity. 43 SFX mono WAV PCM16, 9 Ambience và 3 Music stereo OGG Vorbis quality 5, tất cả 48 kHz.
SFX peak −3 dBFS; loop mục tiêu −20 LUFS (EBU R128). File `validation.json` chứa số đo từ file xuất cuối.

## Tái tạo

Từ thư mục dự án:
```sh
python3 -B Tools/audio/build.py
python3 -B Tools/audio/build.py --verify-only
```
Cần Python 3, NumPy, ffmpeg có libvorbis/ebur128, ffprobe. Không cần sox, pip, tài khoản, API key, Unity, git.
Download cache, SHA-256 và bằng chứng trang CC0 nằm trong `Tools/audio/sources/` và `Tools/audio/sources.json`.
Nếu muốn tải lại, xóa riêng file cache nguồn cần tải; SHA-256 vẫn được kiểm tra. Lỗi tải/giấy phép/hash/DSP/kiểm tra khiến script dừng.
Script chỉ ghi `Tools/audio/` và `Staging/audio/`; ghi đè asset do script tạo khi tái tạo. Seed NumPy cố định 19110605.
PCM thiết kế xác định trên cùng toolchain; byte OGG có thể khác giữa phiên bản encoder. Không ghi `.meta` hoặc sửa game.

## Cách làm và lựa chọn

- Dùng foley CC0 thật khi phù hợp; trích đoạn, hạ cao độ hoặc phối vật liệu. Manifest ghi file trong pack và thời gian trích.
- Tổng hợp nhiễu theo dải tần, cộng hưởng suy giảm, hơi thoát, ma sát, mưa, gió, ù thân tàu; không model âm thanh hoặc AI voice.
- SFX cắt bằng RMS cửa sổ 5ms, ngưỡng −48 dB so với RMS lớn nhất; fade 3ms chống click, peak normalize, xuất PCM16 mono.
- Loop: overlap head/tail cosine 4s (không fade về im lặng), dời điểm bắt đầu, chỉnh biên 4ms, gain tuyến tính theo EBU R128; kiểm tra lại sau encode/decode.
- Music là piano solo CC0: `menu` dùng Wolfgang_; `voyage` và `ending` dùng hai đoạn cùng chủ đề Centurion_of_war. Độ dài lần lượt 45/60/60s.
- Asset là tái dựng nghệ thuật cho 1911, không phải bản thu lịch sử. Không dùng tram điện tử, động cơ diesel, còi ô tô, tiếng máy lạnh hiện đại.
- `nharong_quay` / `saigon_evening`: texture người tổng hợp vô ngôn ngữ; **chưa có tiếng rao/tiếng Việt thật**. Đây là lựa chọn ghi rõ để không dùng lời Pháp cho cảnh Việt. Có thể thay lớp người bằng bản thu Việt CC0 sau.
- `wave_crash` / nước bến: sóng bờ biển phối làm nước thân tàu/boong/sông. `gull_*` còn nền cảng nhẹ. Khoai/than/tro/chậu/sứ/bánh xe là âm thiết kế; không gắn nhãn bản thu thật.
- Kiểm tra tự động xác nhận key, codec, số kênh, sample rate, thời lượng, peak, LUFS, biên loop, im lặng biên và dung lượng. **Chưa có người nghe thẩm định cảm giác vật liệu hoặc tính xác thực lịch sử.**
- Các `ui_*` vẫn peak −3dBFS theo yêu cầu; dùng gain thấp khi tích hợp. −20 LUFS không thay thế ducking nhạc lúc thoại.

## Giấy phép và nguồn

Tất cả nguồn bên ngoài bên dưới là CC0, dùng thương mại và không bắt buộc ghi công. Mọi phần tổng hợp/phối mới cũng hiến CC0 1.0 Universal (`CC0 (generated)` nếu hoàn toàn tổng hợp).
Giữ manifest để truy vết nguồn; bằng chứng license lưu nguyên trang tải. Không dùng Freesound có login, nhạc có bản quyền hay AI voice người thật.

- Kenney: https://kenney.nl/assets/impact-sounds ; https://kenney.nl/assets/rpg-audio ; https://kenney.nl/support
- BigSoundBank: URL riêng từng âm trong manifest; https://bigsoundbank.com/licenses.html
- OpenGameArt: https://opengameart.org/content/regret-short-emotional-piano ; https://opengameart.org/content/emotional-piano-0
- CC0: https://creativecommons.org/publicdomain/zero/1.0/

## Nghe thử

Mở `preview.html` trực tiếp trong trình duyệt. Không cần server, mạng hoặc autoplay. Bật Lặp để nghe biên nối; kiểm tra biên phát ở cuối từng loop.

## Key còn thiếu

Không thiếu tên key. Giới hạn nội dung: tiếng rao/tiếng Việt xác thực chưa có; ghi rõ trên hai ambience liên quan.

## Bảng asset

"""
    (OUT/"README.md").write_text(readme+"\n".join(rows)+"\n")
    cards=[]
    for m in MANIFEST:
        loop="checked" if m["loop"] else ""
        cards.append(f'<article><strong>{html.escape(m["key"])}</strong> <small>{m["duration_s"]:.2f}s · {m["kind"]} · {m["license"]}</small><br><audio controls preload="none" src="{m["file"]}" {"loop" if m["loop"] else ""}></audio> <label><input type="checkbox" {loop} onchange="this.closest(\'article\').querySelector(\'audio\').loop=this.checked">Lặp</label><p>{html.escape(m["notes"])}</p></article>')
    (OUT/"preview.html").write_text('''<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Hai Bàn Tay — nghe thử audio</title><style>body{max-width:1000px;margin:30px auto;padding:0 20px;background:#191c21;color:#eee;font:16px system-ui}article{padding:16px;margin:10px 0;background:#272d34;border-radius:8px}small,p{color:#bcc6cd}p{font-size:13px}audio{margin:10px 0;width:min(500px,100%)}label{padding:12px}input[type=search]{font:inherit;padding:10px;width:90%}</style><h1>Hai Bàn Tay — audio staging</h1><p>CC0 · 48 kHz · SFX −3 dBFS · Ambience/Music −20 LUFS. Loop bật sẵn cho nền và nhạc. Phát từng âm; tự dừng âm trước. Chưa thẩm định bằng người nghe.</p><input type="search" aria-label="Lọc key" placeholder="Lọc key hoặc loại âm" oninput="document.querySelectorAll('article').forEach(a=>a.hidden=!a.innerText.toLowerCase().includes(this.value.toLowerCase()))">'''+"\n".join(cards)+'''<script>document.addEventListener('play',e=>{if(e.target.tagName==='AUDIO')document.querySelectorAll('audio').forEach(a=>{if(a!==e.target)a.pause()})},true)</script></html>''')


EXPECTED_SFX = "metal_clank sack_thud coal_pour tram_bell sweep shovel ship_horn service_bell scrape rope rake potatoes leaf coin ash_pour door_open door_close water_wash sizzle pot_place cup_saucer page_turn pencil_write chain_rattle wave_crash gull_1 gull_2 ui_click ui_open ui_close whistle_bosun cart_wheels".split() + [f"footstep_{m}_{i}" for m,n in [("wood",4),("metal",3),("stone",4)] for i in range(1,n+1)]
EXPECTED_AMB = "engine storm marseille_street nharong_quay saigon_evening sea_deck galley harbor_water hold_cold".split()


def verify():
    manifest=json.loads((OUT/"manifest.json").read_text())
    expected=set(EXPECTED_SFX+EXPECTED_AMB+["menu","voyage","ending"])
    assert len(manifest)==len(expected) and {m["key"] for m in manifest}==expected
    report=[]
    for m in manifest:
        p=OUT/m["file"]
        info=json.loads(command(["ffprobe","-v","quiet","-show_streams","-show_format","-of","json",str(p)]).stdout)
        stream=info["streams"][0]
        channels=1 if m["kind"]=="SFX" else 2
        assert stream["channels"]==channels and int(stream["sample_rate"])==SR,m["key"]
        assert stream["codec_name"]==("pcm_s16le" if channels==1 else "vorbis")
        x=decode(p.read_bytes(),channels=channels)
        duration=len(x)/SR
        assert abs(duration-m["duration_s"])<.005,m["key"]
        peak=20*np.log10(np.max(np.abs(x)))
        row=dict(key=m["key"],duration_s=round(duration,6),sample_rate=SR,channels=channels,peak_dbfs=round(float(peak),3),sha256=hashlib.sha256(p.read_bytes()).hexdigest())
        if channels==1:
            assert abs(peak+3)<.06,(m["key"],peak)
            threshold=float(np.max(np.abs(x)))*10**(-55/20)
            active=np.flatnonzero(np.max(np.abs(x),axis=1)>threshold)
            lead=active[0]/SR;trail=(len(x)-1-active[-1])/SR
            row.update(leading_quiet_ms=round(lead*1000,3),trailing_quiet_ms=round(trail*1000,3))
            assert lead<.02 and trail<.02,(m["key"],lead,trail)
        else:
            assert 30<=duration<=90 and m["loop"]
            lufs=loudness(x)
            seam=float(np.max(np.abs(x[0]-x[-1])))
            differences=np.abs(np.diff(x,axis=0))
            q=float(np.quantile(differences,.999))
            row.update(integrated_lufs=lufs,seam_delta=round(seam,7),adjacent_delta_p999=round(q,7),crossfade_s=4)
            assert abs(lufs+20)<=.4,(m["key"],lufs)
            assert peak < -1,(m["key"],peak)
            assert seam<max(.008,2*q),(m["key"],seam,q)
            a=np.sqrt(np.mean(x[:SR//10]**2));b=np.sqrt(np.mean(x[-SR//10:]**2))
            row["boundary_rms_ratio_db"]=round(float(20*np.log10(a/b)),3)
            assert abs(row["boundary_rms_ratio_db"])<6,(m["key"],row)
        report.append(row)
    size=sum(p.stat().st_size for p in OUT.rglob("*") if p.is_file())
    assert size<120_000_000,size
    result=dict(assets=len(report),sfx=len(EXPECTED_SFX),ambience=len(EXPECTED_AMB),music=3,total_bytes=size,
                missing_keys=[],validation="Technical checks only; no human listening or Unity test.",files=report)
    validation_path = OUT/"validation.json"
    validation_path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n")
    result["total_bytes"] = sum(p.stat().st_size for p in OUT.rglob("*") if p.is_file())
    validation_path.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n")
    assert result["total_bytes"] < 120_000_000
    print(f"PASS: {len(report)} assets, {result['total_bytes']/1e6:.2f} MB",flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--verify-only",action="store_true")
    args=parser.parse_args()
    for tool in ["ffmpeg","ffprobe"]:
        assert shutil.which(tool),f"Required executable missing: {tool}"
    if args.verify_only:
        verify();return
    for kind in ["SFX","Ambience","Music"]:
        (OUT/kind).mkdir(parents=True,exist_ok=True)
    for s in download():
        SOURCES[s["id"]]=s
    for name in ["impact","rpg"]:
        with zipfile.ZipFile(ROOT/"sources"/(name+".zip")) as z:
            (ROOT/"sources"/(name+"-License.txt")).write_bytes(z.read("License.txt"))
    build_sfx()
    build_ambience()
    build_music()
    docs()
    verify()


if __name__=="__main__":
    main()
