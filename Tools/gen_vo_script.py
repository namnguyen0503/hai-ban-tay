#!/usr/bin/env python3
"""Sinh kịch bản thu âm từ JSON và tham chiếu C#; chỉ dùng thư viện chuẩn.

Dùng: python3 Tools/gen_vo_script.py [--content <Resources/Content>] [--output -]
--output - in ra stdout, không ghi tài liệu.
"""
import argparse
import json
import re
import sys
import wave
from collections import defaultdict
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
CONTENT = ROOT / "Game/Assets/_Project/Resources/Content"
TOKEN = re.compile(r"\{w:([a-z0-9_]+)\}")


def strings(value, pointer="$"):
    """Duyệt giá trị chuỗi, giữ đường dẫn JSON làm bằng chứng."""
    if isinstance(value, str):
        yield value, pointer
    elif isinstance(value, dict):
        for key, item in value.items():
            yield from strings(item, f"{pointer}.{key}")
    elif isinstance(value, list):
        for index, item in enumerate(value):
            yield from strings(item, f"{pointer}[{index}]")


def cell(value):
    return str(value).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("|", "\\|").replace("\n", "<br>")


def scene_for_code(path):
    name = path.as_posix()
    if "Prologue" in name or "Saigon" in path.stem:
        return "00_Prologue"
    if "NhaRong" in path.stem:
        return "01_NhaRong"
    if "/Ending/" in name or "Marseille" in path.stem or path.stem in {"PayClerkRoutine", "WaiterRoutine"}:
        return "03_Marseille"
    if "/Shift/" in name or path.stem in {"DayCast", "DayScenery", "GalleyNpcs", "GalleyChores", "GalleySceneBuilder", "ScrubPotTask", "MatchWordsTask", "StudyTask"}:
        return "02_Galley · ngày ? (dùng theo trạng thái/tác vụ)"
    return None


def audio_target(key):
    if not key:
        return None
    path = PurePosixPath(key)
    if not key.startswith("VO/") or len(path.parts) < 2 or ".." in path.parts or "\\" in key or path.suffix:
        raise ValueError(f"Audio key VO không hợp lệ: {key!r}")
    return Path("Game/Assets/_Project/Audio") / (key + ".wav")


