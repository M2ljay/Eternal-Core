// Eternal Core: the Steam menu and Quick Access from the top edge of the screen to the bottom one (Core UI,
// docs/eternal/CORE-UI.md, "Full height").
//
// Steam places both panels between the top bar and the button hints (a top/bottom offset, a max-height, a padded
// wrapper; it differs between clients). Instead of guessing which, the panel itself is measured and stretched:
//
//   margin-top    -= gap above     padding-top    += gap above
//   margin-bottom -= gap below     padding-bottom += gap below
//   height         = its height + both gaps (border-box), max-height none
//
// so its black box (and stars) reaches both edges while its content, focus and scroll position stay exactly where
// Steam put them: the clock and icons above and the hints below stay readable on top. Afterwards the panel is
// measured again; anything but edge-to-edge (an ancestor that scrolls, a flex parent that shrinks it back) undoes
// every change and the panel is left as Steam had it (the Starfield canvas still reaches the edges, the phase 1
// fallback). An ancestor that merely clips it (overflow hidden, no scrolling) is opened vertically.
//
// All writes go through InlineEdits (coreUiDom.ts), so Core UI off restores Steam's values exactly.
// Self-contained apart from coreUiDom.ts: tests drive it with fake DOM structures modelled on Steam's.

import { InlineEdits, type View } from "./coreUiDom";

export type FullHeightResult = "full" | "stretched" | "hidden" | "failed" | "gave-up";

const px = (value: unknown) => { const n = Number.parseFloat(String(value ?? "")); return Number.isFinite(n) ? n : 0; };
const MAX_TRIES = 3;
const TOLERANCE = 2;

type State = { tries: number; height: number; result?: FullHeightResult };

export function createFullHeight(edits: InlineEdits = new InlineEdits()) {
  const states = new Map<any, State>();
  const opened = new Map<any, any[]>();

  function rect(element: any) {
    try { const r = element.getBoundingClientRect(); return { top: r.top, height: r.height, width: r.width, bottom: r.top + r.height }; } catch { return undefined; }
  }
  function computed(view: View, element: any): any {
    try { return view.getComputedStyle?.(element) ?? {}; } catch { return {}; }
  }
  function undo(element: any) {
    edits.restore(element);
    for (const ancestor of opened.get(element) ?? []) edits.restore(ancestor);
    opened.delete(element);
  }

  // Opens ancestors that clip the stretched panel without scrolling. Answers false when an ancestor scrolls (moving
  // the panel inside a scroll container would move what the user sees).
  function openClippers(element: any, view: View, height: number): boolean {
    const changed: any[] = [];
    let node = element.parentElement;
    for (let depth = 0; node && depth < 12; depth++, node = node.parentElement) {
      const style = computed(view, node);
      const overflowY = String(style.overflowY ?? style.overflow ?? "visible");
      if (overflowY === "visible") continue;
      const box = rect(node);
      if (box && box.top <= TOLERANCE && box.bottom >= height - TOLERANCE) break;
      const scrolls = (overflowY === "auto" || overflowY === "scroll") && Number(node.scrollHeight) > Number(node.clientHeight) + 1;
      if (scrolls) { for (const item of changed) edits.restore(item); return false; }
      edits.set(node, "overflow-y", "visible");
      if (String(style.overflowX ?? "visible") !== "visible") edits.set(node, "overflow-x", "clip");
      changed.push(node);
    }
    opened.set(element, changed);
    return true;
  }

  function apply(element: any, view: View): FullHeightResult {
    const height = Number(view.innerHeight) || 0;
    const box = rect(element);
    if (!box || height <= 0 || box.height <= 0 || box.width <= 0) return "hidden";
    let state = states.get(element);
    if (state && state.height !== height) { undo(element); state = undefined; states.delete(element); }
    const limit = Math.round(height / 4);
    const above = Math.round(box.top), below = Math.round(height - box.bottom);
    if (Math.abs(above) <= TOLERANCE && Math.abs(below) <= TOLERANCE) return state?.result === "stretched" ? "stretched" : "full";
    if (state && state.tries >= MAX_TRIES) return "gave-up";
    // Already stretched once and Steam moved it since: start again from Steam's own values.
    if (edits.has(element)) { undo(element); return apply(element, view); }
    state ??= { tries: 0, height };
    state.tries++;
    states.set(element, state);
    const top = Math.max(0, Math.min(limit, above)), bottom = Math.max(0, Math.min(limit, below));
    if (top === 0 && bottom === 0) { state.result = "failed"; return "failed"; }
    const style = computed(view, element);
    edits.set(element, "box-sizing", "border-box");
    edits.set(element, "height", `${Math.round(box.height + top + bottom)}px`);
    edits.set(element, "min-height", "0px");
    edits.set(element, "max-height", "none");
    edits.set(element, "margin-top", `${px(style.marginTop) - top}px`);
    edits.set(element, "margin-bottom", `${px(style.marginBottom) - bottom}px`);
    edits.set(element, "padding-top", `${px(style.paddingTop) + top}px`);
    edits.set(element, "padding-bottom", `${px(style.paddingBottom) + bottom}px`);
    if (!openClippers(element, view, height)) { undo(element); state.result = "failed"; return "failed"; }
    const after = rect(element);
    const ok = !!after && Math.abs(after.top - (box.top - top)) <= TOLERANCE && Math.abs(after.bottom - (box.bottom + bottom)) <= TOLERANCE;
    if (!ok) { undo(element); state.result = "failed"; return "failed"; }
    state.result = "stretched";
    return "stretched";
  }

  return {
    apply,
    release(element: any) { undo(element); states.delete(element); },
    releaseAll() { for (const element of [...states.keys()]) undo(element); states.clear(); edits.restoreAll(); },
    state: (element: any) => states.get(element),
  };
}
