import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Jarvis (docs/eternal/JARVIS.md): the Quick Access text in 12 languages, the status and error lines, combo parsing and
// the hold watcher against a fake Steam input, the push-to-talk and screen-helper flow against a fake backend, the
// Health row, and the backend calls the frontend makes.
const modules = {};
function load(file, extra = {}) {
  const exports = {};
  const require = name => {
    const key = name.replace(/^\.\//, "");
    if (!modules[key]) modules[key] = load(`${key}.ts`);
    return modules[key];
  };
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, require, Proxy, Intl, Promise, ...extra });
  return exports;
}
const report = load("jarvisReport.ts");
modules.jarvisReport = report;
const combos = load("jarvisCombos.ts");
const { createJarvisRuntime } = load("jarvisRuntime.ts");
const { healthLines } = load("healthReport.ts");
const plain = value => JSON.parse(JSON.stringify(value));
const flush = () => new Promise(resolve => setImmediate(resolve));

test("Jarvis text exists in all 12 languages with matching placeholders", () => {
  assert.equal(report.jarvisLanguages.length, 12);
  const english = report.jarvisCopy("en");
  const keys = Object.keys(plain(english));
  for (const language of report.jarvisLanguages) {
    const table = report.jarvisCopy(language);
    for (const key of keys) {
      assert.ok(typeof table[key] === "string" && table[key].length > 0, `${language}/${key}`);
      const tokens = value => (value.match(/\{\w+\}/g) ?? []).sort().join();
      assert.equal(tokens(table[key]), tokens(english[key]), `${language}/${key} placeholders`);
    }
    const line = healthLines({ attention: 0, rows: [{ id: "jarvis", level: "ok", state: "connected", data: { backend: "home-assistant", host: "serverv3", screenHelper: true } }], alerts: [] }, language)[0];
    assert.ok(line.title && !/^r_/.test(line.title) && line.value && !/^jv/.test(line.value), `${language} health row`);
  }
  assert.equal(report.jarvisCopy("it").more, "Altro");
  assert.equal(report.jarvisCopy("ua").title, "Jarvis");
});

test("status, tone, errors and combos in words", () => {
  assert.equal(report.statusLine({ enabled: true, status: "connected", backend: "home-assistant" }, "en"), "Connected through Home Assistant");
  assert.equal(report.statusLine({ enabled: true, status: "connected", backend: "claude" }, "en"), "Connected to the Claude API");
  assert.equal(report.statusLine({ enabled: true, status: "ha-unreachable" }, "de"), "Home Assistant nicht erreichbar");
  assert.equal(report.statusLine({ enabled: false }, "en"), "Off");
  assert.equal(report.statusLine(null, "en"), "Can't reach Jarvis");
  assert.equal(report.statusTone({ enabled: true, status: "connected" }), "ok");
  assert.equal(report.statusTone({ enabled: true, status: "ha-unreachable" }), "warning");
  assert.equal(report.statusTone({ enabled: true, status: "key-refused" }), "error");
  assert.equal(report.errorLine("muted", "en"), "Microphone muted: unmute it in Voice first");
  assert.equal(report.errorLine("needs-key", "it").startsWith("L'aiuto sullo schermo"), true);
  assert.equal(report.errorLine("<script>", "en"), "Something went wrong; nothing was changed");
  assert.equal(report.combosLine({ combos: { dictate: "L5+R5", talk: null, screen: "<b>" } }, "en"), "Hold L5+R5 to dictate · off to talk · off for the screen");
  const history = report.historyLines({ history: [{ role: "user", text: "hi" }, { role: "system", text: "x" }, { role: "assistant", text: " Good evening, sir. " }, { role: "assistant", text: 5 }] });
  assert.deepEqual(plain(history), [{ role: "user", text: "hi" }, { role: "assistant", text: "Good evening, sir." }]);
});

test("health row: Jarvis connected, not set up, Home Assistant unreachable", () => {
  const line = row => healthLines({ attention: 0, rows: [row], alerts: [] }, "en")[0];
  const connected = line({ id: "jarvis", level: "ok", state: "connected", data: { backend: "home-assistant", host: "serverv3", screenHelper: false } });
  assert.equal(connected.title, "Jarvis");
  assert.equal(connected.value, "Connected");
  assert.deepEqual(plain(connected.details), ["Through Home Assistant · serverv3", "Screen helper: needs the Anthropic key"]);
  assert.equal(line({ id: "jarvis", level: "info", state: "not-set-up", data: { backend: "home-assistant" } }).value, "Not set up");
  assert.equal(line({ id: "jarvis", level: "info", state: "ha-unreachable", data: { backend: "home-assistant", host: "serverv3" } }).value, "Home Assistant unreachable");
  assert.deepEqual(plain(line({ id: "jarvis", level: "info", state: "off", data: {} }).details), []);
});

