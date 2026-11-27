"""The Club feed (quick_settings/club_feed.py): parsing Play Retro Games Club's public pages, robots.txt,
the 6-hour / 10-minute fetch limits, backoff, stale fallback and the once-per-change structure warning.

Fixtures (tests/fixtures/club/) are trimmed rebuilds of the club's pages as read on 2026-11-27: visible
text, headings, link targets and order, in two markups (home.html: tables and lists; home_cards.html:
cards with <time datetime>). The raw HTML was not reachable from the research machine, so the parser is
tested on structure rather than CSS classes. Only public display names; member.html is fictional.
"""
import datetime
import json
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from quick_settings import club_feed as club  # noqa: E402

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "club"
NOW = datetime.datetime(2026, 11, 27, 8, 0, tzinfo=datetime.timezone.utc).timestamp()


def utc(*parts):
    return datetime.datetime(*parts, tzinfo=datetime.timezone.utc).timestamp()


def fixture(name):
    return (FIXTURES / name).read_text(encoding="utf-8")


class Log:
    def __init__(self): self.warnings, self.infos = [], []
    def warning(self, message, *args): self.warnings.append(message % args if args else message)
    def info(self, message, *args): self.infos.append(message % args if args else message)
    def exception(self, message, *args): self.warnings.append(message)


class FakeSite:
    def __init__(self):
        self.pages = {
            "/robots.txt": (200, "User-agent: *\nDisallow: /admin/\n"),
            "/": (200, fixture("home.html")),
            "/nominations/": (200, fixture("nominations.html")),
            "/leaderboards/": (200, fixture("leaderboards.html")),
            "/members/examplemember/": (200, fixture("member.html")),
        }
        self.requests, self.headers, self.down, self.retry_after = [], [], False, None

    def __call__(self, url, headers):
        self.headers.append(headers)
        path = url[len(club.BASE):]
        self.requests.append(path)
        if self.down: raise OSError("offline")
        if self.retry_after is not None and path != "/robots.txt":
            return 429, {"retry-after": self.retry_after}, b""
        status, body = self.pages.get(path, (404, ""))
        return status, {}, body.encode("utf-8")


class Clock:
    def __init__(self, now): self.now = now
    def __call__(self): return self.now


