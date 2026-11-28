import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

// Eternal Core 3.3.9: the Consoles tab's tiles (docs/eternal/CORE-UI.md, "The Consoles tab"), from fake collections and
// fake pack slugs. The user's ColorGrade pack itself is never used in tests.

const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 };
const cache = new Map();
function load(file) {
  if (cache.has(file)) return cache.get(file);
  const exports = {};
  const code = ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), { compilerOptions }).outputText;
  const local = { "./coreLibraryIcons": "coreLibraryIcons.ts", "./coreArtwork": "coreArtwork.ts" };
  vm.runInNewContext(code, { exports, require: name => local[name] ? load(local[name]) : {}, Map, Set, Array, Object, JSON, Math, Number, String, RegExp, encodeURIComponent, console });
  cache.set(file, exports);
  return exports;
}
const consoles = load("coreConsoles.ts");
const slugs = new Set(["psx", "ps2", "ps3", "psp", "psvita", "nes", "snes", "n64", "gc", "wii", "switch", "gba", "nds", "n3ds", "genesis", "saturn", "dreamcast", "xbox", "neogeo", "mame"]);
const apps = n => Array.from({ length: n }, (_, i) => ({ appid: i + 1 }));

test("every console in the library is one tile, by family order then name, with the pack's slug", () => {
  const collections = [
    { id: "c1", displayName: "PlayStation 2", allApps: apps(3) }, { id: "c2", displayName: "RomM: PlayStation 2 (romm.local)", allApps: apps(40) },
    { id: "c3", displayName: "RomM: Nintendo 64 (romm.local)", allApps: apps(5) }, { id: "c4", displayName: "Sega Genesis", allApps: apps(2) },
    { id: "c5", displayName: "Favorites", allApps: apps(9) }, { id: "c6", displayName: "VR", allApps: apps(4) },
    { id: "c7", displayName: "GameCube", allApps: apps(1) }, { id: "c8", displayName: "Xbox 360", allApps: apps(6) },
    { id: "c9", displayName: "Arcade", allApps: apps(8) }, { id: "c10", displayName: "WonderSwan", allApps: apps(2) },
  ];
  const tiles = consoles.consoleTiles(collections, slugs);
  assert.deepEqual([...tiles.map(tile => tile.name)], ["PlayStation 2", "GameCube", "Nintendo 64", "Sega Genesis", "Xbox 360", "Arcade", "WonderSwan"]);
  assert.equal(tiles[0].id, "c2", "the console's collection with the most games");
  assert.deepEqual([...tiles.map(tile => tile.slug ?? null)], ["ps2", "gc", "n64", "genesis", null, "mame", null], "no xbox360 in the pack: the tile keeps its name");
  assert.deepEqual([...tiles.map(tile => tile.family)], ["playstation", "nintendo", "nintendo", "sega", "xbox", "retro", "retro"]);
});

test("pack slugs: Core keys, name patterns and the compact name; nothing without a pack", () => {
  assert.equal(consoles.packSlugFor("PlayStation", slugs, "ps1"), "psx");
  assert.equal(consoles.packSlugFor("Nintendo 3DS", slugs, "3ds"), "n3ds");
  assert.equal(consoles.packSlugFor("Nintendo DS", slugs, "ds"), "nds");
  assert.equal(consoles.packSlugFor("PS Vita", slugs, "vita"), "psvita");
  assert.equal(consoles.packSlugFor("Sega Mega Drive", slugs, "megadrive"), "genesis");
  assert.equal(consoles.packSlugFor("Neo Geo", slugs, "neogeo"), "neogeo");
  assert.equal(consoles.packSlugFor("Switch", new Set(), "switch"), undefined);
  assert.equal(consoles.packSlugFor("Virtual Boy", new Set(["virtualboy"]), "generic"), "virtualboy");
});

test("A opens the console's collection; board 1's measures", () => {
  assert.equal(consoles.collectionRoute("uc-Ab 1"), "/library/collection/uc-Ab%201");
  assert.deepEqual({ ...consoles.CONSOLE_TILE }, { across: 8, width: 206, height: 150, gap: 20, toneBar: 4 });
  // 3.3.10: the Consoles page (coreHomeScreen.tsx): A on a console opens its collection.
  const source = readFileSync(new URL("../src/coreHomeScreen.tsx", import.meta.url), "utf8");
  assert.match(source, /const open = \(\) => \{ try \{ Navigation\?\.Navigate\?\.\(collectionRoute\(card\.id\)\)/);
  for (const file of ["coreHomeScreen.tsx", "coreHome.tsx", "coreConsolePack.ts", "coreConsoles.ts"]) {
    assert.doesNotMatch(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), /colorgrade[^"]*\.png/i, `${file}: no pack file is named in the source`);
  }
});

// 3.3.10 (the approved board, HomeConsolesPage): one card per console, most recently played first; families (The
// Collector's family collections) never appear; PS1 is "PlayStation 1".
test("3.3.10 Consoles page: most recently played first, no family collections, PlayStation 1, games kept for screen two", () => {
  const app = (appid, last) => ({ appid, display_name: `Game ${appid}`, rt_last_time_played: last });
  const collections = [
    { id: "f1", displayName: "PlayStation", allApps: [app(1, 900), app(2, 50)] }, { id: "f2", displayName: "Nintendo", allApps: [app(3, 999)] },
    { id: "f3", displayName: "Retro", allApps: [app(4, 5)] }, { id: "f4", displayName: "Sega (RomM)", allApps: [app(5, 5)] },
    { id: "c1", displayName: "PlayStation 1", allApps: [app(1, 900)] }, { id: "c2", displayName: "PlayStation 2", allApps: [app(2, 50), app(6, 700)] },
    { id: "c3", displayName: "Nintendo 64", allApps: [app(3, 999)] }, { id: "c4", displayName: "Dreamcast", allApps: [app(7, 0)] },
    { id: "c5", displayName: "Xbox", allApps: [app(8, 10)] },
  ];
  const cards = consoles.consoleCards(collections, slugs);
  assert.deepEqual([...cards.map(card => card.name)], ["Nintendo 64", "PlayStation 1", "PlayStation 2", "Xbox", "Dreamcast"], "recent first; never played last");
  assert.equal(cards[1].slug, "psx", "PlayStation 1 is the PS1");
  assert.deepEqual([...cards[2].games.map(game => game.appid)], [6, 2], "a console's games, most recently played first");
  assert.equal(cards[0].lastPlayed, 999);
  assert.ok(!cards.some(card => ["f1", "f2", "f3", "f4"].includes(card.id)), "no family collection is a console");
});
