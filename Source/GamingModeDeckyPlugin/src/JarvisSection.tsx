import { DFL, SP_REACT as React, toaster } from "./decky";
import { call } from "./controlBackend";
import { CORE, CORE_STATUS } from "./corePalette";
import { combosLine, errorLine, historyLines, jarvisCopy, statusLine, statusTone, type JarvisState } from "./jarvisReport";
import type { JarvisRuntime } from "./jarvisRuntime";
import { jarvisTabCopy, jarvisTapLine } from "./jarvisTabLocale";
import { subscribeJarvisFocus, takeJarvisFocus, type Presence } from "./jarvisPresence";
import { CoreStars } from "./CoreStars";

// Eternal Core: Jarvis in Quick Access > Core and on its own full page (docs/eternal/JARVIS.md). Type with Steam's
// keyboard (swipe typing applies), dictate into the field, talk to Jarvis, or ask about the screen. The agent does the
// work (MarkIIIHost.Jarvis.cs); the combos and push-to-talk flow are jarvisRuntime.ts. Nothing is sent until asked.
const { PanelSection, PanelSectionRow, Focusable, TextField, DialogButton, Navigation } = DFL as any;
const { black: BLACK, steel: STEEL, gunmetal: GUNMETAL, gunmetalRaised: RAISED, white: WHITE, crimson: CRIMSON, crimsonText: CRIMSON_TEXT, hint: HINT, text: TEXT } = CORE;

export const JARVIS_ROUTE = "/core-jarvis";

let runtime: JarvisRuntime | undefined;
export function setJarvisRuntime(value: JarvisRuntime | undefined) { runtime = value; }
let presence: Presence | undefined;
export function setJarvisPresence(value: Presence | undefined) { presence = value; }

const style = `
.core-jarvis{background:transparent;border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:8px;width:100%}
.core-jarvis-status{padding:0 12px;font-size:12px;line-height:1.4}
.core-jarvis-log{display:flex;flex-direction:column;gap:6px;max-height:260px;overflow-y:auto;padding:0 2px}
.core-jarvis.full .core-jarvis-log{max-height:none}
.core-jarvis-turn{background:${GUNMETAL};border-radius:6px;padding:8px 12px;font-size:13px;line-height:1.45;color:${TEXT};outline:none;white-space:pre-wrap}
.core-jarvis-turn.user{background:${BLACK};border:1px solid ${RAISED};color:${STEEL}}
.core-jarvis-turn.gpfocus,.core-jarvis-turn:focus-visible{outline:2px solid ${CRIMSON};outline-offset:-2px}
.core-jarvis-who{display:block;font-size:11px;font-weight:600;color:${HINT};margin-bottom:2px;text-transform:uppercase;letter-spacing:.04em}
.core-jarvis-buttons{display:grid;grid-template-columns:1fr 1fr;gap:6px}
.core-jarvis-buttons .DialogButton,.core-jarvis-buttons button{min-width:0!important;padding:8px 6px!important;font-size:12px!important}
.core-jarvis-live{display:flex;align-items:center;gap:8px;padding:8px 12px;border-radius:6px;background:${BLACK};border:1px solid ${CRIMSON};color:${CRIMSON_TEXT};font-size:13px;font-weight:600}
.core-jarvis-dot{width:10px;height:10px;border-radius:50%;background:${CRIMSON};flex:none}
.core-jarvis-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${HINT}}
.core-jarvis-screen{border-left:3px solid ${CRIMSON}}
.core-jarvis-last{border-left:3px solid ${CRIMSON}}
.core-jarvis-ask{display:grid;grid-template-columns:1fr auto;gap:6px;align-items:end}
.core-jarvis-ask .DialogButton,.core-jarvis-ask button{min-width:0!important;padding:8px 12px!important;font-size:12px!important}
`;

