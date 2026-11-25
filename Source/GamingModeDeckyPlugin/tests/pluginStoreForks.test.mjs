import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core: the Big Picture plugin store never offers, or installs, an upstream plugin over one
// of the user's forks (Source/Shared/ProtectedPluginForks.json).
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const modules = new Map();
function load(file) {
  if (modules.has(file)) return modules.get(file);
  const exports = {};
  modules.set(file, exports);
  const compiled = ts.transpileModule(readFileSync(file, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
  }).outputText;
  vm.runInNewContext(compiled, {
    exports, URL, Set, Map, Date, Number, JSON, Intl, Promise, Error, String,
    window: {},
    require: (specifier) => {
      const path = resolve(dirname(file), specifier);
      if (path.endsWith(".json")) return JSON.parse(readFileSync(path, "utf8"));
      return load(path + ".ts");
    },
  });
  return exports;
}

const catalogModule = load(join(root, "src/pluginStoreCatalog.ts"));
const forks = load(join(root, "src/pluginStoreForks.ts"));
const decky = load(join(root, "src/pluginStoreDecky.ts"));
const catalog = catalogModule.parseCatalog(readFileSync(join(root, "../../catalog/plugins.json"), "utf8"));
const entry = (repository) => catalog.plugins.find((plugin) => plugin.repository === repository);

// Names and versions exactly as Decky reports the user's installed forks.
const installedForks = [
  { name: "RomM-Dock", version: "0.33.0-win.17" },
  { name: "Nexus Mods", version: "1.21.32-anygame.57" },
  { name: "Awesome Mix", version: "1.0.0" },
  { name: "Discord", version: "1.0.3" },
];

test("the shared fork list loads and every superseded repository is in the shipped catalogue", () => {
  const ids = forks.PROTECTED_FORKS.map((fork) => fork.id).sort();
  assert.deepEqual(ids, ["eternal-assemble", "eternal-collector", "eternal-forge", "eternal-mix"]);
  for (const fork of forks.PROTECTED_FORKS) {
    for (const upstream of fork.supersedes) assert.ok(entry(upstream), `${fork.id}: ${upstream} missing from catalog`);
  }
  assert.throws(() => forks.parseProtectedForks({ schemaVersion: 2, forks: [] }));
});

test("without protection the store would offer upstream updates over two forks (the original risk)", () => {
  assert.equal(catalogModule.pluginHasUpdate(entry("RedRanger14/decky-nexus"), installedForks), true);
  assert.equal(catalogModule.pluginHasUpdate({ ...entry("Crimson3076/romm-dock"), version: "0.34.0" }, installedForks), true);
  assert.equal(catalogModule.pluginHasUpdate({ ...entry("LoZazaMastro/Playhub-Discord"), version: "1.0.4" }, installedForks), true);
});

test("installed forks hide the upstream entries they replace and nothing else", () => {
  const plugins = [
    { ...entry("Crimson3076/romm-dock"), version: "0.34.0" },
    entry("RedRanger14/decky-nexus"),
    { ...entry("LoZazaMastro/Playhub-Discord"), version: "1.0.4" },
    entry("LoZazaMastro/Now-Playing"),
    entry("danielcopper/romm-tender"),
  ];
  const visible = forks.withoutShieldedPlugins(plugins, installedForks).map((plugin) => plugin.repository);
  assert.deepEqual(visible, ["LoZazaMastro/Now-Playing", "danielcopper/romm-tender"]);
  for (const plugin of plugins.slice(0, 3)) assert.equal(forks.isShieldedByInstalledFork(plugin, installedForks), true);
});

test("genuine upstream installs keep their updates", () => {
  const upstream = [{ name: "RomM-Dock", version: "0.32.0" }, { name: "Nexus Mods", version: "1.21.30" }];
  for (const repository of ["Crimson3076/romm-dock", "RedRanger14/decky-nexus"]) {
    assert.equal(forks.isShieldedByInstalledFork(entry(repository), upstream), false);
    assert.equal(catalogModule.pluginHasUpdate(entry(repository), upstream), true);
  }
  assert.equal(forks.withoutShieldedPlugins([entry("Crimson3076/romm-dock")], []).length, 1);
});

test("a Decky install request for a shielded entry is refused before reaching Decky", async () => {
  const plugin = { ...entry("RedRanger14/decky-nexus") };
  await assert.rejects(
    decky.requestDeckyInstall(plugin, { url: plugin.catalogReleaseUrl, name: plugin.releaseAssetName, version: plugin.version, hash: "" }, installedForks),
    /personal fork/,
  );
});
