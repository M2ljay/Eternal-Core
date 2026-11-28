// Eternal Core: the Club tab's view of the Steam library: entries for matching (with the platforms The
// Collector's collections give them), Steam's own artwork, and play / game page actions. Every Steam call is
// guarded: a missing store or method leaves the pick unmatched or without art, never broken.

import type { LibraryEntry } from "./clubMatch";
import { platformOf } from "./clubMatch";

const SHORTCUT_TYPE = 1073741824;

const plausibleAppId = (value: unknown) => {
  const id = Number(value);
  return Number.isSafeInteger(id) && id > 0 && id <= 0xffffffff ? id : null;
};

function appStore(): any {
  return (globalThis as any).appStore ?? (globalThis as any).window?.appStore;
}

function overviews(): any[] {
  const store = appStore();
  const values: any[] = [];
  const add = (value: any) => {
    if (!value) return;
    if (value instanceof Map) { value.forEach((entry, key) => add(entry && typeof entry === "object" ? { ...entry, appid: entry.appid ?? key } : null)); return; }
    if (Array.isArray(value)) { value.forEach(add); return; }
    if (typeof value === "object") values.push(value);
  };
  // 3.3.11: both sources, always. A pick the user owns but has not installed must still match (the Consoles row
  // showed only installed picks); readLibrary keeps the first entry per app id.
  try { add(store?.allApps); } catch { /* the next source may still answer */ }
  try { add(store?.m_mapAppOverview); } catch { /* none */ }
  return values;
}

function collectionPlatforms(): Map<number, string[]> {
  const map = new Map<number, string[]>();
  try {
    const store = (globalThis as any).collectionStore;
    const collections: any[] = store?.userCollections ?? [];
    for (const collection of collections) {
      const platform = platformOf(collection?.displayName ?? collection?.strName ?? collection?.m_strName);
      if (!platform) continue;
      const apps: any = collection?.allApps ?? collection?.apps;
      const ids: number[] = [];
      if (apps instanceof Map) apps.forEach((value: any, key: any) => { const id = plausibleAppId(value?.appid ?? key); if (id) ids.push(id); });
      else if (Array.isArray(apps)) apps.forEach((value: any) => { const id = plausibleAppId(value?.appid ?? value); if (id) ids.push(id); });
      else if (apps instanceof Set) apps.forEach((value: any) => { const id = plausibleAppId(value?.appid ?? value); if (id) ids.push(id); });
      for (const id of ids) map.set(id, [...(map.get(id) ?? []), platform]);
    }
  } catch { /* collections are a refinement; titles still match */ }
  return map;
}

export function readLibrary(): LibraryEntry[] {
  const platforms = collectionPlatforms();
  const seen = new Set<number>();
  const entries: LibraryEntry[] = [];
  for (const overview of overviews()) {
    const appId = plausibleAppId(overview?.appid ?? overview?.app_id ?? overview?.unAppID);
    const name = String(overview?.display_name ?? overview?.localized_name ?? overview?.name ?? overview?.strDisplayName ?? "");
    if (!appId || !name || seen.has(appId)) continue;
    seen.add(appId);
    const shortcut = overview?.app_type === SHORTCUT_TYPE || appId >= 0x80000000;
    entries.push({ appId, name, shortcut, platforms: platforms.get(appId) ?? [] });
  }
  return entries;
}

function overviewFor(appId: number): any {
  try { return appStore()?.GetAppOverviewByAppID?.(appId); } catch { return undefined; }
}

// Steam's own artwork for the app: the user's custom (SteamGridDB / The Collector) portrait first, then Steam's.
export function steamArtFor(appId: number): string {
  const store = appStore();
  const overview = overviewFor(appId);
  if (!store || !overview) return "";
  const attempts: (() => unknown)[] = [
    () => store.GetCustomVerticalCapsuleURLs?.(overview)?.[0],
    () => store.GetVerticalCapsuleURLForApp?.(overview),
    () => store.GetCustomLandcapeImageURLs?.(overview)?.[0],
    () => store.GetLandscapeImageURLForApp?.(overview),
  ];
  for (const attempt of attempts) {
    try { const url = attempt(); if (typeof url === "string" && url) return url; } catch { /* next */ }
  }
  return "";
}

// A: play. Steam's own RunGame, so Core's launch patches (owned-launch dispatcher, return navigation) see it.
export function playApp(appId: number): boolean {
  try {
    const overview = overviewFor(appId);
    const gameId = String(overview?.m_gameid ?? overview?.gameid ?? appId);
    const apps = (globalThis as any).SteamClient?.Apps;
    if (typeof apps?.RunGame !== "function") return false;
    apps.RunGame(gameId, "", -1, 100);
    return true;
  } catch { return false; }
}

// X: the game's page in the library.
export function openGamePage(appId: number, navigate: ((path: string) => void) | undefined): boolean {
  try { navigate?.(`/library/app/${appId}`); return Boolean(navigate); } catch { return false; }
}
