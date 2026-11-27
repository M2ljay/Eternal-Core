// Eternal Core: Jarvis's push-to-talk and screen-helper flow in Core's plugin (docs/eternal/JARVIS.md). The combos
// (jarvisCombos.ts) and the Quick Access buttons both come through here; the agent records, transcribes, answers and
// takes the screenshot (MarkIIIHost.Jarvis.cs). Dictated text is typed through Steam's own keyboard path
// (SteamClient.Input.ControllerKeyboardSendText, what Steam's on-screen keyboard uses) and only falls back to the agent's
// SendInput when Steam has no such call. Ideas from WhisPTT (BSD-3), Decktation and SpeechToDeck; no code taken.
// Self-contained with injected dependencies: tests/coreJarvis.test.mjs runs it against fakes.

import { errorLine, fill, jarvisCopy } from "./jarvisReport";
import type { ComboKind, ReleaseAction } from "./jarvisCombos";

export type ListenMode = "dictate" | "talk";
export type StopAnswer = { ok?: boolean; mode?: string; text?: string; typed?: boolean; answer?: string | null; error?: string | null; refused?: boolean };
export type ScreenAnswer = { ok?: boolean; text?: string; more?: boolean; error?: string | null };

export type JarvisBackend = {
  // auto: tap to talk, the agent ends the recording on a pause (3.3.5).
  listenStart: (mode: ListenMode, auto?: boolean) => Promise<{ ok?: boolean; error?: string | null; mic?: string | null; partyHears?: boolean; pillHidden?: boolean }>;
  listenState?: () => Promise<{ recording?: boolean; ended?: boolean }>;
  listenStop: (steamTyping: boolean) => Promise<StopAnswer>;
  listenCancel: () => Promise<unknown>;
  screen: (question: string) => Promise<ScreenAnswer>;
  type: (text: string) => Promise<{ ok?: boolean; error?: string | null }>;
};

export type RuntimeDeps = {
  backend: JarvisBackend;
  input: () => any;
  toast: (title: string, body: string) => void;
  locale: () => string;
  enabled: () => boolean;
  schedule?: (callback: () => void, delay: number) => unknown;
};

export type RuntimeSnapshot = { listening: ListenMode | null; working: boolean; heard?: string; answer?: string; screen?: ScreenAnswer; error?: string };

export type JarvisRuntime = {
  press: (kind: ComboKind) => void;
  // action: what the combo watch decided on release (jarvisRelease): "talk" keeps a tap's recording going until a pause.
  release: (kind: ComboKind, action?: ReleaseAction) => void;
  start: (mode: ListenMode) => Promise<boolean>;
  stop: (options?: { intoField?: boolean }) => Promise<StopAnswer | undefined>;
  // Drops a recording without transcribing it (a short tap on the talk combo).
  cancel: () => Promise<void>;
  screen: (question?: string) => Promise<ScreenAnswer | undefined>;
  snapshot: () => RuntimeSnapshot;
  subscribe: (listener: () => void) => () => void;
};