test("combos: parsing mirrors the C# parser (L4/R4 in the upper word)", () => {
  assert.deepEqual(plain(combos.parseCombo("r5 + l5")), { text: "L5+R5", lower: (1 << 15 | 1 << 16) >>> 0, upper: 0 });
  assert.deepEqual(plain(combos.parseCombo("L4+R4")), { text: "L4+R4", lower: 0, upper: (1 << 9 | 1 << 10) >>> 0 });
  assert.equal(combos.parseCombo("LB+RB+View").text, "L1+R1+SELECT");
  for (const bad of ["A", "A+B+X+Y+L1", "A+A", "STEAM+A", "QAM+L4", "L9+R9", "", null, 5]) assert.equal(combos.parseCombo(bad), undefined, String(bad));
  assert.equal(combos.comboHeld(combos.parseCombo("L4+L5"), 1 << 15, 1 << 9), true);
  assert.equal(combos.comboHeld(combos.parseCombo("L4+L5"), 1 << 15, 0), false);
});

function fakeInput() {
  let callback;
  return {
    RegisterForControllerStateChanges(handler) { callback = handler; return { unregister() { callback = undefined; } }; },
    send(changes) { callback?.(changes); },
    get registered() { return !!callback; },
  };
}

test("combo watch: press and release once, any controller, the bigger combo wins, analog noise ignored", () => {
  const input = fakeInput();
  const events = [];
  const watch = combos.installJarvisCombos({
    input: () => input,
    combos: () => ({ dictate: "L5+R5", talk: "L4+R4", screen: "L4+R4+A" }),
    onPress: kind => events.push(["press", kind]),
    onRelease: kind => events.push(["release", kind]),
  });
  const L5R5 = (1 << 15 | 1 << 16) >>> 0, L4R4 = (1 << 9 | 1 << 10) >>> 0;
  input.send([{ unControllerIndex: 0, ulButtons: 1 << 15, ulUpperButtons: 0 }]);
  input.send([{ unControllerIndex: 0, ulButtons: L5R5, ulUpperButtons: 0 }]);
  input.send([{ unControllerIndex: 0, ulButtons: L5R5, ulUpperButtons: 0, sLeftStickX: 100 }]);
  input.send([{ unControllerIndex: 0, ulButtons: 0, ulUpperButtons: 0 }]);
  assert.deepEqual(events, [["press", "dictate"], ["release", "dictate"]]);
  events.length = 0;
  input.send([{ unControllerIndex: 1, ulButtons: 1 << 7, ulUpperButtons: L4R4 }]);
  assert.deepEqual(events, [["press", "screen"]], "L4+R4+A held fires only the screen combo");
  input.send([{ unControllerIndex: 1, ulButtons: 0, ulUpperButtons: 0 }]);
  watch.stop();
  assert.equal(input.registered, false);
  const quiet = combos.installJarvisCombos({ input: () => ({}), combos: () => ({}), onPress: () => assert.fail(), onRelease: () => assert.fail() });
  quiet.stop();
});

function fakeBackend(overrides = {}) {
  const calls = [];
  const backend = {
    listenStart: async mode => { calls.push(["start", mode]); return { ok: true, mic: "DJI Mic 3" }; },
    listenStop: async steamTyping => { calls.push(["stop", steamTyping]); return { ok: true, mode: "dictate", text: "gg well played", typed: false }; },
    listenCancel: async () => { calls.push(["cancel"]); return { ok: true }; },
    screen: async question => { calls.push(["screen", question]); return { ok: true, text: "Look up.", more: true }; },
    type: async text => { calls.push(["type", text]); return { ok: true }; },
    ...overrides,
  };
  return { backend, calls };
}

