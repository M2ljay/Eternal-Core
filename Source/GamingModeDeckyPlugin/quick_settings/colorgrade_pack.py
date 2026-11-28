"""ColorGrade icon pack: the user's own console art for Core's Consoles tab (docs/eternal/CORE-UI.md, "The Consoles tab").

The pack is the user's personal download (no licence; personal use). Nothing of it ships with Core, is committed or is
used in tests: the tests build a small fake zip of their own.

- Families are already in ``%APPDATA%\\GamingMode\\icons\\colorgrade\\families\\<family>\\{icon,hero,logo}.png``.
- Platforms are imported once, when ``...\\colorgrade\\platforms`` is missing, from
  ``%USERPROFILE%\\Downloads\\colorgrade-icons-heroes-logos.zip``: every ``.../by_platform/<slug>/{icon,hero,logo}.png``
  entry (83 slugs in the user's copy), extracted to ``...\\colorgrade\\platforms\\<slug>\\``. "Re-import icon pack" in
  Quick Access > Core > Look runs it again (``force``), replacing the folder only once the new copy is complete.

Reading answers each slug's white logo as a data URL. A hero (megabytes each) is answered one at a time by
``read_hero`` (3.3.10, the Consoles page). Only plain slugs and the three known file names are ever read or written.

Standard library only (Decky's frozen Windows Python has ``zipfile``, ``base64``, ``shutil``).
"""
import base64
import os
import re
import shutil
import threading
import zipfile

ZIP_NAME = "colorgrade-icons-heroes-logos.zip"
FILES = ("icon.png", "hero.png", "logo.png")
FAMILIES = ("playstation", "nintendo", "sega", "xbox", "retro")
SLUG_RE = re.compile(r"^[a-z0-9][a-z0-9_-]{0,40}$")
ENTRY_RE = re.compile(r"(?:^|/)by_platform/([^/]+)/([^/]+)$")
MAX_ENTRY = 24 * 1024 * 1024        # bytes per extracted file
MAX_TOTAL = 1536 * 1024 * 1024      # bytes per import
MAX_SLUGS = 400
LOGO_MAX = 1024 * 1024              # bytes per logo answered as a data URL
LOGOS_TOTAL = 32 * 1024 * 1024

_lock = threading.Lock()


def pack_folder(appdata):
    return os.path.join(appdata, "GamingMode", "icons", "colorgrade") if appdata else ""


def zip_candidates(homes):
    """Where the user's download may be: <home>\\Downloads for each known home, without duplicates."""
    out = []
    for home in homes:
        if not home:
            continue
        path = os.path.join(home, "Downloads", ZIP_NAME)
        if os.path.normcase(os.path.abspath(path)) not in [os.path.normcase(os.path.abspath(p)) for p in out]:
            out.append(path)
    return out


def _entries(archive):
    """The pack's platform entries: (slug, file name, ZipInfo), only plain slugs and the three known files."""
    found = []
    for info in archive.infolist():
        name = info.filename.replace("\\", "/")
        if info.is_dir() or ".." in name.split("/"):
            continue
        match = ENTRY_RE.search(name)
        if not match:
            continue
        slug, file_name = match.group(1).lower(), match.group(2).lower()
        if not SLUG_RE.match(slug) or file_name not in FILES:
            continue
        found.append((slug, file_name, info))
    return found


def import_pack(zip_path, folder, force=False):
    """Extracts the pack's platforms into ``folder``/platforms. Without ``force`` it does nothing when that folder
    exists. The new copy is written beside it and swapped in only when complete."""
    with _lock:
        target = os.path.join(folder, "platforms")
        if os.path.isdir(target) and not force:
            return {"ok": True, "imported": False, "reason": "present"}
        if not zip_path or not os.path.isfile(zip_path):
            return {"ok": False, "imported": False, "reason": "zip_missing"}
        staging = os.path.join(folder, "platforms.importing")
        shutil.rmtree(staging, ignore_errors=True)
        slugs, files, total = set(), 0, 0
        try:
            with zipfile.ZipFile(zip_path) as archive:
                for slug, file_name, info in _entries(archive):
                    if slug not in slugs and len(slugs) >= MAX_SLUGS:
                        continue
                    if info.file_size > MAX_ENTRY or total + info.file_size > MAX_TOTAL:
                        continue
                    os.makedirs(os.path.join(staging, slug), exist_ok=True)
                    destination = os.path.join(staging, slug, file_name)
                    written = 0
                    with archive.open(info) as source, open(destination, "wb") as sink:
                        while True:
                            chunk = source.read(1024 * 1024)
                            if not chunk:
                                break
                            written += len(chunk)
                            if written > MAX_ENTRY:
                                raise ValueError("entry_too_large")
                            sink.write(chunk)
                    total += written
                    slugs.add(slug)
                    files += 1
        except (OSError, zipfile.BadZipFile, ValueError) as error:
            shutil.rmtree(staging, ignore_errors=True)
            return {"ok": False, "imported": False, "reason": "bad_zip" if isinstance(error, zipfile.BadZipFile) else "failed",
                    "detail": str(error)[:120]}
        if not slugs:
            shutil.rmtree(staging, ignore_errors=True)
            return {"ok": False, "imported": False, "reason": "no_platforms"}
        old = os.path.join(folder, "platforms.old")
        shutil.rmtree(old, ignore_errors=True)
        if os.path.isdir(target):
            os.replace(target, old)
        os.replace(staging, target)
        shutil.rmtree(old, ignore_errors=True)
        return {"ok": True, "imported": True, "slugs": len(slugs), "files": files}


