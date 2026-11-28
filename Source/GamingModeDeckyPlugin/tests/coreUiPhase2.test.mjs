import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: Core UI 3.3.3 (docs/eternal/CORE-UI.md): the TV fixes of phase 1 and phase 2. Full height on fake DOMs
// shaped like Steam's, the Steam menu's logo band, black chrome, clear layers in Quick Access, the corner rule, the
// Library's tab and collection matching, tones and icon fallbacks, the plugin logos, Core Home's tabs and the Starfield
// screensaver default.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts", "./coreUiDom": "coreUiDom.ts", "./coreFullHeight": "coreFullHeight.ts",
  "./coreArtwork": "coreArtwork.ts", "./coreStarfield": "coreStarfield.ts", "./coreUiSetting": "coreUiSetting.ts", "./coreInputPrompts": "coreInputPrompts.ts" };
const cache = new Map();
function load(file, globals = {}) {
  if (cache.has(file) && !Object.keys(globals).length) return cache.get(file);
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), { compilerOptions }).outputText;
  const require = name => local[name] ? load(local[name]) : {};
  vm.runInNewContext(code, { exports, require, Map, Set, WeakMap, Array, Object, JSON, Math, Number, String, console, encodeURIComponent, Symbol, ...globals });
  if (!Object.keys(globals).length) cache.set(file, exports);
  return exports;
}

const styles = load("coreUiStyles.ts");
const dom = load("coreUiDom.ts");
const full = load("coreFullHeight.ts");
const icons = load("coreLibraryIcons.ts");
const logos = load("corePluginLogos.ts");
const homeTabs = load("coreHomeTabs.ts");
const homeLocale = load("coreHomeLocale.ts");
const saver = load("coreScreensaverDefault.ts");
const artwork = load("coreArtwork.ts");

const maps = {
  mainmenu: { MainMenuEmbedded: "_MmEmbed", Menu: "_Mm", Item: "_MmItem", ItemOuter: "_MmOuter", ItemIcon: "_MmIcon", ItemLabel: "_MmLabel", ActiveDot: "_MmDot", FooterBoxShadow: "_MmFbs" },
  quickaccess: { Menu: "_QaMenu", Container: "_QaCont", QuickAccessMenu: "_QaQam", Title: "_QaTitle", HeaderContainer: "_QaHead", Tab: "_QaTab", Tabs: "_QaTabs",
    TabsWithFooter: "_QaTwf", Selected: "_QaSel", TabGroupPanel: "_QaTgp", TabContentColumn: "_QaTcc", AllTabContents: "_QaAtc", ContentTransition: "_QaCt",
    PanelSection: "_QaPs", FooterBoxShadow: "_QaFbs" },
  footer: { BasicFooter: "_FtBasic", QuickAccessFooter: "_FtQam", Opaque: "_FtOpaque" },
  header: { Header: "_HdHeader", HeaderDownloadContainer: "_HdDl", HeaderOpaque: "_HdOpaque", Clock: "_HdClock" },
  contextmenu: { contextMenuContents: "_CmC", contextMenuItem: "_CmI", BasicContextMenuHeader: "_CmH", Focused: "_CmF" },
  tabbedpage: { TabRow: "_TpRow", TabTitle: "_TpTitle", TabHeaderRowWrapper: "_TpWrap", Tab: "_TpTab", Selected: "_TpSel", GamepadTabbedPage: "_TpPage" },
  library: { GamepadLibrary: "_LbLib", CollectionHeader: "_LbHead" },
};
const sheet = styles.buildCoreUiCss(styles.findCoreUiMaps({ classMap: Object.values(maps), findClassModule: f => Object.values(maps).find(m => f(m)) }));
// 3.3.4: rules are read without the id-level boost every class target carries.
const rule = id => (sheet.css.split("\n").find(line => line.startsWith(`/* ${id} */`)) ?? "").replaceAll(styles.CORE_UI_BOOST, "");

test("corner rule: square panels, bars and focus; one float token for menus and cards", () => {
  assert.equal(styles.CORE_RADIUS_FLOAT, "10px");
  assert.match(rule("corners"), /html\[data-core-ui\] body\{--core-radius-float:10px !important\}/);
  assert.match(rule("context-panel"), /border-radius:var\(--core-radius-float, 10px\)/, "context menus float");
  assert.match(sheet.css, /library-collection-tiles \*\/[^{]*\{[^}]*border-radius:var\(--core-radius-float, 10px\)/, "collection tiles are cards");
  for (const id of ["menu-panel", "menu-focus", "qam-panel", "qam-tabs", "qam-tab-selected", "chrome-header", "chrome-footer", "context-focus", "library-tabs"]) {
    const css = rule(id);
    assert.ok(css, id);
    for (const [, value] of css.matchAll(/border-radius:([^;}]*)/g)) assert.match(value, /^0 !important$/, `${id} is square: ${value}`);
  }
  for (const [, value] of sheet.css.matchAll(/border-radius:([^;}]*)/g)) {
    assert.match(value, /^(0|var\(--core-radius-float, 10px\)|inherit|50%|2px) !important$/, `only the token or square: ${value}`);
  }
});

