// Eternal Core: the "Core UI" setting (Quick Access > Core). On by default; kept in Steam's localStorage under
// core.ui.enabled ("0" = off, anything else or nothing = on). coreUi.ts subscribes and removes or restores
// everything Core UI adds as soon as it changes. Self-contained (no imports): tests load it on its own.

export const CORE_UI_SETTING_KEY = "core.ui.enabled";

type Storage = { getItem(key: string): string | null; setItem(key: string, value: string): void };

export type CoreUiSetting = {
  get(): boolean;
  set(on: boolean): void;
  subscribe(listener: () => void): () => void;
};

export function createCoreUiSetting(storage?: Storage): CoreUiSetting {
  const read = () => { try { return storage?.getItem(CORE_UI_SETTING_KEY) !== "0"; } catch { return true; } };
  let value = read();
  const listeners = new Set<() => void>();
  return {
    get: () => value,
    set(on: boolean) {
      const next = on === true;
      try { storage?.setItem(CORE_UI_SETTING_KEY, next ? "1" : "0"); } catch { /* Kept for this session only. */ }
      if (next === value) return;
      value = next;
      for (const listener of [...listeners]) { try { listener(); } catch { /* One listener cannot stop the others. */ } }
    },
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
  };
}

function defaultStorage(): Storage | undefined {
  try { return typeof localStorage === "undefined" ? undefined : localStorage; } catch { return undefined; }
}

export const coreUiSetting: CoreUiSetting = createCoreUiSetting(defaultStorage());
