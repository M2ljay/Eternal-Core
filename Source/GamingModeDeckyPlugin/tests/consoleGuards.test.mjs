import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Console guards in Quick Access > Core > Health (docs/eternal/CONSOLE-GUARDS.md): the agent's rows become
// lines with their one-button fixes, in Core's 12 languages, and the backend only forwards known actions.
const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/healthReport.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const { healthLines, healthCopy, healthLanguages } = exports;
const now = new Date("2026-11-26T20:00:00-05:00");
const ago = hours => new Date(now.getTime() - hours * 3600000).toISOString();
const line = (row, locale = "en") => healthLines({ attention: row.level === "attention" ? 1 : 0, rows: [row], alerts: [] }, locale, now)[0];

test("every console row has a title in every language", () => {
  const ids = ["display", "sound-output", "controllers", "steam-sign-in", "auto-sign-in", "bitlocker", "windows-prompts", "update-policy", "launcher-updates", "launcher-focus", "wake-check"];
  for (const language of healthLanguages) {
    const titles = ids.map(id => line({ id, level: "unknown", state: "unknown", data: {} }, language).title);
    assert.equal(new Set(titles).size, ids.length, `${language}: distinct titles`);
    assert.ok(titles.every(title => title && !/^r_/.test(title)), `${language}: translated titles`);
  }
  assert.equal(line({ id: "display", level: "unknown", state: "unknown", data: {} }, "it").title, "Schermo");
});

test("display: changed offers Restore always, Save only when open", () => {
  const changed = line({ id: "display", level: "attention", state: "changed", data: {
    saved: "3840x2160 120 Hz HDR primary", current: "3840x2160 60 Hz primary", monitor: "LG TV SSCR2", vrrReadable: true,
    differences: ["refresh", "hdr"], fix: "display-restore", actions: ["display-save"] } });
  assert.equal(changed.value, "Changed: refresh rate, HDR");
  assert.deepEqual([...changed.details], ["LG TV SSCR2", "Normal: 3840x2160 120 Hz HDR primary", "Now: 3840x2160 60 Hz primary"]);
  assert.deepEqual(JSON.parse(JSON.stringify(changed.actions.map(action => [action.id, action.label, action.fix]))),
    [["display-restore", "Restore display settings", true], ["display-save", "Save current display as normal", false]]);
  const normal = line({ id: "display", level: "ok", state: "normal", data: { current: "3840x2160 120 Hz HDR primary", actions: ["display-save"] } });
  assert.equal(normal.value, "Normal · 3840x2160 120 Hz HDR primary");
  assert.ok(normal.details.some(detail => /G-Sync and FreeSync/.test(detail)), "says G-Sync/FreeSync can't be read");
  assert.equal(line({ id: "display", level: "info", state: "missing", data: {} }).value, "TV not connected");
  assert.equal(line({ id: "display", level: "info", state: "not-saved", data: {} }).value, "Not saved yet");
});

test("sound, controllers and Steam", () => {
  const sound = line({ id: "sound-output", level: "attention", state: "changed", data: { saved: "LG TV SSCR2 (NVIDIA High Definition Audio)", current: "Headset", fix: "sound-restore", actions: ["sound-save"] } });
  assert.equal(sound.value, "Now: Headset");
  assert.equal(sound.actions[0].label, "Switch sound back");
  assert.equal(line({ id: "sound-output", level: "info", state: "stream-mode", data: {} }).value, "Left to Stream mode");
  const pads = line({ id: "controllers", level: "info", state: "expected-missing", data: { count: 2, missing: ["057E:0306"],
    controllers: [{ kind: "steam", name: "Valve controller (Steam Controller or Puck)", key: "28DE:1302" }, { kind: "sony", name: "DualSense", key: "054C:0CE6" }] } });
  assert.equal(pads.value, "Expected, not seen: 1");
  assert.equal(pads.level, "info", "information, never an alarm");
  assert.deepEqual([...pads.details], ["Valve controller (Steam Controller or Puck) (28DE:1302)", "DualSense (054C:0CE6)"]);
  const out = line({ id: "steam-sign-in", level: "attention", state: "signed-out", data: {} });
  assert.equal(out.value, "Not signed in");
  assert.match(out.details[0], /Steam Guard/);
  assert.equal(line({ id: "steam-sign-in", level: "ok", state: "signed-in", data: { persona: "Vonn\u0007" } }).value, "Signed in · Vonn", "control characters stripped");
});

test("sign-in and BitLocker never show a secret, and point to the desktop where Windows needs it", () => {
  const off = line({ id: "auto-sign-in", level: "attention", state: "off", data: { user: "vonn", guide: "autologon" } });
  assert.equal(off.value, "Off: Windows will ask for the password");
  assert.ok(off.details.some(detail => /Stays a console/.test(detail)));
  assert.equal(off.actions, undefined, "no button: Core stores no password");
  assert.ok(line({ id: "auto-sign-in", level: "ok", state: "on", data: { user: "vonn", plainTextPassword: true } }).details.some(detail => /plain text/.test(detail)));
  const firmware = line({ id: "bitlocker", level: "attention", state: "firmware-pending", data: { fix: "bitlocker-suspend", backupSeenAt: ago(24 * 30), confirmAt: "account.microsoft.com/devices/recoverykey" } });
  assert.equal(firmware.value, "Firmware update waiting");
  assert.equal(firmware.actions[0].label, "Suspend BitLocker for one restart");
  assert.ok(firmware.details.includes("Confirm the key backup at account.microsoft.com/devices/recoverykey"));
  assert.ok(firmware.details.some(detail => /Windows logged a backup/.test(detail)));
  assert.equal(line({ id: "bitlocker", level: "info", state: "unchecked", data: {} }).details[0], "Check it on the desktop: Core > Gaming Mode > Stays a console.");
  assert.equal(line({ id: "bitlocker", level: "ok", state: "off", data: {} }).value, "Off");
  assert.ok(!/\d{6}-\d{6}/.test(JSON.stringify(firmware)), "no recovery key digits anywhere");
});

