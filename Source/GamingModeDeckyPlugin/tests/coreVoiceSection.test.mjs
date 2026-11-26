import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Core Voice in Quick Access: the agent's /voice answers become the microphone button, the party
// messages and the Discord row; the section keeps the Core palette.
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/voiceReport.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const { micView, partyMessage, discordInstalled, voiceCopy, voiceLanguages, DISCORD_PLUGIN_NAMES, callsMicView, scopeView, callsMicMessage } = exports;

test("microphone button follows the actual state", () => {
  const live = micView({ mic: { state: "live", coreMuted: false, defaults: 2, hotkey: "Ctrl+Alt+M" } }, "en");
  assert.equal(live.button, "Mute microphone");
  assert.equal(live.line, "Microphone live");
  assert.equal(live.muted, false);
  assert.equal(live.detail, "Shortcut Ctrl+Alt+M: bind it to a controller chord in Steam");
  const muted = micView({ mic: { state: "muted", coreMuted: true, hotkey: "" } }, "en");
  assert.equal(muted.button, "Unmute microphone");
  assert.equal(muted.muted, true);
  assert.equal(muted.detail, "", "no hotkey: no hint");
  assert.equal(micView({ mic: { state: "muted", coreMuted: false } }, "en").detail, "Muted outside Core");
  assert.equal(micView({ mic: { state: "none" } }, "en").line, "No microphone");
  assert.equal(micView(null, "en").state, "unknown");
  assert.equal(micView({ mic: { state: "weird" } }, "it").line, "Microfono: sconosciuto");
});

test("partly muted: shown like muted with a warning; the call mic decides the button", () => {
  const callMuted = micView({ mic: { state: "partial", coreMuted: false, communications: "muted", hotkey: "Ctrl+Alt+M" } }, "en");
  assert.equal(callMuted.muted, true, "crimson like muted");
  assert.equal(callMuted.line, "Microphone partly muted");
  assert.equal(callMuted.button, "Unmute microphone");
  assert.match(callMuted.detail, /One default microphone is muted/);
  const callLive = micView({ mic: { state: "partial", coreMuted: false, communications: "live" } }, "en");
  assert.equal(callLive.muted, true);
  assert.equal(callLive.button, "Mute microphone");
  assert.equal(micView({ mic: { state: "partial" } }, "en").button, "Mute microphone", "no Communications default known: mute everything");
});

const DJI = "{0.0.1.00000000}.{11111111-1111-1111-1111-111111111111}";
const WEBCAM = "{0.0.1.00000000}.{22222222-2222-2222-2222-222222222222}";
const devices = [
  { id: WEBCAM, name: "Microphone (USB Webcam)", console: true, communications: false, muted: false },
  { id: DJI, name: "Microphone (Wireless Microphone RX)", console: false, communications: true, muted: false },
];

test("calls mic picker: friendly names, the defaults each holds, Windows' choice first", () => {
  const view = callsMicView({ mic: { state: "live" }, devices, callsMic: { id: DJI, name: "Microphone (Wireless Microphone RX)", status: "active", everything: false } }, "en");
  assert.deepEqual([...view.options.map(option => option.data)], ["", WEBCAM, DJI]);
  assert.equal(view.options[0].label, "Windows' choice");
  assert.equal(view.options[1].label, "Microphone (USB Webcam) · games and apps");
  assert.equal(view.options[2].label, "Microphone (Wireless Microphone RX) · calls");
  assert.equal(view.selected, DJI);
  assert.equal(view.line, "Connected and used for calls.");
  assert.equal(view.canToggleEverything, true);
  const both = callsMicView({ devices: [{ id: DJI, name: "", console: true, communications: true }] }, "en");
  assert.equal(both.options[1].label, "Microphone · calls, games and apps", "no name: a plain fallback, both defaults marked");
  assert.equal(both.selected, "", "nothing chosen: Windows' choice");
  assert.equal(both.line, "");
  assert.equal(both.canToggleEverything, false);
});

test("calls mic not connected: calm, still listed, one line about Windows' default", () => {
  const view = callsMicView({ mic: { state: "live" }, devices: [devices[0]], callsMic: { id: DJI, name: "Microphone (Wireless Microphone RX)", status: "absent", everything: true } }, "en");
  assert.equal(view.selected, DJI);
  assert.equal(view.options.at(-1).label, "Microphone (Wireless Microphone RX) (not connected)");
  assert.equal(view.line, "Not connected. Calls use Windows' default microphone.");
  assert.equal(view.canToggleEverything, false);
  assert.equal(callsMicView({ devices, callsMic: { id: DJI, status: "changed" } }, "en").line, "Windows uses another microphone for calls. Pick it again to use it.");
  assert.match(callsMicView({ devices, callsMic: { id: DJI, status: "failed" } }, "it").line, /Windows non ha permesso/);
});

test("calls mic pick messages: only when it did not work, said once", () => {
  assert.equal(callsMicMessage({ mic: { state: "live" }, result: "ok" }, "en"), "");
  assert.equal(callsMicMessage({ mic: { state: "live" }, result: "notFound" }, "en"), "That microphone is no longer connected.");
  assert.equal(callsMicMessage({ mic: { state: "live" }, result: "refused", callsMic: { status: "failed" } }, "en"), "", "the status line already says it");
  assert.match(callsMicMessage({ mic: { state: "live" }, result: "unavailable", callsMic: { status: "absent" } }, "en"), /did not let Core/);
  assert.equal(callsMicMessage({}, "en"), "Core's service is not answering.");
});

