// Eternal Core: the Library's tab icons, family tones and collection tiles (docs/eternal/CORE-UI.md, "Library icons
// and tones"). Replaces what the CSS Loader themes Enhanced Library and Enhanced Collection did by position: here every
// tab and tile is matched by its **name**, so adding, hiding or reordering one never shifts the others.
//
// - Tabs (TabMaster's or Steam's, by title): All Games, Installed, VR, PlayStation, Nintendo, Sega, Xbox, Retro,
//   Completed. The icon sits before the label; family tabs get a short marker in their tone.
// - Collection tiles (Steam's Collections view, by collection name, RomM's "RomM: <platform> (<host>)" form too).
// - Icons: the user's own set from %APPDATA%\GamingMode\icons\ (map.json, served by Core's plugin backend:
//   get_core_icons), else Core's original drawings (coreArtwork.ts). A tile without any icon keeps Steam's collage and
//   only gains its tone.
//
// Every write is an attribute or a custom property on Steam's element; undecorate removes them all.
// Pure apart from coreArtwork.ts: tests drive it with fake DOMs.

import { CORE_ORIGINAL_ICONS, svgDataUrl } from "./coreArtwork";

export type CoreFamily = "playstation" | "nintendo" | "sega" | "xbox" | "retro";
export const CORE_FAMILY_TONES: Record<CoreFamily, string> = { playstation: "#6E8CB8", nintendo: "#B07A84", sega: "#5E9E9A", xbox: "#7FA36B", retro: "#8F7CB0" };
export const CORE_STEEL = "#C9D1D9";

export type CoreMatch = { key: string; family?: CoreFamily; tone: string };

export const normalName = (value: unknown) => String(value ?? "").normalize("NFKD").replace(/[̀-ͯ]/g, "").toLowerCase()
  .replace(/&/g, " and ").replace(/[^a-z0-9]+/g, " ").trim();

// Tab titles, normalised -> icon key. Tones follow the key's family.
const TABS: Record<string, string> = {
  "all games": "all-games", "all": "all-games", "library": "all-games", "games": "all-games",
  "installed": "installed", "vr": "vr", "virtual reality": "vr",
  "playstation": "playstation", "sony": "playstation", "nintendo": "nintendo", "sega": "sega", "xbox": "xbox", "microsoft": "xbox",
  "retro": "retro", "classic": "retro", "classics": "retro",
  "completed": "completed", "beaten": "completed", "finished": "completed",
};
const FAMILY_KEYS: Record<string, CoreFamily> = { playstation: "playstation", nintendo: "nintendo", sega: "sega", xbox: "xbox", retro: "retro" };

const match = (key: string, family?: CoreFamily): CoreMatch => ({ key, family, tone: family ? CORE_FAMILY_TONES[family] : CORE_STEEL });

export function matchTab(title: unknown): CoreMatch | undefined {
  const name = normalName(title);
  if (!name) return undefined;
  const key = TABS[name];
  return key ? match(key, FAMILY_KEYS[key]) : undefined;
}

// Collection names (RomM's platform names and their usual variants) -> icon key and family. Checked in order: the
// first rule whose pattern matches the normalised name wins.
const COLLECTIONS: Array<[RegExp, string, CoreFamily | undefined]> = [
  [/^completed$/, "completed", undefined],
  [/^(vr|virtual reality)$/, "vr", undefined],
  [/^(playstation|playstation 1|sony playstation|ps1|psx|ps one)$/, "ps1", "playstation"],
  [/^(playstation 2|ps2)$/, "ps2", "playstation"],
  [/^(playstation 3|ps3)$/, "ps3", "playstation"],
  [/^(playstation 4|ps4)$/, "ps4", "playstation"],
  [/^(playstation 5|ps5)$/, "ps5", "playstation"],
  [/^(playstation portable|psp)$/, "psp", "playstation"],
  [/^(playstation vita|ps vita|vita)$/, "vita", "playstation"],
  [/^(nintendo entertainment system|nes|famicom)$/, "nes", "nintendo"],
  [/^(super nintendo entertainment system|super nintendo|snes|super famicom)$/, "snes", "nintendo"],
  [/^(nintendo 64|n64)$/, "n64", "nintendo"],
  [/^(nintendo gamecube|gamecube|ngc)$/, "gamecube", "nintendo"],
  [/^wii u$/, "wiiu", "nintendo"],
  [/^wii$/, "wii", "nintendo"],
  [/^(nintendo switch|switch)$/, "switch", "nintendo"],
  [/^game boy advance$|^gba$/, "gba", "nintendo"],
  [/^game boy colou?r$|^gbc$/, "gbc", "nintendo"],
  [/^game boy$|^gb$/, "gb", "nintendo"],
  [/^(nintendo ds|ds|nds)$/, "ds", "nintendo"],
  [/^(nintendo 3ds|3ds)$/, "3ds", "nintendo"],
  [/^(virtual boy|nintendo virtual boy)$/, "generic", "nintendo"],
  [/^sega master system( mark iii)?$|^master system$|^sms$/, "sms", "sega"],
  [/^sega mega drive( genesis)?$|^sega genesis$|^mega drive$|^genesis$/, "megadrive", "sega"],
  [/^(sega )?32x$/, "megadrive", "sega"],
  [/^sega (cd|mega cd)$/, "sega", "sega"],
  [/^(sega )?saturn$/, "saturn", "sega"],
  [/^(sega )?dreamcast$/, "dreamcast", "sega"],
  [/^(sega )?game gear$/, "gamegear", "sega"],
  [/^xbox 360$/, "xbox360", "xbox"],
  [/^(microsoft )?xbox( one| series x s| series)?$/, "xbox", "xbox"],
  [/^arcade|^mame$|^fbneo$/, "arcade", "retro"],
  [/^atari/, "atari2600", "retro"],
  [/^neo ?geo/, "neogeo", "retro"],
  [/^(turbografx|pc engine)/, "pcengine", "retro"],
  [/^(msx|commodore|amiga|c64|zx spectrum|amstrad|colecovision|intellivision|vectrex|3do|jaguar|wonderswan|neo geo pocket|dos|ms dos)/, "generic", "retro"],
];