test("Windows prompts, update policy, launchers and the wake check", () => {
  const prompts = line({ id: "windows-prompts", level: "info", state: "on", data: { fix: "prompts-off" } });
  assert.equal(prompts.actions[0].label, "Turn off setup screens");
  assert.equal(line({ id: "update-policy", level: "ok", state: "applied", data: { pinnedVersion: "24H2" } }).value, "Drivers held · feature updates stay on 24H2");
  assert.equal(line({ id: "update-policy", level: "ok", state: "applied-home", data: { home: true } }).value, "Drivers held · Home can't hold feature updates");
  assert.ok(line({ id: "update-policy", level: "info", state: "not-applied", data: {} }).details[0].includes("Quiet Windows"));
  assert.equal(line({ id: "launcher-updates", level: "info", state: "pending", data: { pending: ["Easy Anti-Cheat", "Riot Vanguard"] } }).value, "Waiting: Easy Anti-Cheat, Riot Vanguard");
  assert.equal(line({ id: "launcher-focus", level: "ok", state: "watching", data: { pushedToday: 2, last: "EA app" } }).value, "Watching · 2 today");
  const wake = line({ id: "wake-check", level: "info", state: "fixed", data: { at: ago(1), focusRetaken: true, inputResent: true, adapter: "Ethernet", actions: ["wake-check-now"] } });
  assert.equal(wake.value, "Fixed by Core · 1 hour ago");
  assert.deepEqual([...wake.details], ["Big Picture was brought back to the front.", "The TV input switch was sent again.", "Adapter: Ethernet",
    "Temperatures: not shown (Windows has no driver-free reading)."]);
  assert.equal(line({ id: "wake-check", level: "attention", state: "network-down", data: {} }).value, "Network cable unplugged");
});

test("unknown actions from the agent are not offered", () => {
  const row = line({ id: "display", level: "attention", state: "changed", data: { fix: "format-c", actions: ["display-save", "reboot-now", 7] } });
  assert.deepEqual([...row.actions.map(action => action.id)], ["display-save"]);
});

test("the Steam update row shows in Quick Access again", () => {
  const steam = line({ id: "steam-update", level: "info", state: "postponed", data: { reason: "pending-unknown", pending: "1795000000" } });
  assert.equal(steam.title, "Steam update");
  assert.equal(steam.value, "Postponed: Core can't tell yet if the new Steam works with Decky");
});

test("action labels and results exist in every language", () => {
  const en = healthCopy("en");
  const keys = Object.keys({ ...en }).filter(key => /^(a[A-Z]|r_(display|sound|controllers|steamSign|autoSign|bitlocker|prompts|updPolicy|launchUpd|launchFocus|wake))/.test(key));
  assert.equal(keys.length, 27, "11 row titles, 8 actions, 3 results and the 5 actions of 3.3.5 (CoreHealth emulator rows)");
  for (const language of healthLanguages) {
    const table = healthCopy(language);
    for (const key of keys) {
      assert.ok(typeof table[key] === "string" && table[key] !== key, `${language}/${key}`);
      if (language !== "en" && key !== "r_bitlocker") assert.notEqual(table[key], en[key], `${language}/${key} is translated`);
    }
  }
});

test("the section calls the backend, which forwards only the agent's actions", () => {
  const section = readFileSync(new URL("../src/HealthSection.tsx", import.meta.url), "utf8");
  assert.match(section, /call<\[string\], ActionAnswer>\("core_health_action", action\.id\)/);
  const main = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  const python = main.match(/_HEALTH_ACTIONS = \(([^)]*)\)/)[1].match(/"([a-z-]+)"/g).map(text => text.slice(1, -1));
  const agent = readFileSync(new URL("../../GamingModeAgent/GamingMode.Services/ConsoleGuards.cs", import.meta.url), "utf8");
  const csharp = agent.match(/internal static readonly string\[\] Actions = \{([^}]*)\}/)[1].match(/"([a-z-]+)"/g).map(text => text.slice(1, -1));
  assert.deepEqual(python, csharp, "main.py forwards exactly the agent's actions");
  assert.match(main, /async def core_health_action\(self, action\):\s+if action not in _HEALTH_ACTIONS and not \(isinstance\(action, str\) and re\.fullmatch\(_EMULATOR_ACTION, action\)\):\s+return \{\}/);
  const bundled = readFileSync(new URL("../../Playhub/Assets/GamingModeDeckyPlugin/gaming-mode/main.py", import.meta.url), "utf8");
  assert.equal(bundled, main, "the bundled main.py matches");
});
