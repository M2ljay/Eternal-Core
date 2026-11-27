import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Steam updates in Quick Access (src/SteamUpdateCard.tsx): the status line and the answers to "Update Steam now".
const transpile = source => ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
const health = {};
vm.runInNewContext(transpile(readFileSync(new URL("../src/healthReport.ts", import.meta.url), "utf8")), { exports: health });
const card = readFileSync(new URL("../src/SteamUpdateCard.tsx", import.meta.url), "utf8");
const pure = card.slice(card.indexOf("type Copy"), card.indexOf("export function SteamUpdateCard"));
const exports = {};
vm.runInNewContext(transpile(pure), { exports, healthCopy: health.healthCopy });
const { steamUpdateLine, steamUpdateAnswer, steamUpdateCopy, steamUpdateLanguages } = exports;
const plain = value => JSON.parse(JSON.stringify(value)); // objects from the vm realm

test("status line for every phase", () => {
  assert.equal(steamUpdateLine({ holdPhase: "Current" }, "en"), "Steam is up to date.");
  assert.match(steamUpdateLine({ holdPhase: "Soaking", daysLeft: 7 }, "en"), /in 7 days/);
  assert.match(steamUpdateLine({ holdPhase: "Due" }, "en"), /next update night/);
  assert.equal(steamUpdateLine({ holdPhase: "Postponed", postponedReason: "pending-unsupported" }, "en"), "Postponed: the new Steam doesn't work with the installed Decky");
  assert.equal(steamUpdateLine({ holdPhase: "Postponed", postponedReason: "pending-unknown" }, "en"), "Postponed: Core can't tell yet if the new Steam works with Decky");
  assert.equal(steamUpdateLine({ holdPhase: "Postponed", postponedReason: "file-disagrees" }, "en"), "Postponed: Core can't check compatibility with Decky");
  assert.match(steamUpdateLine({ holdPhase: "WaitingForChange" }, "en"), /failed its check/);
  assert.equal(steamUpdateLine({ holdPhase: "Off" }, "en"), "Steam updates itself, as usual.");
  assert.match(steamUpdateLine({ holdPhase: "Soaking", daysLeft: 3 }, "it-IT"), /tra 3 giorni/);
});

test("Update Steam now: one confirmation for can't tell, two for known incompatible", () => {
  assert.deepEqual(plain(steamUpdateAnswer({ ok: false, confirm: "confirm-cant-tell" }, "en")), { message: steamUpdateCopy("en").confirmCantTell, confirm: true });
  assert.deepEqual(plain(steamUpdateAnswer({ ok: false, confirm: "confirm-incompatible" }, "en")), { message: steamUpdateCopy("en").confirmIncompatible, confirm: true });
  assert.deepEqual(plain(steamUpdateAnswer({ ok: false, confirm: "confirm-incompatible-again" }, "en")), { message: steamUpdateCopy("en").confirmAgain, confirm: true });
  assert.deepEqual(plain(steamUpdateAnswer({ ok: true }, "en")), { message: steamUpdateCopy("en").started, confirm: false });
  assert.deepEqual(plain(steamUpdateAnswer({}, "en")), { message: steamUpdateCopy("en").busy, confirm: false });
});

test("every language has every line, with the same placeholders, saying Core", () => {
  assert.equal(steamUpdateLanguages.length, 12);
  const en = steamUpdateCopy("en");
  for (const language of steamUpdateLanguages) {
    const table = steamUpdateCopy(language);
    for (const key of Object.keys(en)) {
      assert.ok(typeof table[key] === "string" && table[key].length > 0, `${language}/${key}`);
      assert.deepEqual((table[key].match(/\{\w+\}/g) ?? []).sort(), (en[key].match(/\{\w+\}/g) ?? []).sort(), `${language}/${key} placeholders`);
      assert.ok(!/Playhub/.test(table[key]), `${language}/${key}`);
    }
  }
});

test("the backend offers the two calls, with a bounded confirmation count", () => {
  const backend = readFileSync(new URL("../main.py", import.meta.url), "utf8");
  assert.match(backend, /async def core_steam_update\(self\):/);
  assert.match(backend, /async def core_steam_update_now\(self, confirm=0\):/);
  assert.match(backend, /confirm < 0 or confirm > 2/);
});