class ParserTests(unittest.TestCase):
    def test_home_picks_with_platform_deadline_and_beaten(self):
        home = club.parse_home(fixture("home.html"), NOW)
        picks = [(p["kind"], p["title"], p["platform"], p["romhack"], p["ends_at"], p["beaten"]) for p in home["picks"]]
        self.assertEqual(picks, [
            ("week", "Mega Man", "NES", False, utc(2026, 11, 29), 35),
            ("month", "Donkey Kong Country", "SNES", False, utc(2026, 12, 1), 56),
            ("romhack", "Super Metroid: GBA Edition", "GBA", True, utc(2026, 12, 1), 23),
            ("season", "Paper Mario", "N64", False, utc(2027, 1, 1), 19),
        ])
        self.assertEqual(home["picks"][0]["url"], "https://www.playretrogamesclub.com/games/mega-man-nes/")
        self.assertEqual(home["picks"][0]["image"], "https://www.playretrogamesclub.com/img/1448.png")
        self.assertEqual(home["picks"][0]["label"], "Game of the Week")

    def test_home_top_players_schedule_nominations_and_stats(self):
        home = club.parse_home(fixture("home.html"), NOW)
        self.assertEqual([(r["rank"], r["name"], r["slug"], r["beaten"]) for r in home["top"]], [
            (1, "Gin649", "gin649", 35), (2, "Draven", "dragonmagic28", 34), (3, "popogego99", "popogego99", 33),
            (4, "malson", "malson", 32), (5, "joeysretrohandhelds", "joeysretrohandhelds", 32)])
        self.assertEqual(home["schedule"][0]["at"], utc(2026, 11, 28))
        self.assertTrue(home["schedule"][0]["text"].startswith("Votes open for December's Game of the Month"))
        self.assertEqual(home["schedule"][-1]["at"], utc(2027, 1, 1), "a yearless January date after November is next year")
        self.assertEqual([n["count"] for n in home["nomination_counts"]], [34, 15, 14])
        self.assertEqual(home["stats"], {"players": 179, "games_beaten": 1185, "hours": 7495, "picks": 84})
        self.assertEqual(home["structure"], {"playing_now": True, "top_players": True})

    def test_card_markup_with_machine_times_parses_the_same_picks(self):
        home = club.parse_home(fixture("home_cards.html"), NOW)
        self.assertEqual([(p["kind"], p["title"], p["platform"], p["romhack"]) for p in home["picks"]], [
            ("week", "Mega Man", "NES", False), ("month", "Donkey Kong Country", "SNES", False),
            ("romhack", "Super Metroid: GBA Edition", "GBA", True), ("season", "Paper Mario", "N64", False)])
        self.assertEqual(home["picks"][0]["ends_at"], utc(2026, 11, 29), "<time datetime> wins")
        self.assertEqual(home["picks"][2]["ends_at"], utc(2026, 12, 1), "data-ends epoch")
        self.assertEqual(home["picks"][3]["ends_at"], utc(2027, 1, 1), "'34 days 16 hours left' as a last resort")
        self.assertEqual(home["picks"][3]["platform"], "N64", "platform from the game's address")
        self.assertEqual([r["name"] for r in home["top"]], ["Gin649", "Draven"])

    def test_leaderboard_keeps_ranks_names_and_profile_slugs(self):
        rows = club.parse_leaderboard(fixture("leaderboards.html"))
        self.assertEqual(len(rows), 8, "only the first (All time, All events) table")
        self.assertEqual(rows[0], {"rank": 1, "name": "Gin649", "slug": "gin649", "beaten": 35})
        self.assertEqual(rows[6], {"rank": 9, "name": "jaso.90", "slug": "jaso-90", "beaten": 25})
        self.assertEqual(rows[7]["rank"], 42)

    def test_nominations_by_category(self):
        groups = club.parse_nominations(fixture("nominations.html"), NOW)
        self.assertEqual([(g["kind"], g["count"], g["opens_at"]) for g in groups], [
            ("month", 34, utc(2026, 11, 28)), ("romhack", 15, utc(2026, 11, 28)), ("season", 14, utc(2026, 12, 29))])
        self.assertEqual(groups[0]["items"][1], {"title": "Katamari Damacy", "platform": "PS2", "romhack": False,
                                                 "nominator": "Gain", "hours": 5.8})
        self.assertEqual(groups[1]["items"][1]["hours"], None)
        self.assertTrue(groups[1]["items"][0]["romhack"])
        self.assertEqual(groups[2]["items"][0]["title"], "EarthBound")

    def test_member_standing_streaks_and_crowns(self):
        member = club.parse_member(fixture("member.html"))
        self.assertEqual((member["name"], member["rank"], member["of"], member["beaten"], member["at100"]),
                         ("Example Member", 42, 179, 12, 1))
        self.assertEqual(member["streak"], {"event": "Game of the Week", "count": 3, "unit": "week", "best": 6})
        self.assertEqual(member["streaks"][2]["count"], 0, "an ended streak is not current")
        self.assertEqual(member["crowns"][0], {"crown": "Triple Crown", "tier": "", "period": "October to December 2026"})
        self.assertEqual(member["beaten_slugs"], ["donkey-kong-country-snes", "alien-hominid-gba"])

    def test_unknown_or_broken_markup_yields_nothing_rather_than_failing(self):
        for document in ("", "<html><body><p>Under maintenance</p></body></html>", "<div><a href='/games/x/'>",
                         "<<<>>> <script>Game of the Week</script>"):
            home = club.parse_home(document, NOW)
            self.assertEqual(home["picks"], [])
            self.assertEqual(club.parse_nominations(document, NOW), [])
            self.assertEqual(club.parse_leaderboard(document), [])

    def test_off_site_links_and_odd_slugs_are_ignored(self):
        lines = club.html_lines('<p>Game of the Week</p><p><a href="https://evil.example/games/x/">X</a></p>'
                                '<p><a href="/games/../../etc/">Y</a></p>')
        self.assertEqual(club.parse_picks(lines, NOW), [])

    def test_loads_without_modules_decky_frozen_python_lacks(self):
        from unittest.mock import patch
        import builtins
        import importlib.util
        original = builtins.__import__

        def restricted(name, *args, **kwargs):
            if name.startswith(("xml", "pyexpat", "html.parser", "sqlite3", "ssl_extra")): raise ModuleNotFoundError(name)
            return original(name, *args, **kwargs)
        with patch("builtins.__import__", side_effect=restricted):
            spec = importlib.util.spec_from_file_location("club_frozen_fixture", Path(__file__).resolve().parents[1] / "quick_settings/club_feed.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            self.assertEqual(len(module.parse_home(fixture("home.html"), NOW)["picks"]), 4)

    def test_member_slug_from_display_name_or_username(self):
        rows = club.parse_leaderboard(fixture("leaderboards.html"))
        self.assertEqual(club.resolve_member_slug("draven", rows), "dragonmagic28")
        self.assertEqual(club.resolve_member_slug("Example Member", rows), "examplemember")
        self.assertEqual(club.resolve_member_slug("jaso.90", rows), "jaso-90")
        self.assertEqual(club.resolve_member_slug("Nobody Here", rows), "")


class RobotsTests(unittest.TestCase):
    def test_rules_crawl_delay_and_specific_agent_group(self):
        robots = club.parse_robots("User-agent: *\nDisallow: /members/\nAllow: /members/public/\nCrawl-delay: 5\n")
        self.assertFalse(club.robots_allows(robots, "/members/gin649/"))
        self.assertTrue(club.robots_allows(robots, "/members/public/x"))
        self.assertTrue(club.robots_allows(robots, "/"))
        self.assertEqual(robots["delay"], 5)
        specific = club.parse_robots("User-agent: *\nDisallow: /\n\nUser-agent: Core\nDisallow: /leaderboards/\n")
        self.assertTrue(club.robots_allows(specific, "/"))
        self.assertFalse(club.robots_allows(specific, "/leaderboards/"))
        self.assertTrue(club.robots_allows(club.parse_robots("User-agent: *\nDisallow:\n"), "/anything"))
        self.assertFalse(club.robots_allows(club.parse_robots("User-agent: *\nDisallow: /*?q=\n"), "/search?q=x"))


class FeedTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.site, self.clock, self.log, self.sleeps = FakeSite(), Clock(NOW), Log(), []
        self.feed = self.make()

    def tearDown(self):
        self.tmp.cleanup()

    def make(self):
        return club.ClubFeed(self.tmp.name, fetch=self.site, clock=self.clock, sleep=self.sleeps.append,
                             version="3.3.2", log=self.log)

    def test_first_read_fetches_politely_and_caches_to_disk(self):
        reply = self.feed.get()
        self.assertEqual(self.site.requests, ["/robots.txt", "/", "/nominations/", "/leaderboards/"])
        self.assertEqual(self.site.headers[0]["User-Agent"], "Core/3.3.2 (+contact: club member)")
        self.assertNotIn("Cookie", self.site.headers[0])
        self.assertEqual(len(reply["snapshot"]["picks"]), 4)
        self.assertEqual([r["name"] for r in reply["snapshot"]["top"]][:2], ["Gin649", "Draven"])
        self.assertEqual(reply["updated"], NOW)
        self.assertFalse(reply["stale"])
        self.assertIsNone(reply["snapshot"]["member"])
        state = json.loads((Path(self.tmp.name) / "club-feed.json").read_text(encoding="utf-8"))
        self.assertEqual(state["snapshot"]["source"], "prgc-public-pages")
        # A second instance (a restart) serves the disk cache without a request.
        self.site.requests.clear()
        self.assertEqual(len(self.make().get()["snapshot"]["picks"]), 4)
        self.assertEqual(self.site.requests, [])

    def test_at_most_every_6_hours_and_on_demand_at_most_once_per_10_minutes(self):
        self.feed.get()
        self.site.requests.clear()
        self.clock.now += 5 * 3600
        self.feed.get()
        self.assertEqual(self.site.requests, [], "inside 6 hours: cache")
        self.clock.now = NOW + 9 * 60
        self.assertFalse(self.feed.get(force=True)["refreshed"])
        self.assertEqual(self.site.requests, [], "on demand inside 10 minutes: refused")
        self.clock.now = NOW + 10 * 60
        self.assertTrue(self.feed.get(force=True)["refreshed"])
        self.assertEqual(self.site.requests, ["/", "/nominations/", "/leaderboards/"], "robots.txt is cached for a day")
        self.site.requests.clear()
        self.clock.now += 6 * 3600
        self.assertTrue(self.feed.get()["refreshed"])

    def test_pages_are_spaced_out_and_robots_crawl_delay_raises_the_gap(self):
        self.feed.get()
        self.assertEqual(len(self.sleeps), 3)
        self.assertTrue(all(abs(wait - club.PAGE_PAUSE) < 1e-6 for wait in self.sleeps))
        self.site.pages["/robots.txt"] = (200, "User-agent: *\nCrawl-delay: 7\n")
        self.sleeps.clear()
        self.clock.now += club.ROBOTS_TTL
        self.make().get()
        self.assertTrue(self.sleeps and all(abs(wait - 7) < 1e-6 for wait in self.sleeps[1:]))

    def test_robots_disallow_is_obeyed(self):
        self.site.pages["/robots.txt"] = (200, "User-agent: *\nDisallow: /leaderboards/\n")
        reply = self.feed.get()
        self.assertNotIn("/leaderboards/", self.site.requests)
        self.assertEqual(len(reply["snapshot"]["picks"]), 4)
        self.assertEqual([r["name"] for r in reply["snapshot"]["top"]][:1], ["Gin649"], "top 5 from the home page")
        self.assertEqual(sum("disallows /leaderboards/" in w for w in self.log.warnings), 1)

    def test_everything_disallowed_or_robots_failing_means_no_crawl(self):
        self.site.pages["/robots.txt"] = (200, "User-agent: *\nDisallow: /\n")
        reply = self.feed.get()
        self.assertEqual(self.site.requests, ["/robots.txt"])
        self.assertIsNone(reply["snapshot"])
        other = FakeSite()
        other.pages["/robots.txt"] = (503, "")
        self.site = other
        self.clock.now += club.ROBOTS_TTL
        self.make().get(force=True)
        self.assertEqual(other.requests, ["/robots.txt"])
        missing = FakeSite()
        missing.pages.pop("/robots.txt")
        self.site = missing
        self.clock.now += club.ROBOTS_TTL
        self.assertEqual(len(self.make().get(force=True)["snapshot"]["picks"]), 4, "404 robots.txt allows")

    def test_failure_keeps_stale_data_with_backoff(self):
        self.feed.get()
        self.site.down = True
        self.clock.now = NOW + 6 * 3600
        reply = self.feed.get()
        self.assertTrue(reply["refreshed"])
        self.assertTrue(reply["stale"])
        self.assertEqual(reply["error"], "unreachable")
        self.assertEqual(len(reply["snapshot"]["picks"]), 4, "last good data stays")
        self.assertEqual(reply["updated"], NOW, "'updated X ago' still names the last success")
        self.assertEqual(reply["refresh_available_at"], NOW + 6 * 3600 + 600)
        self.site.requests.clear()
        self.clock.now += 600
        self.feed.get(force=True)  # second failure in a row
        self.assertEqual(self.feed.get()["refresh_available_at"], self.clock.now + 1200, "the wait doubles")
        self.site.down = False
        self.clock.now += 1200
        reply = self.feed.get(force=True)
        self.assertFalse(reply["stale"])
        self.assertEqual(reply["error"], "")
        self.assertEqual(sum("unreachable" in w for w in self.log.warnings), 1, "logged once")

    def test_retry_after_is_honoured(self):
        self.site.retry_after = "3600"
        reply = self.feed.get()
        self.assertEqual(reply["error"], "rate_limited")
        self.assertEqual(reply["next_refresh"], NOW + 3600)
        self.site.requests.clear()
        self.clock.now += 1800
        self.feed.get(force=True)
        self.assertEqual(self.site.requests, [], "no request while the server asked us to wait")

    def test_member_standing_follows_the_display_name(self):
        self.feed.get()
        self.site.requests.clear()
        self.feed.save(True, "Example Member")
        reply = self.feed.get()
        self.assertEqual(self.site.requests, ["/members/examplemember/"], "one member page, not a full refresh")
        member = reply["snapshot"]["member"]
        self.assertEqual((member["slug"], member["beaten"], member["rank"], member["streak"]["count"]), ("examplemember", 12, 42, 3))
        self.site.requests.clear()
        self.feed.get()
        self.assertEqual(self.site.requests, [])
        self.feed.save(True, "Nobody Here")
        self.assertIsNone(self.feed.get()["snapshot"]["member"])
        self.feed.save(True, "draven")
        self.site.requests.clear()
        self.feed.get()
        self.assertEqual(self.site.requests, [], "a second name inside 10 minutes waits")
        self.clock.now += 600
        self.feed.get()
        self.assertEqual(self.site.requests, ["/members/dragonmagic28/"])

    def test_structure_change_is_logged_once(self):
        self.feed.get()
        self.assertEqual(self.log.warnings, [])
        self.site.pages["/leaderboards/"] = (200, "<html><body><p>New look</p></body></html>")
        for hours in (6, 12, 18):
            self.clock.now = NOW + hours * 3600
            self.feed.get()
        changed = [w for w in self.log.warnings if "changed shape" in w]
        self.assertEqual(changed, ["Club feed: the club's pages changed shape; not found: leaderboards.rows"])
        self.site.pages["/"] = (200, "<html><body><h2>Something else</h2></body></html>")
        self.clock.now += 6 * 3600
        reply = self.feed.get()
        self.assertEqual(reply["error"], "no_picks")
        self.assertEqual(len(reply["snapshot"]["picks"]), 4, "a page without picks never replaces good data")
        self.assertEqual(sum("home.picks" in w for w in self.log.warnings), 1)

    def test_disabled_club_fetches_nothing(self):
        self.feed.save(False, "")
        reply = self.feed.get(force=True)
        self.assertEqual(reply, {"enabled": False, "display_name": "", "snapshot": None})
        self.assertEqual(self.site.requests, [])

    def test_settings_defaults_and_validation(self):
        self.assertEqual(self.feed.settings(), {"enabled": True, "display_name": ""})
        self.assertEqual(self.feed.save(True, "  Gin649\n "), {"enabled": True, "display_name": "Gin649"})
        self.assertEqual(len(self.feed.save(True, "x" * 200)["display_name"]), 64)
        with self.assertRaises(ValueError): self.feed.save("yes", "x")
        with self.assertRaises(ValueError): self.feed.save(True, None)

    def test_user_agent_without_a_known_version(self):
        self.assertEqual(club.user_agent(""), "Core (+contact: club member)")
        self.assertEqual(club.user_agent("3.3.2"), "Core/3.3.2 (+contact: club member)")

    def test_official_json_provider_can_replace_the_scraper(self):
        snapshot = {"schema": 1, "picks": [{"kind": "week", "title": "Mega Man", "slug": "mega-man-nes", "ends_at": utc(2026, 11, 29)}]}
        self.site.pages["/api/club.json"] = (200, json.dumps(snapshot))
        feed = club.ClubFeed(self.tmp.name, fetch=self.site, clock=self.clock, sleep=self.sleeps.append, version="3.3.2",
                             log=self.log, provider=club.JsonFeedProvider("/api/club.json"))
        reply = feed.get()
        self.assertEqual(reply["snapshot"]["source"], "official-json")
        self.assertEqual(reply["snapshot"]["picks"][0]["title"], "Mega Man")


if __name__ == "__main__":
    unittest.main()
