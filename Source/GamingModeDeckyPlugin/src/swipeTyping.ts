// Eternal Core batch 2 (B7, docs/eternal/BATCH-2.md): swipe typing, word suggestions and autocorrect on Steam's own
// on-screen keyboard, with the trackpads. Adapted from VaporType 0.3.2 (MIT, Copyright (c) 2026 VaporType
// contributors, github.com/dmitrii-galantsev/VaporType, commit 53b499c, src/keyboard.ts): this is its loader. The
// keyboard itself (patch.js, modes.js, swipe/engine.js and the word data) ships unchanged in Core's plugin under
// dist/keyboard/ except for one hook in patch.js (see dist/keyboard/CORE-CHANGES.md); the MIT notice and the data
// licences (CC BY-SA 4.0 word data) are there too.
//
// Changes from VaporType's loader: no self-update (Core updates the files with Core); the on/off switch is Core's
// setting; Decky 3/4: the Steam client kind and the keyboard lookups go through steamModules.ts (Core's compat
// helpers), handed to patch.js as window.__coreOskLookups; and Core stands aside when the standalone VaporType plugin
// is loaded, so the two never patch the keyboard at once. Everything is injected, so tests/coreBatchTwo.test.mjs runs
// it against fakes.

export type TypingBackendState = { userWords: unknown; enabled: boolean; epoch: number; install?: unknown };

export type OskFix = {
  version: number;
  stopped?: boolean;
  unsupported?: string | null;
  status: () => string;
  stop: () => void;
  setSetting: (name: string, value: unknown) => void;
  openSettings: () => boolean;
  forgetAll: () => void;
  flushLog?: () => void;
  keyboardOpen?: () => boolean;
  typingLog?: { out?: unknown[] };
  takeUserWords?: () => string | null;
  ackUserWords?: (rev: number) => number;
  takeTypingLog?: () => string | null;
  ackTypingLog?: (upTo: number) => number;
  takeRequests?: () => string | null;
  ackRequests?: (rev: number) => number;
  forgetGen?: number;
  hostAcks?: boolean;
};

type Saved = { saved?: boolean; rev?: number | null; epoch?: number };

export type SwipeTypingDeps = {
  load: () => Promise<TypingBackendState>;
  setEnabled: (on: boolean) => Promise<{ enabled?: unknown }>;
  saveWords: (payload: object, epoch: number) => Promise<Saved>;
  appendLog: (batch: { lines: string[] }, epoch: number) => Promise<Saved>;
  requests: (payload: string, session: string) => Promise<{ epoch?: number; rev?: number | null; ok?: boolean }>;
  forget: () => Promise<{ epoch?: number; ok?: boolean }>;
  fetchText: (file: string) => Promise<string>;
  evaluate: (source: string) => unknown;
  globals: Record<string, any>;
  clientKind: () => "webpack" | "modules" | "unknown";
  standalone: () => boolean;
  lookups: Record<string, () => unknown>;
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
  now: () => number;
  removeStoredWords: () => void;
};

export const KEYBOARD_FILES = ["swipe/words.js", "swipe/lm.js", "swipe/engine.js", "modes.js", "patch.js"] as const;
type KeyboardFile = (typeof KEYBOARD_FILES)[number];

const PRELUDE: { file: KeyboardFile; global: string; pattern: RegExp }[] = [
  { file: "swipe/words.js", global: "__oskWords", pattern: /version: (\d+)/ },
  { file: "swipe/lm.js", global: "__oskLM", pattern: /version: (\d+)/ },
  { file: "swipe/engine.js", global: "__oskSwipeEngine", pattern: /const VERSION = (\d+)/ },
  { file: "modes.js", global: "__oskModes", pattern: /const VERSION = (\d+)/ },
];

const POLL_MS = 5000;
const SAVE_MS = 60000;
const LOG_BATCH = 200;

export type SwipeTypingStatus = "loading" | "on" | "off" | "modules" | "standalone" | "paused";

