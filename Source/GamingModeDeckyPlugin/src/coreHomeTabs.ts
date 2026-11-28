// Eternal Core: Core Home's bottom tabs (docs/eternal/CORE-UI.md, "Phase 2: Home"). Pure: tests drive it with fake
// Steam tab lists and fake collection stores.
//
// Steam's Home tabs come in as `{ id, title, content, ... }`. Core Home answers, in this order (3.3.10):
//   Favorites (10, the default) - Achievements (20) - any registered tab (Club, 30, from src/club/) - any other Steam tab.
// Steam's "What's new", "Recommended" and "Friends" are removed. The Jarvis brief and On this day tabs of 3.3.3-3.3.7
// are gone (the user's call); a remembered active tab that pointed at them opens Consoles.
//
// Registration point: registerHomeTab({ id, label, order, component, isAvailable }) adds a tab without touching Home's
// internals; an unavailable tab (isAvailable() false or throwing) is left out.

export type HomeTabRegistration<C = unknown> = {
  id: string;
  // A label key the caller resolves, or a function of the locale.
  label: string | ((locale: string) => string);
  order: number;
  // What the tab shows: a component given { locale, getLocale }, or a render function given the same.
  component?: C;
  render?: (props: { locale: string; getLocale: () => string }) => unknown;
  isAvailable?: () => boolean;
  // Tells Home when isAvailable() may have changed (the Club's data arriving).
  subscribe?: (listener: () => void) => () => void;
};

const registry = new Map<string, HomeTabRegistration>();
const unsubscribers = new Map<string, () => void>();
const listeners = new Set<() => void>();
let revision = 0;
const notify = () => { revision++; for (const listener of [...listeners]) { try { listener(); } catch { /* Others still hear it. */ } } };

export function registerHomeTab<C>(tab: HomeTabRegistration<C>): () => void {
  if (!tab || typeof tab.id !== "string" || !tab.id) return () => {};
  registry.get(tab.id) && unsubscribers.get(tab.id)?.();
  registry.set(tab.id, tab as HomeTabRegistration);
  let stop: (() => void) | undefined;
  try { stop = tab.subscribe?.(notify); } catch { stop = undefined; }
  unsubscribers.set(tab.id, () => { try { stop?.(); } catch { /* Gone already. */ } unsubscribers.delete(tab.id); });
  notify();
  return () => { if (registry.get(tab.id) === tab) { unsubscribers.get(tab.id)?.(); registry.delete(tab.id); notify(); } };
}

