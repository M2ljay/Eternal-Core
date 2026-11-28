// Eternal Core 3.3.9: the Consoles tab's tiles (docs/eternal/CORE-UI.md, "The Consoles tab"). Every console in the
// library (a Steam collection Core recognises as a console, RomM's included) becomes one tile; A opens that collection.
// Art comes from the user's own ColorGrade pack, imported by the plugin backend into
// %APPDATA%\GamingMode\icons\colorgrade\platforms\<slug>\ (quick_settings/colorgrade_pack.py). The pack is personal and
// unlicensed: nothing of it is in this repository, its tests or its bundle.
//
// Pure: tests drive it with fake collections and fake slug lists.

import { matchCollection, plainCollectionName } from "./coreLibraryIcons";

export type ConsoleFamily = "playstation" | "nintendo" | "sega" | "xbox" | "retro";
export type ConsoleTile = { id: string; name: string; family: ConsoleFamily; key: string; slug?: string; count: number };

export const CONSOLE_FAMILY_ORDER: ConsoleFamily[] = ["playstation", "nintendo", "sega", "xbox", "retro"];
// Board 1: eight tiles across at 1920 px (206 x 150, 20 px apart); the grid keeps eight across and scales with the view.
export const CONSOLE_TILE = { across: 8, width: 206, height: 150, gap: 20, toneBar: 4 } as const;

// Pack slugs to try for each Core collection key, best first (the pack's own names, then common alternatives). The pack
// answers which slugs it has; the first one it has wins.
const SLUGS_FOR_KEY: Record<string, string[]> = {
  ps1: ["psx", "ps1", "playstation"], ps2: ["ps2"], ps3: ["ps3"], ps4: ["ps4"], ps5: ["ps5"], psp: ["psp"], vita: ["psvita", "vita"],
  nes: ["nes", "famicom"], snes: ["snes", "sfc"], n64: ["n64"], gamecube: ["gc", "gamecube", "ngc"], wii: ["wii"], wiiu: ["wiiu"],
  switch: ["switch"], gb: ["gb"], gbc: ["gbc"], gba: ["gba"], ds: ["nds", "ds"], "3ds": ["n3ds", "3ds"],
  sms: ["mastersystem", "sms"], megadrive: ["genesis", "megadrive", "md"], sega: ["segacd", "megacd", "scd"], saturn: ["saturn"],
  dreamcast: ["dreamcast", "dc"], gamegear: ["gamegear", "gg"], xbox: ["xbox"], xbox360: ["xbox360"],
  arcade: ["mame", "arcade", "fbneo"], atari2600: ["atari2600", "atari"], neogeo: ["neogeo"], pcengine: ["pcengine", "tg16", "turbografx16", "pce"],
};
// Names matched only to the generic key, by their own name.
const SLUGS_FOR_NAME: Array<[RegExp, string[]]> = [
  [/^(sega )?32x$/, ["sega32x", "32x"]], [/^sega (cd|mega cd)$/, ["segacd", "megacd"]], [/virtual boy/, ["virtualboy", "vb"]],
  [/neo ?geo pocket/, ["ngpc", "ngp", "neogeopocket"]], [/wonderswan/, ["wonderswan", "wswan", "ws"]], [/^3do/, ["3do"]],
  [/jaguar/, ["atarijaguar", "jaguar"]], [/lynx/, ["atarilynx", "lynx"]], [/^msx/, ["msx"]], [/amiga/, ["amiga"]],
  [/commodore 64|^c64/, ["c64"]], [/^(dos|ms dos)$/, ["dos", "pc"]], [/colecovision/, ["colecovision"]],
  [/intellivision/, ["intellivision"]], [/vectrex/, ["vectrex"]], [/zx spectrum/, ["zxspectrum"]], [/amstrad/, ["amstradcpc"]],
];

const compact = (value: string) => value.toLowerCase().replace(/[^a-z0-9]+/g, "");