def render(content):
    data = {p.name: json.loads(p.read_text(encoding="utf-8")) for p in sorted(content.glob("*.json"))}
    subtitles = data["subtitles.vi.json"]
    lines = subtitles["lines"]
    ids = [line["id"] for line in lines]
    if len(ids) != len(set(ids)):
        raise ValueError("ID phụ đề bị trùng.")
    project = content.parent.parent
    scripts = project / "Scripts"
    vo = project / "Audio/VO"
    if not scripts.is_dir() or not vo.is_dir():
        raise ValueError("--content phải trỏ Resources/Content của project có Scripts và Audio/VO.")
    locations, evidence = defaultdict(set), defaultdict(set)
    known = set(ids)
    days = data.get("days.vi.json", {}).get("days", [])
    words = data.get("french_words.vi.json", {}).get("pairs", [])
    lessons = data.get("study.vi.json", {}).get("lessons", [])

    for name, document in data.items():
        if name == "subtitles.vi.json":
            continue
        for value, pointer in strings(document):
            if value in known:
                evidence[value].add(f"{name}:{pointer}")
                if name == "ending.vi.json":
                    locations[value].add("03_Marseille")
        if name == "days.vi.json":
            for day in days:
                for value, pointer in strings(day):
                    if value in known:
                        locations[value].add(f"02_Galley · Ngày {day['number']} · {day['leg']} ({pointer})")

    # Thoại bài học được tham chiếu gián tiếp qua set/lesson của buổi tối.
    for day in days:
        evening = day.get("evening", {})
        sources = []
        if evening.get("study") == "match":
            sources = [word for word in words if word.get("set") == evening.get("set")]
        elif evening.get("lesson"):
            sources = [lesson for lesson in lessons if lesson["id"] == evening["lesson"]]
        if evening.get("storyLesson"):
            sources.extend(lesson for lesson in lessons if lesson["id"] == evening["storyLesson"])
        for value, pointer in strings(sources):
            if value in known:
                locations[value].add(f"02_Galley · Ngày {day['number']} · buổi tối (set/lesson)")

    # Chỉ quét mã sản phẩm; test/validator không chứng minh thoại được phát trong game.
    for path in sorted(scripts.rglob("*.cs")):
        scene = scene_for_code(path.relative_to(scripts))
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            for value in re.findall(r'"([^"\n]+)"', line):
                if value in known:
                    evidence[value].add(f"Scripts/{path.relative_to(scripts).as_posix()}:{number}")
                    if scene:
                        locations[value].add(scene)

    word_vi = {word["id"]: word["vi"] for word in words}
    groups = defaultdict(list)
    for line in lines:
        groups[line.get("speaker") or "?"].append(line)
    text = [
        "# VO_SCRIPT — Kịch bản thu âm", "",
        "Sinh bằng `python3 Tools/gen_vo_script.py`; không sửa tay bảng bên dưới. Không có thời điểm sinh để chạy lại cho kết quả ổn định.", "",
        "## Hướng dẫn thu", "",
        "- Thu WAV PCM 48 kHz, mono, 24 bit; một câu một file. Giữ giọng trang trọng, đọc rõ, tránh vọng phòng và tiếng nền.",
        "- Mức thu đề nghị: đỉnh khoảng −12 đến −6 dBFS, không clipping; giữ âm lượng nhất quán giữa các vai. Đây là hướng dẫn thu, chưa phải số đo audio hiện có.",
        "- Có câu tiếng Pháp thì thu câu Pháp; cột Việt là bản nghĩa để diễn đạt đúng. Câu chỉ có tiếng Việt thì thu tiếng Việt. Nhờ người biết tiếng Pháp nghe lại.",
        "- `duration` trong JSON phải khớp thời lượng WAV thực (sai số tối đa 0,4 giây). Khi tái thu, cập nhật theo WAV mới; không tăng tốc giọng để ép vừa thời lượng.",
        "- Đặt tên theo audio key: `VO/tên` → `Game/Assets/_Project/Audio/VO/tên.wav`. Dấu `?` ở file đích nghĩa là audio key đang trống, cần người tích hợp chốt key; generator không tự đặt tên hay sửa JSON.",
        "- Sau khi đặt WAV, đóng Unity Editor rồi chạy `./Tools/rebuild_scenes.sh` để AssetFactory dựng lại AudioLibrary; sau đó `./Tools/bake_lighting.sh` vì rebuild xóa lightmap cũ. Chỉ thực hiện khi Unity rảnh.",
        "- Sinh lại khi Content hoặc VO thay đổi. `--content <đường-dẫn-Resources/Content> --output -` đọc project được chọn và in ra stdout.", "",
        "## Phạm vi và cách đọc", "",
        f"Nguồn: `{content.relative_to(ROOT).as_posix() if content.is_relative_to(ROOT) else content.as_posix()}`; **{len(lines)} câu**, **{len(groups)} người nói**.", "",
        "Bảng phản ánh Content và WAV thực có trong project được chọn.", "",
        "Cảnh/ngày suy ra từ tham chiếu ID trong JSON khác và mã C# cùng project; cột bằng chứng giữ vị trí tham chiếu. Tham chiếu C# dùng chung có thể chưa xác định được ngày. Không thấy nơi dùng thì ghi `?`; không coi việc định nghĩa phụ đề là bằng chứng sử dụng.", "",
        "Cột Việt thay `{w:id}` bằng nghĩa Việt trong `french_words.vi.json` cho người thu dễ đọc. Trong game, từ chưa học có thể vẫn hiện tiếng Pháp. Token chưa tra được giữ nguyên. Cột ‘đã có file’ chỉ kiểm sự tồn tại đúng WAV đích, chưa xác nhận chất lượng, định dạng hoặc import Unity.", "",
    ]
    present = 0
    for speaker in sorted(groups):
        label = subtitles.get("speakers", {}).get(speaker, speaker)
        text += [f"## {cell(label)} (`{speaker}`)", "", "| ID | Cảnh/ngày dùng | Tiếng Pháp | Tiếng Việt | Mục tiêu (giây) | File đích | Đã có file | Bằng chứng |", "|---|---|---|---|---|---|---|---|"]
        for line in sorted(groups[speaker], key=lambda item: item["id"]):
            identifier = line["id"]
            target = audio_target(line.get("audio"))
            exists = target is not None and (vo / target.relative_to("Game/Assets/_Project/Audio/VO")).is_file()
            present += bool(exists)
            vietnamese = TOKEN.sub(lambda match: word_vi.get(match[1], match[0]), line.get("vi", ""))
            row = [identifier, "\n".join(sorted(locations[identifier])) or "?", line.get("fr") or "—", vietnamese, line.get("duration", "?"), target.as_posix() if target else "? (audio key trống)", "Có" if exists else "Không" if target else "?", "\n".join(sorted(evidence[identifier])) or "?"]
            text.append("| " + " | ".join(map(cell, row)) + " |")
        text.append("")
    wavs = sorted(vo.rglob("*.wav"))
    formats = set()
    for path in wavs:
        with wave.open(str(path), "rb") as clip:
            formats.add((clip.getframerate(), clip.getnchannels(), clip.getsampwidth() * 8))
    text += [f"Có WAV đích: **{present}/{len(lines)} câu**. Audio key trống: **{sum(not line.get('audio') for line in lines)} câu**.", "",
             f"Thư mục VO có **{len(wavs)} WAV**, tổng **{sum(path.stat().st_size for path in wavs):,} byte**. Định dạng đo từ WAV: " +
             "; ".join(f"{rate} Hz, {channels} kênh, PCM {bits} bit" for rate, channels, bits in sorted(formats)) + ".", "",
             "WAV đã tích hợp là giọng TTS; hướng dẫn PCM24 phía trên dành cho lần thu mới. Kiểm thư viện Unity và danh sách QA/khóa để trống: [báo cáo tích hợp](../Logs/integrate_report.md).", ""]
    return "\n".join(text)


def self_check():
    assert list(strings({"lines": ["x"]})) == [("x", "$.lines[0]")]
    assert cell("a|b\n<c>") == "a\\|b<br>&lt;c&gt;"
    assert audio_target("VO/example").as_posix() == "Game/Assets/_Project/Audio/VO/example.wav"
    assert audio_target("") is None
    for key in ("VO/", "VO/../example", "SFX/example"):
        try:
            audio_target(key)
        except ValueError:
            pass
        else:
            raise AssertionError(f"Phải từ chối audio key: {key}")
    assert scene_for_code(Path("Runtime/Narrative/PrologueDirector.cs")) == "00_Prologue"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--content", type=Path, default=CONTENT, help="Thư mục Resources/Content cần đọc")
    parser.add_argument("--output", default=str(ROOT / "Docs/VO_SCRIPT.md"), help="File Markdown đích; dấu - để in stdout")
    args = parser.parse_args()
    self_check()
    try:
        document = render(args.content.resolve())
        if args.output == "-":
            sys.stdout.write(document)
        else:
            Path(args.output).write_text(document, encoding="utf-8")
            print(f"Đã ghi {args.output}")
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"Không sinh được kịch bản VO: {error}\n")


if __name__ == "__main__":
    main()
