import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Batch 2 (docs/eternal/BATCH-2.md): swipe typing (VaporType's loader in Core), the automations summary, the three new
// Health rows and the local-input report for sleep after streaming.
function load(file, extra = {}) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, Proxy, Intl, URL, ...extra });
  return exports;
}
const report = load("batchTwoReport.ts");
const { healthLines, healthLanguages } = load("healthReport.ts");
const { installLocalInputWatch } = load("localInput.ts");
const typingModule = load("swipeTyping.ts");
const plain = value => JSON.parse(JSON.stringify(value));
const now = new Date("2026-11-26T23:00:00-05:00");
const line = (row, locale = "en") => healthLines({ attention: 0, rows: [row], alerts: [] }, locale, now)[0];
const keyboard = new URL("../../Playhub/Assets/GamingModeDeckyPlugin/gaming-mode/dist/keyboard/", import.meta.url);

test("batch 2 text exists in all 12 languages", () => {
  assert.equal(report.batchTwoLanguages.length, 12);
  const english = Object.keys(report.batchTwoCopy("en"));
  for (const language of report.batchTwoLanguages) {
    const table = report.batchTwoCopy(language);
    for (const key of english) {
      assert.ok(typeof table[key] === "string" && table[key].length > 0, `${language}/${key}`);
      const tokens = value => (value.match(/\{\w+\}/g) ?? []).sort().join();
      assert.equal(tokens(table[key]), tokens(report.batchTwoCopy("en")[key]), `${language}/${key} placeholders`);
    }
    for (const id of ["stream-sleep", "wake-on-lan", "home-assistant"]) {
      const title = line({ id, level: "info", state: "off", data: {} }, language).title;
      assert.ok(title && !/^r_/.test(title), `${language}/${id}`);
    }
  }
  assert.equal(healthLanguages.length, 12);
  assert.equal(report.batchTwoCopy("it").typing, "Scrittura a scorrimento");
});

test("automations summary: enabled rules, TV-on-wake shown once, events named", () => {
  const answer = {
    rules: [
      { id: "controller-tv", enabled: true, trigger: "controller-connected", actions: ["tv-wake"] },
      { id: "wake-profiles", enabled: false, trigger: "wake", actions: ["display-restore"] },
      { id: "game-start-ha", enabled: true, trigger: "game-start", actions: ["ha-event"], event: "lights-down" },
      { id: "junk", enabled: true, trigger: "reboot", actions: ["tv-wake"] },
      { id: "junk2", enabled: true, trigger: "wake", actions: ["format-disk"] },
    ],
    tvOnWake: true,
    bridge: { enabled: true, connected: true, state: "connected", host: "serverv3" },
    streamSleep: { enabled: true, minutes: 10 },
  };
  assert.deepEqual(plain(report.automationLines(answer, "en")), [
    "When Core wakes: wake the TV and switch to Core",
    "When a controller connects: wake the TV and switch to Core",
    "When a game starts: tell Home Assistant (lights-down)",
  ]);
  assert.equal(report.automationLines({ rules: [] }, "en").length, 0);
  assert.equal(report.bridgeLine(answer, "en"), "Connected to serverv3");
  assert.equal(report.bridgeLine({ bridge: { enabled: true, state: "refused", host: "serverv3" } }, "de"), "Anmeldung von serverv3 abgelehnt");
  assert.equal(report.bridgeLine({ bridge: { enabled: false } }, "en"), "Off");
  assert.equal(report.sleepLine(answer, "en"), "After 10 min with nobody here");
  assert.match(report.sleepLine({ streamSleep: { enabled: true, at: "2026-11-27T01:24:00-05:00" } }, "en"), /^Sleeping at .+ unless someone plays here$/);
  assert.equal(report.sleepLine({ streamSleep: { enabled: false } }, "it"), "Spenta");
});

test("health rows: sleep after streaming, Wake-on-LAN, Home Assistant", () => {
  assert.equal(line({ id: "stream-sleep", level: "ok", state: "ready", data: { minutes: 10 } }).value, "On · after 10 min");
  const wol = line({ id: "wake-on-lan", level: "info", state: "check", data: { problems: ["magic-packet", "<b>"], mac: "A8:23:FE:01:02:03", fastStartup: true } });
  assert.equal(wol.title, "Wake-on-LAN");
  assert.equal(wol.value, "Check the network adapter");
  assert.deepEqual(plain(wol.details), [
    "Turn on \"Wake on Magic Packet\" in the adapter's Advanced settings (Device Manager)",
    "Wake address (MAC): A8:23:FE:01:02:03",
    "Fast startup is on: waking from shutdown may fail; waking from sleep works",
  ]);
  assert.equal(line({ id: "wake-on-lan", level: "ok", state: "ready", data: { mac: "junk" } }).details.length, 0);
  assert.equal(line({ id: "home-assistant", level: "ok", state: "connected", data: { host: "serverv3" } }).value, "Connected · serverv3");
  assert.equal(line({ id: "home-assistant", level: "info", state: "refused", data: { host: "serverv3" } }, "fr").value, "Connexion refusée par serverv3");
});

