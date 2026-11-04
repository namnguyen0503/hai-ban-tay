"""Tạo tiếng hiệu cho bản đạo diễn (Docs/DIRECTOR.md §6, §7, §9), tổng hợp hoàn toàn (CC0), chỉ cần Python 3 + NumPy.

  ship_bell_8.wav        tám tiếng chuông tàu (giờ đổi phiên 04:00), gõ theo cặp: ding-ding, nghỉ, ... bốn cặp.
  first_time.wav         nốt gảy ngắn, mềm, phát khi Ba gặp một thứ lần đầu.
  colombo_dockers.wav    (Audio/Ambience) tiếng hò khuân vác bến Colombo vọng qua mặt nước, tối ngày 3: một người xướng, cả
                         nhóm đáp, tiếng kiện hàng nện theo nhịp; không lời (nguyên âm tổng hợp), lọc xa, lặp liền 25,6 s.

Dùng: python3 Tools/audio/make_story_sfx.py [thư mục SFX ra, mặc định Game/Assets/_Project/Audio/SFX]
      (colombo_dockers.wav ghi vào thư mục Ambience cạnh thư mục SFX)
"""
import sys
import wave
from pathlib import Path

import numpy as np

SR = 48000
ROOT = Path(__file__).resolve().parents[2]


def bell_strike(seconds, base):
    """Chuông đồng: họa âm không điều hòa (tỉ lệ chuông nhà thờ/tàu), họa âm cao tắt nhanh hơn; tiếng gõ kim loại ở đầu."""
    t = np.arange(int(seconds * SR)) / SR
    x = np.zeros_like(t)
    for ratio, gain, decay in [(0.5, 0.35, 1.6), (1.0, 1.0, 1.9), (1.19, 0.55, 1.3), (1.5, 0.4, 1.0), (2.0, 0.45, 0.8),
                               (2.51, 0.25, 0.55), (3.01, 0.15, 0.4)]:
        f = base * ratio
        x += gain * np.sin(2 * np.pi * f * t + ratio) * np.exp(-t / decay)
    rng = np.random.default_rng(int(base))
    click = rng.normal(0, 1, int(0.012 * SR)) * np.linspace(1, 0, int(0.012 * SR))
    x[:click.size] += 0.25 * click
    attack = np.minimum(1, t / 0.002)
    return x * attack


def ship_bell():
    total = 9.5
    out = np.zeros(int(total * SR))
    base = 587.0
    starts = []
    for pair in range(4):
        start = pair * 2.05
        starts += [start, start + 0.42]
    for k, s in enumerate(starts):
        strike = bell_strike(total - s, base * (1 + 0.002 * (k % 2)))
        i = int(s * SR)
        out[i:i + strike.size] += strike[:out.size - i] * (0.95 if k % 2 == 0 else 0.8)
    return out


def first_time():
    """Dây gảy (Karplus–Strong) nốt A5, tắt êm trong ~1,6 s, thêm quãng tám rất nhẹ."""
    seconds = 1.8
    n = int(seconds * SR)
    out = np.zeros(n)
    rng = np.random.default_rng(1911)
    for freq, gain in [(880.0, 1.0), (1760.0, 0.18)]:
        period = int(SR / freq)
        buf = rng.uniform(-1, 1, period)
        buf = np.convolve(buf, np.ones(8) / 8, mode="same")
        y = np.zeros(n)
        for i in range(n):
            y[i] = buf[i % period]
            buf[i % period] = 0.996 * 0.5 * (buf[i % period] + buf[(i + 1) % period])
        out += gain * y
    t = np.arange(n) / SR
    return out * np.exp(-t / 0.55) * np.minimum(1, t / 0.004)


# Formant nam (Hz, băng thông Hz, khuếch đại): nguyên âm "o" (xướng) và "a" (đáp).
VOWEL_O = [(450, 80, 1.0), (800, 90, 0.6), (2830, 160, 0.12)]
VOWEL_A = [(730, 90, 1.0), (1090, 110, 0.55), (2440, 160, 0.12)]


def voice(f0, vowel, rng, breath=0.03):
    """Giọng hát không lời: tổng họa âm theo đường cao độ f0 (mảng Hz), độ lớn mỗi họa âm theo bao formant của nguyên âm."""
    n = f0.size
    phase = 2 * np.pi * np.cumsum(f0) / SR
    out = np.zeros(n)
    for k in range(1, 40):
        fk = k * f0
        # Nguồn thanh quản nghiêng −6 dB/quãng tám (1/k), thanh quản lọc qua ba formant của nguyên âm.
        gain = (0.35 + 3.0 * sum(g * np.exp(-0.5 * ((fk - f) / bw) ** 2) for f, bw, g in vowel)) / k
        gain = np.where(fk < 3800, gain, 0.0)
        out += gain * np.sin(k * phase + rng.uniform(0, 2 * np.pi))
    out += breath * rng.normal(0, 1, n) * np.abs(np.sin(phase / 2))
    return out


