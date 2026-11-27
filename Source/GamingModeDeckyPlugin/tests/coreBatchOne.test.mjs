import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Batch 1 (docs/eternal/BATCH-1.md): the save backup and Decky rows in Quick Access > Core > Health, the bedtime
// reminder handed over in the heartbeat's answer, and Auto-Completed (100% games join the "Completed" collection).
function load(file) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports });
  return exports;
}
const { healthLines, healthLanguages } = load("healthReport.ts");
const { heartbeatToast, installCoreHeartbeat } = load("coreHealth.ts");
const completed = load("autoCompleted.ts");
const now = new Date("2026-11-27T05:20:00-05:00");
const line = (row, locale = "en") => healthLines({ attention: row.level === "attention" ? 1 : 0, rows: [row], alerts: [] }, locale, now)[0];
const plain = value => JSON.parse(JSON.stringify(value));

test("batch 1 rows have a title in every language", () => {
  for (const language of healthLanguages) {
    for (const id of ["save-backup", "decky-plugins"]) {
      const title = line({ id, level: "unknown", state: "unknown", data: {} }, language).title;
      assert.ok(title && !/^r_/.test(title), `${language}/${id}`);
    }
  }
  assert.equal(line({ id: "save-backup", level: "ok", state: "never", data: {} }, "it").title, "Backup dei salvataggi");
});

test("save backup: last night OK / failed / Ludusavi not installed", () => {
  const ok = line({ id: "save-backup", level: "ok", state: "ok", data: { recent: true, games: 41, at: "2026-11-27T05:01:00-05:00" } });
  assert.equal(ok.value, "Last night OK");
  assert.deepEqual(plain(ok.details), ["41 games backed up"]);
  assert.equal(line({ id: "save-backup", level: "ok", state: "ok", data: { recent: false, at: "2026-11-24T05:00:00-05:00" } }).value, "OK 3 days ago");
  const failed = line({ id: "save-backup", level: "attention", state: "failed", data: { recent: true, reason: "time-limit" } });
  assert.equal(failed.value, "Last night failed");
  assert.deepEqual(plain(failed.details), ["Took longer than 10 minutes and was stopped"]);
  assert.deepEqual(plain(line({ id: "save-backup", level: "attention", state: "failed", data: { recent: true, reason: "exit-3" } }).details), ["Ludusavi stopped with code 3"]);
  assert.deepEqual(plain(line({ id: "save-backup", level: "attention", state: "failed", data: { recent: true, reason: "<b>raw</b>" } }).details), [], "unknown reasons are not shown");
  const missing = line({ id: "save-backup", level: "info", state: "not-installed", data: {} });
  assert.equal(missing.value, "Ludusavi not installed");
  assert.match(missing.details[0], /winget install -e --id mtkennerly\.ludusavi/);
  assert.equal(line({ id: "save-backup", level: "info", state: "off", data: {} }, "de").value, "Aus");
});

test("decky row: names the failing plugin and one masked line", () => {
  const one = line({ id: "decky-plugins", level: "attention", state: "errors", data: { plugins: ["Forge"], errors: 2, line: "KeyError: 'name'" } });
  assert.equal(one.title, "Decky");
  assert.equal(one.value, "Forge is reporting errors");
  assert.deepEqual(plain(one.details), ["2 errors in the last 6 hours", "Last error: KeyError: 'name'"]);
  assert.equal(line({ id: "decky-plugins", level: "attention", state: "errors", data: { plugins: ["Forge", "Core"] } }).value, "Forge, Core are reporting errors");
  assert.equal(line({ id: "decky-plugins", level: "attention", state: "errors", data: { plugins: ["Forge"] } }, "it").value, "Forge segnala errori");
  const notLoaded = line({ id: "decky-plugins", level: "attention", state: "not-loaded", data: { plugins: ["Forge"], errors: 1 } });
  assert.equal(notLoaded.value, "Plugins not loaded");
  assert.equal(notLoaded.details[0], "Errors from: Forge");
  assert.equal(line({ id: "decky-plugins", level: "ok", state: "healthy", data: { plugins: [] } }).value, "Plugins loaded");
  const tidy = line({ id: "decky-plugins", level: "attention", state: "errors", data: { plugins: ["Bad\u0007Name" + "x".repeat(80)], line: "a\u0000b" } });
  assert.ok(!/[\u0000-\u001f]/.test(tidy.value + tidy.details.join("")) && tidy.value.length < 80, "control characters dropped, names capped");
});

