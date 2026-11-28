// Eternal Core: the Core UI snapshot (docs/eternal/CORE-UI.md, "The snapshot", 3.3.4). Ground truth for Core UI's
// selectors: every selector up to 3.3.3 was inferred from Steam's readable class-map names, never observed, and the TV
// showed most of them missing. The snapshot records how each surface is really built, so the next fixes are exact.
//
// - When: a few seconds after Core UI applies, then the first time each surface opens (the Steam menu, Quick Access and
//   each of its tabs, a context menu, the Library's tab bar, a collection tile and the grid, Home, the footer and the
//   top bar), and on demand (Quick Access > Core > Look > Save Core UI snapshot).
// - What: per surface a pruned tree (at most SNAPSHOT_DEPTH levels and SNAPSHOT_NODES nodes): tag, classes, data-*
//   attribute names, the box, a few computed styles, which Core UI rule matched, and a flag on opaque layers that cover
//   more than 30% of the surface. Plus which class maps resolved and how, the selector table, the documents (main
//   window or popup), the screen size and DPR, icon sources and the CSS Loader state.
// - Never: text beyond short labels (at most 30 characters, and none at all on the grid and Home, where they would be
//   game names), input values, URLs with queries, or anything the user typed.
// - Where: the plugin backend writes it (main.py, save_core_ui_snapshot) to
//   %APPDATA%\GamingMode\diagnostics\core-ui-snapshot.json, overwritten each time, at most SNAPSHOT_MAX_BYTES.
//
// Pure apart from what it is handed (documents, a view, the stylesheet's facts): tests drive it with fake DOMs.

import { targetSelectors, resolvedTargetTable, type CoreUiMaps } from "./coreUiStyles";

export const SNAPSHOT_DEPTH = 6;
export const SNAPSHOT_NODES = 400;
export const SNAPSHOT_MAX_BYTES = 2 * 1024 * 1024;
export const SNAPSHOT_LABEL = 30;
export const SNAPSHOT_COVER_SHARE = 0.3;
// How long after Core UI applies the first capture runs, and how long a newly opened surface settles before capture.
export const SNAPSHOT_FIRST_MS = 5000;
export const SNAPSHOT_SETTLE_MS = 1500;
export const SNAPSHOT_MIN_GAP_MS = 3000;

// The surfaces, each found through the selector table (coreUiStyles.ts, CORE_UI_TARGETS). `labels`: whether short
// labels are recorded (menus and tabs: yes; the grid and Home: never). `perTab`: one capture per Quick Access tab.
export type SnapshotSurface = { id: string; targets: string[]; labels: boolean; perTab?: boolean };
export const SNAPSHOT_SURFACES: SnapshotSurface[] = [
  { id: "steam-menu", targets: ["menu"], labels: true },
  { id: "qam", targets: ["qamPanel", "qamMenu"], labels: true },
  { id: "qam-rail", targets: ["qamRail"], labels: true },
  { id: "qam-title", targets: ["qamTitle"], labels: true },
  { id: "qam-content", targets: ["qamContent"], labels: true, perTab: true },
  { id: "context-menu", targets: ["context"], labels: true },
  { id: "library-tabs", targets: ["libraryTabRow"], labels: true },
  { id: "library-collection-tile", targets: ["collectionTile"], labels: true },
  { id: "library-grid", targets: ["libraryGrid"], labels: false },
  { id: "home", targets: ["home"], labels: false },
  { id: "footer", targets: ["footer"], labels: true },
  { id: "top-bar", targets: ["header"], labels: false },
  // 3.3.7, a host from another plugin: The Collector's ROM game page (no labels: game names).
  { id: "collector-rom-page", targets: ["collectorRomPage"], labels: false },
];

export type SnapshotView = { innerWidth?: number; innerHeight?: number; devicePixelRatio?: number; getComputedStyle?: (element: any) => any; screen?: any };

