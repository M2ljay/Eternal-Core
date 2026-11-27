// Eternal Core batch 1 (B9): Auto-Completed. When a Steam game reaches 100% of its achievements, Core adds it to
// the user's Steam collection named "Completed" (matched by name, ignoring case). The collection is created only
// when "Create the Completed collection if it's missing" is on (Core > Gaming Mode > Before bed). Core never
// removes a game from the collection and skips games with no achievements. It runs when Steam reports an
// achievement unlock, and once a day over recently played games.
//
// Steam's stores are reached through the caller (index.tsx), which looks them up with the Decky 3/4 compat
// helpers (steamModules.ts); everything here fails soft: a store or method that is not there means "not now",
// never an error over Big Picture. Self-contained on purpose: tests/autoCompleted.test.mjs loads this file alone.
//
// Ideas from Completionist and Deck Progress Tracker (both BSD-3-Clause): Steam's achievement progress cache as
// the source of "100%", and collectionStore for the collection. No code was copied from either.

export type AchievementProgress = { unlocked: number; total: number };
export type CompletedSettings = { enabled: boolean; create: boolean; collection: string };
export type CompletedOutcome = "off" | "added" | "created" | "present" | "incomplete" | "no-achievements" | "missing-collection" | "unavailable" | "failed";

export const DEFAULT_COLLECTION = "Completed";
const GAME_TYPE = 1;
const DAY = 24 * 3600 * 1000;

function count(value: unknown): number {
  if (Array.isArray(value)) return value.length;
  if (value && typeof value === "object") return Object.keys(value).length;
  return 0;
}

function integer(value: unknown): number | undefined {
  const number = Number(value);
  return Number.isInteger(number) && number >= 0 && number <= 100000 ? number : undefined;
}

// Achievement progress from what Steam hands out: the progress cache's entry ({ unlocked, total, all_unlocked }),
// GetMyAchievementsForApp's answer ({ result, data: { achieved, unachieved, hidden } }, also as a JSON string),
// or a plain list of achievements with bAchieved. Undefined when none of these.
export function progressFrom(entry: unknown): AchievementProgress | undefined {
  try {
    let value: any = entry;
    if (typeof value === "string") value = JSON.parse(value);
    if (!value || typeof value !== "object") return undefined;
    if (value.data && typeof value.data === "object" && !Array.isArray(value.data)) value = value.data;
    if (Array.isArray(value)) {
      const list = value.filter(item => item && typeof item === "object");
      return { unlocked: list.filter(item => item.bAchieved === true || item.achieved === true).length, total: list.length };
    }
    const unlocked = integer(value.unlocked ?? value.nAchieved), total = integer(value.total ?? value.nTotal);
    if (unlocked !== undefined && total !== undefined) return { unlocked: Math.min(unlocked, total), total };
    if ("achieved" in value || "unachieved" in value) {
      // Hidden achievements still locked count against 100%: never call a game complete too early.
      const achieved = count(value.achieved), locked = count(value.unachieved) + count(value.hidden);
      return { unlocked: achieved, total: achieved + locked };
    }
  } catch { /* Unreadable progress is no progress. */ }
  return undefined;
}

export function isComplete(progress: AchievementProgress | undefined): boolean {
  return !!progress && progress.total > 0 && progress.unlocked >= progress.total;
}

export function settingsFrom(answer: unknown): CompletedSettings {
  const value = (answer ?? {}) as any;
  const name = typeof value.collection === "string" ? value.collection.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 64) : "";
  return { enabled: value.enabled === true, create: value.create === true, collection: name || DEFAULT_COLLECTION };
}

function name(collection: any): string {
  try {
    const value = collection?.displayName ?? collection?.strName ?? collection?.m_strName;
    return typeof value === "string" ? value.trim() : "";
  } catch { return ""; }
}

function collectionsOf(store: any): any[] {
  try {
    const list = store?.userCollections;
    if (Array.isArray(list)) return list;
    if (list && typeof list.values === "function") return Array.from(list.values());
  } catch { /* fall through */ }
  return [];
}

// The user's collection with this name, ignoring case and surrounding spaces. Steam's own lookup by tag is the
// fallback (it is exact, so it only adds a collection the list did not show).
export function findCollection(store: any, wanted: string): any {
  const target = wanted.trim().toLocaleLowerCase();
  if (!target) return undefined;
  const hit = collectionsOf(store).find(collection => name(collection).toLocaleLowerCase() === target);
  if (hit) return hit;
  try {
    if (typeof store?.GetCollectionIDByUserTag === "function" && typeof store?.GetCollection === "function") {
      const id = store.GetCollectionIDByUserTag(wanted);
      const found = typeof id === "string" && id ? store.GetCollection(id) : undefined;
      if (found && name(found).toLocaleLowerCase() === target) return found;
    }
  } catch { /* Not found. */ }
  return undefined;
}

function contains(collection: any, appId: number): boolean {
  try {
    if (typeof collection?.apps?.has === "function" && collection.apps.has(appId)) return true;
    return Array.isArray(collection?.allApps) && collection.allApps.some((app: any) => Number(app?.appid) === appId);
  } catch { return false; }
}

