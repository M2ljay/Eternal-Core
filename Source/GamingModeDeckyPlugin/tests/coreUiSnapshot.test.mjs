import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: Core UI 3.3.4 (docs/eternal/CORE-UI.md): the snapshot (pruning, the size cap, when it runs), the
// selector table, rail icon sizing, the Jarvis brief's data, the Library tab and Quick Access row builders, and the
// guard that switches CSS Loader's legacy Core themes off while Core UI is on.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const local = { "./steamModules": "steamModules.ts", "./coreUiStyles": "coreUiStyles.ts", "./coreUiDom": "coreUiDom.ts", "./coreFullHeight": "coreFullHeight.ts",
  "./coreArtwork": "coreArtwork.ts", "./coreStarfield": "coreStarfield.ts", "./coreUiSetting": "coreUiSetting.ts", "./coreInputPrompts": "coreInputPrompts.ts",
  "./coreHomeLocale": "coreHomeLocale.ts", "./coreBriefLocale": "coreBriefLocale.ts" };
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
const rail = load("coreRailIcons.ts");
const artwork = load("coreArtwork.ts");
const brief = load("coreBrief.ts");
const briefLocale = load("coreBriefLocale.ts");
const legacy = load("coreCssLoader.ts");
const starfield = load("coreStarfield.ts");
const runtime = load("coreUi.ts");

const maps = {
  mainmenu: { MainMenuEmbedded: "_MmEmbed", Menu: "_Mm", Item: "_MmItem", ItemIcon: "_MmIcon", ItemLabel: "_MmLabel", ActiveDot: "_MmDot" },
  quickaccess: { Menu: "_QaMenu", Container: "_QaCont", QuickAccessMenu: "_QaQam", Title: "_QaTitle", Tab: "_QaTab", Tabs: "_QaTabs", Selected: "_QaSel",
    TabGroupPanel: "_QaTgp", TabContentColumn: "_QaTcc" },
  tabbedpage: { TabRow: "_TpRow", TabTitle: "_TpTitle", TabHeaderRowWrapper: "_TpWrap", Tab: "_TpTab", Selected: "_TpSel" },
  library: { GamepadLibrary: "_LbLib", CollectionHeader: "_LbHead" },
  portrait: { LibraryItemBox: "_PtBox" },
  gamepaddialog: { Field: "_GdField", FieldLabel: "_GdLabel", FieldChildren: "_GdKids", FieldDescription: "_GdDesc" },
};

// ---- A small fake DOM ----
function el(tag, { cls = [], data = {}, rect = [0, 0, 100, 100], style = {}, text = "", aria, children = [] } = {}) {
  const node = {
    tagName: tag.toUpperCase(), classList: cls, children, parentElement: null,
    attributes: Object.keys(data).map(name => ({ name: `data-${name}` })),
    childNodes: text ? [{ nodeType: 3, textContent: text }] : [],
    style,
    getBoundingClientRect: () => ({ left: rect[0], top: rect[1], width: rect[2], height: rect[3] }),
    getAttribute: name => (name === "aria-label" ? aria ?? null : null),
    matches(selector) { return selector.split(",").some(part => { const want = [...part.trim().matchAll(/\.([\w-]+)/g)].map(m => m[1]); return want.length > 0 && want.every(c => cls.includes(c)); }); },
  };
  for (const child of children) child.parentElement = node;
  return node;
}
const view = { innerWidth: 1920, innerHeight: 1080, devicePixelRatio: 2, getComputedStyle: element => element.style };
function docWith(map, extra = {}) {
  return { querySelectorAll: selector => map[selector] ?? [], defaultView: view, head: { querySelectorAll: () => [] }, location: { pathname: "/routes/home" }, title: "SP", ...extra };
}

