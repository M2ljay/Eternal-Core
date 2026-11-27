// Eternal Core: Core UI, phases 1 and 2 (docs/eternal/CORE-UI.md). Core's own look for Steam Big Picture, built into
// Core's Decky plugin instead of a CSS Loader theme, so it keeps working when Steam moves to ES modules
// (Decky 4): the global chrome, the Steam menu and Quick Access.
//
// - One stylesheet (coreUiStyles.ts), built from Steam's class maps, injected into every Steam UI document
//   (the main Big Picture window and its popups: the Steam menu and Quick Access can live in either).
// - "Black means starfield": every surface the stylesheet paints black gets the Core Starfield behind its
//   content (coreStarfield.ts). One shared field, one animation loop, at most 30 frames a second, drawn only
//   while a surface is on screen; held still while a game runs or under reduced motion.
// - One setting, "Core UI" (coreUiSetting.ts), on by default. Off removes every style, attribute and canvas.
//
// Nothing here throws into Steam: a lookup that fails leaves that part of Steam as it was.

import {
  buildCoreUiCss, findCoreUiMapsDetailed, CORE_UI_ATTRIBUTE, CORE_UI_HOST_ATTRIBUTE, CORE_UI_ORB_ID, CORE_UI_STARS_ATTRIBUTE, CORE_UI_STYLE_ID,
  CORE_UI_CHROME_ATTRIBUTE, CORE_UI_CLEAR_ATTRIBUTE, CORE_MENU_LOGO_BAND,
  type CoreUiHost, type CoreUiSheet,
} from "./coreUiStyles";
import { InlineEdits, chromeState, clearOpaqueLayers, keepBelowLogo, type View } from "./coreUiDom";
import { createFullHeight } from "./coreFullHeight";
import { sharedStarfield, STARFIELD_FPS, type Starfield } from "./coreStarfield";
import { coreUiSetting, type CoreUiSetting } from "./coreUiSetting";
import { pinControllerPrompts, promptsMemory } from "./coreInputPrompts";

function storage(): Storage | undefined {
  try { return typeof localStorage === "undefined" ? undefined : localStorage; } catch { return undefined; }
}

export type CoreUiDeps = {
  ui: any;
  documents: () => Document[];
  setting: CoreUiSetting;
  // The Steam menu's logo (Core's ring mark by default). `wordmark` is the phase 1 name, still accepted.
  mark?: string;
  wordmark?: string;
  // A game is running: the stars hold still.
  running: () => boolean;
  // `view` is the window of the surface being drawn: its own animation frames run while it is on screen, even when
  // the main Big Picture window is covered (the Steam menu over a game, a popup).
  frame: { request(callback: (time: number) => void, view?: any): any; cancel(handle: any): void };
  every: { start(callback: () => void, ms: number): any; stop(handle: any): void };
  now: () => number;
  log: (message: string) => void;
  // Jarvis's presence orb in the top bar (jarvisPresence.ts): its state, and a way to hear it change.
  orb?: { state: () => string; subscribe?: (listener: () => void) => () => void };
  // Pins Steam's button hints to controller prompts (coreInputPrompts.ts). Answers how to undo it, or nothing
  // when Steam's navigation source is not found yet (tried again on the next sync).
  pinPrompts?: () => (() => void) | undefined;
  // Phase 2: the Library's tab icons, tones and collection tiles (coreLibraryIcons.ts), once a second per document.
  decorate?: (doc: Document, dom: CoreUiSheet["dom"]) => void;
  undecorate?: (doc: Document) => void;
  // 3.3.4: the snapshot's scheduler (coreUiSnapshot.ts), told about Steam's documents once a second.
  probe?: { tick(docs: Document[]): void };
};