// One node of a pruned tree. Short keys keep 400 nodes per surface well inside the size cap; a style that is the
// browser default is left out.
export type SnapshotNode = {
  t: string; c?: string[]; d?: string[]; r: [number, number, number, number];
  s?: Record<string, string>; m?: string[]; cover?: 1; l?: string; k?: SnapshotNode[]; more?: number;
};

const STYLE_KEYS: Array<[string, string, string[]]> = [
  // [short key, computed property, defaults left out]
  ["bg", "backgroundColor", ["rgba(0, 0, 0, 0)", "transparent"]],
  ["img", "backgroundImage", ["none"]],
  ["op", "opacity", ["1"]],
  ["pos", "position", ["static"]],
  ["top", "top", ["auto"]],
  ["bot", "bottom", ["auto"]],
  ["mh", "maxHeight", ["none"]],
  ["ov", "overflow", ["visible"]],
  ["z", "zIndex", ["auto"]],
  ["br", "borderRadius", ["0px"]],
];

// "url(...)" -> "url", "linear-gradient(...)" -> "linear-gradient": the type only, never the address.
export function imageType(value: unknown): string {
  const text = String(value ?? "").trim();
  if (!text || text === "none") return "none";
  const kinds = new Set<string>();
  for (const match of text.matchAll(/(url|(?:repeating-)?(?:linear|radial|conic)-gradient|image-set|-webkit-image-set|cross-fade)\(/gi)) kinds.add(match[1].toLowerCase());
  return kinds.size ? [...kinds].join("+") : "other";
}

const clip = (value: unknown, max = SNAPSHOT_LABEL) => {
  const text = String(value ?? "").replace(/\s+/g, " ").trim();
  return text.length > max ? text.slice(0, max) : text;
};

// A short label: aria-label, title, or the element's own text (its direct text nodes only), at most 30 characters.
export function shortLabel(element: any): string | undefined {
  try {
    const aria = element.getAttribute?.("aria-label") || element.getAttribute?.("title");
    if (aria) return clip(aria) || undefined;
    let own = "";
    for (const node of Array.from(element.childNodes ?? []) as any[]) if (node?.nodeType === 3) own += node.textContent ?? "";
    return clip(own) || undefined;
  } catch { return undefined; }
}

function parseAlpha(colour: string): number {
  const text = colour.trim().toLowerCase();
  if (!text || text === "transparent") return 0;
  const fn = /^rgba?\(([^)]*)\)$/.exec(text);
  if (fn) { const parts = fn[1].split(/[\s,/]+/).filter(Boolean); if (parts.length >= 4) { const a = parts[3]; return a.endsWith("%") ? Number.parseFloat(a) / 100 : Number.parseFloat(a); } return 1; }
  return text.startsWith("#") ? 1 : 0;
}

function rectOf(element: any): [number, number, number, number] {
  try { const r = element.getBoundingClientRect(); return [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)]; }
  catch { return [0, 0, 0, 0]; }
}

function classesOf(element: any): string[] {
  try {
    const list = element.classList ? Array.from(element.classList as ArrayLike<string>) : String(element.className?.baseVal ?? element.className ?? "").split(/\s+/);
    return list.filter(Boolean).slice(0, 12).map(name => clip(name, 60));
  } catch { return []; }
}

function dataNames(element: any): string[] {
  try {
    const names: string[] = [];
    for (const attribute of Array.from(element.attributes ?? []) as any[]) if (/^data-/.test(attribute?.name ?? "")) names.push(String(attribute.name).slice(0, 40));
    if (!element.attributes && element.dataset) for (const key of Object.keys(element.dataset)) names.push("data-" + key.replace(/[A-Z]/g, c => "-" + c.toLowerCase()));
    return names.slice(0, 12);
  } catch { return []; }
}

