import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: Jarvis in Core UI phase 1 (docs/eternal/CORE-UI.md, docs/eternal/JARVIS.md): the talk combo's
// tap/hold split, opening and closing Jarvis's Quick Access tab, the tab itself next to Core's, the presence orb's
// states and the tab's text in 12 languages.

const compile = file => ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const load = (file, context = {}) => { const exports = {}; vm.runInNewContext(compile(file), { exports, Date, Set, Map, Number, Math, ...context }); return exports; };
const combos = load("jarvisCombos.ts");
const presence = load("jarvisPresence.ts");
const tabLocale = load("jarvisTabLocale.ts");
const plain = value => JSON.parse(JSON.stringify(value));

// L5+R5 (Jarvis's only combo since 3.3.5) as Steam reports it; L4+R4 are A and B and fire nothing.
const L5R5 = { lower: (1 << 15 | 1 << 16) >>> 0, upper: 0 };
const L4R4 = { lower: 0, upper: (1 << 9 | 1 << 10) >>> 0 };

function watch({ tapOpens = true } = {}) {
  let callback, clock = 1000;
  const events = [];
  const stop = combos.installJarvisCombos({
    input: () => ({ RegisterForControllerStateChanges: fn => { callback = fn; return { unregister() {} }; } }),
    combos: () => ({ dictate: "L4+R4", talk: "L5+R5", screen: "L4+L5" }), // old settings: the paddle combos no longer parse
    onPress: kind => events.push(["press", kind, clock]),
    onRelease: (kind, held) => events.push([combos.jarvisRelease(kind, held, tapOpens), kind, held]),
    now: () => clock,
  });
  const press = buttons => callback([{ unControllerIndex: 0, ulButtons: buttons.lower, ulUpperButtons: buttons.upper }]);
  const release = () => callback([{ unControllerIndex: 0, ulButtons: 0, ulUpperButtons: 0 }]);
  return { events, press, release, advance: ms => { clock += ms; }, stop };
}

test("L5+R5: a tap talks, a hold opens Jarvis; the paddles L4/R4 fire nothing (3.3.5)", () => {
  assert.equal(combos.JARVIS_TAP_MS, 300);
  const w = watch();
  w.press(L5R5); w.advance(120); w.release();
  assert.deepEqual(plain(w.events), [["press", "talk", 1000], ["talk", "talk", 120]], "a 120 ms tap talks");
  w.events.length = 0;
  w.press(L5R5); w.advance(300); w.release();
  assert.deepEqual(plain(w.events.map(event => event[0])), ["press", "open"], "300 ms or more opens Jarvis");
  w.events.length = 0;
  w.press(L4R4); w.advance(50); w.release();
  assert.deepEqual(plain(w.events), [], "L4+R4 are A and B: no combo fires");
  w.events.length = 0;
  w.press(L5R5); w.advance(50); w.stop.stop();
  assert.equal(w.events[1][0], "release", "stopping the watch never talks");

  const off = watch({ tapOpens: false });
  off.press(L5R5); off.advance(80); off.release();
  assert.equal(off.events[1][0], "talk", "with the desktop toggle off a tap still talks");
  off.events.length = 0;
  off.press(L5R5); off.advance(1500); off.release();
  assert.equal(off.events[1][0], "release", "and a hold is push-to-talk");
  assert.equal(combos.jarvisRelease("talk", Infinity, true), "release");
  assert.equal(combos.jarvisRelease("talk", -5, true), "release");
  assert.equal(combos.jarvisRelease("screen", 10, true), "release");
});

test("defaults since 3.3.5: only L5+R5; dictation and the screen helper have no combo (C# and TypeScript agree)", () => {
  const core = readFileSync(new URL("../../Shared/JarvisCore.cs", import.meta.url), "utf8");
  assert.match(core, /DefaultDictateCombo = ""/);
  assert.match(core, /DefaultTalkCombo = "L5\+R5"/);
  assert.match(core, /DefaultScreenCombo = ""/);
  assert.match(core, /Reserved = \{ "STEAM", "GUIDE", "QAM", "HOME", "L4", "R4" \}/);
  for (const file of ["../../GamingModeAgent/GamingMode.Models/GamingSettings.cs", "../../Playhub/Models/GamingModeConfig.cs"]) {
    const source = readFileSync(new URL(file, import.meta.url), "utf8");
    assert.match(source, /JarvisDictateCombo \{ get; set; \} = ""/, file);
    assert.match(source, /JarvisScreenCombo \{ get; set; \} = ""/, file);
    assert.match(source, /JarvisTalkCombo \{ get; set; \} = "L5\+R5"/, file);
    assert.match(source, /JarvisTapOpensQuickAccess \{ get; set; \} = true/, file);
  }
  assert.match(readFileSync(new URL("../../GamingModeAgent/GamingMode.Services/MarkIIIHost.Jarvis.cs", import.meta.url), "utf8"), /tapOpens = settings\.TapOpens/);
  assert.match(readFileSync(new URL("../../Playhub/MainWindow.Jarvis.cs", import.meta.url), "utf8"), /"jarvisTapOpens"/);
});

