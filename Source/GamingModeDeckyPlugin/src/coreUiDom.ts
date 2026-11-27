// Eternal Core: Core UI's look at the live page (docs/eternal/CORE-UI.md). What a stylesheet cannot know from class
// maps alone, checked once a second while Core UI is on:
//
// - clearOpaqueLayers: large opaque layers inside the Steam menu or Quick Access (a plugin's panel wrapper,
//   Decky's, CSS Loader's grey area) would hide the stars; they are tagged data-core-ui-clear and go clear.
// - chromeState: whether the top bar / button-hint bar sits over game art (stays clear) or over a plain page
//   (pure black), by looking at what is behind it.
// - keepBelowLogo: the Steam menu's first row starts below the ring mark's band, whatever Steam's row container does.
//
// Every write is an attribute or an inline style recorded in InlineEdits, so turning Core UI off restores
// Steam's own values exactly. Self-contained (no imports): tests drive it with fake DOM structures.

export type View = {
  innerWidth: number; innerHeight: number;
  getComputedStyle?: (element: any) => any;
};

// Inline style writes that can be undone: Steam's own value and priority are kept per element and property.
export class InlineEdits {
  private saved = new Map<any, Map<string, { value: string; priority: string }>>();
  set(element: any, property: string, value: string) {
    try {
      const style = element?.style;
      if (!style) return;
      let props = this.saved.get(element);
      if (!props) { props = new Map(); this.saved.set(element, props); }
      if (!props.has(property)) props.set(property, { value: String(style.getPropertyValue?.(property) ?? ""), priority: String(style.getPropertyPriority?.(property) ?? "") });
      style.setProperty?.(property, value, "important");
    } catch { /* Left as Steam had it. */ }
  }
  has(element: any): boolean { return this.saved.has(element); }
  restore(element: any) {
    const props = this.saved.get(element);
    if (!props) return;
    this.saved.delete(element);
    for (const [property, old] of props) {
      try {
        if (old.value) element.style.setProperty(property, old.value, old.priority);
        else element.style.removeProperty?.(property);
      } catch { /* The element went with its window. */ }
    }
  }
  restoreAll() { for (const element of [...this.saved.keys()]) this.restore(element); }
  elements() { return [...this.saved.keys()]; }
}

export type Rgba = { r: number; g: number; b: number; a: number };
// "rgb(1, 2, 3)", "rgba(1,2,3,0.5)", "#RRGGBB", "transparent". Unknown: undefined.
export function parseColor(value: unknown): Rgba | undefined {
  const text = String(value ?? "").trim().toLowerCase();
  if (!text) return undefined;
  if (text === "transparent") return { r: 0, g: 0, b: 0, a: 0 };
  const hex = /^#([0-9a-f]{6})$/.exec(text);
  if (hex) { const n = Number.parseInt(hex[1], 16); return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, a: 1 }; }
  const fn = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)$/.exec(text);
  if (!fn) return undefined;
  let a = 1;
  if (fn[4] !== undefined) a = fn[4].endsWith("%") ? Number.parseFloat(fn[4]) / 100 : Number.parseFloat(fn[4]);
  return { r: Number(fn[1]), g: Number(fn[2]), b: Number(fn[3]), a: Number.isFinite(a) ? a : 1 };
}

function style(view: View, element: any): any {
  try { return view.getComputedStyle?.(element) ?? undefined; } catch { return undefined; }
}
function rectOf(element: any) {
  try { const r = element.getBoundingClientRect(); return { left: r.left, top: r.top, width: r.width, height: r.height, right: r.left + r.width, bottom: r.top + r.height }; }
  catch { return undefined; }
}
const px = (value: unknown) => { const n = Number.parseFloat(String(value ?? "")); return Number.isFinite(n) ? n : 0; };

const INTERACTIVE_TAGS = new Set(["BUTTON", "INPUT", "SELECT", "TEXTAREA", "CANVAS", "IMG", "VIDEO", "SVG", "PICTURE", "IFRAME"]);
const INTERACTIVE_ROLES = new Set(["button", "slider", "switch", "checkbox", "option", "tab", "radio", "textbox", "menuitem"]);

export const CLEAR_ATTRIBUTE = "data-core-ui-clear";