function stylesOf(view: SnapshotView, element: any): { s?: Record<string, string>; opaque: boolean } {
  let computed: any;
  try { computed = view.getComputedStyle?.(element); } catch { computed = undefined; }
  if (!computed) return { opaque: false };
  const out: Record<string, string> = {};
  for (const [key, prop, defaults] of STYLE_KEYS) {
    let value = String(computed[prop] ?? computed[prop.replace(/[A-Z]/g, c => "-" + c.toLowerCase())] ?? "");
    if (key === "img") value = imageType(value);
    if (value && !defaults.includes(value)) out[key] = clip(value, 60);
  }
  const alpha = parseAlpha(String(computed.backgroundColor ?? ""));
  const image = imageType(computed.backgroundImage);
  const opacity = Number.parseFloat(String(computed.opacity ?? "1"));
  const opaque = (alpha >= 0.9 || /gradient|url/.test(image)) && (!Number.isFinite(opacity) || opacity >= 0.9);
  return { s: Object.keys(out).length ? out : undefined, opaque };
}

function matchedRules(element: any, rules: Record<string, string>): string[] | undefined {
  const hits: string[] = [];
  for (const [id, selector] of Object.entries(rules)) {
    try { if (element.matches?.(selector)) hits.push(id); } catch { /* A selector this engine cannot read. */ }
  }
  return hits.length ? hits : undefined;
}

export type CaptureOptions = { depth?: number; nodes?: number; labels?: boolean; rules?: Record<string, string> };

// One surface as a pruned tree: breadth-first, so a cut keeps the outer structure (where the panels and their
// backgrounds are) rather than one deep branch. `more` on a node says how many children were left out.
export function captureTree(root: any, view: SnapshotView, options: CaptureOptions = {}): { tree: SnapshotNode; nodes: number; truncated: boolean } {
  const maxDepth = options.depth ?? SNAPSHOT_DEPTH, maxNodes = Math.max(1, options.nodes ?? SNAPSHOT_NODES);
  const rules = options.rules ?? {};
  const rootRect = rectOf(root);
  const area = Math.max(1, rootRect[2] * rootRect[3]);
  let count = 0, truncated = false;
  const make = (element: any): SnapshotNode => {
    count++;
    const node: SnapshotNode = { t: String(element?.tagName ?? "?").toLowerCase(), r: rectOf(element) };
    const c = classesOf(element); if (c.length) node.c = c;
    const d = dataNames(element); if (d.length) node.d = d;
    const { s, opaque } = stylesOf(view, element); if (s) node.s = s;
    if (opaque && node.r[2] * node.r[3] > area * SNAPSHOT_COVER_SHARE) node.cover = 1;
    const m = matchedRules(element, rules); if (m) node.m = m;
    if (options.labels) { const l = shortLabel(element); if (l) node.l = l; }
    return node;
  };
  const tree = make(root);
  const queue: Array<[any, SnapshotNode, number]> = [[root, tree, 0]];
  while (queue.length) {
    const [element, node, depth] = queue.shift()!;
    let children: any[] = [];
    try { children = Array.from(element.children ?? []); } catch { children = []; }
    if (!children.length) continue;
    if (depth + 1 >= maxDepth) { node.more = children.length; truncated = true; continue; }
    const kids: SnapshotNode[] = [];
    for (const child of children) {
      if (count >= maxNodes) { node.more = (node.more ?? 0) + 1; truncated = true; continue; }
      const kid = make(child);
      kids.push(kid);
      queue.push([child, kid, depth + 1]);
    }
    if (kids.length) node.k = kids;
  }
  return { tree, nodes: count, truncated };
}

// Up to six ancestors of a surface's root (nearest first): enough to see what clips, scrolls or paints behind it.
export function ancestorsOf(element: any, view: SnapshotView, limit = 6): SnapshotNode[] {
  const out: SnapshotNode[] = [];
  let current = element?.parentElement;
  while (current && out.length < limit) {
    const node: SnapshotNode = { t: String(current.tagName ?? "?").toLowerCase(), r: rectOf(current) };
    const c = classesOf(current); if (c.length) node.c = c;
    const d = dataNames(current); if (d.length) node.d = d;
    const { s } = stylesOf(view, current); if (s) node.s = s;
    out.push(node);
    current = current.parentElement;
  }
  return out;
}

