"""Pinned public CC0 downloads. Cache and license evidence stay beside this file."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parent


def download():
    sources = json.loads((ROOT / "sources.json").read_text())
    cache = ROOT / "sources"
    cache.mkdir(exist_ok=True)
    for source in sources:
        dest = cache / (source["id"] + Path(source["url"]).suffix)
        evidence = cache / (source["id"] + ".html")
        if not evidence.exists():
            evidence.write_bytes(urllib.request.urlopen(source["page"], timeout=60).read())
        page = evidence.read_text()
        assert "CC0" in page or "cc0.png" in page, source["page"]
        if not dest.exists():
            data = urllib.request.urlopen(source["url"], timeout=60).read()
            assert len(data) > 1000, source["url"]
            dest.write_bytes(data)
        digest = hashlib.sha256(dest.read_bytes()).hexdigest()
        if "sha256" in source:
            assert digest == source["sha256"], f"Changed source: {source['id']}"
        else:
            source["sha256"] = digest
        print(source["id"], dest.stat().st_size, digest, flush=True)
    (ROOT / "sources.json").write_text(json.dumps(sources, indent=2) + "\n")
    return sources


if __name__ == "__main__":
    download()
