import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: the Club tab (Play Retro Games Club on Home). Days-left maths, fuzzy title matching against a
// Steam library with The Collector's per-platform collections, the Jarvis brief line, availability, the 12
// languages, and the Core palette rules for the tab's styles. The feed itself is tests/test_club_feed.py.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
function load(file, stubs = {}) {
  const exports = {};
  const source = readFileSync(new URL(`../src/club/${file}`, import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions }).outputText;
  const require = name => { if (name in stubs) return stubs[name]; throw new Error(`unexpected import ${name} from ${file}`); };
  vm.runInNewContext(code, { exports, require, Intl, Map, Set, Array, Object, JSON, Math, Number, String, Date, RegExp, Promise, console });
  return exports;
}

const time = load("clubTime.ts");
const match = load("clubMatch.ts");
const brief = load("clubBrief.ts");
const locale = load("clubLocale.ts");

const NOW = Date.UTC(2026, 10, 27, 8, 0); // Nov 27, 2026, 08:00 UTC: the day the site was read.
const s = ms => ms / 1000;

test("days left: whole days as the club leads with them, then the last day, then ended", () => {
  assert.deepEqual({ ...time.timeLeft(s(Date.UTC(2026, 10, 29)), NOW) }, { state: "days", days: 1 }); // 1 day 16 h
  assert.deepEqual({ ...time.timeLeft(s(Date.UTC(2026, 11, 1)), NOW) }, { state: "days", days: 3 }); // 3 days 16 h
  assert.deepEqual({ ...time.timeLeft(s(Date.UTC(2027, 0, 1)), NOW) }, { state: "days", days: 34 });
  assert.deepEqual({ ...time.timeLeft(s(NOW + 5 * 3600_000 + 1), NOW) }, { state: "last", hours: 6 });
  assert.deepEqual({ ...time.timeLeft(s(NOW + 86_400_000), NOW) }, { state: "days", days: 1 });
  assert.equal(time.timeLeft(s(NOW), NOW).state, "ended");
  assert.equal(time.timeLeft(null, NOW).state, "unknown");
  assert.equal(time.timeLeft(Number.NaN, NOW).state, "unknown");
});

test("days left reads naturally in every language, with Slavic plural forms", () => {
  assert.equal(locale.daysLeftText("en", 1), "1 day left");
  assert.equal(locale.daysLeftText("en-GB", 3), "3 days left");
  assert.equal(locale.daysLeftText("it", 2), "Mancano 2 giorni");
  assert.equal(locale.daysLeftText("ru", 1), "Остался 1 день");
  assert.equal(locale.daysLeftText("ru", 3), "Осталось 3 дня");
  assert.equal(locale.daysLeftText("ru", 5), "Осталось 5 дней");
  assert.equal(locale.daysLeftText("uk", 21), "Залишився 21 день");
  assert.equal(locale.daysLeftText("ja", 4), "残り4日");
  assert.equal(locale.daysLeftText("xx", 2), "2 days left");
});

test("updated X ago picks a sensible unit", () => {
  assert.deepEqual({ ...time.updatedAgo(s(NOW - 5 * 60_000), NOW) }, { value: -5, unit: "minute" });
  assert.deepEqual({ ...time.updatedAgo(s(NOW - 3 * 3600_000), NOW) }, { value: -3, unit: "hour" });
  assert.deepEqual({ ...time.updatedAgo(s(NOW - 3 * 86_400_000), NOW) }, { value: -3, unit: "day" });
  assert.equal(time.updatedAgo(null, NOW), null);
  assert.equal(time.formatAgo("en", { value: -3, unit: "hour" }), "3 hours ago");
});