test("selector table: templates resolve through the class maps; a missing map drops only that strategy", () => {
  assert.equal(styles.resolveTemplate(maps, "{quickaccess.Tab}{quickaccess.Selected}", true), "._QaTab._QaSel");
  assert.equal(styles.resolveTemplate(maps, "{quickaccess.Tab}{quickaccess.Selected}"), "._QaTab._QaTab:not(#core-ui-boost)._QaSel._QaSel:not(#core-ui-boost)");
  assert.equal(styles.resolveTemplate(maps, "{appgrid.AppGrid}"), undefined, "missing map: strategy dropped");
  assert.deepEqual([...styles.targetSelectors(maps, "qamTabSelected", true)], ["._QaTab._QaSel", '._QaTab[aria-selected="true"]', "._QaTab.Selected", "._QaTab.Active"]);
  assert.deepEqual([...styles.targetSelectors(maps, "libraryGrid", true)], ["._LbLib"], "falls back to the next strategy");
  const table = styles.resolvedTargetTable(maps);
  assert.ok(table.qamRail.some(row => row.selector === "._QaTabs"));
  assert.ok(table.libraryGrid.some(row => row.selector === undefined), "unresolved strategies are listed too");
  for (const [id, list] of Object.entries(styles.CORE_UI_TARGETS)) assert.ok(list.length > 0, id);
  // Every surface the snapshot records names targets that exist in the table.
  for (const surface of snap.SNAPSHOT_SURFACES) for (const target of surface.targets) assert.ok(styles.CORE_UI_TARGETS[target], `${surface.id}: ${target}`);
});

test("class maps: how each was found is recorded; the boost gives every class target an id's weight", () => {
  const ui = { findClassModule: f => Object.values(maps).find(m => f(m)) };
  const found = styles.findCoreUiMapsDetailed(ui);
  assert.equal(found.routes.mainmenu, "classic");
  assert.equal(found.routes.contextmenu, "missing");
  // Decky 4 (findVar): found through the class-map search's module walk or findVar itself.
  const four = styles.findCoreUiMapsDetailed({ findVar: f => Object.values(maps).find(m => f(m)) });
  assert.ok(["classic", "findVar"].includes(four.routes.quickaccess), four.routes.quickaccess);
  assert.equal(styles.CORE_UI_BOOST, ":not(#core-ui-boost)");
  const sheet = styles.buildCoreUiCss(found.maps);
  assert.ok(sheet.ruleTargets["menu-panel"].includes("._Mm._Mm:not(#core-ui-boost)"), "rule targets for the snapshot");
  assert.ok(!Object.values(sheet.ruleTargets).some(sel => /::(before|after)|:hover|\.gpfocus/.test(sel)), "pseudo parts removed");
});

test("snapshot: a pruned tree (depth, node cap, labels at most 30 characters, cover flag, rule matches)", () => {
  const deep = el("div", { cls: ["leaf"] });
  let chain = deep;
  for (let i = 0; i < 9; i++) chain = el("div", { cls: [`d${i}`], children: [chain] });
  const big = el("div", { cls: ["layer"], rect: [0, 0, 400, 800], style: { backgroundColor: "rgb(35, 38, 46)", backgroundImage: "linear-gradient(red, blue)", opacity: "1" } });
  const row = el("div", { cls: ["row"], text: "A label that is far longer than thirty characters", data: { "core-tab": "x" }, rect: [0, 0, 400, 48] });
  const many = Array.from({ length: 50 }, (_, i) => el("span", { cls: [`n${i}`], rect: [0, i, 10, 10] }));
  const root = el("div", { cls: ["root"], rect: [0, 0, 400, 1080], children: [big, row, chain, ...many], style: { backgroundColor: "rgb(0, 0, 0)", position: "relative" } });
  const { tree, nodes, truncated } = snap.captureTree(root, view, { nodes: 30, labels: true, rules: { "qam-clear": ".layer", "row-rule": ".row" } });
  assert.equal(nodes, 30); assert.equal(truncated, true);
  assert.equal(tree.t, "div"); assert.deepEqual([...tree.c], ["root"]); assert.deepEqual([...tree.r], [0, 0, 400, 1080]);
  assert.equal(tree.s.pos, "relative"); assert.equal(tree.s.bg, "rgb(0, 0, 0)");
  const layer = tree.k[0];
  assert.equal(layer.cover, 1, "opaque and over 30% of the surface");
  assert.equal(layer.s.img, "linear-gradient", "image type only");
  assert.deepEqual([...layer.m], ["qam-clear"]);
  const label = tree.k[1];
  assert.equal(label.l.length, 30); assert.deepEqual([...label.d], ["data-core-tab"]); assert.deepEqual([...label.m], ["row-rule"]);
  assert.ok(tree.more > 0, "children past the cap are counted");
  // Depth: the chain stops at SNAPSHOT_DEPTH levels.
  const depthOnly = snap.captureTree(el("div", { children: [chain] }), view, { nodes: 400 });
  let depth = 0, node = depthOnly.tree;
  while (node.k) { node = node.k[0]; depth++; }
  assert.equal(depth, snap.SNAPSHOT_DEPTH - 1); assert.ok(node.more >= 1);
  // No labels where they would be game names.
  assert.equal(snap.captureTree(row, view, { labels: false }).tree.l, undefined);
  assert.equal(snap.imageType('url("https://x/y.png?token=1")'), "url", "never the address");
  assert.equal(snap.imageType("none"), "none");
});

