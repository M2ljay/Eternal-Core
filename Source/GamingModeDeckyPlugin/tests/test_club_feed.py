"""The Club feed (quick_settings/club_feed.py): parsing Play Retro Games Club's public pages, robots.txt,
the 6-hour / 10-minute fetch limits, backoff, stale fallback, the per-section structure flags (each true only
when that section was parsed), keeping a section's last good data when its markup changes, and the
once-per-change structure warning.

Fixtures (tests/fixtures/club/, see its README): home.html, nominations.html and leaderboards.html are the
club's real pages captured on 2026-11-27, trimmed (fewer rows, no scripts or styles) with the markup, classes
and attributes verbatim. member.html is fictional. Only public display names.
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
            "/leaderboards/": (200, leaderboard_with_members()),
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


FRAME = "<html><body><main>{}</main></body></html>"
# A synthetic leaderboard row in the club's markup, added to the real page for the member tests.
MEMBER_ROWS = ('<tr data-name="Example Member examplemember" data-s-total="12" data-s-hours="40" data-s-name="example member">'
               '<td class="rank">42</td><td class="grow-cell lb-who"><a class="pname" href="/members/examplemember/">Example Member</a></td></tr>'
               '<tr data-name="jaso.90 jaso-90" data-s-total="25" data-s-name="jaso.90">'
               '<td class="rank">9</td><td class="grow-cell lb-who"><a class="pname" href="/members/jaso-90/">jaso.90</a></td></tr>')


def leaderboard_with_members():
    page = fixture("leaderboards.html")
    return page.replace("</tbody>", MEMBER_ROWS + "</tbody>", 1)


class HomeParserTests(unittest.TestCase):
    def setUp(self):
        self.home = club.parse_home(fixture("home.html"), NOW)

    def test_four_room_cards_by_event_class(self):
        picks = [(p["kind"], p["event"], p["label"], p["title"], p["slug"], p["platform"], p["romhack"]) for p in self.home["picks"]]
        self.assertEqual(picks, [
            ("week", "gotw", "Game of the Week", "Mega Man", "mega-man-nes", "NES", False),
            ("month", "gotm", "Game of the Month", "Donkey Kong Country", "donkey-kong-country-snes", "SNES", False),
            ("romhack", "rhotm", "Romhack of the Month", "Super Metroid: GBA Edition", "super-metroid-gba-edition-gba", "GBA", True),
            ("season", "gots", "Game of the Season", "Paper Mario", "paper-mario-n64", "N64", False),
        ])

    def test_card_facts_hours_firsts_cover_and_end(self):
        week = self.home["picks"][0]
        self.assertEqual((week["beaten"], week["at100"], week["rating"], week["hours"]), (35, 6, 5.1, 2.8))
        self.assertEqual(week["first_clear"], {"name": "chiefly1818", "at": utc(2026, 11, 22, 1, 50, 7)})
        self.assertEqual(week["first_100"], {"name": "xsandraax", "at": utc(2026, 11, 22, 4, 41, 17)})
        self.assertEqual(week["ends_at"], utc(2026, 11, 29))
        self.assertEqual(week["url"], "https://www.playretrogamesclub.com/games/mega-man-nes/")
        self.assertEqual(week["image"], "https://www.playretrogamesclub.com/img/1448.png")
        self.assertEqual(week["cover"], "/img/1448.png")
        self.assertEqual([p["ends_at"] for p in self.home["picks"]][1:], [utc(2026, 12, 1), utc(2026, 12, 1), utc(2027, 1, 1)])
        self.assertEqual(self.home["picks"][3]["hours"], 23.1)
        self.assertEqual(self.home["picks"][2]["first_100"]["name"], "Draven")

    def test_coming_up_strip(self):
        schedule = self.home["schedule"]
        self.assertEqual([(item["at"], item["title"]) for item in schedule], [
            (utc(2026, 11, 28), "Votes open"), (utc(2026, 11, 29), "Game of the Week revealed"), (utc(2026, 12, 1), "December picks start")])
        self.assertEqual(schedule[0]["text"], "Votes open: December's Game of the Month, December's Romhack of the Month, Christmas Special")
        self.assertEqual([tag["kind"] for tag in schedule[0]["tags"]], ["month", "romhack", "special"])
        self.assertEqual(schedule[0]["until"], utc(2026, 11, 30, 23, 59))
        self.assertEqual(schedule[1]["text"], "Game of the Week revealed", "a tag already in the title is not repeated")
        self.assertEqual(schedule[1]["until"], utc(2026, 12, 6))
        self.assertIsNone(schedule[2]["until"])

    def test_nomination_counts_top_players_and_totals(self):
        self.assertEqual(self.home["nomination_counts"], [
            {"kind": "month", "label": "December Game of the Month", "count": 34},
            {"kind": "romhack", "label": "December Romhack of the Month", "count": 15},
            {"kind": "season", "label": "January to March 2027 Game of the Season", "count": 14}])
        self.assertEqual(self.home["top"], [
            {"rank": 1, "name": "Gin649", "slug": "gin649", "beaten": 35},
            {"rank": 2, "name": "Draven", "slug": "dragonmagic28", "beaten": 34},
            {"rank": 3, "name": "popogego99", "slug": "popogego99", "beaten": 33}])
        self.assertEqual(self.home["stats"], {"players": 179, "games_beaten": 1186, "hours": 7519, "picks": 84})
        self.assertEqual(self.home["structure"], {"playing_now": True, "top_players": True, "schedule": True,
                                                  "nominations": True, "stats": True})

    def test_each_flag_is_false_when_only_its_section_is_missing(self):
        page = fixture("home.html")
        without_strip = page.replace('class="cal-strip"', 'class="calendar"')
        home = club.parse_home(without_strip, NOW)
        self.assertEqual(home["schedule"], [])
        self.assertFalse(home["structure"]["schedule"])
        self.assertTrue(home["structure"]["playing_now"] and home["structure"]["top_players"])
        renamed = club.parse_home(page.replace("<h2>Top players</h2>", "<h2>Leaders</h2>"), NOW)
        self.assertEqual(renamed["top"], [])
        self.assertFalse(renamed["structure"]["top_players"])
        self.assertTrue(renamed["structure"]["nominations"])

    def test_a_special_card_and_an_unknown_system(self):
        card = ('<a class="room x" href="/games/a-christmas-hack-snes/"><div class="frame"><img class="cover" src="/img/77.png" alt="A box art"></div>'
                '<div><span class="tag">Christmas Special</span><h3>A Christmas Hack</h3><div class="plat">PICO-8 · about 1 hour</div>'
                '<div class="ends">Ends <time datetime="2026-12-31T00:00:00Z">Dec 31</time></div></div></a>')
        pick = club.parse_home(FRAME.format(card), NOW)["picks"][0]
        self.assertEqual((pick["kind"], pick["event"], pick["label"], pick["platform"], pick["hours"], pick["ends_at"]),
                         ("special", "special", "Christmas Special", "PICO-8", 1.0, utc(2026, 12, 31)))
        self.assertIsNone(pick["beaten"])
        self.assertIsNone(pick["first_clear"])


class NominationParserTests(unittest.TestCase):
    def setUp(self):
        self.groups = club.parse_nominations(fixture("nominations.html"), NOW)

    def test_one_group_per_run_section(self):
        self.assertEqual([(g["kind"], g["event"], g["label"], g["period"], g["opens_at"], g["count"]) for g in self.groups], [
            ("month", "gotm", "Game of the Month · December 2026", "December 2026", utc(2026, 11, 28), 34),
            ("romhack", "rhotm", "Romhack of the Month · December 2026", "December 2026", utc(2026, 11, 28), 15)])

    def test_rows_from_data_attributes_and_links(self):
        first = self.groups[0]["items"][0]
        self.assertEqual(first, {"title": "Spyro: Year of the Dragon", "platform": "PS1", "romhack": False,
                                 "nominator": "joeysretrohandhelds", "nominator_slug": "joeysretrohandhelds", "hours": 8.2,
                                 "order": 1, "ra_game_id": 11267, "cover": "/img/11267.png"})
        katamari = self.groups[0]["items"][1]
        self.assertEqual((katamari["nominator"], katamari["nominator_slug"]), ("Gain", "gavin4010"))
        self.assertEqual(self.groups[0]["items"][2]["title"], "Sin & Punishment: Star Successor", "entities decoded")
        self.assertEqual(self.groups[0]["items"][2]["platform"], "Wii")
        self.assertEqual([i["order"] for i in self.groups[0]["items"]], [1, 2, 4])
        hack, blank = self.groups[1]["items"]
        self.assertTrue(hack["romhack"])
        self.assertEqual((hack["platform"], hack["ra_game_id"]), ("N64", 19490))
        self.assertIsNone(blank["hours"], "an empty data-s-hours is no time")
        self.assertEqual(blank["title"], "Pokémon Recordkeepers")

    def test_only_retroachievements_game_links_count(self):
        row = ('<section class="run m" id="gotm"><span class="tag">Game of the Month</span><h2>December 2026</h2>'
               '<table id="nomt-gotm"><tbody><tr data-sys="NES" data-s-order="1" data-s-hours="1" data-s-name="x">'
               '<td><a href="https://evil.example/game/5">X</a></td></tr>'
               '<tr data-sys="NES" data-s-order="2" data-s-name="y"><td><a href="http://retroachievements.org/game/6">Y</a></td></tr></tbody></table></section>')
        items = club.parse_nominations(FRAME.format(row), NOW)[0]["items"]
        self.assertEqual([(i["title"], i["ra_game_id"]) for i in items], [("x", None), ("y", None)],
                         "the title falls back to data-s-name; no id from an off-site or plain-http link")


class LeaderboardParserTests(unittest.TestCase):
    def test_rows_and_their_data_stats(self):
        rows = club.parse_leaderboard(fixture("leaderboards.html"))
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0], {"rank": 1, "name": "Gin649", "slug": "gin649", "beaten": 35, "hardcore": 0, "completed": 2,
                                   "mastered": 0, "firsts": 13, "firsthundred": 0, "hours": 172,
                                   "by_event": {"week": 22, "month": 5, "romhack": 5, "season": 2, "special": 1}})
        self.assertEqual((rows[1]["name"], rows[1]["slug"], rows[1]["completed"], rows[1]["firsthundred"], rows[1]["hours"]),
                         ("Draven", "dragonmagic28", 32, 24, 602))


class MemberAndSafetyTests(unittest.TestCase):
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
                         "<<<>>> <script>Game of the Week</script>", "</div></div><a class='room w'>", "<td><tr><li>" * 50,
                         FRAME.format('<a class="room w" href="/games/mega-man-nes/"><span class="tag">Game of the Week</span></a>')):
            home = club.parse_home(document, NOW)
            self.assertEqual(home["picks"], [])
            self.assertFalse(any(home["structure"].values()))
            self.assertEqual(club.parse_nominations(document, NOW), [])
            self.assertEqual(club.parse_leaderboard(document), [])

    def test_off_site_links_and_odd_slugs_are_ignored(self):
        cards = ('<a class="room w" href="https://evil.example/games/x/"><h3>X</h3></a>'
                 '<a class="room m" href="/games/../../etc/"><h3>Y</h3></a>'
                 '<a class="room r" href="/games/ok-snes/"><h3>Z</h3><img class="cover" src="https://evil.example/img/1.png"></a>')
        picks = club.parse_home(FRAME.format(cards), NOW)["picks"]
        self.assertEqual([p["title"] for p in picks], ["Z"])
        self.assertEqual((picks[0]["image"], picks[0]["cover"]), ("", ""), "no off-site image")

    def test_scripts_and_comments_are_not_content(self):
        page = fixture("home.html").replace("<h3>Mega Man</h3>", "<h3><!-- x -->Mega Man<script>var a = '<h3>No</h3>';</script></h3>")
        self.assertEqual(club.parse_home(page, NOW)["picks"][0]["title"], "Mega Man")

    def test_loads_without_modules_decky_frozen_python_lacks(self):
        from unittest.mock import patch
        import builtins
        import importlib.util
        original = builtins.__import__

        def restricted(name, *args, **kwargs):
            if name.startswith(("xml", "pyexpat", "html.parser", "_markupbase", "sqlite3", "ssl_extra")): raise ModuleNotFoundError(name)
            return original(name, *args, **kwargs)
        with patch("builtins.__import__", side_effect=restricted):
            spec = importlib.util.spec_from_file_location("club_frozen_fixture", Path(__file__).resolve().parents[1] / "quick_settings/club_feed.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            self.assertEqual(len(module.parse_home(fixture("home.html"), NOW)["picks"]), 4)
            self.assertEqual(len(module.parse_nominations(fixture("nominations.html"), NOW)), 2)

    def test_member_slug_from_display_name_or_username(self):
        rows = club.parse_leaderboard(leaderboard_with_members())
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

    def test_at_most_hourly_and_on_demand_at_most_once_per_10_minutes(self):
        self.feed.get()
        self.site.requests.clear()
        self.clock.now += 50 * 60
        self.feed.get()
        self.assertEqual(self.site.requests, [], "inside the hour: cache")
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

    def state(self):
        return json.loads((Path(self.tmp.name) / "club-feed.json").read_text(encoding="utf-8"))

    def test_every_structure_flag_is_true_on_the_real_pages(self):
        reply = self.feed.get()
        self.assertEqual(self.state()["structure"], {
            "home.playing_now": True, "home.top_players": True, "home.schedule": True, "home.nominations": True,
            "home.stats": True, "home.picks": True, "nominations.categories": True, "leaderboards.rows": True})
        snapshot = reply["snapshot"]
        self.assertEqual(snapshot["carried"], [])
        self.assertEqual([g["event"] for g in snapshot["nominations"]], ["gotm", "rhotm"])
        self.assertEqual(snapshot["nominations"][0]["items"][0]["ra_game_id"], 11267)
        self.assertEqual(snapshot["top"][0]["by_event"]["week"], 22)
        self.assertEqual(snapshot["leaderboard"][1], {"rank": 2, "name": "Draven", "slug": "dragonmagic28", "beaten": 34, "hours": 602})
        self.assertEqual(snapshot["picks"][0]["event"], "gotw")
        self.assertEqual(self.log.warnings, [])

    def test_a_changed_page_keeps_that_sections_last_good_data(self):
        first = self.feed.get()["snapshot"]
        self.site.pages["/leaderboards/"] = (200, "<html><body><table class='board'><tr><td>Gin649</td><td>35</td></tr></table></body></html>")
        self.site.pages["/nominations/"] = (200, "<html><body><section class='round'><h2>December 2026</h2></section></body></html>")
        self.site.pages["/"] = (200, fixture("home.html").replace('class="noms"', 'class="noms-v2"').replace('class="cal-strip"', 'class="cal"'))
        self.clock.now = NOW + 6 * 3600
        reply = self.feed.get()
        self.assertTrue(reply["refreshed"])
        self.assertEqual(reply["error"], "")
        snapshot = reply["snapshot"]
        structure = self.state()["structure"]
        self.assertFalse(structure["leaderboards.rows"] or structure["nominations.categories"] or structure["home.schedule"]
                         or structure["home.nominations"])
        self.assertTrue(structure["home.picks"] and structure["home.top_players"])
        self.assertEqual(sorted(snapshot["carried"]), ["leaderboard", "nominations", "schedule"])
        self.assertEqual(snapshot["leaderboard"], first["leaderboard"])
        self.assertEqual(snapshot["nominations"], first["nominations"])
        self.assertEqual(snapshot["schedule"], first["schedule"])
        self.assertEqual(snapshot["top"][0]["name"], "Gin649", "top from the home page while the leaderboard is unreadable")
        self.assertEqual(snapshot["fetched_at"], NOW + 6 * 3600)
        changed = [w for w in self.log.warnings if "changed shape" in w]
        self.assertEqual(changed, ["Club feed: the club's pages changed shape; not found: home.nominations, home.schedule, "
                                   "leaderboards.rows, nominations.categories"])

    def test_home_counts_stand_in_for_an_unreadable_nominations_page(self):
        self.site.pages["/nominations/"] = (200, "<html><body><p>Soon</p></body></html>")
        snapshot = self.feed.get()["snapshot"]
        self.assertEqual([(g["event"], g["count"], g["items"]) for g in snapshot["nominations"]],
                         [("gotm", 34, []), ("rhotm", 15, []), ("gots", 14, [])])
        self.assertFalse(self.state()["structure"]["nominations.categories"], "the flag stays honest")

    def test_a_parser_crash_keeps_the_cache(self):
        from unittest.mock import patch
        self.feed.get()
        self.clock.now = NOW + 6 * 3600
        with patch.object(club, "parse_nominations", side_effect=RuntimeError("boom")):
            reply = self.feed.get()
        self.assertEqual(reply["error"], "parse_error")
        self.assertTrue(reply["stale"])
        self.assertEqual(len(reply["snapshot"]["picks"]), 4)
        self.assertEqual(reply["updated"], NOW)

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
