import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core 3.3.8: Core UI's class-map matchers (docs/eternal/CORE-UI.md, "How class maps are found"). Every
// matcher runs against the key lists read from the live client (tests/fixtures/steam-class-maps.json): Quick Access
// (webpack 34544, no TabContentColumn) and Steam's dialog fields (webpack 2053, no FieldChildren). One missing key must
// never again disable a whole surface; a map that is not found leaves its closest module's key names in the snapshot.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts", "./coreUiDom": "coreUiDom.ts", "./coreFullHeight": "coreFullHeight.ts",
  "./coreArtwork": "coreArtwork.ts", "./coreStarfield": "coreStarfield.ts", "./coreUiSetting": "coreUiSetting.ts", "./coreInputPrompts": "coreInputPrompts.ts" };
const cache = new Map();
function load(file) {
  if (cache.has(file)) return cache.get(file);
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), { compilerOptions }).outputText;
  const require = name => local[name] ? load(local[name]) : {};
  vm.runInNewContext(code, { exports, require, Map, Set, WeakMap, Array, Object, JSON, Math, Number, String, Date, Intl, Promise, console, encodeURIComponent, Symbol });
  cache.set(file, exports);
  return exports;
}

const styles = load("coreUiStyles.ts");
const snap = load("coreUiSnapshot.ts");
const fixture = JSON.parse(readFileSync(new URL("./fixtures/steam-class-maps.json", import.meta.url), "utf8"));

// A real key list as a class map: every key gets a made-up hashed class.
const asMap = (keys, prefix) => Object.fromEntries(keys.map((key, i) => [key, `_${prefix}${i}x`]));
const real = { quickaccess: asMap(fixture.maps.quickaccess.keys, "qa"), gamepaddialog: asMap(fixture.maps.gamepaddialog.keys, "gd") };
// 3.3.9: every surface the coordinator read live (partial key lists), keyed like CoreUiMapName.
const live = Object.fromEntries(Object.entries(fixture.maps).map(([name, entry]) => [name, asMap(entry.keys, name.slice(0, 3))]));

// The other maps, as the earlier tests shape them.
const others = {
  mainmenu: { Menu: "_Mm", Item: "_MmItem", ItemIcon: "_MmIcon", ItemLabel: "_MmLabel", ActiveDot: "_MmDot", FooterBoxShadow: "_MmFbs" },
  controls: { PanelSection: "_CtlPs", PanelSectionTitle: "_CtlPst", PanelSectionRow: "_CtlRow" },
  footer: { BasicFooter: "_FtBasic", QuickAccessFooter: "_FtQam", Opaque: "_FtOpaque" },
  header: { Header: "_HdHeader", HeaderDownloadContainer: "_HdDl", HeaderOpaque: "_HdOpaque", Clock: "_HdClock" },
  dropdown: { DropDownControlButton: "_DdButton" },
  contextmenu: { contextMenuContents: "_CmContents", contextMenuItem: "_CmItem", BasicContextMenuHeader: "_CmHeader", Focused: "_CmFocused" },
  gamepadui: { BasicHome: "_GuHome", MainNavMenuAnchor: "_GuAnchor", OpaqueBackground: "_GuOpaque" },
  tabbedpage: { TabRow: "_TpRow", TabTitle: "_TpTitle", TabHeaderRowWrapper: "_TpWrap", Tab: "_TpTab", Selected: "_TpSel", GamepadTabbedPage: "_TpPage" },
  library: { GamepadLibrary: "_LbLib", CollectionHeader: "_LbHead" },
  appgrid: { AppGridSectionLabel: "_AgLabel", LibraryImageBackgroundGlow: "_AgGlow" },
  portrait: { LibraryItemBox: "_PtBox" },
  recentgames: { RecentGamesContainer: "_RgCont", RecentGamesBackgroundContainer: "_RgBg" },
  carousel: { CarouselCapsuleBackgroundGlow: "_CaGlow" },
  home: { TabbedContent: "_HmTabbed" },
};
const decky3 = list => ({ classMap: list, findClassModule: filter => list.find(map => filter(map)), findModule: () => undefined });
const QAM_RULES = ["qam-panel", "qam-clear", "qam-title", "qam-tabs", "qam-tab-selected", "qam-rail-icons", "qam-tabs-footer", "qam-footer-shadow", "qam-controls"];