test("snapshot: surfaces found through the table, keyed per Quick Access tab, with documents and screen", () => {
  const tabs = [el("div", { cls: ["_QaTab"], aria: "Core" }), el("div", { cls: ["_QaTab", "_QaSel"], aria: "Jarvis" })];
  const content = el("div", { cls: ["_QaTcc"], rect: [1520, 0, 400, 1000] });
  const menu = el("div", { cls: ["_Mm"], rect: [0, 0, 0, 0] });
  const popup = docWith({ "._QaTcc": [content], "._QaTab": tabs, "._QaTab._QaSel": [tabs[1]] });
  const main = docWith({ "._Mm": [menu] });
  const found = snap.findSurfaces([main, popup], maps);
  assert.deepEqual([...found.map(f => f.key)], ["qam-content:1:Jarvis"], "hidden menu skipped; content keyed by the open tab");
  assert.equal(found[0].docIndex, 1);
  assert.match(found[0].via, /^qamContent: \._QaTcc$/);
  const record = snap.recordSurface(found[0], view, {}, 50, "2026-11-27T00:00:00.000Z");
  assert.equal(record.doc, 1); assert.deepEqual([...record.rect], [1520, 0, 400, 1000]);
  const out = JSON.parse(snap.assembleSnapshot({ reason: "manual", version: "", now: new Date(0), docs: [main, popup], main, styleId: "core-ui-style",
    sheet: { applied: ["a"], skipped: ["b"], routes: { mainmenu: "classic" }, maps }, surfaces: new Map([[record.key, record]]), extra: { icons: { tabs: [] } } }).json);
  assert.equal(out.kind, "core-ui-snapshot"); assert.equal(out.reason, "manual");
  assert.deepEqual(out.screen, { width: 1920, height: 1080, dpr: 2, screenWidth: 0, screenHeight: 0 });
  assert.equal(out.documents.length, 2); assert.equal(out.documents[0].main, true); assert.equal(out.documents[1].main, false);
  assert.deepEqual(out.coreUi.mapRoutes, { mainmenu: "classic" });
  assert.ok(out.coreUi.selectors.qamTab);
  assert.equal(out.surfaces[0].key, "qam-content:1:Jarvis");
  assert.ok(out.icons);
});

