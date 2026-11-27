import { DFL, SP_REACT as React, toaster } from "./decky";
import { call } from "./controlBackend";
import { CORE, CORE_STATUS } from "./corePalette";
import { combosLine, errorLine, historyLines, jarvisCopy, statusLine, statusTone, type JarvisState } from "./jarvisReport";
import type { JarvisRuntime } from "./jarvisRuntime";

// Eternal Core: Jarvis in Quick Access > Core and on its own full page (docs/eternal/JARVIS.md). Type with Steam's
// keyboard (swipe typing applies), dictate into the field, talk to Jarvis, or ask about the screen. The agent does the
// work (MarkIIIHost.Jarvis.cs); the combos and push-to-talk flow are jarvisRuntime.ts. Nothing is sent until asked.
const { PanelSection, PanelSectionRow, Focusable, TextField, DialogButton, Navigation } = DFL as any;
const { black: BLACK, steel: STEEL, gunmetal: GUNMETAL, gunmetalRaised: RAISED, white: WHITE, crimson: CRIMSON, crimsonText: CRIMSON_TEXT, hint: HINT, text: TEXT } = CORE;

export const JARVIS_ROUTE = "/core-jarvis";

let runtime: JarvisRuntime | undefined;
export function setJarvisRuntime(value: JarvisRuntime | undefined) { runtime = value; }

const style = `
.core-jarvis{background:${BLACK};border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:8px;width:100%}
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

export function JarvisPanel({ locale, full = false }: { locale: string; full?: boolean }) {
  const text = jarvisCopy(locale);
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
    if (answer?.text) setDraft("");
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
      <TextField value={draft} label={text.placeholder} onChange={(event: any) => setDraft(String(event?.target?.value ?? "").slice(0, 2000))} />
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

// The full page (Decky route): the same panel with the whole session, on AMOLED black.
export function JarvisPage({ locale }: { locale: () => string }) {
  return (
    <div style={{ background: BLACK, color: WHITE, minHeight: "100%", boxSizing: "border-box", padding: "56px 64px 40px", overflowY: "auto", height: "100%" }}>
      <div style={{ maxWidth: 900, margin: "0 auto" }}>
        <div style={{ fontSize: 28, fontWeight: 700, marginBottom: 12 }}>{jarvisCopy(locale()).title}</div>
        <JarvisPanel locale={locale()} full={true} />
      </div>
    </div>
  );
}
