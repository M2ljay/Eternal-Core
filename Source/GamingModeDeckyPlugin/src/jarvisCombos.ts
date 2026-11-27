// Eternal Core: Jarvis's controller combos (docs/eternal/JARVIS.md). Steam owns the controller and the agent never reads
// one (DashboardShortcutService.cs), so the combos are watched here, inside Steam, through
// SteamClient.Input.RegisterForControllerStateChanges: two to four buttons held together. Dictation and "talk to Jarvis"
// record while the combo is held; the screen helper fires once per press. Bit positions follow Steam's
// ControllerStateChange as @decky/ui's typings describe it (ulButtons; ulUpperButtons holds the back grips L4/R4). The
// 2026 Steam Controller is assumed to report its grips like the Steam Deck (inferred; TV checklist). The same parser
// exists in C# (Shared/JarvisCore.cs, JarvisCombos) for the desktop app and the agent.
// Self-contained on purpose: tests/coreJarvis.test.mjs loads this file alone.

export type ComboKind = "dictate" | "talk" | "screen";
export type JarvisCombo = { text: string; lower: number; upper: number };

const BITS: Record<string, [boolean, number]> = {
  R2: [false, 0], L2: [false, 1], R1: [false, 2], L1: [false, 3], Y: [false, 4], B: [false, 5], X: [false, 6], A: [false, 7],
  UP: [false, 8], RIGHT: [false, 9], LEFT: [false, 10], DOWN: [false, 11], SELECT: [false, 12], START: [false, 14],
  L5: [false, 15], R5: [false, 16], L3: [false, 22], R3: [false, 26], L4: [true, 9], R4: [true, 10],
};
const ALIASES: Record<string, string> = {
  LB: "L1", RB: "R1", LT: "L2", RT: "R2", VIEW: "SELECT", BACK: "SELECT", MENU: "START", LS: "L3", RS: "R3",
  DUP: "UP", DDOWN: "DOWN", DLEFT: "LEFT", DRIGHT: "RIGHT",
};
const RESERVED = new Set(["STEAM", "GUIDE", "QAM", "HOME"]);
const ORDER = ["L4", "R4", "L5", "R5", "L1", "R1", "L2", "R2", "L3", "R3", "SELECT", "START", "UP", "DOWN", "LEFT", "RIGHT", "A", "B", "X", "Y"];

export function parseCombo(text: unknown): JarvisCombo | undefined {
  if (typeof text !== "string" || !text.trim() || text.length > 64) return undefined;
  const buttons: string[] = [];
  for (const raw of text.split("+")) {
    let name = raw.trim().toUpperCase().replace(/ /g, "");
    name = ALIASES[name] ?? name;
    if (RESERVED.has(name) || !(name in BITS) || buttons.includes(name)) return undefined;
    buttons.push(name);
  }
  if (buttons.length < 2 || buttons.length > 4) return undefined;
  buttons.sort((a, b) => ORDER.indexOf(a) - ORDER.indexOf(b));
  let lower = 0, upper = 0;
  for (const button of buttons) {
    const [isUpper, bit] = BITS[button];
    if (isUpper) upper = (upper | (1 << bit)) >>> 0; else lower = (lower | (1 << bit)) >>> 0;
  }
  return { text: buttons.join("+"), lower, upper };
}

export function comboHeld(combo: JarvisCombo, buttons: unknown, upperButtons: unknown): boolean {
  const lower = Number(buttons) >>> 0, upper = Number(upperButtons) >>> 0;
  return (lower & combo.lower) >>> 0 === combo.lower && (upper & combo.upper) >>> 0 === combo.upper;
}

// Core UI phase 1: the talk combo has two behaviours. A short tap (released within JARVIS_TAP_MS) opens or closes
// Jarvis's Quick Access tab, also during a game; holding it is push-to-talk as before. Recording starts on the press
// either way (no lost first syllable); a tap cancels it.
export const JARVIS_TAP_MS = 300;
export type ReleaseAction = "tap" | "release";
export function jarvisRelease(kind: ComboKind, heldMs: number, tapOpens: boolean): ReleaseAction {
  return kind === "talk" && tapOpens && Number.isFinite(heldMs) && heldMs >= 0 && heldMs < JARVIS_TAP_MS ? "tap" : "release";
}

