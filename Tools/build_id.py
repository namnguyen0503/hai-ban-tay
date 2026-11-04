#!/usr/bin/env python3
"""Mã bản dựng (PRODUCTION_PLAN W1.4, cổng G1).

Tools/regen_gate.sh tạo mã sau khi dựng lại scene và bake (Logs/build_id.json), rồi smoke build mang mã đó. Ảnh, log bot,
kết quả test và bản build gắn mã này (biến môi trường HBT_BUILD_ID do Tools/*.sh đặt; bản build đọc Resources/BuildId).
Builder (Scripts/Editor) hay scene đổi sau lần sinh lại thì mã thành "<mã>+stale": ảnh/test lúc đó không dùng làm bằng chứng,
chạy lại cổng.

Dùng:
  build_id.py new [--steps a,b]   tạo mã cho dữ liệu hiện tại, in mã
  build_id.py smoke ok|failed     ghi kết quả smoke build của mã hiện tại
  build_id.py show                in mã hiện tại (+stale nếu dữ liệu đã đổi, +nosmoke nếu smoke build chưa qua); "none" nếu chưa có
  build_id.py check               như show, mã thoát 1 khi không phải mã sạch
"""
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STATE = ROOT / "Logs" / "build_id.json"
PROJECT = ROOT / "Game" / "Assets" / "_Project"


def editor_signature():
    """Nội dung C# dựng scene và bake (SceneBuilders, Lighting): đổi là scene trong repo dựng từ code cũ."""
    h = hashlib.sha1()
    for folder in ("SceneBuilders", "Lighting"):
        for p in sorted((PROJECT / "Scripts" / "Editor" / folder).rglob("*.cs")):
            h.update(str(p.relative_to(PROJECT)).encode())
            h.update(p.read_bytes())
    return h.hexdigest()[:16]


def scenes_signature():
    """Kích thước + thời điểm sửa của scene và dữ liệu bake (nhanh; lightmap lớn không đọc nội dung)."""
    h = hashlib.sha1()
    for p in sorted((PROJECT / "Scenes").rglob("*")):
        if p.is_file() and p.suffix != ".meta":
            st = p.stat()
            h.update(f"{p.relative_to(PROJECT)}|{st.st_size}|{st.st_mtime_ns}".encode())
    return h.hexdigest()[:16]


def git(*args):
    try:
        return subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return ""


def load():
    try:
        return json.loads(STATE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def save(state):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def current():
    state = load()
    if state is None:
        return "none", False
    tags = []
    if state.get("editor_sig") != editor_signature() or state.get("scenes_sig") != scenes_signature():
        tags.append("stale")
    if state.get("smoke") != "ok":
        tags.append("nosmoke")
    return state["id"] + "".join("+" + t for t in tags), not tags


def main(argv):
    cmd = argv[1] if len(argv) > 1 else "show"
    if cmd == "new":
        steps = argv[argv.index("--steps") + 1].split(",") if "--steps" in argv else []
        head = git("rev-parse", "HEAD")
        dirty = bool(git("status", "--porcelain", "--untracked-files=no"))
        now = datetime.now(timezone.utc)
        build_id = now.astimezone().strftime("%Y%m%d-%H%M") + "-" + (head[:7] or "nogit") + ("d" if dirty else "")
        save({
            "id": build_id,
            "created": now.isoformat(timespec="seconds"),
            "git": head,
            "dirty": dirty,
            "steps": steps,
            "editor_sig": editor_signature(),
            "scenes_sig": scenes_signature(),
            "smoke": None,
        })
        print(build_id)
        return 0
    if cmd == "smoke":
        state = load()
        if state is None:
            print("Chưa có mã bản dựng (chạy Tools/regen_gate.sh).", file=sys.stderr)
            return 2
        state["smoke"] = argv[2] if len(argv) > 2 else "ok"
        # Smoke build không được đổi scene; ghi lại chữ ký sau build để bản build mang đúng dữ liệu đã kiểm.
        if state["smoke"] == "ok" and state.get("scenes_sig") != scenes_signature():
            state["smoke"] = "scenes_changed_during_build"
        save(state)
        print(state["id"], state["smoke"])
        return 0 if state["smoke"] == "ok" else 1
    if cmd in ("show", "check"):
        text, clean = current()
        print(text)
        return 0 if clean or cmd == "show" else 1
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