test("Steam menu: the ring mark alone, 44px, top left, in its own band; Quick Access has no logo", () => {
  assert.ok(styles.CORE_MENU_LOGO_SIZE >= 40 && styles.CORE_MENU_LOGO_SIZE <= 48);
  const css = rule("menu-panel");
  assert.match(css, new RegExp(`::before\\{[^}]*display:block[^}]*order:-1[^}]*flex:0 0 ${styles.CORE_MENU_LOGO_BAND}px[^}]*height:${styles.CORE_MENU_LOGO_BAND}px[^}]*24px center / 44px 44px no-repeat`));
  assert.ok(css.includes(encodeURIComponent('<circle r="62"')), "the ring mark, no wordmark");
  assert.doesNotMatch(css, /wordmark|playhub-wordmark/);
  assert.doesNotMatch(rule("qam-title") + rule("qam-panel"), /url\(/, "no logo in Quick Access");
});

test("logo band: the first row is pushed below the mark when Steam's list ignores it", () => {
  const edits = new dom.InlineEdits();
  const style = () => ({ props: new Map(), getPropertyValue(n) { return this.props.get(n) ?? ""; }, getPropertyPriority() { return ""; }, setProperty(n, v) { this.props.set(n, v); }, removeProperty(n) { this.props.delete(n); } });
  const list = { style: style(), parentElement: null };
  const item = { parentElement: list, getBoundingClientRect: () => ({ left: 0, top: 30 + parseFloat(list.style.props.get("margin-top") ?? "0"), width: 400, height: 48 }) };
  const menu = { style: style(), querySelector: () => item, getBoundingClientRect: () => ({ left: 0, top: 0, width: 400, height: 1080 }) };
  list.parentElement = menu;
  const view = { innerWidth: 1920, innerHeight: 1080, getComputedStyle: () => ({ paddingTop: "0px", marginTop: "0px", position: "static" }) };
  assert.equal(dom.keepBelowLogo(menu, "._MmItem", styles.CORE_MENU_LOGO_BAND, view, edits), list);
  assert.equal(list.style.props.get("margin-top"), "50px", "80px band - 30px = 50px more");
  assert.equal(dom.keepBelowLogo(menu, "._MmItem", styles.CORE_MENU_LOGO_BAND, view, edits), undefined, "already below: untouched");
  edits.restoreAll();
  assert.equal(list.style.props.has("margin-top"), false, "Core UI off: Steam's value back");
});

// A fake Steam panel: Steam places it between the top bar and the hints (top offset and a max height). Its rect follows
// its inline margin, height and padding, like a block in normal flow.
function steamPanel({ top = 72, height = 940, clip = false, scroll = false, ignoreHeight = false } = {}) {
  const make = () => ({ props: new Map(), getPropertyValue(n) { return this.props.get(n) ?? ""; }, getPropertyPriority() { return ""; },
    setProperty(n, v) { this.props.set(n, v); }, removeProperty(n) { this.props.delete(n); } });
  const root = { style: make(), parentElement: null, scrollHeight: 1080, clientHeight: 1080, getBoundingClientRect: () => ({ left: 0, top: 0, width: 1920, height: 1080 }) };
  const wrapper = { style: make(), parentElement: root, scrollHeight: scroll ? 2000 : height, clientHeight: height,
    getBoundingClientRect: () => ({ left: 1520, top, width: 400, height }) };
  const panel = { style: make(), parentElement: wrapper };
  panel.getBoundingClientRect = () => {
    const m = parseFloat(panel.style.props.get("margin-top") ?? "0");
    const h = ignoreHeight ? height : parseFloat(panel.style.props.get("height") ?? String(height));
    return { left: 1520, top: top + m, width: 400, height: h };
  };
  const computed = new Map([[wrapper, { overflowY: clip || scroll ? (scroll ? "auto" : "hidden") : "visible", overflowX: "visible" }],
    [panel, { marginTop: "0px", marginBottom: "0px", paddingTop: "0px", paddingBottom: "0px" }], [root, { overflowY: "hidden" }]]);
  const view = { innerWidth: 1920, innerHeight: 1080, getComputedStyle: el => computed.get(el) ?? {} };
  return { panel, wrapper, view };
}

test("full height: the panel itself spans the top edge to the bottom one, its content stays where Steam put it", () => {
  const { panel, view } = steamPanel();
  const runner = full.createFullHeight();
  assert.equal(runner.apply(panel, view), "stretched");
  const p = Object.fromEntries(panel.style.props);
  assert.equal(p["margin-top"], "-72px"); assert.equal(p["padding-top"], "72px");
  assert.equal(p["margin-bottom"], "-68px"); assert.equal(p["padding-bottom"], "68px");
  assert.equal(p.height, "1080px"); assert.equal(p["max-height"], "none"); assert.equal(p["box-sizing"], "border-box");
  const rect = panel.getBoundingClientRect();
  assert.equal(rect.top, 0); assert.equal(rect.top + rect.height, 1080);
  assert.equal(runner.apply(panel, view), "stretched", "stable on the next check");
  runner.releaseAll();
  assert.equal(panel.style.props.size, 0, "Core UI off: Steam's values back");
});

test("full height: a clipping wrapper (Decky's QAM wrapper) is opened, a scrolling one or a refusing layout is left alone", () => {
  const clipped = steamPanel({ clip: true });
  const runner = full.createFullHeight();
  assert.equal(runner.apply(clipped.panel, clipped.view), "stretched");
  assert.equal(clipped.wrapper.style.props.get("overflow-y"), "visible");
  runner.releaseAll();
  assert.equal(clipped.wrapper.style.props.size, 0);

  const scrolling = steamPanel({ scroll: true });
  assert.equal(full.createFullHeight().apply(scrolling.panel, scrolling.view), "failed");
  assert.equal(scrolling.panel.style.props.size, 0, "undone: focus and scrolling untouched");

  const stubborn = steamPanel({ ignoreHeight: true });
  const again = full.createFullHeight();
  assert.equal(again.apply(stubborn.panel, stubborn.view), "failed");
  assert.equal(stubborn.panel.style.props.size, 0);
  again.apply(stubborn.panel, stubborn.view); again.apply(stubborn.panel, stubborn.view);
  assert.equal(again.apply(stubborn.panel, stubborn.view), "gave-up", "three tries, then Steam's layout stays");

  const already = steamPanel({ top: 0, height: 1080 });
  assert.equal(full.createFullHeight().apply(already.panel, already.view), "full");
});

test("hint bar: pure black over a plain page, clear over game art; the top bar always clear (3.3.9)", () => {
  const css = rule("chrome-footer") + rule("chrome-footer-opaque");
  assert.match(css, /\[data-core-ui-chrome="black"\]\{background-color:#000000 !important;background-image:none !important;box-shadow:none !important;backdrop-filter:none !important;filter:none !important;opacity:1 !important\}/);
  const header = rule("chrome-header");
  assert.doesNotMatch(header, /#000000/, "the top bar is never black");
  assert.match(header, /_HdOpaque\._HdOpaque[^{]*\{background-color:transparent !important;background-image:none !important;box-shadow:none !important;backdrop-filter:none !important/);
  assert.equal(rule("chrome-header-opaque"), "", "no separate opaque rule");
  const bar = { tagName: "DIV", contains: () => false, getBoundingClientRect: () => ({ left: 0, top: 0, width: 1920, height: 48 }) };
  const view = { innerWidth: 1920, innerHeight: 1080, getComputedStyle: el => el.css ?? {} };
  const page = { tagName: "DIV", css: { backgroundColor: "rgb(14, 20, 27)" } };
  assert.equal(dom.chromeState(bar, { elementsFromPoint: () => [bar, page] }, view), "black");
  const art = { tagName: "IMG", css: {} };
  assert.equal(dom.chromeState(bar, { elementsFromPoint: () => [bar, art, page] }, view), "clear");
  const hero = { tagName: "DIV", css: { backgroundImage: 'url("hero.jpg")' } };
  assert.equal(dom.chromeState(bar, { elementsFromPoint: () => [bar, hero] }, view), "clear");
  assert.equal(dom.chromeState(bar, {}, view), undefined, "no hit testing: left as it is");
});

test("Quick Access: the real panel is black with stars, everything above it clear (plugin panels, CSS Loader's grey area)", () => {
  assert.match(rule("qam-panel"), /_QaCont\._QaCont,[^{]*_QaQam\._QaQam\{background-color:transparent/);
  assert.match(rule("qam-panel"), /_QaCont\._QaCont\[data-core-ui-host="1"\][^{]*\{background-color:#000000/);
  assert.match(rule("qam-clear"), /\[data-core-ui-clear\]\{background-color:transparent !important/);
  assert.match(rule("qam-title"), /background-color:transparent/);
  const ids = sheet.hosts.filter(h => h.panel).map(h => h.id);
  assert.deepEqual([...ids], ["menu", "qam-container", "qam-panel", "qam"], "outer candidates first");
  const node = (tag, css, rect, children = [], attrs = {}) => ({ tagName: tag, css, children, attrs, className: attrs.class ?? "",
    getAttribute(n) { return n in this.attrs ? this.attrs[n] : null; }, hasAttribute(n) { return n in this.attrs; }, setAttribute(n, v) { this.attrs[n] = v; },
    getBoundingClientRect: () => rect });
  const panelRect = { left: 1520, top: 0, width: 400, height: 1080 };
  const button = node("DIV", { backgroundColor: "rgb(50,60,70)" }, { left: 1520, top: 0, width: 400, height: 600 }, [], { class: "DialogButton" });
  const cssLoader = node("DIV", { backgroundColor: "rgba(35, 38, 46, 0.9)" }, { left: 1520, top: 100, width: 380, height: 700 }, [button]);
  const art = node("DIV", { backgroundImage: 'url("x.png")', backgroundColor: "rgb(0,0,0)" }, { left: 1520, top: 0, width: 400, height: 800 });
  const small = node("DIV", { backgroundColor: "rgb(40,40,40)" }, { left: 1520, top: 0, width: 40, height: 40 });
  const host = node("DIV", {}, panelRect, [cssLoader, art, small]);
  const view = { innerWidth: 1920, innerHeight: 1080, getComputedStyle: el => el.css };
  assert.equal(dom.clearOpaqueLayers(host, view), 1);
  assert.equal(cssLoader.attrs["data-core-ui-clear"], "1", "the grey panel goes clear");
  assert.equal(button.attrs["data-core-ui-clear"], undefined, "CSS Loader's controls keep their look");
  assert.equal(art.attrs["data-core-ui-clear"], undefined, "pictures are kept");
  assert.equal(small.attrs["data-core-ui-clear"], undefined, "small things are kept");
});

test("Library tabs: matched by title, family tones, steel for the rest", () => {
  const tones = { PlayStation: "#6E8CB8", Nintendo: "#B07A84", Sega: "#5E9E9A", Xbox: "#7FA36B", Retro: "#8F7CB0" };
  for (const [title, tone] of Object.entries(tones)) {
    const found = icons.matchTab(title);
    assert.equal(found.tone, tone, title);
    assert.equal(found.family, title.toLowerCase());
  }
  for (const title of ["All Games", "Installed", "VR", "Completed", "  all   games ", "INSTALLED"]) {
    const found = icons.matchTab(title);
    assert.ok(found, title); assert.equal(found.tone, "#C9D1D9"); assert.equal(found.family, undefined);
  }
  assert.equal(icons.matchTab("Soundtracks"), undefined, "unknown tabs keep Steam's look");
  assert.equal(icons.matchTab(""), undefined);
  assert.deepEqual({ ...styles.CORE_FAMILY_TONES }, { ...icons.CORE_FAMILY_TONES }, "the stylesheet and the matcher agree");
  // 3.3.4: icons and tones are part of the one tab rule (libraryTabCss; its own test is in coreUiSnapshot.test.mjs).
  const css = rule("library-tabs");
  assert.match(css, /\[data-core-tab\]\[data-core-icon\] \._TpTitle\._TpTitle::before\{[^}]*var\(--core-icon\)/, "the icon before the label");
  for (const [family, tone] of Object.entries(icons.CORE_FAMILY_TONES)) assert.ok(css.includes(`[data-core-tone="${family}"]{--core-tone:${tone} !important}`), family);
  assert.match(css, /_TpTab\._TpTab\._TpSel\._TpSel \._TpTitle\._TpTitle::after[^{]*\{opacity:1 !important;background-color:#D7263D/, "the active tab: one crimson underline under the label");
});

test("collections: matched by name (plain and RomM's prefixed form), with families", () => {
  const cases = { "PlayStation 2": ["ps2", "playstation"], "RomM: Nintendo 64 (romm.local)": ["n64", "nintendo"], "Sega Mega Drive/Genesis": ["megadrive", "sega"],
    "Xbox 360": ["xbox360", "xbox"], "Xbox": ["xbox", "xbox"], "Atari 2600": ["atari2600", "retro"], "TurboGrafx-16/PC Engine": ["pcengine", "retro"],
    "Game Boy Color": ["gbc", "nintendo"], "Game Boy": ["gb", "nintendo"], "Wii U": ["wiiu", "nintendo"], "Wii": ["wii", "nintendo"], "VR": ["vr", undefined],
    "Completed": ["completed", undefined], "Super Nintendo Entertainment System": ["snes", "nintendo"], "Dreamcast": ["dreamcast", "sega"] };
  for (const [name, [key, family]] of Object.entries(cases)) {
    const found = icons.matchCollection(name);
    assert.equal(found?.key, key, name); assert.equal(found?.family, family, name);
  }
  assert.equal(icons.matchCollection("Favorites"), undefined);
  assert.equal(icons.plainCollectionName("RomM: Sega Saturn (192.168.1.5)"), "Sega Saturn");
});

test("icons: the user's map wins, then Core's key, then Core's original drawings; nothing third-party is shipped", () => {
  const png = "data:image/png;base64,AAAA";
  const set = { tabs: { playstation: png }, collections: { "Nintendo 64": png }, keys: { xbox: "data:image/png;base64,XBOX", bad: "javascript:alert(1)" } };
  assert.equal(icons.tabIcon("PlayStation", icons.matchTab("PlayStation"), set), png, "by title, any case");
  assert.equal(icons.tabIcon("Xbox", icons.matchTab("Xbox"), set), "data:image/png;base64,XBOX", "by key");
  assert.match(icons.tabIcon("Nintendo", icons.matchTab("Nintendo"), set), /^data:image\/svg\+xml,/, "family tab: Core's generic pad");
  assert.match(icons.tabIcon("VR", icons.matchTab("VR"), undefined), /^data:image\/svg\+xml,/, "VR: Core's original headset");
  assert.equal(icons.collectionIcon("RomM: Nintendo 64 (host)", icons.matchCollection("RomM: Nintendo 64 (host)"), set), png, "by plain collection name");
  assert.equal(icons.collectionIcon("Sega Saturn", icons.matchCollection("Sega Saturn"), undefined), undefined, "no original: Steam's collage stays");
  assert.match(icons.collectionIcon("Xbox 360", icons.matchCollection("Xbox 360"), undefined), /^data:image\/svg\+xml,/);
  assert.equal(icons.tabIcon("Odd", undefined, { tabs: { Odd: "javascript:alert(1)" } }), undefined, "only image data URLs");
  assert.deepEqual(Object.keys(artwork.CORE_ORIGINAL_ICONS).sort(), ["all-games", "completed", "generic", "installed", "ps3", "vr", "xbox", "xbox360"]);
  const source = readFileSync(new URL("../src/coreArtwork.ts", import.meta.url), "utf8");
  assert.doesNotMatch(source, /PBMini|lnkd|core-steel-|core-satin-|core-pbline-/i, "no third-party pack art");
});

test("decorating the live Library: attributes and the icon property, all removed again", () => {
  const make = (text, attrs = {}) => ({ textContent: text, attrs: { ...attrs }, style: { props: new Map(), getPropertyValue(n) { return this.props.get(n) ?? ""; }, setProperty(n, v) { this.props.set(n, v); }, removeProperty(n) { this.props.delete(n); } },
    getAttribute(n) { return n in this.attrs ? this.attrs[n] : null; }, setAttribute(n, v) { this.attrs[n] = String(v); }, removeAttribute(n) { delete this.attrs[n]; }, querySelector() { return null; } });
  const tabs = [make("All Games"), make("PlayStation"), make("Soundtracks")];
  const tiles = [make("", { "aria-label": "PlayStation 2", href: "/library/collection/1" }), make("Favorites", { href: "/library/collection/2" })];
  const doc = { querySelectorAll: selector => selector === "._TpTab" ? tabs : tiles };
  const decorator = icons.createLibraryDecorator({ icons: () => ({ collections: { "PlayStation 2": "data:image/png;base64,UFMy" } }) });
  decorator.decorate(doc, { tab: "._TpTab" });
  assert.equal(tabs[1].attrs["data-core-tab"], "playstation"); assert.equal(tabs[1].attrs["data-core-tone"], "playstation");
  assert.equal(tabs[1].attrs["data-core-icon"], "1"); assert.match(tabs[1].style.props.get("--core-icon"), /^url\("data:image\/svg\+xml,/);
  assert.equal(tabs[0].attrs["data-core-tone"], undefined, "All Games is steel");
  assert.deepEqual(tabs[2].attrs, {}, "unknown tab untouched");
  assert.equal(tiles[0].attrs["data-core-collection"], "ps2"); assert.equal(tiles[0].style.props.get("--core-icon"), 'url("data:image/png;base64,UFMy")');
  assert.equal(tiles[1].attrs["data-core-collection"], undefined);
  decorator.undecorate(doc);
  for (const element of [...tabs, ...tiles]) {
    assert.equal(Object.keys(element.attrs).filter(k => k.startsWith("data-core")).length, 0);
    assert.equal(element.style.props.size, 0);
  }
});

test("the icon store loads once, keeps the last good set on failure, refreshes on request", async () => {
  let calls = 0, fail = false, time = 0;
  const store = icons.createIconStore(async () => { calls++; if (fail) throw new Error("no"); return { tabs: { VR: "data:image/png;base64,AA" } }; }, () => time);
  assert.equal(store.get(), undefined);
  await new Promise(r => setTimeout(r, 0));
  assert.equal(store.get().tabs.VR, "data:image/png;base64,AA");
  store.get(); store.get();
  assert.equal(calls, 1, "not reloaded every second");
  fail = true; await store.refresh(true);
  assert.equal(store.get().tabs.VR, "data:image/png;base64,AA", "kept");
});

test("plugin logos: matched by Decky plugin name or repository; others keep their own icon", () => {
  const cases = { Discord: "assemble", "Awesome Mix": "awesome-mix", "Now Playing Plus": "awesome-mix", "Nexus Mods": "forge", Forge: "forge",
    "romm-dock": "the-collector", "RomM-Dock": "the-collector", "The Collector": "the-collector", " the  collector ": "the-collector" };
  for (const [name, logo] of Object.entries(cases)) assert.equal(logos.corePluginLogo(name), logo, name);
  for (const name of ["CSS Loader", "Discord Status", "", undefined, "Awesome Mixer"]) assert.equal(logos.corePluginLogo(name), undefined, String(name));
  const glyph = logos.corePluginGlyph("Discord");
  assert.match(glyph, /currentColor/); assert.doesNotMatch(glyph, /#D7263D|url\(#chrome\)|<defs>/, "single colour");
  assert.match(logos.corePluginTile("M2ljay/Eternal-Mix"), /^data:image\/svg\+xml,/);
  assert.match(logos.corePluginTile("m2ljay/vibe-nexus"), /^data:image\/svg\+xml,/, "repository aliases");
  assert.equal(logos.corePluginTile("LoZazaMastro/Playhub-Discord"), undefined, "upstream keeps its own cover");
});

test("Core Home (3.3.8): Consoles and registered tabs (Club) without Favorites in the list; the brief, On this day, What's new and Friends gone", () => {
  const steam = [{ id: "WhatsNew", title: "What's new" }, { id: "Recommended", title: "On this day" }, { id: "Friends", title: "Friends" }, { id: "Other", title: "Other" }];
  // A stale brief entry from an older caller is dropped too.
  const core = [{ id: "WhatsNew", title: "Brief", order: 10 }, { id: "CoreConsoles", title: "Consoles", order: 20 }, { id: "CoreClub", title: "Club", order: 30 }];
  const next = homeTabs.composeHomeTabs(steam, core);
  assert.deepEqual([...next.map(tab => tab.title)], ["Consoles", "Club", "Other"]);
  assert.ok(next.every(tab => !("order" in tab)));
  // A remembered active tab that pointed at a removed tab opens Consoles.
  for (const old of ["Friends", "WhatsNew", "Recommended", "CoreBrief", undefined]) assert.equal(homeTabs.activeHomeTab(old, next), "CoreConsoles", String(old));
  assert.equal(homeTabs.activeHomeTab("CoreClub", next), "CoreClub");
  assert.equal(homeTabs.HOME_TAB_ORDER.brief, undefined);

  let clubReady = false; const clubListeners = new Set();
  const off = homeTabs.registerHomeTab({ id: "club", label: "Club", order: 30, render: () => null, isAvailable: () => clubReady,
    subscribe: listener => { clubListeners.add(listener); return () => clubListeners.delete(listener); } });
  const seen = homeTabs.homeTabsRevision();
  assert.deepEqual([...homeTabs.availableHomeTabs().map(tab => tab.id)], [], "the Club waits for its data");
  clubReady = true; for (const listener of clubListeners) listener();
  assert.ok(homeTabs.homeTabsRevision() > seen, "Home hears the Club's store");
  homeTabs.registerHomeTab({ id: "Broken", label: "x", order: 31, component: () => null, isAvailable: () => { throw new Error("no"); } });
  homeTabs.registerHomeTab({ id: "Later", label: "y", order: 25, component: () => null, isAvailable: () => false });
  assert.deepEqual([...homeTabs.availableHomeTabs().map(tab => tab.id)], ["club"]);
  off();
  assert.deepEqual([...homeTabs.availableHomeTabs().map(tab => tab.id)], []);
  assert.equal(clubListeners.size, 0, "unregistering stops listening");

  const source = readFileSync(new URL("../src/coreHome.tsx", import.meta.url), "utf8");
  assert.match(source, /registerHomeTab\(\{ id: clubHomeTab\.id[^}]*order: HOME_TAB_ORDER\.club[^}]*subscribe: clubHomeTab\.subscribe/, "the Club through the registry");
  assert.doesNotMatch(source, /CoreBriefHero|JarvisBrief|HistoryTabs/, "no brief and no On this day on Home (3.3.8)");
  assert.match(source, /class HomeBoundary/); assert.match(source, /return fallback;/);
  assert.match(readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8"), /<ControlSection id="club".*?TbTrophy.*?><ClubSettings locale=\{locale\} \/><\/ControlSection>/);
});

test("Core Home data: console shelves by family, last played, Forge's updates, greetings in 12 languages", () => {
  const app = (appid, name, last) => ({ appid, display_name: name, rt_last_time_played: last });
  const collections = [{ displayName: "PlayStation 2", allApps: [app(1, "A", 5), app(2, "B", 9)] }, { displayName: "RomM: PlayStation (h)", allApps: [app(2, "B", 9), app(3, "C", 1)] },
    { displayName: "VR", visibleApps: [app(4, "D", 0)] }, { displayName: "Favorites", allApps: [app(5, "E", 99)] }, { displayName: "Nintendo 64", allApps: new Map([[6, app(6, "F", 2)]]) }];
  const familyOf = name => { const found = icons.matchCollection(name); return found?.key === "vr" ? "vr" : found?.family; };
  const shelves = homeTabs.consoleShelves(collections, familyOf);
  assert.deepEqual([...shelves.map(s => s.family)], ["playstation", "nintendo", "vr"]);
  assert.deepEqual([...shelves[0].apps.map(a => a.appid)], [2, 1, 3], "de-duplicated, recent first");
  assert.equal(homeTabs.lastPlayedApp([app(1, "A", 5), app(2, "B", 9), app(3, "", 50)]).name, "B");
  assert.equal(homeTabs.lastPlayedApp([]), undefined);
  assert.equal(homeTabs.forgeUpdateCount({ __eternalForgeUpdates: { version: 1, count: 3 } }), 3);
  assert.equal(homeTabs.forgeUpdateCount({ __eternalForgeUpdates: { version: 1, games: [{ count: 2 }, { count: 1 }] } }), 3);
  assert.equal(homeTabs.forgeUpdateCount({}), 0);
  assert.deepEqual([...homeLocale.coreHomeLanguages].sort(), ["de", "en", "es", "fr", "hi", "it", "ja", "ko", "pt", "ru", "uk", "zh"]);
  for (const language of homeLocale.coreHomeLanguages) {
    const copy = homeLocale.coreHomeCopy(language);
    for (const value of Object.values(copy)) assert.ok(typeof value === "string" && value.length > 0, language);
    assert.ok(copy.lastPlayed.includes("{game}") && copy.mods.includes("{count}"), language);
  }
  assert.equal(homeLocale.coreHomeCopy("en").evening, "Good evening");
  assert.deepEqual([0, 4, 5, 11, 12, 17, 18, 23].map(homeLocale.greetingKey), ["night", "night", "morning", "morning", "afternoon", "afternoon", "evening", "evening"]);
});

test("Starfield screensaver: a one-time default over Steam's slideshow, never over a later choice; the switch", () => {
  const storage = () => { const map = new Map(); return { getItem: k => map.get(k) ?? null, setItem: (k, v) => map.set(k, v), map }; };
  const store = current => ({ clientSettings: { screensaver_current_id: current }, SetClientSetting(key, value) { this.clientSettings[key] = value; } });
  const s1 = store("steam-gameslideshow"), st1 = storage();
  assert.equal(saver.applyStarfieldDefault(s1, st1), "applied");
  assert.equal(s1.clientSettings.screensaver_current_id, "Core Starfield");
  assert.equal(st1.map.get(saver.STARFIELD_DEFAULT_FLAG), "1");
  s1.clientSettings.screensaver_current_id = "steam-gameslideshow";       // the user goes back to Steam's slideshow
  assert.equal(saver.applyStarfieldDefault(s1, st1), "already-applied");
  assert.equal(s1.clientSettings.screensaver_current_id, "steam-gameslideshow", "never overridden again");

  const s2 = store("steam-bouncinglogo"), st2 = storage();
  assert.equal(saver.applyStarfieldDefault(s2, st2), "user-choice");
  assert.equal(s2.clientSettings.screensaver_current_id, "steam-bouncinglogo");
  assert.equal(st2.map.get(saver.STARFIELD_DEFAULT_FLAG), "1");

  const s3 = store(undefined), st3 = storage();
  assert.equal(saver.applyStarfieldDefault(s3, st3), "applied", "unset counts as Steam's default");
  const s4 = { clientSettings: { screensaver_current_id: "" } }, st4 = storage();
  assert.equal(saver.applyStarfieldDefault(s4, st4), "unavailable", "no known setter: nothing written");
  assert.equal(st4.map.has(saver.STARFIELD_DEFAULT_FLAG), false, "tried again next start");
  const s5 = { clientSettings: { screensaver_current_id: "steam-gameslideshow" }, UpdateClientSettings(patch) { Object.assign(this.clientSettings, patch); } };
  assert.equal(saver.applyStarfieldDefault(s5, storage()), "applied", "another client's setter");

  let current = store("steam-gameslideshow"), heard = 0;
  const control = saver.createScreensaverControl(() => current);
  control.subscribe(() => heard++);
  assert.equal(control.available(), true); assert.equal(control.get(), false);
  control.set(true); assert.equal(control.get(), true);
  control.set(false); assert.equal(current.clientSettings.screensaver_current_id, "steam-gameslideshow");
  assert.equal(heard, 2);
  current = undefined; assert.equal(control.available(), false);
  const wiring = readFileSync(new URL("../src/circlesScreensaver.ts", import.meta.url), "utf8");
  assert.match(wiring, /applyStarfieldDefault\(settingsStore,window\.localStorage\)/);
});

test("Starfield frames: each surface's own window, a timer backstop, one clock", () => {
  const timers = [];
  const host = { performance: { now: () => 1000 }, setTimeout: (fn, ms) => { timers.push({ fn, ms }); return timers.length; }, clearTimeout: id => { if (timers[id - 1]) timers[id - 1].fn = null; } };
  const coreUi = load("coreUi.ts", { window: host, performance: host.performance, Date });
  let rafCallback;
  const popup = { requestAnimationFrame: fn => { rafCallback = fn; return 7; }, cancelAnimationFrame: () => {} };
  const times = [];
  coreUi.frameIn(host, popup, time => times.push(time));
  assert.equal(timers[0].ms, 250, "a backstop while waiting for the popup's frame");
  rafCallback(99999);
  assert.deepEqual(times, [1000], "the main window's clock, not the popup's");
  assert.equal(timers[0].fn, null, "backstop cleared");
  const frozen = { requestAnimationFrame: () => 8, cancelAnimationFrame: () => {} };
  coreUi.frameIn(host, frozen, time => times.push(time));
  timers[1].fn();
  assert.deepEqual(times, [1000, 1000], "a window that never draws still moves the stars");
  const starfield = load("coreStarfield.ts");
  const field = starfield.createStarfield();
  field.advanceTo(0); field.advanceTo(200);
  assert.ok(Math.abs(field.depth() - 0.2 * starfield.STARFIELD_SPEED) < 1e-9, "a 5 fps popup still moves at full speed");
});

test("3.3.9 Favorites: first and the default, Steam's own collection most recently played first, the old default migrated once", () => {
  const core = [{ id: "CoreFavorites", title: "Favorites", order: 10 }, { id: "CoreConsoles", title: "Consoles", order: 20 }, { id: "CoreClub", title: "Club", order: 30 }];
  const next = homeTabs.composeHomeTabs([{ id: "WhatsNew" }, { id: "Recommended" }], core);
  assert.deepEqual([...next.map(tab => tab.id)], ["CoreFavorites", "CoreConsoles", "CoreClub"]);
  for (const old of ["WhatsNew", "Recommended", "Friends", undefined]) assert.equal(homeTabs.activeHomeTab(old, next), "CoreFavorites", String(old));
  assert.equal(homeTabs.activeHomeTab("CoreConsoles", next), "CoreConsoles", "a chosen tab stays");
  assert.equal(homeTabs.activeHomeTab("CoreConsoles", next, true), "CoreFavorites", "the saved 3.3.8 default moves to Favorites once");
  assert.equal(homeTabs.activeHomeTab("CoreClub", next, true), "CoreClub");
  assert.deepEqual({ ...homeTabs.HOME_TAB_ORDER }, { favorites: 10, consoles: 20, club: 30 });

  const app = (appid, name, last) => ({ appid, display_name: name, rt_last_time_played: last });
  const favorite = { id: "favorite", allApps: [app(10, "Steam game", 5), app(2147483650, "ROM shortcut", 9), app(10, "Steam game", 5), app(3, "Never", 0), app(4, "Alpha", 0)] };
  const store = { GetCollection: id => id === "favorite" ? favorite : undefined };
  const apps = homeTabs.favoriteApps(homeTabs.favoritesCollection(store));
  assert.deepEqual([...apps.map(a => a.name)], ["ROM shortcut", "Steam game", "Alpha", "Never"], "recent first, then by name, no duplicates");
  assert.equal(homeTabs.favoritesCollection({ userCollections: [{ id: "x" }, favorite] }), favorite, "the store's list when there is no getter");
  assert.deepEqual([...homeTabs.favoriteApps(homeTabs.favoritesCollection({}))], []);

  for (const language of homeLocale.coreHomeLanguages) {
    const copy = homeLocale.coreHomeCopy(language);
    assert.ok(copy.favorites && copy.favoritesEmpty, language);
    assert.doesNotMatch(copy.favoritesEmpty, /\bsir\b/i);
  }
  assert.equal(homeLocale.coreHomeCopy("en").favoritesEmpty, "Favorite a game in your Library to see it here");
  const source = readFileSync(new URL("../src/coreHome.tsx", import.meta.url), "utf8");
  assert.match(source, /id: FAVORITES_ID, title: text\.favorites, content: <Favorites/);
  assert.match(source, /core-favorite\.gpfocus[^{]*\{outline:none;translate:0 -6px\}/, "3.3.10: no ring, the tile lifts");
  assert.match(source, /Navigate\?\.\(`\/library\/app\/\$\{app\.appid\}`\)/, "A opens the game view");
});

// 3.3.10 (TV photo: Street Fighter 6 was an empty gunmetal box on Favorites): a tile tries Steam's portraits, then draws
// the hero with the logo, the hero with the name, or the name on a gradient; never an empty box.
test("3.3.10 Favorites art: portrait first (Steam's own lookups too), then hero and logo, never an empty tile", () => {
  const art = load("coreFavoriteArt.ts");
  const overview = { appid: 1364780 };
  const store = {
    GetAppOverviewByAppID: id => id === 1364780 ? overview : undefined,
    GetVerticalCapsuleURLForApp: o => o === overview ? "https://steamloopback.host/assets/1364780/abc/library_600x900.jpg" : "",
    GetCustomHeroImageURLs: () => { throw new Error("not on this client"); },
    GetHeroImageURLs: () => ["https://steamloopback.host/assets/1364780/abc/library_hero.jpg"],
    GetLogoImageURLForApp: () => "https://steamloopback.host/assets/1364780/abc/logo.png",
  };
  const found = art.favoriteArt(1364780, store);
  assert.equal(found.portrait[0], "https://steamloopback.host/assets/1364780/abc/library_600x900.jpg", "Steam's own portrait lookup (hashed folder)");
  assert.ok(found.portrait.includes("https://steamloopback.host/customimages/1364780p.png"), "then the local cache");
  assert.equal(found.hero[0], "https://steamloopback.host/assets/1364780/abc/library_hero.jpg");
  assert.equal(found.logo[0], "https://steamloopback.host/assets/1364780/abc/logo.png");
  assert.equal(new Set(found.portrait).size, found.portrait.length, "no source twice");

  const none = { portrait: found.portrait.length, hero: 0, logo: 0 };
  assert.deepEqual({ ...art.favoriteLayers(found, { portrait: 0, hero: 0, logo: 0 }) }, { kind: "portrait", src: found.portrait[0] });
  assert.deepEqual({ ...art.favoriteLayers(found, none) }, { kind: "fallback", hero: found.hero[0], logo: found.logo[0] }, "no portrait: hero and logo");
  const bare = art.favoriteLayers(found, { portrait: found.portrait.length, hero: found.hero.length, logo: found.logo.length });
  assert.equal(bare.kind, "fallback"); assert.equal(bare.hero, undefined); assert.equal(bare.logo, undefined);
  const source = readFileSync(new URL("../src/coreHome.tsx", import.meta.url), "utf8");
  assert.match(source, /layers\.logo \? <img[^>]*className="core-favorite-logo"[^>]*\/> : <span>\{app\.name\}<\/span>/, "with no logo, the name is drawn");
  assert.equal(JSON.stringify(art.favoriteArt(-1, undefined)), JSON.stringify({ portrait: [], hero: [], logo: [] }), "a bad id asks for nothing");
});
