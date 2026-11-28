// Eternal Core 3.3.11: the controller's back buttons and trackpads in Steam's menus. This Steam version has no Big
// Picture controller layout, so outside games the UI hears only the standard buttons. Core fills the gap:
// - L4 is A and R4 is B: Steam's own focus navigation is told the button went down and up (OnButtonActionInternal, the
//   call Steam's input makes; button 1 OK, 2 CANCEL; source 1 gamepad). L5/R5 stay Jarvis (jarvisCombos.ts).
// - Both trackpads move the Windows pointer like a laptop trackpad, and a pad click is a left click. The plugin reads
//   the pads here (Steam owns the controller); batched deltas go to the agent through the backend
//   (main.py core_pointer_input, at most one call in flight, about 60 a second), which moves the pointer with SendInput.
//   The right pad's lower half as a right click was left out: Steam reports one click bit per pad, so the half would be
//   read from the touch position at the moment of the click, which misfires on a pad touched near its middle.
// - Only outside games: the moment a game runs (the overlay over it included), nothing is read or sent, so per-game Steam
//   Input layouts stay in charge. Off with the setting "Back buttons and trackpads in menus" (on by default).
// Bit positions follow jarvisCombos.ts (ulButtons; ulUpperButtons holds the back grips L4/R4).

export const MENU_INPUT = {
  // ulUpperButtons bits.
  L4: 1 << 9, R4: 1 << 10,
  // ulButtons bits: pad clicks and touches.
  LEFT_PAD_CLICK: 1 << 17, RIGHT_PAD_CLICK: 1 << 18, LEFT_PAD_TOUCH: 1 << 19, RIGHT_PAD_TOUCH: 1 << 20,
  // Steam's navigation buttons and the gamepad source.
  OK: 1, CANCEL: 2, SOURCE_GAMEPAD: 1,
  // Pointer: pixels for a full pad width at rest, the acceleration and its cap, how often a batch may go.
  BASE_PX: 520, ACCEL: 0.9, MAX_GAIN: 3.2, SEND_MS: 16,
} as const;

export const MENU_INPUT_SETTING_KEY = "core.menuInput.enabled";

export type PadState = { lower: number; upper: number; lx: number; ly: number; rx: number; ry: number };
export type NavButton = { button: number; down: boolean };

// L4/R4 edges since the last packet: A or B, down on press and up on release.
export function backButtonEdges(prior: number, upper: number): NavButton[] {
  const out: NavButton[] = [];
  for (const [bit, button] of [[MENU_INPUT.L4, MENU_INPUT.OK], [MENU_INPUT.R4, MENU_INPUT.CANCEL]] as const) {
    const was = (prior & bit) !== 0, now = (upper & bit) !== 0;
    if (was !== now) out.push({ button, down: now });
  }
  return out;
}

// Pad clicks become the left button: down when the first pad clicks, up when no pad is clicked.
export function padClickEdge(prior: number, lower: number): "left-down" | "left-up" | undefined {
  const mask = MENU_INPUT.LEFT_PAD_CLICK | MENU_INPUT.RIGHT_PAD_CLICK;
  const was = (prior & mask) !== 0, now = (lower & mask) !== 0;
  return was === now ? undefined : now ? "left-down" : "left-up";
}

// One pad's movement since the last packet while it stays touched (Steam's pad coordinates run -32768..32767, y up).
// A new touch is a fresh start, never a jump from where the finger left.
export function padMovement(prior: PadState | undefined, now: PadState): { dx: number; dy: number } {
  if (!prior) return { dx: 0, dy: 0 };
  let dx = 0, dy = 0;
  const pads: Array<[number, "lx" | "rx", "ly" | "ry"]> = [[MENU_INPUT.LEFT_PAD_TOUCH, "lx", "ly"], [MENU_INPUT.RIGHT_PAD_TOUCH, "rx", "ry"]];
  for (const [touch, x, y] of pads) {
    if ((prior.lower & touch) === 0 || (now.lower & touch) === 0) continue;
    dx += now[x] - prior[x];
    dy -= now[y] - prior[y];
  }
  return { dx, dy };
}

// Pad units to pixels with trackpad-style acceleration: slow movement is precise, a quick swipe goes further.
export function pointerPixels(dx: number, dy: number, elapsedMs: number): { x: number; y: number } {
  const units = 65536;
  const base = MENU_INPUT.BASE_PX / units;
  const ms = Math.max(1, Math.min(100, elapsedMs || 1));
  const speed = Math.hypot(dx, dy) / units / ms * 1000; // pad widths a second
  const gain = Math.min(MENU_INPUT.MAX_GAIN, 1 + MENU_INPUT.ACCEL * Math.max(0, speed - 0.25));
  return { x: dx * base * gain, y: dy * base * gain };
}

