// Eternal Core 3.3.10: the Achievements tab on Home (docs/eternal/CORE-UI.md, "Home in 3.3.10"). Recent unlocks and
// the games closest to 100%, Steam's and RetroAchievements' together. Pure: tests hand it fake answers.
//
// Steam: the client's own answers. Recent unlocks from SteamClient.Apps.GetMyAchievementsForApp for the most recently
// played Steam games (`{ result, data: { achieved, unachieved, hidden } }`, also as a JSON string; each achievement
// `{ strName, strImage, rtUnlocked, flAchieved }`), closest to 100% from Steam's achievement progress cache
// (`{ appid, unlocked, total, percentage }` per game).
// RetroAchievements: only through The Collector's read-only bridge (Collector win.36), window.__eternalCollectorAchievements
// { version: 1, getRecentUnlocks(limit), getClosestToComplete(limit) }, each a promise of { ok: true, items } or
// { ok: false, reason } (not_configured, auth_failed, rate_limited, server_unreachable, backend_error, bridge_error...).
// Unlocks: { romId, appId | null, game, platform, title, description, badgeUrl | null, unlockedAt (ISO UTC), hardcore };
// progress: { romId, appId | null, game, platform, earned, total, percent }. Limits 1 to 50. It may register after Core
// (ready event COLLECTOR_ACHIEVEMENTS_READY). Every failure, an absent bridge or an unknown shape means Steam's alone,
// quietly. Nothing here sees a key or a credential.

export type AchievementSource = "steam" | "ra";
export type Unlock = { source: AchievementSource; name: string; game: string; appId?: number; icon?: string; at: number; rarity?: number; hardcore?: boolean };
export type NearlyDone = { source: AchievementSource; game: string; appId?: number; unlocked: number; total: number };

export const RECENT_LIMIT = 5;
export const CLOSEST_LIMIT = 4;
// A Steam achievement fewer than this share of players hold shows its rarity ("6.1% of players").
export const RARE_PERCENT = 10;