function useJarvisState(): [JarvisState | null | undefined, () => void] {
  const [state, setState] = React.useState<JarvisState | null | undefined>(undefined);
  const load = React.useCallback(() => {
    call<[], JarvisState>("core_jarvis_state")
      .then(value => setState(value && typeof value === "object" && "status" in value ? value : null))
      .catch(() => setState(null));
  }, []);
  React.useEffect(() => {
    load();
    const timer = window.setInterval(load, 3000);
    const unsubscribe = runtime?.subscribe(load);
    return () => { window.clearInterval(timer); unsubscribe?.(); };
  }, [load]);
  return [state, load];
}

// layout: "section" (inside Quick Access > Core), "page" (the full page) or "tab" (Jarvis's own Quick Access tab:
// the input first, then What's on screen?, the hold-to-talk hint, the last answer and the chat).
export function JarvisPanel({ locale, full = false, tab = false }: { locale: string; full?: boolean; tab?: boolean }) {
  const text = jarvisCopy(locale);
  const tabText = jarvisTabCopy(locale);
  const field = React.useRef<HTMLDivElement | null>(null);
  React.useEffect(() => {
    if (!tab) return;
    const focus = () => { if (takeJarvisFocus()) window.setTimeout(() => { try { field.current?.querySelector("input")?.focus(); } catch { /* Gone. */ } }, 60); };
    focus();
    return subscribeJarvisFocus(focus);
  }, [tab]);
  const [state, reload] = useJarvisState();
  const [draft, setDraft] = React.useState("");
  const [pending, setPending] = React.useState(false);
  const [screenText, setScreenText] = React.useState<{ text: string; more: boolean } | null>(null);
  const [, setTick] = React.useState(0);
  React.useEffect(() => runtime?.subscribe(() => setTick((value: number) => value + 1)), []);
  if (state === undefined) return null;
  if (state && state.enabled === false) return full ? <div className="core-jarvis-hint">{text.stOff}</div> : null;
  const live = runtime?.snapshot();
  const listening = live?.listening ?? (state?.listening?.active ? (state.listening.mode as "dictate" | "talk") : null);
  const screen = screenText ?? (state?.screen?.text ? { text: state.screen.text, more: state.screen.more === true } : null);
  const turns = historyLines(state, full ? 30 : 6);
  const lastAnswer = [...turns].reverse().find(turn => turn.role === "assistant")?.text;
  const toast = (body: string) => toaster.toast({ title: text.title, body });

  const send = async () => {
    const message = draft.trim();
    if (!message || pending) return;
    setPending(true);
    const answer = await call<[string, boolean], { ok?: boolean; text?: string; error?: string }>("core_jarvis_chat", message, false).catch(() => undefined);
    setPending(false);
    if (answer?.text) { setDraft(""); presence?.ready(); }
    else toast(errorLine(answer?.error ?? "unreachable", locale));
    reload();
  };
  const dictate = async () => {
    if (!runtime) return;
    if (listening === "dictate") {
      const answer = await runtime.stop({ intoField: true });
      if (answer?.ok && answer.text) setDraft((value: string) => (value ? `${value} ${answer.text}` : answer.text!));
    } else await runtime.start("dictate");
  };
  const talk = async () => {
    if (!runtime) return;
    if (listening === "talk") { await runtime.stop(); reload(); } else await runtime.start("talk");
  };
  const askScreen = async () => {
    if (!runtime) return;
    // Quick Access closes first, so the screenshot shows the game rather than this panel.
    try { Navigation?.CloseSideMenus?.(); } catch { /* Already closed. */ }
    await new Promise(resolve => window.setTimeout(resolve, 700));
    const answer = await runtime.screen(draft.trim());
    if (answer?.ok && answer.text) { setScreenText({ text: answer.text, more: answer.more === true }); setDraft(""); }
  };
  const more = async () => {
    const answer = await call<[], { ok?: boolean; text?: string; error?: string }>("core_jarvis_screen_more").catch(() => undefined);
    if (answer?.ok && answer.text) setScreenText({ text: answer.text, more: false });
    else toast(errorLine(answer?.error, locale));
  };
  const speak = (value: string) => { void call<[string], unknown>("core_jarvis_speak", value).catch(() => undefined); };
  const clear = async () => {
    await call<[], unknown>("core_jarvis_clear").catch(() => undefined);
    setScreenText(null);
    reload();
  };
  const openPage = () => {
    try { Navigation?.Navigate?.(JARVIS_ROUTE); Navigation?.CloseSideMenus?.(); } catch { /* Router unavailable. */ }
  };
  const tone = statusTone(state);
  const input = (
    <div ref={field}>
      <TextField value={draft} label={text.placeholder} onChange={(event: any) => setDraft(String(event?.target?.value ?? "").slice(0, 2000))} />
    </div>
  );
  if (tab) {
    const tapLine = jarvisTapLine(state, locale);
    return (
      <Focusable className="core-jarvis tab" flow-children="column">
        <style>{style}</style>
        <div className="core-jarvis-status" role="status" style={{ color: CORE_STATUS[tone] }}>{statusLine(state, locale)}</div>
        <Focusable className="core-jarvis-ask" flow-children="row">
          {input}
          <DialogButton onClick={send} disabled={!draft.trim() || pending}>{text.send}</DialogButton>
        </Focusable>
        <Focusable className="core-jarvis-buttons" flow-children="row">
          <DialogButton onClick={askScreen} disabled={state?.screenHelper !== true}>{text.screen}</DialogButton>
          <DialogButton onClick={talk} style={listening === "talk" ? { background: CRIMSON, color: WHITE } : undefined}>{listening === "talk" ? text.talkStop : text.talk}</DialogButton>
        </Focusable>
        {listening && <div className="core-jarvis-live" role="status"><span className="core-jarvis-dot" />{text.listening}</div>}
        {(pending || live?.working) && <div className="core-jarvis-hint">{text.thinking}</div>}
        {tapLine && <div className="core-jarvis-hint">{tapLine}</div>}
        {state?.screenHelper !== true && <div className="core-jarvis-hint">{text.helperOff}</div>}
        {lastAnswer && (
          <Focusable className="core-jarvis-turn core-jarvis-last" noFocusRing={true} onActivate={() => speak(lastAnswer)}>
            <span className="core-jarvis-who">{tabText.last}</span>{lastAnswer}
          </Focusable>
        )}
        <div className="core-jarvis-log">
          {screen && (
            <Focusable className="core-jarvis-turn core-jarvis-screen" noFocusRing={true} onActivate={() => speak(screen.text)}>
              <span className="core-jarvis-who">{text.screen}</span>{screen.text}
            </Focusable>
          )}
          {turns.map((turn, index) => (
            <Focusable key={index} className={`core-jarvis-turn ${turn.role}`} noFocusRing={true} onActivate={() => { if (turn.role === "assistant") speak(turn.text); }}>
              <span className="core-jarvis-who">{turn.role === "user" ? text.you : text.title}</span>{turn.text}
            </Focusable>
          ))}
        </div>
        <Focusable className="core-jarvis-buttons" flow-children="row">
          {screen?.more && <DialogButton onClick={more}>{text.more}</DialogButton>}
          <DialogButton onClick={dictate} style={listening === "dictate" ? { background: CRIMSON, color: WHITE } : undefined}>{listening === "dictate" ? text.dictateStop : text.dictate}</DialogButton>
          <DialogButton onClick={clear}>{text.clear}</DialogButton>
          <DialogButton onClick={openPage}>{text.page}</DialogButton>
        </Focusable>
        <div className="core-jarvis-hint">{text.hint}</div>
      </Focusable>
    );
  }

  const body = (
    <Focusable className={`core-jarvis${full ? " full" : ""}`} flow-children="column">
      <style>{style}</style>
      <div className="core-jarvis-status" role="status" style={{ color: CORE_STATUS[tone] }}>{statusLine(state, locale)}</div>
      {listening && <div className="core-jarvis-live" role="status"><span className="core-jarvis-dot" />{text.listening}</div>}
      {(pending || live?.working) && <div className="core-jarvis-hint">{text.thinking}</div>}
      <div className="core-jarvis-log">
        {turns.map((turn, index) => (
          <Focusable key={index} className={`core-jarvis-turn ${turn.role}`} noFocusRing={true} onActivate={() => { if (turn.role === "assistant") speak(turn.text); }}>
            <span className="core-jarvis-who">{turn.role === "user" ? text.you : text.title}</span>{turn.text}
          </Focusable>
        ))}
        {screen && (
          <Focusable className="core-jarvis-turn core-jarvis-screen" noFocusRing={true} onActivate={() => speak(screen.text)}>
            <span className="core-jarvis-who">{text.screen}</span>{screen.text}
          </Focusable>
        )}
      </div>
      {input}
      <Focusable className="core-jarvis-buttons" flow-children="row">
        <DialogButton onClick={send} disabled={!draft.trim() || pending}>{text.send}</DialogButton>
        <DialogButton onClick={dictate} style={listening === "dictate" ? { background: CRIMSON, color: WHITE } : undefined}>{listening === "dictate" ? text.dictateStop : text.dictate}</DialogButton>
        <DialogButton onClick={talk} style={listening === "talk" ? { background: CRIMSON, color: WHITE } : undefined}>{listening === "talk" ? text.talkStop : text.talk}</DialogButton>
        <DialogButton onClick={askScreen} disabled={state?.screenHelper !== true}>{text.screen}</DialogButton>
        {screen?.more && <DialogButton onClick={more}>{text.more}</DialogButton>}
        {lastAnswer && <DialogButton onClick={() => speak(lastAnswer)}>{text.speak}</DialogButton>}
        <DialogButton onClick={clear}>{text.clear}</DialogButton>
        {!full && <DialogButton onClick={openPage}>{text.page}</DialogButton>}
      </Focusable>
      {state?.screenHelper !== true && <div className="core-jarvis-hint">{text.helperOff}</div>}
      <div className="core-jarvis-hint">{combosLine(state, locale)}</div>
      <div className="core-jarvis-hint">{text.hint}</div>
    </Focusable>
  );
  return full ? body : (
    <PanelSection title={text.title}>
      <PanelSectionRow>{body}</PanelSectionRow>
    </PanelSection>
  );
}

