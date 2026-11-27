// Eternal Core: controller button hints, always (Core UI phase 1, docs/eternal/CORE-UI.md, "Controller prompts").
//
// How Steam decides: Big Picture's focus navigation controller (FocusNavController, also reached as
// GamepadNavTree.m_context.m_controller) keeps the last input source in m_navigationSource, a SubscribableValue of
// { eActivationSourceType, nActiveGamepadIndex, nLastActiveGamepadIndex } that ChangeNavigationSource sets on every
// input. The footer hints and other glyph prompts follow it: a controller source shows that controller's glyphs, a
// keyboard or mouse source shows Enter / Esc / mouse prompts. There is no Steam setting or SteamClient call for it.
//
// Core is a console: a stray keyboard or mouse event (Windows, the swipe keyboard, dictation through SendInput, a
// remote) must not flip the hints. So the value's Set is wrapped on that one object: a keyboard or mouse value is
// rewritten to the last controller source seen (same source type number, same controller index, so Steam still picks
// that controller's glyph family). The type number Steam uses for a controller is learned from a value that names a
// controller (nActiveGamepadIndex >= 0), never assumed, and remembered across restarts so the first hint after
// start-up is already right. Nothing else changes: keyboard and mouse input still work.
//
// Fails soft: an unknown shape is left alone (undefined is returned and coreUi.ts tries again later); undo restores
// Steam's own Set. Self-contained (no imports): tests/coreUi.test.mjs runs it against a fake controller.

export type NavigationSource = { eActivationSourceType: number; nActiveGamepadIndex: number; nLastActiveGamepadIndex?: number; [key: string]: unknown };
type Subscribable = { m_currentValue?: unknown; Set?: (value: unknown) => void };
type Memory = { read(): number | undefined; write(type: number): void };

export const PROMPTS_MEMORY_KEY = "core.ui.controller-source-type";

function isSource(value: any): value is NavigationSource {
  return !!value && typeof value === "object" && Number.isInteger(value.eActivationSourceType) && Number.isInteger(value.nActiveGamepadIndex);
}
const isController = (value: NavigationSource) => value.nActiveGamepadIndex >= 0;

// The navigation controllers Steam may have (one, or two on some betas), each once.
export function navigationControllers(host: any): any[] {
  const out: any[] = [];
  for (const read of [() => host?.GamepadNavTree?.m_context?.m_controller, () => host?.FocusNavController]) {
    try { const value = read(); if (value && typeof value === "object" && !out.includes(value)) out.push(value); } catch { /* Not on this client. */ }
  }
  return out;
}

// The rewritten value for a keyboard or mouse source, or undefined to pass it on unchanged.
export function pinnedSource(value: unknown, controllerType: number | undefined, lastIndex: number | undefined): NavigationSource | undefined {
  if (!isSource(value) || isController(value) || controllerType === undefined) return undefined;
  const index = Number.isInteger(value.nLastActiveGamepadIndex) && (value.nLastActiveGamepadIndex as number) >= 0
    ? value.nLastActiveGamepadIndex as number : lastIndex ?? 0;
  return { ...value, eActivationSourceType: controllerType, nActiveGamepadIndex: index, nLastActiveGamepadIndex: index };
}

export function pinControllerPrompts(host: any, memory?: Memory, log: (message: string) => void = () => {}): (() => void) | undefined {
  const undo: Array<() => void> = [];
  for (const controller of navigationControllers(host)) {
    let source: Subscribable | undefined;
    try { source = controller.m_navigationSource; } catch { source = undefined; }
    if (!source || typeof source.Set !== "function" || !isSource(source.m_currentValue)) continue;
    const original = source.Set;
    const own = Object.getOwnPropertyDescriptor(source, "Set");
    let type: number | undefined, lastIndex: number | undefined;
    try { const saved = memory?.read(); if (Number.isInteger(saved)) type = saved; } catch { /* No memory. */ }
    const learn = (value: unknown) => {
      if (!isSource(value) || !isController(value)) return;
      lastIndex = value.nActiveGamepadIndex;
      if (type !== value.eActivationSourceType) { type = value.eActivationSourceType; try { memory?.write(type); } catch { /* Kept for this session. */ } }
    };
    learn(source.m_currentValue);
    const target = source;
    let active = true;
    const wrapper = function (this: unknown, value: unknown) {
      if (this !== target || !active) return Reflect.apply(original, this, [value]);
      try {
        learn(value);
        const pinned = pinnedSource(value, type, lastIndex);
        if (pinned) return Reflect.apply(original, this, [pinned]);
      } catch { /* Steam's own value goes through. */ }
      return Reflect.apply(original, this, [value]);
    };
    try { Object.defineProperty(source, "Set", { configurable: true, writable: true, enumerable: false, value: wrapper }); }
    catch { continue; }
    // A keyboard or mouse source already in place is put back to the controller now.
    try { const pinned = pinnedSource(target.m_currentValue, type, lastIndex); if (pinned) Reflect.apply(original, target, [pinned]); } catch { /* Next input. */ }
    undo.push(() => {
      active = false;
      try {
        if (Object.getOwnPropertyDescriptor(target, "Set")?.value !== wrapper) return;
        if (own) Object.defineProperty(target, "Set", own); else delete (target as any).Set;
      } catch { /* A wrapper left in place passes everything through once its owner is gone. */ }
    });
  }
  if (!undo.length) return undefined;
  log(`[Core UI] Button hints pinned to the controller (${undo.length} navigation source${undo.length > 1 ? "s" : ""})`);
  return () => { for (const step of undo.splice(0)) step(); };
}

export function promptsMemory(storage: { getItem(key: string): string | null; setItem(key: string, value: string): void } | undefined): Memory {
  return {
    read: () => { const value = storage?.getItem(PROMPTS_MEMORY_KEY); return value === null || value === undefined ? undefined : Number.parseInt(value, 10); },
    write: type => storage?.setItem(PROMPTS_MEMORY_KEY, String(type)),
  };
}