test("heartbeat answer: a bedtime reminder is shown once, quietly when asked", async () => {
  const seen = new Set();
  const answer = { ok: true, toast: { id: "winddown-1795000000", title: "ignored", body: "It's 3:50 — good moment to save.", quiet: true } };
  assert.deepEqual(plain(heartbeatToast(answer, seen)), { id: "winddown-1795000000", title: "Core", body: "It's 3:50 — good moment to save.", quiet: true });
  assert.equal(heartbeatToast(answer, seen), undefined, "only once");
  assert.equal(heartbeatToast({ ok: true, toast: null }, seen), undefined);
  assert.equal(heartbeatToast({ toast: { id: "BAD ID", body: "x" } }, seen), undefined);
  assert.equal(heartbeatToast({ toast: { id: "a", body: "   " } }, seen), undefined);
  assert.equal(heartbeatToast(undefined, seen), undefined);

  const shown = [];
  let scheduled;
  const stop = installCoreHeartbeat({
    beat: async () => ({ ok: true, toast: { id: "winddown-2", body: "It's 3:30 — good moment to save." } }),
    schedule: (callback) => { scheduled = callback; return 1; }, cancel: () => {},
    onAnswer: value => { const toast = heartbeatToast(value, seen); if (toast) shown.push(toast.body); },
  });
  await new Promise(resolve => setImmediate(resolve));
  stop();
  assert.deepEqual(shown, ["It's 3:30 — good moment to save."]);
  assert.equal(typeof scheduled, "function");
});

// ---------- Auto-Completed ----------

function fakeSteam({ collections = [], create = true } = {}) {
  const saved = [];
  const makeCollection = (name, apps = []) => {
    const collection = {
      displayName: name, id: "uc-" + name, allApps: [...apps],
      AsDragDropCollection: () => ({ AddApps: list => collection.allApps.push(...list), RemoveApps: () => { throw new Error("never removes"); } }),
      Save: async () => { saved.push(name); },
    };
    return collection;
  };
  const store = {
    userCollections: collections.map(([name, apps]) => makeCollection(name, apps)),
    GetCollectionIDByUserTag: () => null, GetCollection: () => undefined,
    NewUnsavedCollection: create ? (name, _filter, apps) => { const made = makeCollection(name, apps); store.userCollections.push(made); return made; } : undefined,
  };
  return { store, saved };
}
const game = appId => ({ appid: appId, app_type: 1, display_name: "Game " + appId });

function deps(over = {}) {
  const { store } = over.steam ?? fakeSteam();
  return {
    settings: async () => ({ enabled: true, create: false, collection: "Completed", ...(over.settings ?? {}) }),
    progress: async appId => over.progress?.[appId] ?? { unlocked: 50, total: 50 },
    collections: () => store,
    overview: appId => (over.overviews ?? {})[appId] ?? game(appId),
  };
}

test("progress: Steam's cache, GetMyAchievementsForApp and plain lists", () => {
  assert.deepEqual(plain(completed.progressFrom({ all_unlocked: true, unlocked: 50, total: 50, percentage: 100 })), { unlocked: 50, total: 50 });
  assert.deepEqual(plain(completed.progressFrom(JSON.stringify({ result: 1, data: { achieved: { a: {}, b: {} }, unachieved: { c: {} }, hidden: {} } }))), { unlocked: 2, total: 3 });
  assert.deepEqual(plain(completed.progressFrom({ data: { achieved: [1, 2], unachieved: [], hidden: [3] } })), { unlocked: 2, total: 3 }, "locked hidden achievements count");
  assert.deepEqual(plain(completed.progressFrom([{ bAchieved: true }, { bAchieved: false }])), { unlocked: 1, total: 2 });
  assert.equal(completed.progressFrom("not json"), undefined);
  assert.equal(completed.progressFrom(null), undefined);
  assert.equal(completed.isComplete({ unlocked: 0, total: 0 }), false, "no achievements is never complete");
  assert.equal(completed.isComplete({ unlocked: 49, total: 50 }), false);
  assert.equal(completed.isComplete({ unlocked: 50, total: 50 }), true);
});

test("collection: matched by name ignoring case, created only with the setting on", async () => {
  let steam = fakeSteam({ collections: [["  completed ", []], ["Favourites", []]] });
  assert.equal(await completed.addIfCompleted(deps({ steam }), 1145360), "added");
  assert.equal(steam.store.userCollections[0].allApps[0].appid, 1145360);
  assert.equal(await completed.addIfCompleted(deps({ steam }), 1145360), "present", "already there: nothing saved again");
  assert.deepEqual(steam.saved, ["  completed "]);

  steam = fakeSteam();
  assert.equal(await completed.addIfCompleted(deps({ steam }), 10), "missing-collection");
  assert.equal(steam.store.userCollections.length, 0, "not created without the setting");
  assert.equal(await completed.addIfCompleted(deps({ steam, settings: { create: true } }), 10), "created");
  assert.equal(steam.store.userCollections[0].displayName, "Completed");
  assert.equal(await completed.addIfCompleted(deps({ steam: fakeSteam({ create: false }), settings: { create: true } }), 10), "unavailable");
});