// What the Starfield is doing, for the TV checklist (window[Symbol.for("core.ui.diagnostics")]).
export type CoreUiDiagnostics = { surfaces: number; shown: number; state: "off" | "idle" | "moving" | "still-game" | "still-reduced"; frames: number; full: Record<string, string>;
  // 3.3.4: how often Core UI's stylesheet was moved back to the end of <head> (after CSS Loader's or another's).
  restyled: number;
  // 3.3.7: per host id, how many elements carry its Starfield right now (Steam's hosts and other plugins' alike).
  hosts: Record<string, number> };

type Surface = { element: HTMLElement; canvas: HTMLCanvasElement; host: CoreUiHost; doc: Document; position?: string; still: boolean; top: number; bottom: number; scroll: number; pageHeight: number };

const MAX_BUILDS = 20;

export function createCoreUi(deps: CoreUiDeps) {
  let sheet: CoreUiSheet | undefined, builds = 0, field: Starfield | undefined;
  const logged = new Set<string>();
  const styles = new Map<Document, HTMLStyleElement>();
  const surfaces = new Map<HTMLElement, Surface>();
  let loop: any, last: number | undefined, timer: any, disposed = false;
  let unpin: (() => void) | undefined, pinTries = 0;
  const orbs = new Set<HTMLElement>();
  // Every inline style Core UI writes on Steam's elements (full height, the logo band), undone on off.
  const edits = new InlineEdits();
  const fullHeight = createFullHeight(edits);
  const chromes = new Set<Element>();
  const diagnostics: CoreUiDiagnostics = { surfaces: 0, shown: 0, state: "idle", frames: 0, full: {}, restyled: 0, hosts: {} };

  function orbState(): string {
    try { return deps.orb?.state() ?? "hidden"; } catch { return "hidden"; }
  }
  function updateOrbs() {
    const state = orbState();
    for (const orb of [...orbs]) {
      if (!orb.isConnected) { orbs.delete(orb); continue; }
      if (orb.getAttribute("data-state") !== state) orb.setAttribute("data-state", state);
    }
  }
  // The orb sits right after Steam's clock; put back if Steam re-renders the top bar.
  function placeOrb(doc: Document, clockSelector: string | undefined) {
    if (!deps.orb || !clockSelector) return;
    let clock: Element | null = null;
    try { clock = doc.querySelector(clockSelector); } catch { clock = null; }
    let orb = doc.getElementById(CORE_UI_ORB_ID) as HTMLElement | null;
    if (!clock?.parentElement) { if (orb) { orb.remove(); orbs.delete(orb); } return; }
    if (!orb) {
      orb = doc.createElement("span");
      orb.id = CORE_UI_ORB_ID;
      orb.setAttribute("aria-hidden", "true");
    }
    if (orb.parentElement !== clock.parentElement || (clock as any).nextSibling !== orb) clock.parentElement.insertBefore(orb, (clock as any).nextSibling ?? null);
    orbs.add(orb);
  }
  const unsubscribeOrb = deps.orb?.subscribe?.(() => { if (!disposed && deps.setting.get()) updateOrbs(); }) ?? (() => {});

  function logOnce(key: string, message: string) {
    if (logged.has(key)) return;
    logged.add(key);
    try { deps.log(message); } catch { /* Logging is best effort. */ }
  }

  function stylesheet(): CoreUiSheet | undefined {
    // Steam builds some class maps late, and Decky 4 fills its scope lists after start: rebuild until every
    // rule applies or MAX_BUILDS tries have passed, then keep what was found.
    if (sheet && (sheet.skipped.length === 0 || builds >= MAX_BUILDS)) return sheet;
    builds++;
    let next: CoreUiSheet | undefined;
    try {
      const found = findCoreUiMapsDetailed(deps.ui);
      next = buildCoreUiCss(found.maps, { mark: deps.mark ?? deps.wordmark });
      next.routes = found.routes;
    } catch { next = undefined; }
    if (next && (!sheet || next.applied.length >= sheet.applied.length)) sheet = next;
    if (sheet && (sheet.skipped.length === 0 || builds >= MAX_BUILDS)) {
      for (const id of sheet.skipped) logOnce(`skip:${id}`, `[Core UI] ${id}: Steam class map not found on this client; that part keeps Steam's look`);
    }
    return sheet;
  }

  function reduced(doc: Document): boolean {
    try { return doc.defaultView?.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches === true; } catch { return false; }
  }

  function shown(surface: Surface): boolean {
    try {
      const { element, doc } = surface;
      if (!element.isConnected || doc.visibilityState === "hidden") return false;
      const rect = element.getBoundingClientRect();
      const view = doc.defaultView;
      const width = view?.innerWidth ?? Infinity, height = view?.innerHeight ?? Infinity;
      if (!(rect.width > 0 && rect.height > 0) || rect.right <= 0 || rect.bottom <= 0 || rect.left >= width || rect.top >= height) return false;
      if (rect.width * rect.height < surface.host.minArea) return false;
      // A closed Steam menu can stay in place with an ancestor hidden or faded out: Chromium's checkVisibility sees
      // through ancestors (display, visibility, opacity), getComputedStyle only sees the element.
      const check = (element as any).checkVisibility;
      if (typeof check === "function" && check.call(element, { checkOpacity: true, checkVisibilityCSS: true, opacityProperty: true, visibilityProperty: true }) === false) return false;
      const style = view?.getComputedStyle?.(element);
      return !style || (style.visibility !== "hidden" && style.display !== "none" && String(style.opacity ?? "1") !== "0");
    } catch { return false; }
  }

  // Where the canvas goes: at the surface's scroll position, and for the Steam menu and Quick Access from the
  // screen's top edge to its bottom edge (the gaps Steam leaves for the top bar and the button hints, at most a
  // quarter of the screen each). Only the canvas moves: the panel's own box, focus and scrolling are untouched.
  // A plugin's page (viewport) scrolls inside Steam's page: its canvas covers exactly the part of the element on
  // screen, so it never reaches outside the element (the hero art above it stays clear). Returns true when anything
  // changed.
  function place(surface: Surface): boolean {
    let top = 0, bottom = 0, scroll = 0, pageHeight = 0;
    try {
      scroll = Math.max(0, Math.round(surface.element.scrollTop || 0));
      if (surface.host.viewport) {
        const rect = surface.element.getBoundingClientRect(), height = surface.doc.defaultView?.innerHeight ?? 0;
        const from = Math.max(0, rect.top), to = Math.min(height, rect.bottom);
        scroll += Math.max(0, Math.round(from - rect.top));
        pageHeight = Math.max(0, Math.round(to - from));
      } else if (surface.host.page) pageHeight = Math.max(0, Math.round(Math.min(surface.element.clientHeight || 0, surface.doc.defaultView?.innerHeight ?? 0)));
      if (surface.host.fullHeight) {
        const rect = surface.element.getBoundingClientRect(), height = surface.doc.defaultView?.innerHeight ?? 0;
        if (height > 0 && rect.height > 0) {
          const limit = Math.round(height / 4);
          top = Math.max(0, Math.min(limit, Math.round(rect.top)));
          bottom = Math.max(0, Math.min(limit, Math.round(height - rect.bottom)));
        }
      }
    } catch { top = bottom = scroll = 0; }
    if (top === surface.top && bottom === surface.bottom && scroll === surface.scroll && pageHeight === surface.pageHeight) return false;
    surface.top = top; surface.bottom = bottom; surface.scroll = scroll; surface.pageHeight = pageHeight;
    try {
      const style: any = surface.canvas.style;
      style.setProperty?.("--core-ui-extend-top", `${top}px`);
      style.setProperty?.("--core-ui-extend-bottom", `${bottom}px`);
      style.setProperty?.("--core-ui-scroll", `${scroll}px`);
      if (surface.host.page) style.setProperty?.("--core-ui-page-height", `${pageHeight}px`);
    } catch { /* The canvas keeps its last place. */ }
    return true;
  }

  function paint(surface: Surface) {
    place(surface);
    const canvas = surface.canvas;
    const width = Math.max(0, Math.round(surface.element.clientWidth));
    const height = surface.host.page ? surface.pageHeight : Math.max(0, Math.round(surface.element.clientHeight)) + surface.top + surface.bottom;
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
    const context = canvas.getContext("2d");
    if (context && field) field.draw(context as any, width, height);
  }

  function frame(time: number) {
    loop = undefined;
    if (disposed || !deps.setting.get()) return;
    const now = Number.isFinite(time) ? time : deps.now();
    if (last !== undefined && now - last < 1000 / STARFIELD_FPS - 1) { loop = deps.frame.request(frame); return; }
    last = now;
    let running = false;
    try { running = deps.running(); } catch { running = false; }
    let moving = false, advanced = false, visible = 0, view: any, still: CoreUiDiagnostics["state"] | undefined;
    for (const surface of surfaces.values()) {
      if (!shown(surface)) { surface.still = false; continue; }
      visible++;
      const held = running ? "still-game" : reduced(surface.doc) ? "still-reduced" : undefined;
      if (held) {
        // Held still: one frame, then nothing until it moves again.
        still = held;
        if (!surface.still) { paint(surface); surface.still = true; }
        continue;
      }
      if (!advanced) { field?.advanceTo(now); advanced = true; }
      surface.still = false;
      paint(surface);
      moving = true;
      view ??= surface.doc.defaultView;
    }
    diagnostics.shown = visible; diagnostics.surfaces = surfaces.size;
    diagnostics.state = moving ? "moving" : still ?? "idle";
    if (moving) { diagnostics.frames++; loop = deps.frame.request(frame, view); }
    else last = undefined;
  }

  // The window to animate in: the first surface on screen (the Steam menu or Quick Access may be in a popup).
  function activeView(): any {
    for (const surface of surfaces.values()) if (shown(surface)) return surface.doc.defaultView;
    return undefined;
  }

  function wake() {
    if (loop === undefined && !disposed && surfaces.size) loop = deps.frame.request(frame, activeView());
  }

  function release(surface: Surface) {
    try { fullHeight.release(surface.element); } catch { /* Already gone with its window. */ }
    try { surface.canvas.remove(); } catch { /* Already gone with its window. */ }
    try {
      surface.element.removeAttribute(CORE_UI_HOST_ATTRIBUTE);
      if (surface.position !== undefined) surface.element.style.position = surface.position;
    } catch { /* Already gone with its window. */ }
    surfaces.delete(surface.element);
  }

  function attach(doc: Document, hosts: CoreUiHost[]) {
    if (!hosts.length) return;
    const any = `[${CORE_UI_HOST_ATTRIBUTE}="1"]`;
    const width = Number(doc.defaultView?.innerWidth) || 0;
    for (const host of hosts) {
      let found: HTMLElement[] = [];
      try { found = Array.from(doc.querySelectorAll<HTMLElement>(host.selector)).slice(0, 8); } catch { found = []; }
      for (const element of found) {
        // A host inside another host stays clear: the outer surface's stars show through it. Hosts are listed outer
        // first, so an outer candidate is already marked when its inner ones are looked at. A panel candidate wider
        // than 60% of the screen is a full-screen wrapper, never the panel: it stays clear too.
        let nested = false;
        try { nested = !!element.parentElement?.closest(any); } catch { nested = false; }
        if (!nested && host.panel && width > 0) {
          try { nested = element.getBoundingClientRect().width > width * 0.6; } catch { nested = false; }
        }
        if (nested) {
          const prior = surfaces.get(element);
          if (prior) release(prior);
          if (element.getAttribute(CORE_UI_HOST_ATTRIBUTE) !== "nested") element.setAttribute(CORE_UI_HOST_ATTRIBUTE, "nested");
          continue;
        }
        let surface = surfaces.get(element);
        if (surface && !surface.canvas.isConnected) { release(surface); surface = undefined; }
        if (!surface) {
          const canvas = doc.createElement("canvas");
          canvas.setAttribute(CORE_UI_STARS_ATTRIBUTE, host.id);
          canvas.setAttribute("aria-hidden", "true");
          element.insertBefore(canvas, element.firstChild);
          surface = { element, canvas, host, doc, still: false, top: -1, bottom: -1, scroll: -1, pageHeight: -1 };
          // The canvas sits inside the surface: a surface Steam leaves static becomes the containing block.
          try {
            if (doc.defaultView?.getComputedStyle?.(element).position === "static") { surface.position = element.style.position; element.style.position = "relative"; }
          } catch { /* Left as it is. */ }
          surfaces.set(element, surface);
        }
        if (element.getAttribute(CORE_UI_HOST_ATTRIBUTE) !== "1") element.setAttribute(CORE_UI_HOST_ATTRIBUTE, "1");
        const view = doc.defaultView as unknown as View | null;
        if (view && host.fullHeight && shown(surface)) {
          // The panel itself from the top edge to the bottom one (coreFullHeight.ts); the canvas extension below is
          // the fallback when Steam does not let it stretch.
          let result = "hidden";
          try { result = fullHeight.apply(element, view); } catch { result = "failed"; }
          diagnostics.full[host.id] = result;
        }
        if (view && host.panel && shown(surface)) {
          // Large opaque layers inside the panel (Decky's plugin panels, CSS Loader's grey area) go clear.
          try { clearOpaqueLayers(element, view); } catch { /* Left as Steam had it. */ }
        }
        // A held-still surface that moved or scrolled is painted again in its new place.
        if (place(surface)) surface.still = false;
      }
    }
  }

  // The top bar and the button-hint bar: pure black over a plain page, clear over game art.
  function tagChrome(doc: Document, dom: CoreUiSheet["dom"]) {
    const view = doc.defaultView as unknown as View | null;
    if (!view) return;
    for (const selector of [dom.header, dom.footer]) {
      if (!selector) continue;
      let found: Element[] = [];
      try { found = Array.from(doc.querySelectorAll(selector)).slice(0, 4); } catch { found = []; }
      for (const element of found) {
        let state: string | undefined;
        try { state = chromeState(element, doc, view, CORE_UI_STARS_ATTRIBUTE); } catch { state = undefined; }
        if (!state) continue;
        if (element.getAttribute(CORE_UI_CHROME_ATTRIBUTE) !== state) element.setAttribute(CORE_UI_CHROME_ATTRIBUTE, state);
        chromes.add(element);
      }
    }
  }

  // The Steam menu's first row starts below the ring mark's band.
  function reserveLogo(doc: Document, dom: CoreUiSheet["dom"]) {
    const view = doc.defaultView as unknown as View | null;
    if (!view || !dom.menu) return;
    let menus: Element[] = [];
    try { menus = Array.from(doc.querySelectorAll(dom.menu)).slice(0, 2); } catch { menus = []; }
    for (const menu of menus) { try { keepBelowLogo(menu, dom.menuItem, CORE_MENU_LOGO_BAND, view, edits); } catch { /* Left as it is. */ } }
  }

  function sync() {
    if (disposed) return;
    if (!deps.setting.get()) { teardown(); return; }
    const current = stylesheet();
    if (!current || !current.css) return;
    field ??= sharedStarfield();
    let docs: Document[] = [];
    try { docs = deps.documents().filter(doc => !!doc?.head && !!doc.documentElement); } catch { docs = []; }
    for (const [doc, style] of styles) if (!docs.includes(doc)) { try { style.remove(); } catch {} styles.delete(doc); }
    for (const surface of [...surfaces.values()]) if (!docs.includes(surface.doc) || !surface.element.isConnected) release(surface);
    for (const doc of docs) {
      try {
        let style = styles.get(doc);
        if (!style || !style.isConnected) {
          style = (doc.getElementById(CORE_UI_STYLE_ID) as HTMLStyleElement | null) ?? doc.createElement("style");
          style.id = CORE_UI_STYLE_ID;
          doc.head.appendChild(style);
          styles.set(doc, style);
        }
        if (style.textContent !== current.css) style.textContent = current.css;
        // Last in <head>: a stylesheet CSS Loader (or anything else) adds later would otherwise win ties. Moved back
        // only when a stylesheet actually follows it.
        if (laterStylesheet(style)) { doc.head.appendChild(style); diagnostics.restyled++; }
        if (doc.documentElement.getAttribute(CORE_UI_ATTRIBUTE) !== "1") doc.documentElement.setAttribute(CORE_UI_ATTRIBUTE, "1");
        attach(doc, current.hosts);
        const dom = current.dom ?? {};
        tagChrome(doc, dom);
        reserveLogo(doc, dom);
        try { deps.decorate?.(doc, dom); } catch { /* The Library keeps Steam's look. */ }
        placeOrb(doc, current.clock);
      } catch { /* A Steam popup may close while it is being styled. */ }
    }
    const counts: Record<string, number> = {};
    for (const surface of surfaces.values()) counts[surface.host.id] = (counts[surface.host.id] ?? 0) + 1;
    diagnostics.hosts = counts;
    updateOrbs();
    try { deps.probe?.tick(docs); } catch { /* The snapshot is diagnostics only. */ }
    if (!unpin && deps.pinPrompts && pinTries < 120) {
      pinTries++;
      try { unpin = deps.pinPrompts(); } catch { unpin = undefined; }
      if (!unpin && pinTries === 120) logOnce("prompts", "[Core UI] Steam's navigation source was not found; button hints follow Steam's own choice");
    }
    wake();
  }

  function teardown() {
    if (loop !== undefined) { try { deps.frame.cancel(loop); } catch {} loop = undefined; }
    if (unpin) { try { unpin(); } catch {} unpin = undefined; }
    pinTries = 0;
    for (const orb of orbs) { try { orb.remove(); } catch {} }
    orbs.clear();
    last = undefined;
    for (const surface of [...surfaces.values()]) release(surface);
    try { fullHeight.releaseAll(); } catch {}
    edits.restoreAll();
    for (const element of chromes) { try { element.removeAttribute(CORE_UI_CHROME_ATTRIBUTE); } catch {} }
    chromes.clear();
    for (const [doc, style] of styles) {
      try { style.remove(); } catch {}
      try { deps.undecorate?.(doc); } catch {}
      try {
        doc.documentElement.removeAttribute(CORE_UI_ATTRIBUTE);
        for (const attribute of [CORE_UI_HOST_ATTRIBUTE, CORE_UI_CLEAR_ATTRIBUTE, CORE_UI_CHROME_ATTRIBUTE]) {
          for (const element of Array.from(doc.querySelectorAll(`[${attribute}]`))) element.removeAttribute(attribute);
        }
      } catch {}
    }
    styles.clear();
    diagnostics.state = "off";
    diagnostics.hosts = {};
  }

  const unsubscribe = deps.setting.subscribe(sync);
  timer = deps.every.start(sync, 1000);
  sync();
  return {
    sync,
    frame: (time: number) => frame(time),
    surfaces: () => [...surfaces.values()],
    sheet: () => sheet,
    diagnostics: () => diagnostics,
    dispose() {
      if (disposed) return;
      unsubscribe();
      unsubscribeOrb();
      try { deps.every.stop(timer); } catch {}
      teardown();
      disposed = true;
    },
  };
}