export function subscribeHomeTabs(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export const homeTabsRevision = () => revision;

// Registered tabs that are available now, by order.
export function availableHomeTabs(): HomeTabRegistration[] {
  return [...registry.values()].filter(tab => {
    try { return tab.isAvailable ? tab.isAvailable() === true : true; } catch { return false; }
  }).sort((a, b) => a.order - b.order);
}

// 3.3.8 (the user's call): Home is Consoles (the default) and Club. Steam's What's new (Core's brief took its id),
// Recommended (On this day took its slot) and Friends are all removed.
// 3.3.9: Favorites first (the default), then Consoles and Club.
// 3.3.10 (the approved design): Consoles is a page of its own (L1/R1, coreHomePages.ts); the tabs below the fold are
// Favorites, Achievements and Club. A remembered Consoles tab opens Favorites.
export const HOME_TAB_ORDER = { favorites: 10, achievements: 20, club: 30 } as const;
export const FAVORITES_ID = "CoreFavorites";
export const ACHIEVEMENTS_ID = "CoreAchievements";
export const CONSOLES_ID = "CoreConsoles";
export const REMOVED_STEAM_TABS = new Set(["WhatsNew", "Friends", "Recommended"]);
export const ON_THIS_DAY_ID = "Recommended";
export const BRIEF_ID = "WhatsNew";
// Ids a remembered active tab may still hold from 3.3.3-3.3.7; each now opens Consoles.
export const RETIRED_HOME_TABS = new Set([BRIEF_ID, ON_THIS_DAY_ID, "CoreBrief", "Friends", CONSOLES_ID]);

type Tab = { id: any; [key: string]: any };

// Steam's tabs plus Core's (each with an order) -> the final list. `core` must not contain Steam ids other than the
// brief's.
export function composeHomeTabs(native: Tab[], core: Array<Tab & { order: number }>): Tab[] {
  const list = Array.isArray(native) ? native : [];
  const others = list.filter(tab => tab && !REMOVED_STEAM_TABS.has(tab.id) && !RETIRED_HOME_TABS.has(tab.id));
  const ordered = [...core].filter(tab => tab && !RETIRED_HOME_TABS.has(tab.id))
    .sort((a, b) => a.order - b.order)
    .map(({ order, ...tab }) => { void order; return tab as Tab; });
  return [...ordered, ...others];
}

// The active tab Steam remembers may be one Core removed (the brief, On this day, Friends): Favorites opens instead,
// then Consoles. `migrate` (3.3.9, once per install: coreHome.tsx keeps the flag) moves a remembered Consoles to the new
// default, Favorites, as well.
export function activeHomeTab(active: unknown, tabs: Tab[], migrate = false): unknown {
  const fallback = tabs.find(tab => tab.id === FAVORITES_ID)?.id ?? tabs[0]?.id ?? active;
  if (migrate && (active === CONSOLES_ID || active === undefined)) return fallback;
  if (tabs.some(tab => tab.id === active) && !RETIRED_HOME_TABS.has(active as any)) return active;
  return fallback;
}

// 3.3.9: the Favorites tab: the games in Steam's own Favorites collection (Steam games and ROM shortcuts alike), read
// from the collection store each time, most recently played first, then by name. Never a copy.
// 3.3.10: `more` adds other collections (The Collector's Favourites), each game once.
export function favoriteApps(collection: any, limit = 200, more: any[] = []): HomeApp[] {
  const out = new Map<number, HomeApp>();
  for (const raw of [collection, ...(Array.isArray(more) ? more : [])].flatMap(item => appsOf(item)).slice(0, 5000)) {
    const app = homeApp(raw);
    if (app && !out.has(app.appid)) out.set(app.appid, app);
  }
  return [...out.values()].sort((a, b) => b.lastPlayed - a.lastPlayed || a.name.localeCompare(b.name)).slice(0, limit);
}

// Steam's Favorites collection from its collection store (id "favorite"), by the store's own getter, then its list.
export function favoritesCollection(store: any): any {
  try { const found = store?.GetCollection?.("favorite"); if (found) return found; } catch { /* The list below. */ }
  try {
    const list = store?.userCollections;
    const items = Array.isArray(list) ? list : list && typeof list.values === "function" ? Array.from(list.values()) : [];
    return items.find((item: any) => item?.id === "favorite") ?? store?.favoritesCollection;
  } catch { return undefined; }
}

// 3.3.10: The Collector's Favourites collection(s): RomM's favourites as The Collector names them on Steam
// ("[Favourites]" under Classic names on Windows, "Favourites" under Clean, "RomM: [Favourites] (<host>)" elsewhere;
// a "(RomM)" suffix when the name was taken). Steam's own Favorites (id "favorite") is not one of them.
const COLLECTOR_FAVOURITES = /^\[?favou?rites\]?$/i;
export function collectorFavorites(collections: any[]): any[] {
  return (Array.isArray(collections) ? collections.slice(0, 400) : []).filter(collection => {
    try {
      if (collection?.id === "favorite") return false;
      const raw = String(collection?.displayName ?? collection?.strName ?? collection?.name ?? "");
      const plain = raw.replace(/^\s*romm\s*:\s*/i, "").replace(/\s*\([^()]*\)\s*$/, "").trim();
      return COLLECTOR_FAVOURITES.test(plain) && (plain.startsWith("[") || /^\s*romm\s*:/i.test(raw) || /\(romm\)\s*$/i.test(raw) || plain.toLowerCase() === "favourites");
    } catch { return false; }
  });
}

// ---- Data for the brief and the console shelves ----

export type HomeApp = { appid: number; name: string; lastPlayed: number };

function appsOf(collection: any): any[] {
  for (const key of ["allApps", "visibleApps", "apps", "m_rgApps"]) {
    try {
      const value = collection?.[key];
      if (Array.isArray(value)) return value;
      if (value && typeof value.values === "function") return Array.from(value.values());
    } catch { /* Next shape. */ }
  }
  return [];
}
export function homeApp(app: any): HomeApp | undefined {
  const appid = Number(app?.appid ?? app?.unAppID ?? app?.m_unAppID);
  if (!Number.isFinite(appid) || appid <= 0) return undefined;
  const name = String(app?.display_name ?? app?.strDisplayName ?? app?.name ?? "").trim();
  const lastPlayed = Number(app?.rt_last_time_played ?? app?.rtLastTimePlayed ?? 0) || 0;
  return { appid, name, lastPlayed };
}

export type ShelfFamily = "playstation" | "nintendo" | "sega" | "xbox" | "retro" | "vr";
export const SHELF_ORDER: ShelfFamily[] = ["playstation", "nintendo", "sega", "xbox", "retro", "vr"];

// One shelf per family from the user's collections, matched by name through `familyOf` (coreLibraryIcons'
// matchCollection, or a VR collection). Games de-duplicated, most recently played first, at most `limit`.
export function consoleShelves(collections: any[], familyOf: (name: string) => ShelfFamily | undefined, limit = 24) {
  const shelves = new Map<ShelfFamily, Map<number, HomeApp>>();
  for (const collection of Array.isArray(collections) ? collections.slice(0, 400) : []) {
    let name = "";
    try { name = String(collection?.displayName ?? collection?.strName ?? collection?.name ?? ""); } catch { name = ""; }
    const family = familyOf(name);
    if (!family) continue;
    let shelf = shelves.get(family);
    if (!shelf) { shelf = new Map(); shelves.set(family, shelf); }
    for (const raw of appsOf(collection).slice(0, 5000)) {
      const app = homeApp(raw);
      if (app && !shelf.has(app.appid)) shelf.set(app.appid, app);
    }
  }
  return SHELF_ORDER.filter(family => shelves.has(family)).map(family => ({
    family,
    apps: [...shelves.get(family)!.values()].sort((a, b) => b.lastPlayed - a.lastPlayed || a.name.localeCompare(b.name)).slice(0, limit),
  }));
}

// The most recently played game among `apps` (any iterable of Steam app overviews), if any was ever played.
export function lastPlayedApp(apps: any): HomeApp | undefined {
  let best: HomeApp | undefined;
  let list: any[] = [];
  try { list = Array.isArray(apps) ? apps : apps && typeof apps.values === "function" ? Array.from(apps.values()) : []; } catch { list = []; }
  for (const raw of list.slice(0, 20000)) {
    const app = homeApp(raw);
    if (app && app.lastPlayed > 0 && app.name && (!best || app.lastPlayed > best.lastPlayed)) best = app;
  }
  return best;
}

// Forge's mod updates, if Forge publishes them: window.__eternalForgeUpdates = { version: 1, count } or
// { version: 1, games: [{ appId, name, count }] }. Forge does not publish this yet (CORE-UI.md); no record, no row.
export function forgeUpdateCount(host: any): number {
  try {
    const record = host?.__eternalForgeUpdates;
    if (!record || record.version !== 1) return 0;
    if (Number.isFinite(record.count)) return Math.max(0, Math.floor(record.count));
    if (Array.isArray(record.games)) return record.games.reduce((sum: number, game: any) => sum + (Number.isFinite(game?.count) ? Math.max(0, Math.floor(game.count)) : 0), 0);
  } catch { /* No row. */ }
  return 0;
}