test("local input: button messages only, seconds ago", () => {
  let clock = 1_000_000, handler, unregistered = false, analog = false;
  const watch = installLocalInputWatch({
    input: () => ({
      RegisterForControllerInputMessages: callback => { handler = callback; return { unregister: () => { unregistered = true; } }; },
      RegisterForControllerAnalogInputMessages: () => { analog = true; },
    }),
    now: () => clock,
  });
  assert.equal(watch.secondsAgo(), undefined);
  handler([{ button: 1 }]);
  clock += 42_500;
  assert.equal(watch.secondsAgo(), 42);
  assert.equal(analog, false, "analog input never registered");
  watch.stop();
  assert.equal(unregistered, true);
  assert.equal(installLocalInputWatch({ input: () => { throw new Error("no Steam"); }, now: () => 0 }).secondsAgo(), undefined);
});

function fakeTyping(overrides = {}) {
  const calls = [];
  const timers = [];
  const globals = {};
  const files = { "swipe/words.js": "globalThis.__oskWords = { version: 4 }", "swipe/lm.js": "x = { version: 6 }", "swipe/engine.js": "const VERSION = 12;", "modes.js": "const VERSION = 7;", "patch.js": "const VERSION = 43;" };
  const evaluated = [];
  let saved = 0;
  const fix = {
    version: 43, stopped: false, status: () => "ok", stop() { this.stopped = true; calls.push("stop"); },
    setSetting: (name, value) => calls.push(["setting", name, value]), openSettings: () => true, forgetAll: () => calls.push("forgetAll"),
    keyboardOpen: () => false, takeUserWords: () => (saved++ === 0 ? JSON.stringify({ words: { hello: 3 }, rev: 1 }) : null), ackUserWords: rev => calls.push(["ack", rev]),
    takeRequests: () => null, takeTypingLog: () => null,
  };
  const deps = {
    load: async () => ({ userWords: { words: {} }, enabled: true, epoch: 2 }),
    setEnabled: async on => { calls.push(["setEnabled", on]); return { enabled: on }; },
    saveWords: async (payload, epoch) => { calls.push(["save", payload, epoch]); return { saved: true, rev: 1, epoch }; },
    appendLog: async () => ({ saved: true }),
    requests: async () => ({ ok: true, epoch: 2 }),
    forget: async () => { calls.push("forgetFiles"); return { ok: true, epoch: 3 }; },
    fetchText: async file => { calls.push(["fetch", file]); return files[file]; },
    evaluate: source => { evaluated.push(source); if (source.startsWith("const VERSION = 43")) globals.__oskTrackpadFix = fix; return "ok"; },
    globals,
    clientKind: () => "webpack",
    standalone: () => false,
    lookups: { inputSettings: () => null },
    schedule: (callback, delay) => { timers.push({ callback, delay }); return timers.length; },
    cancel: () => {},
    now: () => 5_000_000,
    removeStoredWords: () => calls.push("removeStored"),
    ...overrides,
  };
  return { typing: typingModule.createSwipeTyping(deps), calls, globals, evaluated, timers, fix };
}

test("swipe typing: injects VaporType's keyboard with Core's lookups and saves learned words", async () => {
  const { typing, calls, globals, evaluated } = fakeTyping();
  await typing.start();
  assert.deepEqual(plain(calls.filter(call => call[0] === "fetch").map(call => call[1])), ["swipe/words.js", "swipe/lm.js", "swipe/engine.js", "modes.js", "patch.js"]);
  assert.equal(evaluated.length, 5);
  assert.ok(globals.__coreOskLookups && typeof globals.__coreOskLookups.inputSettings === "function", "lookups handed to patch.js");
  assert.equal(globals.__oskInstall.via, "core");
  assert.equal(typing.status().state, "on");
  const save = calls.find(call => call[0] === "save");
  assert.deepEqual(plain(save[1]), { words: { hello: 3 }, rev: 1 });
  assert.equal(save[2], 2, "saved under the current epoch");
  assert.ok(calls.some(call => call[0] === "ack" && call[1] === 1));
  typing.shutdown();
  assert.ok(calls.includes("stop"));
  assert.equal(globals.__coreOskLookups, undefined);
});

