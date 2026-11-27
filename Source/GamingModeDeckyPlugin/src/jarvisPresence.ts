// Eternal Core: Jarvis in Core UI phase 1 (docs/eternal/CORE-UI.md, docs/eternal/JARVIS.md).
// - Jarvis's own Quick Access tab: its id, opening or closing it from the talk combo's short tap, and the request
//   that puts the cursor in its input field.
// - The presence orb in the top bar: idle (dim steel), busy (a slow crimson pulse while listening or thinking),
//   ready (a brief crimson ring when an answer arrives), hidden during a game unless Jarvis is active.
// Self-contained (no imports): tests/coreJarvisTab.test.mjs runs it against fake Steam stores.

// "JRV". Core's own tab is 0x50484B ("PHK", qam.ts); Steam's tabs are small numbers, Decky's 999.
export const JARVIS_TAB_ID = 0x4A5256;
export const JARVIS_READY_MS = 4000;
// Steam's side menu numbers (MenuStore.GetOpenSideMenu): 1 = the Steam menu, 2 = Quick Access.
const QUICK_ACCESS = 2;

let focusRequested = 0;
const focusListeners = new Set<() => void>();
export function requestJarvisFocus(now = Date.now()) {
  focusRequested = now;
  for (const listener of [...focusListeners]) { try { listener(); } catch { /* A closed tab. */ } }
}
// True once per request, within five seconds of it (the tab's input takes the focus when it mounts or hears this).
export function takeJarvisFocus(now = Date.now()): boolean {
  if (!focusRequested || now - focusRequested > 5000) return false;
  focusRequested = 0;
  return true;
}
export function subscribeJarvisFocus(listener: () => void) { focusListeners.add(listener); return () => { focusListeners.delete(listener); }; }

type MenuStore = { GetOpenSideMenu?: () => number; GetQuickAccessTab?: () => number; OpenQuickAccessMenu?: (tab?: number) => void; CloseSideMenus?: () => void };
export type TabDeps = {
  // The menu store of the window Quick Access opens in: the game's overlay while a game runs, else Big Picture's.
  menuStore: () => MenuStore | undefined;
  navigation: () => { OpenQuickAccessMenu?: (tab?: number) => void; CloseSideMenus?: () => void } | undefined;
};

// A short tap on the talk combo: open Quick Access on Jarvis's tab (the input focused), or close it when it is
// already open there. Answers what it did; "unavailable" when Steam offers neither path.
export function toggleJarvisTab(deps: TabDeps): "opened" | "closed" | "unavailable" {
  let store: MenuStore | undefined;
  try { store = deps.menuStore(); } catch { store = undefined; }
  let open = false;
  try { open = store?.GetOpenSideMenu?.() === QUICK_ACCESS && store?.GetQuickAccessTab?.() === JARVIS_TAB_ID; } catch { open = false; }
  const navigation = (() => { try { return deps.navigation(); } catch { return undefined; } })();
  if (open) {
    try {
      if (typeof store?.CloseSideMenus === "function") store.CloseSideMenus();
      else if (typeof navigation?.CloseSideMenus === "function") navigation.CloseSideMenus();
      else return "unavailable";
      return "closed";
    } catch { return "unavailable"; }
  }
  try {
    if (typeof store?.OpenQuickAccessMenu === "function") store.OpenQuickAccessMenu(JARVIS_TAB_ID);
    else if (typeof navigation?.OpenQuickAccessMenu === "function") navigation.OpenQuickAccessMenu(JARVIS_TAB_ID);
    else return "unavailable";
  } catch { return "unavailable"; }
  requestJarvisFocus();
  return "opened";
}

// Steam's overlay window for the running game (its MenuStore opens Quick Access over the game), else the Big
// Picture window's.
export function jarvisMenuStore(windowStore: any, runningApp: number): MenuStore | undefined {
  try {
    if (runningApp > 0 && Array.isArray(windowStore?.OverlayWindows)) {
      const overlay = windowStore.OverlayWindows.find((candidate: any) => {
        try { return candidate?.IsGamepadUIOverlayWindow?.() === true && candidate?.MenuStore && candidate?.BrowserWindow?.closed !== true; } catch { return false; }
      });
      if (overlay?.MenuStore) return overlay.MenuStore;
    }
    return windowStore?.GamepadUIMainWindowInstance?.MenuStore ?? undefined;
  } catch { return undefined; }
}

export type OrbState = "hidden" | "idle" | "busy" | "ready";
export type PresenceInput = { enabled: boolean; listening: boolean; working: boolean; readyUntil: number; now: number; gameRunning: boolean };

export function orbState(input: PresenceInput): OrbState {
  if (!input.enabled) return "hidden";
  if (input.listening || input.working) return "busy";
  if (input.readyUntil > input.now) return "ready";
  // During a game the orb shows only while Jarvis is doing something.
  return input.gameRunning ? "hidden" : "idle";
}

// Remembers when an answer arrived, from the runtime's snapshots (talk answers, screen answers) and from the chat.
export function createPresence(now: () => number = Date.now) {
  let readyUntil = 0, lastAnswer: unknown, lastScreen: unknown;
  const listeners = new Set<() => void>();
  const notify = () => { for (const listener of [...listeners]) { try { listener(); } catch { /* A closed view. */ } } };
  return {
    ready() { readyUntil = now() + JARVIS_READY_MS; notify(); },
    observe(snapshot: { answer?: unknown; screen?: unknown } | undefined) {
      if (!snapshot) return;
      const fresh = (snapshot.answer && snapshot.answer !== lastAnswer) || (snapshot.screen && snapshot.screen !== lastScreen);
      lastAnswer = snapshot.answer; lastScreen = snapshot.screen;
      if (fresh) this.ready(); else notify();
    },
    readyUntil: () => readyUntil,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
  };
}
export type Presence = ReturnType<typeof createPresence>;