def phrase(seconds, start_hz, end_hz, vowel, rng, vibrato=5.2):
    t = np.arange(int(seconds * SR)) / SR
    glide = start_hz + (end_hz - start_hz) * np.clip(t / seconds, 0, 1) ** 1.6
    f0 = glide * (1 + 0.012 * np.sin(2 * np.pi * vibrato * t + rng.uniform(0, 6)) + 0.004 * rng.normal(0, 1, t.size).cumsum() / np.sqrt(t.size))
    env = np.minimum(1, t / 0.09) * np.minimum(1, (seconds - t) / 0.25) ** 1.3
    return voice(f0, vowel, rng) * np.clip(env, 0, 1)


def lowpass(x, cutoff):
    """Lọc thông thấp một cực, chạy hai lần (xuôi rồi ngược) cho mềm và không lệch pha."""
    a = np.exp(-2 * np.pi * cutoff / SR)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i in range(x.size):
            acc = (1 - a) * x[i] + a * acc
            y[i] = acc
        x = y[::-1]
    return x


def colombo_dockers():
    """
    Tám nhịp xướng–đáp (3,2 s/nhịp, 25,6 s): người xướng kéo "ô" lên rồi xuống, bảy người đáp "a" thấp lệch nhau, một tiếng kiện hàng
    nện ở cuối câu đáp. Đặt xa ~170 m: lọc 900 Hz, vang mặt nước, tiếng nhỏ. Đuôi vang cộng vòng về đầu để lặp không nối.
    """
    rng = np.random.default_rng(1911)
    beat, beats = 3.2, 8
    total = beat * beats
    n = int(total * SR)
    out = np.zeros(n + int(3 * SR))
    for b in range(beats):
        s = b * beat
        lead = phrase(1.15, 168 + rng.uniform(-6, 6), 142, VOWEL_O, rng)
        i = int((s + rng.uniform(0, 0.05)) * SR)
        out[i:i + lead.size] += 0.8 * lead
        for v in range(7):
            base = 118 * 2 ** (rng.uniform(-0.6, 0.6) / 12) * (1.5 if v == 6 else 1.0)
            reply = phrase(0.85 + rng.uniform(-0.05, 0.08), base * 1.06, base, VOWEL_A, rng, vibrato=4.6 + v * 0.2)
            j = int((s + 1.35 + rng.uniform(-0.06, 0.08)) * SR)
            out[j:j + reply.size] += 0.32 * reply
        thud_n = int(0.35 * SR)
        tt = np.arange(thud_n) / SR
        thud = (np.sin(2 * np.pi * 62 * tt) + 0.5 * rng.normal(0, 1, thud_n) * np.exp(-tt / 0.02)) * np.exp(-tt / 0.09)
        k = int((s + 2.25) * SR)
        out[k:k + thud_n] += 0.5 * thud
    out = lowpass(out, 900.0)
    # Vang mặt nước: vài dội muộn tắt dần.
    wet = np.zeros_like(out)
    for delay, gain in [(0.11, 0.45), (0.23, 0.32), (0.41, 0.22), (0.67, 0.14), (0.95, 0.09), (1.4, 0.05)]:
        d = int(delay * SR)
        wet[d:] += gain * out[:-d]
    out = out + lowpass(wet, 600.0)
    loop = out[:n].copy()
    tail = out[n:]
    loop[:tail.size] += tail
    return loop


def write_loop(path, x, peak_db):
    """Như write nhưng không fade cuối: clip lặp liền."""
    x = x / max(1e-9, np.abs(x).max()) * 10 ** (peak_db / 20)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{path.name}: {x.size / SR:.2f} s (lặp)")


def write(path, x, peak_db=-3.0):
    x = x / max(1e-9, np.abs(x).max()) * 10 ** (peak_db / 20)
    fade = int(0.03 * SR)
    x[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())
    print(f"{path.name}: {x.size / SR:.2f} s")


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Game/Assets/_Project/Audio/SFX"
    out.mkdir(parents=True, exist_ok=True)
    write(out / "ship_bell_8.wav", ship_bell(), -4.0)
    write(out / "first_time.wav", first_time(), -9.0)
    ambience = out.parent / "Ambience"
    ambience.mkdir(parents=True, exist_ok=True)
    write_loop(ambience / "colombo_dockers.wav", colombo_dockers(), -6.0)


if __name__ == "__main__":
    main()
