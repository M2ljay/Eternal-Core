import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Core Health in Quick Access: the agent's GET /health answer becomes the lines the section shows.
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/healthReport.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const { healthLines, healthSummary, healthCopy, healthLanguages, formatBytes, formatWhen } = exports;
const now = new Date("2026-11-26T20:00:00-05:00");
const ago = hours => new Date(now.getTime() - hours * 3600000).toISOString();

const rows = [
  { id: "server-backup", level: "ok", state: "ok", data: { server: "V3", lastSuccessAt: ago(14), consecutiveFailures: 0, skippedPlatforms: ["ps2", "ngc"] } },
  { id: "restore-check", level: "ok", state: "ok", data: { at: ago(24 * 20) } },
  { id: "server-drive", level: "ok", state: "healthy", data: { label: "Games (E:)", freeBytes: 812e9 } },
  { id: "romm", level: "ok", state: "reachable", data: {} },
  { id: "windows-restart", level: "ok", state: "none", data: {} },
  { id: "update-night", level: "ok", state: "nothing", data: { hour: 5 } },
  { id: "tv", level: "ok", state: "reachable", data: {} },
  { id: "pc-disk", level: "ok", state: "ok", data: { drive: "C:\\", freeBytes: 300e9 } },
  { id: "core-update", level: "ok", state: "current", data: { current: "3.3.0" } },
];

test("all good: one summary line and nine rows in order", () => {
  assert.equal(healthSummary({ attention: 0, rows }, "en").line, "All good");
  const lines = healthLines({ attention: 0, rows, alerts: [] }, "en", now);
  assert.deepEqual([...lines.map(line => line.title)], ["Server backup", "Restore check", "Server drive", "RomM", "Windows restart", "Update night", "TV", "PC disk space", "Core update"]);
  assert.equal(lines[0].value, "OK · 14 hours ago");
  assert.ok(lines[0].details.includes("ROMs skipped: ps2, ngc"));
  assert.equal(lines[2].value, "Healthy · 812 GB free");
  assert.equal(lines[8].value, "Up to date · 3.3.0");
});

test("problems: count, crimson level, details and an alert to dismiss", () => {
  const answer = { attention: 2, alertsEnabled: true, rows: [
    { id: "server-backup", level: "attention", state: "failed", data: { lastSuccessAt: ago(60), consecutiveFailures: 3, message: "Proton Drive: not signed in." } },
    { id: "server-drive", level: "attention", state: "warning", data: { message: "3 uncorrected read/write errors" } },
  ], alerts: [{ id: "backup-failing", rowId: "server-backup", dismissed: false }, { id: "server-drive", rowId: "server-drive", dismissed: true }] };
  assert.equal(healthSummary(answer, "en").line, "Needs attention: 2");
  const [backup, drive] = healthLines(answer, "en", now);
  assert.equal(backup.level, "attention");
  assert.equal(backup.value, "Failed");
  assert.deepEqual([...backup.details], ["Last success 3 days ago", "3 runs in a row not OK", "Proton Drive: not signed in."]);
  assert.equal(backup.alertId, "backup-failing");
  assert.equal(drive.alertId, undefined, "a dismissed alert offers no button");
  assert.equal(healthLines({ ...answer, alertsEnabled: false }, "en", now)[0].alertId, undefined, "alerts off: nothing to dismiss");
});

test("microphone row: muted in crimson without counting as a problem", () => {
  const rowsFor = state => healthLines({ attention: 0, rows: [{ id: "microphone", level: state === "live" ? "ok" : "info", state, data: { state } }] }, "en", now)[0];
  const muted = rowsFor("muted");
  assert.equal(muted.title, "Microphone");
  assert.equal(muted.value, "Muted");
  assert.equal(muted.tone, "crimson");
  assert.equal(muted.level, "info");
  assert.equal(rowsFor("live").value, "Live");
  assert.equal(rowsFor("live").tone, undefined);
  assert.equal(rowsFor("none").value, "No microphone");
  assert.equal(healthLines({ rows: [{ id: "microphone", level: "unknown", state: "unknown", data: {} }] }, "it", now)[0].value, "Sconosciuto");
  const source = readFileSync(new URL("../src/HealthSection.tsx", import.meta.url), "utf8");
  assert.match(source, /\.core-health-row\.crimson \.core-health-value/);
});

test("server status states are distinct", () => {
  const states = ["not-configured", "unreachable", "missing", "unreadable"].map(state =>
    healthLines({ rows: [{ id: "server-backup", level: "attention", state, data: { server: "V3" } }] }, "en", now)[0].value);
  assert.deepEqual(states, ["Set the server in Core > Gaming Mode", "Server not reachable", "No status file on the server", "Status file unreadable"]);
  const stale = healthLines({ rows: [{ id: "server-backup", level: "attention", state: "stale", data: { writtenAt: ago(40) } }] }, "en", now)[0];
  assert.equal(stale.value, "Last news 40 hours ago");
  const drive = healthLines({ rows: [{ id: "server-drive", level: "unknown", state: "missing", data: {} }] }, "en", now)[0];
  assert.equal(drive.value, "Unknown");
});

test("every language has every line, with the same placeholders", () => {
  assert.equal(healthLanguages.length, 12);
  const en = healthCopy("en");
  for (const language of healthLanguages) {
    const table = healthCopy(language);
    for (const key of Object.keys({ ...en })) {
      const value = table[key];
      assert.ok(typeof value === "string" && value.length > 0, `${language}/${key}`);
      assert.deepEqual((value.match(/\{\w+\}/g) ?? []).sort(), (en[key].match(/\{\w+\}/g) ?? []).sort(), `${language}/${key} placeholders`);
      assert.ok(!/Playhub/.test(value), `${language}/${key} says Core, not Playhub`);
    }
  }
  assert.equal(healthCopy("it-IT").title, "Salute");
  assert.equal(healthCopy("xx").title, "Health");
});

test("sizes and times", () => {
  assert.equal(formatBytes(2e12, "en"), "2 TB");
  assert.equal(formatBytes(1.5e9, "en"), "1.5 GB");
  assert.equal(formatBytes(undefined, "en"), "?");
  assert.equal(formatWhen(ago(0.5), "en", now), "30 minutes ago");
  assert.equal(formatWhen("garbage", "en", now), "?");
});

test("the section uses the Core palette and no glow", () => {
  const source = readFileSync(new URL("../src/HealthSection.tsx", import.meta.url), "utf8");
  for (const colour of ["#000000", "#D7263D", "#C9D1D9", "#1A1D21"]) assert.ok(source.includes(colour), colour);
  assert.ok(!/blur\(|drop-shadow|box-shadow:(?!none)|text-shadow:(?!none)/.test(source), "no haze or glow");
  assert.match(source, /Focusable/);
});