test("the real Quick Access and dialog-field modules each match their own matcher and no other", () => {
  for (const [name, map] of Object.entries(real)) {
    const hits = Object.entries(styles.CORE_UI_MAP_FILTERS).filter(([, filter]) => filter(map)).map(([id]) => id);
    assert.deepEqual(hits, [name], `${name} (webpack ${fixture.maps[name].webpack}) matched ${hits.join(", ") || "nothing"}`);
  }
  // Neither key the old matchers hung on is in today's modules.
  assert.ok(!("TabContentColumn" in real.quickaccess) && !("FieldChildren" in real.gamepaddialog));
});

test("3.3.9: every live key list matches its own matcher and no other", () => {
  for (const [name, map] of Object.entries(live)) {
    const hits = Object.entries(styles.CORE_UI_MAP_FILTERS).filter(([, filter]) => filter(map)).map(([id]) => id);
    assert.deepEqual(hits, [name], `${name} matched ${hits.join(", ") || "nothing"}`);
  }
  for (const name of Object.keys(live)) assert.ok(name in styles.CORE_UI_MAP_SIGNATURES, `${name} has a matcher`);
});

test("every matcher still matches its fake map, and none matches another map's", () => {
  const all = { ...others, ...real };
  for (const [name, filter] of Object.entries(styles.CORE_UI_MAP_FILTERS)) {
    for (const [other, map] of Object.entries(all)) assert.equal(filter(map), name === other, `${name} vs ${other}`);
  }
});

test("no matcher hangs on one key: any key with a sibling in its group can go and the map is still found", () => {
  for (const [name, signature] of Object.entries(styles.CORE_UI_MAP_SIGNATURES)) {
    const keys = [...new Set([...signature.all, ...(signature.any ?? []).flat()])];
    const full = asMap(keys, name);
    assert.ok(styles.matchesSignature(full, signature), name);
    for (const group of signature.any ?? []) {
      if (group.length < 2) continue;
      for (const key of group) {
        const { [key]: _gone, ...rest } = full;
        assert.ok(styles.matchesSignature(rest, signature), `${name} without ${key}`);
      }
    }
    assert.ok(signature.all.length <= 3, `${name}: at most three required keys`);
  }
});

test("with today's client every Quick Access rule applies and the rows and rail resolve", () => {
  const ui = decky3([...Object.values(others), real.quickaccess, real.gamepaddialog]);
  const found = styles.findCoreUiMapsDetailed(ui);
  assert.equal(found.routes.quickaccess, "classic");
  assert.equal(found.routes.gamepaddialog, "classic");
  assert.ok(!("quickaccess" in found.candidates) && !("gamepaddialog" in found.candidates));
  const sheet = styles.buildCoreUiCss(found.maps);
  for (const id of QAM_RULES) assert.ok(sheet.applied.includes(id), id);
  for (const id of sheet.skipped) assert.ok(!id.startsWith("qam-"), id);
  const qa = real.quickaccess;
  // qam-clear falls back to the content layers that exist now.
  const clear = sheet.css.split("\n").find(part => part.startsWith("/* qam-clear */"));
  for (const key of ["AllTabContents", "ContentTransition", "TabGroupPanel"]) assert.ok(clear.includes(qa[key]), key);
  // The rail's new list and bottom group go clear; the rows' dialog fields resolve.
  const tabs = sheet.css.split("\n").find(part => part.startsWith("/* qam-tabs */"));
  assert.ok(tabs.includes(qa.TabList) && tabs.includes(qa.BottomTabs));
  assert.ok(styles.targetSelectors(found.maps, "qamRowField").length >= 1);
  assert.ok(sheet.css.includes(real.gamepaddialog.Field));
});

