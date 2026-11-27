"""Club feed: Play Retro Games Club (playretrogamesclub.com) on Core's Home.

Until the organisers offer a feed, this reads the club's public pages politely:
- robots.txt is read first (once a day) and obeyed, including Crawl-delay;
- a full refresh at most every 6 hours, an on-demand refresh at most once per 10 minutes,
  with exponential backoff after failures and Retry-After honoured;
- one page at a time with a pause between pages, a bounded body size, and a User-Agent
  that names Core and says who is asking ("Core/<version> (+contact: club member)");
- no login, no cookies, no credentials; only public display names are kept.

Parsing is pure (``parse_*`` functions take HTML text and return plain dicts) and anchored on
visible text and link targets rather than CSS classes, so it survives a restyle. When a page's
structure stops matching, one warning is logged per change.

``ClubProvider`` is the seam: an official JSON feed or API can replace ``PrgcScraperProvider``
by returning the same snapshot shape (see ``JsonFeedProvider``).

Standard library only, and only modules Decky's frozen Windows Python ships (no html.parser,
no xml): the same set home_news.py uses.
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
REFRESH = 6 * 3600          # automatic refresh: at most every 6 hours
ON_DEMAND = 10 * 60         # on-demand refresh: at most once per 10 minutes
ROBOTS_TTL = 24 * 3600
PAGE_PAUSE = 1.5            # seconds between page requests (raised by robots Crawl-delay)
LIMIT = 1024 * 1024         # bytes per page
TIMEOUT = 10
LOG = logging.getLogger("core.club")

EVENTS = (
    ("week", re.compile(r"^game of the week\b", re.I)),
    ("month", re.compile(r"^game of the month\b", re.I)),
    ("romhack", re.compile(r"^romhack of the month\b", re.I)),
    ("season", re.compile(r"^game of the season\b", re.I)),
    ("special", re.compile(r"^(?:[a-z]+\s){0,2}special\b", re.I)),
)
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
MONTHS = {m: i for i, m in enumerate(("jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"), 1)}
DATE_RE = re.compile(r"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+(\d{1,2})(?:,?\s+(\d{4}))?(?:,?\s+(\d{1,2}):(\d{2}))?\s*(UTC|GMT)?", re.I)
LEFT_RE = re.compile(r"(?:(\d+)\s*days?)?\s*(?:(\d+)\s*h(?:ours?)?)?\s+left", re.I)
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


def infer_date(month, day, year, hour, minute, now):
    """Text dates carry no year when it is the current one: take the year that puts the date
    nearest to ``now`` (the club never lists anything more than about a year away)."""
    try:
        if year:
            return datetime.datetime(int(year), month, day, hour, minute, tzinfo=datetime.timezone.utc).timestamp()
        today = datetime.datetime.fromtimestamp(now, datetime.timezone.utc).year
        candidates = []
        for y in (today - 1, today, today + 1):
            try: candidates.append(datetime.datetime(y, month, day, hour, minute, tzinfo=datetime.timezone.utc).timestamp())
            except ValueError: pass
        return min(candidates, key=lambda ts: abs(ts - now)) if candidates else None
    except ValueError:
        return None


def text_dates(text, now):
    out = []
    for m in DATE_RE.finditer(text or ""):
        hour = int(m.group(4)) if m.group(4) else 0
        minute = int(m.group(5)) if m.group(5) else 0
        ts = infer_date(MONTHS[m.group(1)[:3].lower()], int(m.group(2)), m.group(3), hour, minute, now)
        if ts is not None: out.append(ts)
    return out


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


def _event_kind(text):
    text = (text or "").strip()
    if len(text) > 48: return ""
    for kind, pattern in EVENTS:
        if pattern.match(text): return kind
    return ""


# ---------------------------------------------------------------------------------------------
# Pure page parsers
# ---------------------------------------------------------------------------------------------
def parse_picks(lines, now):
    """The current picks: one card per event label line, each with a /games/ link."""
    section = _sections(lines, r"playing now|now playing|current picks|playing now and next") or lines
    cards, current = [], None
    for line in section:
        kind = _event_kind(line["text"].split(" | ")[0])
        if kind:
            current = {"kind": kind, "label": _clean(EVENT_ANYWHERE.search(line["text"]).group(1)), "lines": []}
            cards.append(current)
            if not line["links"]: continue
            # A label and its title link in one block: the block belongs to the card.
        if current is not None:
            current["lines"].append(line)
    picks, seen = [], set()
    for card in cards:
        game = None
        for line in card["lines"]:
            for link in line["links"]:
                if _path_slug(link["href"], "games") and link["text"] and not _event_kind(link["text"]) \
                        and not re.search(r"\b(beaten|left|ends?)\b|\d+\s*h\b", link["text"], re.I):
                    game = link
                    break
            if game: break
        if not game: continue
        slug = _path_slug(game["href"], "games")
        joined = " ".join(line["text"] for line in card["lines"])
        title = re.sub(r"\s+box art$", "", game["text"], flags=re.I).strip()
        platform, romhack = "", False
        explicit = re.search(r"\(([^()]{1,24})\)", joined[joined.find(title) + len(title):] if title in joined else joined)
        if explicit: platform, romhack = platform_code(explicit.group(1))
        if not platform:
            for line in card["lines"]:
                if line["text"] != title and len(line["text"]) <= 24:
                    platform, romhack = platform_code(line["text"])
                    if platform: break
        if not platform:
            platform, _ = platform_code(slug.rsplit("-", 1)[-1])
        romhack = romhack or card["kind"] == "romhack"
        ends = None
        machine = [ts for line in card["lines"] for ts in line["times"]]
        if machine: ends = max(machine)
        if ends is None:
            ending = [line["text"] for line in card["lines"] if re.search(r"\bend|until|closes|left\b", line["text"], re.I)]
            dates = [ts for text in (ending or [joined]) for ts in text_dates(text, now)]
            if dates: ends = max(dates)
        if ends is None:
            left = LEFT_RE.search(joined)
            if left and (left.group(1) or left.group(2)):
                ends = round((now + int(left.group(1) or 0) * 86400 + int(left.group(2) or 0) * 3600) / 3600) * 3600
        beaten = re.search(r"(\d[\d,]*)\s*beaten", joined, re.I)
        image = ""
        for line in card["lines"]:
            for img in line["imgs"]:
                if "box art" in img["alt"].lower() or "/img/" in img["src"]:
                    image = _absolute(img["src"])
                    break
            if image: break
        key = (card["kind"], slug)
        if key in seen or any(p["kind"] == card["kind"] and p["kind"] != "special" for p in picks): continue
        seen.add(key)
        picks.append({"kind": card["kind"], "label": card["label"], "title": title, "slug": slug,
                      "platform": platform, "romhack": romhack, "url": _absolute(game["href"]), "image": image,
                      "ends_at": ends, "beaten": int(beaten.group(1).replace(",", "")) if beaten else None})
    return picks


def parse_member_rows(lines):
    """Ranked rows: one /members/ link per row, the first number after the name is games beaten."""
    rows = []
    for line in lines:
        members = [link for link in line["links"] if _path_slug(link["href"], "members") and link["text"]]
        if len(members) != 1: continue
        link = members[0]
        text = line["text"]
        at = text.find(link["text"])
        before, after = (text[:at], text[at + len(link["text"]):]) if at >= 0 else ("", text)
        count = re.search(r"(\d[\d,]*)\s*beaten", after, re.I) or re.search(r"(?<![\w.#])(\d[\d,]*)(?![\w.%])", after)
        if not count: continue
        rank = re.search(r"#?\s*(\d+)\D*$", before)
        rows.append({"rank": int(rank.group(1)) if rank else len(rows) + 1, "name": link["text"],
                     "slug": _path_slug(link["href"], "members"), "beaten": int(count.group(1).replace(",", ""))})
    seen, unique = set(), []
    for row in rows:
        if row["slug"] in seen: continue
        seen.add(row["slug"])
        unique.append(row)
    return unique


def parse_leaderboard(document):
    lines = html_lines(document)
    section = _sections(lines, r"all time\W+all events") or _sections(lines, r"all time") or lines
    return parse_member_rows(section)


def parse_schedule(lines, now):
    section = _sections(lines, r"coming up|schedule") or []
    items = []
    for line in section:
        dates = text_dates(line["text"], now)
        start = min(line["times"]) if line["times"] else (dates[0] if dates else None)
        if start is None: continue
        text = DATE_RE.sub("", line["text"], count=1).strip(" :|-·")
        if text: items.append({"at": start, "text": text[:200]})
    return items[:10]


def parse_home(document, now):
    lines = html_lines(document)
    top_section = _sections(lines, r"top players|leaderboard")
    nominations = []
    for line in _sections(lines, r"^nominations") or []:
        match = re.match(r"^(.*?)\s*\|?\s*(\d+)(?:\s+nominated)?$", line["text"])
        if match and EVENT_ANYWHERE.search(match.group(1)):
            nominations.append({"label": match.group(1).strip(" |"), "count": int(match.group(2))})
    stats = {}
    joined = " ".join(line["text"] for line in lines[:120])
    for key, pattern in (("players", r"(\d[\d,]*)\s*players"), ("games_beaten", r"(\d[\d,]*)\s*club games beaten"),
                         ("hours", r"(\d[\d,]*)\s*hours played"), ("picks", r"(\d[\d,]*)\s*picks\b")):
        found = re.search(pattern, joined, re.I)
        if found: stats[key] = int(found.group(1).replace(",", ""))
    return {"picks": parse_picks(lines, now), "top": parse_member_rows(top_section or [])[:10],
            "schedule": parse_schedule(lines, now), "nomination_counts": nominations, "stats": stats,
            "structure": {"playing_now": _sections(lines, r"playing now|now playing") is not None,
                          "top_players": top_section is not None}}


def parse_nominations(document, now):
    lines = html_lines(document)
    categories, current = [], None
    for line in lines:
        if line["level"] and EVENT_ANYWHERE.search(line["text"]) and not line["links"]:
            label = line["text"]
            kind = _event_kind(EVENT_ANYWHERE.search(label).group(1))
            current = {"kind": kind, "label": label[:120], "opens_at": None, "count": None, "items": []}
            categories.append(current)
            continue
        if current is None: continue
        if line["level"] and line["level"] <= 2: current = None; continue
        text = line["text"]
        if current["opens_at"] is None and re.search(r"vote opens|opens", text, re.I):
            dates = line["times"] or text_dates(text, now)
            if dates: current["opens_at"] = min(dates)
        counted = re.search(r"(\d+)\s+nominated", text, re.I)
        if counted and current["count"] is None: current["count"] = int(counted.group(1))
        game = next((l for l in line["links"] if l["text"] and ("retroachievements.org/game/" in l["href"] or _path_slug(l["href"], "games"))), None)
        if not game: continue
        nominator = next((l["text"] for l in line["links"] if _path_slug(l["href"], "members") and l["text"]), "")
        rest = text.replace(game["text"], " ", 1)
        platform, romhack = platform_code(rest)
        hours = re.search(r"(\d+(?:\.\d+)?)\s*h(?:ours?)?\b", rest, re.I)
        if len(current["items"]) < 60:
            current["items"].append({"title": game["text"][:120], "platform": platform, "romhack": romhack,
                                     "nominator": nominator[:64], "hours": float(hours.group(1)) if hours else None})
    for category in categories:
        if category["count"] is None and category["items"]: category["count"] = len(category["items"])
    return [c for c in categories if c["items"] or c["count"]]


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
            nominations = [{"kind": _event_kind(EVENT_ANYWHERE.search(n["label"]).group(1)), "label": n["label"],
                            "opens_at": None, "count": n["count"], "items": []} for n in home["nomination_counts"]]
        return {"picks": home["picks"], "top": (leaderboard or home["top"])[:5],
                "leaderboard": [{"rank": r["rank"], "name": r["name"], "slug": r["slug"], "beaten": r["beaten"]} for r in leaderboard[:500]],
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
        in a row (capped at the 6-hour cadence)."""
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