test("a tap opens Quick Access on Jarvis's tab with the input focused, and closes it when it is open there", () => {
  const calls = [];
  let side = 0, tab = 0;
  const store = { GetOpenSideMenu: () => side, GetQuickAccessTab: () => tab,
    OpenQuickAccessMenu: value => { calls.push(["open", value]); side = 2; tab = value; }, CloseSideMenus: () => { calls.push(["close"]); side = 0; } };
  const deps = { menuStore: () => store, navigation: () => undefined };
  assert.equal(presence.toggleJarvisTab(deps), "opened");
  assert.deepEqual(plain(calls), [["open", presence.JARVIS_TAB_ID]]);
  assert.equal(presence.takeJarvisFocus(), true, "the input is asked for the focus");
  assert.equal(presence.takeJarvisFocus(), false, "once");
  assert.equal(presence.toggleJarvisTab(deps), "closed");
  side = 2; tab = 3;
  assert.equal(presence.toggleJarvisTab(deps), "opened", "Quick Access open on another tab: switch to Jarvis");
  // Without a menu store, Decky's Navigation.
  const nav = [];
  assert.equal(presence.toggleJarvisTab({ menuStore: () => undefined, navigation: () => ({ OpenQuickAccessMenu: value => nav.push(value) }) }), "opened");
  assert.deepEqual(nav, [presence.JARVIS_TAB_ID]);
  assert.equal(presence.toggleJarvisTab({ menuStore: () => { throw new Error("x"); }, navigation: () => undefined }), "unavailable");
});

test("while a game runs the tap opens Quick Access over the game (its overlay window's menu store)", () => {
  const main = { name: "main" }, overlay = { name: "overlay" };
  const store = { GamepadUIMainWindowInstance: { MenuStore: main },
    OverlayWindows: [{ IsGamepadUIOverlayWindow: () => false, MenuStore: {} }, { IsGamepadUIOverlayWindow: () => true, MenuStore: overlay, BrowserWindow: {} }] };
  assert.equal(presence.jarvisMenuStore(store, 0), main);
  assert.equal(presence.jarvisMenuStore(store, 1), overlay);
  assert.equal(presence.jarvisMenuStore({ GamepadUIMainWindowInstance: { MenuStore: main }, OverlayWindows: [] }, 1), main);
  assert.equal(presence.jarvisMenuStore(undefined, 1), undefined);
});

test("the presence orb: dim when idle, pulsing while busy, a ring when ready, hidden in games unless active", () => {
  const base = { enabled: true, listening: false, working: false, readyUntil: 0, now: 10_000, gameRunning: false };
  assert.equal(presence.orbState(base), "idle");
  assert.equal(presence.orbState({ ...base, listening: true }), "busy");
  assert.equal(presence.orbState({ ...base, working: true }), "busy");
  assert.equal(presence.orbState({ ...base, readyUntil: 12_000 }), "ready");
  assert.equal(presence.orbState({ ...base, gameRunning: true }), "hidden");
  assert.equal(presence.orbState({ ...base, gameRunning: true, listening: true }), "busy");
  assert.equal(presence.orbState({ ...base, gameRunning: true, readyUntil: 12_000 }), "ready");
  assert.equal(presence.orbState({ ...base, enabled: false, listening: true }), "hidden");
  let now = 0, heard = 0;
  const p = presence.createPresence(() => now);
  p.subscribe(() => heard++);
  p.observe({ answer: "Sir." });
  assert.equal(p.readyUntil(), presence.JARVIS_READY_MS);
  p.observe({ answer: "Sir." });
  assert.equal(p.readyUntil(), presence.JARVIS_READY_MS, "the same answer is not new");
  now = 9000; p.observe({ answer: "Sir.", screen: { text: "A boss." } });
  assert.equal(p.readyUntil(), 9000 + presence.JARVIS_READY_MS);
  assert.ok(heard >= 3);
});