// The pack slug for a collection name, or undefined when the pack has none.
export function packSlugFor(name: unknown, slugs: ReadonlySet<string>, key?: string): string | undefined {
  if (!slugs.size) return undefined;
  const plain = plainCollectionName(name).toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
  const tries = [...(key ? SLUGS_FOR_KEY[key] ?? [] : []), ...SLUGS_FOR_NAME.filter(([pattern]) => pattern.test(plain)).flatMap(([, list]) => list), compact(plain)];
  return tries.find(slug => slugs.has(slug));
}

function appCount(collection: any): number {
  for (const key of ["allApps", "visibleApps", "apps", "m_rgApps"]) {
    try {
      const value = collection?.[key];
      if (Array.isArray(value)) return value.length;
      if (value && typeof value.size === "number") return value.size;
    } catch { /* Next shape. */ }
  }
  return 0;
}

// Every console collection as a tile: one per console (the collection with the most games wins when a console has
// several, e.g. a Steam one and a RomM one), by family order then name.
export function consoleTiles(collections: unknown, slugs: ReadonlySet<string> = new Set()): ConsoleTile[] {
  const list = Array.isArray(collections) ? collections.slice(0, 400) : [];
  const byConsole = new Map<string, ConsoleTile>();
  for (const collection of list) {
    let raw = "", id = "";
    try { raw = String(collection?.displayName ?? collection?.strName ?? collection?.name ?? ""); id = String(collection?.id ?? collection?.strId ?? ""); } catch { continue; }
    const found = matchCollection(raw);
    if (!found?.family || !id) continue;
    const name = plainCollectionName(raw);
    const slug = packSlugFor(raw, slugs, found.key);
    const identity = slug ?? (found.key === "generic" ? `name:${compact(name)}` : found.key);
    const tile: ConsoleTile = { id, name, family: found.family as ConsoleFamily, key: found.key, slug, count: appCount(collection) };
    const prior = byConsole.get(identity);
    if (!prior || tile.count > prior.count) byConsole.set(identity, tile);
  }
  return [...byConsole.values()].filter(tile => CONSOLE_FAMILY_ORDER.includes(tile.family))
    .sort((a, b) => CONSOLE_FAMILY_ORDER.indexOf(a.family) - CONSOLE_FAMILY_ORDER.indexOf(b.family) || a.name.localeCompare(b.name));
}

// ---- 3.3.10: the Consoles page (CORE-UI.md, "Home in 3.3.10") ----
//
// One card per console, most recently played first (a console never played comes after, by name). The Collector keeps
// one Steam collection per console (PS1 is "PlayStation 1" since its 0.33 builds) and one per family ("PlayStation",
// "Nintendo", "Sega", "Xbox", "Retro"): families never appear on Home, so a collection named exactly like a family is
// not a console (Xbox is both a family and a console: the console's collection is kept, the bigger one when there are
// two). Each card keeps its games (most recently played first) for screen two.
export const FAMILY_COLLECTION_NAMES = new Set(["playstation", "nintendo", "sega", "retro"]);
export type ConsoleCard = ConsoleTile & { lastPlayed: number; games: Array<{ appid: number; name: string; lastPlayed: number }> };

function appsIn(collection: any): any[] {
  for (const key of ["allApps", "visibleApps", "apps", "m_rgApps"]) {
    try {
      const value = collection?.[key];
      if (Array.isArray(value)) return value;
      if (value && typeof value.values === "function") return Array.from(value.values());
    } catch { /* Next shape. */ }
  }
  return [];
}

function gameOf(app: any): { appid: number; name: string; lastPlayed: number } | undefined {
  const appid = Number(app?.appid ?? app?.unAppID ?? app?.m_unAppID);
  if (!Number.isFinite(appid) || appid <= 0) return undefined;
  return { appid, name: String(app?.display_name ?? app?.strDisplayName ?? app?.name ?? "").trim(), lastPlayed: Number(app?.rt_last_time_played ?? app?.rtLastTimePlayed ?? 0) || 0 };
}

