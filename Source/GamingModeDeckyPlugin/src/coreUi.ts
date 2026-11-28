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
  CORE_UI_LIVE_ATTRIBUTE, buildCoreUiCss, findCoreUiMapsDetailed, CORE_UI_ATTRIBUTE, CORE_UI_HOST_ATTRIBUTE, CORE_UI_ORB_ID, CORE_UI_STARS_ATTRIBUTE, CORE_UI_STYLE_ID,
  CORE_UI_CHROME_ATTRIBUTE, CORE_UI_CLEAR_ATTRIBUTE, CORE_MENU_LOGO_BAND, CORE_UI_HEADER_ATTRIBUTE, headerHidden, CORE_QAM_VISIBLE_ATTRIBUTE, CORE_QAM_VISIBLE_VAR,
  type CoreUiHost, type CoreUiSheet,
} from "./coreUiStyles";
import { InlineEdits, chromeState, clearOpaqueLayers, keepBelowLogo, type View } from "./coreUiDom";
import { createFullHeight } from "./coreFullHeight";
import { sharedStarfield, STARFIELD_FPS, type Starfield } from "./coreStarfield";
import { coreUiSetting, type CoreUiSetting } from "./coreUiSetting";
import { pinControllerPrompts, promptsMemory } from "./coreInputPrompts";
import { browserViewDocuments, invalidateOnboardingBrowserViewOwners } from "./onboardingAnchors";
import { installQamRail, measureQamVisible } from "./coreQamRail";
import { backToScreenOne } from "./coreHomePages";
import { relabelContextMenus } from "./coreLabels";

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
// The game view's screen two starts this far below the top edge: the top bar's height, so the tab row is not under it.
export const GAME_VIEW_TOP_INSET = 40;

// The nearest ancestor that scrolls vertically (Steam's page scroller), else undefined.
export function scrollParent(element: Element, doc: Document): any {
  const view: any = doc.defaultView;
  for (let node: any = element.parentElement; node; node = node.parentElement) {
    try {
      const overflow = String(view?.getComputedStyle?.(node)?.overflowY ?? "");
      if ((overflow === "auto" || overflow === "scroll" || overflow === "overlay") && node.scrollHeight > node.clientHeight) return node;
    } catch { /* Next ancestor. */ }
  }
  return undefined;
}

