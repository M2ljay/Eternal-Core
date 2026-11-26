// Rebrand phase 2: Decky lists the plugin as "Core"; Decky's own name-keyed lists are renamed once.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

function load() {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/deckyPluginName.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, globalThis: {} });
  return exports;
}
const api = load();
const plain = value => JSON.parse(JSON.stringify(value));

test('both names are this plugin; nothing else is', () => {
  assert.ok(api.isOwnPluginName('Core') && api.isOwnPluginName('Playhub'));
  assert.ok(!api.isOwnPluginName('core') && !api.isOwnPluginName('Shortcuts') && !api.isOwnPluginName(undefined));
});

test('Playhub is renamed in place and a later Core entry is dropped', () => {
  assert.deepEqual(plain(api.renameOwnPlugin(['Music', 'Playhub', 'Shortcuts'])), ['Music', 'Core', 'Shortcuts']);
  assert.deepEqual(plain(api.renameOwnPlugin(['Playhub', 'Music', 'Core'])), ['Core', 'Music']);
  assert.equal(api.renameOwnPlugin(['Music', 'Core']), null, 'already migrated');
  assert.equal(api.renameOwnPlugin([]), null);
  assert.equal(api.renameOwnPlugin(null), null);
  assert.equal(api.renameOwnPlugin(['Playhub', 3]), null, 'unexpected shapes are left alone');
});

test('Decky order and hidden lists are rewritten once, with the live state updated', async () => {
  const stored = { pluginOrder: ['Playhub', 'Music', 'Core'], hiddenPlugins: ['Weather'] };
  const calls = [], live = {};
  const host = {
    DeckyBackend: { call: async (route, key, value) => {
      calls.push([route, key]);
      if (route === 'utilities/settings/get') return stored[key] ?? value;
      stored[key] = value; return null;
    } },
    DeckyPluginLoader: { deckyState: { setPluginOrder: v => { live.order = v; }, setHiddenPlugins: v => { live.hidden = v; } } },
  };
  assert.deepEqual(plain(await api.migrateDeckyPluginName(host)), ['pluginOrder']);
  assert.deepEqual(plain(stored.pluginOrder), ['Core', 'Music']);
  assert.deepEqual(plain(stored.hiddenPlugins), ['Weather'], 'a list without Playhub is not written');
  assert.deepEqual(plain(live), { order: ['Core', 'Music'] });
  assert.ok(!calls.some(([route, key]) => route === 'utilities/settings/set' && key === 'hiddenPlugins'));
  assert.deepEqual(plain(await api.migrateDeckyPluginName(host)), [], 'second run changes nothing');
});

test('a hidden Playhub stays hidden as Core; backend failures are swallowed', async () => {
  const stored = { pluginOrder: [], hiddenPlugins: ['Playhub'] };
  const host = { DeckyBackend: { call: async (route, key, value) => route.endsWith('get') ? stored[key] : (stored[key] = value) } };
  assert.deepEqual(plain(await api.migrateDeckyPluginName(host)), ['hiddenPlugins']);
  assert.deepEqual(plain(stored.hiddenPlugins), ['Core']);
  const broken = { DeckyBackend: { call: async () => { throw new Error('offline'); } } };
  assert.deepEqual(plain(await api.migrateDeckyPluginName(broken)), []);
  assert.deepEqual(plain(await api.migrateDeckyPluginName({})), []);
});