// Tags every descendant of `host` that covers at least `share` of the host's area and paints an opaque or
// translucent colour (no picture) so the stylesheet makes it clear. Buttons, inputs, sliders and pictures are never
// touched. Bounded: at most `limit` elements are looked at, `depth` levels down. Answers how many were tagged.
export function clearOpaqueLayers(host: any, view: View, options: { share?: number; limit?: number; depth?: number } = {}): number {
  const hostRect = rectOf(host);
  if (!hostRect || hostRect.width <= 0 || hostRect.height <= 0) return 0;
  const share = options.share ?? 0.3, limit = options.limit ?? 600, maxDepth = options.depth ?? 10;
  const area = hostRect.width * hostRect.height;
  let looked = 0, tagged = 0;
  const queue: Array<[any, number]> = [];
  try { for (const child of Array.from(host.children ?? []) as any[]) queue.push([child, 1]); } catch { return 0; }
  while (queue.length && looked < limit) {
    const [element, depth] = queue.shift()!;
    looked++;
    const tag = String(element?.tagName ?? "").toUpperCase();
    if (INTERACTIVE_TAGS.has(tag)) continue;
    let role = "", classes = "";
    try { role = String(element.getAttribute?.("role") ?? "").toLowerCase(); classes = String(element.className?.baseVal ?? element.className ?? ""); } catch { /* Plain element. */ }
    if (INTERACTIVE_ROLES.has(role) || /\bDialogButton\b/.test(classes)) continue;
    let already = false;
    try { already = element.hasAttribute?.(CLEAR_ATTRIBUTE) === true || element.getAttribute?.(CLEAR_ATTRIBUTE) != null; } catch { already = false; }
    const rect = rectOf(element);
    if (rect && rect.width * rect.height >= area * share && !already) {
      const computed = style(view, element);
      const colour = parseColor(computed?.backgroundColor ?? computed?.["background-color"]);
      const image = String(computed?.backgroundImage ?? computed?.["background-image"] ?? "none");
      const picture = /url\(/i.test(image);
      if (!picture && ((colour && colour.a > 0.02) || /gradient/i.test(image))) {
        try { element.setAttribute(CLEAR_ATTRIBUTE, "1"); tagged++; } catch { /* Left as it is. */ }
      }
    }
    if (depth < maxDepth) { try { for (const child of Array.from(element.children ?? []) as any[]) queue.push([child, depth + 1]); } catch { /* No children. */ } }
  }
  return tagged;
}

export type ChromeState = "black" | "clear";
// What is behind the top bar or the button-hint bar: game art (a picture, a video, an image background) keeps it
// clear; a plain page makes it pure black. Three points along it are checked; any art among them wins.
export function chromeState(element: any, doc: any, view: View, starsAttribute = "data-core-ui-stars"): ChromeState | undefined {
  const rect = rectOf(element);
  if (!rect || rect.width <= 0 || rect.height <= 0 || typeof doc?.elementsFromPoint !== "function") return undefined;
  const y = Math.min(view.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
  for (const fraction of [0.25, 0.5, 0.75]) {
    const x = Math.min(view.innerWidth - 1, Math.max(0, rect.left + rect.width * fraction));
    let stack: any[] = [];
    try { stack = Array.from(doc.elementsFromPoint(x, y) ?? []); } catch { stack = []; }
    for (const below of stack) {
      if (below === element) continue;
      try { if (element.contains?.(below)) continue; } catch { /* Not ours. */ }
      const tag = String(below?.tagName ?? "").toUpperCase();
      if (tag === "CANVAS" && below.getAttribute?.(starsAttribute) != null) break;
      if (tag === "IMG" || tag === "VIDEO" || tag === "PICTURE") return "clear";
      const computed = style(view, below);
      if (/url\(/i.test(String(computed?.backgroundImage ?? computed?.["background-image"] ?? ""))) return "clear";
      const colour = parseColor(computed?.backgroundColor ?? computed?.["background-color"]);
      if (colour && colour.a >= 0.9) break;
    }
  }
  // Nothing but plain colour (or nothing at all: the page's own black) behind it.
  return "black";
}

// The Steam menu's first row must start below the ring mark's band. If Steam's row container ignores the band (an
// absolutely placed list, say), the container is pushed down by what is missing. Answers the element moved, if any.
export function keepBelowLogo(menu: any, itemSelector: string | undefined, band: number, view: View, edits: InlineEdits): any {
  if (!itemSelector) return undefined;
  const menuRect = rectOf(menu);
  if (!menuRect || menuRect.height <= 0) return undefined;
  let first: any;
  try { first = menu.querySelector(itemSelector); } catch { first = undefined; }
  const itemRect = first ? rectOf(first) : undefined;
  if (!first || !itemRect || itemRect.height <= 0) return undefined;
  const padding = px(style(view, menu)?.paddingTop);
  const wanted = menuRect.top + padding + band;
  const missing = Math.ceil(wanted - itemRect.top);
  if (missing <= 0) return undefined;
  // The menu's own child that holds the first row.
  let container = first;
  while (container && container.parentElement && container.parentElement !== menu) container = container.parentElement;
  if (!container || container.parentElement !== menu) return undefined;
  const computed = style(view, container);
  const position = String(computed?.position ?? "static");
  if (position === "absolute" || position === "fixed") edits.set(container, "top", `${px(computed?.top) + missing}px`);
  else edits.set(container, "margin-top", `${px(computed?.marginTop) + missing}px`);
  return container;
}