// Whether a stylesheet (a <style> or a stylesheet <link>) comes after `style` in its parent.
export function laterStylesheet(style: any): boolean {
  let next = style?.nextElementSibling;
  while (next) {
    const tag = String(next.tagName ?? "").toUpperCase();
    if (tag === "STYLE" || (tag === "LINK" && /\bstylesheet\b/i.test(String(next.getAttribute?.("rel") ?? "")))) return true;
    next = next.nextElementSibling;
  }
  return false;
}

// Steam's UI documents: the Big Picture window, every Steam UI window and their popups (the Steam menu and
// Quick Access are popups on some clients).
export function steamUiDocuments(host: any, router: any): Document[] {
  const documents = new Set<Document>(), windows: any[] = [];
  const values = (value: any): any[] => {
    try { return value?.values ? Array.from(value.values()) : Array.isArray(value) ? value : value && typeof value === "object" ? Object.values(value) : []; }
    catch { return []; }
  };
  const add = (value: any) => {
    try { if (value?.document && !value.closed && !windows.includes(value) && windows.length < 32) { windows.push(value); documents.add(value.document); } } catch {}
  };
  const addWindow = (value: any) => { for (const item of [value, value?.window, value?.m_Window, value?.m_BrowserWindow, value?.BrowserWindow]) add(item); };
  add(host);
  for (const store of [host?.SteamUIStore?.WindowStore, router?.WindowStore]) {
    for (const item of [store?.GamepadUIMainWindowInstance, ...values(store?.SteamUIWindows)]) addWindow(item);
  }
  for (let i = 0; i < windows.length; i++) {
    try { for (const popup of values(windows[i].g_PopupManager?.m_mapPopups)) addWindow(popup); } catch {}
  }
  return [...documents];
}

