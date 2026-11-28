// Eternal Core 3.3.10: Home's two pages, Home and Consoles (docs/eternal/CORE-UI.md, "Home in 3.3.10"), switched with
// L1 and R1 from screen one, and the measures of screen one's row. Pure: tests drive it directly.
//
// Each page is two screens. Screen one is the focused item's art filling the screen with a row on the footer: on Home,
// the recent games Steam lists; on Consoles, one card per console. Screen two is below the fold: on Home, the tabs
// Favorites, Achievements and Club; on Consoles, the focused console's recent games.

export type HomePage = "home" | "consoles";
export const HOME_PAGES: HomePage[] = ["home", "consoles"];

// Steam's gamepad buttons (GamepadButton in @decky/ui): the bumpers switch pages, down leaves screen one.
export const BUTTON = { BUMPER_LEFT: 5, BUMPER_RIGHT: 6, DIR_DOWN: 10 } as const;

let page: HomePage = "home";
let focusedConsole = "";
const listeners = new Set<() => void>();
const notify = () => { for (const listener of [...listeners]) { try { listener(); } catch { /* Others still hear it. */ } } };

export const homePage = () => page;
export const focusedConsoleId = () => focusedConsole;
export function subscribeHomePage(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function setHomePage(next: HomePage) {
  if (!HOME_PAGES.includes(next) || next === page) return;
  page = next;
  notify();
}
export function setFocusedConsole(id: string) {
  const next = typeof id === "string" ? id : "";
  if (next === focusedConsole) return;
  focusedConsole = next;
  notify();
}

// The page a bumper leads to: L1 to the page on the left, R1 to the one on the right; no wrap. Anything else: none.
export function pageForButton(current: HomePage, button: unknown): HomePage | undefined {
  const at = HOME_PAGES.indexOf(current);
  if (button === BUTTON.BUMPER_LEFT) return at > 0 ? HOME_PAGES[at - 1] : undefined;
  if (button === BUTTON.BUMPER_RIGHT) return at >= 0 && at < HOME_PAGES.length - 1 ? HOME_PAGES[at + 1] : undefined;
  return undefined;
}

// Screen one's row, from the approved board (1920 x 1080) in viewport units, so the TV's 1348 x 758 gets the board's
// proportions: the focused tile 29% of the height (about 219 px on the TV), the others 25%, sitting 2.6% above the
// footer (28 px on the board). Games are portrait (2:3), consoles landscape (16:9). The focused tile lifts 10 px on the
// board (7 px on the TV); the others are dimmed to 82%.
export const HOME_ROW = {
  gameFocusedVh: 28.9, gameVh: 25.2, consoleFocusedVh: 20.8, consoleVh: 17.7,
  gapVw: 1.15, insetVw: 3.33, aboveFooterVh: 2.6, footerPx: 42, liftPx: 7, dim: 0.82,
} as const;

// Px at a viewport, for the docs and tests (the CSS uses the vh/vw values directly).
export function rowSizes(width: number, height: number) {
  const vh = (n: number) => Math.round(n * height / 100), vw = (n: number) => Math.round(n * width / 100);
  const portrait = (h: number) => ({ width: Math.round(h * 2 / 3), height: h });
  const landscape = (h: number) => ({ width: Math.round(h * 16 / 9), height: h });
  return {
    game: portrait(vh(HOME_ROW.gameVh)), gameFocused: portrait(vh(HOME_ROW.gameFocusedVh)),
    console: landscape(vh(HOME_ROW.consoleVh)), consoleFocused: landscape(vh(HOME_ROW.consoleFocusedVh)),
    gap: vw(HOME_ROW.gapVw), inset: vw(HOME_ROW.insetVw), bottom: HOME_ROW.footerPx + vh(HOME_ROW.aboveFooterVh),
  };
}

// The recent games Steam hands its Home row: the `games` list of the carousel in the recents section's output (or its
// own props), as app ids. Steam's sentinel 0 and anything not a positive integer are dropped; duplicates once. Undefined
// when no such list is found (then Steam's own row stays).
export function recentGamesIn(tree: unknown, props?: any, depth = 0): number[] | undefined {
  const clean = (list: unknown[]) => [...new Set(list.map(Number).filter(id => Number.isInteger(id) && id > 0))].slice(0, 40);
  if (props && Array.isArray(props.games)) return clean(props.games);
  if (!tree || depth > 24) return undefined;
  if (Array.isArray(tree)) {
    for (const node of tree) { const found = recentGamesIn(node, undefined, depth + 1); if (found) return found; }
    return undefined;
  }
  const own = (tree as any)?.props;
  if (!own || typeof own !== "object") return undefined;
  if (Array.isArray(own.games) && (typeof own.onItemFocus === "function" || "showFeaturedItem" in own || "refOnItemFocus" in own)) return clean(own.games);
  for (const key of ["children", "native"]) {
    const found = recentGamesIn(own[key], undefined, depth + 1);
    if (found) return found;
  }
  return undefined;
}

// ---- 3.3.10 additions (approved after the boards) ----

// The full-screen art follows focus only once it has rested on a tile for HERO_DELAY_MS, then crossfades over
// HERO_FADE_MS; the art of the tiles HERO_PRELOAD either side is loaded ahead.
export const HERO_DELAY_MS = 150;
export const HERO_FADE_MS = 250;
export const HERO_PRELOAD = 2;
export function neighbours(index: number, length: number, reach = HERO_PRELOAD): number[] {
  const out: number[] = [];
  for (let step = 1; step <= reach; step++) for (const at of [index + step, index - step]) if (at >= 0 && at < length) out.push(at);
  return out;
}

// Back to screen one from screen two: B anywhere there, or up from its top row (Home's tab row; the Consoles page's
// games). On screen one B stays Steam's.
export const BUTTON_CANCEL = 2;
export const BUTTON_UP = 9;
export function backToScreenOne(button: unknown, where: { inTwo: boolean; inTopRow: boolean }): boolean {
  if (!where.inTwo) return false;
  return button === BUTTON_CANCEL || (button === BUTTON_UP && where.inTopRow);
}

// The club's current Game of the Week and Game of the Month, when owned, lead the Consoles row (at most those two, in
// that order; nothing when neither is owned). Tags in the pick's colour.
export const CONSOLE_PICK_EVENTS = ["gotw", "gotm"] as const;
export const CONSOLE_PICK_TONES: Record<(typeof CONSOLE_PICK_EVENTS)[number], string> = { gotw: "#8F7CB0", gotm: "#B07A84" };
export type ConsolePick = { event: (typeof CONSOLE_PICK_EVENTS)[number]; label: string; title: string; appId: number; tone: string };
export function consolePicks<P extends { label?: string; title?: string }>(picks: P[] | undefined, eventOf: (pick: P) => string | null,
  match: (pick: P) => { appId: number } | null, ended: (pick: P) => boolean = () => false): ConsolePick[] {
  const out: ConsolePick[] = [];
  for (const event of CONSOLE_PICK_EVENTS) {
    const pick = (Array.isArray(picks) ? picks : []).find(item => { try { return eventOf(item) === event && !ended(item); } catch { return false; } });
    if (!pick) continue;
    let found: { appId: number } | null = null;
    try { found = match(pick); } catch { found = null; }
    if (!found || !(found.appId > 0) || out.some(item => item.appId === found!.appId)) continue;
    out.push({ event, label: String(pick.label ?? ""), title: String(pick.title ?? ""), appId: found.appId, tone: CONSOLE_PICK_TONES[event] });
  }
  return out;
}

// The user's Library icon set (index.tsx hands it in through coreHome.setCoreHomeIcons): a console tile without
// ColorGrade art falls back to its icon.
let iconSource: () => any = () => undefined;
export function setHomeIconSource(source: () => any) { iconSource = source; }
export const homeIconSet = () => { try { return iconSource(); } catch { return undefined; } };