test("title normalisation drops punctuation, The, regions and ROM tags, and folds roman numerals", () => {
  assert.equal(match.normalizeTitle("Super Metroid: GBA Edition"), "super metroid gba edition");
  assert.equal(match.normalizeTitle("Super Metroid - GBA Edition (USA) [!]"), "super metroid gba edition");
  assert.equal(match.normalizeTitle("Legend of Zelda, The - A Link to the Past (Europe) (Rev 1)"), "legend of zelda a link to the past");
  assert.equal(match.normalizeTitle("The Legend of Zelda: A Link to the Past"), "legend of zelda a link to the past");
  assert.equal(match.normalizeTitle("Pokémon Emerald (U) (V1.1)"), "pokemon emerald");
  assert.equal(match.normalizeTitle("Final Fantasy VI"), "final fantasy 6");
  assert.equal(match.normalizeTitle("Banjo & Kazooie"), "banjo and kazooie");
  assert.equal(match.normalizeTitle("Donkey Kong Country.sfc"), "donkey kong country");
});

test("similar titles match; sequels and different games do not", () => {
  assert.equal(match.titleSimilarity("Donkey Kong Country", "Donkey Kong Country (USA) (Rev 2)"), 1);
  assert.equal(match.titleSimilarity("Final Fantasy VI", "Final Fantasy 6"), 1);
  assert.ok(match.titleSimilarity("Mega Man", "MegaMan") > 0.9);
  assert.ok(match.titleSimilarity("Castlevania: Aria of Sorrow", "Castlevania - Aria of Sorow") > 0.9);
  assert.equal(match.titleSimilarity("Mega Man", "Mega Man 2"), 0);
  assert.equal(match.titleSimilarity("Mega Man X", "Mega Man X3"), 0);
  assert.equal(match.titleSimilarity("Paper Mario", "Super Mario 64"), 0);
  assert.equal(match.titleSimilarity("Galaga", "Gauge"), 0);
});

test("collections name the platform The Collector filed a shortcut under", () => {
  assert.equal(match.platformOf("SNES"), "SNES");
  assert.equal(match.platformOf("RomM: Super Nintendo"), "SNES");
  assert.equal(match.platformOf("Nintendo Entertainment System"), "NES");
  assert.equal(match.platformOf("Game Boy Advance"), "GBA");
  assert.equal(match.platformOf("Nintendo 64"), "N64");
  assert.equal(match.platformOf("PlayStation 2"), "PS2");
  assert.equal(match.platformOf("Favourites"), "");
  assert.equal(match.platformKey("GBA romhack"), "GBA");
});

test("a pick matches the shortcut on its platform first, never a shortcut filed under another platform", () => {
  const library = [
    { appId: 10, name: "Mega Man Legacy Collection", shortcut: false, platforms: [] },
    { appId: 2147483650, name: "Mega Man (USA)", shortcut: true, platforms: ["NES"] },
    { appId: 2147483651, name: "Mega Man", shortcut: true, platforms: ["GB"] },
    { appId: 2147483652, name: "Mega Man 2 (USA)", shortcut: true, platforms: ["NES"] },
    { appId: 2147483653, name: "Paper Mario", shortcut: true, platforms: [] },
    { appId: 20, name: "Paper Mario", shortcut: false, platforms: [] },
    { appId: 30, name: "Donkey Kong Country", shortcut: false, platforms: [] },
    { appId: 2147483654, name: "Super Metroid - GBA Edition", shortcut: true, platforms: ["SNES"] },
  ];
  assert.equal(match.matchPick({ title: "Mega Man", platform: "NES" }, library).appId, 2147483650);
  assert.equal(match.matchPick({ title: "Paper Mario", platform: "N64" }, library).appId, 2147483653);
  assert.equal(match.matchPick({ title: "Donkey Kong Country", platform: "SNES" }, library).appId, 30);
  // The romhack is a GBA pick; a shortcut filed under SNES is a different game.
  assert.equal(match.matchPick({ title: "Super Metroid: GBA Edition", platform: "GBA" }, library), null);
  assert.equal(match.matchPick({ title: "Kirby's Pinball Land", platform: "GB" }, library), null);
});

