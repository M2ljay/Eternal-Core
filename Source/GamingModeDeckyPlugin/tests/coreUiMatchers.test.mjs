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
  assert.equal(Object.keys(found.candidates).length, 0);
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
