import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// 3.3.5 in Quick Access: the controller reference (identical to the agent's hotkey table), exiting games through
// Steam (lifetime and Exit Game reports), and the Health rows for game exit, back paddles, emulator updates, each
// emulator and its hotkeys, in Core's 12 languages. docs/eternal/CONSOLE-GUARDS.md, EMULATOR-UPDATES.md.
const load = file => {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports });
  return exports;
};
const { EMULATOR_HOTKEYS, controlsRows, controlsLanguages, controlsCopy } = load("emulatorHotkeys.ts");
const { installGameExitWatch } = load("gameExit.ts");
const { healthLines, healthLanguages } = load("healthReport.ts");
const now = new Date("2026-11-27T05:30:00-05:00");
const plain = value => JSON.parse(JSON.stringify(value));
const line = (row, locale = "en") => healthLines({ attention: 0, rows: [row], alerts: [] }, locale, now)[0];

test("the Quick Access table is exactly the agent's hotkey table", () => {
  const cs = readFileSync(new URL("../../GamingModeAgent/GamingMode.Services/EmulatorHealth.cs", import.meta.url), "utf8");
  const agent = [...cs.matchAll(/^\s*\("([a-z-]+)", "([^"]+)"\),\s*$/gm)].map(match => [match[1], match[2]]);
  assert.deepEqual(plain(EMULATOR_HOTKEYS), agent);
  for (const [, buttons] of EMULATOR_HOTKEYS) {
    assert.match(buttons, /^Select \+ /);
    assert.doesNotMatch(buttons, /Start|L4|R4|L5|R5/);
  }
});

test("the reference has the paddles, Jarvis tap/hold and exit-through-Steam in every language", () => {
  for (const language of controlsLanguages) {
    const rows = controlsRows(language);
    assert.equal(rows.length, EMULATOR_HOTKEYS.length + 5, language);
    assert.ok(rows.every(row => row.label && row.buttons && !/^(save|load|exit|jarvis)/.test(row.label)), `${language}: translated`);
    const text = controlsCopy(language);
    assert.match(text.jarvisTalkHow, /L5 \+ R5/);
    assert.match(text.jarvisOpenHow, /L5 \+ R5/);
  }
  assert.equal(controlsRows("it")[2].buttons, "Select + croce destra");
});

test("Steam's Exit Game is reported after Steam's own call, and lifetime events carry the title", async () => {
  const calls = [];
  let lifetime;
  const steam = {
    Apps: { TerminateApp(appId, flag) { calls.push(["steam", appId, flag]); return "done"; } },
    GameSessions: { RegisterForAppLifetimeNotifications(callback) { lifetime = callback; return { unregister() { calls.push(["unregistered"]); } }; } },
  };
  const original = steam.Apps.TerminateApp;
  const stop = installGameExitWatch({
    steam: () => steam, title: appId => `Game\u0007 ${appId}`,
    lifetime: (appId, running, title) => { calls.push(["lifetime", appId, running, title]); },
    exitChosen: () => { calls.push(["exit"]); return Promise.reject(new Error("agent down")); },
  });
  assert.equal(steam.Apps.TerminateApp("123", false), "done");
  lifetime({ unAppID: 3000000000, bRunning: false });
  lifetime({ unAppID: -1, bRunning: true });
  assert.deepEqual(calls, [["steam", "123", false], ["exit"], ["lifetime", 3000000000, false, "Game 3000000000"]]);
  // Installing twice does not wrap twice; stopping restores Steam's own function.
  installGameExitWatch({ steam: () => steam, title: () => "", lifetime() {}, exitChosen() {} });
  stop();
  assert.equal(steam.Apps.TerminateApp, original);
  assert.deepEqual(calls.at(-1), ["unregistered"]);
  assert.doesNotThrow(() => installGameExitWatch({ steam: () => undefined, title: () => "", lifetime() {}, exitChosen() {} })());
});

test("Health: a game Steam lost track of offers Core's Exit; a game that did not exit is crimson", () => {
  const untracked = line({ id: "game-exit", level: "attention", state: "untracked", data: { untracked: "Ico", fix: "game-exit-now" } });
  assert.equal(untracked.title, "Exiting games");
  assert.equal(untracked.value, "Steam lost track of Ico");
  assert.deepEqual(plain(untracked.actions), [{ id: "game-exit-now", label: "Exit Ico", fix: true }]);
  assert.equal(line({ id: "game-exit", level: "attention", state: "did-not-exit", data: { title: "Ico", left: 1 } }).value, "Ico did not exit: 1 still running");
});

test("Health: emulator rows are titled with the emulator, list settings, RetroAchievements, BIOS and offer one-tap fixes", () => {
  const row = line({ id: "emulator-pcsx2", level: "info", state: "check", data: {
    emulator: "pcsx2", name: "PCSX2", version: "2.4.0", ra: "signed-in", issues: ["settings"], fix: "emu-fix-pcsx2",
    settings: [{ kind: "fullscreen", state: "wrong" }, { kind: "exit-prompt", state: "ok" }], bios: [{ system: "ps2", state: "ok" }],
  } });
  assert.equal(row.title, "PCSX2");
  assert.equal(row.value, "Check: settings");
  assert.ok(row.details.includes("Starts fullscreen: no") && row.details.includes("RetroAchievements: signed in") && row.details.includes("BIOS PS2: present"));
  assert.deepEqual(plain(row.actions), [{ id: "emu-fix-pcsx2", label: "Fix PCSX2 settings", fix: true }]);
  const hotkeys = line({ id: "hotkeys-dolphin", level: "info", state: "drifted", data: { name: "Dolphin", drifted: ["slot-next", "guard:General/Exit"], cantDo: ["rewind", "menu", "fps"], fix: "emu-hotkeys-dolphin" } });
  assert.equal(hotkeys.title, "Hotkeys · Dolphin");
  assert.equal(hotkeys.value, "Drifted: next slot, a quit or clashing combo");
  assert.ok(hotkeys.details.includes("Can't do: rewind, emulator menu, FPS display"));
  assert.equal(hotkeys.actions[0].label, "Re-apply hotkeys to Dolphin");
  assert.equal(line({ id: "hotkeys-cemu", level: "ok", state: "unsupported", data: { name: "Cemu", unsupported: "no-save-states" } }).value, "Can't: it has no save states");
});

test("Health: emulator updates and the back paddles", () => {
  const updates = line({ id: "emulator-updates", level: "info", state: "needs-you", data: { emulators: [
    { name: "Cemu", phase: "NeedsConfirmation", pending: "v2.6" }, { name: "PCSX2", phase: "Soaking", pending: "v2.4", daysLeft: 4 }], notOffered: ["RetroArch"] } });
  assert.equal(updates.value, "Needs you: Cemu");
  assert.ok(updates.details[0].startsWith("Cemu v2.6: confirm it once on the desktop"));
  assert.ok(updates.details.includes("Updated by EmuDeck: RetroArch"));
  const paddles = line({ id: "back-paddles", level: "info", state: "not-set", data: { layouts: [{ layout: "big-picture", verdict: "a-b" }, { layout: "desktop", verdict: "unbound" }], fix: "paddles-setup", actions: ["paddles-setup"] } });
  assert.equal(paddles.value, "Not set in Steam's layout");
  assert.deepEqual(plain(paddles.details.slice(0, 2)), ["Big Picture: set", "Desktop: not set"]);
  assert.deepEqual(plain(paddles.actions), [{ id: "paddles-setup", label: "Open Steam's controller layout", fix: true }]);
});

test("every new Health text exists in every language", () => {
  const rows = [
    { id: "game-exit", level: "ok", state: "watching", data: {} }, { id: "back-paddles", level: "ok", state: "in-place", data: {} },
    { id: "emulator-updates", level: "ok", state: "current", data: {} }, { id: "hotkeys-pcsx2", level: "ok", state: "matches", data: { name: "PCSX2" } },
    { id: "emulator-cemu", level: "ok", state: "ok", data: { name: "Cemu", version: "2.6" } },
  ];
  for (const language of healthLanguages)
    for (const row of rows) {
      const shown = line(row, language);
      assert.ok(shown && shown.title && !/^r_/.test(shown.title) && shown.value && !/^(ge|bp|eu|hk|em)[A-Z]/.test(shown.value), `${language}/${row.id}`);
    }
});

test("the backend forwards only known emulator actions", () => {
  const main = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  assert.match(main, /_EMULATOR_ACTION = r"game-exit-now\|paddles-setup\|emu-\(\?:fix\|hotkeys\|update\)-\[a-z0-9\]\{2,20\}"/);
  for (const name of ["core_emulators_state", "core_launcher_auto_close", "core_game_lifetime", "core_game_exit_chosen"]) assert.match(main, new RegExp(`async def ${name}\\(`));
});