export type ComboDeps = {
  input: () => any;
  combos: () => Partial<Record<ComboKind, string | null | undefined>>;
  onPress: (kind: ComboKind) => void;
  // heldMs: how long the combo was held, by `now`.
  onRelease: (kind: ComboKind, heldMs: number) => void;
  now?: () => number;
};

export type ComboWatch = { stop: () => void; held: () => ComboKind[] };

// Watches every controller; a combo counts as held while any controller holds it. Each press calls onPress once and
// each release onRelease once. A combo whose buttons another held combo contains does not fire at the same time.
export function installJarvisCombos(deps: ComboDeps): ComboWatch {
  const perController = new Map<number, { lower: number; upper: number }>();
  const held = new Set<ComboKind>();
  const since = new Map<ComboKind, number>();
  const now = () => { try { return Number(deps.now ? deps.now() : Date.now()); } catch { return Date.now(); } };
  const heldFor = (kind: ComboKind) => { const start = since.get(kind); since.delete(kind); return start === undefined ? Infinity : Math.max(0, now() - start); };
  let registration: any;
  const evaluate = () => {
    const configured = deps.combos();
    const current = new Set<ComboKind>();
    const parsed: [ComboKind, JarvisCombo][] = [];
    for (const kind of ["dictate", "talk", "screen"] as ComboKind[]) {
      const combo = parseCombo(configured?.[kind]);
      if (combo) parsed.push([kind, combo]);
    }
    for (const [kind, combo] of parsed) {
      for (const state of perController.values()) if (comboHeld(combo, state.lower, state.upper)) current.add(kind);
    }
    // The bigger of two held combos wins (L4+R4+A held does not also fire L4+R4).
    for (const [kind, combo] of parsed) {
      if (!current.has(kind)) continue;
      for (const [other, otherCombo] of parsed) {
        if (other === kind || !current.has(other)) continue;
        const contains = (otherCombo.lower & combo.lower) >>> 0 === combo.lower && (otherCombo.upper & combo.upper) >>> 0 === combo.upper;
        if (contains && (otherCombo.lower !== combo.lower || otherCombo.upper !== combo.upper)) current.delete(kind);
      }
    }
    for (const kind of current) if (!held.has(kind)) { held.add(kind); since.set(kind, now()); try { deps.onPress(kind); } catch { /* The runtime reports its own failures. */ } }
    for (const kind of [...held]) if (!current.has(kind)) { held.delete(kind); const ms = heldFor(kind); try { deps.onRelease(kind, ms); } catch { /* Same. */ } }
  };
  try {
    const input = deps.input();
    if (typeof input?.RegisterForControllerStateChanges === "function") {
      registration = input.RegisterForControllerStateChanges((changes: any[]) => {
        if (!Array.isArray(changes)) return;
        let changed = false;
        for (const change of changes) {
          const index = Number(change?.unControllerIndex ?? 0);
          const lower = Number(change?.ulButtons ?? 0) >>> 0, upper = Number(change?.ulUpperButtons ?? 0) >>> 0;
          const before = perController.get(index);
          if (!before || before.lower !== lower || before.upper !== upper) { perController.set(index, { lower, upper }); changed = true; }
        }
        if (changed) evaluate();
      });
    }
  } catch { /* No controller state API on this Steam client: the Quick Access buttons still work. */ }
  return {
    held: () => [...held],
    stop: () => {
      try { registration?.unregister?.(); } catch { /* Already gone. */ }
      // Stopping is never a tap: a held combo ends as a release.
      for (const kind of [...held]) { held.delete(kind); since.delete(kind); try { deps.onRelease(kind, Infinity); } catch { /* Same. */ } }
    },
  };
}