test("mute scope: everything by default (short), calls only explains what stays live", () => {
  const all = scopeView({ mic: { state: "live" } }, "en");
  assert.equal(all.selected, "all");
  assert.deepEqual([...all.options.map(option => option.label)], ["Mute everything", "Mute calls only"]);
  assert.ok(all.hint.length < 60, "everything: a short line");
  const calls = scopeView({ mic: { state: "muted", scope: "calls" } }, "en");
  assert.equal(calls.selected, "calls");
  assert.match(calls.hint, /games and apps mic stays live/);
  assert.match(calls.hint, /OBS/);
  assert.equal(scopeView({ mic: { scope: "loud" } }, "en").selected, "all");
});

test("party messages: nothing to say when the bar opened", () => {
  assert.equal(partyMessage({ ok: true, method: "WinG", reason: "shortcut" }, "en"), "");
  assert.equal(partyMessage({ ok: true, method: "Uri", reason: "override" }, "en"), "");
  assert.equal(partyMessage({ ok: true, method: "Uri", reason: "gameRunning" }, "en"), "", "opened by URI during a game");
  assert.equal(partyMessage({ ok: true, method: "AlreadyOpen", reason: "cooldown" }, "en"), "", "a second press right after: nothing to say");
  assert.equal(partyMessage({ ok: false, method: "Unavailable", reason: "uriFailed" }, "en"), "Windows could not open the Game Bar.");
  assert.equal(partyMessage({ ok: true, method: "AlreadyOpen", reason: "alreadyOpen" }, "en"), "The Game Bar is already open.");
  assert.equal(partyMessage({ ok: false, method: "Unavailable", reason: "notInstalled" }, "en"), "The Xbox Game Bar is not installed.");
  assert.equal(partyMessage({ ok: false, method: "Unavailable", reason: "protectedGame" }, "en"), "Not while a game with anti-cheat is running.");
  assert.equal(partyMessage({ ok: false, method: "Unavailable", reason: "inputRefused" }, "en"), "Windows did not accept the Game Bar shortcut.");
  assert.equal(partyMessage({}, "en"), "Core's service is not answering.");
  assert.equal(partyMessage(undefined, "de"), "Der Dienst von Core antwortet nicht.");
});

test("Discord row: Eternal-Assemble keeps the Decky name Discord", () => {
  const forks = JSON.parse(readFileSync(new URL("../../Shared/ProtectedPluginForks.json", import.meta.url), "utf8"));
  const assemble = forks.forks.find(fork => fork.id === "eternal-assemble");
  assert.deepEqual([...DISCORD_PLUGIN_NAMES], assemble.pluginNames, "same names as the fork list");
  assert.equal(discordInstalled({ plugins: [{ name: "Playhub" }, { name: "Discord" }] }), true);
  assert.equal(discordInstalled({ plugins: [], installedPlugins: ["discord"] }), true);
  assert.equal(discordInstalled({ plugins: [{ name: "Awesome Mix" }] }), false);
  assert.equal(discordInstalled(undefined), false);
});

test("every language has every line, with the same placeholders", () => {
  assert.equal(voiceLanguages.length, 12);
  const en = voiceCopy("en");
  for (const language of voiceLanguages) {
    const table = voiceCopy(language);
    for (const key of Object.keys({ ...en })) {
      const value = table[key];
      assert.ok(typeof value === "string" && value.length > 0, `${language}/${key}`);
      assert.deepEqual((value.match(/\{\w+\}/g) ?? []).sort(), (en[key].match(/\{\w+\}/g) ?? []).sort(), `${language}/${key} placeholders`);
      assert.ok(!/Playhub/.test(value), `${language}/${key} says Core, not Playhub`);
    }
  }
  assert.equal(voiceCopy("it-IT").title, "Voce");
  assert.equal(voiceCopy("xx").title, "Voice");
});

test("the section: Core palette, no glow, controller-navigable, closes Quick Access before the bar", () => {
  const source = readFileSync(new URL("../src/VoiceSection.tsx", import.meta.url), "utf8");
  for (const colour of ["#000000", "#D7263D", "#C9D1D9", "#1A1D21"]) assert.ok(source.includes(colour), colour);
  assert.ok(!/blur\(|drop-shadow|box-shadow:(?!none)|text-shadow:(?!none)/.test(source), "no haze or glow");
  assert.match(source, /Focusable/);
  assert.match(source, /DialogButton/);
  assert.match(source, /DropdownItem/, "the calls mic and the scope are controller dropdowns");
  assert.match(source, /ToggleField label=\{text\.everything\}/);
  assert.ok(source.indexOf("CloseSideMenus") < source.indexOf("core_voice_party"), "Quick Access closes before the Game Bar opens");
});

test("backend: the mic action is validated and the top-bar mark is Core crimson", () => {
  const backend = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  assert.match(backend, /_VOICE_MIC_ACTIONS = \("mute", "unmute", "toggle"\)/);
  assert.match(backend, /if action not in _VOICE_MIC_ACTIONS:/);
  assert.match(backend, /color:#D7263D/);
  assert.match(backend, /mic\.get\("state"\) in \("muted", "partial"\)/, "the mark shows a partly muted microphone too");
  assert.match(backend, /"\/voice\/party"/);
  assert.match(backend, /_VOICE_MUTE_SCOPES = \("all", "calls"\)/);
  assert.match(backend, /if scope not in _VOICE_MUTE_SCOPES:/);
  assert.match(backend, /_VOICE_DEVICE_ID\.fullmatch\(device_id\)/, "only Windows endpoint ids reach the agent");
  assert.match(backend, /"\/voice\/calls-mic\?" \+ query/);
  assert.equal((backend.match(/"\/voice\/state", timeout=\d, maximum=_VOICE_STATE_MAXIMUM\)/g) ?? []).length, 2, "the device list fits in every state read");
  const index = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  assert.match(index, /<VoiceSection locale=\{locale\} \/>/);
});