test("Jarvis's brief line: the nearest unbeaten pick ending within three days, or null", () => {
  const picks = [
    { kind: "week", slug: "mega-man-nes", ends_at: s(Date.UTC(2026, 10, 29)) },
    { kind: "month", slug: "donkey-kong-country-snes", ends_at: s(Date.UTC(2026, 11, 1)) },
    { kind: "season", slug: "paper-mario-n64", ends_at: s(Date.UTC(2027, 0, 1)) },
  ];
  assert.equal(brief.briefLineFor({ picks }, NOW), "One day left on this week's pick, sir.");
  assert.equal(brief.briefLineFor({ picks }, NOW - 86_400_000), "Two days left on this week's pick, sir.");
  assert.equal(brief.briefLineFor({ picks }, Date.UTC(2026, 10, 28, 12)), "Last day for this week's pick, sir.");
  assert.equal(brief.briefLineFor({ picks, member: { beaten_slugs: ["mega-man-nes"] } }, NOW), "Three days left on this month's pick, sir.");
  assert.equal(brief.briefLineFor({ picks: [picks[2]] }, NOW), null);
  assert.equal(brief.briefLineFor({ picks: [{ kind: "special", label: "Christmas Special", ends_at: s(NOW + 2 * 86_400_000 + 1) }] }, NOW), "Two days left on the Christmas Special, sir.");
  assert.equal(brief.briefLineFor(null, NOW), null);
});

test("availability: off when the setting is off, and until there is club data", () => {
  let state = { settings: null, feed: null, loading: false, failed: false };
  let loads = 0;
  const store = { clubState: () => state, ensureClubLoaded: () => { loads++; return null; } };
  const available = load("clubAvailable.ts", { "./clubStore": store });
  assert.equal(available.clubAvailable(), false);
  assert.equal(loads, 1);
  const snapshot = { picks: [{ kind: "week", title: "Mega Man" }] };
  state = { ...state, settings: { enabled: true, display_name: "" }, feed: { enabled: true, snapshot: { picks: [] } } };
  assert.equal(available.clubAvailable(), false);
  state = { ...state, feed: { enabled: true, snapshot, stale: true } };
  assert.equal(available.clubAvailable(), true, "stale cached data still counts");
  state = { ...state, settings: { enabled: false, display_name: "" } };
  assert.equal(available.clubAvailable(), false);
  state = { ...state, settings: { enabled: true, display_name: "" }, feed: { enabled: false, snapshot: null } };
  assert.equal(available.clubAvailable(), false);
});

test("every language has every string, and the setting is on by default in the backend", () => {
  const en = locale.clubCopy("en");
  assert.equal(locale.clubLanguages.length, 12);
  for (const language of locale.clubLanguages) {
    const copy = locale.clubCopy(language);
    for (const key of Object.keys(en)) assert.ok(copy[key], `${language}.${key}`);
    assert.ok(copy.updated.includes("{ago}"), `${language}.updated keeps {ago}`);
    assert.ok(copy.rankOf.includes("{rank}") && copy.rankOf.includes("{of}"), `${language}.rankOf`);
    assert.ok(copy.notFound.includes("{name}"), `${language}.notFound`);
  }
  assert.equal(locale.clubCopy("pt-BR").play, "Jogar");
  assert.equal(locale.clubCopy("ua").tab, "Клуб");
  const backend = readFileSync(new URL("../quick_settings/club_feed.py", import.meta.url), "utf8");
  assert.match(backend, /return \{"enabled": True, "display_name": ""\}/);
});

test("the tab follows the Core palette: crimson square focus, 10 px cards, no site art until allowed", () => {
  const source = readFileSync(new URL("../src/club/ClubTab.tsx", import.meta.url), "utf8");
  assert.match(source, /\.ph-club-focus\.gpfocus,\.ph-club-focus:focus-visible\{box-shadow:0 0 0 3px \$\{CORE\.crimson\};border-radius:0\}/);
  assert.match(source, /\.ph-club-card\{background:\$\{CORE\.gunmetal\};border-radius:10px/);
  assert.match(source, /<CoreStars \/>/);
  assert.match(source, /export const CLUB_SITE_ART_ALLOWED = false;/);
  assert.doesNotMatch(source, /#[0-9a-fA-F]{6}/, "colours come from corePalette");
  const assemble = readFileSync(new URL("../src/club/clubAssemble.ts", import.meta.url), "utf8");
  assert.match(assemble, /TODO\(club-assemble\)/);
  assert.match(assemble, /sent: false, reason: "not_implemented"/);
});