function visible(element: any): boolean {
  try { const r = element.getBoundingClientRect(); return r.width > 0 && r.height > 0; } catch { return false; }
}

export type FoundSurface = { id: string; key: string; surface: SnapshotSurface; element: any; doc: any; docIndex: number; via: string };

// Which surfaces are on screen now, across Steam's documents, through the selector table. The first visible element
// of the first strategy that finds one wins. Quick Access content is keyed per open tab ("qam-content:3:Core").
export function findSurfaces(docs: any[], maps: CoreUiMaps, surfaces: SnapshotSurface[] = SNAPSHOT_SURFACES): FoundSurface[] {
  const found: FoundSurface[] = [];
  for (const surface of surfaces) {
    let hit: FoundSurface | undefined;
    for (const target of surface.targets) {
      for (const selector of targetSelectors(maps, target, true)) {
        for (let index = 0; index < docs.length && !hit; index++) {
          let list: any[] = [];
          try { list = Array.from(docs[index].querySelectorAll(selector)).slice(0, 12); } catch { list = []; }
          const element = list.find(visible);
          if (element) hit = { id: surface.id, key: surface.id, surface, element, doc: docs[index], docIndex: index, via: `${target}: ${selector}` };
        }
        if (hit) break;
      }
      if (hit) break;
    }
    if (!hit) continue;
    if (surface.perTab) hit.key = `${surface.id}:${selectedQamTab(hit.doc, maps)}`;
    found.push(hit);
  }
  return found;
}

// The open Quick Access tab, as "<index>:<label>" (label at most 30 characters; index alone when it has none).
export function selectedQamTab(doc: any, maps: CoreUiMaps): string {
  const tabs: any[] = [];
  for (const selector of targetSelectors(maps, "qamTab", true)) { try { tabs.push(...Array.from(doc.querySelectorAll(selector))); } catch { /* Next. */ } if (tabs.length) break; }
  const selected = new Set<any>();
  for (const selector of targetSelectors(maps, "qamTabSelected", true)) { try { for (const tab of Array.from(doc.querySelectorAll(selector))) selected.add(tab); } catch { /* Next. */ } }
  const index = tabs.findIndex(tab => selected.has(tab));
  if (index < 0) return "unknown";
  const label = shortLabel(tabs[index]);
  return label ? `${index}:${label}` : String(index);
}

export type SurfaceRecord = {
  id: string; key: string; doc: number; via: string; at: string; rect: [number, number, number, number];
  nodes: number; truncated: boolean; tree: SnapshotNode; ancestors: SnapshotNode[];
};

export function recordSurface(found: FoundSurface, view: SnapshotView, rules: Record<string, string>, nodes = SNAPSHOT_NODES, at = new Date().toISOString()): SurfaceRecord {
  const { tree, nodes: count, truncated } = captureTree(found.element, view, { nodes, labels: found.surface.labels, rules });
  return { id: found.id, key: found.key, doc: found.docIndex, via: found.via, at, rect: tree.r, nodes: count, truncated, tree, ancestors: ancestorsOf(found.element, view) };
}

export type DocumentRecord = { index: number; main: boolean; path: string; title: string; width: number; height: number; dpr: number;
  coreStyle: boolean; coreStyleLast: boolean; stylesAfterCore: number; styles: number };