test("snapshot (3.3.7): every host with its selector and source; The Collector's ROM page as a surface, without labels", () => {
  const sheet = styles.buildCoreUiCss(maps);
  const out = JSON.parse(snap.assembleSnapshot({ reason: "manual", version: "", now: new Date(0), docs: [docWith({})], main: undefined, styleId: "core-ui-style",
    sheet, surfaces: new Map() }).json);
  const collector = out.coreUi.hosts.find(host => host.id === "collector-rom-page");
  assert.deepEqual(collector, { id: "collector-rom-page", selector: ".romm-core-page", source: "plugin", kind: "page" });
  assert.deepEqual(out.coreUi.hosts.find(host => host.id === "menu"), { id: "menu", selector: "._Mm", source: "steam", kind: "panel" });
  assert.deepEqual(out.coreUi.selectors.collectorRomPage, [{ strategy: ".romm-core-page", selector: ".romm-core-page" }]);
  assert.equal(snap.hostList(undefined).length, 0);

  const page = el("div", { cls: ["_Overview", "romm-core-page"], rect: [0, 560, 1920, 1800], children: [el("div", { cls: ["romm-core-title"], text: "A Game Name", rect: [0, 600, 900, 60] })] });
  const found = snap.findSurfaces([docWith({ ".romm-core-page": [page] })], maps);
  assert.deepEqual([...found.map(f => f.key)], ["collector-rom-page"]);
  const record = snap.recordSurface(found[0], view, {}, 50, "2026-11-27T00:00:00.000Z");
  assert.ok(!JSON.stringify(record).includes("A Game Name"), "no game names in the snapshot");
});

test("snapshot: held under 2 MB (recaptured smaller, then the largest trees dropped)", () => {
  const long = Array.from({ length: 12 }, (_, i) => `class-${"x".repeat(50)}-${i}`);
  const bigTree = nodes => ({ t: "div", r: [0, 0, 1, 1], k: Array.from({ length: nodes }, () => ({ t: "div", c: long, r: [0, 0, 1, 1] })) });
  const surfaces = nodes => new Map(Array.from({ length: 12 }, (_, i) => [`s${i}`, { id: `s${i}`, key: `s${i}`, doc: 0, via: "", at: "", rect: [0, 0, 1, 1], nodes, truncated: false, tree: bigTree(nodes), ancestors: [] }]));
  const input = { reason: "start", version: "", now: new Date(0), docs: [docWith({})], main: null, styleId: "x", surfaces: surfaces(400) };
  let recaptures = 0;
  const result = snap.assembleSnapshot(input, nodes => { recaptures++; return surfaces(nodes); });
  assert.ok(result.bytes <= snap.SNAPSHOT_MAX_BYTES, `${result.bytes}`);
  assert.ok(recaptures >= 1);
  assert.equal(JSON.parse(result.json).surfaces.length, 12, "smaller trees kept every surface");
  const dropped = snap.assembleSnapshot(input);
  assert.ok(dropped.bytes <= snap.SNAPSHOT_MAX_BYTES);
  assert.ok(dropped.dropped.length > 0, "without recapture the largest are dropped and named");
  assert.deepEqual([...JSON.parse(dropped.json).dropped], [...dropped.dropped]);
  assert.equal(snap.utf8Length("é€😀"), 2 + 3 + 4);
});

test("snapshot: first capture a few seconds after Core UI applies, then once per newly opened surface", async () => {
  let now = 0;
  const writes = [];
  const tab = el("div", { cls: ["_TpRow"], rect: [0, 0, 1920, 60] });
  const docs = [docWith({})];
  const probe = snap.createSnapshotProbe({ now: () => now, version: "", styleId: "x", main: () => docs[0], sheet: () => ({ maps, ruleTargets: {} }),
    write: async json => { writes.push(JSON.parse(json)); return { ok: true, path: "C:/Users/u/AppData/Roaming/GamingMode/diagnostics/core-ui-snapshot.json" }; } });
  probe.tick(docs);
  assert.equal(writes.length, 0);
  now = 5000; probe.tick(docs); await Promise.resolve(); await Promise.resolve();
  assert.equal(writes.length, 1); assert.equal(writes[0].reason, "start");
  docs[0] = docWith({ "._TpRow": [tab] });
  now = 6000; probe.tick(docs);
  now = 7000; probe.tick(docs); await Promise.resolve();
  assert.equal(writes.length, 1, "settles first, and at most one write every 3 seconds");
  now = 9000; probe.tick(docs); await Promise.resolve(); await Promise.resolve();
  assert.equal(writes.length, 2); assert.match(writes[1].reason, /opened: library-tabs/);
  assert.deepEqual(writes[1].surfaces.map(s => s.key), ["library-tabs"]);
  now = 20000; probe.tick(docs); await Promise.resolve();
  assert.equal(writes.length, 2, "a surface seen before does not trigger again");
  const manual = await probe.saveNow(docs);
  assert.equal(manual.ok, true); assert.equal(writes.length, 3); assert.equal(writes[2].reason, "manual");
});