// "RomM: Nintendo 64 (romm.local)" -> "Nintendo 64"; plain names unchanged.
export function plainCollectionName(name: unknown): string {
  return String(name ?? "").replace(/^\s*romm\s*:\s*/i, "").replace(/\s*\([^()]*\)\s*$/, "").trim();
}

export function matchCollection(name: unknown): CoreMatch | undefined {
  const plain = normalName(plainCollectionName(name));
  if (!plain) return undefined;
  for (const [pattern, key, family] of COLLECTIONS) if (pattern.test(plain)) return match(key, family);
  return undefined;
}

// ---- Icons ----

// What the plugin backend answers (get_core_icons): image URLs (data:) by tab title, by collection name and by Core
// icon key, read from %APPDATA%\GamingMode\icons\map.json.
export type CoreIconSet = { tabs?: Record<string, string>; collections?: Record<string, string>; keys?: Record<string, string> };

// Core's original drawings for a key; family tabs without an original drawing take the generic pad. Collection keys
// without one answer undefined (the tile keeps Steam's collage).
const ORIGINAL_FOR_KEY: Record<string, string> = {
  "all-games": "all-games", installed: "installed", vr: "vr", completed: "completed", ps3: "ps3", xbox: "xbox", xbox360: "xbox360",
};
const ORIGINAL_FOR_TAB: Record<string, string> = { ...ORIGINAL_FOR_KEY, playstation: "generic", nintendo: "generic", sega: "generic", retro: "generic" };

const urlCache = new Map<string, string>();
function original(name: string | undefined): string | undefined {
  if (!name || !CORE_ORIGINAL_ICONS[name]) return undefined;
  let url = urlCache.get(name);
  if (!url) { url = svgDataUrl(CORE_ORIGINAL_ICONS[name]); urlCache.set(name, url); }
  return url;
}
const safeUrl = (value: unknown) => typeof value === "string" && /^data:image\/(png|svg\+xml|webp|jpeg)[;,]/i.test(value) ? value : undefined;
function lookup(map: Record<string, string> | undefined, name: string): string | undefined {
  if (!map) return undefined;
  const wanted = normalName(name);
  for (const [key, value] of Object.entries(map)) if (normalName(key) === wanted) return safeUrl(value);
  return undefined;
}

// Where an icon came from (3.3.4, recorded in the Core UI snapshot): the user's map by name, by Core's key, Core's
// original drawing, or none.
export type IconSource = "user-name" | "user-key" | "original" | "none";
export type IconPick = { url?: string; source: IconSource };

