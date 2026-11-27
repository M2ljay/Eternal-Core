// Eternal Core: Core Starfield as Steam's screensaver (docs/eternal/CORE-UI.md, "Screensaver").
//
// - A one-time default: once Core Starfield is registered in Steam's screensaver list (circlesScreensaver.ts), Steam's
//   own game-hero slideshow (or no choice at all) becomes Core Starfield. A flag in Steam's localStorage makes it run
//   once only: a screensaver the user picks afterwards, Steam's slideshow included, is never overridden. A user who had
//   already picked another screensaver keeps it (the flag is set then too).
// - The "Use Core Starfield as screensaver" switch (Quick Access > Core > Look) selects it or goes back to Steam's
//   slideshow.
//
// Writes go only through Steam's settings store setters (whichever this client has); no config file is touched. A
// client without a known setter is left alone and the default is tried again on the next start.
// Self-contained (no imports): tests/coreScreensaver.test.mjs drives it with fake stores.

export const CORE_STARFIELD_ID = "Core Starfield";
export const STEAM_SLIDESHOW_ID = "steam-gameslideshow";
export const STARFIELD_DEFAULT_FLAG = "core.ui.starfield-default";
const KEY = "screensaver_current_id";

type Storage = { getItem(key: string): string | null; setItem(key: string, value: string): void };

export function currentScreensaver(store: any): string | undefined {
  try {
    const value = store?.clientSettings?.[KEY];
    return typeof value === "string" ? value : undefined;
  } catch { return undefined; }
}

// Steam's slideshow, under its id or its host name, or nothing chosen yet.
export function isSteamDefault(id: string | undefined): boolean {
  return !id || id === STEAM_SLIDESHOW_ID || id === "steam-gameslideshow.steamscreensavers.host";
}

// Sets Steam's screensaver through the settings store's own setter. Answers whether a setter took it.
export function setScreensaver(store: any, id: string): boolean {
  const setters: Array<() => unknown> = [
    () => store.SetClientSetting(KEY, id),
    () => store.SetSetting(KEY, id),
    () => store.UpdateClientSettings({ [KEY]: id }),
    () => store.SetClientSettings({ [KEY]: id }),
  ];
  const names = ["SetClientSetting", "SetSetting", "UpdateClientSettings", "SetClientSettings"];
  for (let i = 0; i < setters.length; i++) {
    try {
      if (typeof store?.[names[i]] !== "function") continue;
      const result: any = setters[i]();
      if (result && typeof result.catch === "function") result.catch(() => {});
      return true;
    } catch { /* Try the next one. */ }
  }
  return false;
}

export type DefaultResult = "applied" | "already-applied" | "user-choice" | "unavailable";

export function applyStarfieldDefault(store: any, storage: Storage | undefined): DefaultResult {
  let flagged = false;
  try { flagged = storage?.getItem(STARFIELD_DEFAULT_FLAG) === "1"; } catch { flagged = false; }
  if (flagged) return "already-applied";
  const remember = () => { try { storage?.setItem(STARFIELD_DEFAULT_FLAG, "1"); } catch { /* Tried again next start. */ } };
  const current = currentScreensaver(store);
  if (current === CORE_STARFIELD_ID) { remember(); return "already-applied"; }
  if (!isSteamDefault(current)) { remember(); return "user-choice"; }
  if (!setScreensaver(store, CORE_STARFIELD_ID)) return "unavailable";
  remember();
  return "applied";
}

// The Look section's switch.
export type ScreensaverControl = { available(): boolean; get(): boolean; set(on: boolean): boolean; subscribe(listener: () => void): () => void };

export function createScreensaverControl(store: () => any): ScreensaverControl {
  const listeners = new Set<() => void>();
  return {
    available: () => !!store() && currentScreensaver(store()) !== undefined,
    get: () => currentScreensaver(store()) === CORE_STARFIELD_ID,
    set(on: boolean) {
      const done = setScreensaver(store(), on ? CORE_STARFIELD_ID : STEAM_SLIDESHOW_ID);
      for (const listener of [...listeners]) { try { listener(); } catch { /* Others still hear it. */ } }
      return done;
    },
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
  };
}