function smoothScroll(scroller: any, top: number) {
  const target = Math.max(0, Math.round(top));
  try { if (typeof scroller.scrollTo === "function") { scroller.scrollTo({ top: target, behavior: "smooth" }); return; } } catch { /* Plain set below. */ }
  scroller.scrollTop = target;
}
// A panel candidate wider than this share of the screen is a full-screen wrapper, never the panel (3.3.9: 0.7, so
// Quick Access's own 855 px window on a 1348 px screen counts).
export const PANEL_SHARE = 0.7;

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
  const observers = new Map<Document, { disconnect(): void }>();
  let lastDom: CoreUiSheet["dom"] = {};
  const focusWatch = new Map<Document, (event: any) => void>();
  // 3.3.10: per document, the scroll listener and the page scroller it last saw (the top bar hides past screen one).
  const scrollWatch = new Map<Document, { handler: (event: any) => void; scroller?: Element; hidden: boolean }>();
  // 3.3.10: per document, Quick Access's flipped rail (coreQamRail.ts) and Home's screen jumps.
  const railWatch = new Map<Document, () => void>();
  const homeWatch = new Map<Document, () => void>();
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
      next.candidates = found.candidates;
      next.ambiguous = found.ambiguous;
    } catch { next = undefined; }
    if (next && (!sheet || next.applied.length >= sheet.applied.length)) sheet = next;
    if (sheet && (sheet.skipped.length === 0 || builds >= MAX_BUILDS)) {
      for (const id of sheet.skipped) logOnce(`skip:${id}`, `[Core UI] ${id}: Steam class map not found on this client; that part keeps Steam's look`);
      // 3.3.10: two modules matched one signature (the Steam menu matched the in-game menu's module first).
      for (const [map, seen] of Object.entries(sheet.ambiguous ?? {})) {
        if (seen) logOnce(`ambiguous:${map}`, `[Core UI] class map ${map}: ${seen.matches} modules match; ${seen.merged > 1 ? `${seen.merged} equal copies merged` : seen.switched ? "took the one with" : "kept the first, with"} ${seen.chosen.join(", ") || "no unique key"}`);
      }
      for (const [map, candidate] of Object.entries(sheet.candidates ?? {})) {
        if (candidate) logOnce(`map:${map}`, `[Core UI] class map ${map} not found; closest module (${Math.round(candidate.score * 100)}%) lacks ${candidate.missing.join(", ")}`);
      }
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
      if (style && (style.visibility === "hidden" || style.display === "none" || String(style.opacity ?? "1") === "0")) return false;
      // 3.3.8: a plugin's page counts only while its plugin paints it (CORE-UI.md, "Hosts from other plugins": the
      // plugin paints its own page black). A page left behind after its plugin's stylesheet went is not a surface.
      if (surface.host.source === "plugin" && style && transparent(style.backgroundColor)) return false;
      return true;
    } catch { return false; }
  }

  function transparent(colour: unknown): boolean {
    if (typeof colour !== "string" || !colour) return false;
    const value = colour.replace(/\s+/g, "").toLowerCase();
    return value === "transparent" || /^rgba\(\d+,\d+,\d+,0(\.0*)?\)$/.test(value);
  }

  // 3.3.8 (TV: a black, starry band stayed over every page after a ROM page closed): a canvas is visible only while
  // its surface is on screen (data-core-ui-live; without it the stylesheet hides the canvas), and its host is always its
  // containing block, so a canvas can never outlive or reach outside the element it belongs to.
  function setLive(surface: Surface, live: boolean) {
    try {
      if (live) { if (surface.canvas.getAttribute(CORE_UI_LIVE_ATTRIBUTE) !== "1") surface.canvas.setAttribute(CORE_UI_LIVE_ATTRIBUTE, "1"); }
      else if (surface.canvas.getAttribute(CORE_UI_LIVE_ATTRIBUTE) !== null) surface.canvas.removeAttribute(CORE_UI_LIVE_ATTRIBUTE);
    } catch { /* Gone with its window. */ }
  }

  function containingBlock(surface: Surface) {
    try {
      if (surface.doc.defaultView?.getComputedStyle?.(surface.element).position === "static") {
        if (surface.position === undefined) surface.position = surface.element.style.position;
        surface.element.style.position = "relative";
      }
    } catch { /* Left as it is. */ }
  }

  // Every surface whose element left the page, or whose canvas left its element, is released; every Starfield canvas
  // that belongs to no live surface is removed; a surface off screen has its canvas hidden.
  function sweep(docs?: Document[]) {
    for (const surface of [...surfaces.values()]) {
      if (!surface.element.isConnected || surface.canvas.parentElement !== surface.element || (docs && !docs.includes(surface.doc))) { release(surface); continue; }
      if (!shown(surface)) { setLive(surface, false); surface.still = false; }
    }
    const known = new Set<any>([...surfaces.values()].map(surface => surface.canvas));
    for (const doc of docs ?? [...new Set([...surfaces.values()].map(surface => surface.doc))]) {
      let stray: Element[] = [];
      try { stray = Array.from(doc.querySelectorAll(`[${CORE_UI_STARS_ATTRIBUTE}]`)); } catch { stray = []; }
      for (const canvas of stray) if (String(canvas.tagName).toLowerCase() === "canvas" && !known.has(canvas)) { try { canvas.remove(); } catch { /* Gone already. */ } }
    }
  }

  // 3.3.9 (TV: "pressing down from the play bar must jump fully to screen two, the tab row at the top"): when focus
  // moves from the play bar into the rest of a game page, the page scrolls so screen two starts at the top (its tab row
  // when there is one); back up into the play bar, it scrolls to screen one. The Collector does the same on a ROM page,
  // so a ROM page is left to it.
  function watchGameView(doc: Document, dom: CoreUiSheet["dom"]) {
    if (focusWatch.has(doc) || !dom.playSection || !dom.appContainer) return;
    let inBar = false;
    const handler = (event: any) => {
      try {
        const target = event?.target as Element | null;
        const page = target?.closest?.(dom.appContainer!);
        if (!page || page.querySelector(".romm-core-page")) { inBar = false; return; }
        const bar = page.querySelector(dom.playSection!);
        const nowInBar = !!bar && (bar === target || bar.contains(target));
        if (nowInBar === inBar) return;
        inBar = nowInBar;
        const scroller = scrollParent(page, doc);
        if (!scroller) return;
        if (nowInBar) { smoothScroll(scroller, 0); return; }
        const anchor = (dom.tabRow ? page.querySelector(dom.tabRow) : null) ?? (bar?.nextElementSibling as Element | null) ?? null;
        if (!anchor) return;
        const offset = anchor.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
        smoothScroll(scroller, scroller.scrollTop + offset - GAME_VIEW_TOP_INSET);
      } catch { /* Steam's own scrolling stands. */ }
    };
    try { doc.addEventListener("focusin", handler, true); focusWatch.set(doc, handler); } catch { /* Steam's own scrolling stands. */ }
  }

  // 3.3.10 (TV: the clock, weather and search sat over Home's tiles): the top bar slides out once the page under it has
  // scrolled past its first screen, and comes back near the top. A page scroller is an element that scrolls vertically
  // and is at least 60% of the window tall (a shelf or carousel scrolling sideways never counts). Checked on every
  // scroll (one capture listener per document) and on every sync, so a route change that removes the scroller brings
  // the bar back.
  function pageScroller(element: any, doc: Document): boolean {
    try {
      const height = Number(doc.defaultView?.innerHeight) || 0;
      return !!element && element.nodeType === 1 && element.clientHeight >= height * 0.6 && element.scrollHeight > element.clientHeight + 1;
    } catch { return false; }
  }
  function applyHeader(doc: Document, dom: CoreUiSheet["dom"], hidden: boolean) {
    if (!dom.header) return;
    let found: Element[] = [];
    try { found = Array.from(doc.querySelectorAll(dom.header)).slice(0, 4); } catch { found = []; }
    for (const element of found) {
      try {
        if (hidden) { if (element.getAttribute(CORE_UI_HEADER_ATTRIBUTE) !== "hidden") element.setAttribute(CORE_UI_HEADER_ATTRIBUTE, "hidden"); }
        else if (element.getAttribute(CORE_UI_HEADER_ATTRIBUTE) !== null) element.removeAttribute(CORE_UI_HEADER_ATTRIBUTE);
        chromes.add(element);
      } catch { /* The bar stays as it is. */ }
    }
  }
  function headerHeight(doc: Document, dom: CoreUiSheet["dom"]): number {
    try { const bar = dom.header ? doc.querySelector(dom.header) : null; return bar ? Math.round(bar.getBoundingClientRect().height) || 0 : 0; } catch { return 0; }
  }
  function updateHeader(doc: Document, dom: CoreUiSheet["dom"], scroller?: Element) {
    const watch = scrollWatch.get(doc);
    if (!watch) return;
    if (scroller) watch.scroller = scroller;
    let hidden = false;
    const current: any = watch.scroller;
    if (current && current.isConnected !== false && pageScroller(current, doc)) {
      hidden = headerHidden(Number(current.scrollTop) || 0, Number(doc.defaultView?.innerHeight) || 0, headerHeight(doc, dom), watch.hidden);
    } else watch.scroller = undefined;
    watch.hidden = hidden;
    applyHeader(doc, dom, hidden);
  }
  function watchScroll(doc: Document, dom: CoreUiSheet["dom"]) {
    if (!dom.header) return;
    if (scrollWatch.has(doc)) { updateHeader(doc, dom); return; }
    const handler = (event: any) => {
      try {
        const target = event?.target;
        if (!pageScroller(target, doc)) return;
        updateHeader(doc, sheet?.dom ?? dom, target);
      } catch { /* The bar stays as it is. */ }
    };
    try { doc.addEventListener("scroll", handler, true); scrollWatch.set(doc, { handler, hidden: false }); } catch { /* The bar stays visible. */ }
  }

  // 3.3.10 (the approved board): Home's pages are two screens each, like the game view. Focus moving from screen one
  // (Core's row, data-core-home-one) to anything below it scrolls the page so screen two starts at the top (Home's tab
  // row, or the Consoles page's games); back into screen one, the page scrolls to its top.
  // 3.3.10 (approved addition): on screen two, B, or up from its top row (Home's tab row; the Consoles page's games),
  // goes straight back to screen one: focus returns to the tile it left, and the same scroll brings screen one back.
  // On screen one B stays Steam's.
  function screenTwoOf(target: Element, dom: CoreUiSheet["dom"]): { two: Element | null; topRow: boolean } {
    const consoles = target.closest("[data-core-home-two]");
    if (consoles) return { two: consoles, topRow: !!target.closest(".core-two-row") };
    const tabbed = dom.tabbedPage ? target.closest(dom.tabbedPage) : null;
    if (!tabbed || !tabbed.querySelector("[data-core-home-tab]")) return { two: null, topRow: false };
    return { two: tabbed, topRow: !!(dom.tabRow && target.closest(dom.tabRow)) };
  }
  function watchHomeScreens(doc: Document, dom: CoreUiSheet["dom"]) {
    if (homeWatch.has(doc)) return;
    let inOne = true;
    let lastTile: Element | null = null;
    const back = (event: any) => {
      try {
        const target = ((doc as any).activeElement ?? event?.target) as Element | null;
        const one = doc.querySelector("[data-core-home-one]");
        if (!one || !target || typeof (target as any).closest !== "function" || one.contains(target)) return;
        const where = screenTwoOf(target, sheet?.dom ?? dom);
        if (!backToScreenOne(event?.detail?.button, { inTwo: !!where.two, inTopRow: where.topRow })) return;
        const tile = (lastTile && (lastTile as any).isConnected && one.contains(lastTile) ? lastTile : one.querySelector(".core-tile")) as any;
        if (!tile?.focus) return;
        event.preventDefault?.(); event.stopPropagation?.();
        tile.focus();
      } catch { /* Steam's own navigation stands. */ }
    };
    const handler = (event: any) => {
      try {
        const target = event?.target as Element | null;
        const one = doc.querySelector("[data-core-home-one]");
        if (!one || !target || typeof (target as any).closest !== "function") return;
        const nowInOne = one.contains(target);
        if (nowInOne && target.closest(".core-tile")) lastTile = target.closest(".core-tile");
        if (nowInOne === inOne) return;
        const scroller = scrollParent(one, doc);
        if (!scroller) { inOne = nowInOne; return; }
        if (nowInOne) { inOne = true; smoothScroll(scroller, 0); return; }
        const two = screenTwoOf(target, dom).two;
        if (!two || !scroller.contains(two)) return;
        inOne = false;
        const offset = two.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
        smoothScroll(scroller, scroller.scrollTop + offset);
      } catch { /* Steam's own scrolling stands. */ }
    };
    try {
      doc.addEventListener("focusin", handler, true);
      doc.addEventListener("vgp_onbuttondown", back, true);
      homeWatch.set(doc, () => { doc.removeEventListener("focusin", handler, true); doc.removeEventListener("vgp_onbuttondown", back, true); });
    } catch { /* Steam's own scrolling stands. */ }
  }
  function watchQamRail(doc: Document) {
    if (railWatch.has(doc)) return;
    try {
      railWatch.set(doc, installQamRail(doc, () => {
        const dom = sheet?.dom ?? {};
        return { menu: dom.qamMenu, rail: dom.qamRail, tab: dom.qamTab, selected: dom.qamTabSelected };
      }));
    } catch { railWatch.set(doc, () => {}); }
  }

  // 3.3.11: the Quick Access Menu takes the width of the part of its window that is on screen, so the rail (the
  // reversed row's last item) sits at the visible right edge (coreQamRail.ts, measureQamVisible).
  const qamSized = new Set<HTMLElement>();
  function sizeQam(docs: Document[], dom: CoreUiSheet["dom"]) {
    if (!dom.qamMenu) return;
    const menus: Array<{ doc: Document; element: HTMLElement }> = [];
    for (const doc of docs) {
      try { for (const element of Array.from(doc.querySelectorAll<HTMLElement>(dom.qamMenu)).slice(0, 2)) menus.push({ doc, element }); } catch { /* Next document. */ }
    }
    let placeholderLeft: number | undefined, hostWidth: number | undefined;
    if (dom.qamPlaceholder) {
      for (const doc of docs) {
        if (menus.some(menu => menu.doc === doc)) continue;
        try {
          const width = Number(doc.defaultView?.innerWidth) || 0;
          const holder = doc.querySelector(dom.qamPlaceholder);
          const rect = holder?.getBoundingClientRect();
          if (rect && rect.width > 0 && width > rect.left) { placeholderLeft = rect.left; hostWidth = width; break; }
        } catch { /* Next document. */ }
      }
    }
    for (const { doc, element } of menus) {
      try {
        const view = doc.defaultView as any;
        const full = Number(view?.innerWidth) || 0;
        const visible = measureQamVisible({ placeholderLeft, hostWidth, screenX: Number(view?.screenX), screenWidth: Number(view?.screen?.width), full });
        if (visible && visible < full) {
          if (element.style.getPropertyValue(CORE_QAM_VISIBLE_VAR) !== `${visible}px`) element.style.setProperty(CORE_QAM_VISIBLE_VAR, `${visible}px`);
          if (element.getAttribute(CORE_QAM_VISIBLE_ATTRIBUTE) !== "1") element.setAttribute(CORE_QAM_VISIBLE_ATTRIBUTE, "1");
          qamSized.add(element);
        } else unsizeQam(element);
      } catch { /* Left as Steam lays it out. */ }
    }
  }
  function unsizeQam(element: HTMLElement) {
    try { element.removeAttribute(CORE_QAM_VISIBLE_ATTRIBUTE); element.style.removeProperty(CORE_QAM_VISIBLE_VAR); } catch {}
    qamSized.delete(element);
  }

  // Removals (a route change, a page closing) are swept at once instead of on the next one-second sync.
  function observe(doc: Document) {
    if (observers.has(doc)) return;
    const Observer = (doc.defaultView as any)?.MutationObserver;
    if (typeof Observer !== "function" || !doc.body) return;
    try {
      const observer = new Observer((records: any[]) => {
        // 3.3.11: a context menu that just opened shows Core's names at once, not after the next sync.
        if (lastDom.contextMenu && records.some(record => record?.addedNodes?.length)) relabelContextMenus(doc, lastDom.contextMenu);
        if (!surfaces.size) return;
        if (records.some(record => record?.removedNodes?.length)) sweep();
      });
      observer.observe(doc.body, { childList: true, subtree: true });
      observers.set(doc, observer);
    } catch { /* The one-second sync still sweeps. */ }
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
        // 3.3.9: a host with `below` starts its stars under that child (the game page's play bar over the art).
        let floor = 0;
        if (surface.host.below) {
          const child = surface.element.querySelector(surface.host.below);
          if (child) floor = Math.max(0, child.getBoundingClientRect().bottom);
        }
        const from = Math.max(0, rect.top, floor), to = Math.min(height, rect.bottom);
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
    setLive(surface, true);
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
      if (!shown(surface)) { surface.still = false; setLive(surface, false); continue; }
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
    try { surface.canvas.removeAttribute(CORE_UI_LIVE_ATTRIBUTE); } catch { /* Already gone with its window. */ }
    try {
      surface.element.removeAttribute(CORE_UI_HOST_ATTRIBUTE);
      if (surface.position !== undefined) surface.element.style.position = surface.position;
    } catch { /* Already gone with its window. */ }
    surfaces.delete(surface.element);
  }

  function attach(doc: Document, hosts: CoreUiHost[], screenWidth = 0) {
    if (!hosts.length) return;
    const any = `[${CORE_UI_HOST_ATTRIBUTE}="1"]`;
    // 3.3.9: measured against the screen (the widest Steam window), not the document: Quick Access and the Steam menu
    // are windows of their own (855 and 240 px wide on the TV), and the panel fills its window.
    const width = Math.max(Number(doc.defaultView?.innerWidth) || 0, screenWidth);
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
          try { nested = element.getBoundingClientRect().width > width * PANEL_SHARE; } catch { nested = false; }
        }
        if (nested) {
          const prior = surfaces.get(element);
          if (prior) release(prior);
          if (element.getAttribute(CORE_UI_HOST_ATTRIBUTE) !== "nested") element.setAttribute(CORE_UI_HOST_ATTRIBUTE, "nested");
          continue;
        }
        let surface = surfaces.get(element);
        // 3.3.9: an element holding the host's exclusion (a ROM page The Collector draws) is not this host.
        let excluded = false;
        if (host.exclude) { try { excluded = !!element.querySelector(host.exclude); } catch { excluded = false; } }
        if (excluded) {
          if (surface) release(surface);
          if (element.getAttribute(CORE_UI_HOST_ATTRIBUTE) !== null) element.removeAttribute(CORE_UI_HOST_ATTRIBUTE);
          continue;
        }
        if (surface && !surface.canvas.isConnected) { release(surface); surface = undefined; }
        if (!surface) {
          const canvas = doc.createElement("canvas");
          canvas.setAttribute(CORE_UI_STARS_ATTRIBUTE, host.id);
          canvas.setAttribute("aria-hidden", "true");
          element.insertBefore(canvas, element.firstChild);
          surface = { element, canvas, host, doc, still: false, top: -1, bottom: -1, scroll: -1, pageHeight: -1 };
          surfaces.set(element, surface);
        }
        // The canvas sits inside the surface: a surface left static becomes the containing block, checked on every
        // pass (a plugin's stylesheet that made it relative can go while the element stays).
        containingBlock(surface);
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
    sweep(docs);
    let screenWidth = 0;
    for (const doc of docs) { try { screenWidth = Math.max(screenWidth, Number(doc.defaultView?.innerWidth) || 0); } catch {} }
    for (const [doc, observer] of observers) if (!docs.includes(doc)) { try { observer.disconnect(); } catch {} observers.delete(doc); }
    for (const [doc, handler] of focusWatch) if (!docs.includes(doc)) { try { doc.removeEventListener("focusin", handler, true); } catch {} focusWatch.delete(doc); }
    for (const [doc, watch] of scrollWatch) if (!docs.includes(doc)) { try { doc.removeEventListener("scroll", watch.handler, true); } catch {} scrollWatch.delete(doc); }
    for (const [doc, stop] of railWatch) if (!docs.includes(doc)) { stop(); railWatch.delete(doc); }
    for (const [doc, stop] of homeWatch) if (!docs.includes(doc)) { try { stop(); } catch {} homeWatch.delete(doc); }
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
        attach(doc, current.hosts, screenWidth);
        observe(doc);
        watchGameView(doc, current.dom ?? {});
        const dom = current.dom ?? {};
        lastDom = dom;
        relabelContextMenus(doc, dom.contextMenu);
        tagChrome(doc, dom);
        watchScroll(doc, dom);
        watchHomeScreens(doc, dom);
        watchQamRail(doc);
        reserveLogo(doc, dom);
        try { deps.decorate?.(doc, dom); } catch { /* The Library keeps Steam's look. */ }
        placeOrb(doc, current.clock);
      } catch { /* A Steam popup may close while it is being styled. */ }
    }
    try { sizeQam(docs, current.dom ?? {}); } catch { /* Quick Access keeps Steam's width. */ }
    sweep(docs);
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
    for (const observer of observers.values()) { try { observer.disconnect(); } catch {} }
    observers.clear();
    for (const [doc, handler] of focusWatch) { try { doc.removeEventListener("focusin", handler, true); } catch {} }
    focusWatch.clear();
    for (const [doc, watch] of scrollWatch) { try { doc.removeEventListener("scroll", watch.handler, true); } catch {} }
    scrollWatch.clear();
    for (const stop of railWatch.values()) stop();
    railWatch.clear();
    for (const stop of homeWatch.values()) { try { stop(); } catch {} }
    homeWatch.clear();
    try { fullHeight.releaseAll(); } catch {}
    for (const element of [...qamSized]) unsizeQam(element);
    edits.restoreAll();
    for (const element of chromes) { try { element.removeAttribute(CORE_UI_CHROME_ATTRIBUTE); element.removeAttribute(CORE_UI_HEADER_ATTRIBUTE); } catch {} }
    chromes.clear();
    for (const [doc, style] of styles) {
      try { style.remove(); } catch {}
      try { deps.undecorate?.(doc); } catch {}
      try {
        doc.documentElement.removeAttribute(CORE_UI_ATTRIBUTE);
        for (const attribute of [CORE_UI_HOST_ATTRIBUTE, CORE_UI_CLEAR_ATTRIBUTE, CORE_UI_CHROME_ATTRIBUTE, CORE_UI_HEADER_ATTRIBUTE]) {
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
// Quick Access are popups on some clients). 3.3.9 (TV snapshot: Quick Access and the Steam menu unstyled): on the
// current client they are BrowserViews, separate windows titled QuickAccess_uid* and MainMenu_uid* that no window store
// lists; `popups` answers those for each document found (popupDocuments below), so every popup gets the stylesheet,
// the Starfield and the full-height logic like the main window.
export function steamUiDocuments(host: any, router: any, popups?: (main: Document) => Document[]): Document[] {
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
  if (popups) {
    for (const main of [...documents]) {
      let found: Document[] = [];
      try { found = popups(main) ?? []; } catch { found = []; }
      for (const doc of found.slice(0, 16)) { try { if (doc?.head && doc.documentElement && documents.size < 48) documents.add(doc); } catch {} }
    }
  }
  return [...documents];
}

// BrowserView popups, re-discovered every POPUP_RESCAN_MS: Steam creates them lazily and can recreate them, and the
// owner cache only notices a closed one, never a new one.
export const POPUP_RESCAN_MS = 3000;
export function popupDocuments(now: () => number = () => Date.now(), find: (main: Document) => Document[] = browserViewDocuments,
  invalidate: (main: Document) => void = invalidateOnboardingBrowserViewOwners) {
  const scanned = new WeakMap<Document, number>();
  return (main: Document): Document[] => {
    const at = now(), last = scanned.get(main);
    if (last === undefined || at - last >= POPUP_RESCAN_MS) { try { invalidate(main); } catch {} scanned.set(main, at); }
    try { return find(main); } catch { return []; }
  };
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
    documents: (() => { const popups = popupDocuments(); return () => steamUiDocuments(host, options.ui?.Router, popups); })(),
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