test("Jarvis's tab sits right after Core's in Quick Access and goes away with it", () => {
  class Hook {
    tabs = [{ id: 999, content: {} }];
    render(tabs, visible) {
      if (tabs.filter(tab => tab.decky).length === this.tabs.length) return;
      for (const tab of this.tabs) tabs.push({ key: tab.id, decky: true, panel: tab.content, initialVisibility: visible });
    }
  }
  const host = new EventTarget();
  host.__TABS_HOOK_INSTANCE = new Hook();
  const values = new Map();
  const icons = {};
  vm.runInNewContext(compile("qamIcons.ts"), { exports: icons, require: () => ({ createElement: (type, props) => ({ type, props }) }) });
  const api = {};
  vm.runInNewContext(compile("qam.ts"), { exports: api, window: host, WeakRef, setInterval: () => 1, clearInterval() {},
    localStorage: { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) },
    require: name => {
      if (name === "./qamTabVisibility") return { createQamTabVisibility: () => ({ update() {}, stop() {} }) };
      if (name === "./qamLayoutRuntime") { const layout = {}; vm.runInNewContext(compile("qamLayoutRuntime.ts"), { exports: layout, window: host, WeakRef, require: () => icons }); return layout; }
      const standalone = {}; vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: standalone, WeakRef, setInterval: () => 1, clearInterval() {} }); return standalone;
    } });
  const stop = api.initPlayhubQam({ content: {}, icon: {}, extraTabs: [{ id: presence.JARVIS_TAB_ID, name: "Jarvis", content: { jarvis: true }, icon: {} }] });
  const tabs = [{ key: 1 }, { key: 4 }];
  host.__TABS_HOOK_INSTANCE.render(tabs, true);
  const keys = tabs.map(tab => tab.key);
  assert.equal(keys[0], 0x50484B, "Core's tab first");
  assert.equal(keys[1], presence.JARVIS_TAB_ID, "Jarvis right after");
  assert.equal(keys.filter(key => key === presence.JARVIS_TAB_ID).length, 1);
  host.__TABS_HOOK_INSTANCE.render(tabs, true);
  assert.equal(tabs.filter(tab => tab.key === presence.JARVIS_TAB_ID).length, 1, "rendering again does not duplicate it");
  stop();
  host.__TABS_HOOK_INSTANCE.render(tabs, true);
  assert.equal(tabs.some(tab => tab.key === presence.JARVIS_TAB_ID || tab.key === 0x50484B), false, "removed with Core's tab");
});

test("the tab's text: 12 languages, the hold-to-talk line follows the combo and the desktop toggle", () => {
  assert.deepEqual([...tabLocale.jarvisTabLanguages].sort(), ["de", "en", "es", "fr", "hi", "it", "ja", "ko", "pt", "ru", "uk", "zh"]);
  for (const language of tabLocale.jarvisTabLanguages) {
    const copy = tabLocale.jarvisTabCopy(language);
    assert.ok(copy.tap.includes("{talk}") && copy.tapOff.includes("{talk}") && copy.last, language);
  }
  assert.equal(tabLocale.jarvisTapLine({ combos: { talk: "L5+R5" } }, "en"), "Tap L5+R5 to talk · hold it to open or close Jarvis");
  assert.equal(tabLocale.jarvisTapLine({ combos: { talk: "L5+R5" }, tapOpens: false }, "en"), "Tap L5+R5 to talk · or hold it while you speak");
  assert.equal(tabLocale.jarvisTapLine({ combos: { talk: null } }, "en"), "");
  assert.equal(tabLocale.jarvisTapLine({ combos: { talk: "<b>" } }, "en"), "");
  assert.equal(tabLocale.jarvisTabCopy("it-IT").last, "Ultima risposta");
});

test("Core's tab no longer carries the Jarvis section in Quick Access; the Steam menu has no Jarvis entry", () => {
  const index = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  assert.match(index, /status && origin !== "qam" && <JarvisSection/);
  assert.match(index, /extraTabs: \[\{ id: JARVIS_TAB_ID, name: "Jarvis"/);
  assert.doesNotMatch(readFileSync(new URL("../src/coreMenuEntries.ts", import.meta.url), "utf8"), /jarvis/i);
});