test("push-to-talk: dictation typed through Steam's keyboard path", async () => {
  const { backend, calls } = fakeBackend();
  const sent = [], toasts = [];
  const runtime = createJarvisRuntime({ backend, input: () => ({ ControllerKeyboardSendText: text => sent.push(text) }), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  runtime.press("dictate");
  assert.equal(runtime.snapshot().listening, "dictate");
  runtime.release("dictate");
  await flush(); await flush();
  assert.deepEqual(calls, [["start", "dictate"], ["stop", true]], "the agent is told Steam types, so it does not SendInput");
  assert.deepEqual(sent, ["gg well played"]);
  assert.deepEqual(toasts, ["Typed: gg well played"]);
  assert.equal(runtime.snapshot().listening, null);
});

test("push-to-talk: no Steam keyboard call falls back to the agent; anti-cheat never types", async () => {
  let { backend, calls } = fakeBackend();
  const toasts = [];
  let runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  await runtime.start("dictate");
  await runtime.stop();
  assert.deepEqual(calls.map(call => call[0]), ["start", "stop", "type"]);
  assert.deepEqual(calls[1], ["stop", false]);
  ({ backend, calls } = fakeBackend({ listenStop: async () => ({ ok: true, mode: "dictate", text: "hello", typed: false, error: "anti-cheat" }) }));
  toasts.length = 0;
  runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  await runtime.start("dictate");
  await runtime.stop();
  assert.ok(!calls.some(call => call[0] === "type"));
  assert.match(toasts[0], /anti-cheat/);
});

test("push-to-talk: dictation into the Quick Access field types nothing", async () => {
  const { backend, calls } = fakeBackend();
  const sent = [];
  const runtime = createJarvisRuntime({ backend, input: () => ({ ControllerKeyboardSendText: text => sent.push(text) }), toast: () => {}, locale: () => "en", enabled: () => true });
  await runtime.start("dictate");
  const answer = await runtime.stop({ intoField: true });
  assert.equal(answer.text, "gg well played");
  assert.deepEqual(sent, []);
  assert.deepEqual(calls[1], ["stop", true]);
});

test("push-to-talk: muted refusal, talk mode answer, party warning, off", async () => {
  let toasts = [];
  let { backend } = fakeBackend({ listenStart: async () => ({ ok: false, error: "muted" }) });
  let runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  assert.equal(await runtime.start("talk"), false);
  assert.deepEqual(toasts, ["Microphone muted: unmute it in Voice first"]);
  assert.equal(runtime.snapshot().listening, null);
  toasts = [];
  ({ backend } = fakeBackend({
    listenStart: async () => ({ ok: true, partyHears: true }),
    listenStop: async () => ({ ok: true, mode: "talk", text: "dim the lights", answer: "Done, sir. Living room at thirty percent." }),
  }));
  runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  await runtime.start("talk");
  await runtime.stop();
  assert.deepEqual(toasts, ["Your Xbox party hears you too", "Done, sir. Living room at thirty percent."]);
  toasts = [];
  runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => false });
  assert.equal(await runtime.start("talk"), false);
  assert.deepEqual(toasts, ["Jarvis is off"]);
});

test("screen helper: nudge first with More, errors in words, nothing while busy", async () => {
  const toasts = [];
  let { backend, calls } = fakeBackend();
  let runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  runtime.press("screen");
  await flush();
  assert.deepEqual(calls, [["screen", ""]]);
  assert.deepEqual(toasts, ["Look up. (More: Quick Access)"]);
  assert.equal(runtime.snapshot().screen.more, true);
  toasts.length = 0;
  ({ backend } = fakeBackend({ screen: async () => ({ ok: false, error: "needs-key" }) }));
  runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  await runtime.screen();
  assert.match(toasts[0], /Anthropic key/);
  let release;
  ({ backend } = fakeBackend({ screen: () => new Promise(resolve => { release = resolve; }) }));
  toasts.length = 0;
  runtime = createJarvisRuntime({ backend, input: () => ({}), toast: (title, body) => toasts.push(body), locale: () => "en", enabled: () => true });
  const first = runtime.screen();
  await runtime.screen();
  assert.deepEqual(toasts, ["Jarvis is still answering"]);
  release({ ok: true, text: "Full answer.", more: false });
  await first;
  assert.equal(toasts.at(-1), "Full answer.");
});

test("backend calls exist in main.py, text is cleaned and capped, never logged", () => {
  const main = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  const shipped = readFileSync(new URL("../../Playhub/Assets/GamingModeDeckyPlugin/gaming-mode/main.py", import.meta.url), "utf8");
  assert.equal(main, shipped, "the shipped main.py is the plugin's main.py");
  for (const name of ["core_jarvis_state", "core_jarvis_chat", "core_jarvis_clear", "core_jarvis_listen_start", "core_jarvis_listen_stop", "core_jarvis_listen_cancel", "core_jarvis_screen", "core_jarvis_screen_more", "core_jarvis_speak", "core_jarvis_speak_stop", "core_jarvis_type"])
    assert.match(main, new RegExp(`async def ${name}\\(`), name);
  const block = main.slice(main.indexOf("async def core_jarvis_state"), main.indexOf("def _jarvis_text"));
  assert.ok(!/logging\./.test(block), "no Jarvis call logs");
  assert.match(main, /if mode not in _JARVIS_LISTEN_MODES/);
  assert.match(main, /_JARVIS_TEXT_MAXIMUM = 2000/);
  const section = readFileSync(new URL("../src/JarvisSection.tsx", import.meta.url), "utf8");
  assert.ok(!/#[0-9A-Fa-f]{6}/.test(section.replace(/\/\/.*$/gm, "")), "colours come from the Core palette");
  assert.match(section, /CloseSideMenus/, "Quick Access closes before the screenshot");
});