export function createSwipeTyping(deps: SwipeTypingDeps) {
  const g = deps.globals;
  let sources: Promise<Record<KeyboardFile, string>> | null = null;
  let state: TypingBackendState | null = null;
  let lastError: string | null = null;
  let generation = 0;
  let disposed = false;
  let pollTimer: unknown = null;
  let queue: Promise<unknown> = Promise.resolve();
  let lastSave = 0;
  let wasOpen = false;
  let blocked: "modules" | "standalone" | null = null;

  const message = (error: unknown) => String((error as Error)?.message ?? error).slice(0, 160);
  const live = (gen: number) => !disposed && gen === generation && state?.enabled === true && blocked === null;
  const fix = (): OskFix | undefined => g.__oskTrackpadFix;

  function serial<T>(job: () => Promise<T>): Promise<T> {
    const run = queue.then(job, job);
    queue = run.catch(() => undefined);
    return run;
  }

  function fetchSources(): Promise<Record<KeyboardFile, string>> {
    if (!sources) {
      const pending = Promise.all(KEYBOARD_FILES.map(async file => [file, await deps.fetchText(file)] as const))
        .then(pairs => Object.fromEntries(pairs) as Record<KeyboardFile, string>);
      pending.catch(() => { if (sources === pending) sources = null; });
      sources = pending;
    }
    return sources;
  }

  // Decky 3/4: the stable Steam client (webpack) is what the keyboard patch understands. On the ES-module client it
  // would only find nothing and roll back, so Core does not inject it there at all. Standalone VaporType: it owns the
  // keyboard; injecting a second copy would stop it.
  function gate(): boolean {
    let kind: string = "unknown", standalone = false;
    try { kind = deps.clientKind(); } catch { kind = "unknown"; }
    try { standalone = deps.standalone(); } catch { standalone = false; }
    blocked = standalone ? "standalone" : kind === "modules" ? "modules" : null;
    return blocked === null;
  }

  async function inject(gen: number): Promise<string | null> {
    const src = await fetchSources();
    for (const { file, global, pattern } of PRELUDE) {
      if (!live(gen)) return null;
      const want = Number(pattern.exec(src[file])?.[1] ?? 0);
      if ((g[global]?.version ?? 0) !== want) deps.evaluate(src[file] + "\n;0");
    }
    if (!live(gen)) return null;
    if (!g.__oskUserWordsLoaded) {
      g.__oskUserWords = state?.userWords ?? null;
      g.__oskUserWordsLoaded = true;
    }
    g.__oskInstall = { date: null, via: "core", decky: "", plugin: "core" };
    g.__coreOskLookups = deps.lookups;
    return String(deps.evaluate(src["patch.js"]));
  }

  function due(f: OskFix, force: boolean): boolean {
    const now = deps.now();
    let open = false;
    try { open = f.keyboardOpen?.() === true; } catch (error) { lastError = message(error); }
    if (open && !wasOpen) lastSave = now;
    wasOpen = open;
    if (force || !open || now - lastSave >= SAVE_MS || (f.typingLog?.out?.length ?? 0) >= LOG_BATCH) { lastSave = now; return true; }
    return false;
  }

  type Batch = { fix: OskFix; epoch: number; gen: number; requests: string | null; words: string | null; log: string | null };

  function collect(f: OskFix, force = true): Batch {
    let save = true;
    try { f.hostAcks = true; save = due(f, force); if (save) f.flushLog?.(); } catch (error) { lastError = message(error); }
    return {
      fix: f, epoch: state?.epoch ?? 0, gen: f.forgetGen ?? 0,
      requests: f.takeRequests?.() ?? null,
      words: save ? (f.takeUserWords?.() ?? null) : null,
      log: save ? (f.takeTypingLog?.() ?? null) : null,
    };
  }

  async function handle(f: OskFix, requests: string): Promise<number> {
    g.__vaporTypeSession ??= `${deps.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
    const answer = await deps.requests(requests, String(g.__vaporTypeSession));
    const epoch = Number(answer?.epoch ?? state?.epoch ?? 0);
    if (state) state.epoch = epoch;
    const rev = (JSON.parse(requests) as { rev?: unknown }).rev;
    if (!answer?.ok) lastError = "some files could not be deleted; retrying";
    else if (typeof rev === "number") f.ackRequests?.(rev);
    return epoch;
  }

  const forgotSince = (f: OskFix, gen: number) => (f.forgetGen ?? 0) !== gen;

  async function persist(batch: Batch): Promise<void> {
    if (!state) return;
    const f = batch.fix;
    try {
      lastError = null;
      let epoch = batch.epoch;
      if (batch.requests) epoch = await handle(f, batch.requests);
      const logEpoch = epoch;
      let words = batch.words;
      let gen = batch.gen;
      for (let attempt = 0; words && attempt < 3; attempt++) {
        const answer = await deps.saveWords(JSON.parse(words) as object, epoch);
        const answered = Number(answer?.epoch ?? epoch);
        if (state) state.epoch = answered;
        if (answer?.saved && !forgotSince(f, gen)) {
          if (typeof answer.rev === "number") f.ackUserWords?.(answer.rev);
          break;
        }
        const request = f.takeRequests?.();
        epoch = request ? await handle(f, request) : answered;
        gen = f.forgetGen ?? 0;
        words = f.takeUserWords?.() ?? null;
      }
      if (batch.log && !forgotSince(f, batch.gen)) {
        const { upTo, lines } = JSON.parse(batch.log) as { upTo: number; lines: string[] };
        const answer = await deps.appendLog({ lines }, logEpoch);
        const answered = Number(answer?.epoch ?? logEpoch);
        if (state) state.epoch = answered;
        if (answer?.saved || answered !== logEpoch) f.ackTypingLog?.(Number(upTo));
      }
    } catch (error) {
      lastError = `saving failed: ${message(error)}`;
    }
  }

  async function tick(gen: number): Promise<void> {
    if (!live(gen)) return;
    try {
      const f = fix();
      if (!f || (f.stopped && !f.unsupported)) await inject(gen);
    } catch (error) {
      lastError = `loading failed: ${message(error)}`;
    }
    if (!live(gen)) return;
    const f = fix();
    if (f) await persist(collect(f, false));
  }

  function cancelPoll() {
    if (pollTimer !== null) deps.cancel(pollTimer);
    pollTimer = null;
  }

  function schedule(gen: number) {
    cancelPoll();
    if (!live(gen)) return;
    pollTimer = deps.schedule(() => {
      pollTimer = null;
      void serial(() => tick(gen)).finally(() => { if (live(gen) && pollTimer === null) schedule(gen); });
    }, POLL_MS);
  }

  function stopLocal(): Promise<void> {
    const f = fix();
    if (!f) return Promise.resolve();
    const batch = collect(f);
    try { if (!f.stopped) f.stop(); } catch (error) { lastError = `stop failed: ${message(error)}`; }
    return serial(() => persist(batch));
  }

  async function start(): Promise<void> {
    const gen = generation;
    let loaded: TypingBackendState;
    try {
      loaded = await deps.load();
      if (!loaded || typeof loaded.enabled !== "boolean") throw new Error("no answer");
    } catch (error) {
      lastError = `backend unavailable: ${message(error)}`;
      loaded = { userWords: null, enabled: false, epoch: 0 };
    }
    if (disposed || gen !== generation) return;
    state = { ...loaded, epoch: Number(loaded.epoch) || 0 };
    if (!gate()) return;
    await serial(() => tick(gen));
    schedule(gen);
  }

  function shutdown(): void {
    disposed = true;
    generation++;
    cancelPoll();
    void stopLocal();
    try { delete g.__coreOskLookups; } catch { /* Not ours to clear. */ }
  }

  async function setEnabled(on: boolean): Promise<void> {
    if (disposed || !state) return;
    const gen = ++generation;
    cancelPoll();
    state.enabled = on;
    const stopped = on ? Promise.resolve() : stopLocal();
    try {
      const answer = await serial(() => deps.setEnabled(on));
      if (typeof answer?.enabled === "boolean") state.enabled = answer.enabled;
    } catch (error) {
      lastError = `could not save the setting: ${message(error)}`;
    }
    await stopped;
    if (!on || !gate() || !live(gen)) return;
    await serial(() => tick(gen));
    schedule(gen);
  }

  function forgetEverything(): Promise<boolean> {
    return serial(async () => {
      const f = fix();
      let ok = true;
      try { f?.forgetAll(); } catch (error) { lastError = `forget failed: ${message(error)}`; ok = false; }
      try { deps.removeStoredWords(); } catch (error) { lastError = message(error); ok = false; }
      g.__oskUserWords = null;
      if (state) state.userWords = null;
      try {
        const answer = await deps.forget();
        if (state && answer?.epoch !== undefined) state.epoch = Number(answer.epoch);
        if (!answer?.ok) { lastError = "some files could not be deleted; they will be retried"; ok = false; }
      } catch (error) { lastError = message(error); ok = false; }
      if (f) await persist(collect(f));
      return ok;
    });
  }

  function status(): { state: SwipeTypingStatus; error: string | null } {
    if (blocked) return { state: blocked, error: null };
    if (!state) return { state: "loading", error: lastError };
    if (!state.enabled) return { state: "off", error: null };
    const f = fix();
    if (f?.unsupported) return { state: "paused", error: String(f.unsupported) };
    if (f && !f.stopped) return { state: "on", error: lastError };
    return { state: lastError ? "paused" : "loading", error: lastError };
  }

  return {
    start, shutdown, setEnabled, forgetEverything, status,
    enabled: () => state?.enabled === true,
    running: () => { const f = fix(); return !!f && !f.stopped && blocked === null; },
    settingsValue: (name: string): unknown => g.__oskSettings?.[name],
    setSetting: (name: string, value: unknown): boolean => {
      const f = fix();
      if (!f || f.stopped) return false;
      f.setSetting(name, value);
      return true;
    },
    openSettingsWhenKeyboardOpens: (ms: number): Promise<boolean> => new Promise(resolve => {
      const until = deps.now() + ms;
      const poll = () => {
        const f = fix();
        if (disposed) resolve(false);
        else if (f && !f.stopped && f.openSettings()) resolve(true);
        else if (deps.now() > until) resolve(false);
        else deps.schedule(poll, 250);
      };
      poll();
    }),
  };
}

export type SwipeTyping = ReturnType<typeof createSwipeTyping>;

// Core's Decky 3/4 lookups for patch.js (window.__coreOskLookups): Steam's trackpad-typing settings store and the
// keyboard's CSS class map. Built from steamModules.ts's findExport / findClassModule by the caller; pure here.
export function keyboardLookups(ui: any, finders: { findExport: (ui: any, filter: (value: any) => any) => any; findClassModule: (ui: any, filter: (module: any) => boolean) => Record<string, string> | undefined }) {
  return {
    inputSettings: () => {
      const found = finders.findExport(ui, (value: any) => value && typeof value === "object" && "TrackpadTypingTriggerAsClick" in value);
      return found && typeof found === "object" ? found : null;
    },
    classes: () => {
      const keyboard = finders.findClassModule(ui, (module: any) => !!module && typeof module.TouchpadPointerContainer === "string" && typeof module.KeyboardKey === "string");
      if (!keyboard || typeof keyboard.Keyboard !== "string") return null;
      const glass = finders.findClassModule(ui, (module: any) => !!module && typeof module.BackgroundGlass === "string" && typeof module.DrawBackground === "string");
      return { ...(glass ? { BackgroundGlass: glass.BackgroundGlass, DrawBackground: glass.DrawBackground } : {}), ...keyboard };
    },
  };
}

// The standalone VaporType plugin, in Decky's plugin list: Core then leaves the keyboard to it.
export function vaporTypeInstalled(inventory: any): boolean {
  try {
    const names = [...(Array.isArray(inventory?.plugins) ? inventory.plugins : []), ...(Array.isArray(inventory?.installedPlugins) ? inventory.installedPlugins : [])]
      .map((plugin: any) => String(typeof plugin === "string" ? plugin : plugin?.name ?? "").trim().toLowerCase());
    return names.includes("vaportype");
  } catch { return false; }
}
