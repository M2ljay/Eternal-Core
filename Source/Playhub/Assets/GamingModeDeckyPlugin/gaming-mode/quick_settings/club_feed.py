"""Club feed: Play Retro Games Club (playretrogamesclub.com) on Core's Home.

Until the organisers offer a feed, this reads the club's public pages politely:
- robots.txt is read first (once a day) and obeyed, including Crawl-delay;
- a full refresh at most once an hour (3.3.8), an on-demand refresh at most once per 10 minutes,
  with exponential backoff after failures and Retry-After honoured;
- one page at a time with a pause between pages, a bounded body size, and a User-Agent
  that names Core and says who is asking ("Core/<version> (+contact: club member)");
- no login, no cookies, no credentials; only public display names are kept.

Parsing is pure (``parse_*`` functions take HTML text and return plain dicts). The home, nominations and
leaderboards parsers read the club's own markup and data attributes (event classes w/m/r/s/x, ``a.room``
cards, ``ol.cal-strip``, ``section.run``, ``data-sys``/``data-s-*`` rows, ``<time datetime>``), as captured
on 2026-11-27 (tests/fixtures/club/). Each section has a structure flag that is true only when that section
was parsed; when a page's structure stops matching, one warning is logged per change and the section's last
good data is kept. The member page is still read by visible text (``html_lines``): no capture of it exists.

``ClubProvider`` is the seam: an official JSON feed or API can replace ``PrgcScraperProvider``
by returning the same snapshot shape (see ``JsonFeedProvider``).

Standard library only, and only modules Decky's frozen Windows Python ships: ``html`` and
``html.entities`` but no ``html.parser`` and no ``xml``, so the small DOM below is built with a regex
tokenizer (the same set home_news.py uses).
"""
import datetime
import gzip
import html
import json
import logging
import os
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = "https://www.playretrogamesclub.com"
HOST = "www.playretrogamesclub.com"
SCHEMA = 1
REFRESH = 3600              # automatic refresh: at most hourly (3.3.8; was 6 hours)
ON_DEMAND = 10 * 60         # on-demand refresh: at most once per 10 minutes
ROBOTS_TTL = 24 * 3600
PAGE_PAUSE = 1.5            # seconds between page requests (raised by robots Crawl-delay)
LIMIT = 1024 * 1024         # bytes per page
TIMEOUT = 10
LOG = logging.getLogger("core.club")

EVENT_ANYWHERE = re.compile(r"(game of the week|game of the month|romhack of the month|game of the season|[a-z]+ special)", re.I)
# The club's system filter, plus common spellings. Value = the short code the site uses.
PLATFORMS = {
    "arcade": "Arcade", "dc": "DC", "dreamcast": "DC", "gb": "GB", "gba": "GBA", "gbc": "GBC", "gc": "GC",
    "gamecube": "GC", "md": "MD", "genesis": "MD", "mega drive": "MD", "n64": "N64", "nds": "NDS", "ds": "NDS",
    "nes": "NES", "nes/famicom": "NES", "famicom": "NES", "pce": "PCE", "ps1": "PS1", "psx": "PS1", "ps2": "PS2",
    "psp": "PSP", "sms": "SMS", "snes": "SNES", "snes/super famicom": "SNES", "wii": "Wii", "sat": "SAT",
    "saturn": "SAT", "3ds": "3DS", "atari 2600": "2600", "2600": "2600", "neo geo": "NEOGEO", "32x": "32X",
    "sega cd": "SCD", "lynx": "LYNX", "ws": "WS", "vb": "VB",
}
_PLATFORM_TOKEN = "|".join(sorted((re.escape(k) for k in PLATFORMS), key=len, reverse=True))
PLATFORM_RE = re.compile(r"(?<![\w/])(" + _PLATFORM_TOKEN + r")(\s+romhack)?(?![\w/])", re.I)
SLUG_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")


# ---------------------------------------------------------------------------------------------
# HTML -> lines. A line is one block (heading, paragraph, list item, table row) with its text,
# its links, any machine-readable times found in attributes, and its images.
# ---------------------------------------------------------------------------------------------
_BLOCK = {"p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article", "header", "footer",
          "ul", "ol", "table", "thead", "tbody", "tfoot", "main", "nav", "br", "dt", "dd", "dl", "figure",
          "figcaption", "aside", "details", "summary", "form", "blockquote", "caption", "hr"}
_CELL = {"td", "th"}
_SKIP = {"script", "style", "noscript", "template", "svg", "head", "select", "option", "button"}
_TAG = re.compile(r"<!--[\s\S]*?-->|<(/?)([a-zA-Z][\w-]*)((?:\s+[^\s=>]+(?:\s*=\s*(?:\"[^\"]*\"|'[^']*'|[^\s>]+))?)*)\s*/?>", re.S)
_ATTR = re.compile(r"([^\s=>/]+)(?:\s*=\s*(\"[^\"]*\"|'[^']*'|[^\s>]+))?")
_TIME_ATTR = re.compile(r"^(datetime|content|data-[\w-]*(?:time|end|ends|until|utc|date|ts|deadline)[\w-]*)$", re.I)


def _attrs(raw):
    out = {}
    for name, value in _ATTR.findall(raw or ""):
        if value[:1] in "\"'": value = value[1:-1]
        out[name.lower()] = html.unescape(value)
    return out


def _clean(value):
    return re.sub(r"\s+", " ", html.unescape(value or "")).strip()


