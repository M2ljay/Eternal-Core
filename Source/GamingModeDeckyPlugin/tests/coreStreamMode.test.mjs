import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Core Stream mode in Big Picture: the Steam Remote Play watch that reports to the agent, the Quick
// Access section's lines in 12 languages, the Health row, and the backend's validation.
function load(file) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, Promise });
  return exports;
}
const { steamStreamReport, installSteamStreamWatch } = load("streamMode.ts");
const { streamView, streamCopy, streamLanguages, streamSourceLabel } = load("streamReport.ts");
const { healthLines, healthCopy, healthLanguages } = load("healthReport.ts");

test("Steam report: a device with a streaming session id is streaming", () => {
  assert.deepEqual({ ...steamStreamReport([{ clientName: "Steam Frame", status: "Connected", unStreamingSessionID: 0 }]) }, { active: false, device: "" });
  assert.deepEqual({ ...steamStreamReport([
    { clientName: "ROG Flow Z13", status: "Paired", unStreamingSessionID: 0 },
    { clientName: "Steam Frame\u0007", status: "Connected", unStreamingSessionID: 7 },
  ]) }, { active: true, device: "Steam Frame" });
  assert.equal(steamStreamReport(undefined).active, false);
  assert.equal(steamStreamReport([null, { unStreamingSessionID: "x" }, { unStreamingSessionID: -3 }]).active, false, "nonsense ids are not a stream");
  assert.equal(steamStreamReport([{ clientName: "x".repeat(200), unStreamingSessionID: 1 }]).device.length, 64);
});

test("Steam watch: reports on change at once, repeats on a timer, unregisters", async () => {
  const reports = [];
  let callback, unregistered = false, scheduled = 0, cancelled = false;
  const remote = { RegisterForDevicesChanges: fn => { callback = fn; return { unregister: () => { unregistered = true; } }; } };
  const timers = [];
  const stop = installSteamStreamWatch({
    remotePlay: () => remote,
    report: report => { reports.push({ ...report }); return Promise.resolve(); },
    schedule: (fn, delay) => { scheduled++; timers.push(fn); assert.equal(delay, 30000); return scheduled; },
    cancel: () => { cancelled = true; },
  });
  assert.deepEqual(reports, [{ active: false, device: "" }], "first report at once");
  callback([{ clientName: "AYN Thor", unStreamingSessionID: 3 }]);
  assert.deepEqual(reports.at(-1), { active: true, device: "AYN Thor" }, "a stream starting is reported at once");
  callback([{ clientName: "AYN Thor", unStreamingSessionID: 3 }]);
  assert.equal(reports.length, 2, "the same state is not re-sent on every Steam callback");
  timers.shift()();
  assert.equal(reports.length, 3, "but it is repeated by the timer, so the agent does not forget it");
  callback([]);
  assert.deepEqual(reports.at(-1), { active: false, device: "" }, "the stream ending is reported at once");
  stop();
  assert.ok(unregistered && cancelled);
  callback([{ clientName: "late", unStreamingSessionID: 1 }]);
  assert.equal(reports.at(-1).active, false, "nothing after stop");
});

test("Steam watch: no RemotePlay API, a throwing report, a failing agent: never throws", () => {
  const stop = installSteamStreamWatch({ remotePlay: () => undefined, report: () => { throw new Error("down"); }, schedule: () => 1, cancel: () => {} });
  stop();
  const stop2 = installSteamStreamWatch({ remotePlay: () => { throw new Error("no Steam"); }, report: () => Promise.reject(new Error("down")), schedule: () => 1, cancel: () => {} });
  stop2();
});

test("Quick Access lines: off, on with the device, forced, sound, Apollo", () => {
  const off = streamView({ active: false, forced: false, apollo: { installed: false } }, "en");
  assert.equal(off.state, "off");
  assert.equal(off.line, "Off");
  assert.equal(off.detail, "Turns on by itself when this PC streams to another device.");
  assert.equal(off.apollo, "Apollo: not installed (optional, see Core's guide)");
  const frame = streamView({ active: true, detected: true, source: "steam", device: "Steam Frame", audio: { enabled: true, mode: "virtual", device: "Speakers (Steam Streaming Speakers)" }, apollo: { installed: true, running: false } }, "en");
  assert.equal(frame.line, "On · Steam Frame");
  assert.match(frame.detail, /does not wake the TV/);
  assert.equal(frame.audio, "Sound: on the stream (Speakers (Steam Streaming Speakers))");
  assert.equal(frame.apollo, "Apollo: installed, not streaming");
  const vr = streamView({ active: true, detected: true, source: "steamvr", device: null, audio: { enabled: true, mode: "muted" } }, "en");
  assert.equal(vr.line, "On · SteamVR");
  assert.equal(vr.audio, "Sound: TV speaker muted");
  const apollo = streamView({ active: true, detected: true, source: "apollo", device: "AYN Odin 2 Portal", audio: { enabled: true, mode: "already" }, apollo: { installed: true, running: true, virtualDisplay: true } }, "en");
  assert.equal(apollo.apollo, "Apollo: virtual display active");
  assert.equal(apollo.audio, "Sound: already on the stream");
  const forced = streamView({ active: true, forced: true, detected: false, audio: { enabled: false } }, "en");
  assert.equal(forced.line, "On · kept on by you");
  assert.equal(forced.forced, true);
  assert.equal(forced.audio, "Sound: left as it is (turned off in settings)");
  assert.equal(streamView(null, "en").line, "Core's service is not answering.");
  assert.equal(streamView(undefined, "en").state, "unknown");
  assert.equal(streamView({ active: false }, "it").line, "Spenta");
  assert.equal(streamSourceLabel("nope"), "");
});

