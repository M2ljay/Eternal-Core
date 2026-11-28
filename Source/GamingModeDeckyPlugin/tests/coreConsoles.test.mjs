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
  const source = readFileSync(new URL("../src/coreHome.tsx", import.meta.url), "utf8");
  assert.match(source, /repeat\(\$\{CONSOLE_TILE\.across\},minmax\(0,1fr\)\)/, "eight across, scaled to the view");
  assert.match(source, /onActivate=\{\(\) => \{ try \{ Navigation\?\.Navigate\?\.\(collectionRoute\(tile\.id\)\)/);
  assert.doesNotMatch(source, /colorgrade[^"]*\.png/i, "no pack file is named in the source");
});