// Whether Core handles the back buttons and pads now: the setting on and no game running.
export function menuInputActive(state: { enabled: boolean; runningAppId: number }): boolean {
  return state.enabled === true && !(Number(state.runningAppId) > 0);
}

export type MenuInputDeps = {
  input: () => any;
  enabled: () => boolean;
  runningAppId: () => number;
  navController: () => any;
  send: (dx: number, dy: number, buttons: string[]) => Promise<unknown>;
  now?: () => number;
  setTimer?: (fn: () => void, ms: number) => any;
};

export function installMenuInput(deps: MenuInputDeps): () => void {
  const now = () => { try { return deps.now ? deps.now() : Date.now(); } catch { return Date.now(); } };
  const later = deps.setTimer ?? ((fn: () => void, ms: number) => setTimeout(fn, ms));
  const pads = new Map<number, { state: PadState; at: number }>();
  let pendingX = 0, pendingY = 0, pendingButtons: string[] = [], inFlight = false, scheduled = false, leftDown = false;
  const nav = (button: number, down: boolean) => {
    try { deps.navController()?.OnButtonActionInternal?.(down, button, MENU_INPUT.SOURCE_GAMEPAD); } catch { /* Steam keeps its own input. */ }
  };
  const flush = () => {
    scheduled = false;
    if (inFlight) { schedule(); return; }
    const x = Math.trunc(pendingX), y = Math.trunc(pendingY);
    if (!x && !y && !pendingButtons.length) return;
    pendingX -= x; pendingY -= y;
    const buttons = pendingButtons; pendingButtons = [];
    inFlight = true;
    Promise.resolve().then(() => deps.send(x, y, buttons)).catch(() => {}).finally(() => { inFlight = false; });
  };
  const schedule = () => { if (!scheduled) { scheduled = true; later(flush, MENU_INPUT.SEND_MS); } };
  const releaseAll = () => {
    if (leftDown) { pendingButtons.push("left-up"); leftDown = false; flush(); }
    pendingX = 0; pendingY = 0; pads.clear();
  };
  const onChanges = (changes: any[]) => {
    let active = false;
    try { active = menuInputActive({ enabled: deps.enabled(), runningAppId: deps.runningAppId() }); } catch { active = false; }
    if (!active) { releaseAll(); return; }
    for (const change of Array.isArray(changes) ? changes : []) {
      const index = Number(change?.unControllerIndex) || 0;
      const state: PadState = { lower: Number(change?.ulButtons) >>> 0, upper: Number(change?.ulUpperButtons) >>> 0,
        lx: Number(change?.sLeftPadX) || 0, ly: Number(change?.sLeftPadY) || 0, rx: Number(change?.sRightPadX) || 0, ry: Number(change?.sRightPadY) || 0 };
      const prior = pads.get(index);
      const at = now();
      for (const edge of backButtonEdges(prior?.state.upper ?? 0, state.upper)) nav(edge.button, edge.down);
      const click = padClickEdge(prior?.state.lower ?? 0, state.lower);
      if (click === "left-down" && !leftDown) { leftDown = true; pendingButtons.push(click); }
      if (click === "left-up" && leftDown) { leftDown = false; pendingButtons.push(click); }
      const move = padMovement(prior?.state, state);
      if (move.dx || move.dy) {
        const px = pointerPixels(move.dx, move.dy, at - (prior?.at ?? at));
        pendingX += px.x; pendingY += px.y;
      }
      pads.set(index, { state, at });
      if (pendingButtons.length || Math.abs(pendingX) >= 1 || Math.abs(pendingY) >= 1) schedule();
    }
  };
  let registration: any;
  try { registration = deps.input()?.RegisterForControllerStateChanges?.(onChanges); } catch { registration = undefined; }
  return () => {
    releaseAll();
    try { registration?.unregister?.(); } catch { /* Steam drops it with the page. */ }
  };
}

// The setting, stored like Core UI's own (coreUiSetting.ts): on unless set to "0".
type Listener = () => void;
const listeners = new Set<Listener>();
const storage = (): Storage | undefined => { try { return (globalThis as any).localStorage; } catch { return undefined; } };
export const menuInputSetting = {
  get(): boolean { try { return storage()?.getItem(MENU_INPUT_SETTING_KEY) !== "0"; } catch { return true; } },
  set(on: boolean) { try { storage()?.setItem(MENU_INPUT_SETTING_KEY, on ? "1" : "0"); } catch { /* Kept for this session only. */ } listeners.forEach(listener => listener()); },
  subscribe(listener: Listener): () => void { listeners.add(listener); return () => { listeners.delete(listener); }; },
};