const text = (value: unknown, max = 120) => (typeof value === "string" ? value.trim().slice(0, max) : "");
const number = (value: unknown) => { const n = Number(value); return Number.isFinite(n) ? n : undefined; };
// Seconds or milliseconds since the epoch, or an ISO date: seconds out.
export function epochSeconds(value: unknown): number {
  if (typeof value === "string" && value && !/^\d+(\.\d+)?$/.test(value)) { const ms = Date.parse(value); return Number.isFinite(ms) ? Math.floor(ms / 1000) : 0; }
  const n = number(value);
  if (!n || n <= 0) return 0;
  return Math.floor(n > 1e12 ? n / 1000 : n);
}
// Only https or Steam's own loopback images (never a data: or javascript: URL from a bridge).
export function safeImage(value: unknown): string | undefined {
  const url = text(value, 2048);
  return /^https:\/\/[^\s"'()<>]+$/i.test(url) ? url : undefined;
}

function parse(answer: unknown): any {
  if (typeof answer !== "string") return answer;
  try { return JSON.parse(answer); } catch { return undefined; }
}

// Steam's unlocked achievements of one game, from GetMyAchievementsForApp's answer.
export function steamUnlocks(answer: unknown, game: { appid: number; name: string }): Unlock[] {
  const data = parse(answer)?.data ?? parse(answer);
  const achieved = Array.isArray(data?.achieved) ? data.achieved : Array.isArray(data) ? data.filter((item: any) => item?.bAchieved) : [];
  const out: Unlock[] = [];
  for (const item of achieved.slice(0, 500)) {
    const at = epochSeconds(item?.rtUnlocked ?? item?.unlock_time);
    const name = text(item?.strName ?? item?.name);
    if (!at || !name) continue;
    const rarity = number(item?.flAchieved);
    out.push({ source: "steam", name, game: game.name, appId: game.appid, icon: safeImage(item?.strImage), at,
      ...(rarity !== undefined && rarity >= 0 && rarity <= 100 ? { rarity } : {}) });
  }
  return out;
}

// Steam's progress cache (a Map or a plain object of { appid, unlocked, total }): games started and not finished.
export function steamClosest(cache: unknown, name: (appid: number) => string): NearlyDone[] {
  let values: any[] = [];
  try { values = cache && typeof (cache as any).values === "function" ? Array.from((cache as any).values()) : cache && typeof cache === "object" ? Object.values(cache as object) : []; }
  catch { values = []; }
  const out: NearlyDone[] = [];
  for (const entry of values.slice(0, 5000)) {
    const appId = number(entry?.appid), unlocked = number(entry?.unlocked), total = number(entry?.total);
    if (!appId || appId <= 0 || !total || total <= 0 || unlocked === undefined || unlocked <= 0 || unlocked >= total) continue;
    const game = text(name(appId));
    if (game) out.push({ source: "steam", game, appId, unlocked, total });
  }
  return out;
}

// The Collector's bridge, if loaded.
export const COLLECTOR_ACHIEVEMENTS_READY = "eternal_collector_achievements_ready";
export function collectorBridge(host: any): any {
  try {
    const bridge = host?.__eternalCollectorAchievements;
    return bridge && typeof bridge === "object" && (bridge.version === undefined || bridge.version === 1) ? bridge : undefined;
  } catch { return undefined; }
}

// One RA unlock from the bridge, whatever spelling it uses for its fields; undefined when it lacks a name or a date.
export function raUnlock(item: any): Unlock | undefined {
  if (!item || typeof item !== "object") return undefined;
  const name = text(item.name ?? item.title ?? item.achievement);
  const game = text(item.game ?? item.gameTitle ?? item.game_title ?? item.gameName ?? item.game_name);
  const at = epochSeconds(item.unlockedAt ?? item.unlocked_at ?? item.date ?? item.at ?? item.when);
  if (!name || !at) return undefined;
  const appId = number(item.appId ?? item.app_id ?? item.steamAppId);
  return { source: "ra", name, game, at, hardcore: item.hardcore === true || item.mode === "hardcore",
    icon: safeImage(item.icon ?? item.badge ?? item.badgeUrl ?? item.image), ...(appId && appId > 0 ? { appId } : {}) };
}

export function raNearlyDone(item: any): NearlyDone | undefined {
  if (!item || typeof item !== "object") return undefined;
  const game = text(item.game ?? item.title ?? item.gameTitle ?? item.game_title ?? item.name);
  const total = number(item.total ?? item.max ?? item.max_possible ?? item.numAchievements);
  const unlocked = number(item.unlocked ?? item.earned ?? item.awarded ?? item.num_awarded);
  if (!game || !total || total <= 0 || unlocked === undefined || unlocked < 0 || unlocked >= total) return undefined;
  const appId = number(item.appId ?? item.app_id ?? item.steamAppId);
  return { source: "ra", game, unlocked, total, ...(appId && appId > 0 ? { appId } : {}) };
}

// Calls one bridge method; a throw, a rejection or a non-list answer is an empty list.
export async function fromBridge<T>(bridge: any, method: string, limit: number, map: (item: any) => T | undefined): Promise<T[]> {
  try {
    if (typeof bridge?.[method] !== "function") return [];
    const answer = parse(await Promise.resolve(bridge[method](Math.max(1, Math.min(50, Math.floor(limit) || 1)))));
    if (answer && typeof answer === "object" && !Array.isArray(answer) && answer.ok === false) return [];
    const list = Array.isArray(answer) ? answer : Array.isArray(answer?.items) ? answer.items : [];
    return list.slice(0, 50).map(map).filter((item: T | undefined): item is T => item !== undefined);
  } catch { return []; }
}

// Both sources together: the newest unlocks first; the games nearest to 100% first (by share, then fewest left).
export function mergeUnlocks(lists: Unlock[][], limit = RECENT_LIMIT): Unlock[] {
  return lists.flat().sort((a, b) => b.at - a.at).slice(0, limit);
}
export function mergeClosest(lists: NearlyDone[][], limit = CLOSEST_LIMIT): NearlyDone[] {
  return lists.flat().sort((a, b) => b.unlocked / b.total - a.unlocked / a.total || (a.total - a.unlocked) - (b.total - b.unlocked)).slice(0, limit);
}

export const percentOf = (entry: NearlyDone) => Math.floor(entry.unlocked / entry.total * 100);

// The crimson note beside an unlock: Hardcore for an RA unlock earned in hardcore, "6.1% of players" for a rare Steam one.
export function unlockNote(unlock: Unlock, words: { hardcore: string; players: string }): string {
  if (unlock.source === "ra") return unlock.hardcore ? words.hardcore : "";
  if (unlock.rarity !== undefined && unlock.rarity < RARE_PERCENT) return words.players.replace("{n}", (Math.round(unlock.rarity * 10) / 10).toLocaleString("en", { maximumFractionDigits: 1 }));
  return "";
}

// "2 hours ago", "yesterday", "3 days ago", "last week", or the date, in the user's language (Intl).
export function whenText(at: number, now: number, locale: string): string {
  const seconds = Math.max(0, Math.floor(now / 1000) - at);
  try {
    const rtf = new Intl.RelativeTimeFormat(locale || "en", { numeric: "auto" });
    if (seconds < 3600) return rtf.format(-Math.max(1, Math.round(seconds / 60)), "minute");
    if (seconds < 86400) return rtf.format(-Math.round(seconds / 3600), "hour");
    if (seconds < 7 * 86400) return rtf.format(-Math.round(seconds / 86400), "day");
    if (seconds < 30 * 86400) return rtf.format(-Math.round(seconds / (7 * 86400)), "week");
    return new Intl.DateTimeFormat(locale || "en", { day: "numeric", month: "short", year: "numeric" }).format(new Date(at * 1000));
  } catch { return new Date(at * 1000).toISOString().slice(0, 10); }
}

// ---- 3.3.10 (approved addition): rarity cached, the tab drawn from cache ----
//
// Steam's global unlock percentages per game are kept with the time they were read and read again at most every
// RARITY_MAX_AGE_S per game, in the background. The tab draws at once from the last model it showed (MODEL_KEY) with the
// cached rarity, and fills in as answers arrive: it never waits on the network to draw. Bounded; names and numbers
// only (no keys, no credentials).
export const RARITY_KEY = "core.home.achievementRarity.v1";
export const MODEL_KEY = "core.home.achievements.v1";
export const RARITY_MAX_AGE_S = 24 * 3600;
export const RARITY_GAMES = 120;
export type RarityCache = { v: 1; games: Record<string, { at: number; rates: Record<string, number> }> };

export function readJson<T>(storage: { getItem(key: string): string | null } | undefined, key: string): T | undefined {
  try { const text = storage?.getItem(key); return text ? JSON.parse(text) as T : undefined; } catch { return undefined; }
}
export function writeJson(storage: { setItem(key: string, value: string): void } | undefined, key: string, value: unknown) {
  try { storage?.setItem(key, JSON.stringify(value)); } catch { /* Kept in memory only. */ }
}

export function rarityCache(raw: unknown): RarityCache {
  const games: RarityCache["games"] = {};
  const source = (raw as any)?.v === 1 && (raw as any).games && typeof (raw as any).games === "object" ? (raw as any).games : {};
  for (const [id, entry] of Object.entries(source).slice(0, RARITY_GAMES * 2)) {
    const at = number((entry as any)?.at), rates = (entry as any)?.rates;
    if (!/^\d{1,10}$/.test(id) || !at || !rates || typeof rates !== "object") continue;
    const clean: Record<string, number> = {};
    for (const [name, pct] of Object.entries(rates).slice(0, 500)) { const n = number(pct); if (n !== undefined && n >= 0 && n <= 100) clean[text(name)] = n; }
    games[id] = { at, rates: clean };
  }
  return { v: 1, games };
}

export const rarityStale = (cache: RarityCache, appId: number, nowSeconds: number) => {
  const entry = cache.games[String(appId)];
  return !entry || nowSeconds - entry.at >= RARITY_MAX_AGE_S;
};

// Every achievement's global percentage in one GetMyAchievementsForApp answer (achieved, unachieved and hidden alike).
export function ratesFrom(answer: unknown): Record<string, number> {
  const data = parse(answer)?.data ?? parse(answer);
  const out: Record<string, number> = {};
  for (const list of [data?.achieved, data?.unachieved, data?.hidden]) {
    if (!Array.isArray(list)) continue;
    for (const item of list.slice(0, 500)) {
      const name = text(item?.strName ?? item?.name), pct = number(item?.flAchieved);
      if (name && pct !== undefined && pct >= 0 && pct <= 100) out[name] = pct;
    }
  }
  return out;
}

// Stores a game's rates; the oldest games go past RARITY_GAMES.
export function storeRates(cache: RarityCache, appId: number, rates: Record<string, number>, nowSeconds: number): RarityCache {
  const games = { ...cache.games, [String(appId)]: { at: nowSeconds, rates } };
  const kept = Object.entries(games).sort((a, b) => b[1].at - a[1].at).slice(0, RARITY_GAMES);
  return { v: 1, games: Object.fromEntries(kept) };
}

// A Steam unlock's rarity from the cache (none while its game's rates have not arrived yet).
export function withRarity(unlock: Unlock, cache: RarityCache): Unlock {
  if (unlock.source !== "steam" || !unlock.appId) return unlock;
  const pct = cache.games[String(unlock.appId)]?.rates?.[unlock.name];
  const { rarity: _drop, ...rest } = unlock;
  void _drop;
  return pct === undefined ? rest : { ...rest, rarity: pct };
}

// The last model shown, as stored: bounded and re-validated on read.
export function storedModel(raw: unknown): { recent: Unlock[]; closest: NearlyDone[] } | undefined {
  const value = raw as any;
  if (!value || !Array.isArray(value.recent) || !Array.isArray(value.closest)) return undefined;
  const recent = value.recent.slice(0, RECENT_LIMIT).map((item: any) => {
    const name = text(item?.name), at = epochSeconds(item?.at), source = item?.source === "ra" ? "ra" : "steam";
    if (!name || !at) return undefined;
    const appId = number(item?.appId);
    return { source, name, game: text(item?.game), at, hardcore: item?.hardcore === true, icon: safeImage(item?.icon), ...(appId && appId > 0 ? { appId } : {}) } as Unlock;
  }).filter(Boolean) as Unlock[];
  const closest = value.closest.slice(0, CLOSEST_LIMIT).map((item: any) => {
    const entry = raNearlyDone(item);
    return entry ? { ...entry, source: item?.source === "ra" ? "ra" : "steam" } as NearlyDone : undefined;
  }).filter(Boolean) as NearlyDone[];
  return { recent, closest };
}