def parse_time_value(value):
    """An ISO 8601 date-time or a Unix epoch (s or ms) as epoch seconds, else None."""
    value = (value or "").strip()
    if re.fullmatch(r"\d{10}", value): return float(value)
    if re.fullmatch(r"\d{13}", value): return int(value) / 1000.0
    if re.match(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", value):
        try:
            parsed = datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
            if parsed.tzinfo is None: parsed = parsed.replace(tzinfo=datetime.timezone.utc)
            return parsed.timestamp()
        except ValueError:
            return None
    return None


def html_lines(document):
    lines = []
    state = {"text": [], "links": [], "times": [], "imgs": [], "level": 0}
    anchors = []
    skip = []

    def flush():
        # A link that wraps several blocks (a card-sized <a>) belongs to each of them.
        for index, (href, start) in enumerate(anchors):
            label = _clean("".join(state["text"][start:]).replace("|", " "))
            if label or state["imgs"]: state["links"].append({"text": label, "href": href})
            anchors[index] = (href, 0)
        text = _clean("".join(state["text"]))
        text = re.sub(r"(\s*\|\s*)+", " | ", text).strip(" |")
        if text or state["imgs"] or state["times"]:
            lines.append({"text": text, "links": state["links"], "times": state["times"], "imgs": state["imgs"],
                          "level": state["level"]})
        state.update(text=[], links=[], times=[], imgs=[], level=0)

    position = 0
    for match in _TAG.finditer(document):
        if not skip:
            state["text"].append(document[position:match.start()])
        position = match.end()
        if match.group(0).startswith("<!--"): continue
        closing, name, raw = match.group(1) == "/", match.group(2).lower(), match.group(3)
        if name in _SKIP:
            if closing:
                if skip and skip[-1] == name: skip.pop()
            elif not match.group(0).endswith("/>"):
                skip.append(name)
            continue
        if skip: continue
        attrs = {} if closing else _attrs(raw)
        for key, value in attrs.items():
            if _TIME_ATTR.match(key):
                parsed = parse_time_value(value)
                if parsed: state["times"].append(parsed)
        if name in _BLOCK:
            flush()
            if not closing and re.fullmatch(r"h[1-6]", name): state["level"] = int(name[1])
            continue
        if name in _CELL:
            state["text"].append(" | ")
            continue
        if name == "a":
            if closing:
                if anchors:
                    href, start = anchors.pop()
                    label = _clean("".join(state["text"][start:]).replace("|", " "))
                    state["links"].append({"text": label, "href": href})
            else:
                anchors.append((attrs.get("href", ""), len(state["text"])))
            continue
        if name == "img" and not closing:
            src = attrs.get("src") or attrs.get("data-src") or ""
            if src: state["imgs"].append({"src": src, "alt": attrs.get("alt", "")})
    if not skip:
        state["text"].append(document[position:])
    flush()
    return lines


def _absolute(href):
    try:
        url = urllib.parse.urljoin(BASE + "/", href or "")
        parts = urllib.parse.urlsplit(url)
        if parts.scheme != "https" or parts.hostname != HOST or parts.username or parts.password: return ""
        return url
    except ValueError:
        return ""


def _path_slug(href, prefix):
    url = _absolute(href)
    if not url: return ""
    match = re.match(r"^/" + prefix + r"/([^/?#]+)/?$", urllib.parse.urlsplit(url).path)
    slug = urllib.parse.unquote(match.group(1)).lower() if match else ""
    return slug if SLUG_RE.match(slug) else ""


def platform_code(text):
    match = PLATFORM_RE.search(text or "")
    if not match: return "", False
    return PLATFORMS[match.group(1).lower()], bool(match.group(2))


def _sections(lines, pattern):
    """Lines under the first heading matching ``pattern``, up to the next heading of the same or a
    higher level. None when no heading matches."""
    for index, line in enumerate(lines):
        if line["level"] and re.search(pattern, line["text"], re.I):
            level, body = line["level"], []
            for other in lines[index + 1:]:
                if other["level"] and other["level"] <= level: break
                body.append(other)
            return body
    return None


# ---------------------------------------------------------------------------------------------
# A small, forgiving DOM. Decky's frozen Python ships ``html`` and ``html.entities`` but not
# ``html.parser`` (tests/decky_frozen_stdlib.txt in the plugin forks), so the tree is built from the
# same tag regex as html_lines: elements with their attributes and children, text as strings.
# Unclosed elements are closed by their parent's end tag; stray end tags are ignored.
# ---------------------------------------------------------------------------------------------
_VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"}
_RAW = {"script", "style", "noscript", "template", "svg", "head", "textarea", "title"}
# An open element of the same family is closed by the next one (<li> after <li>, <td> after <th>).
_AUTO_CLOSE = {"li": {"li"}, "tr": {"tr", "td", "th"}, "td": {"td", "th"}, "th": {"td", "th"}, "p": {"p"},
               "dt": {"dt", "dd"}, "dd": {"dt", "dd"}, "option": {"option"}}
_SCOPE = {"ul", "ol", "table", "tbody", "thead", "tfoot", "dl", "select", "section", "div"}
_MAX_NODES = 60000


class Node:
    __slots__ = ("tag", "attrs", "children", "parent")

    def __init__(self, tag, attrs=None, parent=None):
        self.tag, self.attrs, self.children, self.parent = tag, attrs or {}, [], parent

    @property
    def classes(self):
        return set((self.attrs.get("class") or "").split())

    def has(self, *names):
        classes = self.classes
        return all(name in classes for name in names)

    def get(self, name, default=""):
        return self.attrs.get(name, default)

    def elements(self):
        """Every descendant element, document order."""
        stack = [child for child in reversed(self.children) if isinstance(child, Node)]
        while stack:
            node = stack.pop()
            yield node
            stack.extend(child for child in reversed(node.children) if isinstance(child, Node))

    def find_all(self, tag=None, cls=(), **attrs):
        cls = (cls,) if isinstance(cls, str) else tuple(cls)
        for node in self.elements():
            if tag and node.tag != tag: continue
            if cls and not node.has(*cls): continue
            if any(node.attrs.get(key.replace("_", "-")) != value for key, value in attrs.items()): continue
            yield node

    def find(self, tag=None, cls=(), **attrs):
        return next(self.find_all(tag, cls, **attrs), None)

    def child_elements(self):
        return [child for child in self.children if isinstance(child, Node)]

    @property
    def text(self):
        parts, stack = [], list(reversed(self.children))
        while stack:
            item = stack.pop()
            if isinstance(item, Node): stack.extend(reversed(item.children))
            else: parts.append(item)
        return _clean("".join(parts))


def parse_dom(document):
    root = Node("#root")
    current, count, raw = root, 0, None
    position = 0
    for match in _TAG.finditer(document or ""):
        between = document[position:match.start()]
        position = match.end()
        whole = match.group(0)
        if whole.startswith("<!--"):
            if raw is None and between: current.children.append(between)
            continue
        closing, name = match.group(1) == "/", match.group(2).lower()
        if raw is not None:
            if closing and name == raw: raw = None
            continue
        if between: current.children.append(between)
        if closing:
            node = current
            while node is not root and node.tag != name: node = node.parent
            if node is not root: current = node.parent
            continue
        if name in _RAW:
            if not whole.endswith("/>"): raw = name
            continue
        count += 1
        if count > _MAX_NODES: break
        closes = _AUTO_CLOSE.get(name)
        if closes:
            node = current
            while node is not root and node.tag not in _SCOPE:
                if node.tag in closes:
                    current = node.parent
                    break
                node = node.parent
        element = Node(name, _attrs(match.group(3)), current)
        current.children.append(element)
        if name not in _VOID and not whole.endswith("/>"): current = element
    if raw is None and position < len(document or ""): current.children.append(document[position:])
    return root


# The club's event classes (and the ids of its nominations sections) -> Core's kinds and Assemble's event codes.
EVENT_CLASSES = {"w": "week", "m": "month", "r": "romhack", "s": "season", "x": "special"}
EVENT_IDS = {"gotw": "week", "gotm": "month", "rhotm": "romhack", "gots": "season"}
EVENT_CODES = {"week": "gotw", "month": "gotm", "romhack": "rhotm", "season": "gots", "special": "special"}
RA_GAME_RE = re.compile(r"^https://retroachievements\.org/game/(\d{1,9})/?$")
COVER_RE = re.compile(r"^/img/(\d{1,9})\.(?:png|jpg|jpeg|webp)$")


def _event_of(node):
    for cls in node.classes:
        if cls in EVENT_CLASSES: return EVENT_CLASSES[cls]
    return ""


def _number(value, kind=int):
    text = (value or "").replace(",", "").strip()
    try:
        return kind(text) if text else None
    except ValueError:
        return None


def _time_of(node):
    """Epoch seconds of the first <time datetime> (or data-end) inside ``node``, else None."""
    if node is None: return None
    for element in [node, *node.elements()]:
        for key in ("datetime", "data-end"):
            if element.attrs.get(key):
                parsed = parse_time_value(element.attrs[key])
                if parsed: return parsed
    return None


def _platform(value):
    """The club's system code ("PS1", "GBA romhack") -> (code, romhack). Unknown codes stay as written."""
    text = _clean(value)
    romhack = bool(re.search(r"\bromhack\b", text, re.I))
    text = re.sub(r"\s*\bromhack\b", "", text, flags=re.I).strip()
    if not text: return "", romhack
    known = PLATFORMS.get(text.lower())
    if known: return known, romhack
    code, _ = platform_code(text)
    if code: return code, romhack
    return (text[:12] if re.fullmatch(r"[A-Za-z0-9 ./+-]{1,24}", text) else ""), romhack


def _cover(node):
    img = node.find("img", "cover") or node.find("img") if node is not None else None
    if img is None: return "", ""
    src = img.get("src") or img.get("data-src")
    url = _absolute(src)
    path = urllib.parse.urlsplit(url).path if url else ""
    return (path if COVER_RE.match(path) else ""), url


def _section_by_heading(root, pattern):
    """The block that holds a ``.sec`` heading matching ``pattern`` (the home's "Coming up", "Nominations",
    "Top players" columns), else None."""
    for heading in root.find_all("h2"):
        if not re.search(pattern, heading.text, re.I): continue
        holder = heading.parent
        if holder is not None and holder.has("sec") and holder.parent is not None: return holder.parent
        return holder
    return None


# ---------------------------------------------------------------------------------------------
# Pure page parsers, anchored on the club's own markup (captured 2026-11-27, tests/fixtures/club/)
# ---------------------------------------------------------------------------------------------
def parse_room(room, now=None):
    """One ``a.room.<event>`` card on the home page, or None when it is not a game card."""
    kind = _event_of(room)
    slug = _path_slug(room.get("href"), "games")
    heading = room.find("h3")
    title = heading.text if heading else ""
    if not title:
        img = room.find("img")
        title = re.sub(r"\s+box art$", "", (img.get("alt") if img else ""), flags=re.I).strip()
    if not kind or not slug or not title: return None
    tag = room.find(cls="tag")
    label = tag.text if tag else ""
    plat = room.find(cls="plat")
    plat_text = plat.text if plat else ""
    system, _, rest = plat_text.partition("·")
    platform, romhack = _platform(system)
    hours = re.search(r"(\d+(?:\.\d+)?)\s*hours?", rest or plat_text, re.I)
    facts = {}
    holder = room.find(cls="facts")
    for fact in holder.child_elements() if holder else []:
        value, name = fact.find("b"), fact.find("span")
        if value is not None and name is not None: facts[name.text.lower()] = value.text
    firsts = {}
    for line in room.find_all(cls="first"):
        who = line.find("b")
        key = "first_100" if re.match(r"first to 100", line.text, re.I) else "first_clear" if re.match(r"first clear", line.text, re.I) else ""
        if key and who is not None:
            firsts[key] = {"name": who.text[:64], "at": _time_of(line)}
    ends_at = _time_of(room.find(cls="ends")) or _time_of(room.find(cls="countdown"))
    cover, image = _cover(room)
    return {"kind": kind, "event": EVENT_CODES[kind], "label": label[:80] or kind.title(), "title": title[:120],
            "slug": slug, "platform": platform, "romhack": romhack or kind == "romhack", "url": _absolute(room.get("href")),
            "image": image, "cover": cover, "ends_at": ends_at, "beaten": _number(facts.get("beaten")),
            "at100": _number(facts.get("at 100%")), "rating": _number(facts.get("rating"), float),
            "hours": float(hours.group(1)) if hours else None,
            "first_clear": firsts.get("first_clear"), "first_100": firsts.get("first_100")}


def parse_home(document, now):
    root = parse_dom(document)
    picks, seen = [], set()
    for room in root.find_all("a", "room"):
        pick = parse_room(room, now)
        if not pick: continue
        key = pick["kind"] if pick["kind"] != "special" else (pick["kind"], pick["slug"])
        if key in seen: continue
        seen.add(key)
        picks.append(pick)

    schedule = []
    strip = root.find("ol", "cal-strip")
    for item in strip.find_all("li") if strip is not None else []:
        when = next((child for child in item.child_elements() if child.tag == "time"), None) or item.find("time")
        at = _time_of(when)
        head = item.find("strong")
        title = head.text if head else ""
        if at is None or not title: continue
        tags = [{"kind": _event_of(tag), "label": tag.text[:80]} for tag in item.find_all(cls="tag") if tag.text]
        until = None
        for small in item.find_all("small"):
            if small.parent is item: until = _time_of(small) or until
        named = [tag["label"] for tag in tags if tag["label"].lower() not in title.lower()]
        text = title + (": " + ", ".join(named) if named else "")
        schedule.append({"at": at, "text": text[:200], "title": title[:120], "tags": tags[:6], "until": until})
        if len(schedule) >= 10: break

    nominations = []
    block = _section_by_heading(root, r"^nominations$")
    noms = block.find(cls="noms") if block is not None else None
    for item in noms.find_all("li") if noms is not None else []:
        tag, num = item.find(cls="tag"), item.find(cls="num")
        count = _number(num.text) if num is not None else None
        if tag is None or count is None: continue
        nominations.append({"kind": _event_of(tag), "label": tag.text[:120], "count": count})

    top = []
    block = _section_by_heading(root, r"^top players$")
    board = block.find("ol", "list") if block is not None else None
    for index, item in enumerate(board.find_all("li") if board is not None else []):
        link = item.find("a", "pname") or item.find("a")
        slug = _path_slug(link.get("href"), "members") if link is not None else ""
        rank, num = item.find(cls="rn"), item.find(cls="num")
        beaten = re.search(r"\d[\d,]*", num.text) if num is not None else None
        if not slug or not link.text or not beaten: continue
        top.append({"rank": _number(rank.text) if rank is not None else index + 1, "name": link.text[:64], "slug": slug,
                    "beaten": int(beaten.group(0).replace(",", ""))})

    stats = {}
    totals = root.find("ul", "totals")
    for item in totals.find_all("li") if totals is not None else []:
        value, name = item.find("b"), item.find("span")
        if value is None or name is None: continue
        label = name.text.lower()
        key = "players" if label == "players" else "games_beaten" if label.startswith("club games beaten") \
            else "hours" if label.startswith("hours played") else "picks" if label.startswith("picks") else ""
        number = _number(value.text)
        if key and number is not None: stats[key] = number

    return {"picks": picks, "top": top[:10], "schedule": schedule, "nomination_counts": nominations, "stats": stats,
            "structure": {"playing_now": bool(picks), "top_players": bool(top), "schedule": bool(schedule),
                          "nominations": bool(nominations), "stats": bool(stats)}}


def _ra_game(href):
    match = RA_GAME_RE.match((href or "").strip())
    return int(match.group(1)) if match else None


def parse_nomination_row(row):
    link = next((a for a in row.find_all("a") if _ra_game(a.get("href"))), None)
    title = link.text if link is not None else (row.get("data-s-name") or "")
    if not title: return None
    system = row.get("data-sys")
    label = row.find(cls="muted")
    platform, romhack = _platform(system or (label.text if label is not None else ""))
    if label is not None and re.search(r"\bromhack\b", label.text, re.I): romhack = True
    nominator = row.find("a", "pname")
    cover, _ = _cover(row)
    return {"title": title[:120], "platform": platform, "romhack": romhack,
            "nominator": nominator.text[:64] if nominator is not None else "",
            "nominator_slug": _path_slug(nominator.get("href"), "members") if nominator is not None else "",
            "hours": _number(row.get("data-s-hours"), float), "order": _number(row.get("data-s-order")),
            "ra_game_id": _ra_game(link.get("href")) if link is not None else None, "cover": cover}


def parse_nominations(document, now):
    """One group per ``section.run`` (gotm, rhotm, gots, ...): event, period heading, vote opening, count, rows."""
    root = parse_dom(document)
    groups = []
    for section in root.find_all("section", "run"):
        kind = EVENT_IDS.get(section.get("id")) or _event_of(section)
        if not kind: continue
        tag, heading = section.find(cls="tag"), section.find("h2")
        event_label, period = (tag.text if tag else ""), (heading.text if heading else "")
        opens_at = closes_at = None
        for live in section.find_all(cls="live"):
            if re.search(r"\bopens?\b", live.text, re.I): opens_at = opens_at or _time_of(live)
            elif re.search(r"\bclos", live.text, re.I): closes_at = closes_at or _time_of(live)
        count = None
        for stat in section.find_all(cls="stat"):
            value, name = stat.find("b"), stat.find("span")
            if value is not None and name is not None and "nominated" in name.text.lower():
                count = _number(value.text)
                break
        table = section.find("table", id="nomt-" + section.get("id")) or section.find("table")
        items = []
        body = table.find("tbody") if table is not None else None
        for row in (body or table).find_all("tr") if table is not None else []:
            if row.find("th") is not None and row.find("td") is None: continue
            item = parse_nomination_row(row)
            if item and len(items) < 60: items.append(item)
        if not event_label and not period and not items: continue
        label = " · ".join(part for part in (event_label, period) if part)
        groups.append({"kind": kind, "event": EVENT_CODES[kind], "label": label[:120], "period": period[:60],
                       "opens_at": opens_at, "closes_at": closes_at,
                       "count": count if count is not None else (len(items) or None), "items": items})
    return groups


LEADERBOARD_STATS = ("hardcore", "completed", "mastered", "firsts", "firsthundred", "hours")


def parse_leaderboard(document):
    """The first board (All time · All events): ranked rows with the stats the site keeps in data-s-*."""
    root = parse_dom(document)
    table = root.find("table", id="board-all-all") or root.find("table", "lb")
    rows = []
    for row in table.find_all("tr") if table is not None else []:
        link = row.find("a", "pname")
        slug = _path_slug(link.get("href"), "members") if link is not None else ""
        beaten = _number(row.get("data-s-total"))
        if not slug or not link.text or beaten is None: continue
        rank = row.find("td", "rank")
        entry = {"rank": _number(rank.text) if rank is not None else None, "name": link.text[:64], "slug": slug, "beaten": beaten}
        if entry["rank"] is None: entry["rank"] = len(rows) + 1
        for key in LEADERBOARD_STATS:
            value = _number(row.get("data-s-" + key), float)
            entry[key] = int(value) if value is not None and value.is_integer() else value
        by_event = {}
        counts = row.find(cls="lb-counts")
        for part in counts.child_elements() if counts is not None else []:
            kind, value = _event_of(part), part.find("b")
            if kind and value is not None and _number(value.text) is not None: by_event[kind] = _number(value.text)
        entry["by_event"] = by_event
        rows.append(entry)
    seen, unique = set(), []
    for row in rows:
        if row["slug"] in seen: continue
        seen.add(row["slug"])
        unique.append(row)
    return unique


def parse_member(document):
    lines = html_lines(document)
    joined = " ".join(line["text"] for line in lines)
    heading = next((line["text"] for line in lines if line["level"] == 1), "")
    name = re.sub(r"\s*\(.*\)\s*$", "", heading).strip()

    def number(pattern):
        found = re.search(pattern, joined, re.I)
        return int(found.group(1).replace(",", "")) if found else None

    rank = re.search(r"#\s*(\d+)\s+of\s+(\d[\d,]*)", joined)
    streaks = []
    for line in _sections(lines, r"^streaks") or lines:
        found = re.search(r"(\d+)\s+(week|month|season)s?\s+in a row(\s+now)?", line["text"], re.I)
        if not found: continue
        event = EVENT_ANYWHERE.search(line["text"])
        best = re.search(r"best\s+(\d+)", line["text"], re.I)
        current = int(found.group(1)) if found.group(3) or not re.search(r"ended|broke|last", line["text"], re.I) else 0
        streaks.append({"event": _clean(event.group(1)) if event else "", "count": current, "unit": found.group(2).lower(),
                        "best": int(best.group(1)) if best else int(found.group(1))})
    crowns = []
    for line in lines[:80]:
        for found in re.finditer(r"(Triple Crown|Perfect Season|Grand Slam)(?:,?\s*(Completed|Mastered))?\s+for\s+([A-Za-z0-9 ,]+?\d{4})", line["text"]):
            crown = {"crown": found.group(1), "tier": found.group(2) or "", "period": found.group(3).strip()}
            if crown not in crowns: crowns.append(crown)
    beaten_slugs = []
    for line in _sections(lines, r"games beaten") or []:
        for link in line["links"]:
            slug = _path_slug(link["href"], "games")
            if slug and slug not in beaten_slugs: beaten_slugs.append(slug)
    best_streak = max(streaks, key=lambda s: (s["count"], {"season": 3, "month": 2, "week": 1}.get(s["unit"], 0)), default=None)
    return {"name": name[:64], "rank": int(rank.group(1)) if rank else None,
            "of": int(rank.group(2).replace(",", "")) if rank else None,
            "beaten": number(r"(\d[\d,]*)\s*club games beaten"), "at100": number(r"(\d[\d,]*)\s*at 100%"),
            "hours": number(r"(\d[\d,]*)\s*hours played"), "streak": best_streak, "streaks": streaks,
            "crowns": crowns[:12], "beaten_slugs": beaten_slugs[:60]}


# ---------------------------------------------------------------------------------------------
# robots.txt
# ---------------------------------------------------------------------------------------------
def parse_robots(text, agent="core"):
    groups, current, last_was_agent = [], None, False
    for raw in (text or "").splitlines():
        line = raw.split("#", 1)[0].strip()
        if ":" not in line: continue
        key, value = (part.strip() for part in line.split(":", 1))
        key = key.lower()
        if key == "user-agent":
            if not last_was_agent:
                current = {"agents": [], "rules": [], "delay": None}
                groups.append(current)
            current["agents"].append(value.lower())
            last_was_agent = True
            continue
        last_was_agent = False
        if current is None: continue
        if key in ("allow", "disallow"):
            current["rules"].append((key == "allow", value))
        elif key == "crawl-delay":
            try: current["delay"] = float(value)
            except ValueError: pass
    chosen = [g for g in groups if any(a != "*" and a in agent.lower() for a in g["agents"])] or \
             [g for g in groups if "*" in g["agents"]]
    rules = [rule for g in chosen for rule in g["rules"]]
    delays = [g["delay"] for g in chosen if g["delay"] is not None]
    return {"rules": rules, "delay": max(delays) if delays else None}


def robots_allows(robots, path):
    if robots is None: return True
    if robots.get("block_all"): return False
    best = (-1, True)
    for allow, pattern in robots.get("rules", []):
        if pattern == "":
            continue  # "Disallow:" with no value allows everything
        regex = "^" + re.escape(pattern).replace(r"\*", ".*")
        if regex.endswith(r"\$"): regex = regex[:-2] + "$"
        if re.match(regex, path):
            length = len(pattern)
            if length > best[0] or (length == best[0] and allow): best = (length, allow)
    return best[1]


# ---------------------------------------------------------------------------------------------
# Providers
# ---------------------------------------------------------------------------------------------
class FetchError(Exception):
    def __init__(self, reason, status=0, retry_after=None):
        super().__init__(reason)
        self.reason, self.status, self.retry_after = reason, status, retry_after


class ClubProvider:
    """The seam an official feed or API replaces. ``snapshot`` returns the snapshot dict shape
    below; ``member`` returns a member dict or None. ``get(path)`` fetches one page politely."""
    name = "provider"

    def snapshot(self, get, now):  # -> {"picks", "top", "leaderboard", "schedule", "nominations", ...}
        raise NotImplementedError

    def member(self, get, now, display_name, leaderboard):  # -> member dict | None
        raise NotImplementedError


class PrgcScraperProvider(ClubProvider):
    name = "prgc-public-pages"
    pages = ("/", "/nominations/", "/leaderboards/")

    def snapshot(self, get, now):
        home = parse_home(get("/"), now)
        structure = {"home." + key: value for key, value in home.pop("structure").items()}
        structure["home.picks"] = bool(home["picks"])
        try:
            nominations = parse_nominations(get("/nominations/"), now)
        except FetchError as error:
            if error.reason != "robots_disallowed": raise
            nominations = []
        structure["nominations.categories"] = bool(nominations)
        try:
            leaderboard = parse_leaderboard(get("/leaderboards/"))
        except FetchError as error:
            if error.reason != "robots_disallowed": raise
            leaderboard = []
        structure["leaderboards.rows"] = bool(leaderboard)
        if not nominations and home["nomination_counts"]:
            # The home page's counts stand in for the nominations page (no rows, no vote date).
            nominations = [{"kind": n["kind"], "event": EVENT_CODES.get(n["kind"], ""), "label": n["label"], "period": "",
                            "opens_at": None, "closes_at": None, "count": n["count"], "items": []}
                           for n in home["nomination_counts"]]
        return {"picks": home["picks"], "top": (leaderboard or home["top"])[:5],
                "leaderboard": [{"rank": r["rank"], "name": r["name"], "slug": r["slug"], "beaten": r["beaten"],
                                 "hours": r.get("hours")} for r in leaderboard[:500]],
                "schedule": home["schedule"], "nominations": nominations, "stats": home["stats"], "structure": structure}

    def member(self, get, now, display_name, leaderboard):
        slug = resolve_member_slug(display_name, leaderboard)
        if not slug: return None
        member = parse_member(get("/members/" + slug + "/"))
        member["slug"] = slug
        listed = next((r for r in leaderboard if r["slug"] == slug), None)
        if listed:
            member["rank"] = member["rank"] or listed["rank"]
            if member["beaten"] is None: member["beaten"] = listed["beaten"]
            member["name"] = member["name"] or listed["name"]
        if member["of"] is None and leaderboard: member["of"] = len(leaderboard)
        return member


class JsonFeedProvider(ClubProvider):
    """For an official feed: one JSON document already in the snapshot shape (schema 1)."""
    name = "official-json"

    def __init__(self, path):
        self.path = path

    def snapshot(self, get, now):
        payload = json.loads(get(self.path))
        if not isinstance(payload, dict) or payload.get("schema") != SCHEMA: raise FetchError("unsupported_feed")
        return {key: payload.get(key) or [] for key in ("picks", "top", "leaderboard", "schedule", "nominations")} | {"stats": payload.get("stats") or {}, "structure": {}}

    def member(self, get, now, display_name, leaderboard):
        return None


def _fold(value):
    return re.sub(r"[^a-z0-9]+", "", (value or "").lower())


def resolve_member_slug(display_name, leaderboard):
    """The member's page from their public display name (or username) via the leaderboard."""
    wanted = _fold(display_name)
    if not wanted: return ""
    for row in leaderboard or []:
        if _fold(row.get("name")) == wanted or _fold(row.get("slug")) == wanted:
            return row["slug"]
    candidate = (display_name or "").strip().lower().replace(" ", "-").replace(".", "-")
    return candidate if SLUG_RE.match(candidate) and not leaderboard else ""


# ---------------------------------------------------------------------------------------------
# Service
# ---------------------------------------------------------------------------------------------
def core_version():
    """Core's installed version, from the installer's uninstall entry (HKCU, written from
    EternalRelease.Version). Empty when unknown: the version is never written here."""
    try:
        import winreg  # Windows only
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub") as key:
            value = str(winreg.QueryValueEx(key, "DisplayVersion")[0])
            return value if re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", value) else ""
    except Exception:
        return ""


def user_agent(version):
    return ("Core/" + version if version else "Core") + " (+contact: club member)"


def _http_fetch(url, headers):
    request = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
            data = response.read(LIMIT + 1)
            status = getattr(response, "status", 200)
            encoding = (response.headers.get("Content-Encoding") or "").lower()
            final = response.geturl()
    except urllib.error.HTTPError as error:
        return error.code, {"retry-after": error.headers.get("Retry-After", "") if error.headers else ""}, b""
    if encoding == "gzip" or data.startswith(b"\x1f\x8b"): data = gzip.decompress(data)
    if len(data) > LIMIT: raise FetchError("too_large")
    if (urllib.parse.urlsplit(final).hostname or "") != HOST: raise FetchError("redirected_off_site")
    return status, {}, data


class ClubFeed:
    def __init__(self, directory, fetch=None, clock=time.time, sleep=time.sleep, provider=None, version=None, log=LOG):
        self.settings_path = os.path.join(directory, "club.json")
        self.state_path = os.path.join(directory, "club-feed.json")
        self.lock = threading.RLock()
        self.fetch = fetch or _http_fetch
        self.clock, self.sleep, self.log = clock, sleep, log
        self.provider = provider or PrgcScraperProvider()
        self._version = version
        self._last_request = 0.0
        self._robots = None

    # settings ------------------------------------------------------------------------------
    def settings(self):
        with self.lock:
            try:
                with open(self.settings_path, encoding="utf-8") as f: raw = json.load(f)
                name = raw.get("display_name") if isinstance(raw.get("display_name"), str) else ""
                return {"enabled": raw.get("enabled") is not False, "display_name": name[:64]}
            except (OSError, ValueError, AttributeError):
                return {"enabled": True, "display_name": ""}

    def save(self, enabled, display_name):
        if type(enabled) is not bool or not isinstance(display_name, str): raise ValueError("invalid_club_settings")
        display_name = re.sub(r"[\x00-\x1f]", "", display_name).strip()[:64]
        with self.lock:
            os.makedirs(os.path.dirname(self.settings_path) or ".", exist_ok=True)
            self._write(self.settings_path, {"enabled": enabled, "display_name": display_name})
            return self.settings()

    # state ---------------------------------------------------------------------------------
    @staticmethod
    def _write(path, value):
        with open(path + ".tmp", "w", encoding="utf-8") as f: json.dump(value, f)
        os.replace(path + ".tmp", path)

    def _state(self):
        try:
            with open(self.state_path, encoding="utf-8") as f: raw = json.load(f)
            if isinstance(raw, dict) and raw.get("schema") == SCHEMA: return raw
        except (OSError, ValueError):
            pass
        return {"schema": SCHEMA, "snapshot": None, "fetched_at": 0, "attempt_at": 0, "failures": 0,
                "next_allowed": 0, "retry_until": 0, "error": "", "structure": {}, "robots": None, "member": None,
                "member_request_at": 0}

    def _save_state(self, state):
        try:
            os.makedirs(os.path.dirname(self.state_path) or ".", exist_ok=True)
            self._write(self.state_path, state)
        except OSError:
            self.log.warning("Club feed cache could not be written")

    # politeness ----------------------------------------------------------------------------
    def _headers(self):
        if self._version is None: self._version = core_version()
        return {"User-Agent": user_agent(self._version), "Accept": "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5",
                "Accept-Encoding": "gzip", "Accept-Language": "en"}

    def _pause(self, state):
        delay = PAGE_PAUSE
        robots = state.get("robots") or {}
        if robots.get("delay"): delay = max(delay, min(60.0, float(robots["delay"])))
        wait = self._last_request + delay - self.clock()
        if self._last_request and wait > 0: self.sleep(wait)
        self._last_request = self.clock()

    def _robots_for(self, state, now):
        robots = state.get("robots")
        if robots and now - robots.get("fetched_at", 0) < ROBOTS_TTL: return robots
        self._pause(state)
        try:
            status, headers, body = self.fetch(BASE + "/robots.txt", self._headers())
        except FetchError:
            raise
        except Exception as error:
            if robots: return robots  # keep yesterday's rules while the site is unreachable
            raise FetchError("unreachable") from error
        if 200 <= status < 300:
            parsed = parse_robots(body.decode("utf-8", "replace"))
        elif 400 <= status < 500 and status != 429:
            parsed = {"rules": [], "delay": None}          # no robots.txt: everything allowed
        else:
            raise FetchError("robots_unavailable", status, headers.get("retry-after"))  # 5xx/429: crawl nothing
        parsed["fetched_at"] = now
        state["robots"] = parsed
        return parsed

    def _getter(self, state, now):
        def get(path):
            robots = self._robots_for(state, now)
            if not robots_allows(robots, path):
                self._warn_once(state, "robots:" + path, "Club feed: robots.txt disallows %s; skipping it", path)
                raise FetchError("robots_disallowed")
            self._pause(state)
            if path.startswith("/members/"): state["member_request_at"] = now
            try:
                status, headers, body = self.fetch(BASE + path, self._headers())
            except FetchError:
                raise
            except Exception as error:
                raise FetchError("unreachable") from error
            if status in (429, 503):
                raise FetchError("rate_limited", status, headers.get("retry-after"))
            if not 200 <= status < 300: raise FetchError("http_%d" % status, status)
            return body.decode("utf-8", "replace")
        return get

    def _warn_once(self, state, key, message, *args):
        warned = state.setdefault("warned", [])
        if key in warned: return
        warned.append(key)
        del warned[:-50]
        self.log.warning(message, *args)

    def _check_structure(self, state, structure):
        previous = state.get("structure") or {}
        missing = sorted(key for key, ok in structure.items() if not ok)
        if structure != previous and missing:
            self.log.warning("Club feed: the club's pages changed shape; not found: %s", ", ".join(missing))
        elif structure != previous and previous:
            self.log.info("Club feed: page structure recognised again")
        state["structure"] = structure

    @staticmethod
    def _retry_after(value, now):
        try:
            return now + max(0.0, min(float(value), 24 * 3600))
        except (TypeError, ValueError):
            return 0

    # A section whose structure flag is False came back empty because its markup was not recognised (or its
    # page was skipped), not because the club emptied it: keep the last good data for it.
    CARRY = (("leaderboard", "leaderboards.rows"), ("top", "home.top_players"), ("schedule", "home.schedule"),
             ("nominations", "nominations.categories"), ("stats", "home.stats"))

    @classmethod
    def _carry_over(cls, previous, snapshot, structure):
        carried = []
        for key, flag in cls.CARRY:
            if structure.get(flag) is not False or snapshot.get(key): continue
            old = (previous or {}).get(key)
            if old:
                snapshot[key] = old
                carried.append(key)
        return carried

    # refresh -------------------------------------------------------------------------------
    def _refresh(self, state, now, display_name):
        state["attempt_at"] = now
        get = self._getter(state, now)
        try:
            snapshot = self.provider.snapshot(get, now)
            if not snapshot.get("picks"):
                raise FetchError("no_picks")
            member = None
            if display_name:
                try:
                    member = self.provider.member(get, now, display_name, snapshot.get("leaderboard") or snapshot.get("top"))
                except FetchError as error:
                    if error.reason in ("rate_limited", "unreachable"): raise
                    member = None
            structure = snapshot.pop("structure", {})
            if display_name: structure["member.found"] = member is not None
            self._check_structure(state, structure)
            snapshot["carried"] = self._carry_over(state.get("snapshot"), snapshot, structure)
            snapshot.update(schema=SCHEMA, source=self.provider.name, fetched_at=now)
            state.update(snapshot=snapshot, fetched_at=now, failures=0, error="", next_allowed=now + REFRESH,
                         member={"for": _fold(display_name), "data": member} if display_name else None)
        except FetchError as error:
            self._fail(state, now, error)
        except Exception as error:  # a parser bug must never take the Home down
            self.log.exception("Club feed refresh failed")
            self._fail(state, now, FetchError("parse_error"))
        self._save_state(state)

    def _fail(self, state, now, error):
        state["failures"] = int(state.get("failures") or 0) + 1
        state["error"] = error.reason
        state["next_allowed"] = now + min(REFRESH, ON_DEMAND * 2 ** (state["failures"] - 1))
        if error.retry_after is not None:
            state["retry_until"] = max(state.get("retry_until") or 0, self._retry_after(error.retry_after, now))
        if error.reason == "no_picks":
            self._check_structure(state, {**(state.get("structure") or {}), "home.picks": False})
        self._warn_once(state, "fail:" + error.reason, "Club feed refresh failed (%s); showing the last data", error.reason)

    def _refresh_member(self, state, now, display_name):
        # One member page per 10 minutes, whoever it is for (a name that matches nobody costs no request).
        key = _fold(display_name)
        if now - (state.get("member_request_at") or 0) < ON_DEMAND or now < (state.get("retry_until") or 0): return
        snapshot = state.get("snapshot") or {}
        try:
            member = self.provider.member(self._getter(state, now), now, display_name, snapshot.get("leaderboard") or snapshot.get("top"))
        except FetchError as error:
            if error.retry_after is not None:
                state["retry_until"] = self._retry_after(error.retry_after, now)
            member = None
        except Exception:
            self.log.exception("Club member refresh failed")
            member = None
        state["member"] = {"for": key, "data": member}
        self._save_state(state)

    @staticmethod
    def _on_demand_at(state):
        """Earliest on-demand refresh: 10 minutes after the last attempt, doubling per failure
        in a row (capped at the hourly cadence)."""
        attempt = state.get("attempt_at") or 0
        if not attempt: return 0
        failures = int(state.get("failures") or 0)
        return attempt + min(REFRESH, ON_DEMAND * 2 ** max(0, failures - 1))

    def get(self, force=False):
        config = self.settings()
        if not config["enabled"]:
            return {"enabled": False, "display_name": config["display_name"], "snapshot": None}
        with self.lock:
            now = self.clock()
            state = self._state()
            server_hold = now < (state.get("retry_until") or 0)
            due = now - (state.get("fetched_at") or 0) >= REFRESH and now >= (state.get("next_allowed") or 0)
            on_demand = force and now >= self._on_demand_at(state)
            refreshed = False
            if not server_hold and (due or on_demand):
                self._refresh(state, now, config["display_name"])
                refreshed = True
            name = config["display_name"]
            member_entry = state.get("member") or {}
            if name and state.get("snapshot") and member_entry.get("for") != _fold(name) and not server_hold:
                self._refresh_member(state, now, name)
                member_entry = state.get("member") or {}
            fetched = state.get("fetched_at") or 0
            snapshot = state.get("snapshot")
            if snapshot is not None:
                snapshot = dict(snapshot)
                snapshot["member"] = member_entry.get("data") if name and member_entry.get("for") == _fold(name) else None
            attempt = state.get("attempt_at") or 0
            return {"enabled": True, "display_name": name, "snapshot": snapshot, "updated": fetched or None,
                    "stale": bool(snapshot) and (bool(state.get("error")) or now - fetched > REFRESH + ON_DEMAND),
                    "error": state.get("error") or "", "refreshed": refreshed,
                    "next_refresh": max(state.get("next_allowed") or 0, state.get("retry_until") or 0),
                    "refresh_available_at": max(self._on_demand_at(state), state.get("retry_until") or 0)}
