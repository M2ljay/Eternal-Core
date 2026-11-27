import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: the dual-loader module lookups (src/steamModules.ts) against a fake Decky 3
// library (webpack exports only) and a fake Decky 4 library (exports plus module scopes).
function load() {
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL("../src/steamModules.ts", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  vm.runInNewContext(code, { exports, Proxy, Reflect, Map, Set, Array, Object });
  return exports;
}
const sm = load();

function decky3(modules) {
  const all = () => modules.flatMap(m => [m.default, m]).filter(Boolean);
  return {
    findModule: filter => all().find(m => filter(m)),
    findModuleExport: filter => { for (const m of all()) for (const k in m) if (m[k] && filter(m[k], k)) return m[k]; },
    findModuleByExport: filter => { for (const m of all()) for (const k in m) if (m[k] && filter(m[k], k)) return m; },
    findModuleChild: find => { for (const m of all()) { const hit = find(m); if (hit) return hit; } },
    classMap: [],
  };
}
function decky4(modules, scopes, classMap = []) {
  const ui = decky3(modules);
  const vars = () => scopes.flatMap(s => Object.entries(s));
  return {
    ...ui, classMap,
    moduleScopes: new Map(scopes.map((s, i) => [`chunk-${i}.js`, s])),
    findScope: filter => scopes.find(s => filter(s)),
    findVar: filter => vars().find(([k, v]) => v && filter(v, k))?.[1],
    findModuleScopeByVar: filter => scopes.find(s => Object.entries(s).some(([k, v]) => v && filter(v, k))),
  };
}

const store = { GetLocalScreensavers() {}, GetActiveState() {} };
const qam = { type: { toString: () => "function QuickAccessMenuBrowserView(){}" } };

test("Decky 3: the export search finds what it always found, and no scope search is attempted", () => {
  const ui = decky3([{ b3: store }]);
  assert.equal(sm.findExport(ui, v => typeof v?.GetLocalScreensavers === "function"), store);
  assert.equal(sm.findModule(ui, m => m?.b3 === store)?.b3, store);
  assert.equal(sm.findExport(ui, v => v === "missing"), undefined);
  assert.equal(sm.findModuleChild(ui, m => m?.b3), store);
});

test("Decky 4: a miss in the exports falls back to module scopes (findVar / findModuleScopeByVar / findScope)", () => {
  const scope = { a: store, r: qam, s: { logged_in: true, steamid: "76561190000000000" } };
  const ui = decky4([{ unrelated: 1 }], [scope]);
  assert.equal(sm.findExport(ui, v => typeof v?.GetLocalScreensavers === "function"), store);
  assert.equal(sm.findModuleByExport(ui, v => v?.type?.toString?.().includes("QuickAccessMenuBrowserView")), scope);
  assert.equal(sm.findModule(ui, m => m === scope), scope);
  assert.equal(sm.findModule(ui, m => m?.GetActiveState === store.GetActiveState), store, "a variable counts as a module");
  assert.equal(sm.findModuleChild(ui, m => Object.values(m).find(v => v?.logged_in === true)?.steamid), "76561190000000000");
});

test("an export hit wins over the scope search", () => {
  const exported = { GetLocalScreensavers() {}, GetActiveState() {} };
  const ui = decky4([{ b3: exported }], [{ a: store }]);
  assert.equal(sm.findExport(ui, v => typeof v?.GetLocalScreensavers === "function"), exported);
});

test("every lookup fails soft: throwing loaders, throwing filters and missing functions give undefined", () => {
  const boom = () => { throw new Error("boom"); };
  const broken = { findModule: boom, findModuleExport: boom, findModuleByExport: boom, findModuleChild: boom,
    findVar: boom, findScope: boom, findModuleScopeByVar: boom, get moduleScopes() { throw new Error("no"); } };
  for (const ui of [broken, {}, null, undefined, 42]) {
    assert.equal(sm.findExport(ui, () => true), undefined);
    assert.equal(sm.findModuleByExport(ui, () => true), undefined);
    assert.equal(sm.findModule(ui, () => true), undefined);
    assert.equal(sm.findModuleChild(ui, () => true), undefined);
    assert.equal(sm.findClassModule(ui, () => true), undefined);
  }
  const ui = decky4([], [{ a: 1 }]);
  assert.equal(sm.findModuleChild(ui, boom), undefined);
});

test("the wrapped library keeps every other member and routes the four lookups through the fallbacks", () => {
  const raw = { ...decky4([], [{ a: store }]), Router: { id: "router" }, Carousel: "carousel" };
  const ui = sm.withModuleFallbacks(raw);
  assert.equal(ui.Router, raw.Router);
  assert.equal(ui.Carousel, "carousel");
  assert.equal(ui.findModuleExport(v => typeof v?.GetActiveState === "function"), store);
  assert.equal(ui.findModuleByExport(v => v === store)?.a, store);
  assert.equal(ui.findModule(m => m?.a === store)?.a, store);
  assert.equal(ui.findModuleChild(m => m?.a), store);
  assert.equal(typeof ui.coreClassSelectors, "function");
  assert.equal(sm.withModuleFallbacks(null), null);
});

test("Steam's client kind: the chunk array means webpack, module scripts without it mean the ES-module client", () => {
  assert.equal(sm.steamClientKind({ webpackChunksteamui: [] }), "webpack");
  assert.equal(sm.steamClientKind({ document: { querySelectorAll: () => [{}] } }), "modules");
  assert.equal(sm.steamClientKind({ document: { querySelectorAll: () => [] } }), "unknown");
  assert.equal(sm.steamClientKind(undefined), "unknown");
  assert.equal(sm.steamClientKind({ get webpackChunksteamui() { throw new Error("x"); } }), "unknown");
});

test("[class*=] selectors gain the class map's hashed names on the new client and stay as written without one", () => {
  const css = '.a [class*="Field"]::after,.a [class*="Divider"]{display:none}';
  assert.equal(sm.withClassMapSelectors(decky3([]), css), css, "no class map: unchanged");
  const stable = decky3([]); stable.classMap = [{ Field: "gamepaddialog_Field_S-_La", FieldLabel: "gamepaddialog_FieldLabel_3b0U" }];
  assert.equal(sm.withClassMapSelectors(stable, css), css, "readable stable names already match the attribute selector");
  const fresh = decky4([], [], [{ Field: "x1Ab2", FieldLabel: "q9Zz0 extra7" }, { Divider: "d0d0" }, { Other: "nope" }]);
  const out = sm.withClassMapSelectors(fresh, css);
  assert.equal(out, '.a :is([class*="Field"],.x1Ab2,.q9Zz0,.extra7)::after,.a :is([class*="Divider"],.d0d0){display:none}');
  const huge = decky4([], [], [Object.fromEntries(Array.from({ length: 400 }, (_, i) => [`Field${i}`, `h${i}`]))]);
  assert.equal(sm.withClassMapSelectors(huge, css).includes(".h1,"), false, "an over-broad fragment stays attribute-only");
  const ui = sm.withModuleFallbacks(fresh);
  assert.equal(ui.coreClassSelectors(css), out);
  assert.equal(ui.coreClassSelectors(css), out, "cached result is the same");
  assert.equal(sm.findClassModule(fresh, m => "Divider" in m)?.Divider, "d0d0");
});

test("call sites use the class-map method optionally, so a fake library keeps the CSS as written", () => {
  for (const file of ["ControlCenter.tsx", "DashboardPage.tsx", "PluginStorePage.tsx", "StoreDigitalStyles.tsx",
    "quickSettings/index.tsx", "quickSettings/losslessProfiles.tsx"]) {
    const source = readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
    assert.match(source, /coreClassSelectors\?\.\(/, `${file} maps its [class*=] selectors`);
  }
  const decky = readFileSync(new URL("../src/decky.ts", import.meta.url), "utf8");
  assert.match(decky, /withModuleFallbacks\(DeckyUI\)/);
  assert.doesNotMatch(decky, /const DFL\b/, "no local named DFL: the bundle maps @decky/ui to that global");
});
