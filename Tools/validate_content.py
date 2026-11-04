#!/usr/bin/env python3
"""Kiểm tra toàn vẹn JSON nội dung ↔ Docs/FACTS.md ↔ EventIds.cs (PLAN.md mục 8.5).

Game có hư cấu và dàn dựng (một disclaimer là đủ), nên script chỉ kiểm toàn vẹn dữ liệu: JSON hợp lệ, ID tồn tại
và không trùng, factIds có trong FACTS, độ dài chữ vừa UI; không kiểm giới hạn về lời thoại hay cảnh dựng.

Dùng: python3 Tools/validate_content.py [--release]
"""
import json
import re
import sys
import unicodedata
from pathlib import Path
try:
    from audit_vo import audit as audit_vo
except ImportError:  # bản công khai không kèm công cụ TTS/VO
    audit_vo = None

ROOT = Path(__file__).resolve().parent.parent
CONTENT = ROOT / "Game/Assets/_Project/Resources/Content"
FACTS = ROOT / "Docs/FACTS.md"
EVENTS = ROOT / "Game/Assets/_Project/Scripts/Runtime/Core/EventIds.cs"
FILES = ["notebook_pages", "source_cards", "subtitles", "ui_strings",
         "french_words", "map_transitions", "ending", "days", "study"]
ORDER_KINDS = {"fire", "sack", "wash", "secure", "scrub_tall", "tall_out", "ash", "sweep", "greens", "pan", "haul", "coal_fetch", "water", "meat", "fish", "ice", "serve", "coffee", "peel", "asparagus", "soup", "chieu"}
STUDY_KINDS = {"rest", "match", "trace", "fill", "sentence"}
# Khớp ContentDatabase.NpcIds / NpcActions (M11: NPC theo ngày).
NPC_IDS = {"soldiers", "jacques", "mai"}
NPC_ACTIONS = {"help", "visit", "deck", "teach", "cards"}
# Kiểm PHASE (PRODUCTION_PLAN W1/W2): ca kết thúc 21:00 (S16: "chín giờ tối, công việc mới xong"); mỗi bữa nấu tối đa
# số nồi đặt được trên ô bếp; lượt rửa không đòi nhiều nồi hơn số nồi bẩn đang có (count 99 = rửa hết); lượt không có
# giờ phục vụ phải có số phút.
SHIFT_END = 21 * 60
MAX_MEAL_POTS = 3
WASH_ALL = 99
FILL_BLANK = "___"
TOKEN = re.compile(r"\{w:([a-z0-9_]+)\}")
MAX_SUBTITLE, MAX_PAGE, MAX_CARD, MAX_ENDCARD = 90, 220, 260, 120
# Disclaimer duy nhất (màn "Chơi mới" và credits): thiếu khóa nào thì game ném ContentException lúc chạy.
REQUIRED_UI = ("disclaimer.body", "disclaimer.continue", "credits.disclaimer")

errors, warnings = [], []


def err(f, msg):
    errors.append(f"[{f}] {msg}")


def load_facts():
    facts = {}
    if not FACTS.exists():
        # Bản mã nguồn công khai không kèm Docs/FACTS.md: bỏ qua kiểm factId.
        return facts
    for line in FACTS.read_text(encoding="utf-8").splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) >= 4 and re.fullmatch(r"F\d+[a-z]?", cells[0]):
            facts[cells[0]] = cells[3].strip("`")
    return facts


def load_events():
    if not EVENTS.exists():
        err("EventIds.cs", "không tìm thấy file")
        return set()
    return set(re.findall(r'const\s+string\s+\w+\s*=\s*"([^"]+)"', EVENTS.read_text(encoding="utf-8")))


def load_json(name):
    path = CONTENT / f"{name}.vi.json"
    if not path.exists():
        err(name, "thiếu file")
        return None
    raw = path.read_text(encoding="utf-8")  # rule 1: UTF-8
    if unicodedata.normalize("NFC", raw) != raw:
        err(name, "chưa chuẩn hóa NFC")
    def no_duplicates(pairs):
        # Key trùng: JSON giữ giá trị sau, ghi đè im lặng chữ của chỗ khác.
        seen = {}
        for k, v in pairs:
            if k in seen:
                err(name, f"key trùng: {k}")
            seen[k] = v
        return seen

    try:
        return json.loads(raw, object_pairs_hook=no_duplicates)
    except json.JSONDecodeError as e:
        err(name, f"JSON lỗi: {e}")
        return None


