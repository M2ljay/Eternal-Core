import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: Core UI phase 1 (docs/eternal/CORE-UI.md). Selector building from fake Steam class maps on
// Decky 3 and Decky 4 shaped libraries, the Starfield (shared field, readable text, the screensaver's seed),
// the Digital stores -> Awesome Mix menu swap, and the "Core UI" setting (on removes nothing, off removes all).

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const cache = new Map();
function load(file, stubs = {}, globals = {}) {
  const key = file + JSON.stringify(Object.keys(stubs));
  if (cache.has(key) && !Object.keys(globals).length) return cache.get(key);
  const exports = {};
  const source = readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
  const code = ts.transpileModule(source, { compilerOptions }).outputText;
  const require = name => {
    if (name in stubs) return stubs[name];
    const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts", "./coreStarfield": "coreStarfield.ts",
      "./coreUiSetting": "coreUiSetting.ts", "./coreMenuEntries": "coreMenuEntries.ts", "./coreUiDom": "coreUiDom.ts",
      "./coreFullHeight": "coreFullHeight.ts", "./coreArtwork": "coreArtwork.ts", "./coreQamRail": "coreQamRail.ts", "./coreHomePages": "coreHomePages.ts" }[name];
    if (local) return load(local);
    return {};
  };
  vm.runInNewContext(code, { exports, require, Proxy, Reflect, Map, Set, WeakMap, Array, Object, JSON, Math, Number, String, console, encodeURIComponent, ...globals });
  if (!Object.keys(globals).length) cache.set(key, exports);
  return exports;
}

const styles = load("coreUiStyles.ts");
const starfield = load("coreStarfield.ts");
const entries = load("coreMenuEntries.ts");
const settingModule = load("coreUiSetting.ts");
const locale = load("coreUiLocale.ts");

// Steam's class maps as the readable key -> hashed class Steam ships (shapes from css_translations.json).
const maps = {
  mainmenu: { MainMenuEmbedded: "_MmEmbedQ", Menu: "_23IDiIFxX", Item: "_2w9TpItem", ItemOuter: "_9OuterX", ItemIcon: "_3IconQ", ItemLabel: "_1LabelZ", ActiveDot: "_1uLVHdot",
    FooterBoxShadow: "_FbsMain", Open: "_OpenM", RunningAppIcon: "_RunIcon" },
  quickaccess: { Menu: "_QamMenu", Container: "_QamCont", QuickAccessMenu: "_QamQam", Title: "_QamTitle", HeaderContainer: "_QamHead", Tab: "_QamTab",
    Tabs: "_QamTabs", TabsWithFooter: "_QamTwf", Selected: "_QamSel", TabGroupPanel: "_QamTgp", TabContentColumn: "_QamTcc", AllTabContents: "_QamAtc",
    ContentTransition: "_QamCt", PanelSection: "_QamPs", FooterBoxShadow: "_QamFbs", BatteryDetailsLabels: "_QamBat" },
  controls: { PanelSection: "_CtlPs", PanelSectionTitle: "_CtlPst", PanelSectionRow: "_CtlRow" },
  footer: { BasicFooter: "_FtBasic", QuickAccessFooter: "_FtQam", Opaque: "_FtOpaque", FooterLegend: "_FtLegend" },
  header: { Header: "_HdHeader", HeaderDownloadContainer: "_HdDl", HeaderOpaque: "_HdOpaque", Clock: "_HdClock" },
  dropdown: { DropDownControlButton: "_DdButton" },
  contextmenu: { contextMenu: "_CmMenu", contextMenuContents: "_CmContents", contextMenuItem: "_CmItem", BasicContextMenuHeader: "_CmHeader",
    MenuSectionHeader: "_CmSection", ContextMenuSeparator: "_CmSep", Focused: "_CmFocused", Destructive: "_CmDanger", SubMenu: "_CmSub", active: "_CmActive", Positive: "_CmPos" },
  // Phase 2 (Home, the Library).
  gamepadui: { BasicHome: "_GuHome", MainNavMenuAnchor: "_GuAnchor", OpaqueBackground: "_GuOpaque" },
  tabbedpage: { TabRow: "_TpRow", TabTitle: "_TpTitle", TabHeaderRowWrapper: "_TpWrap", Tab: "_TpTab", Selected: "_TpSel", GamepadTabbedPage: "_TpPage", TabCount: "_TpCount" },
  library: { GamepadLibrary: "_LbLib", CollectionHeader: "_LbHead" },
  appgrid: { AppGridSectionLabel: "_AgLabel", LibraryImageBackgroundGlow: "_AgGlow", Rule: "_AgRule" },
  portrait: { LibraryItemBox: "_PtBox" },
  recentgames: { RecentGamesContainer: "_RgCont", RecentGamesBackgroundContainer: "_RgBg", RecentGamesBackgroundImages: "_RgImgs", RecentGamesHeaderLabel: "_RgLabel" },
  carousel: { CarouselCapsuleBackgroundGlow: "_CaGlow" },
  home: { TabbedContent: "_HmTabbed" },
  // 3.3.8: the dialog fields as the November 2026 client ships them (no FieldChildren).
  gamepaddialog: { Field: "_GdField", FieldLabel: "_GdLabel", FieldDescription: "_GdDesc", FieldChildrenInner: "_GdKids" },
  // 3.3.9: Steam's game page root and play bar.
  appdetails: { AppDetailsContainer: "_AdCont", PlaySection: "_AdPlay", AppDetailsRoot: "_AdRoot", AppActionButton: "_AdPlayBtn" },
};
// Lookalikes: a dialog map with a dropdown key, a desktop context menu (no BasicContextMenuHeader).
const unrelated = [{ GamepadDialogContent: "_gdc", DropDownControlButton: "_wrongDd" }, { Item: "_otherItem", Menu: "_otherMenu" },
  { contextMenuContents: "_deskContents", contextMenuItem: "_deskItem", Focused: "_deskFocused" }];

function decky3(classMaps) {
  const list = [...unrelated, ...classMaps];
  return { classMap: list, findClassModule: filter => list.find(map => filter(map)), findModule: () => undefined };
}
function decky4(classMaps) {
  // ES-module client: no class-map list; maps are variables of module scopes.
  const scopes = [{ a: unrelated[0], b: 1 }, ...classMaps.map((map, i) => ({ [`v${i}`]: map, other: () => 0 }))];
  const vars = () => scopes.flatMap(scope => Object.entries(scope));
  return {
    classMap: [],
    findVar: filter => vars().find(([k, v]) => v && filter(v, k))?.[1],
    findModuleScopeByVar: filter => scopes.find(scope => Object.entries(scope).some(([k, v]) => v && filter(v, k))),
  };
}
const decky4ScopesOnly = classMaps => {
  const scopes = classMaps.map((map, i) => ({ [`v${i}`]: map }));
  return { findModuleScopeByVar: filter => scopes.find(scope => Object.values(scope).some(v => filter(v))) };
};

// Splits a selector list on commas outside :is(...) and attribute brackets.
function splitTop(list) {
  const out = []; let depth = 0, current = "";
  for (const ch of list) {
    if (ch === "(" || ch === "[") depth++;
    if (ch === ")" || ch === "]") depth--;
    if (ch === "," && depth === 0) { out.push(current); current = ""; } else current += ch;
  }
  out.push(current);
  return out;
}