export function createJarvisRuntime(deps: RuntimeDeps): JarvisRuntime {
  let snapshot: RuntimeSnapshot = { listening: null, working: false };
  let starting: Promise<boolean> | undefined;
  const listeners = new Set<() => void>();
  const set = (next: Partial<RuntimeSnapshot>) => {
    snapshot = { ...snapshot, ...next };
    for (const listener of listeners) { try { listener(); } catch { /* A closed view. */ } }
  };
  const text = () => jarvisCopy(deps.locale());
  const fail = (code: unknown) => {
    const line = errorLine(code, deps.locale());
    set({ error: line });
    deps.toast(text().title, line);
  };

  // Tap to talk: the recording keeps going after the tap; a pause (the agent), a second tap or the cap ends it.
  let latched = false, secondTap = false, polls = 0;
  const later = (callback: () => void, delay: number) => (deps.schedule ?? ((run, wait) => setTimeout(run, wait)))(callback, delay);
  const poll = () => {
    if (!latched || !deps.backend.listenState) return;
    if (++polls > 200) { latched = false; void stop(); return; }
    deps.backend.listenState().then(state => {
      if (!latched) return;
      if (state?.ended || state?.recording === false) { latched = false; void stop(); }
      else later(poll, 150);
    }).catch(() => { if (latched) later(poll, 300); });
  };

  const start = (mode: ListenMode, auto = false): Promise<boolean> => {
    if (!deps.enabled()) { fail("off"); return Promise.resolve(false); }
    if (snapshot.listening || starting || snapshot.working) return Promise.resolve(false);
    set({ listening: mode, error: undefined });
    starting = deps.backend.listenStart(mode, auto).then(answer => {
      if (!answer?.ok) { set({ listening: null }); fail(answer?.error); return false; }
      if (answer.partyHears) deps.toast(text().title, text().party);
      // Exclusive fullscreen can hide the agent's pill: say it here instead (the orb pulses as well).
      if (answer.pillHidden) deps.toast(text().title, text().listening);
      return true;
    }).catch(() => { set({ listening: null }); fail("unreachable"); return false; })
      .finally(() => { starting = undefined; });
    return starting;
  };

  // Steam's keyboard path types into the focused field the way Steam's own keyboard does.
  const steamSend = (): ((value: string) => void) | undefined => {
    try {
      const input = deps.input();
      return typeof input?.ControllerKeyboardSendText === "function" ? (value: string) => input.ControllerKeyboardSendText(value) : undefined;
    } catch { return undefined; }
  };

  const stop = async (options: { intoField?: boolean } = {}): Promise<StopAnswer | undefined> => {
    latched = false;
    if (starting && !(await starting)) return undefined;
    const mode = snapshot.listening;
    if (!mode) return undefined;
    set({ listening: null, working: true });
    const send = mode === "dictate" && !options.intoField ? steamSend() : undefined;
    let answer: StopAnswer;
    try { answer = await deps.backend.listenStop(options.intoField === true || send !== undefined); }
    catch { set({ working: false }); fail("unreachable"); return undefined; }
    set({ working: false });
    if (!answer?.ok && !answer?.answer) { fail(answer?.error); return answer; }
    const heard = typeof answer.text === "string" ? answer.text : "";
    if (mode === "dictate") {
      set({ heard });
      if (options.intoField || !heard) return answer;
      if (!answer.typed) {
        let typed = false;
        if (send) { try { send(heard); typed = true; } catch { typed = false; } }
        if (!typed && answer.error !== "anti-cheat") {
          const fallback = await deps.backend.type(heard).catch(() => ({ ok: false, error: "unreachable" }));
          typed = fallback?.ok === true;
          if (!typed) { fail(fallback?.error ?? "failed"); return answer; }
        }
        if (!typed) { fail("anti-cheat"); return answer; }
      }
      deps.toast(text().title, fill(text().typed, { text: heard.length > 80 ? heard.slice(0, 79) + "…" : heard }));
      return answer;
    }
    set({ heard, answer: answer.answer ?? undefined });
    if (answer.answer) deps.toast(text().title, answer.answer);
    else fail(answer.error);
    return answer;
  };

  const screen = async (question = ""): Promise<ScreenAnswer | undefined> => {
    if (!deps.enabled()) { fail("off"); return undefined; }
    if (snapshot.working) { fail("busy"); return undefined; }
    set({ working: true, error: undefined });
    let answer: ScreenAnswer;
    try { answer = await deps.backend.screen(question); }
    catch { set({ working: false }); fail("unreachable"); return undefined; }
    set({ working: false });
    if (!answer?.ok || typeof answer.text !== "string") { fail(answer?.error); return answer; }
    set({ screen: answer });
    deps.toast(text().title, answer.more ? `${answer.text} (${text().more}: Quick Access)` : answer.text);
    return answer;
  };

  const cancel = async (): Promise<void> => {
    latched = false;
    if (starting) await starting.catch(() => false);
    if (!snapshot.listening) return;
    set({ listening: null });
    try { await deps.backend.listenCancel(); } catch { /* The agent drops an orphaned recording on its own. */ }
  };

  return {
    cancel,
    press: kind => {
      if (kind === "screen") { void screen(); return; }
      // A second tap while a tap's recording runs ends it (on its release).
      if (kind === "talk" && latched) { secondTap = true; return; }
      void start(kind, kind === "talk");
    },
    release: (kind, action = "release") => {
      if (kind === "screen") return;
      if (secondTap) { secondTap = false; latched = false; void stop(); return; }
      if (kind === "talk" && action === "talk") {
        if (!(snapshot.listening === "talk" || starting)) return;
        latched = true; polls = 0; later(poll, 150);
        return;
      }
      if (action === "open") { void cancel(); return; }
      if (snapshot.listening === kind || starting) void stop();
    },
    start, stop, screen,
    snapshot: () => snapshot,
    subscribe: listener => { listeners.add(listener); return () => listeners.delete(listener); },
  };
}
