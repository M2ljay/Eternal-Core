import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core 3.3.10: Home as approved (docs/eternal/CORE-UI.md, "Home in 3.3.10"): two pages (Home, Consoles) with
// L1/R1, screen one's row, the Achievements tab's data, Quick Access's rail on the right. Fake data only; the user's
// ColorGrade pack is never used in tests.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const cache = new Map();
const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts" };
function load(file) {
  if (cache.has(file)) return cache.get(file);
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), { compilerOptions }).outputText;
  vm.runInNewContext(code, { exports, require: name => local[name] ? load(local[name]) : {}, Map, Set, WeakMap, Array, Object, JSON, Math, Number, String, RegExp, Date, Intl,
    Promise, setTimeout, clearTimeout, console, encodeURIComponent, Symbol });
  cache.set(file, exports);
  return exports;
}
const source = file => readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
const plain = value => JSON.parse(JSON.stringify(value));
const pages = load("coreHomePages.ts");
const ach = load("coreAchievements.ts");
const rail = load("coreQamRail.ts");
const styles = load("coreUiStyles.ts");

test("pages: L1 and R1 switch Home and Consoles without wrapping; nothing else does; the store tells its listeners", () => {
  assert.equal(pages.pageForButton("home", 6), "consoles");
  assert.equal(pages.pageForButton("consoles", 5), "home");
  assert.equal(pages.pageForButton("home", 5), undefined, "no wrap");
  assert.equal(pages.pageForButton("consoles", 6), undefined);
  for (const other of [1, 2, 9, 10, 11, 12, undefined]) assert.equal(pages.pageForButton("home", other), undefined);
  let heard = 0;
  const stop = pages.subscribeHomePage(() => heard++);
  pages.setHomePage("consoles"); pages.setHomePage("consoles"); pages.setHomePage("nowhere");
  assert.equal(pages.homePage(), "consoles"); assert.equal(heard, 1, "only a real change is told");
  pages.setHomePage("home"); stop();
});

test("3.3.11: screen one's row at the TV (1348 x 758): 27% tall, above screen two's tab names, portrait games, landscape consoles", () => {
  const tv = plain(pages.rowSizes(1348, 758));
  assert.deepEqual(tv.gameFocused, { width: 137, height: 205 });
  assert.deepEqual(tv.game, { width: 119, height: 178 });
  assert.deepEqual(tv.consoleFocused, { width: 263, height: 148 });
  assert.deepEqual(tv.console, { width: 222, height: 125 });
  assert.equal(tv.bottom, 112, "above the 42 px footer and the 58 px tab names");
  assert.equal(pages.HOME_SCREEN_CUT_PX, 100);
  const board = plain(pages.rowSizes(1920, 1080));
  assert.deepEqual(board.gameFocused, { width: 195, height: 292 }); assert.deepEqual(board.game, { width: 169, height: 254 });
  assert.ok(tv.gameFocused.height + tv.bottom + pages.HOME_ROW.liftPx < 758, "the lifted tile stays on screen");
});

test("Steam's recent games are read from the recents section's output or props; sentinel 0 and duplicates dropped", () => {
  const carousel = { props: { games: [0, 10, 20, 10, "x", -1], onItemFocus() {}, showFeaturedItem: true } };
  const tree = { props: { children: [{ props: { children: "Recent" } }, { props: { children: carousel } }] } };
  assert.deepEqual(plain(pages.recentGamesIn(tree)), [10, 20]);
  assert.deepEqual(plain(pages.recentGamesIn({ props: { native: carousel } })), [10, 20], "inside another plugin's wrapper");
  assert.deepEqual(plain(pages.recentGamesIn(undefined, { games: [7, 7, 8] })), [7, 8]);
  assert.equal(pages.recentGamesIn({ props: { children: [] } }), undefined, "no list: Steam's own row stays");
});