test("a map that is not found leaves its closest module's key names (never its classes) for the snapshot", () => {
  const { TabGroupPanel: _a, ...broken } = real.quickaccess;
  const ui = decky3([...Object.values(others), broken, real.gamepaddialog]);
  const found = styles.findCoreUiMapsDetailed(ui);
  assert.equal(found.routes.quickaccess, "missing");
  const candidate = found.candidates.quickaccess;
  assert.ok(candidate && candidate.score > 0.5 && candidate.score < 1);
  assert.ok(candidate.missing.includes("TabGroupPanel"));
  assert.ok(candidate.keys.includes("QuickAccessMenu") && candidate.keys.includes("BottomTabs"));
  assert.ok(!JSON.stringify(candidate).includes("_qa"), "no hashed class in the candidate");
  const recorded = snap.candidateList(found.candidates);
  assert.equal(recorded.quickaccess.missing.join(), candidate.missing.join());
  const sheet = styles.buildCoreUiCss(found.maps);
  assert.ok(sheet.skipped.includes("qam-panel"));
});

test("Home's tab row: one crimson marker on the open tab, no pill, a clear row, scoped to Core Home's tabbed page", () => {
  const sheet = styles.buildCoreUiCss({ ...others, ...real });
  assert.ok(sheet.applied.includes("home-tabs"));
  const part = sheet.css.split("\n").find(line => line.startsWith("/* home-tabs */"));
  assert.ok(part.includes(":has([data-core-home-tab])"));
  assert.ok(part.includes("._TpPage") && part.includes("._GuHome"));
  assert.ok(part.includes("background-color:transparent !important"));
  assert.ok(part.includes("#D7263D"), "crimson marker");
  assert.ok(part.includes("border-radius:0 !important"), "no 64px pill");
  assert.ok(!/background-color:#000000/.test(part.split("{")[1] ?? ""), "the row is not painted black over Home's stars");
  // The Library keeps its own rule (black row) untouched.
  assert.ok(sheet.applied.includes("library-tabs"));
});

test("the top bar's search: found through the header map and structure, transparent with a steel underline, crimson on focus (3.3.9)", () => {
  const sheet = styles.buildCoreUiCss({ ...others, ...real });
  assert.ok(sheet.applied.includes("search-field"));
  const part = sheet.css.split("\n").find(line => line.startsWith("/* search-field */"));
  assert.ok(part.includes("._HdHeader") && part.includes("div:has(> input)"));
  assert.ok(!part.includes("_2a1-sPpExCp24Q8dfImB0a"), "no hashed class written");
  assert.match(part, /background-color:transparent !important;[^}]*border-radius:0 !important;box-shadow:inset 0 -1px 0 #C9D1D9/);
  assert.doesNotMatch(part, /#1A1D21/, "no gunmetal field");
  assert.match(part, /::placeholder\{color:#9AA4AE !important/);
  assert.match(part, /box-shadow:inset 0 -2px 0 #D7263D !important/);
  assert.doesNotMatch(part, /#1a9fff|blur|drop-shadow/i);
});

test("Steam's dialog field rows (Settings and other pages): the focused row gunmetal with the crimson bar", () => {
  const sheet = styles.buildCoreUiCss({ ...others, ...real });
  assert.ok(sheet.applied.includes("dialog-fields"));
  const part = sheet.css.split("\n").find(line => line.startsWith("/* dialog-fields */"));
  assert.ok(part.includes(real.gamepaddialog.Field) && part.includes(real.gamepaddialog.FieldLabel));
  assert.match(part, /background-color:#1A1D21 !important;background-image:none !important;box-shadow:inset 3px 0 0 #D7263D/);
});

test("3.3.9: with the live key lists every new surface's rule applies, in the Core look", () => {
  const maps = { ...others, ...live, quickaccess: real.quickaccess, gamepaddialog: real.gamepaddialog };
  const sheet = styles.buildCoreUiCss(maps);
  for (const id of ["game-hero", "game-playbar", "game-screen-two", "game-sections", "settings-pages", "toggles", "downloads-page", "download-bar",
    "ingame-menu", "overlay-configurator", "search-pages"]) assert.ok(sheet.applied.includes(id), id);
  // game-cloud waits for the play section's map, not read live yet.
  assert.ok(sheet.skipped.includes("game-cloud"));
  const part = id => sheet.css.split("\n").find(line => line.startsWith(`/* ${id} */`));
  const g = styles.CORE_GAME_VIEW;
  assert.deepEqual([g.playbar, g.footer, g.logo, g.brightness, g.button], [80, 40, 120, 0.6, 48], "Clean Gameview's measures, as The Collector's");
  const hero = part("game-hero");
  assert.ok(hero.includes(`height:calc(100vh - 120px) !important;transition:height ${g.enterMs}ms ease-out`), "screen one, and the fullscreen enter as a transition");
  assert.ok(hero.includes(live.appheader.FullscreenEnterActive) && hero.includes(live.appheader.FullscreenExitDone));
  assert.match(hero, /object-fit:cover !important;object-position:center !important;filter:brightness\(0\.6\)/);
  assert.match(hero, /top:calc\(100vh - 120px \* 1\.3 - 120px\) !important;max-height:120px/, "the logo locked bottom-left, at most 120px");
  assert.match(hero, /@media \(prefers-reduced-motion: reduce\)/);
  const bar = part("game-playbar");
  assert.match(bar, /height:120px !important;min-height:120px !important;padding-bottom:40px/);
  assert.match(bar, /background-color:#D7263D !important;background-image:none !important;color:#FFFFFF/, "crimson Play");
  assert.match(bar, /outline:3px solid #FFFFFF/, "white focus ring on Play");
  assert.match(bar, /min-width:48px !important;height:48px !important;background-color:#000000/, "square black icon buttons");
  assert.match(bar, /outline:2px solid #D7263D/, "crimson focus edge");
  assert.match(part("game-sections"), /background-color:#1A1D21 !important;background-image:none !important;border-radius:var\(--core-radius-float, 10px\)/);
  assert.ok(part("game-screen-two").includes(":has") === false && part("game-screen-two").includes(live.appdetails.AppDetailsContainer));
  assert.match(part("settings-pages"), /inset 3px 0 0 #D7263D/);
  assert.ok(part("settings-pages").includes(live.settings.Active));
  assert.ok(part("toggles").includes(real.gamepaddialog.On) && part("toggles").includes("#D7263D"));
  for (const id of ["downloads-page", "download-bar", "ingame-menu", "overlay-configurator", "search-pages"]) assert.doesNotMatch(part(id), /#1a9fff|gradient|drop-shadow/i, id);
  const host = sheet.hosts.find(item => item.id === "game-page");
  assert.equal(host.below, "." + live.appdetails.PlaySection);
});

test("3.3.9 Home after Art Hero: the art one screen tall with a short fade, the carousel low, the name above it, no header or triangle", () => {
  const maps = { ...others,
    recentgames: { RecentGamesContainer: "_RgCont", RecentGamesBackgroundContainer: "_RgBg", RecentGamesBackgroundImages: "_RgImgs", RecentGamesBackground: "_RgBack",
      RecentGamesBackgroundImage: "_RgImg", RecentGamesHeader: "_RgHead", RecentGamesInnerContainer: "_RgInner" },
    carousel: { CarouselCapsuleBackgroundGlow: "_CaGlow", CarouselGameLabelWrapper: "_CaLabel", ShowAsHovered: "_CaHover", Play: "_CaPlay", ActionIcon: "_CaIcon" },
    home: { TabbedContent: "_HmTabbed", RecentSection: "_HmRecent" } };
  const sheet = styles.buildCoreUiCss(maps);
  const part = sheet.css.split("\n").find(line => line.startsWith("/* home-hero */")).replaceAll(styles.CORE_UI_BOOST, "");
  const a = styles.CORE_HOME_ART;
  assert.ok(part.includes(`mask-image:linear-gradient(180deg, #000000 ${a.fadeFrom}%, transparent 100%)`), "a short fade at the bottom only");
  assert.match(part, /_HmRecent\._HmRecent\{height:100vh !important;padding:0/);
  assert.match(part, /_RgImg\._RgImg\{object-fit:cover !important;object-position:center !important;opacity:1 !important;filter:none/, "full brightness");
  assert.match(part, /_RgHead\._RgHead\{display:none/);
  assert.match(part, /_RgInner\._RgInner\{position:relative !important;top:50vh !important;height:70vh/);
  assert.match(part, /\.gpfocuswithin > \._CaLabel\._CaLabel[^{]*\{position:absolute !important;top:-4em !important;visibility:visible !important;font-size:22px/);
  assert.match(part, /_CaPlay\._CaPlay \._CaIcon\._CaIcon\{display:none/, "no green triangle");
  assert.doesNotMatch(part, /brightness\(0/);
});