def _data_url(path, budget):
    try:
        size = os.path.getsize(path)
    except OSError:
        return None, 0
    if size > LOGO_MAX or size > budget:
        return None, 0
    try:
        with open(path, "rb") as handle:
            data = handle.read(LOGO_MAX + 1)
    except OSError:
        return None, 0
    return "data:image/png;base64," + base64.b64encode(data).decode("ascii"), len(data)


def read_pack(folder):
    """The slugs the pack has and each one's logo (data URL), plus the families' logos."""
    answer = {"ok": False, "folder": folder, "slugs": [], "logos": {}, "families": {}, "errors": []}
    if not folder or not os.path.isdir(folder):
        answer["errors"].append("folder_missing")
        return answer
    budget = LOGOS_TOTAL
    platforms = os.path.join(folder, "platforms")
    try:
        names = sorted(os.listdir(platforms)) if os.path.isdir(platforms) else []
    except OSError:
        names = []
    for slug in names[:MAX_SLUGS]:
        if not SLUG_RE.match(slug) or not os.path.isdir(os.path.join(platforms, slug)):
            continue
        answer["slugs"].append(slug)
        url, used = _data_url(os.path.join(platforms, slug, "logo.png"), budget)
        if url:
            answer["logos"][slug] = url
            budget -= used
        elif os.path.isfile(os.path.join(platforms, slug, "logo.png")):
            answer["errors"].append("logo_too_large:" + slug)
    for family in FAMILIES:
        url, used = _data_url(os.path.join(folder, "families", family, "logo.png"), budget)
        if url:
            answer["families"][family] = url
            budget -= used
    if not answer["slugs"]:
        answer["errors"].append("no_platforms")
    answer["ok"] = True
    return answer


HERO_MAX = 8 * 1024 * 1024          # bytes per hero answered as a data URL (3.3.10; the pack's are 1 to 4 MB)


def read_hero(folder, slug):
    """3.3.10: one platform's hero (``platforms/<slug>/hero.png``) as a data URL, for the Consoles page's background and
    tiles. One file per call (heroes are megabytes, never sent in bulk); only a plain slug the pack has, only that name."""
    answer = {"ok": False, "slug": slug if isinstance(slug, str) else "", "url": None}
    if not folder or not isinstance(slug, str) or not SLUG_RE.match(slug):
        answer["error"] = "bad_slug"
        return answer
    path = os.path.join(folder, "platforms", slug, "hero.png")
    try:
        size = os.path.getsize(path)
    except OSError:
        answer["error"] = "missing"
        return answer
    if size > HERO_MAX:
        answer["error"] = "too_large"
        return answer
    try:
        with open(path, "rb") as handle:
            data = handle.read(HERO_MAX + 1)
    except OSError:
        answer["error"] = "unreadable"
        return answer
    answer["ok"] = True
    answer["url"] = "data:image/png;base64," + base64.b64encode(data).decode("ascii")
    return answer


def ensure_and_read(appdata, homes, force=False):
    """Imports the platforms when missing (or when forced) from the first zip found, then reads the pack."""
    folder = pack_folder(appdata)
    result = None
    if folder and (force or not os.path.isdir(os.path.join(folder, "platforms"))):
        source = next((path for path in zip_candidates(homes) if os.path.isfile(path)), "")
        os.makedirs(folder, exist_ok=True)
        result = import_pack(source, folder, force=force)
    answer = read_pack(folder)
    if result is not None:
        answer["import"] = result
        answer["imported"] = bool(result.get("imported"))
    return answer