test("additions: art after a 150 ms rest with a 250 ms crossfade, two neighbours each side preloaded, B and up go back to screen one", () => {
  assert.equal(pages.HERO_DELAY_MS, 150); assert.equal(pages.HERO_FADE_MS, 250);
  assert.deepEqual(plain(pages.neighbours(0, 10)), [1, 2]);
  assert.deepEqual(plain(pages.neighbours(5, 10)), [6, 4, 7, 3]);
  assert.deepEqual(plain(pages.neighbours(9, 10)), [8, 7]);
  assert.equal(pages.backToScreenOne(2, { inTwo: true, inTopRow: false }), true, "B anywhere on screen two");
  assert.equal(pages.backToScreenOne(9, { inTwo: true, inTopRow: true }), true, "up from the top row");
  assert.equal(pages.backToScreenOne(9, { inTwo: true, inTopRow: false }), false, "up further down stays Steam's");
  assert.equal(pages.backToScreenOne(2, { inTwo: false, inTopRow: false }), false, "B on screen one stays Steam's");
  const runtime = source("coreUi.ts");
  assert.match(runtime, /doc\.addEventListener\("vgp_onbuttondown", back, true\)/);
  assert.match(runtime, /if \(nowInOne\) \{ inOne = true; smoothScroll\(scroller, 0\); return; \}/, "the same scroll as going down");
});

test("the club's owned Game of the Week and Game of the Month lead the Consoles row, at most two, nothing when neither is owned", () => {
  const picks = [{ kind: "month", label: "Game of the Month", title: "DKC" }, { kind: "week", label: "Game of the Week", title: "Mega Man" },
    { kind: "romhack", label: "Romhack", title: "Hack" }, { kind: "season", label: "Season", title: "Paper Mario" }];
  const eventOf = pick => ({ week: "gotw", month: "gotm", romhack: "rhotm", season: "gots" })[pick.kind];
  const owned = { "Mega Man": 11, DKC: 12, Hack: 13, "Paper Mario": 14 };
  const out = plain(pages.consolePicks(picks, eventOf, pick => owned[pick.title] ? { appId: owned[pick.title] } : null));
  assert.deepEqual(out.map(pick => [pick.event, pick.appId, pick.tone]), [["gotw", 11, "#8F7CB0"], ["gotm", 12, "#B07A84"]]);
  assert.deepEqual(plain(pages.consolePicks(picks, eventOf, () => null)), [], "none owned: nothing");
  assert.deepEqual(plain(pages.consolePicks(picks, eventOf, pick => pick.title === "DKC" ? { appId: 12 } : null)).map(pick => pick.event), ["gotm"]);
  assert.deepEqual(plain(pages.consolePicks(picks, eventOf, () => ({ appId: 11 }), pick => pick.kind === "week")).map(pick => pick.event), ["gotm"], "an ended pick is left out");
  assert.deepEqual(plain(pages.consolePicks(undefined, eventOf, () => ({ appId: 1 }))), []);
  const screen = source("coreHomeScreen.tsx");
  assert.match(screen, /clubState, loadClub, subscribeClub/, "the Club's own data (refreshed hourly), no extra fetch");
  assert.match(screen, /tag: picks\.find\(pick => card\.games\.some\(game => game\.appid === pick\.appId\)\)/, "the console holding the game carries the tag");
});

