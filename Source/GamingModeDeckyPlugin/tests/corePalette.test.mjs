import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import test from "node:test";
import { corePalette } from "./corePaletteModule.mjs";

// The Core palette (approved 2026-11-26) lives in src/corePalette.ts; the plugin's own screens
// take their colours from it.
const { CORE, CORE_STATUS, coreAlpha } = corePalette;
const src = new URL("../src/", import.meta.url);
const read = name => readFileSync(new URL(name, src), "utf8");

const luminance = hex => {
  const channel = value => { const c = value / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  const n = Number.parseInt(hex.slice(1), 16);
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
};
const contrast = (a, b) => { const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };

test("the tokens are the approved palette", () => {
  assert.equal(CORE.black, "#000000");
  assert.equal(CORE.gunmetal, "#1A1D21");
  assert.equal(CORE.gunmetalRaised, "#2A2F36");
  assert.equal(CORE.steel, "#C9D1D9");
  assert.equal(CORE.hint, "#9AA4AE");
  assert.equal(CORE.crimson, "#D7263D");
  assert.equal(CORE.crimsonText, "#EA5A68");
  assert.equal(CORE.ok, "#6FA37F");
  assert.equal(CORE.warning, "#C98B4A");
  assert.equal(CORE.onStatus, "#000000");
  assert.deepEqual({ ...CORE_STATUS }, { ok: "#6FA37F", warning: "#C98B4A", error: "#EA5A68", neutral: "#C9D1D9" });
  assert.equal(coreAlpha("#D7263D", 0.4), "rgba(215,38,61,0.4)");
});

test("text colours keep 4.5:1 on black and gunmetal; black on the status fills", () => {
  for (const colour of [CORE.white, CORE.text, CORE.steel, CORE.hint, CORE.crimsonText, CORE.ok, CORE.warning])
    for (const ground of [CORE.black, CORE.gunmetal]) assert.ok(contrast(colour, ground) >= 4.5, `${colour} on ${ground}`);
  for (const fill of [CORE.ok, CORE.warning]) assert.ok(contrast(CORE.onStatus, fill) >= 4.5, `text on ${fill}`);
  assert.ok(contrast(CORE.white, CORE.crimson) >= 4.5, "white on crimson");
  assert.ok(contrast(CORE.white, CORE.crimsonDeep) >= 4.5, "white on pressed crimson");
});

test("no gold and no third-party brand blue anywhere in the plugin's screens", () => {
  const banned = /#(?:d4a72c|d9a337|ffc83c|ffcb0f|66c0f4|1a9fff|42d483|4dde8d|71e2a5|c42b1c)\b|\bgold\b(?!enratio)/i;
  const files = readdirSync(src, { recursive: true }).filter(name => /\.tsx?$/.test(name) && !/qamIcons|historyImages|corePalette/.test(name));
  for (const name of files) assert.ok(!banned.test(read(name)), name);
});

test("the restyled screens use tokens, not colour literals", () => {
  for (const name of ["HealthSection.tsx", "VoiceSection.tsx", "StreamSection.tsx", "UpdateNightCard.tsx", "ControlCenter.tsx",
    "DashboardPage.tsx", "PluginStorePage.tsx", "StoreDigitalStyles.tsx", "DailyHistory.tsx", "HomeNews.tsx",
    "DeviceSettings.tsx", "DeviceCapabilities.tsx", "ControllerSettings.tsx", "storeDownloadMenu.tsx", "storeLibrarySteam.tsx",
    "QamShortcutsView.tsx", "quickSettings/losslessProfiles.tsx"]) {
    const source = read(name);
    assert.match(source, /corePalette"|corePalette'/, `${name} imports the palette`);
    assert.ok(!/#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b(?![\w-])|rgba?\(\s*\d/.test(source.replace(/`#\$\{[^}]+\}`/g, "")), `${name} has a colour literal`);
  }
});
