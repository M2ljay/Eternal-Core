"""Check that every Steam class token in the Core theme's CSS exists in css_translations.json.

Usage: python3 -I check_classes.py <css_translations.json> <theme dir>
A token counts as verified if any form of it (hashed, readable, readable+hash) appears in the
translations. Plain global classes (BasicUI, Focusable, gpfocus, ...) are listed separately:
they are not module classes and CSS Loader does not translate them.
"""
import json
import pathlib
import re
import sys

trans = json.load(open(sys.argv[1], encoding="utf-8"))
known = {name for forms in trans.values() for name in forms}
known |= {name.lstrip("_") for name in known}
theme = pathlib.Path(sys.argv[2])
spec = json.loads((theme / "theme.json").read_text(encoding="utf-8"))

def injected(node, acc):
    if isinstance(node, dict):
        for k, v in node.items():
            if k.endswith(".css"):
                acc.add(k)
            injected(v, acc)
    return acc

files = sorted(injected(spec, set()))
missing_files = [f for f in files if not (theme / f).is_file()]
token_re = re.compile(r"\.(-?[_a-zA-Z][\w-]*)")
bad = 0
for f in files:
    if f in missing_files:
        continue
    css = re.sub(r"/\*.*?\*/", "", (theme / f).read_text(encoding="utf-8"), flags=re.S)
    css = re.sub(r'url\([^)]*\)|"[^"]*"', "", css)
    tokens = set()
    for block in re.findall(r"([^{}]+)\{", css):
        if block.strip().startswith("@") or re.fullmatch(r"\s*(from|to|[\d.%\s,]+)\s*", block):
            continue
        tokens |= set(token_re.findall(block))
    module = sorted(t for t in tokens if "_" in t.strip("_") or t.startswith("_"))
    plain = sorted(t for t in tokens if t not in module)
    unknown = [t for t in module if t not in known]
    bad += len(unknown)
    print(f"{f}: {len(module) - len(unknown)}/{len(module)} module classes verified"
          + (f"; NOT FOUND: {', '.join(unknown)}" if unknown else "")
          + (f"; global classes: {', '.join(plain)}" if plain else ""))
for f in missing_files:
    print(f"{f}: referenced in theme.json but missing")
sys.exit(1 if bad or missing_files else 0)