// What each Steam document is: the main window or a popup, its size and DPR, and where Core UI's stylesheet sits among
// the document's stylesheets (CSS Loader appends its own to <head>; Core UI keeps itself last).
export function documentRecord(doc: any, index: number, main: any, styleId: string): DocumentRecord {
  const view = doc?.defaultView ?? {};
  let path = "", title = "";
  try { path = clip(String(doc.location?.pathname ?? ""), 80); } catch { path = ""; }
  try { title = clip(doc.title ?? "", 40); } catch { title = ""; }
  let styles: any[] = [];
  try { styles = Array.from(doc.head?.querySelectorAll?.("style,link[rel~='stylesheet']") ?? []); } catch { styles = []; }
  const at = styles.findIndex(style => style?.id === styleId);
  return { index, main: doc === main, path, title, width: Number(view.innerWidth) || 0, height: Number(view.innerHeight) || 0, dpr: Number(view.devicePixelRatio) || 1,
    coreStyle: at >= 0, coreStyleLast: at >= 0 && at === styles.length - 1, stylesAfterCore: at >= 0 ? styles.length - 1 - at : 0, styles: styles.length };
}

export type SnapshotInput = {
  reason: string; version: string; now: Date;
  docs: any[]; main: any; styleId: string;
  sheet?: { applied?: string[]; skipped?: string[]; routes?: Record<string, string>; maps?: CoreUiMaps; ruleTargets?: Record<string, string>;
    // 3.3.8: per missing class map, the closest module's key names (names only, never hashed classes).
    candidates?: Record<string, { score: number; matched: string[]; missing: string[]; keys: string[] } | undefined>;
    // 3.3.10: per map more than one module matched, which was taken (key names only).
    ambiguous?: Record<string, { matches: number; switched: boolean; chosen: string[]; passedOver: string[][] } | undefined>;
    hosts?: Array<{ id: string; selector: string; source?: string; page?: boolean; fullHeight?: boolean; panel?: boolean }> };
  surfaces: Map<string, SurfaceRecord>;
  extra?: Record<string, unknown>;
};

// The whole snapshot, held under SNAPSHOT_MAX_BYTES: when too big, every surface is captured again with half the nodes
// (callers pass `recapture`), then trees are dropped largest first, each drop named in `dropped`.
export function assembleSnapshot(input: SnapshotInput, recapture?: (nodes: number) => Map<string, SurfaceRecord>): { json: string; bytes: number; nodes: number; dropped: string[] } {
  const firstView: SnapshotView = input.docs[0]?.defaultView ?? {};
  const base = {
    kind: "core-ui-snapshot", format: 1, core: input.version || undefined, reason: input.reason, capturedAt: input.now.toISOString(),
    screen: { width: Number(firstView.innerWidth) || 0, height: Number(firstView.innerHeight) || 0, dpr: Number(firstView.devicePixelRatio) || 1,
      screenWidth: Number(firstView.screen?.width) || 0, screenHeight: Number(firstView.screen?.height) || 0 },
    coreUi: { applied: input.sheet?.applied ?? [], skipped: input.sheet?.skipped ?? [], mapRoutes: input.sheet?.routes ?? {},
      mapCandidates: candidateList(input.sheet?.candidates),
      mapAmbiguity: ambiguityList(input.sheet?.ambiguous),
      selectors: input.sheet?.maps ? resolvedTargetTable(input.sheet.maps) : {},
      // 3.3.7: every Starfield host resolved on this client (Steam's and other plugins'), with its selector.
      hosts: hostList(input.sheet?.hosts) },
    documents: input.docs.map((doc, index) => documentRecord(doc, index, input.main, input.styleId)),
    ...(input.extra ?? {}),
  };
  let surfaces = input.surfaces, dropped: string[] = [];
  const encode = () => {
    const list = [...surfaces.values()].sort((a, b) => a.key.localeCompare(b.key));
    return JSON.stringify({ ...base, surfaces: list, dropped: dropped.length ? dropped : undefined });
  };
  let json = encode();
  const size = (text: string) => utf8Length(text);
  for (let nodes = SNAPSHOT_NODES / 2; size(json) > SNAPSHOT_MAX_BYTES && recapture && nodes >= 25; nodes = Math.floor(nodes / 2)) {
    surfaces = recapture(nodes);
    json = encode();
  }
  while (size(json) > SNAPSHOT_MAX_BYTES && surfaces.size) {
    const largest = [...surfaces.values()].sort((a, b) => JSON.stringify(b).length - JSON.stringify(a).length)[0];
    surfaces = new Map(surfaces); surfaces.delete(largest.key); dropped = [...dropped, largest.key];
    json = encode();
  }
  if (size(json) > SNAPSHOT_MAX_BYTES) json = JSON.stringify({ kind: "core-ui-snapshot", format: 1, core: input.version, reason: input.reason, capturedAt: input.now.toISOString(), error: "too_large" });
  let nodes = 0;
  for (const record of surfaces.values()) nodes += record.nodes;
  return { json, bytes: size(json), nodes, dropped };
}