export function consoleCards(collections: unknown, slugs: ReadonlySet<string> = new Set(), limit = 24): ConsoleCard[] {
  const list = (Array.isArray(collections) ? collections.slice(0, 400) : []).filter(collection => {
    let raw = "";
    try { raw = String(collection?.displayName ?? collection?.strName ?? collection?.name ?? ""); } catch { return false; }
    return !FAMILY_COLLECTION_NAMES.has(plainCollectionName(raw).toLowerCase().replace(/[^a-z0-9]+/g, " ").trim());
  });
  const byId = new Map<string, any>();
  for (const collection of list) { try { byId.set(String(collection?.id ?? collection?.strId ?? ""), collection); } catch { /* Skipped. */ } }
  return consoleTiles(list, slugs).map(tile => {
    const seen = new Set<number>();
    const games = appsIn(byId.get(tile.id)).slice(0, 5000).map(gameOf).filter((game): game is NonNullable<typeof game> => !!game && !seen.has(game.appid) && !!seen.add(game.appid))
      .sort((a, b) => b.lastPlayed - a.lastPlayed || a.name.localeCompare(b.name));
    return { ...tile, games: games.slice(0, limit), lastPlayed: games[0]?.lastPlayed ?? 0 };
  }).sort((a, b) => (b.lastPlayed > 0 ? 1 : 0) - (a.lastPlayed > 0 ? 1 : 0) || b.lastPlayed - a.lastPlayed || a.name.localeCompare(b.name));
}

// Steam's route for a collection (the Library's own collection page). 3.3.11 (TV: A on a console opened the Library
// on All Games): the route is Steam's own, read from Steam's route builders when they can be found (setCollectionRoutes,
// coreHomeScreen.tsx); the fixed form is only the fallback.
export const COLLECTION_ROUTE_FALLBACK = (id: string) => `/library/collection/${encodeURIComponent(id)}`;
let routeBuilders: Array<(id: string) => unknown> = [];
export function setCollectionRoutes(builders: Array<(id: string) => unknown>) { routeBuilders = Array.isArray(builders) ? builders.slice(0, 8) : []; }
// The first builder that answers a path naming the collection: absolute, holding "collection" and the id, no scheme.
export function collectionRouteFrom(builders: Array<(id: string) => unknown>, id: string): string | undefined {
  const plain = String(id ?? "");
  if (!plain) return undefined;
  const answers: string[] = [];
  for (const build of builders) {
    let route: unknown;
    try { route = build(plain); } catch { continue; }
    if (typeof route !== "string" || !route.startsWith("/") || route.includes("://") || !/collection/i.test(route)) continue;
    if (!route.includes(plain) && !route.includes(encodeURIComponent(plain))) continue;
    answers.push(route);
  }
  // A Library route first (the gamepad UI's), then any other.
  return answers.find(route => route.startsWith("/library/")) ?? answers[0];
}
export const collectionRoute = (id: string) => collectionRouteFrom(routeBuilders, id) ?? COLLECTION_ROUTE_FALLBACK(id);
// Whether a function looks like one of Steam's route builders for a collection: short, one argument, its source
// building a path with "collection" in it. Only such functions are ever called.
export function looksLikeCollectionRoute(fn: unknown): fn is (id: string) => unknown {
  if (typeof fn !== "function" || fn.length !== 1) return false;
  let source = "";
  try { source = Function.prototype.toString.call(fn); } catch { return false; }
  return source.length < 240 && /["'`]\/[^"'`]*collection/i.test(source) && !/\b(fetch|XMLHttpRequest|SteamClient|Navigate|window|document)\b/.test(source);
}

// What the backend answers (get_colorgrade_pack): the slugs it has, and per slug the white logo as a data URL.
export type ColorGradePack = { ok: boolean; slugs: string[]; logos: Record<string, string>; families?: Record<string, string>; imported?: boolean; errors?: string[] };
