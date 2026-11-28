import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core 3.3.11 (docs/eternal/CORE-UI.md, "3.3.11"): Quick Access's visible width, the Home row's games, Steam's
// game menu on Core's tiles, Core's names for the upstream plugins, the shared game view rules. Fake data only.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const cache = new Map();
const created = [];
const fakeReact = { createElement: (type, props) => { const element = { type, props }; created.push(element); return element; } };
const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts", "./decky": "__decky" };
function load(file) {
  if (file === "__decky") return { SP_REACT: fakeReact, DFL: {} };
  if (cache.has(file)) return cache.get(file);
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), { compilerOptions }).outputText;
  vm.runInNewContext(code, { exports, require: name => local[name] ? load(local[name]) : {}, Map, Set, WeakMap, Array, Object, JSON, Math, Number, String, RegExp, Date, Intl,
    Promise, setTimeout, clearTimeout, console, encodeURIComponent, Symbol, Function, window: {} });
  cache.set(file, exports);
  return exports;
}
const source = file => readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
const rail = load("coreQamRail.ts");
const pages = load("coreHomePages.ts");
const menu = load("coreGameMenu.ts");
const labels = load("coreLabels.ts");
const styles = load("coreUiStyles.ts");

test("Quick Access: the visible width from the main window's placeholder, else the window's own place on screen", () => {
  assert.equal(rail.measureQamVisible({ placeholderLeft: 1000, hostWidth: 1348, full: 855 }), 348, "the TV: about 350 px of 855 on screen");
  assert.equal(rail.measureQamVisible({ placeholderLeft: 400, hostWidth: 1348, full: 855 }), 855, "never wider than the window");
  assert.equal(rail.measureQamVisible({ screenX: 1000, screenWidth: 1348, full: 855 }), 348, "the window's own place");
  assert.equal(rail.measureQamVisible({ placeholderLeft: 1300, hostWidth: 1348, screenX: 1000, screenWidth: 1348, full: 855 }), 348, "a sliver is not trusted");
  assert.equal(rail.measureQamVisible({ screenX: 0, screenWidth: 1, full: 855 }), undefined, "Steam's 1 px screen is not trusted");
  assert.equal(rail.measureQamVisible({ full: 855 }), undefined);
  assert.equal(styles.CORE_QAM_VISIBLE_VAR, "--core-qam-visible");
  const css = source("coreUiStyles.ts");
  assert.match(css, /\[\$\{CORE_QAM_VISIBLE_ATTRIBUTE\}\]\{\$\{imp\(\{ width: `var\(\$\{CORE_QAM_VISIBLE_VAR\}\)`/, "the Menu takes the measured width");
  assert.match(css, /svg:not\(\[fill\]\)"\)\}\{\$\{imp\(\{ fill: "currentColor" \}\)/, "icons without a fill take the tab's colour");
});

test("Home row: games, demos and mods stay; SteamVR, tools, apps and launcher shortcuts go; VR games stay", () => {
  const steam = (appid, app_type, name) => ({ appid, app_type, display_name: name });
  const shortcut = (name) => ({ appid: 3000000000, app_type: pages.HOME_SHORTCUT_TYPE, display_name: name });
  assert.equal(pages.isHomeGame(steam(620980, 1, "Beat Saber")), true, "a VR game is a game");
  assert.equal(pages.isHomeGame(steam(250820, 4, "SteamVR")), false, "a tool");
  assert.equal(pages.isHomeGame(steam(1, 2, "Some App")), false, "an application");
  assert.equal(pages.isHomeGame(steam(2, 8, "Demo")), true);
  assert.equal(pages.isHomeGame(shortcut("Playnite")), false);
  assert.equal(pages.isHomeGame(shortcut("EA app")), false);
  assert.equal(pages.isHomeGame(shortcut("RetroArch")), false);
  assert.equal(pages.isHomeGame(shortcut("Halo: The Master Chief Collection")), true);
  assert.equal(pages.isHomeGame(shortcut("Mirror's Edge")), true, "whole names only");
  assert.equal(pages.isHomeGame(undefined), true, "no overview: kept");
  const overviews = { 1: steam(1, 1, "A"), 2: steam(2, 4, "SteamVR"), 3: shortcut("Steam Link") };
  assert.deepEqual([...pages.homeGames([1, 2, 3, 4], id => overviews[id])], [1, 4]);
  assert.match(source("coreHomeScreen.tsx"), /homeGames\(games, appid => steamWindow\(\)\.appStore\?\.GetAppOverviewByAppID\?\.\(appid\)\)/);
});

test("Steam's game menu: found once by Steam's own markers, shown for the app's overview", () => {
  function SteamMenu(props) { return { navigator: props }; }
  SteamMenu.toString = () => "function(e){return x({navigator: y(), ...e})}";
  const marker = { toString: () => "function(){return t().LibraryContextMenu}" };
  const module = { a: marker, b: () => 1, c: SteamMenu };
  let filters = 0;
  const find = filter => { filters++; return [{ z: 1 }, module].find(mod => Object.values(mod).some(value => filter(value))); };
  assert.equal(menu.steamGameMenu(find, true), SteamMenu);
  assert.equal(menu.steamGameMenu(() => { throw new Error("no"); }), SteamMenu, "looked up once");
  assert.equal(filters > 0, true);
  const element = menu.gameMenuElement(SteamMenu, { appid: 620980 });
  assert.equal(element.type, SteamMenu);
  assert.equal(element.props.overview.appid, 620980);
  assert.equal(menu.gameMenuElement(SteamMenu, undefined), undefined);
  assert.equal(menu.gameMenuElement(undefined, { appid: 1 }), undefined);
  assert.deepEqual(Object.keys(menu.gameMenuProps(0)), []);
  assert.deepEqual(Object.keys(menu.gameMenuProps(10)), ["onMenuButton", "onMenuActionDescription"]);
  assert.equal(menu.steamGameMenu(() => ({ a: () => 1 }), true), undefined, "another Steam build: nothing");
});

// Item 14: every kind of game tile Core draws on Home opens Steam's game menu with Options.
test("every Core game tile on Home opens Steam's game menu: Home row, club picks, Consoles' recently played, Favorites, Achievements", () => {
  const tiles = [
    ["coreHomeScreen.tsx", "Home row and Consoles' recently played (GameTile)", /function GameTile[\s\S]*?\{\.\.\.gameMenuProps\(appid\)\}/],
    ["coreHomeScreen.tsx", "club picks on the Consoles row (PickTile)", /function PickTile[\s\S]*?\{\.\.\.gameMenuProps\(pick\.appId\)\}/],
    ["coreHome.tsx", "Favorites (FavoriteTile)", /className="core-favorite"[^>]*\{\.\.\.gameMenuProps\(app\.appid\)\}/],
    ["CoreAchievementsTab.tsx", "Achievements: recent unlocks", /className="core-ach-row"[\s\S]{0,300}\{\.\.\.gameMenuProps\(unlock\.appId\)\}/],
    ["CoreAchievementsTab.tsx", "Achievements: closest to 100%", /className="core-ach-card"[\s\S]{0,300}\{\.\.\.gameMenuProps\(entry\.appId\)\}/],
  ];
  for (const [file, kind, pattern] of tiles) assert.match(source(file), pattern, kind);
  const screen = source("coreHomeScreen.tsx");
  assert.match(screen, /export function ConsoleScreenTwo[\s\S]*?<GameTile /, "Consoles' recently played are GameTiles");
  assert.match(screen, /className="core-two-row"[\s\S]*?<GameTile /);
});

test("visible names: Playhub Metadata and Artworks read Core in menus and the store; anything else unchanged", () => {
  assert.equal(labels.corePluginLabel("Playhub Metadata"), "Core Metadata");
  assert.equal(labels.corePluginLabel("Playhub Artworks"), "Core Artworks");
  assert.equal(labels.corePluginLabel("ThemeDeck"), "ThemeDeck");
  assert.equal(labels.corePluginLabel("Playhub Metadata settings"), "Playhub Metadata settings", "exact names only");
  const nodes = [{ nodeValue: " Playhub Metadata " }, { nodeValue: "Properties" }, { nodeValue: "Playhub Artworks" }];
  const doc = { createTreeWalker: () => { let i = -1; return { nextNode: () => nodes[++i] ?? null }; } };
  const root = { ownerDocument: doc };
  assert.equal(labels.relabelTree(root), 2);
  assert.deepEqual(nodes.map(node => node.nodeValue), [" Core Metadata ", "Properties", "Core Artworks"]);
  assert.equal(labels.relabelContextMenus({ querySelectorAll: () => [root] }, ".menu"), 0, "already Core");
  assert.match(source("PluginStorePage.tsx"), /ph-store-row-title">\{corePluginLabel\(plugin\.name\)\}/);
  assert.match(source("PluginStorePage.tsx"), /ph-store-detail-title">\{corePluginLabel\(plugin\.name\)\}/);
});

test("the game view: one set of rules for Steam's page and The Collector's (Steam's classes, the Collector's fixed ones)", () => {
  const maps = {
    appdetails: { AppDetailsContainer: "_AdC", PlaySection: "_AdP", ActionRow: "_AdRow", ActionButtonAndStatusPanel: "_AdPanel", AppButtons: "_AdBtns",
      AppDetailsRoot: "_AdRoot", AppDetailsContent: "_AdContent", GameInfoContainer: "_AdInfo" },
    appview: { HeaderLoaded: "_AvL", InnerContainer: "_AvIn", AppDetailsOverviewPanel: "_AvOver", Glassy: "_AvGlass", ScrollContainer: "_AvScroll" },
    actionbutton: { PlayButtonContainer: "_AbCont", PlayButton: "_AbPlay", Green: "_AbGreen" },
    playsection: { CloudStatusLabel: "_PsCloud", StatusAndStats: "_PsStats", GameStatsSection: "_PsGame", PlayBarDetailLabel: "_PsLabel", MenuButton: "_PsMenu" },
    appheader: { TopCapsule: "_AhTop", ImgSrc: "_AhImg", BoxSizer: "_AhBox" },
    tabbedpage: { TabRow: "_TpRow", Tab: "_TpTab", TabTitle: "_TpTitle", GamepadTabbedPage: "_TpPage", TabContents: "_TpContents" },
  };
  const sheet = styles.buildCoreUiCss(maps, {});
  for (const id of ["game-view-clear", "game-info-cards", "game-actions", "game-still-art", "rom-screen-two"]) assert.ok(sheet.applied.includes(id), id);
  const rule = id => sheet.css.split("\n").find(line => line.startsWith(`/* ${id} */`)) ?? "";
  assert.match(rule("game-view-clear"), /_AvGlass[^{]*\{[^}]*background-color:transparent/, "Steam's tinted panel is clear");
  assert.match(rule("game-view-clear"), /_AdC\._AdC:not\(#core-ui-boost\) \._TpPage/, "screen two's tabbed page inside the game page only");
  assert.match(rule("game-view-clear"), /_AvIn\._AvIn:not\(#core-ui-boost\):not\(:has\(\.romm-game-view\)\)\{background-color:#000000/, "Steam's page black, The Collector's left to it");
  assert.match(rule("game-actions"), /_AbPlay[^{]*_AbGreen[^{]*\{background-color:#D7263D[^}]*color:#FFFFFF[^}]*border-radius:0/, "Play crimson, white, square, the green state too");
  assert.match(rule("game-actions"), /\.romm-game-view :is\(\.romm-native-play,\.romm-btn-play\)/, "The Collector's Play / Install");
  assert.match(rule("game-actions"), /_AdRow\._AdRow:not\(#core-ui-boost\)\{display:flex[^}]*justify-content:flex-start/, "icon buttons follow the stats");
  assert.match(rule("game-actions"), /_PsMenu[^{]*\{width:48px[^}]*background-color:#000000[^}]*border-radius:0/, "square black icon buttons");
  assert.match(rule("game-still-art"), /_AhTop[^{]* video\{display:none/, "still art only");
  assert.match(rule("rom-screen-two"), /\.romm-tab\.romm-tab\.romm-tab-active\{color:#FFFFFF !important;box-shadow:inset 0 -3px 0 #D7263D/);
  assert.match(rule("rom-screen-two"), /\.romm-tab\.romm-tab::before,[^{]*::after\{content:none/, "no focus box");
  assert.ok(!sheet.hosts.some(host => host.id === "context"), "the context menu is a gunmetal panel, never a Starfield host");
});

test("Steam menu: Friends & Chat is left out by its route or key, never its label; the item list closes up", () => {
  const entries = load("coreMenuEntries.ts");
  const item = (key, route, label = "Anything") => ({ key, props: { route, label, onFocus() {} } });
  assert.equal(entries.isFriendsMenuItem(item("friends", "/friends")), true);
  assert.equal(entries.isFriendsMenuItem(item("x", "/chat")), true);
  assert.equal(entries.isFriendsMenuItem(item("chat", "")), true, "by key");
  assert.equal(entries.isFriendsMenuItem(item("library", "/library", "Freunde & Chat")), false, "the label is never read");
  assert.equal(entries.isFriendsMenuItem(item("store", "/steamweb")), false);
  assert.equal(entries.isFriendsMenuItem(item("necro-menu:x", "/friends")), false, "a plugin's own entry stays");
  assert.deepEqual([...entries.coreSteamMenuEntries({ plugins: [] }, true).map(entry => entry.id)], ["core-hide-friends"]);
  assert.deepEqual([...entries.coreSteamMenuEntries({ plugins: [] }, false)], [], "Core UI off: Steam's menu as it is");
  const registry = source("StoreDigitalMenuRegistry.ts");
  assert.match(registry, /const clean = items\.filter\(\(item: any\) => !ownedItem\(item\) && !hidden\(item\)\);/, "left out before React renders: no gap in focus");
  assert.match(registry, /const entries = all\.filter\(entry => typeof entry\.hide !== "function"\)/, "a hiding entry adds no item");
  assert.match(source("StoreDigitalPortal.tsx"), /coreSteamMenuEntries\(state,coreUi\)/);
});

test("the Steam menu and Quick Access fill the screen: their placeholders lose the top bar's and the footer's inset", () => {
  const sheet = styles.buildCoreUiCss({ quickaccess: { Menu: "_QM", Tab: "_QT", ViewPlaceholder: "_QVP", Tabs: "_QTs" }, mainmenu: { Menu: "_MM", Item: "_MI", ViewPlaceholder: "_MVP" } }, {});
  const rule = sheet.css.split("\n").find(line => line.startsWith("/* panels-full-height */")) ?? "";
  assert.match(rule, /_QVP\._QVP:not\(#core-ui-boost\),html\[data-core-ui\] \._MVP\._MVP:not\(#core-ui-boost\)\{top:0 !important;bottom:0 !important/);
  assert.match(rule, /_QM\._QM:not\(#core-ui-boost\)\[data-core-qam-full\]\{padding-top:40px/, "a top safe area only once the window is full height");
});