// Order: the user's map by the exact title, then by Core's key, then Core's original drawing.
export function tabIconPick(title: string, found: CoreMatch | undefined, set: CoreIconSet | undefined): IconPick {
  const byName = lookup(set?.tabs, title);
  if (byName) return { url: byName, source: "user-name" };
  const byKey = found ? lookup(set?.keys, found.key) : undefined;
  if (byKey) return { url: byKey, source: "user-key" };
  const drawn = found ? original(ORIGINAL_FOR_TAB[found.key]) : undefined;
  return drawn ? { url: drawn, source: "original" } : { source: "none" };
}
export function collectionIconPick(name: string, found: CoreMatch | undefined, set: CoreIconSet | undefined): IconPick {
  const byName = lookup(set?.collections, name) ?? lookup(set?.collections, plainCollectionName(name));
  if (byName) return { url: byName, source: "user-name" };
  const byKey = found ? lookup(set?.keys, found.key) : undefined;
  if (byKey) return { url: byKey, source: "user-key" };
  const drawn = found ? original(ORIGINAL_FOR_KEY[found.key]) : undefined;
  return drawn ? { url: drawn, source: "original" } : { source: "none" };
}
export function tabIcon(title: string, found: CoreMatch | undefined, set: CoreIconSet | undefined): string | undefined {
  return tabIconPick(title, found, set).url;
}
export function collectionIcon(name: string, found: CoreMatch | undefined, set: CoreIconSet | undefined): string | undefined {
  return collectionIconPick(name, found, set).url;
}

// ---- The live page ----

export const TAB_ATTRIBUTE = "data-core-tab";
export const TILE_ATTRIBUTE = "data-core-collection";
export const TONE_ATTRIBUTE = "data-core-tone";
export const ICON_ATTRIBUTE = "data-core-icon";
// 3.3.4: a decorated collection tile's name, drawn under its icon (the stylesheet's attr(data-core-name)).
export const NAME_ATTRIBUTE = "data-core-name";
const ATTRIBUTES = [TAB_ATTRIBUTE, TILE_ATTRIBUTE, TONE_ATTRIBUTE, ICON_ATTRIBUTE, NAME_ATTRIBUTE];

export type DecorateDom = { tab?: string; tabTitle?: string; library?: string };
export type DecorateOptions = { icons: () => CoreIconSet | undefined; tileSelector?: () => string | undefined };

function text(element: any): string {
  try { return String(element?.textContent ?? "").replace(/\s+/g, " ").trim(); } catch { return ""; }
}
function setVar(element: any, name: string, value: string | undefined) {
  try {
    if (value) { if (element.style.getPropertyValue?.(name) !== `url("${value}")`) element.style.setProperty(name, `url("${value}")`); }
    else element.style.removeProperty?.(name);
  } catch { /* Left as it is. */ }
}
function tag(element: any, attribute: string, value: string | undefined) {
  try {
    if (value === undefined) { if (element.getAttribute(attribute) !== null) element.removeAttribute(attribute); }
    else if (element.getAttribute(attribute) !== value) element.setAttribute(attribute, value);
  } catch { /* Left as it is. */ }
}
function apply(element: any, kind: "tab" | "tile", name: string, found: CoreMatch | undefined, icon: string | undefined) {
  tag(element, kind === "tab" ? TAB_ATTRIBUTE : TILE_ATTRIBUTE, found ? found.key : icon ? "custom" : undefined);
  tag(element, TONE_ATTRIBUTE, found?.family);
  tag(element, ICON_ATTRIBUTE, icon ? "1" : undefined);
  setVar(element, "--core-icon", icon);
  void name;
}

// Steam's Collections view: a collection tile is a focusable element whose label is the collection's name. Steam has
// no stable class for it across clients, so tiles are the links into /library/collection/<id>, plus anything the
// caller's selector names.
export const COLLECTION_TILE_SELECTOR = 'a[href*="/library/collection/"],[data-collection-id]';

// What the decorator did last, for the snapshot: per tab its title (at most 30 characters), key and icon source; tiles
// counted by source.
export type DecoratorReport = { iconSet: { tabs: number; collections: number; keys: number } | null; tabs: Array<{ title: string; key?: string; source: IconSource }>;
  tiles: Record<IconSource, number> & { total: number } };