// Missing maps' closest modules as the snapshot records them: key names only, bounded.
export function candidateList(candidates: NonNullable<SnapshotInput["sheet"]>["candidates"]): Record<string, { score: number; matched: string[]; missing: string[]; keys: string[] }> {
  const out: Record<string, { score: number; matched: string[]; missing: string[]; keys: string[] }> = {};
  if (!candidates || typeof candidates !== "object") return out;
  const names = (list: unknown, max: number) => (Array.isArray(list) ? list : []).slice(0, max).map(key => String(key).slice(0, 80));
  for (const [map, candidate] of Object.entries(candidates).slice(0, 32)) {
    if (!candidate) continue;
    out[map] = { score: Number(candidate.score) || 0, matched: names(candidate.matched, 40), missing: names(candidate.missing, 40), keys: names(candidate.keys, 120) };
  }
  return out;
}

// 3.3.10: maps more than one module matched, as the snapshot records them: key names only, bounded.
export function ambiguityList(ambiguous: NonNullable<SnapshotInput["sheet"]>["ambiguous"]): Record<string, { matches: number; switched: boolean; chosen: string[]; passedOver: string[][] }> {
  const out: Record<string, { matches: number; switched: boolean; chosen: string[]; passedOver: string[][] }> = {};
  if (!ambiguous || typeof ambiguous !== "object") return out;
  const names = (list: unknown) => (Array.isArray(list) ? list : []).slice(0, 12).map(key => String(key).slice(0, 80));
  for (const [map, seen] of Object.entries(ambiguous).slice(0, 40)) {
    if (!seen) continue;
    out[map] = { matches: Number(seen.matches) || 0, switched: seen.switched === true, chosen: names(seen.chosen),
      passedOver: (Array.isArray(seen.passedOver) ? seen.passedOver : []).slice(0, 8).map(names) };
  }
  return out;
}

// The host list as the snapshot records it: id, plain selector, where it came from and its kind.
export function hostList(hosts: NonNullable<SnapshotInput["sheet"]>["hosts"]): Array<{ id: string; selector: string; source: string; kind: string }> {
  if (!Array.isArray(hosts)) return [];
  return hosts.slice(0, 64).map(host => ({ id: String(host.id), selector: String(host.selector).slice(0, 200), source: host.source === "plugin" ? "plugin" : "steam",
    kind: host.page ? "page" : host.panel ? "panel" : "surface" }));
}

export function utf8Length(text: string): number {
  let bytes = 0;
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    if (code < 0x80) bytes += 1; else if (code < 0x800) bytes += 2;
    else if (code >= 0xd800 && code <= 0xdbff) { bytes += 4; i++; } else bytes += 3;
  }
  return bytes;
}

// ---- The scheduler: when to capture ----

export type ProbeDeps = {
  now: () => number;
  version: string;
  styleId: string;
  main: () => any;
  // The facts of the stylesheet in use (maps, routes, rule targets).
  sheet: () => SnapshotInput["sheet"];
  // Anything else worth recording (icon sources, the CSS Loader state, the Starfield's diagnostics).
  extra?: () => Record<string, unknown>;
  // Writes the JSON (main.py's save_core_ui_snapshot); answers where it went.
  write: (json: string) => Promise<{ ok?: boolean; path?: string; error?: string } | undefined>;
  log?: (message: string) => void;
};

