// Eternal Core: Core Home's bottom tabs (docs/eternal/CORE-UI.md, "Phase 2: Home"). Pure: tests drive it with fake
// Steam tab lists and fake collection stores.
//
// Steam's Home tabs come in as `{ id, title, content, ... }`. Core Home answers, in this order:
//   Jarvis brief (10, takes Steam's "WhatsNew" id so a remembered active tab still opens) - Consoles (20) -
//   any registered tab (Club, 30, from src/club/) - On this day (40, Steam's "Recommended" slot, filled by
//   DailyHistory.tsx) - any other Steam tab Core does not know.
// Steam's "What's new" (its content moves into the brief) and "Friends" are removed.
//
// Registration point: registerHomeTab({ id, label, order, component, isAvailable }) adds a tab without touching Home's
// internals; an unavailable tab (isAvailable() false or throwing) is left out.

export type HomeTabRegistration<C = unknown> = {
  id: string;
  // A label key the caller resolves, or a function of the locale.
  label: string | ((locale: string) => string);
  order: number;
  component: C;
  isAvailable?: () => boolean;
};

const registry = new Map<string, HomeTabRegistration>();
const listeners = new Set<() => void>();
let revision = 0;
const notify = () => { revision++; for (const listener of [...listeners]) { try { listener(); } catch { /* Others still hear it. */ } } };

export function registerHomeTab<C>(tab: HomeTabRegistration<C>): () => void {
  if (!tab || typeof tab.id !== "string" || !tab.id) return () => {};
  registry.set(tab.id, tab as HomeTabRegistration);
  notify();
  return () => { if (registry.get(tab.id) === tab) { registry.delete(tab.id); notify(); } };
}

export function subscribeHomeTabs(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export const homeTabsRevision = () => revision;

// Registered tabs that are available now, by order.
export function availableHomeTabs(): HomeTabRegistration[] {
  return [...registry.values()].filter(tab => {
    try { return tab.isAvailable ? tab.isAvailable() === true : true; } catch { return false; }
  }).sort((a, b) => a.order - b.order);
}

export const HOME_TAB_ORDER = { brief: 10, consoles: 20, club: 30, onThisDay: 40 } as const;
export const REMOVED_STEAM_TABS = new Set(["WhatsNew", "Friends"]);
export const ON_THIS_DAY_ID = "Recommended";
export const BRIEF_ID = "WhatsNew";

type Tab = { id: any; [key: string]: any };

// Steam's tabs plus Core's (each with an order) -> the final list. `core` must not contain Steam ids other than the
// brief's.
export function composeHomeTabs(native: Tab[], core: Array<Tab & { order: number }>): Tab[] {
  const list = Array.isArray(native) ? native : [];
  const onThisDay = list.find(tab => tab?.id === ON_THIS_DAY_ID);
  const known = new Set([...REMOVED_STEAM_TABS, ON_THIS_DAY_ID]);
  const others = list.filter(tab => tab && !known.has(tab.id));
  const ordered = [...core, ...(onThisDay ? [{ ...onThisDay, order: HOME_TAB_ORDER.onThisDay }] : [])]
    .sort((a, b) => a.order - b.order)
    .map(({ order, ...tab }) => { void order; return tab as Tab; });
  return [...ordered, ...others];
}

// The active tab Steam remembers may be one Core removed (Friends): fall back to the brief.
export function activeHomeTab(active: unknown, tabs: Tab[]): unknown {
  return tabs.some(tab => tab.id === active) ? active : tabs[0]?.id ?? active;
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