function checkSheet(sheet) {
  // 3.3.4: every class target carries the id-level boost; the checks below read the selectors without it.
  const css = sheet.css.replaceAll(styles.CORE_UI_BOOST, "");
  for (const value of Object.values(maps).flatMap(map => Object.values(map))) {
    if (["_OpenM", "_RunIcon", "_QamBat", "_CtlPst", "_CtlRow", "_FtLegend", "_HdDl", "_HdClock", "_CmPos", "_QamTab", "_GuAnchor", "_GuOpaque", "_GdDesc", "_GdKids"].includes(value) && !css.includes("." + value)) continue;
    assert.ok(css.includes("." + value), `class ${value} used`);
  }
  assert.ok(!/mainmenu_|quickaccessmenu_|_23IDiI-F_FxXotrgpY71r8/.test(css), "no readable or hard-coded hashed Steam class names");
  assert.ok(!css.includes("_wrongDd") && !css.includes("_otherItem") && !css.includes("_desk"), "lookalike maps are not used");
  // The orb's pulse keyframes and its reduced-motion override are the only at-rules.
  const flat = css.replace(/@keyframes core-jarvis-pulse\{0%,100%\{opacity:0\.4\}50%\{opacity:1\}\}/, "").replace(/@media \(prefers-reduced-motion: reduce\)\{([^{}]*\{[^}]*\})\}/g, "$1");
  assert.doesNotMatch(flat, /@/);
  for (const rule of flat.split("}").filter(part => part.includes("{"))) {
    const [selectors, body] = rule.split("{");
    if (selectors.trim().startsWith("@")) continue;
    for (const selector of splitTop(selectors.replace(/\/\*[^*]*\*\//g, ""))) assert.match(selector.trim(), /^html\[data-core-ui\] /, `scoped: ${selector}`);
    for (const declaration of body.split(";").filter(Boolean)) assert.match(declaration, /!important$/, `important: ${declaration}`);
  }
  // Brand: no gold, no Steam blue, no gradients or haze, no glow (blurred shadows).
  // The hero's fade into black is a mask (no colour painted), the one gradient allowed.
  assert.doesNotMatch(css.replace(/(-webkit-)?mask-image:linear-gradient\(180deg, #000000 \d+%, transparent 100%\) !important/g, "").replace(/text-shadow:0 1px 3px rgba\(0,0,0,0\.65\) !important/g, ""), /gradient|#1a9fff|#FFD700|#D4AF37|gold|drop-shadow|text-shadow:(?!none)/i);
  for (const [, value] of css.matchAll(/box-shadow:([^;}]*)/g)) assert.match(value, /^(none|inset [^,]*(, inset [^,]*)*) !important$/, `no glow: ${value}`);
  for (const [, value] of css.matchAll(/box-shadow:([^;}]*)/g)) for (const part of value.replace(" !important", "").split(",")) assert.doesNotMatch(part.trim(), /^inset \S+ \S+ [1-9]/, `no blur: ${value}`);
  assert.match(css, /inset 3px 0 0 #D7263D/, "focused Steam menu row: crimson left bar");
  assert.match(css, /_23IDiIFxX\._23IDiIFxX \._2w9TpItem\._2w9TpItem:is\(\.gpfocus/, "focus targets the menu's rows");
  assert.match(css, /_1uLVHdot\._1uLVHdot\{[^}]*background-color:#D7263D/, "Home's active marker is crimson");
  assert.match(css, /_3IconQ\._3IconQ\{[^}]*flex:0 0 32px/, "fixed-width icon cells");
  assert.match(css, /_2w9TpItem\._2w9TpItem\{[^}]*padding-left:24px[^}]*transform:none/, "one left inset, no drift");
  assert.match(css, /_FbsMain\._FbsMain\{[^}]*background-color:#000000/, "the MENU footer is black");
  assert.match(css, /_QamTitle\._QamTitle,[^{]*_QamHead\._QamHead\{[^}]*background-color:transparent[^}]*background-image:none/, "clear title strip over the stars");
  assert.match(css, /_QamTab\._QamTab\._QamSel\._QamSel,[^{]*\{[^}]*background-color:#1A1D21/, "selected tab gunmetal");
  assert.match(css, /_FtQam\._FtQam[^{]*\{[^}]*background-color:#000000/, "black Quick Access footer");
  assert.match(css, /_QamMenu\._QamMenu \.DialogButton:not\(\[style\*="background"\]\)[^{]*\{[^}]*background-color:#1A1D21/, "neutral buttons inside Quick Access, inline colours kept");
  assert.match(css, /_QamMenu\._QamMenu \._DdButton\._DdButton/, "neutral dropdowns inside Quick Access");
  assert.match(css, /_HdHeader\._HdHeader[^{]*\{[^}]*background-color:transparent/, "clear top bar");
  assert.match(css, /_FtBasic\._FtBasic[^{]*\{[^}]*background-color:transparent/, "clear button-hint bar");
  assert.match(css, /url\("http:\/\/127\.0\.0\.1:1337\/plugins\/Playhub\/assets\/w\.png"\) 24px center \/ 44px 44px/, "the mark on the Steam menu, 44px, top left");
  // Context menus: the Context board, with the Steam menu's left bar for focus and crimson danger text.
  assert.match(css, /_CmContents\._CmContents\{[^}]*background-color:#000000[^}]*inset 0 0 0 1px #1A1D21[^}]*border-radius:var\(--core-radius-float, 10px\)/, "black panel, hairline edge, the float corner token");
  assert.match(css, /_CmItem\._CmItem\{[^}]*min-height:48px[^}]*padding:0 22px[^}]*font-size:16px[^}]*color:#C9D1D9/, "board rows, steel text");
  assert.match(css, /_CmItem\._CmItem\._CmFocused\._CmFocused,[^{]*\{[^}]*background-color:#1A1D21[^}]*inset 3px 0 0 #D7263D/, "focused: gunmetal and crimson left bar");
  assert.match(css, /_CmItem\._CmItem\._CmSub\._CmSub\._CmActive\._CmActive/, "an open submenu's item keeps the marker");
  assert.match(css, /_CmItem\._CmItem\._CmDanger\._CmDanger\{color:#EA5A68/, "danger items in crimson text");
  assert.match(css, /_CmItem\._CmItem:is\(\[disabled\],\[aria-disabled="true"\]\)\{color:#9AA4AE/, "disabled items in hint grey");
  assert.match(css, /_CmSep\._CmSep\{[^}]*background-color:#1A1D21[^}]*height:1px/, "hairline separators");
  assert.match(css, /_CmHeader\._CmHeader[^{]*\{[^}]*color:#9AA4AE[^}]*font-size:13px/, "header");
  // Full height: the menu's and Quick Access's stars reach the screen edges through the canvas only.
  assert.match(css, /canvas\[data-core-ui-stars\]\{[^}]*top:calc\(var\(--core-ui-scroll, 0px\) - var\(--core-ui-extend-top, 0px\)\)[^}]*background-color:#000000/);
  const hosts = JSON.parse(JSON.stringify(sheet.hosts));
  assert.deepEqual(hosts.map(host => host.id), ["menu", "qam-container", "qam-panel", "qam", "menu-footer", "qam-footer", "qam-footer-shadow", "footer-opaque", "home", "library", "game-page", "collector-rom-page", "awesome-mix-page"]);
  assert.deepEqual(hosts.filter(host => host.page).map(host => host.id), ["home", "library", "game-page", "collector-rom-page", "awesome-mix-page"]);
  const game = hosts.find(host => host.id === "game-page");
  assert.equal(game.viewport, true); assert.equal(game.exclude, ".romm-core-page"); assert.equal(game.below, "._AdPlay");
  assert.deepEqual(hosts.filter(host => host.fullHeight).map(host => host.id), ["menu", "qam-container", "qam-panel", "qam"]);
  assert.deepEqual(hosts.filter(host => host.panel).map(host => host.id), ["menu", "qam-container", "qam-panel", "qam"]);
  assert.deepEqual(hosts.filter(host => host.source === "plugin").map(host => host.id), ["collector-rom-page", "awesome-mix-page"]);
  // 3.3.9's surfaces (the game page, Settings, Downloads...) are not in these fake maps; tests/coreUiMatchers.test.mjs
  // builds them from the live key lists.
  const later = new Set(["game-hero", "game-playbar", "game-cloud", "game-screen-two", "game-sections", "settings-pages", "toggles", "downloads-page",
    "download-bar", "ingame-menu", "overlay-configurator", "search-pages", "settings-text"]);
  assert.deepEqual(JSON.parse(JSON.stringify(sheet.skipped)).filter(id => !later.has(id)), []);
}

const all = Object.values(maps);
const wordmark = "http://127.0.0.1:1337/plugins/Playhub/assets/w.png";

test("Decky 3: every rule is built from Steam's class maps", () => {
  checkSheet(styles.buildCoreUiCss(styles.findCoreUiMaps(decky3(all)), { wordmark }));
});

test("Decky 4: the same sheet from module variables (findVar) and from module scopes alone (findModuleScopeByVar)", () => {
  const three = styles.buildCoreUiCss(styles.findCoreUiMaps(decky3(all)), { wordmark }).css;
  const four = styles.buildCoreUiCss(styles.findCoreUiMaps(decky4(all)), { wordmark });
  checkSheet(four);
  assert.equal(four.css, three);
  assert.equal(styles.buildCoreUiCss(styles.findCoreUiMaps(decky4ScopesOnly(all)), { wordmark }).css, three);
});

test("a missing class map skips only its rules and names them", () => {
  const sheet = styles.buildCoreUiCss(styles.findCoreUiMaps(decky4([maps.mainmenu, maps.footer])), { wordmark });
  assert.ok(sheet.applied.includes("menu-rows") && sheet.applied.includes("menu-footer-bar") && sheet.applied.includes("chrome-footer"));
  for (const id of ["qam-panel", "qam-title", "qam-tabs", "chrome-header"]) assert.ok(sheet.skipped.includes(id), id);
  assert.ok(!sheet.css.includes("_Qam"));
  const none = styles.buildCoreUiCss(styles.findCoreUiMaps({}), {});
  assert.equal(none.css, "");
  assert.equal(none.applied.length, 0);
  assert.equal(none.hosts.length, 0);
});

test("a class string that is not a plain class list is never written into a selector", () => {
  assert.equal(styles.classSelector({ Menu: "a{}b" }, "Menu"), undefined);
  assert.equal(styles.classSelector({ Menu: "_a _b" }, "Menu"), "._a._b._a:not(#core-ui-boost)");
  assert.equal(styles.buildCoreUiCss({ mainmenu: { ...maps.mainmenu, Item: "x,y" } }).skipped.includes("menu-rows"), true);
  assert.doesNotMatch(styles.buildCoreUiCss({ mainmenu: maps.mainmenu }, { wordmark: "javascript:alert(1)" }).css, /javascript|url\(/);
});

test("the Starfield: the screensaver's field, one shared clock, readable text over the brightest star", () => {
  const source = readFileSync(new URL("../screensaver/main.js", import.meta.url), "utf8");
  assert.match(source, /let seed=23;/);
  assert.match(source, /c<\.04\?CRIMSON:c<\.3\?WHITE:STEEL/);
  // The screensaver's first three stars (same generator, same seed).
  let seed = 23; const rand = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
  const stars = starfield.makeStars(3);
  for (const star of stars) { rand(); assert.equal(star.x, rand()); assert.equal(star.y, rand()); assert.equal(star.z, rand()); }
  for (const text of ["#9AA4AE", "#C9D1D9", "#E6EAEE", "#FFFFFF"]) assert.ok(starfield.worstStarContrast(text) >= 4.5, `${text} ${starfield.worstStarContrast(text)}`);
  assert.ok(starfield.starAlpha(0) <= starfield.STARFIELD_MAX_ALPHA);
  const field = starfield.createStarfield();
  const draws = [];
  const context = { clearRect() {}, beginPath() {}, arc() {}, fill() { draws.push(this.fillStyle); }, fillStyle: "" };
  assert.ok(field.draw(context, 400, 1000) > 0);
  for (const style of draws) assert.ok(Number(/,([\d.]+)\)$/.exec(style)[1]) <= starfield.STARFIELD_MAX_ALPHA);
  field.advance(10);
  assert.ok(field.depth() <= starfield.STARFIELD_MAX_STEP * starfield.STARFIELD_SPEED + 1e-9, "a long pause does not jump the field");
});

test("Steam menu: Awesome Mix replaces Digital stores, only while the plugin is loaded", () => {
  assert.deepEqual(JSON.parse(JSON.stringify(entries.coreSteamMenuEntries({ plugins: [{ name: "Discord" }, { name: "Awesome Mix" }] }))),
    [{ id: "awesome-mix", label: "Awesome Mix", route: "/awesome-mix/youtube-music-big-picture", order: 15 }]);
  assert.deepEqual(entries.coreSteamMenuEntries({ plugins: [{ name: "Discord" }] }).length, 0, "absent: no entry");
  assert.deepEqual(entries.coreSteamMenuEntries({ plugins: [], installedPlugins: [{ name: "Awesome Mix" }] }).length, 0, "installed but not loaded: no route, no entry");
  assert.equal(entries.coreSteamMenuEntries({ installedPlugins: [{ name: "awesome mix" }] }).length, 1, "a loader without a loaded list");
  assert.equal(entries.coreSteamMenuEntries({ installedPlugins: [{ name: "Awesome Mix" }], disabledPlugins: [{ name: "Awesome Mix" }] }).length, 0, "disabled");
  assert.equal(entries.coreSteamMenuEntries(undefined).length, 0);
  assert.ok(entries.coreSteamMenuEntries({ plugins: ["Awesome Mix"] }).every(entry => entry.id !== "store-digital"));

  // The portal registers what coreSteamMenuEntries answers, and never Digital stores.
  const portal = load("StoreDigitalPortal.tsx", { "./decky": { SP_REACT: { createElement: (type, props) => ({ type, props }) } } });
  const registered = [], removed = [];
  const register = entry => { registered.push(entry.id); return () => removed.push(entry.id); };
  const menu = new Map();
  portal.syncCoreSteamMenu(menu, entries.coreSteamMenuEntries({ plugins: [{ name: "Awesome Mix" }] }), register);
  portal.syncCoreSteamMenu(menu, entries.coreSteamMenuEntries({ plugins: [{ name: "Awesome Mix" }] }), register);
  assert.deepEqual(registered, ["awesome-mix"], "registered once");
  portal.syncCoreSteamMenu(menu, entries.coreSteamMenuEntries({ plugins: [] }), register);
  assert.deepEqual(removed, ["awesome-mix"], "removed when the plugin goes");
  const source = readFileSync(new URL("../src/StoreDigitalPortal.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(source, /id:"store-digital"/);
  const section = readFileSync(new URL("../src/CoreUiSection.tsx", import.meta.url), "utf8");
  assert.match(section, /Navigate\?\.\(STORE_DIGITAL_ROUTE\)/, "Digital stores opens from Quick Access > Core");
  const index = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  assert.match(index, /<DigitalStoresEntry locale=\{locale\} \/>/);
  assert.match(index, /<CoreUiSection locale=\{locale\} \/>/);
});

test("the Core UI setting: on by default, stored, and listeners hear changes once", () => {
  const store = new Map();
  const storage = { getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) };
  const setting = settingModule.createCoreUiSetting(storage);
  assert.equal(setting.get(), true);
  let heard = 0; const stop = setting.subscribe(() => heard++);
  setting.set(false); setting.set(false);
  assert.equal(heard, 1); assert.equal(store.get("core.ui.enabled"), "0");
  assert.equal(settingModule.createCoreUiSetting(storage).get(), false, "remembered");
  setting.set(true); stop(); setting.set(false);
  assert.equal(heard, 2);
  assert.equal(settingModule.createCoreUiSetting({ getItem() { throw new Error("no storage"); }, setItem() {} }).get(), true);
  assert.deepEqual([...locale.coreUiLanguages].sort(), ["de", "en", "es", "fr", "hi", "it", "ja", "ko", "pt", "ru", "uk", "zh"]);
  for (const language of locale.coreUiLanguages) assert.equal(locale.coreUiCopy(language).label, "Core UI");
});

// A small DOM: enough for the runtime's style, attribute and canvas work.
class Node {
  constructor(doc, tag, classes = []) { this.ownerDocument = doc; this.tagName = tag; this.attrs = new Map(); this.children = []; this.parentElement = null; this.style = { position: "", props: new Map(), setProperty(name, value) { this.props.set(name, value); } }; this.classes = classes; this.textContent = ""; this.rect = { left: 0, top: 0, width: 400, height: 900 }; }
  get nodeType() { return 1; }
  get id() { return this.attrs.get("id") ?? ""; } set id(value) { this.attrs.set("id", value); }
  get isConnected() { let node = this; while (node.parentElement) node = node.parentElement; return node === this.ownerDocument.documentElement; }
  get firstChild() { return this.children[0] ?? null; }
  querySelector(selector) { const walk = node => { for (const child of node.children) { if (child.matches(selector)) return child; const hit = walk(child); if (hit) return hit; } return null; }; return walk(this); }
  get nextSibling() { const list = this.parentElement?.children ?? []; return list[list.indexOf(this) + 1] ?? null; }
  get scrollTop() { return this._scroll ?? 0; } set scrollTop(v) { this._scroll = v; }
  get clientWidth() { return this.rect.width; } get clientHeight() { return this.rect.height; }
  getAttribute(name) { return this.attrs.has(name) ? this.attrs.get(name) : null; }
  setAttribute(name, value) { this.attrs.set(name, String(value)); }
  removeAttribute(name) { this.attrs.delete(name); }
  appendChild(child) { child.remove(); child.parentElement = this; this.children.push(child); return child; }
  insertBefore(child, before) { child.remove(); child.parentElement = this; const at = before ? this.children.indexOf(before) : -1; at < 0 ? this.children.push(child) : this.children.splice(at, 0, child); return child; }
  remove() { if (this.parentElement) { this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1); this.parentElement = null; } }
  matches(selector) {
    return selector.split(",").some(part => {
      part = part.trim();
      const attr = /^\[([\w-]+)(?:="([^"]*)")?\]$/.exec(part);
      if (attr) return attr[2] === undefined ? this.attrs.has(attr[1]) : this.attrs.get(attr[1]) === attr[2];
      return part.split(".").filter(Boolean).every(name => this.classes.includes(name));
    });
  }
  closest(selector) { for (let node = this; node; node = node.parentElement) if (node.matches(selector)) return node; return null; }
  contains(other) { for (let node = other; node; node = node.parentElement) if (node === this) return true; return false; }
  get nextElementSibling() { return this.nextSibling; }
  get scrollHeight() { return this._scrollHeight ?? this.rect.height; }
  scrollTo({ top }) { this.scrolledTo = top; this._scroll = top; }
  getBoundingClientRect() { const { left, top, width, height } = this.rect; return { left, top, width, height, right: left + width, bottom: top + height }; }
  getContext() { return this.ownerDocument.context; }
  get width() { return this._w ?? 0; } set width(v) { this._w = v; } get height() { return this._h ?? 0; } set height(v) { this._h = v; }
}
function fakeDocument({ reduce = false } = {}) {
  const doc = { visibilityState: "visible", draws: 0 };
  doc.context = { clearRect() {}, beginPath() {}, arc() {}, fill() { doc.draws++; }, fillStyle: "" };
  doc.documentElement = new Node(doc, "html");
  doc.head = doc.documentElement.appendChild(new Node(doc, "head"));
  doc.body = doc.documentElement.appendChild(new Node(doc, "body"));
  const all = () => { const out = []; const walk = node => { for (const child of node.children) { out.push(child); walk(child); } }; walk(doc.documentElement); return out; };
  doc.createElement = tag => new Node(doc, tag);
  doc.getElementById = id => all().find(node => node.id === id) ?? null;
  doc.querySelectorAll = selector => all().filter(node => node.matches(selector));
  doc.querySelector = selector => doc.querySelectorAll(selector)[0] ?? null;
  doc.defaultView = { innerWidth: 1920, innerHeight: 1080, matchMedia: () => ({ matches: reduce }), getComputedStyle: node => ({ position: node.style.position || "static", visibility: "visible", display: "block" }) };
  return doc;
}

function runtimeFixture({ reduce = false, orb, host } = {}) {
  const doc = fakeDocument({ reduce });
  const header = doc.body.appendChild(new Node(doc, "div", ["_HdHeader"]));
  const clock = header.appendChild(new Node(doc, "div", ["_HdClock"]));
  header.appendChild(new Node(doc, "div", ["_HdIcons"]));
  const menu = doc.body.appendChild(new Node(doc, "div", ["_23IDiIFxX"]));
  menu.appendChild(new Node(doc, "div", ["_2w9TpItem"]));
  const qam = doc.body.appendChild(new Node(doc, "div", ["_QamMenu"]));
  const qamFooter = qam.appendChild(new Node(doc, "div", ["_FtQam"]));
  qamFooter.rect = { left: 0, top: 1040, width: 400, height: 40 };
  const store = new Map();
  const setting = settingModule.createCoreUiSetting({ getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) });
  const frames = []; let running = false; const logs = [];
  const coreUi = load("coreUi.ts", {}, { window: {}, performance: { now: () => 0 }, Date });
  const runtime = coreUi.createCoreUi({
    ui: decky4(all), documents: () => [doc], setting, wordmark,
    running: () => running,
    frame: { request: callback => { frames.push(callback); return frames.length; }, cancel: () => {} },
    every: { start: () => 1, stop: () => {} }, now: () => 0, log: message => logs.push(message), orb,
    pinPrompts: host ? () => load("coreInputPrompts.ts").pinControllerPrompts(host) : undefined,
  });
  const step = time => { const pending = frames.splice(0); for (const callback of pending) callback(time); return pending.length; };
  return { doc, header, clock, menu, qam, qamFooter, setting, runtime, frames, step, logs, setRunning: value => { running = value; } };
}

test("Core UI on: one stylesheet, the root attribute, one Starfield canvas per black surface; off removes all of it", () => {
  const f = runtimeFixture();
  const { doc, menu, qam, qamFooter } = f;
  const style = doc.getElementById("core-ui-style");
  assert.ok(style && style.textContent.includes("_23IDiIFxX"), "stylesheet injected");
  assert.equal(doc.documentElement.getAttribute("data-core-ui"), "1");
  assert.equal(menu.firstChild.tagName, "canvas"); assert.equal(menu.firstChild.getAttribute("data-core-ui-stars"), "menu");
  assert.equal(qam.firstChild.tagName, "canvas");
  assert.equal(qamFooter.getAttribute("data-core-ui-host"), "nested", "a black footer inside Quick Access shows the panel's stars");
  assert.ok(!qamFooter.children.some(child => child.tagName === "canvas"));
  assert.equal(menu.style.position, "relative");

  f.runtime.sync();
  assert.equal(doc.querySelectorAll("[data-core-ui-stars]").length, 2, "sync is idempotent");
  assert.equal(doc.head.children.filter(child => child.tagName === "style").length, 1);

  // One shared field: both visible surfaces drawn in one frame.
  f.step(0); f.step(40);
  assert.ok(doc.draws > 0);

  f.setting.set(false);
  assert.equal(doc.getElementById("core-ui-style"), null, "stylesheet removed");
  assert.equal(doc.documentElement.getAttribute("data-core-ui"), null);
  assert.equal(doc.querySelectorAll("[data-core-ui-stars]").length, 0, "canvases removed");
  assert.equal(doc.querySelectorAll("[data-core-ui-host]").length, 0);
  assert.equal(menu.style.position, "", "Steam's own position back");
  f.step(80);
  assert.equal(f.frames.length, 0, "no frames while off");

  f.setting.set(true);
  assert.ok(doc.getElementById("core-ui-style"));
  assert.equal(doc.querySelectorAll("[data-core-ui-stars]").length, 2, "on again: everything back");
  f.runtime.dispose();
  assert.equal(doc.getElementById("core-ui-style"), null);
  assert.equal(doc.querySelectorAll("[data-core-ui-stars]").length, 0);
});

test("the Starfield pauses while a game runs, while hidden and under reduced motion", () => {
  const f = runtimeFixture();
  f.step(0); f.step(40);
  assert.ok(f.frames.length > 0, "animating while visible");
  f.setRunning(true);
  let drawsBefore = f.doc.draws; f.step(80);
  assert.ok(f.doc.draws > drawsBefore, "one still frame");
  assert.equal(f.frames.length, 0, "then no more frames while a game runs");
  f.runtime.sync(); drawsBefore = f.doc.draws; f.step(120);
  assert.equal(f.doc.draws, drawsBefore, "still frame is not redrawn");
  f.setRunning(false); f.runtime.sync(); f.step(160); f.step(200);
  assert.ok(f.frames.length > 0, "moves again after the game");
  f.menu.rect = { left: -400, top: 0, width: 400, height: 900 }; f.qam.rect = { left: 1920, top: 0, width: 400, height: 900 };
  drawsBefore = f.doc.draws; f.step(240);
  assert.equal(f.frames.length, 0, "menus off screen: stopped");
  assert.equal(f.doc.draws, drawsBefore);
  f.runtime.dispose();

  const reduced = runtimeFixture({ reduce: true });
  reduced.step(0);
  assert.ok(reduced.doc.draws > 0, "reduced motion: one still frame");
  assert.equal(reduced.frames.length, 0, "reduced motion: no animation");
  reduced.runtime.dispose();
});

test("a client without some class maps logs each skipped part once and still styles the rest", () => {
  const doc = fakeDocument();
  const setting = settingModule.createCoreUiSetting(undefined);
  const logs = [];
  const coreUi = load("coreUi.ts", {}, { window: {}, performance: { now: () => 0 }, Date });
  const runtime = coreUi.createCoreUi({ ui: decky3([maps.mainmenu]), documents: () => [doc], setting, running: () => false,
    frame: { request: () => 1, cancel() {} }, every: { start: () => 1, stop() {} }, now: () => 0, log: message => logs.push(message) });
  for (let i = 0; i < 30; i++) runtime.sync();
  assert.ok(doc.getElementById("core-ui-style").textContent.includes("_2w9TpItem"));
  const qamLogs = logs.filter(line => line.includes("qam-panel"));
  assert.equal(qamLogs.length, 1, "logged once");
  assert.equal(new Set(logs).size, logs.length);
  runtime.dispose();
});

test("full height: the Steam menu's and Quick Access's stars reach the top and bottom edges, the panel itself untouched", () => {
  const f = runtimeFixture();
  f.menu.rect = { left: 0, top: 64, width: 400, height: 960 };     // below the top bar, above the hints (1080p)
  f.qam.rect = { left: 1520, top: 72, width: 400, height: 940 };
  f.qam.scrollTop = 30;
  f.runtime.sync();
  const props = node => Object.fromEntries(node.firstChild.style.props);
  assert.deepEqual(props(f.menu), { "--core-ui-extend-top": "64px", "--core-ui-extend-bottom": "56px", "--core-ui-scroll": "0px" });
  assert.deepEqual(props(f.qam), { "--core-ui-extend-top": "72px", "--core-ui-extend-bottom": "68px", "--core-ui-scroll": "30px" });
  f.step(0); f.step(40);
  assert.equal(f.menu.firstChild.height, 960 + 64 + 56, "the canvas covers the whole screen height");
  assert.equal(f.menu.style.position, "relative", "only a static panel gains a position");
  assert.equal(f.menu.margin, undefined);
  // 4K: same CSS pixels at twice the device pixels; a panel already full height gets no extension; a panel
  // far from the edges is capped at a quarter of the screen.
  f.doc.defaultView.innerHeight = 1080;
  f.menu.rect = { left: 0, top: 0, width: 400, height: 1080 };
  f.qam.rect = { left: 1520, top: 500, width: 400, height: 100 };
  f.runtime.sync();
  assert.equal(props(f.menu)["--core-ui-extend-top"], "0px");
  assert.equal(props(f.qam)["--core-ui-extend-top"], "270px");
  assert.equal(props(f.qam)["--core-ui-extend-bottom"], "270px");
  f.runtime.dispose();
});

test("Jarvis's orb sits right after Steam's clock, follows its state, and goes with Core UI", () => {
  let state = "idle"; const listeners = new Set();
  const f = runtimeFixture({ orb: { state: () => state, subscribe: listener => { listeners.add(listener); return () => listeners.delete(listener); } } });
  const orb = f.doc.getElementById("core-jarvis-orb");
  assert.ok(orb);
  assert.equal(f.clock.nextSibling, orb, "next to the clock");
  assert.equal(orb.getAttribute("data-state"), "idle");
  state = "busy"; for (const listener of listeners) listener();
  assert.equal(orb.getAttribute("data-state"), "busy", "changes at once, not at the next sync");
  assert.match(f.doc.getElementById("core-ui-style").textContent, /#core-jarvis-orb\[data-state="busy"\]\{[^}]*animation:core-jarvis-pulse/);
  assert.match(f.doc.getElementById("core-ui-style").textContent, /prefers-reduced-motion: reduce\)\{[^{]*core-jarvis-orb\[data-state="busy"\]\{animation:none !important\}/);
  orb.remove(); f.runtime.sync();
  assert.equal(f.clock.nextSibling?.id, "core-jarvis-orb", "put back after Steam re-renders the top bar");
  f.setting.set(false);
  assert.equal(f.doc.getElementById("core-jarvis-orb"), null);
  f.runtime.dispose();
});

test("button hints stay on controller prompts: keyboard and mouse sources are pinned to the last controller", () => {
  const prompts = load("coreInputPrompts.ts");
  const seen = [];
  const source = { m_currentValue: { eActivationSourceType: 2, nActiveGamepadIndex: 0, nLastActiveGamepadIndex: 0 },
    Set(value) { this.m_currentValue = value; seen.push(value); } };
  const controller = { m_navigationSource: source };
  const host = { FocusNavController: controller, GamepadNavTree: { m_context: { m_controller: controller } } };
  const store = new Map();
  const memory = prompts.promptsMemory({ getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) });
  const undo = prompts.pinControllerPrompts(host, memory);
  assert.equal(typeof undo, "function");
  assert.equal(store.get("core.ui.controller-source-type"), "2", "the controller type is learned, not assumed");
  source.Set({ eActivationSourceType: 1, nActiveGamepadIndex: -1, nLastActiveGamepadIndex: 0 }); // keyboard
  assert.deepEqual(plainObject(source.m_currentValue), { eActivationSourceType: 2, nActiveGamepadIndex: 0, nLastActiveGamepadIndex: 0 });
  source.Set({ eActivationSourceType: 2, nActiveGamepadIndex: 1, nLastActiveGamepadIndex: 1 }); // a second controller
  source.Set({ eActivationSourceType: 4, nActiveGamepadIndex: -1, nLastActiveGamepadIndex: -1 }); // mouse
  assert.deepEqual(plainObject(source.m_currentValue), { eActivationSourceType: 2, nActiveGamepadIndex: 1, nLastActiveGamepadIndex: 1 }, "the last controller's glyph family");
  undo();
  source.Set({ eActivationSourceType: 1, nActiveGamepadIndex: -1, nLastActiveGamepadIndex: 0 });
  assert.equal(source.m_currentValue.eActivationSourceType, 1, "undone: Steam's own choice again");
  assert.equal(Object.getOwnPropertyDescriptor(source, "Set").value.name, "Set");

  // Started on the keyboard, the remembered controller type puts the hints back at once.
  const cold = { m_currentValue: { eActivationSourceType: 1, nActiveGamepadIndex: -1, nLastActiveGamepadIndex: 0 }, Set(value) { this.m_currentValue = value; } };
  const undoCold = prompts.pinControllerPrompts({ FocusNavController: { m_navigationSource: cold } }, memory);
  assert.equal(cold.m_currentValue.eActivationSourceType, 2);
  undoCold();
  // Unknown shapes are left alone.
  assert.equal(prompts.pinControllerPrompts({ FocusNavController: { m_navigationSource: { Set() {}, m_currentValue: "x" } } }), undefined);
  assert.equal(prompts.pinControllerPrompts({}), undefined);
  // Nothing learned and nothing remembered: passed through untouched.
  const fresh = { m_currentValue: { eActivationSourceType: 1, nActiveGamepadIndex: -1 }, Set(value) { this.m_currentValue = value; } };
  const undoFresh = prompts.pinControllerPrompts({ FocusNavController: { m_navigationSource: fresh } });
  fresh.Set({ eActivationSourceType: 4, nActiveGamepadIndex: -1 });
  assert.equal(fresh.m_currentValue.eActivationSourceType, 4);
  undoFresh();
});

test("the controller-prompt pin is part of the Core UI setting", () => {
  const source = { m_currentValue: { eActivationSourceType: 2, nActiveGamepadIndex: 0, nLastActiveGamepadIndex: 0 }, Set(value) { this.m_currentValue = value; } };
  const steam = source.Set;
  const f = runtimeFixture({ host: { FocusNavController: { m_navigationSource: source } } });
  assert.notEqual(source.Set, steam, "pinned while on");
  f.setting.set(false);
  assert.equal(source.Set, steam, "unpinned when off");
  f.setting.set(true);
  assert.notEqual(source.Set, steam, "pinned again");
  f.runtime.dispose();
  assert.equal(source.Set, steam);
});

function plainObject(value) { return JSON.parse(JSON.stringify(value)); }

// 3.3.7: hosts from other plugins (CORE-UI.md, "Hosts from other plugins"). The Collector's ROM game page is found by
// its own root class, `.romm-core-page`, and marked data-core-ui-host="1" while Core UI is on.
test("plugin hosts: The Collector's page is a page host by its fixed selector, never full height or a panel", () => {
  const sheet = styles.buildCoreUiCss(styles.findCoreUiMaps(decky3(all)), { wordmark });
  const host = JSON.parse(JSON.stringify(sheet.hosts.find(item => item.id === "collector-rom-page")));
  assert.deepEqual(host, { id: "collector-rom-page", selector: ".romm-core-page", strong: ".romm-core-page.romm-core-page:not(#core-ui-boost)", minArea: 0,
    fullHeight: false, panel: false, page: true, source: "plugin", viewport: true });
  // Its canvas: behind the content, the visible height only, in the shared host rules.
  assert.match(sheet.css, /html\[data-core-ui\] \.romm-core-page\[data-core-ui-host\] > canvas\[data-core-ui-stars\][,{]/);
  assert.match(sheet.css, /html\[data-core-ui\] \.romm-core-page\[data-core-ui-host\] > canvas\[data-core-ui-stars\]\{height:var\(--core-ui-page-height, 0px\) !important;max-height:100% !important\}/);
  // 3.3.8: a canvas without data-core-ui-live (its surface off screen, or no live surface) is never drawn.
  assert.match(sheet.css, /html\[data-core-ui\] canvas\[data-core-ui-stars\]:not\(\[data-core-ui-live\]\)\{display:none !important\}/);
  assert.match(sheet.css, /\.romm-core-page\.romm-core-page:not\(#core-ui-boost\)\[data-core-ui-host="nested"\]/);
  // Core UI paints nothing else on it: no rule targets the page or Steam's header art for it.
  const other = sheet.css.split("\n").filter(line => !line.startsWith("/* starfield-hosts */"));
  assert.ok(other.every(line => !line.includes("romm-core-page")), "only the Starfield host rules name the page");
  // A selector that is not a plain class list is refused.
  assert.equal(styles.resolveHostSelector({}, { id: "x", selector: "div > .a", minArea: 0 }), undefined);
  assert.equal(styles.resolveHostSelector({}, { id: "x", selector: ".a[data-x]", minArea: 0 }), undefined);
  assert.deepEqual(JSON.parse(JSON.stringify(styles.resolveHostSelector({}, { id: "x", selector: ".a.b", minArea: 0 }))), { plain: ".a.b", strong: ".a.b.a:not(#core-ui-boost)" });
  // No Steam class map at all: no Core UI, so no plugin host either.
  const none = styles.buildCoreUiCss({});
  assert.equal(none.css, "");
  assert.equal(none.hosts.length, 0);
  // The selector table and the snapshot can find it.
  assert.deepEqual(JSON.parse(JSON.stringify(styles.targetSelectors({}, "collectorRomPage", true))), [".romm-core-page"]);
});

function collectorFixture(options = {}) {
  const f = runtimeFixture(options);
  const { doc } = f;
  // Steam's game page: the header art above, The Collector's page (its own still field first) below it.
  const details = doc.body.appendChild(new Node(doc, "div", ["_AppDetails"]));
  const art = details.appendChild(new Node(doc, "div", ["_HeaderArt"]));
  art.rect = { left: 0, top: 0, width: 1920, height: 560 };
  const page = details.appendChild(new Node(doc, "div", ["_OverviewPanel", "romm-core-page"]));
  page.style.position = "relative";
  page.appendChild(new Node(doc, "div", ["romm-core-stars"]));
  page.rect = { left: 0, top: 560, width: 1920, height: 1800 };
  f.runtime.sync();
  return { ...f, art, page };
}

test("plugin hosts: data-core-ui-host=\"1\" and the Starfield on The Collector's page while Core UI is on, gone when it is off", () => {
  const f = collectorFixture();
  const { doc, page, art } = f;
  assert.equal(page.getAttribute("data-core-ui-host"), "1");
  const canvas = page.firstChild;
  assert.equal(canvas.tagName, "canvas");
  assert.equal(canvas.getAttribute("data-core-ui-stars"), "collector-rom-page");
  assert.ok(page.children.some(child => child.classes.includes("romm-core-stars")), "the plugin's own still field stays in place (its CSS hides it)");
  assert.equal(art.children.length, 0, "nothing is put in the hero art");
  assert.equal(art.getAttribute("data-core-ui-host"), null);
  assert.equal(page.style.position, "relative", "the page's own position is kept");
  // Never full height, never a panel: no inline edits, no clear layers.
  assert.equal(f.runtime.diagnostics().full["collector-rom-page"], undefined);
  assert.equal(page.style.props.size, 0, "no full-height stretch on the page");
  assert.equal(f.runtime.diagnostics().hosts["collector-rom-page"], 1);

  // The page starts below the art: the canvas covers from the page's top to the screen's bottom (520px), never above.
  assert.equal(canvas.style.props.get("--core-ui-scroll"), "0px");
  assert.equal(canvas.style.props.get("--core-ui-page-height"), "520px");
  assert.equal(canvas.style.props.get("--core-ui-extend-top"), "0px");
  f.step(0);
  assert.equal(canvas.height, 520);
  assert.equal(canvas.width, 1920);
  assert.ok(f.frames.length > 0, "animated");

  // Scrolled down past the art: the canvas follows to the part on screen.
  page.rect = { left: 0, top: -300, width: 1920, height: 1800 };
  f.step(40);
  assert.equal(canvas.style.props.get("--core-ui-scroll"), "300px");
  assert.equal(canvas.style.props.get("--core-ui-page-height"), "1080px");
  assert.equal(canvas.height, 1080);

  // Near the end of the page: only the page's remaining height.
  page.rect = { left: 0, top: -1500, width: 1920, height: 1800 };
  f.step(80);
  assert.equal(canvas.style.props.get("--core-ui-scroll"), "1500px");
  assert.equal(canvas.style.props.get("--core-ui-page-height"), "300px");

  f.setting.set(false);
  assert.equal(page.getAttribute("data-core-ui-host"), null, "the attribute goes with Core UI off");
  assert.ok(!page.children.some(child => child.tagName === "canvas"));
  assert.ok(page.children.some(child => child.classes.includes("romm-core-stars")), "the plugin's still field is untouched");
  assert.deepEqual({ ...f.runtime.diagnostics().hosts }, {});
  f.setting.set(true);
  assert.equal(page.getAttribute("data-core-ui-host"), "1", "and comes back with it on");
  f.runtime.dispose();
  assert.equal(page.getAttribute("data-core-ui-host"), null);
});

test("plugin hosts: The Collector's page is held still under reduced motion and while a game runs, and released when it closes", () => {
  const reduced = collectorFixture({ reduce: true });
  reduced.menu.remove(); reduced.qam.remove();
  reduced.runtime.sync();
  reduced.step(0);
  assert.ok(reduced.doc.draws > 0, "one still frame");
  assert.equal(reduced.frames.length, 0, "no animation");
  assert.equal(reduced.runtime.diagnostics().state, "still-reduced");
  reduced.runtime.dispose();

  const f = collectorFixture();
  f.menu.remove(); f.qam.remove();
  f.runtime.sync();
  f.step(0);
  f.setRunning(true);
  f.step(40);
  assert.equal(f.frames.length, 0);
  assert.equal(f.runtime.diagnostics().state, "still-game");
  // The ROM page closes: its surface is released.
  f.page.remove();
  f.runtime.sync();
  assert.equal(f.runtime.diagnostics().hosts["collector-rom-page"], undefined);
  assert.equal(f.runtime.surfaces().length, 0);
  f.runtime.dispose();
});

// 3.3.8 (TV: after closing a ROM page a black, starry band covered the bottom of every page).
test("plugin hosts: leaving a ROM page for another route leaves no canvas, no live layer and no host attribute behind", () => {
  const f = collectorFixture();
  const { doc, page } = f;
  f.step(0);
  const canvas = page.firstChild;
  assert.equal(canvas.getAttribute("data-core-ui-live"), "1", "live while the page is on screen");
  // Route change: Steam replaces the game page with Downloads.
  page.parentElement.remove();
  const downloads = doc.body.appendChild(new Node(doc, "div", ["_DownloadsPage"]));
  downloads.rect = { left: 0, top: 0, width: 1920, height: 1080 };
  f.runtime.sync();
  f.step(40);
  const left = doc.querySelectorAll("[data-core-ui-stars]").filter(node => node.getAttribute("data-core-ui-stars") === "collector-rom-page");
  assert.equal(left.length, 0, "no Collector canvas anywhere");
  assert.equal(doc.querySelectorAll("[data-core-ui-host]").filter(node => node.classes.includes("romm-core-page")).length, 0);
  assert.equal(f.runtime.diagnostics().hosts["collector-rom-page"], undefined);
  assert.equal(downloads.children.length, 0, "nothing put on the next page");
  f.runtime.dispose();
});

test("plugin hosts: a ROM page kept in the DOM but no longer painted by its plugin has its canvas hidden and never escapes it", () => {
  const f = collectorFixture();
  const { doc, page } = f;
  f.step(0);
  const canvas = page.firstChild;
  assert.equal(canvas.getAttribute("data-core-ui-live"), "1");
  // The Collector's stylesheet goes (page closed) while Steam keeps the element: transparent and static.
  const computed = doc.defaultView.getComputedStyle;
  doc.defaultView.getComputedStyle = node => node === page ? { position: "static", visibility: "visible", display: "block", backgroundColor: "rgba(0, 0, 0, 0)" } : computed(node);
  page.style.position = "";
  f.runtime.sync();
  assert.equal(canvas.getAttribute("data-core-ui-live"), null, "hidden: its plugin no longer paints the page");
  assert.equal(page.style.position, "relative", "the host stays the canvas's containing block");
  f.step(40);
  assert.equal(canvas.getAttribute("data-core-ui-live"), null);
  f.runtime.dispose();
});

test("every Starfield canvas that belongs to no connected host is removed", () => {
  const f = collectorFixture();
  const { doc, page } = f;
  f.step(0);
  // A canvas moved out of its host (React re-parenting) and a stray one in the body.
  const canvas = page.firstChild;
  doc.body.appendChild(canvas);
  const stray = doc.body.appendChild(doc.createElement("canvas"));
  stray.setAttribute("data-core-ui-stars", "collector-rom-page");
  f.runtime.sync();
  assert.ok(!doc.body.children.includes(stray), "stray canvas removed");
  assert.ok(!doc.body.children.includes(canvas), "orphaned canvas removed");
  const now = page.children.filter(child => child.tagName === "canvas");
  assert.equal(now.length, 1, "the host gets one fresh canvas");
  f.runtime.dispose();
});

// 3.3.9: Steam's game page, screen two.
function gamePageFixture() {
  const f = runtimeFixture();
  const { doc } = f;
  const page = doc.body.appendChild(new Node(doc, "div", ["_AdCont"]));
  page.rect = { left: 0, top: 638, width: 1920, height: 2000 };
  const bar = page.appendChild(new Node(doc, "div", ["_AdPlay"]));
  bar.rect = { left: 0, top: 638, width: 1920, height: 120 };
  f.runtime.sync();
  return { ...f, page, bar };
}

test("the game page: stars only below the play bar, never over the art", () => {
  const f = gamePageFixture();
  const { page } = f;
  assert.equal(page.getAttribute("data-core-ui-host"), "1");
  f.step(0);
  const canvas = page.firstChild;
  assert.equal(canvas.getAttribute("data-core-ui-stars"), "game-page");
  // The play bar ends at 758: the canvas starts there (120px into the page) and covers the rest of the screen.
  assert.equal(canvas.style.props.get("--core-ui-scroll"), "120px");
  assert.equal(canvas.style.props.get("--core-ui-page-height"), "322px");
  // Scrolled to screen two: the play bar has left the screen, the stars fill the view.
  page.rect = { left: 0, top: -400, width: 1920, height: 2000 };
  f.bar.rect = { left: 0, top: -400, width: 1920, height: 120 };
  f.step(40);
  assert.equal(canvas.style.props.get("--core-ui-scroll"), "400px");
  assert.equal(canvas.style.props.get("--core-ui-page-height"), "1080px");
  f.runtime.dispose();
});

test("the game page: a ROM page (The Collector's) is never this host, and leaving the page leaves nothing behind", () => {
  const f = gamePageFixture();
  const { doc, page } = f;
  f.step(0);
  page.appendChild(new Node(doc, "div", ["romm-core-page"]));
  f.runtime.sync();
  assert.equal(page.getAttribute("data-core-ui-host"), null);
  assert.ok(!page.children.some(child => child.tagName === "canvas"), "no Core canvas on a ROM page's container");
  page.children.pop();
  f.runtime.sync();
  assert.equal(page.getAttribute("data-core-ui-host"), "1");
  page.remove();
  f.runtime.sync();
  assert.equal(doc.querySelectorAll("[data-core-ui-stars]").filter(node => node.getAttribute("data-core-ui-stars") === "game-page").length, 0);
  assert.equal(f.runtime.diagnostics().hosts["game-page"], undefined);
  f.runtime.dispose();
});

// 3.3.9 (TV snapshot: Quick Access and the Steam menu are BrowserView windows of their own, QuickAccess_uid21 and
// MainMenu_uid21, which no window store lists, so Core UI never reached them). 3.3.10: the TV now titles them
// QuickAccess_uid2 and MainMenu_uid2; nothing may depend on the number.
test("popup windows: Quick Access's and the Steam menu's own documents are found and styled like the main window", () => {
  const coreUi = load("coreUi.ts", {}, { window: {}, performance: { now: () => 0 }, Date });
  const main = fakeDocument(); main.defaultView.innerWidth = 1348; main.title = "Steam Big Picture Mode";
  const qamDoc = fakeDocument(); qamDoc.defaultView.innerWidth = 855; qamDoc.title = "QuickAccess_uid2";
  const menuDoc = fakeDocument(); menuDoc.defaultView.innerWidth = 240; menuDoc.title = "MainMenu_uid2";
  const host = { document: main, closed: false };
  let scans = 0, clock = 0;
  const popups = coreUi.popupDocuments(() => clock, doc => doc === main ? [qamDoc, menuDoc] : [], () => { scans++; });
  const docs = coreUi.steamUiDocuments(host, undefined, popups);
  assert.deepEqual([...docs.map(doc => doc.title)], ["Steam Big Picture Mode", "QuickAccess_uid2", "MainMenu_uid2"]);
  coreUi.steamUiDocuments(host, undefined, popups);
  assert.equal(scans, 1, "the owner cache is refreshed at most every POPUP_RESCAN_MS");
  clock += coreUi.POPUP_RESCAN_MS;
  coreUi.steamUiDocuments(host, undefined, popups);
  assert.equal(scans, 2, "and then again, so a recreated popup is found");

  // The runtime styles every popup: the stylesheet, the root attribute, and the panel in its own 855 px window.
  const qam = qamDoc.body.appendChild(new Node(qamDoc, "div", ["_QamMenu"]));
  qam.rect = { left: 0, top: 0, width: 855, height: 758 };
  const menu = menuDoc.body.appendChild(new Node(menuDoc, "div", ["_23IDiIFxX"]));
  menu.rect = { left: 0, top: 0, width: 240, height: 758 };
  const store = new Map();
  const setting = settingModule.createCoreUiSetting({ getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, value) });
  const runtime = coreUi.createCoreUi({ ui: decky4(all), documents: () => [main, qamDoc, menuDoc], setting, wordmark, running: () => false,
    frame: { request: () => 1, cancel: () => {} }, every: { start: () => 1, stop: () => {} }, now: () => 0, log: () => {} });
  for (const doc of [main, qamDoc, menuDoc]) {
    assert.ok(doc.getElementById("core-ui-style"), `stylesheet in ${doc.title}`);
    assert.equal(doc.documentElement.getAttribute("data-core-ui"), "1");
  }
  assert.equal(qam.getAttribute("data-core-ui-host"), "1", "Quick Access's panel fills its window and is still the panel");
  assert.equal(menu.getAttribute("data-core-ui-host"), "1");
  assert.ok(qam.children.some(child => child.tagName === "canvas"), "the Starfield in Quick Access's window");
  runtime.dispose();
  assert.equal(qamDoc.getElementById("core-ui-style"), null, "and all of it goes with Core UI off");
});

test("popup windows: every live BrowserView React holds is found (closed ones are not)", () => {
  const anchors = load("onboardingAnchors.ts", {}, { window: undefined });
  const view = title => ({ closed: false, document: { title } });
  const qamView = view("QuickAccess_uid2"), menuView = view("MainMenu_uid7"), gone = { closed: true, document: { title: "Old" } };
  const owner = win => ({ GetViewWindow: () => win, GetBrowserView: () => ({}) });
  const leaf = { memoizedState: { memoizedState: owner(qamView), next: { memoizedState: owner(gone), next: null } } };
  const other = { memoizedState: { memoizedState: owner(menuView), next: null } };
  const root = { child: leaf, sibling: null, memoizedState: null };
  leaf.sibling = other;
  const element = { ["__reactContainer$x"]: { return: null, stateNode: { current: root } } };
  const main = { querySelectorAll: () => [element] };
  assert.deepEqual([...anchors.browserViewDocuments(main).map(doc => doc.title)], ["QuickAccess_uid2", "MainMenu_uid7"]);
});

test("the game view: down from the play bar scrolls screen two (its tab row) to the top, up scrolls back; a ROM page is The Collector's", () => {
  const f = runtimeFixture();
  const { doc } = f;
  const listeners = [];
  doc.addEventListener = (type, handler) => listeners.push({ type, handler });
  doc.removeEventListener = (type, handler) => { const at = listeners.findIndex(item => item.handler === handler); if (at >= 0) listeners.splice(at, 1); };
  const computed = doc.defaultView.getComputedStyle;
  const scroller = doc.body.appendChild(new Node(doc, "div", ["_Scroller"]));
  scroller.rect = { left: 0, top: 0, width: 1920, height: 1080 }; scroller._scrollHeight = 4000;
  doc.defaultView.getComputedStyle = node => node === scroller ? { overflowY: "auto", position: "relative" } : computed(node);
  const page = scroller.appendChild(new Node(doc, "div", ["_AdCont"]));
  const bar = page.appendChild(new Node(doc, "div", ["_AdPlay"]));
  const play = bar.appendChild(new Node(doc, "div", ["_AdPlayBtn"]));
  const row = page.appendChild(new Node(doc, "div", ["_TpRow"]));
  const tab = row.appendChild(new Node(doc, "div", ["_TpTab"]));
  row.rect = { left: 0, top: 1100, width: 1920, height: 58 };
  f.runtime.sync();
  const focusin = listeners.find(item => item.type === "focusin")?.handler;
  assert.ok(focusin, "listens for focus");
  focusin({ target: play });
  focusin({ target: tab });
  assert.equal(scroller.scrolledTo, 1100 - 40, "the tab row lands just under the top bar");
  focusin({ target: play });
  assert.equal(scroller.scrolledTo, 0, "back up: screen one");
  // A ROM page: The Collector scrolls it itself.
  scroller.scrolledTo = undefined;
  page.appendChild(new Node(doc, "div", ["romm-core-page"]));
  focusin({ target: tab });
  assert.equal(scroller.scrolledTo, undefined);
  f.runtime.dispose();
  assert.equal(listeners.length, 0, "the listener goes with Core UI off");
});

// 3.3.10 (TV photo: the clock, weather and search sat over Home's tiles): the top bar stays fully transparent at the top of
// a page, slides out once the page's first screen has scrolled under it, and comes back near the top.
test("3.3.10 top bar: hidden past the first screen, back near the top, never for a sideways shelf; gone with Core UI off", () => {
  assert.equal(styles.headerHidden(0, 758, 40, false), false, "the top of a page");
  assert.equal(styles.headerHidden(600, 758, 40, false), false, "still inside screen one");
  assert.equal(styles.headerHidden(700, 758, 40, false), true, "past screen one (758 less two 40px bars)");
  assert.equal(styles.headerHidden(660, 758, 40, true), true, "a small scroll back at the line keeps it hidden");
  assert.equal(styles.headerHidden(600, 758, 40, true), false, "back near the top, it returns");
  assert.equal(styles.headerHidden(90, 120, 40, false), true, "a short window still hides it, after 80px at least");

  const f = runtimeFixture();
  const { doc, header } = f;
  doc.defaultView.innerHeight = 758;
  header.rect = { left: 0, top: 0, width: 1348, height: 40 };
  const listeners = [];
  doc.addEventListener = (type, handler) => listeners.push({ type, handler });
  doc.removeEventListener = (type, handler) => { const at = listeners.findIndex(item => item.handler === handler); if (at >= 0) listeners.splice(at, 1); };
  const page = doc.body.appendChild(new Node(doc, "div", ["_HomeScroller"]));
  page.rect = { left: 0, top: 0, width: 1348, height: 758 }; page._scrollHeight = 3000;
  const shelf = doc.body.appendChild(new Node(doc, "div", ["_Shelf"]));
  shelf.rect = { left: 0, top: 500, width: 1348, height: 240 }; shelf._scrollHeight = 240;
  f.runtime.sync();
  const scroll = listeners.find(item => item.type === "scroll")?.handler;
  assert.ok(scroll, "one scroll listener per document");
  page.scrollTop = 900; scroll({ target: page });
  assert.equal(header.getAttribute("data-core-ui-header"), "hidden");
  scroll({ target: shelf });
  assert.equal(header.getAttribute("data-core-ui-header"), "hidden", "a shelf scrolling sideways says nothing about the page");
  page.scrollTop = 0; scroll({ target: page });
  assert.equal(header.getAttribute("data-core-ui-header"), null, "back at the top");
  page.scrollTop = 900; scroll({ target: page });
  page.remove(); f.runtime.sync();
  assert.equal(header.getAttribute("data-core-ui-header"), null, "the page left (a route change): the bar comes back");
  const css = f.runtime.sheet().css;
  assert.match(css, /\[data-core-ui-header="hidden"\]\{opacity:0 !important;translate:0 -100% !important;pointer-events:none/);
  f.runtime.dispose();
  assert.equal(listeners.length, 0, "the listener goes with Core UI off");
});

// 3.3.10 (the approved board and its additions): Home's pages are two screens; down into screen two scrolls it to the
// top, and B (or up from the tab row) goes straight back to the tile focus left, the page scrolling back up.
test("3.3.10 Home: screen two scrolls to the top; B or up from the tab row returns to screen one's tile", () => {
  const f = runtimeFixture();
  const { doc } = f;
  const listeners = [];
  doc.addEventListener = (type, handler) => listeners.push({ type, handler });
  doc.removeEventListener = (type, handler) => { const at = listeners.findIndex(item => item.handler === handler); if (at >= 0) listeners.splice(at, 1); };
  const computed = doc.defaultView.getComputedStyle;
  const scroller = doc.body.appendChild(new Node(doc, "div", ["_Home"]));
  scroller.rect = { left: 0, top: 0, width: 1348, height: 758 }; scroller._scrollHeight = 3000;
  doc.defaultView.getComputedStyle = node => node === scroller ? { overflowY: "auto", position: "relative" } : computed(node);
  const one = scroller.appendChild(new Node(doc, "div", [])); one.setAttribute("data-core-home-one", "home");
  const tileA = one.appendChild(new Node(doc, "div", ["core-tile"])), tileB = one.appendChild(new Node(doc, "div", ["core-tile"]));
  for (const tile of [tileA, tileB]) tile.focus = function () { doc.activeElement = this; this.focused = (this.focused ?? 0) + 1; };
  const page = scroller.appendChild(new Node(doc, "div", ["_TpPage"])); page.rect = { left: 0, top: 758, width: 1348, height: 900 };
  const row = page.appendChild(new Node(doc, "div", ["_TpRow"]));
  const tab = row.appendChild(new Node(doc, "div", ["_TpTab"]));
  const content = page.appendChild(new Node(doc, "div", [])); content.setAttribute("data-core-home-tab", "CoreFavorites");
  const card = content.appendChild(new Node(doc, "div", ["core-favorite"]));
  f.runtime.sync();
  const focusins = listeners.filter(item => item.type === "focusin").map(item => item.handler);
  const focusin = target => { for (const handler of focusins) handler({ target }); };
  const buttons = listeners.find(item => item.type === "vgp_onbuttondown" && String(item.handler).includes("backToScreenOne"))?.handler
    ?? listeners.filter(item => item.type === "vgp_onbuttondown").map(item => item.handler)[0];
  assert.ok(buttons, "listens for the controller");
  focusin(tileB);
  focusin(tab);
  assert.equal(scroller.scrolledTo, 758, "down: screen two's top at the top of the page");
  const press = (target, button) => { doc.activeElement = target; const event = { target, detail: { button }, prevented: 0, preventDefault() { this.prevented++; }, stopPropagation() {} };
    for (const item of listeners.filter(l => l.type === "vgp_onbuttondown")) item.handler(event); return event; };
  assert.equal(press(card, 9).prevented, 0, "up inside the content stays Steam's");
  assert.equal(press(card, 2).prevented, 1, "B on screen two");
  assert.equal(doc.activeElement, tileB, "back to the tile focus left");
  focusin(tileB);
  assert.equal(scroller.scrolledTo, 0, "the page scrolls back to screen one");
  focusin(tab);
  assert.equal(press(tab, 9).prevented, 1, "up from the tab row");
  assert.equal(doc.activeElement, tileB);
  assert.equal(press(tileA, 2).prevented, 0, "B on screen one stays Steam's");
  f.runtime.dispose();
  assert.equal(listeners.length, 0, "every listener goes with Core UI off");
});
