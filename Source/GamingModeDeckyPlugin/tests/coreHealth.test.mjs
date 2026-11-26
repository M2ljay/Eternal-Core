import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/coreHealth.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports, Promise, Number, String });
const settle = () => new Promise(done => setImmediate(done));

test("heartbeat: beats at once, then on every interval, and stops on dispose", async () => {
  let next, beats = 0, cancelled = false;
  const stop = exports.installCoreHeartbeat({ beat: async () => { beats++; }, schedule: fn => { next = fn; return 1; }, cancel: () => { cancelled = true; } });
  await settle();
  assert.equal(beats, 1);
  next(); await settle(); next(); await settle();
  assert.equal(beats, 3);
  stop();
  assert.ok(cancelled);
  next(); await settle();
  assert.equal(beats, 3);
});

test("heartbeat: a failing or throwing agent call never breaks the loop", async () => {
  let next, beats = 0;
  exports.installCoreHeartbeat({ beat: () => { beats++; if (beats === 1) throw new Error("sync"); return Promise.reject(new Error("down")); }, schedule: fn => { next = fn; }, cancel: () => {} });
  await settle(); next(); await settle(); next(); await settle();
  assert.equal(beats, 3);
});

const info = { name: "Core Repair", exe: "C:\\Users\\v\\AppData\\Local\\Programs\\Playhub\\Plugins\\Gaming Mode\\gaming-mode-win-x64\\GamingMode.exe", startDir: "C:\\x", launchOptions: "repair" };
function fixture(over = {}) {
  const calls = [], overviews = new Map(); let saved = null;
  const apps = {
    AddShortcut: async (...args) => { calls.push(["AddShortcut", ...args]); overviews.set(77, { display_name: "GamingMode" }); return 77; },
    SetShortcutName: (id, v) => { calls.push(["SetShortcutName", id, v]); overviews.set(id, { display_name: v }); },
    SetShortcutExe: (id, v) => calls.push(["SetShortcutExe", id, v]),
    SetShortcutStartDir: (id, v) => calls.push(["SetShortcutStartDir", id, v]),
    SetShortcutLaunchOptions: (id, v) => calls.push(["SetShortcutLaunchOptions", id, v]),
  };
  const deps = { info: async () => info, apps: () => apps, overview: id => overviews.get(id), shortcuts: () => [], read: () => saved, write: v => { saved = v; }, sleep: async () => {}, ...over };
  return { deps, calls, overviews, saved: () => saved, setSaved: v => { saved = v; } };
}

test("tile: created once with every field set after the overview exists", async () => {
  const f = fixture();
  assert.equal(await exports.ensureCoreRepairTile(f.deps), "created");
  assert.deepEqual(f.calls.map(c => c[0]), ["AddShortcut", "SetShortcutName", "SetShortcutExe", "SetShortcutStartDir", "SetShortcutLaunchOptions"]);
  assert.deepEqual(f.calls.at(-1), ["SetShortcutLaunchOptions", 77, "repair"]);
  assert.equal(f.saved(), "77");
  assert.equal(await exports.ensureCoreRepairTile(f.deps), "present");
  assert.equal(f.calls.length, 5);
});

test("tile: an existing Core Repair shortcut is adopted, never duplicated", async () => {
  const f = fixture({ shortcuts: () => [{ display_name: "Other", app_type: 1073741824, appid: 5 }, { display_name: "Core Repair", app_type: 1073741824, appid: 9 }] });
  assert.equal(await exports.ensureCoreRepairTile(f.deps), "present");
  assert.equal(f.calls.length, 0);
  assert.equal(f.saved(), "9");
});

test("tile: unavailable agent answers or missing Steam APIs create nothing", async () => {
  for (const bad of [null, {}, { ...info, launchOptions: "shell" }, { ...info, exe: "notepad" }]) {
    const f = fixture({ info: async () => bad });
    assert.equal(await exports.ensureCoreRepairTile(f.deps), "unavailable");
    assert.equal(f.calls.length, 0);
  }
  const f = fixture({ apps: () => ({ AddShortcut: async () => 1 }) });
  assert.equal(await exports.ensureCoreRepairTile(f.deps), "unavailable");
});

test("tile: Steam errors are reported as failed, not thrown", async () => {
  const f = fixture({ apps: () => ({ AddShortcut: async () => { throw new Error("x"); }, SetShortcutName() {}, SetShortcutExe() {}, SetShortcutStartDir() {}, SetShortcutLaunchOptions() {} }) });
  assert.equal(await exports.ensureCoreRepairTile(f.deps), "failed");
});