export function JarvisSection({ locale }: { locale: string }) {
  return <JarvisPanel locale={locale} />;
}

// Jarvis's own Quick Access tab (Core UI phase 1). The locale follows Steam's, read when the tab renders.
export function JarvisTab({ locale }: { locale: () => string }) {
  return <div style={{ padding: "8px 12px 16px", boxSizing: "border-box" }}><JarvisPanel locale={locale()} tab={true} /></div>;
}

// The tab's icon: Core's ring in miniature, a crimson orb in a steel ring (no emoji, no glow).
export function JarvisOrbIcon() {
  return (
    <svg viewBox="0 0 24 24" width="1em" height="1em" aria-hidden="true">
      <circle cx="12" cy="12" r="9.5" fill="none" stroke="currentColor" strokeWidth="2" opacity="0.55" />
      <path d="M2.5 12a9.5 9.5 0 0 1 9.5-9.5" fill="none" stroke={CRIMSON} strokeWidth="2.4" strokeLinecap="round" />
      <circle cx="12" cy="12" r="4.2" fill={CRIMSON} />
    </svg>
  );
}

// The full page (Decky route): the same panel with the whole session, on AMOLED black.
export function JarvisPage({ locale }: { locale: () => string }) {
  return (
    <div style={{ background: BLACK, color: WHITE, minHeight: "100%", boxSizing: "border-box", padding: "56px 64px 40px", overflowY: "auto", height: "100%", position: "relative", isolation: "isolate" }}>
      <CoreStars />
      <div style={{ maxWidth: 900, margin: "0 auto" }}>
        <div style={{ fontSize: 28, fontWeight: 700, marginBottom: 12 }}>{jarvisCopy(locale()).title}</div>
        <JarvisPanel locale={locale()} full={true} />
      </div>
    </div>
  );
}