test("every language has every line, with the same placeholders", () => {
  assert.equal(streamLanguages.length, 12);
  const en = streamCopy("en");
  for (const language of streamLanguages) {
    const table = streamCopy(language);
    for (const key of Object.keys({ ...en })) {
      const value = table[key];
      assert.ok(typeof value === "string" && value.length > 0, `${language}/${key}`);
      assert.deepEqual((value.match(/\{\w+\}/g) ?? []).sort(), (en[key].match(/\{\w+\}/g) ?? []).sort(), `${language}/${key} placeholders`);
      assert.ok(!/Playhub/.test(value), `${language}/${key} says Core, not Playhub`);
    }
  }
  assert.equal(streamCopy("it-IT").keep, "Lascia stare la TV");
  assert.equal(streamCopy("xx").title, "Stream mode");
});

test("Health: the Streaming row and a TV left alone", () => {
  const now = new Date("2026-11-26T20:00:00-05:00");
  const line = (row) => healthLines({ attention: 0, rows: [row] }, "en", now)[0];
  assert.equal(line({ id: "streaming", level: "ok", state: "idle", data: {} }).value, "Idle");
  const to = line({ id: "streaming", level: "info", state: "streaming", data: { source: "steam", device: "Steam Frame" } });
  assert.equal(to.title, "Streaming");
  assert.equal(to.value, "Streaming to Steam Frame");
  assert.deepEqual([...to.details], ["Through Steam Remote Play"]);
  const apollo = line({ id: "streaming", level: "info", state: "streaming", data: { source: "apollo", device: null, virtualDisplay: true } });
  assert.equal(apollo.value, "Streaming · Apollo");
  assert.deepEqual([...apollo.details], ["Through Apollo", "Apollo's virtual display is active"]);
  assert.equal(line({ id: "streaming", level: "info", state: "kept-out", data: { forced: true } }).value, "TV kept out by you");
  assert.equal(line({ id: "tv", level: "info", state: "stream-mode", data: {} }).value, "Left alone (Stream mode)");
  assert.equal(healthLines({ rows: [{ id: "streaming", level: "ok", state: "idle", data: {} }] }, "it", now)[0].value, "Inattivo");
  for (const language of healthLanguages) for (const key of ["r_stream", "sIdle", "sTo", "sVia", "sKept", "tHeld", "sThrough", "sVd"]) {
    assert.ok(healthCopy(language)[key] && healthCopy(language)[key] !== key, `${language}/${key}`);
  }
});

test("the section and the backend: Core palette, validated calls, wired in", () => {
  const source = readFileSync(new URL("../src/StreamSection.tsx", import.meta.url), "utf8");
  assert.match(source, /from "\.\/corePalette"/, "colours come from the Core palette");
  for (const token of ["black", "crimson", "steel", "gunmetal"]) assert.match(source, new RegExp(`\\b${token}: [A-Z_]+\\b`), token);
  assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(source), "no hard-coded colours");
  assert.ok(!/blur\(|drop-shadow|box-shadow:(?!none)|text-shadow:(?!none)/.test(source), "no haze or glow");
  assert.match(source, /ToggleField label=\{text\.keep\}/);
  assert.match(source, /"core_stream_keep_tv_out"/);
  const backend = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  assert.match(backend, /async def core_stream_keep_tv_out\(self, on\):\r?\n\s+if not isinstance\(on, bool\):/);
  assert.match(backend, /async def core_stream_report\(self, active, device\):\r?\n\s+if not isinstance\(active, bool\) or not isinstance\(device, str\):/);
  assert.match(backend, /character\.isprintable\(\)\)\[:64\]/, "device names are trimmed before they reach the agent");
  assert.match(backend, /"\/stream\/steam\?" \+ query/);
  const index = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  assert.match(index, /<StreamSection locale=\{locale\} \/>/);
  assert.match(index, /installSteamStreamWatch\(\{/);
  assert.match(index, /stopSteamStreamWatch,/);
});