test("screen one shows no name, count or status line; consoles without art fall back to logo, icon, name; never an empty box", () => {
  const screen = source("coreHomeScreen.tsx");
  assert.doesNotMatch(screen, />\{appName\(|\.count\b|lastPlayed\}|playtime/i, "no name, count or status drawn beside the art");
  assert.match(screen, /mark \? <img className="core-tile-logo"[\s\S]{0,120}?\/> : <span className="core-tile-name">\{card\.name\}<\/span>/, "the name only as the last fallback");
  assert.match(screen, /collectionIcon\(card\.name, matchCollection\(card\.name\), homeIconSet\(\)\)/, "the console's icon");
  assert.match(screen, /\{!top\?\.sources\.length \? <CoreStars \/> : null\}/, "no art: black with the Starfield");
  assert.match(screen, /\.core-tile\.core-plain\{background:#1A1D21\}/);
  assert.match(screen, /\.core-tile\.gpfocus,[^{]*\{opacity:1;height:\$\{R\.gameFocusedVh\}vh;translate:0 -\$\{R\.liftPx\}px;outline:none/, "larger and lifted, no outline");
  assert.match(screen, /<span className="core-key">L1<\/span>[\s\S]*<span className="core-key">R1<\/span>/, "L1 HOME CONSOLES R1");
});

test("one route patch draws both Core parts of Home (the tabs and screen one), told apart by their props", () => {
  const news = source("HomeNews.tsx");
  assert.equal((news.match(/createOutputPatch\(\[/g) ?? []).length, 2, "the news shelf and one Home patch");
  assert.match(news, /\(node: any\) => isTabs\(node\?\.props\) \|\| isRecents\(node\?\.props\)/);
  assert.match(news, /<CoreHomeScreen native=\{tree\} games=\{recentGamesIn\(tree, sourceProps\)\}/);
  const home = source("coreHome.tsx");
  assert.match(home, /if \(page === "consoles"\) return <HomeBoundary fallback=\{fallback\}><ConsoleScreenTwo/, "Consoles: its games in place of the tabs");
  assert.match(home, /id: ACHIEVEMENTS_ID, title: text\.achievements, content: <CoreAchievementsTab/);
  assert.doesNotMatch(home, /CONSOLES_ID, title/, "Consoles is no longer a tab");
});

test("Favorites: Steam's and The Collector's favourites in one grid, each game once", () => {
  const tabs = load("coreHomeTabs.ts");
  const app = (appid, last) => ({ appid, display_name: `G${appid}`, rt_last_time_played: last });
  const steam = { id: "favorite", displayName: "Favorites", allApps: [app(1, 5), app(2, 1)] };
  const collections = [steam, { id: "a", displayName: "[Favourites]", allApps: [app(2, 1), app(3, 9)] }, { id: "b", displayName: "RomM: [Favourites] (romm.local)", allApps: [app(4, 2)] },
    { id: "c", displayName: "Favourites", allApps: [app(5, 0)] }, { id: "d", displayName: "My favorites", allApps: [app(6, 0)] }, { id: "e", displayName: "Favorites", allApps: [app(7, 0)] }];
  const extra = tabs.collectorFavorites(collections);
  assert.deepEqual(plain(extra.map(item => item.id)), ["a", "b", "c"], "The Collector's names only; never Steam's own or a user's");
  assert.deepEqual(plain(tabs.favoriteApps(steam, 200, extra).map(item => item.appid)), [3, 1, 4, 2, 5]);
});

test("achievements: Steam's unlocks and progress, The Collector's bridge read defensively, merged and labelled", async () => {
  const answer = JSON.stringify({ result: 1, data: { achieved: [{ strName: "Elden Lord", strImage: "https://cdn.example/a.jpg", rtUnlocked: 1700000000, flAchieved: 6.1 },
    { strName: "Old", rtUnlocked: 1600000000, flAchieved: 50, strImage: "javascript:alert(1)" }, { strName: "", rtUnlocked: 1 }], unachieved: [{ strName: "Never", flAchieved: 1 }] } });
  const unlocks = plain(ach.steamUnlocks(answer, { appid: 10, name: "Elden Ring" }));
  assert.equal(unlocks.length, 2);
  assert.equal(unlocks[0].icon, "https://cdn.example/a.jpg"); assert.equal(unlocks[1].icon, undefined, "only https images");
  const closest = plain(ach.steamClosest(new Map([[1, { appid: 1, unlocked: 47, total: 50 }], [2, { appid: 2, unlocked: 50, total: 50 }], [3, { appid: 3, unlocked: 0, total: 9 }]]), id => `Game ${id}`));
  assert.deepEqual(closest.map(entry => entry.appId), [1], "started and not finished");

  assert.equal(ach.collectorBridge({}), undefined, "no bridge: Steam alone");
  const throwing = { getRecentUnlocks() { throw new Error("no"); }, getClosestToComplete: async () => { throw new Error("no"); } };
  assert.deepEqual(plain(await ach.fromBridge(throwing, "getRecentUnlocks", 5, ach.raUnlock)), []);
  assert.deepEqual(plain(await ach.fromBridge(throwing, "getClosestToComplete", 4, ach.raNearlyDone)), []);
  assert.deepEqual(plain(await ach.fromBridge({ getRecentUnlocks: () => "not json" }, "getRecentUnlocks", 5, ach.raUnlock)), []);
  const bridge = { getRecentUnlocks: async () => [{ title: "Speed Demon", gameTitle: "Super Metroid", unlockedAt: "2026-11-26T20:00:00Z", hardcore: true, badgeUrl: "https://ra.example/b.png" },
    { title: "", gameTitle: "x", unlockedAt: 1 }],
    getClosestToComplete: () => ({ items: [{ title: "Super Metroid", earned: 44, total: 50 }, { title: "Done", earned: 5, total: 5 }] }) };
  const ra = plain(await ach.fromBridge(bridge, "getRecentUnlocks", 5, ach.raUnlock));
  assert.equal(ra.length, 1); assert.equal(ra[0].source, "ra"); assert.equal(ra[0].hardcore, true);
  const raClose = plain(await ach.fromBridge(bridge, "getClosestToComplete", 4, ach.raNearlyDone));
  assert.deepEqual(raClose.map(entry => entry.game), ["Super Metroid"]);
  const merged = plain(ach.mergeUnlocks([unlocks, ra]));
  assert.deepEqual(merged.map(unlock => unlock.name), ["Speed Demon", "Elden Lord", "Old"], "newest first");
  assert.deepEqual(plain(ach.mergeClosest([closest, raClose])).map(entry => entry.game), ["Game 1", "Super Metroid"], "nearest to 100% first");
  const words = { hardcore: "Hardcore", players: "{n}% of players" };
  assert.equal(ach.unlockNote(ra[0], words), "Hardcore");
  assert.equal(ach.unlockNote({ ...unlocks[0], rarity: 6.1 }, words), "6.1% of players");
  assert.equal(ach.unlockNote({ ...unlocks[0], rarity: 50 }, words), "");
  // The exact bridge (Collector win.36): { ok, items } answers, failures quiet, limits clamped, version 1.
  const asked = [];
  const exact = { version: 1,
    getRecentUnlocks: async limit => { asked.push(limit); return { ok: true, items: [{ romId: 3, appId: null, game: "Mega Man", platform: "NES", title: "Perfect Run",
      description: "d", badgeUrl: null, unlockedAt: "2026-11-24T10:00:00Z", hardcore: true }] }; },
    getClosestToComplete: async () => ({ ok: true, items: [{ romId: 4, appId: 2147483900, game: "Super Metroid", platform: "SNES", earned: 44, total: 50, percent: 88 }] }) };
  const exactUnlocks = plain(await ach.fromBridge(exact, "getRecentUnlocks", 500, ach.raUnlock));
  assert.deepEqual(asked, [50], "limits clamped to 50");
  assert.equal(exactUnlocks[0].name, "Perfect Run"); assert.equal(exactUnlocks[0].game, "Mega Man"); assert.equal(exactUnlocks[0].appId, undefined, "appId null: not actionable");
  assert.equal(exactUnlocks[0].at, Date.parse("2026-11-24T10:00:00Z") / 1000);
  assert.equal(plain(await ach.fromBridge(exact, "getClosestToComplete", 4, ach.raNearlyDone))[0].appId, 2147483900);
  for (const reason of ["not_configured", "auth_failed", "rate_limited", "server_unreachable", "backend_error", "bridge_error", "bad_request"]) {
    assert.deepEqual(plain(await ach.fromBridge({ version: 1, getRecentUnlocks: async () => ({ ok: false, reason }) }, "getRecentUnlocks", 5, ach.raUnlock)), [], reason);
  }
  assert.equal(ach.collectorBridge({ __eternalCollectorAchievements: { version: 2 } }), undefined, "an unknown version is not read");
  const tab = source("CoreAchievementsTab.tsx");
  assert.match(tab, /window\.addEventListener\(COLLECTOR_ACHIEVEMENTS_READY, ready\)/, "a bridge that registers later is read");
  assert.match(tab, /\.core-ach-tag\.ra\{background:#C98B4A;color:#000000\}/, "RetroAchievements amber with black text");
  assert.match(tab, /\.core-ach-bar i\{display:block;height:6px;background:#D7263D\}/, "crimson progress");
  assert.deepEqual([...source("coreAchievements.ts").matchAll(/export const (\w+_KEY) = "([^"]+)"/g)].map(m => m[2]), ["core.home.achievementRarity.v1", "core.home.achievements.v1"],
    "the only storage: the rarity cache and the last model");
});

test("achievements: rarity cached per game for 24 hours; the tab draws from cache first and never waits to draw", () => {
  const store = new Map();
  const storage = { getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) };
  let cache = ach.rarityCache(ach.readJson(storage, ach.RARITY_KEY));
  assert.equal(ach.rarityStale(cache, 10, 1000), true, "nothing cached yet");
  const rates = plain(ach.ratesFrom({ data: { achieved: [{ strName: "A", flAchieved: 6.1 }], unachieved: [{ strName: "B", flAchieved: 90 }], hidden: [{ strName: "C", flAchieved: 101 }] } }));
  assert.deepEqual(rates, { A: 6.1, B: 90 });
  cache = ach.storeRates(cache, 10, rates, 1000);
  ach.writeJson(storage, ach.RARITY_KEY, cache);
  cache = ach.rarityCache(ach.readJson(storage, ach.RARITY_KEY));
  assert.equal(ach.rarityStale(cache, 10, 1000 + 3600), false);
  assert.equal(ach.rarityStale(cache, 10, 1000 + 24 * 3600), true, "read again after a day");
  const unlock = { source: "steam", name: "A", game: "G", appId: 10, at: 5 };
  assert.equal(ach.withRarity(unlock, cache).rarity, 6.1, "filled in from the cache");
  assert.equal(ach.withRarity({ ...unlock, appId: 11 }, cache).rarity, undefined, "not arrived yet: no rarity");
  for (let i = 0; i < ach.RARITY_GAMES + 10; i++) cache = ach.storeRates(cache, 100 + i, { X: 1 }, 2000 + i);
  assert.equal(Object.keys(cache.games).length, ach.RARITY_GAMES, "bounded");
  const model = ach.storedModel({ recent: [{ source: "ra", name: "N", game: "G", at: 9, icon: "data:x" }], closest: [{ source: "steam", game: "H", unlocked: 3, total: 4 }] });
  assert.equal(plain(model).recent[0].icon, undefined, "re-validated on read");
  assert.equal(ach.storedModel({ recent: "x" }), undefined);
  const tab = source("CoreAchievementsTab.tsx");
  assert.match(tab, /React\.useState<[^>]*>\(\(\) => storedModel\(readJson\(storage\(\), MODEL_KEY\)\) \?\? null\)/, "the first render reads the stored model");
  assert.match(tab, /if \(answer !== undefined && rarityStale\(rarity, game\.appid, /, "a game's rates are read again only when stale");
});

test("empty states: one line of hint text, in every language", () => {
  const home = load("coreHomeLocale.ts");
  const club = load("club/clubLocale.ts");
  assert.equal(home.coreHomeCopy("en").favoritesEmpty, "Star a game to add it here");
  assert.equal(home.coreHomeCopy("en").achievementsEmpty, "Unlock an achievement and it shows up here");
  assert.equal(club.clubCopy("en").unreachable, "The club couldn't be reached. It'll try again within the hour.");
  for (const language of home.coreHomeLanguages) {
    const copy = home.coreHomeCopy(language);
    for (const key of ["home", "consoles", "achievements", "recentUnlocks", "closest", "recentlyPlayed", "hardcore", "players", "left", "achievementsEmpty", "favoritesEmpty"]) {
      assert.ok(copy[key], `${language}.${key}`);
      assert.doesNotMatch(copy[key], /\bsir\b/i);
    }
    assert.ok(copy.players.includes("{n}") && copy.left.includes("{n}"), language);
    if (language !== "en") assert.notEqual(copy.favoritesEmpty, "Star a game to add it here", `${language} is translated`);
  }
  for (const language of club.clubLanguages) {
    const copy = club.clubCopy(language);
    for (const key of ["unreachable", "inLibrary", "openInLibrary", "comingUp", "beatenCount", "topLine"]) assert.ok(copy[key], `${language}.${key}`);
  }
  assert.match(source("coreHome.tsx"), /\.core-home \.core-home-empty\{font-size:15px;color:#9AA4AE;[^}]*text-align:left/);
});

test("Quick Access's rail on the right: the row reversed, the D-pad true to the picture, sliders keep left and right", () => {
  assert.equal(rail.railMove(11, { inRail: true, rightOfFocus: false, takesSides: false }), "into-content", "left from the rail: the content");
  assert.equal(rail.railMove(12, { inRail: true, rightOfFocus: false, takesSides: false }), "stay", "right from the rail: nothing there");
  assert.equal(rail.railMove(12, { inRail: false, rightOfFocus: false, takesSides: false }), "into-rail", "right from the content's edge: the rail");
  assert.equal(rail.railMove(12, { inRail: false, rightOfFocus: true, takesSides: false }), "steam", "an item to the right: Steam moves");
  assert.equal(rail.railMove(12, { inRail: false, rightOfFocus: false, takesSides: true }), "steam", "a slider keeps right");
  assert.equal(rail.railMove(11, { inRail: false, rightOfFocus: false, takesSides: false }), "steam");
  assert.equal(rail.railMove(9, { inRail: true, rightOfFocus: false, takesSides: false }), "steam", "up and down are Steam's");
  const maps = { quickaccess: { Menu: "_QaMenu", Tabs: "_QaTabs", Tab: "_QaTab", Selected: "_QaSel" } };
  const sheet = styles.buildCoreUiCss(maps);
  assert.match(sheet.css, /\/\* qam-rail-right \*\/html\[data-core-ui\] \._QaMenu\._QaMenu:not\(#core-ui-boost\)\{display:flex !important;flex-direction:row-reverse !important\}/);
  assert.match(sheet.css, /\._QaTabs\._QaTabs:not\(#core-ui-boost\)\{[^}]*box-shadow:inset 1px 0 0 #1A1D21/, "the divider on the rail's left edge");
  assert.equal(sheet.dom.qamMenu, "._QaMenu"); assert.equal(sheet.dom.qamRail, "._QaTabs"); assert.equal(sheet.dom.qamTabSelected, "._QaTab._QaSel");

  // The listener on a fake Quick Access: rail on the right, two rows of content on the left.
  const node = (classes, rect, children = []) => {
    const element = { classes, rect, children, parentElement: null, focused: 0, attributes: {}, isConnected: true,
      getBoundingClientRect: () => ({ ...rect, right: rect.left + rect.width, bottom: rect.top + rect.height }),
      focus() { this.focused++; doc.activeElement = this; },
      contains(other) { for (let n = other; n; n = n.parentElement) if (n === this) return true; return false; },
      closest(selector) { for (let n = this; n; n = n.parentElement) if (n.matches(selector)) return n; return null; },
      matches(selector) { return selector.split(",").some(part => part.trim().split(".").filter(Boolean).every(name => n(name))); function n(name) { return classes.includes(name); } },
      querySelectorAll(selector) { const out = []; const walk = e => { for (const c of e.children) { if (c.matches(selector)) out.push(c); walk(c); } }; walk(this); return out; },
      querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; } };
    for (const child of children) child.parentElement = element;
    return element;
  };
  const tabA = node(["_QaTab", "Focusable"], { left: 806, top: 180, width: 48, height: 64 });
  const tabB = node(["_QaTab", "_QaSel", "Focusable"], { left: 806, top: 244, width: 48, height: 64 });
  const railEl = node(["_QaTabs"], { left: 806, top: 0, width: 48, height: 679 }, [tabA, tabB]);
  const row1 = node(["Focusable"], { left: 0, top: 200, width: 790, height: 48 });
  const row2 = node(["Focusable"], { left: 0, top: 250, width: 790, height: 48 });
  const content = node(["_Content"], { left: 0, top: 0, width: 806, height: 679 }, [row1, row2]);
  const menu = node(["_QaMenu"], { left: 0, top: 0, width: 854, height: 679 }, [content, railEl]);
  const listeners = [];
  const doc = { activeElement: tabB, addEventListener: (type, fn, capture) => listeners.push({ type, fn, capture }), removeEventListener: (type, fn) => { const at = listeners.findIndex(l => l.fn === fn); if (at >= 0) listeners.splice(at, 1); } };
  const stop = rail.installQamRail(doc, () => ({ menu: "._QaMenu", rail: "._QaTabs", tab: "._QaTab", selected: "._QaTab._QaSel" }));
  assert.equal(listeners[0].type, "vgp_onbuttondown"); assert.equal(listeners[0].capture, true);
  const press = button => { const event = { detail: { button }, prevented: 0, preventDefault() { this.prevented++; }, stopPropagation() {} }; listeners[0].fn(event); return event; };
  assert.equal(press(11).prevented, 1); assert.equal(doc.activeElement, row2, "left from the open tab: the content row level with it");
  doc.activeElement = row1;
  assert.equal(press(12).prevented, 1); assert.equal(doc.activeElement, tabB, "right from the content: the open tab");
  assert.equal(press(11).prevented, 1); assert.equal(doc.activeElement, row1, "back left: where focus last was in the content");
  assert.equal(press(12).prevented, 1); assert.equal(press(12).prevented, 1, "right from the rail: nothing");
  assert.equal(press(9).prevented, 0, "up is Steam's");
  stop(); assert.equal(listeners.length, 0);
});

test("Home's own rules: the clock back right and no search on Home; nothing left for The Collector's old shelf", () => {
  const sheet = styles.buildCoreUiCss({ header: { Header: "_HdHeader", HeaderOpaque: "_HdOpaque" } });
  const part = sheet.css.split("\n").find(line => line.startsWith("/* home-pages */"));
  assert.match(part, /html\[data-core-ui\] body:has\(\[data-core-home-one\]\) \._HdHeader\._HdHeader:not\(#core-ui-boost\) \[data-playhub-clock-left\]\{order:0 !important\}/);
  assert.doesNotMatch(sheet.css, /data-collector-shelf/, "The Collector no longer patches Home (win.36)");
  assert.match(part, /div:has\(> input\)\{display:none !important\}/);
});