test("stylesheet order: Core UI's stays after CSS Loader's", () => {
  const style = { tagName: "STYLE" };
  const later = { tagName: "STYLE", nextElementSibling: null };
  const script = { tagName: "SCRIPT", nextElementSibling: null };
  style.nextElementSibling = script;
  assert.equal(runtime.laterStylesheet(style), false);
  script.nextElementSibling = later;
  assert.equal(runtime.laterStylesheet(style), true);
  const link = { tagName: "LINK", getAttribute: () => "stylesheet", nextElementSibling: null };
  assert.equal(runtime.laterStylesheet({ nextElementSibling: link }), true);
});

// 3.3.10 (TV photo: plugin icons sat ~17px high in the 48 x 64 tab, off the selected fill): every rail icon is 24px,
// Steam's own icon size, centred both ways in a flex tab.
test("rail icons: 24px (Steam's own) with Core UI on, 1em off; glyphs are single-colour and fill their box", () => {
  assert.equal(rail.CORE_RAIL_ICON_PX, 24);
  const on = rail.railIconBox(true), off = rail.railIconBox(false);
  assert.equal(on.width, 24); assert.equal(on.height, 24); assert.equal(on.fontSize, 24, "icons drawn at 1em fill it");
  assert.equal(off.width, "1em"); assert.equal(off.fontSize, undefined);
  for (const [name, svg] of Object.entries(artwork.CORE_PLUGIN_GLYPHS)) {
    assert.doesNotMatch(svg, /#000|black/i, `${name}: cut-outs are holes, not black paint`);
    const [, , w, h] = /viewBox="([\d.-]+) ([\d.-]+) ([\d.]+) ([\d.]+)"/.exec(svg).slice(1).map(Number);
    assert.equal(w, h, `${name}: square box`);
    for (const [, r] of svg.matchAll(/ r="([\d.]+)"/g)) assert.ok(Number(r) * 24 / w >= 0.8, `${name}: dots at least 0.8px at 24px (r=${r})`);
    for (const [, sw] of svg.matchAll(/stroke-width="([\d.]+)"/g)) assert.ok(Number(sw) * 24 / w >= 0.96, `${name}: strokes at least about 1px at 24px`);
    const html = rail.railGlyphHtml(svg);
    assert.match(html, /^<svg width="100%" height="100%" shape-rendering="geometricPrecision" aria-hidden="true" focusable="false" /);
  }
  assert.equal(rail.railGlyphHtml("<img onerror=x>"), "", "only SVG markup");
  const css = styles.buildCoreUiCss(maps).css;
  const part = css.split("\n").find(line => line.startsWith("/* qam-rail-icons */"));
  assert.ok(part, "the rule applies");
  const blocks = part.slice("/* qam-rail-icons */".length).split("}").filter(Boolean).map(block => block.split("{"));
  const tab = blocks.find(([sel]) => /^html\[data-core-ui\] \._QaTab\._QaTab:not\(#core-ui-boost\)$/.test(sel.trim()));
  assert.ok(tab, "a rule on the tab itself");
  for (const want of ["display:flex", "align-items:center", "justify-content:center", "padding:0", "border-radius:0"]) assert.match(tab[1], new RegExp(want + " !important"), `the tab: ${want}`);
  const icons = blocks.find(([sel]) => sel.includes("> svg") && sel.includes(":has(> :is(svg,img))") && sel.includes("[data-core-rail-icon]"));
  assert.ok(icons, "Steam's svg, a plugin's span and Core's glyph share one box");
  for (const want of ["width:24px", "height:24px", "margin:0", "position:static"]) assert.match(icons[1], new RegExp(want + " !important"), `icons: ${want}`);
  assert.doesNotMatch(part, /30px/);
});

test("Library tabs: icon and label on one line, ONE marker under the label (tone on hover or focus, crimson when open)", () => {
  const css = styles.libraryTabCss({ scopes: ["html[data-core-ui] ._L"], tab: ["._T"], selected: ["._T._S"], title: ["._Ti"], row: ["._R"] });
  const blocks = css.split("}").filter(Boolean);
  const markers = blocks.filter(block => /::after\{[^]*content:""/.test(block));
  assert.equal(markers.length, 1, "one marker rule");
  assert.match(markers[0], /^html\[data-core-ui\] ._L ._T ._Ti::after\{/, "under the label, not the icon");
  assert.match(markers[0], /width:28px !important;height:3px[^]*opacity:0 !important/, "hidden until hover, focus or open");
  assert.match(css, /._T:is\(:hover,.gpfocus,:focus-visible\) ._Ti::after\{opacity:1 !important\}/);
  assert.match(css, /._T._S ._Ti::after\{opacity:1 !important;background-color:#D7263D !important\}/, "open tab: crimson");
  assert.match(css, /._L ._T._S\{background-color:transparent !important;background-image:none !important;color:#C9D1D9/, "open tab: transparent, steel text (no grey-blue)");
  assert.match(css, /._L ._T::before,html\[data-core-ui\] ._L ._T::after\{content:none !important;display:none !important\}/, "no other underline on the tab");
  assert.match(css, /._T\[data-core-tab\]\[data-core-icon\] ._Ti::before\{content:"" !important;display:inline-block !important;flex:0 0 24px !important;width:24px !important;height:24px/, "24px icon");
  assert.match(css, /._L ._T ._Ti\{position:relative !important;display:inline-flex !important;align-items:center !important;gap:8px/, "one baseline, even spacing");
  assert.doesNotMatch(css, /inset 0 -3px 0/, "no full-width underline");
  assert.equal(styles.libraryTabCss({ scopes: [], tab: ["._T"] }), "");
});

test("Quick Access rows, collection tiles and capsules", () => {
  const rows = styles.qamRowCss(["._Q .DialogButton"], ["._Q ._F"]);
  assert.match(rows, /._Q .DialogButton\{background-color:#1A1D21 !important;background-image:none !important;color:#C9D1D9/, "gunmetal rows, steel text");
  assert.match(rows, /:is\(.gpfocus,:focus-visible,:focus\)\{background-color:#2A2F36 !important;background-image:none !important;color:#FFFFFF !important;box-shadow:inset 3px 0 0 #D7263D/, "focus: square crimson left bar, never white");
  assert.match(rows, /._Q ._F,[^{]*::before,[^{]*::after\{background-color:transparent/);
  const tiles = styles.collectionTileCss();
  assert.match(tiles, /\[data-core-collection\]\[data-core-icon\]:not\(#core-ui-boost\)::before\{[^}]*left:50% !important;top:18%/, "icon centred");
  assert.match(tiles, /\[data-core-name\]::after\{content:attr\(data-core-name\)/, "name below");
  assert.match(tiles, /\[data-core-tone\]\{box-shadow:inset 0 -3px 0 var\(--core-tone\)/, "tone bar along the bottom");
  assert.match(tiles, /min-height:160px/, "one size");
  const capsules = styles.libraryCapsuleCss(["._Lib ._Box"]);
  assert.match(capsules, /scale:0.84 !important/);
  assert.ok(styles.CORE_LIBRARY_CAPSULE_SCALE >= 0.8 && styles.CORE_LIBRARY_CAPSULE_SCALE <= 0.85, "15-20% smaller");
});

test("Starfield: denser on large pages, a few near stars, never brighter than the readable ceiling", () => {
  assert.equal(starfield.starsForArea(400, 758), 160, "the Steam menu keeps its stars at the TV's 1348 x 758 viewport");
  assert.equal(starfield.starsForArea(1348, 758), starfield.STARFIELD_MAX_COUNT, "a full page at the TV's viewport gets the maximum");
  assert.ok(starfield.starsForArea(1920, 1080) > 300, "a full page gets more");
  assert.equal(starfield.starsForArea(3840, 2160), starfield.STARFIELD_MAX_COUNT);
  assert.equal(starfield.starsForArea(100, 100), starfield.STARFIELD_MIN_COUNT);
  const fills = [];
  const context = { clearRect() {}, beginPath() {}, fill() {}, arc(x, y, r) { fills.push(r); }, set fillStyle(v) { fills.push(v); } };
  const field = starfield.createStarfield();
  const small = field.draw(context, 400, 1080);
  const large = field.draw(context, 1920, 1080);
  assert.ok(large > small, `${large} > ${small}`);
  for (const value of fills.filter(v => typeof v === "string")) assert.ok(Number(/,([\d.]+)\)$/.exec(value)[1]) <= starfield.STARFIELD_MAX_ALPHA + 1e-9, value);
  assert.ok(fills.some(v => typeof v === "number" && v >= 1.3), "near stars are larger");
  assert.ok(starfield.worstStarContrast("#9AA4AE") >= 4.5);
});

test("Today (the brief): greeting, date and time, weather, Continue, the Club's pick and the chips, from fakes", () => {
  const now = new Date(2026, 10, 27, 21, 5);
  const model = brief.assembleBrief({ now, locale: "en", weather: { enabled: true, iconText: "☀", tempText: "18°" },
    last: { appid: 1245620, name: "Elden Ring", minutes: 144, hero: ["a.jpg", "", "b.jpg"] },
    club: { title: "Chrono Trigger", endsAt: now.getTime() / 1000 + 2.5 * 86400, art: "art.jpg", appId: 7 },
    steamUpdate: { holdPhase: "held" }, downloads: 2, mods: 1, achievements: 3 });
  assert.equal(model.greeting, "Good evening");
  assert.match(model.date, /Friday/); assert.match(model.time, /9:05/);
  assert.equal(model.weather, "☀ 18°");
  assert.deepEqual({ ...model.continue, hero: [...model.continue.hero] }, { appid: 1245620, name: "Elden Ring", played: "2.4 hours played", hero: ["a.jpg", "b.jpg"] });
  assert.equal(model.club.title, "Chrono Trigger"); assert.equal(model.club.left, "2 days left"); assert.equal(model.club.label, "Club pick");
  assert.deepEqual(JSON.parse(JSON.stringify(model.chips.map(c => [c.id, c.text, c.tone]))), [["steam-update", "Steam update waiting", "amber"], ["downloads", "2 downloading", "steel"], ["mods", "1 mod update", "steel"], ["achievements", "3 nearly done", "green"]]);
  const empty = brief.assembleBrief({ now: new Date(2026, 10, 27, 3, 0), locale: "it", weather: { enabled: false, tempText: "18°" } });
  assert.equal(empty.greeting, "Ancora sveglio"); assert.equal(empty.weather, undefined); assert.equal(empty.continue, undefined); assert.equal(empty.club, undefined);
  assert.deepEqual([...empty.chips], []);
  const t = briefLocale.coreBriefCopy("en");
  assert.equal(brief.playedText(0, t), "Not played yet"); assert.equal(brief.playedText(42, t), "42 minutes played");
  assert.equal(brief.playedText(60, t), "1 hour played"); assert.equal(brief.playedText(60 * 37, t), "37 hours played");
  assert.equal(brief.leftText(now.getTime() / 1000 + 3600, now.getTime(), t), "Last day");
  // The Club's pick: the one ending soonest that is not beaten, with art from the library match.
  const pick = brief.clubPickFor({ picks: [{ title: "A", slug: "a", ends_at: 9e9 }, { title: "B", slug: "b", ends_at: 5e9 }, { title: "C", slug: "c", ends_at: 4e9 }],
    member: { beaten_slugs: ["c"] } }, 0, p => ({ art: `${p.title}.jpg`, appId: 3 }));
  assert.deepEqual(JSON.parse(JSON.stringify(pick)), { title: "B", endsAt: 5e9, art: "B.jpg", appId: 3 });
  assert.equal(brief.clubPickFor(null, 0), undefined);
  assert.equal(brief.downloadCount({ downloadsStore: { m_DownloadItems: [{ completed: false }, { completed: true }, {}] } }), 2);
  assert.equal(brief.downloadCount({}), 0);
  assert.equal(brief.weatherLine({ enabled: true, iconText: "x".repeat(80) }).length, 40);
  const store = { GetAppOverviewByAppID: id => ({ appid: id, minutes_playtime_forever: 90 }), GetCustomHeroImageURLs: () => ["custom.png"] };
  assert.deepEqual([...brief.heroSources(5, store)].slice(0, 2), ["custom.png", "https://steamloopback.host/assets/5/library_hero.jpg"]);
  assert.equal(brief.minutesPlayed(5, store), 90);
  assert.equal(briefLocale.coreBriefLanguages.length, 12);
  const keys = Object.keys(t).sort();
  for (const language of briefLocale.coreBriefLanguages) assert.deepEqual(Object.keys(briefLocale.coreBriefCopy(language)).sort(), keys, language);
});

test("CSS Loader: the legacy Core themes go off through CSS Loader's own call, else a reload; restore puts back only those", async () => {
  assert.equal(legacy.cssLoaderName({ plugins: [{ name: "Discord" }, { name: "CSS Loader" }] }), "CSS Loader");
  assert.equal(legacy.cssLoaderName({ plugins: [] }), undefined);
  assert.deepEqual([...legacy.LEGACY_CORE_THEMES], ["Core", "Core Accents"]);
  const calls = [];
  const bridge = (fail = {}) => ({ name: () => "CSS Loader", call: async (route, ...args) => { calls.push([route, ...args]); if (fail[route]) throw new Error("no"); return { success: true }; } });
  assert.equal(await legacy.applyInCssLoader(bridge(), ["Core"], false), "api");
  assert.deepEqual(calls.pop(), ["loader/call_plugin_method", "CSS Loader", "set_theme_state", "Core", false, false, false]);
  assert.equal(await legacy.applyInCssLoader(bridge({ "loader/call_plugin_method": true }), ["Core"], false), "legacy");
  assert.equal(await legacy.applyInCssLoader(bridge({ "loader/call_plugin_method": true, "loader/call_legacy_plugin_method": true }), ["Core"], false), "reload");
  assert.deepEqual(calls.pop(), ["loader/reload_plugin", "CSS Loader"]);
  assert.equal(await legacy.applyInCssLoader({ name: () => undefined, call: async () => {} }, ["Core"], false), undefined, "no CSS Loader: nothing to tell");
  const actions = [];
  const guard = legacy.createLegacyThemeGuard({
    backend: async action => { actions.push(action); return action === "disable" ? { ok: true, changed: ["Core"], themes: [{ name: "Core", active: false }, { name: "CapyMenu (Context Menus)", active: true }] } : { ok: true, restored: ["Core"] }; },
    bridge: bridge(), now: () => 1,
  });
  const off = await guard.disable();
  assert.deepEqual([...off.changed], ["Core"]); assert.equal(off.route, "api");
  assert.equal(off.themes[1].active, true, "the user's other themes are left alone");
  const on = await guard.restore();
  assert.deepEqual([...on.restored], ["Core"]); assert.deepEqual(actions, ["disable", "restore"]);
  assert.deepEqual(calls.slice(-1)[0].slice(0, 5), ["loader/call_plugin_method", "CSS Loader", "set_theme_state", "Core", true]);
});