// One animation frame in `view` (the window being drawn), with a timer backstop: a window Steam has stopped
// compositing never answers requestAnimationFrame, and the stars must not freeze because of it. The time passed on
// is always the main window's clock, so frames from different windows share one timeline.
export function frameIn(host: any, view: any, callback: (time: number) => void, fps = STARFIELD_FPS) {
  const target = view && typeof view.requestAnimationFrame === "function" ? view : host;
  const now = () => (typeof host?.performance?.now === "function" ? host.performance.now() : Date.now());
  let done = false, id: any, timer: any;
  const fire = () => { if (done) return; done = true; try { host.clearTimeout?.(timer); } catch {} try { if (id !== undefined) target.cancelAnimationFrame?.(id); } catch {} callback(now()); };
  try { if (typeof target?.requestAnimationFrame === "function") id = target.requestAnimationFrame(fire); } catch { id = undefined; }
  timer = host.setTimeout(fire, id === undefined ? 1000 / fps : 250);
  return { cancel() { done = true; try { host.clearTimeout?.(timer); } catch {} try { if (id !== undefined) target.cancelAnimationFrame?.(id); } catch {} } };
}

export function installCoreUi(options: {
  ui: any; mark?: string; wordmark?: string; setting?: CoreUiSetting; orb?: CoreUiDeps["orb"];
  decorate?: CoreUiDeps["decorate"]; undecorate?: CoreUiDeps["undecorate"]; probe?: CoreUiDeps["probe"];
  onRuntime?: (runtime: ReturnType<typeof createCoreUi>) => void;
}): () => void {
  const host: any = window;
  const runtime = createCoreUi({
    ui: options.ui,
    documents: () => steamUiDocuments(host, options.ui?.Router),
    setting: options.setting ?? coreUiSetting,
    mark: options.mark,
    wordmark: options.wordmark,
    decorate: options.decorate,
    undecorate: options.undecorate,
    probe: options.probe,
    running: () => {
      const id = Number(host.SteamUIStore?.MainRunningAppID ?? options.ui?.Router?.MainRunningAppID ?? 0);
      return Number.isFinite(id) && id > 0;
    },
    frame: { request: (callback, view) => frameIn(host, view, callback), cancel: handle => handle?.cancel?.() },
    every: { start: (callback, ms) => host.setInterval(callback, ms), stop: handle => host.clearInterval(handle) },
    now: () => (typeof performance !== "undefined" ? performance.now() : Date.now()),
    log: message => console.info(message),
    orb: options.orb,
    pinPrompts: () => pinControllerPrompts(host, promptsMemory(storage()), message => console.info(message)),
  });
  try { host[Symbol.for("core.ui.diagnostics")] = runtime.diagnostics; } catch { /* Diagnostics are optional. */ }
  try { options.onRuntime?.(runtime); } catch { /* Diagnostics are optional. */ }
  return () => runtime.dispose();
}