export function createLibraryDecorator(options: DecorateOptions) {
  const touched = new Map<any, Set<any>>();
  let report: DecoratorReport = { iconSet: null, tabs: [], tiles: { "user-name": 0, "user-key": 0, original: 0, none: 0, total: 0 } };
  function remember(doc: any, element: any) { let set = touched.get(doc); if (!set) { set = new Set(); touched.set(doc, set); } set.add(element); }

  function decorate(doc: any, dom: DecorateDom) {
    let set: CoreIconSet | undefined;
    try { set = options.icons(); } catch { set = undefined; }
    const tabReport: DecoratorReport["tabs"] = [];
    const tileReport: DecoratorReport["tiles"] = { "user-name": 0, "user-key": 0, original: 0, none: 0, total: 0 };
    if (dom.tab) {
      let tabs: any[] = [];
      try { tabs = Array.from(doc.querySelectorAll(dom.tab)).slice(0, 60); } catch { tabs = []; }
      for (const tab of tabs) {
        let label: any = tab;
        try { if (dom.tabTitle) label = tab.querySelector(dom.tabTitle) ?? tab; } catch { label = tab; }
        const title = text(label);
        const found = matchTab(title);
        const pick = tabIconPick(title, found, set);
        const icon = pick.url;
        if (tabReport.length < 40 && title) tabReport.push({ title: title.slice(0, 30), key: found?.key, source: pick.source });
        if (!found && !icon && !touched.get(doc)?.has(tab)) continue;
        apply(tab, "tab", title, found, icon);
        remember(doc, tab);
      }
    }
    let tiles: any[] = [];
    const extra = (() => { try { return options.tileSelector?.(); } catch { return undefined; } })();
    try { tiles = Array.from(doc.querySelectorAll(extra ? `${COLLECTION_TILE_SELECTOR},${extra}` : COLLECTION_TILE_SELECTOR)).slice(0, 200); } catch { tiles = []; }
    for (const tile of tiles) {
      const name = String(tile.getAttribute?.("aria-label") || tile.getAttribute?.("title") || text(tile));
      const found = matchCollection(name);
      const pick = collectionIconPick(name, found, set);
      const icon = pick.url;
      tileReport.total++; tileReport[pick.source]++;
      if (!found && !icon && !touched.get(doc)?.has(tile)) continue;
      apply(tile, "tile", name, found, icon);
      tag(tile, NAME_ATTRIBUTE, icon ? plainCollectionName(name).slice(0, 40) || undefined : undefined);
      remember(doc, tile);
    }
    if (tabReport.length || tileReport.total) report = { iconSet: set ? { tabs: Object.keys(set.tabs ?? {}).length, collections: Object.keys(set.collections ?? {}).length, keys: Object.keys(set.keys ?? {}).length } : null,
      tabs: tabReport, tiles: tileReport };
  }

  function undecorate(doc: any) {
    for (const element of touched.get(doc) ?? []) {
      for (const attribute of ATTRIBUTES) tag(element, attribute, undefined);
      setVar(element, "--core-icon", undefined);
    }
    touched.delete(doc);
  }

  return { decorate, undecorate, report: () => report };
}

// ---- Loading the user's icon set ----

export type IconLoader = () => Promise<CoreIconSet & { ok?: boolean; errors?: string[]; folder?: string }>;

// Loads the set once, again on request (Core UI's setting toggled) and at most every ten minutes; failures keep the
// last good set (or none: Core's originals).
// 3.3.4: the last answer's facts (ok, folder, errors, counts) are kept for the snapshot, and logged once per change, so
// "my icons are not used" can be told apart from "the backend did not find them".
export type IconStoreStatus = { ok: boolean; folder?: string; errors: string[]; tabs: number; collections: number; keys: number; failed?: string; at?: number };

export function createIconStore(source: IconLoader, now: () => number = Date.now, log: (message: string) => void = () => {}) {
  let current: CoreIconSet | undefined, loading = false, loadedAt = -Infinity, status: IconStoreStatus | undefined, logged = "";
  const refresh = async (force = false) => {
    if (loading || (!force && now() - loadedAt < 10 * 60 * 1000)) return;
    loading = true;
    try {
      const answer = await source();
      if (answer && typeof answer === "object") {
        current = { tabs: answer.tabs ?? {}, collections: answer.collections ?? {}, keys: answer.keys ?? {} };
        status = { ok: answer.ok === true, folder: typeof answer.folder === "string" ? answer.folder : undefined, errors: Array.isArray(answer.errors) ? answer.errors.slice(0, 20).map(String) : [],
          tabs: Object.keys(current.tabs ?? {}).length, collections: Object.keys(current.collections ?? {}).length, keys: Object.keys(current.keys ?? {}).length, at: now() };
      }
    } catch (error) {
      status = { ok: false, errors: [], tabs: 0, collections: 0, keys: 0, failed: String((error as any)?.message ?? error).slice(0, 120), at: now() };
    }
    finally { loading = false; loadedAt = now(); }
    const line = status ? (status.ok ? `[Core UI] icons loaded: ${status.tabs} tabs, ${status.collections} collections, ${status.keys} keys from ${status.folder ?? "?"}`
      : `[Core UI] icons not loaded (${status.failed ?? (status.errors.join(", ") || "unknown")}); Core's own drawings are used`) : "";
    if (line && line !== logged) { logged = line; try { log(line); } catch { /* Logging is best effort. */ } }
  };
  return { get: () => { void refresh(); return current; }, refresh, status: () => status };
}