export function createSnapshotProbe(deps: ProbeDeps) {
  const captured = new Map<string, SurfaceRecord>();
  const seen = new Set<string>();
  let docs: any[] = [], firstAt: number | undefined, dueAt: number | undefined, lastWrite = -Infinity, writing = false, reason = "start";
  let last: { ok?: boolean; path?: string; error?: string; bytes?: number; at?: string } | undefined;

  function captureAll(nodes = SNAPSHOT_NODES): Map<string, SurfaceRecord> {
    const sheet = deps.sheet();
    const maps = sheet?.maps ?? {};
    const rules = sheet?.ruleTargets ?? {};
    const at = new Date(deps.now()).toISOString();
    const next = new Map(captured);
    for (const found of findSurfaces(docs, maps)) {
      try { next.set(found.key, recordSurface(found, found.doc?.defaultView ?? {}, rules, nodes, at)); } catch { /* That surface closed mid-capture. */ }
    }
    return next;
  }

  async function write(why: string) {
    if (writing) return last;
    writing = true;
    try {
      const fresh = captureAll();
      for (const [key, record] of fresh) captured.set(key, record);
      const assembled = assembleSnapshot({ reason: why, version: deps.version, now: new Date(deps.now()), docs, main: deps.main(), styleId: deps.styleId,
        sheet: deps.sheet(), surfaces: captured, extra: deps.extra?.() }, nodes => captureAll(nodes));
      lastWrite = deps.now();
      const answer = await deps.write(assembled.json);
      last = { ok: answer?.ok === true, path: answer?.path, error: answer?.error, bytes: assembled.bytes, at: new Date(deps.now()).toISOString() };
      if (!last.ok) deps.log?.(`[Core UI] snapshot not saved: ${answer?.error ?? "no answer"}`);
      return last;
    } catch (error) {
      last = { ok: false, error: String((error as any)?.message ?? error).slice(0, 120) };
      deps.log?.(`[Core UI] snapshot not saved: ${last.error}`);
      return last;
    } finally { writing = false; }
  }

  return {
    // Called by Core UI once a second with Steam's documents: schedules the first capture and one per newly opened surface.
    tick(currentDocs: any[]) {
      docs = currentDocs;
      const now = deps.now();
      if (firstAt === undefined) { firstAt = now + SNAPSHOT_FIRST_MS; dueAt = firstAt; reason = "start"; }
      if (now >= (firstAt ?? 0)) {
        let fresh: string[] = [];
        try { fresh = findSurfaces(docs, deps.sheet()?.maps ?? {}).map(found => found.key).filter(key => !seen.has(key)); } catch { fresh = []; }
        if (fresh.length) {
          for (const key of fresh) seen.add(key);
          if (dueAt === undefined) { dueAt = now + SNAPSHOT_SETTLE_MS; reason = `opened: ${fresh.join(", ")}`; }
          else if (reason !== "start") reason = `${reason}, ${fresh.join(", ")}`;
        }
      }
      if (dueAt !== undefined && now >= dueAt && now - lastWrite >= SNAPSHOT_MIN_GAP_MS && !writing) {
        dueAt = undefined;
        void write(reason);
      }
    },
    saveNow: (currentDocs?: any[]) => { if (currentDocs) docs = currentDocs; return write("manual"); },
    last: () => last,
    keys: () => [...captured.keys()],
  };
}

// ---- The Look section's button ----

type Control = { available(): boolean; saveNow(): Promise<{ ok?: boolean; path?: string; error?: string } | undefined> };
let control: { saveNow: () => Promise<any> } | undefined;
export function setCoreUiSnapshotControl(next: { saveNow: () => Promise<any> } | undefined) { control = next; }
export const coreUiSnapshotControl: Control = {
  available: () => !!control,
  saveNow: () => control ? control.saveNow() : Promise.resolve({ ok: false, error: "unavailable" }),
};