test("only Steam games at 100%, never with the feature off", async () => {
  const steam = fakeSteam({ collections: [["Completed", []]] });
  assert.equal(await completed.addIfCompleted(deps({ steam, progress: { 5: { unlocked: 0, total: 0 } } }), 5), "no-achievements");
  assert.equal(await completed.addIfCompleted(deps({ steam, progress: { 6: { unlocked: 9, total: 10 } } }), 6), "incomplete");
  assert.equal(await completed.addIfCompleted(deps({ steam, overviews: { 7: { appid: 7, app_type: 1073741824 } } }), 7), "unavailable", "non-Steam shortcuts are skipped");
  assert.equal(await completed.addIfCompleted(deps({ steam, settings: { enabled: false } }), 8), "off");
  assert.equal(await completed.addIfCompleted({ ...deps({ steam }), collections: () => undefined }, 9), "unavailable");
  assert.equal(await completed.addIfCompleted({ ...deps({ steam }), progress: async () => { throw new Error("boom"); } }, 9), "failed", "fails soft");
  assert.equal(steam.store.userCollections[0].allApps.length, 0);
});

test("settings and recent games", () => {
  assert.deepEqual(plain(completed.settingsFrom({ enabled: true, create: true, collection: "Completed" })), { enabled: true, create: true, collection: "Completed" });
  assert.deepEqual(plain(completed.settingsFrom(undefined)), { enabled: false, create: false, collection: "Completed" }, "no agent: off");
  const nowMs = Date.parse("2026-11-27T05:00:00Z"), day = 86400;
  const apps = [
    { appid: 1, app_type: 1, rt_last_time_played: nowMs / 1000 - day },
    { appid: 2, app_type: 1, rt_last_time_played: nowMs / 1000 - 30 * day },
    { appid: 3, app_type: 1073741824, rt_last_time_played: nowMs / 1000 },
    { appid: 4, app_type: 1, rt_last_time_played: nowMs / 1000 - 3600 },
  ];
  assert.deepEqual(plain(completed.recentGames(apps, nowMs)), [4, 1]);
});

test("install: an unlock checks that game; the sweep runs once a day over recent games", async () => {
  const timers = [];
  const checked = [], reports = [];
  let callback, sweepAt = 0, clock = Date.parse("2026-11-27T05:00:00Z");
  const steam = fakeSteam({ collections: [["Completed", []]] });
  const base = deps({ steam });
  const stop = completed.installAutoCompleted({
    ...base,
    progress: async appId => { checked.push(appId); return { unlocked: 3, total: 3 }; },
    register: cb => { callback = cb; return () => { callback = undefined; }; },
    recent: () => [20, 21],
    schedule: (run, delay) => { timers.push({ run, delay }); return timers.length; },
    cancel: id => { if (timers[id - 1]) timers[id - 1].run = () => {}; },
    now: () => clock,
    readSweep: () => sweepAt, writeSweep: at => { sweepAt = at; },
    report: (appId, outcome) => reports.push([appId, outcome]),
    unlockDelay: 5, sweepCheck: 10,
  });
  const flush = () => new Promise(resolve => setTimeout(resolve, 0));
  callback({ unAppID: 1145360, nCurrentProgress: 3, nMaxProgress: 3 });
  callback({ unAppID: 1145360 });
  const unlockTimers = timers.filter(timer => timer.delay === 5);
  assert.equal(unlockTimers.length, 2, "a second unlock reschedules");
  unlockTimers.forEach(timer => timer.run());
  await flush(); await flush();
  assert.deepEqual(checked, [1145360], "one check per game");
  assert.deepEqual(reports, [[1145360, "added"]]);

  timers.find(timer => timer.delay === 10).run();
  for (let i = 0; i < 6; i++) await flush();
  assert.deepEqual(checked, [1145360, 20, 21], "daily sweep over recent games");
  assert.equal(sweepAt, clock);
  const before = checked.length;
  timers.filter(timer => timer.delay === 10).at(-1).run();
  for (let i = 0; i < 4; i++) await flush();
  assert.equal(checked.length, before, "not again the same day");
  stop();
  assert.equal(callback, undefined, "unregistered on unload");
});

test("backend forwards only checked Auto-Completed calls", () => {
  const backend = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  assert.match(backend, /async def core_completed_settings\(self\):\r?\n\s+return await asyncio\.to_thread\(self\._agent_local, "GET", "\/core\/completed"/);
  assert.match(backend, /async def core_completed_report\(self, app_id, outcome\):\r?\n\s+if isinstance\(app_id, bool\) or not isinstance\(app_id, int\)/);
  assert.match(backend, /if outcome not in \("added", "created", "missing-collection", "failed"\):/);
});