def unique(name, items, label):
    seen = set()
    for it in items:
        i = it.get("id")
        if i in seen:
            err(name, f"{label} trùng ID: {i}")
        seen.add(i)
    return seen


def contains_todo(obj):
    if isinstance(obj, str):
        return "TODO" in obj
    if isinstance(obj, dict):
        return any(contains_todo(v) for v in obj.values())
    if isinstance(obj, list):
        return any(contains_todo(v) for v in obj)
    return False


def main():
    release = "--release" in sys.argv
    facts, events = load_facts(), load_events()
    data = {n: load_json(n) for n in FILES}

    cards = (data["source_cards"] or {}).get("cards", [])
    pages = (data["notebook_pages"] or {}).get("pages", [])
    lines = (data["subtitles"] or {}).get("lines", [])
    card_ids = unique("source_cards", cards, "thẻ")
    unique("notebook_pages", pages, "trang")
    unique("subtitles", lines, "thoại")

    # 3. tham chiếu thẻ và fact
    for p in pages:
        for c in p.get("sourceCardIds", []):
            if c not in card_ids:
                err("notebook_pages", f"{p['id']}: sourceCardId '{c}' không tồn tại")
    for e in (data["ending"] or {}).get("endCards", []):
        if e.get("sourceCardId") not in card_ids:
            err("ending", f"sourceCardId '{e.get('sourceCardId')}' không tồn tại")
    layers = (data["source_cards"] or {}).get("layers", {})
    used_facts = {}
    for c in cards:
        if c.get("layer") not in layers:
            err("source_cards", f"{c['id']}: layer '{c.get('layer')}' không khai báo")
        for f in (c.get("factIds", []) if FACTS.exists() else []):
            if f not in facts:
                err("source_cards", f"{c['id']}: factId '{f}' không có trong FACTS.md")
            else:
                used_facts.setdefault(f, []).append(c)
        # 6. thẻ nào cũng nêu sự kiện nên phải có fact
        if not c.get("factIds"):
            err("source_cards", f"{c['id']}: thẻ phải có factIds")
        if len(c.get("body", "")) > MAX_CARD:
            err("source_cards", f"{c['id']}: thân {len(c['body'])} > {MAX_CARD} ký tự")

    # 4. sự kiện
    for p in pages:
        if p.get("unlockEvent") not in events:
            err("notebook_pages", f"{p['id']}: unlockEvent '{p.get('unlockEvent')}' không có trong EventIds.cs")
        flag = (p.get("appendIfFlag") or {}).get("flag")
        if flag and flag not in events:
            err("notebook_pages", f"{p['id']}: flag '{flag}' không có trong EventIds.cs")
        if len(p.get("body", "")) > MAX_PAGE:
            err("notebook_pages", f"{p['id']}: thân {len(p['body'])} > {MAX_PAGE} ký tự")
    lt = (data["notebook_pages"] or {}).get("linkTarget")
    if lt:
        if lt.get("event") not in events:
            err("notebook_pages", f"linkTarget.event '{lt.get('event')}' không có trong EventIds.cs")
        pids = {p["id"] for p in pages}
        for k in ("a", "b"):
            if lt.get(k) not in pids:
                err("notebook_pages", f"linkTarget.{k} '{lt.get(k)}' không phải trang")

    # 5. thoại
    speakers = (data["subtitles"] or {}).get("speakers", {})
    for l in lines:
        if l.get("speaker") not in speakers:
            err("subtitles", f"{l['id']}: speaker '{l.get('speaker')}' không có trong speakers")
        if l.get("gaze") not in (None, "task", "other"):
            err("subtitles", f"{l['id']}: gaze '{l.get('gaze')}' không hợp lệ (task, other hoặc bỏ trống)")
        if not l.get("vi"):
            err("subtitles", f"{l['id']}: thiếu 'vi'")
        elif len(l["vi"]) > MAX_SUBTITLE:
            err("subtitles", f"{l['id']}: phụ đề {len(l['vi'])} > {MAX_SUBTITLE} ký tự")

    line_ids = {l["id"] for l in lines}
    words = (data["french_words"] or {}).get("pairs", [])
    word_ids = unique("french_words", words, "từ")
    word_map = {w.get("id"): w for w in words}
    for pr in words:
        if not pr.get("id"):
            err("french_words", f"{pr.get('fr')}: thiếu 'id'")
        if pr.get("line") and pr["line"] not in line_ids:
            err("french_words", f"{pr.get('fr')}: line '{pr['line']}' không có trong subtitles")

    def longest(text):
        def pick(m):
            w = word_map.get(m.group(1))
            if not w:
                return m.group(0)
            return w["fr"] if len(w["fr"]) >= len(w["vi"]) else w["vi"]
        return TOKEN.sub(pick, text or "")

    # 11. từ vựng {w:id} trong chữ hiển thị
    for l in lines:
        for t in TOKEN.findall(l.get("vi", "")):
            if t not in word_ids:
                err("subtitles", f"{l['id']}: nhắc từ '{t}' không có trong french_words")
        if len(longest(l.get("vi", ""))) > MAX_SUBTITLE:
            err("subtitles", f"{l['id']}: phụ đề (bản dài nhất) {len(longest(l['vi']))} > {MAX_SUBTITLE} ký tự")
    for k, v in ((data["ui_strings"] or {}).get("strings", {})).items():
        for t in TOKEN.findall(v):
            if t not in word_ids:
                err("ui_strings", f"{k}: nhắc từ '{t}' không có trong french_words")
    for kind in ORDER_KINDS:
        if f"order.{kind}" not in (data["ui_strings"] or {}).get("strings", {}):
            err("ui_strings", f"thiếu nhãn việc 'order.{kind}'")
    for key in REQUIRED_UI:
        if not (data["ui_strings"] or {}).get("strings", {}).get(key):
            err("ui_strings", f"thiếu chữ disclaimer '{key}'")

    # 13. bài học buổi tối (study.vi.json)
    study = data["study"] or {}
    letters = {}
    for g in study.get("letters", []):
        gid = g.get("id")
        if gid in letters:
            err("study", f"chữ '{gid}' trùng")
        letters[gid] = g
        if not g.get("strokes"):
            err("study", f"chữ '{gid}' không có nét nào")
        for stroke in g.get("strokes", []):
            if len(stroke) < 2 or any(len(pt) != 2 or not all(0 <= v <= 1 for v in pt) for pt in stroke):
                err("study", f"chữ '{gid}': mỗi nét cần >= 2 điểm [x, y] trong ô 0–1")
    lessons = {}
    for lesson in study.get("lessons", []):
        lid = lesson.get("id")
        if lid in lessons:
            err("study", f"bài '{lid}' trùng")
        lessons[lid] = lesson
        kind = lesson.get("kind")
        if kind not in ("trace", "fill", "sentence"):
            err("study", f"bài '{lid}': kiểu '{kind}' không hợp lệ")
        if not lesson.get("items"):
            err("study", f"bài '{lid}' không có mục nào")
        for it in lesson.get("items", []):
            w = f"bài '{lid}', mục '{it.get('id')}'"
            if not it.get("vi"):
                err("study", f"{w}: thiếu nghĩa 'vi'")
            if not it.get("words") and lesson.get("language") != "vi":
                err("study", f"{w}: thiếu 'words' (từ học được)")
            for wid in it.get("words", []):
                if wid not in word_ids:
                    err("study", f"{w}: từ '{wid}' không có trong french_words")
            if it.get("line") and it["line"] not in line_ids:
                err("study", f"{w}: line '{it['line']}' không có trong subtitles")
            if kind == "trace":
                if not it.get("text"):
                    err("study", f"{w}: thiếu chữ cần tô 'text'")
                for ch in it.get("text", ""):
                    if ch not in letters:
                        err("study", f"{w}: chữ '{ch}' chưa có nét trong letters")
            elif kind == "fill":
                if (it.get("fr") or "").count(FILL_BLANK) != 1:
                    err("study", f"{w}: câu 'fr' cần đúng một chỗ trống '{FILL_BLANK}'")
                opts = it.get("options", [])
                if len(opts) < 2 or it.get("answer") not in opts:
                    err("study", f"{w}: cần >= 2 lựa chọn và có đáp án")
                for o in opts:
                    if o not in word_ids:
                        err("study", f"{w}: lựa chọn '{o}' không có trong french_words")
            elif kind == "sentence":
                if len(it.get("tokens", [])) < 2:
                    err("study", f"{w}: câu cần >= 2 từ")
            if len(it.get("fr") or "") > MAX_SUBTITLE or len(it.get("vi") or "") > MAX_SUBTITLE:
                err("study", f"{w}: câu dài quá {MAX_SUBTITLE} ký tự")

    # 12. ngày trên tàu
    transitions = {t.get("id") for t in (data["map_transitions"] or {}).get("transitions", [])}
    numbers = set()
    for day in (data["days"] or {}).get("days", []):
        d = day.get("id")
        if day.get("number") in numbers:
            err("days", f"{d}: trùng số ngày {day.get('number')}")
        numbers.add(day.get("number"))
        if day.get("transition") and day["transition"] not in transitions:
            err("days", f"{d}: bản đồ '{day['transition']}' không có trong map_transitions")
        if not day.get("phases"):
            err("days", f"{d}: không có lượt việc nào")
        for e in day.get("startEvents", []):
            if e not in events:
                err("days", f"{d}: sự kiện đầu ngày '{e}' không có trong EventIds.cs")
        phase_ids = {"day", "evening"} | {ph.get("id") for ph in day.get("phases", [])}
        for b in day.get("npcs", []):
            n = f"{d}/npc {b.get('who')}"
            if b.get("who") not in NPC_IDS:
                err("days", f"{n}: NPC không có trong {sorted(NPC_IDS)}")
            if b.get("action") not in NPC_ACTIONS:
                err("days", f"{n}: việc '{b.get('action')}' không hợp lệ")
            if b.get("phase") not in phase_ids:
                err("days", f"{n}: lượt '{b.get('phase')}' không có trong ngày")
            if b.get("until") and b["until"] not in phase_ids:
                err("days", f"{n}: lượt rời đi '{b['until']}' không có trong ngày")
            if (b.get("action") in ("teach", "cards")) != (b.get("phase") == "evening"):
                err("days", f"{n}: việc 'teach'/'cards' chỉ dùng cho buổi tối (phase evening) và buổi tối chỉ có hai việc này")
            for key in ("call",):
                if b.get(key) and b[key] not in line_ids:
                    err("days", f"{n}: {key} '{b[key]}' không có trong subtitles")
            for l in b.get("lines", []):
                if l not in line_ids:
                    err("days", f"{n}: thoại '{l}' không có trong subtitles")
            if b.get("event") and b["event"] not in events:
                err("days", f"{n}: sự kiện '{b['event']}' không có trong EventIds.cs")
        last_start = 0
        clock = day.get("clockStart", 240)
        dirty = 0
        strings = (data["ui_strings"] or {}).get("strings", {})
        for ph in day.get("phases", []):
            p = f"{d}/{ph.get('id')}"
            # PHASE: khung giờ của lượt theo đồng hồ chạy bằng việc.
            begin = max(clock, ph.get("startAt", 0))
            if ph.get("serviceAt"):
                end = ph["serviceAt"]
            elif ph.get("minutes", 0) > 0:
                end = begin + ph["minutes"]
            else:
                err("days", f"{p}: lượt không có serviceAt thì cần minutes > 0 (đồng hồ chạy theo việc)")
                end = begin
            if end < begin:
                err("days", f"{p}: giờ kết thúc {end} trước giờ bắt đầu {begin}")
            if end > SHIFT_END:
                err("days", f"{p}: lượt kết thúc {end // 60}:{end % 60:02d}, quá 21:00")
            clock = max(clock, end)
            if ph.get("montage"):
                if not strings.get(ph.get("caption", "")):
                    err("days", f"{p}: montage cần caption có trong ui_strings")
                if ph.get("afterLines"):
                    err("days", f"{p}: montage không có thoại sau lượt (không ai đứng nghe)")
            if ph.get("dirty", 0) > MAX_MEAL_POTS:
                err("days", f"{p}: bữa nấu {ph['dirty']} nồi, ô bếp có nồi chỉ đặt được {MAX_MEAL_POTS}")
            # Montage: nấu và phục vụ trước, rồi mới rửa (rửa được cả nồi của chính bữa đó).
            if ph.get("montage"):
                dirty += ph.get("dirty", 0)
            for o in ph.get("orders", []):
                if o.get("task") == "wash":
                    c = o.get("count", 1)
                    if c != WASH_ALL and c > dirty:
                        err("days", f"{p}: lượt rửa đòi {c} nồi nhưng chỉ có {dirty} nồi bẩn")
                    dirty -= dirty if c == WASH_ALL else min(c, dirty)
            if not ph.get("montage"):
                dirty += ph.get("dirty", 0)
            for line in ph.get("afterLines", []):
                if line not in line_ids: err("days", f"{p}: thoại sau lượt {line} không tồn tại")
            for key in ("line", "lateLine"):
                if ph.get(key) and ph[key] not in line_ids:
                    err("days", f"{p}: {key} '{ph[key]}' không có trong subtitles")
            for c in ph.get("cards", []) + ph.get("startCards", []):
                if c not in card_ids:
                    err("days", f"{p}: thẻ '{c}' không tồn tại")
            for e in ph.get("events", []):
                if e not in events:
                    err("days", f"{p}: sự kiện '{e}' không có trong EventIds.cs")
            for o in ph.get("orders", []):
                if o.get("task") not in ORDER_KINDS:
                    err("days", f"{p}: việc '{o.get('task')}' không hợp lệ")
                if o.get("count", 1) < 1:
                    err("days", f"{p}: số lượng phải >= 1")
            start = ph.get("startAt", 0)
            if start and start < last_start:
                err("days", f"{p}: startAt lùi giờ so với lượt trước")
            last_start = max(last_start, start)
            if ph.get("serviceAt") and start and ph["serviceAt"] <= start:
                err("days", f"{p}: serviceAt phải sau startAt")
        if dirty:
            err("days", f"{d}: hết ca còn {dirty} nồi bẩn chưa rửa")
        ev = day.get("evening") or {}
        if ev:
            if ev.get("storyLesson") and ev["storyLesson"] not in lessons:
                err("days", f"{d}: storyLesson không tồn tại")
            for event in ev.get("events", []):
                if event not in events: err("days", f"{d}: sự kiện buổi tối {event} không tồn tại")
            if ev.get("study", "rest") not in STUDY_KINDS:
                err("days", f"{d}: kiểu học '{ev.get('study')}' không hợp lệ")
            if ev.get("line") and ev["line"] not in line_ids:
                err("days", f"{d}: câu buổi tối '{ev['line']}' không có trong subtitles")
            for c in ev.get("cards", []):
                if c not in card_ids:
                    err("days", f"{d}: thẻ buổi tối '{c}' không tồn tại")
            if ev.get("study") == "match" and sum(1 for w in words if w.get("set") == ev.get("set")) < 2:
                err("days", f"{d}: bộ từ '{ev.get('set')}' cần ít nhất 2 từ")
            if ev.get("study") in ("trace", "fill", "sentence"):
                lesson = lessons.get(ev.get("lesson"))
                if lesson is None:
                    err("days", f"{d}: bài học '{ev.get('lesson')}' không có trong study")
                elif lesson.get("kind") != ev.get("study"):
                    err("days", f"{d}: bài '{ev.get('lesson')}' là kiểu '{lesson.get('kind')}', cần '{ev.get('study')}'")
            if ev.get("study", "rest") != "rest" and ev.get("items", 4) < 1:
                err("days", f"{d}: số mục học phải >= 1")
            for l in ev.get("after", []):
                if l not in line_ids:
                    err("days", f"{d}: câu sau buổi học '{l}' không có trong subtitles")
    day_numbers = sorted(numbers)
    if day_numbers and day_numbers != list(range(1, len(day_numbers) + 1)):
        err("days", f"số ngày phải liền nhau từ 1: {day_numbers}")

    # 7. thẻ mốc ending
    ending = data["ending"] or {}
    timing = ending.get("timings", {})
    play_keys = ("dawn", "finalClean", "payQueue", "pay", "farewell", "quay", "quayObserve", "tram", "cafe", "cafeServe", "pagesLink")
    film_keys = ("fadeToBlack", "narrator", "title", "credits", "disclaimer")
    for key in play_keys + film_keys + ("endCard", "payHold", "linkHintAfter", "maiRecallCard"):
        if not isinstance(timing.get(key), (int, float)) or timing[key] <= 0:
            err("ending", f"timings.{key}: cần số dương")
    total = 7 * timing.get("maiRecallCard", 0) + sum(timing.get(k, 0) for k in play_keys + film_keys) + len(ending.get("endCards", [])) * timing.get("endCard", 0)
    if not 480 <= total <= 720:
        err("ending", f"thời lượng dự kiến {total} s ngoài 480–720 s")
    if ending.get("coins") != {"player": 10, "frenchHand": 0}:
        err("ending", "cần đúng 10 xu cho người chơi, frenchHand=0")
    for key in ("lines", "farewellLines", "farewellReplies"):
        for line in ending.get(key, []):
            if line not in line_ids:
                err("ending", f"{key}: câu '{line}' không tồn tại")
    if len(ending.get("farewellLines", [])) != 6 or len(ending.get("farewellReplies", [])) != 6:
        err("ending", "cần đủ lời chào và đáp cho sáu người")
    for id in ("p_why_france", "sc_slogan"):
        item = next((x for x in pages + cards if x.get("id") == id), {})
        if "1789" in item.get("body", ""):
            err("ending", f"{id}: F11 không ghi năm 1789")
    for e in ending.get("endCards", []):
        if len(e.get("text", "")) > MAX_ENDCARD:
            err("ending", f"thẻ mốc {e.get('date')}: {len(e['text'])} > {MAX_ENDCARD} ký tự")

    # 9. fact literary
    for f, cs in used_facts.items():
        if facts and facts.get(f) == "literary":
            for c in cs:
                if c.get("layer") != "van_hoc_hoa":
                    err("source_cards", f"{c['id']}: fact literary {f} chỉ được dẫn từ tầng van_hoc_hoa")

    # 8. release
    if release:
        for f, cs in used_facts.items():
            if facts.get(f) == "todo":
                for c in cs:
                    err("release", f"{c['id']} tham chiếu fact todo {f}")
        for n, d in data.items():
            if d is not None and contains_todo(d):
                err("release", f"{n}: có chuỗi chứa 'TODO'")
        for f, st in facts.items():
            if st == "secondary":
                warnings.append(f"fact {f} còn 'secondary' — đối chiếu sách gốc trước khi nộp")

    # VO là cổng chặn: chữ sửa sau bản thu hoặc duration lệch đều phải sửa trước khi chạy game.
    try:
        if audit_vo is None:
            raise LookupError
        vo_errors, vo_results = audit_vo(ROOT)
        for message in vo_errors:
            err("VO", message)
        review = [r["id"] for r in vo_results if r.get("asr_status") == "review"]
        if review:
            warnings.append("VO cần người nghe kiểm ASR: " + ", ".join(review))
    except LookupError:
        pass
    except (OSError, ValueError, KeyError, TypeError) as error:
        err("VO", str(error))

    # 10. tóm tắt
    for w in warnings:
        print("WARN ", w)
    for e in errors:
        print("ERROR", e)
    print(f"CONTENT: {len(pages)} trang, {len(cards)} thẻ, {len(lines)} thoại, "
          f"{len(facts)} fact, {len(events)} event · {len(errors)} lỗi"
          f"{' (release)' if release else ''}")
    print("CONTENT: " + ("FAIL" if errors else "OK"))
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