test("swipe typing: off, the ES-module Steam client and the standalone plugin keep Steam's keyboard", async () => {
  const off = fakeTyping({ load: async () => ({ userWords: null, enabled: false, epoch: 0 }) });
  await off.typing.start();
  assert.equal(off.evaluated.length, 0);
  assert.equal(off.typing.status().state, "off");
  const modules = fakeTyping({ clientKind: () => "modules" });
  await modules.typing.start();
  assert.equal(modules.evaluated.length, 0);
  assert.equal(modules.typing.status().state, "modules");
  const standalone = fakeTyping({ standalone: () => true });
  await standalone.typing.start();
  assert.equal(standalone.evaluated.length, 0);
  assert.equal(standalone.typing.status().state, "standalone");
  const down = fakeTyping({ load: async () => { throw new Error("agent down"); } });
  await down.typing.start();
  assert.equal(down.evaluated.length, 0, "no backend: stays off");
  assert.equal(typingModule.vaporTypeInstalled({ plugins: [{ name: "VaporType" }, { name: "Playhub" }] }), true);
  assert.equal(typingModule.vaporTypeInstalled({ plugins: ["Playhub"] }), false);
  assert.equal(typingModule.vaporTypeInstalled(undefined), false);
});

test("swipe typing: turning it off stops the patch; forget everything clears every copy", async () => {
  const { typing, calls } = fakeTyping();
  await typing.start();
  await typing.setEnabled(false);
  assert.ok(calls.some(call => call[0] === "setEnabled" && call[1] === false));
  assert.ok(calls.includes("stop"));
  assert.equal(typing.status().state, "off");
  const second = fakeTyping();
  await second.typing.start();
  assert.equal(await second.typing.forgetEverything(), true);
  assert.ok(second.calls.includes("forgetAll") && second.calls.includes("removeStored") && second.calls.includes("forgetFiles"));
  assert.equal(second.typing.setSetting("privateMode", true), true);
});

test("keyboard lookups go through Core's compat finders", () => {
  const seen = [];
  const lookups = typingModule.keyboardLookups({}, {
    findExport: (ui, filter) => (filter({ TrackpadTypingTriggerAsClick: true }) ? { TrackpadTypingTriggerAsClick: true } : undefined),
    findClassModule: (ui, filter) => {
      const keyboardMap = { Keyboard: "kb_1", KeyboardKey: "key_2", TouchpadPointerContainer: "tp_3" };
      const glassMap = { BackgroundGlass: "g_4", DrawBackground: "d_5", Other: "o" };
      seen.push(filter(keyboardMap) ? "keyboard" : filter(glassMap) ? "glass" : "none");
      return filter(keyboardMap) ? keyboardMap : filter(glassMap) ? glassMap : undefined;
    },
  });
  assert.deepEqual(plain(lookups.inputSettings()), { TrackpadTypingTriggerAsClick: true });
  assert.deepEqual(plain(lookups.classes()), { BackgroundGlass: "g_4", DrawBackground: "d_5", Keyboard: "kb_1", KeyboardKey: "key_2", TouchpadPointerContainer: "tp_3" });
  const none = typingModule.keyboardLookups({}, { findExport: () => undefined, findClassModule: () => undefined });
  assert.equal(none.inputSettings(), null);
  assert.equal(none.classes(), null);
});

test("the keyboard ships with its MIT notice, data licences and Core's one hook", () => {
  for (const file of ["patch.js", "modes.js", "swipe/engine.js", "swipe/words.js", "swipe/lm.js", "swipe/DATA-LICENSES.md", "LICENSE", "CORE-CHANGES.md"])
    assert.ok(existsSync(new URL(file, keyboard)), file);
  assert.match(readFileSync(new URL("LICENSE", keyboard), "utf8"), /MIT License[\s\S]*Copyright \(c\) 2026 VaporType contributors/);
  const patch = readFileSync(new URL("patch.js", keyboard), "utf8");
  assert.match(patch, /function coreLookup\(name\)/);
  assert.match(patch, /coreLookup\("inputSettings"\)/);
  assert.match(patch, /coreLookup\("classes"\)/);
  assert.doesNotMatch(patch, /fcntl|releases\/latest/);
  for (const name of typingModule.KEYBOARD_FILES) assert.ok(existsSync(new URL(name, keyboard)), name);
  const notices = readFileSync(new URL("../THIRD-PARTY-NOTICES.md", import.meta.url), "utf8");
  assert.match(notices, /VaporType/);
});

test("the backend offers every swipe typing and automations call, without self-update", () => {
  const source = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  for (const name of ["core_typing_load", "core_typing_set_enabled", "core_typing_save_words", "core_typing_append_log", "core_typing_requests", "core_typing_forget", "core_automations"])
    assert.match(source, new RegExp(`async def ${name}\\(`), name);
  assert.match(source, /async def core_heartbeat\(self, input_ago=None\)/);
  assert.doesNotMatch(source, /^import fcntl/m);
  const bundled = readFileSync(new URL("../../Playhub/Assets/GamingModeDeckyPlugin/gaming-mode/main.py", import.meta.url), "utf8");
  assert.equal(bundled.replace(/\r\n/g, "\n"), source.replace(/\r\n/g, "\n"), "bundled main.py matches");
});