// Steam games played in the last `days` days, newest first: what the daily sweep looks at.
export function recentGames(overviews: readonly any[] | undefined, nowMs: number, days = 14, limit = 60): number[] {
  const since = nowMs / 1000 - days * 86400;
  const list: { appId: number; played: number }[] = [];
  try {
    for (const app of overviews ?? []) {
      const appId = Number(app?.appid), played = Number(app?.rt_last_time_played);
      if (!Number.isInteger(appId) || appId <= 0 || appId > 0xffffffff || app?.app_type !== GAME_TYPE) continue;
      if (Number.isFinite(played) && played >= since) list.push({ appId, played });
    }
  } catch { return []; }
  return list.sort((a, b) => b.played - a.played).slice(0, limit).map(item => item.appId);
}

export type CompletedDeps = {
  settings: () => Promise<CompletedSettings>;
  progress: (appId: number) => Promise<AchievementProgress | undefined>;
  collections: () => any;
  overview: (appId: number) => any;
};

// One game: add it to "Completed" when it is a Steam game at 100%. Never removes anything.
export async function addIfCompleted(deps: CompletedDeps, appId: number): Promise<CompletedOutcome> {
  try {
    if (!Number.isInteger(appId) || appId <= 0 || appId > 0xffffffff) return "unavailable";
    const settings = await deps.settings();
    if (!settings.enabled) return "off";
    const overview = deps.overview(appId);
    if (!overview || Number(overview.appid) !== appId || overview.app_type !== GAME_TYPE) return "unavailable";
    const progress = await deps.progress(appId);
    if (!progress) return "unavailable";
    if (progress.total <= 0) return "no-achievements";
    if (!isComplete(progress)) return "incomplete";
    const store = deps.collections();
    if (!store) return "unavailable";
    const existing = findCollection(store, settings.collection);
    if (existing) {
      if (contains(existing, appId)) return "present";
      const editable = existing.AsDragDropCollection?.();
      if (typeof editable?.AddApps !== "function" || typeof existing.Save !== "function") return "unavailable";
      editable.AddApps([overview]);
      await existing.Save();
      return contains(existing, appId) ? "added" : "failed";
    }
    if (!settings.create) return "missing-collection";
    if (typeof store.NewUnsavedCollection !== "function") return "unavailable";
    const created = store.NewUnsavedCollection(settings.collection, undefined, [overview]);
    if (!created || typeof created.Save !== "function") return "unavailable";
    await created.Save();
    return "created";
  } catch { return "failed"; }
}

export type AutoCompletedDeps = CompletedDeps & {
  // Steam's achievement notifications; returns the unregister function, or undefined when unavailable.
  register: (callback: (notification: unknown) => void) => (() => void) | undefined;
  recent: () => number[];
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
  now: () => number;
  // When the last daily sweep ran (milliseconds), kept by the caller (localStorage).
  readSweep: () => number;
  writeSweep: (at: number) => void;
  report: (appId: number, outcome: CompletedOutcome) => void;
  // How long after an unlock to look (Steam updates its progress cache a moment later), and how often to ask
  // whether a sweep is due. Tests shorten them.
  unlockDelay?: number;
  sweepCheck?: number;
};

// Wires it up: unlock notifications (per game, one check at a time, a moment after the unlock) and the daily sweep.
export function installAutoCompleted(deps: AutoCompletedDeps): () => void {
  let disposed = false, sweepTimer: unknown, sweeping = false;
  const pending = new Map<number, unknown>(), busy = new Set<number>();
  const unlockDelay = deps.unlockDelay ?? 4000, sweepCheck = deps.sweepCheck ?? 3600000;

  async function check(appId: number) {
    if (disposed || busy.has(appId)) return;
    busy.add(appId);
    try {
      const outcome = await addIfCompleted(deps, appId);
      if (["added", "created", "missing-collection", "failed"].includes(outcome)) { try { deps.report(appId, outcome); } catch { /* Logging is optional. */ } }
    } finally { busy.delete(appId); }
  }

  let unregister: (() => void) | undefined;
  try {
    unregister = deps.register(notification => {
      if (disposed) return;
      const appId = Number((notification as any)?.unAppID ?? (notification as any)?.appid);
      if (!Number.isInteger(appId) || appId <= 0 || appId > 0xffffffff) return;
      if (pending.has(appId)) deps.cancel(pending.get(appId));
      pending.set(appId, deps.schedule(() => { pending.delete(appId); void check(appId); }, unlockDelay));
    });
  } catch { unregister = undefined; }

  async function sweep() {
    if (disposed || sweeping) return;
    let last = 0;
    try { last = Number(deps.readSweep()) || 0; } catch { last = 0; }
    const now = deps.now();
    if (now - last < DAY && now >= last) return;
    sweeping = true;
    try {
      try { deps.writeSweep(now); } catch { /* Memory is optional. */ }
      let apps: number[] = [];
      try { apps = deps.recent(); } catch { apps = []; }
      for (const appId of apps) { if (disposed) break; await check(appId); }
    } finally { sweeping = false; }
  }

  function tick() {
    if (disposed) return;
    void sweep();
    sweepTimer = deps.schedule(tick, sweepCheck);
  }
  // The first sweep waits a minute: Big Picture is busy starting.
  sweepTimer = deps.schedule(tick, Math.min(60000, sweepCheck));

  return () => {
    disposed = true;
    deps.cancel(sweepTimer);
    for (const timer of pending.values()) deps.cancel(timer);
    pending.clear();
    try { unregister?.(); } catch { /* Steam may already be gone. */ }
  };
}
