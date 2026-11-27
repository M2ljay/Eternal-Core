import { DFL, SP_REACT as React, toaster } from "./decky";
import { CORE } from "./corePalette";
import { batchTwoCopy, typingStatus } from "./batchTwoReport";
import type { SwipeTyping } from "./swipeTyping";

// Eternal Core batch 2 (B7): swipe typing in Quick Access > Core. The switch is Core's setting (also in the desktop
// app); private mode, the typing log, the keyboard's own settings and "forget everything" are VaporType's
// (src/swipeTyping.ts). The keyboard's own settings panel is VaporType's and in English.
const { PanelSection, PanelSectionRow, Focusable, ToggleField, ButtonItem, Navigation } = DFL as any;
const { black: BLACK, steel: STEEL, gunmetal: GUNMETAL, white: WHITE, crimson: CRIMSON, crimsonText: CRIMSON_TEXT, hint: HINT } = CORE;

let runtime: SwipeTyping | undefined;
export function setSwipeTypingRuntime(value: SwipeTyping | undefined) { runtime = value; }

const style = `
.core-typing{background:${BLACK};border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:6px;width:100%}
.core-typing-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;display:flex;justify-content:space-between;align-items:baseline;gap:10px;outline:none}
.core-typing-row.gpfocus,.core-typing-row:focus-visible{outline:2px solid ${CRIMSON};outline-offset:-2px}
.core-typing-title{font-size:13px;color:${WHITE};font-weight:600;white-space:nowrap}
.core-typing-value{font-size:12px;color:${STEEL};text-align:right}
.core-typing-value.paused{color:${CRIMSON_TEXT}}
.core-typing-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${HINT}}
`;

export function SwipeTypingSection({ locale }: { locale: string }) {
  const text = batchTwoCopy(locale);
  const [, setTick] = React.useState(0);
  const [confirm, setConfirm] = React.useState(false);
  const [forgetting, setForgetting] = React.useState(false);
  React.useEffect(() => {
    const timer = window.setInterval(() => setTick((value: number) => value + 1), 1000);
    return () => window.clearInterval(timer);
  }, []);
  React.useEffect(() => {
    if (!confirm) return;
    const timer = window.setTimeout(() => setConfirm(false), 4000);
    return () => window.clearTimeout(timer);
  }, [confirm]);
  if (!runtime) return null;
  const typing = runtime;
  const status = typing.status();
  const running = typing.running();
  const forget = async () => {
    if (forgetting) return;
    if (!confirm) { setConfirm(true); return; }
    setConfirm(false);
    setForgetting(true);
    const ok = await typing.forgetEverything().catch(() => false);
    setForgetting(false);
    toaster.toast({ title: "Core", body: ok ? text.forgotOk : text.forgotFail });
  };
  const openSettings = async () => {
    try { Navigation?.CloseSideMenus?.(); } catch { /* The menu closes itself on the keyboard. */ }
    toaster.toast({ title: "Core", body: text.settingsToast });
    if (!(await typing.openSettingsWhenKeyboardOpens(20000))) toaster.toast({ title: "Core", body: text.noKeyboard });
  };
  return (
    <PanelSection title={text.typing}>
      <style>{style}</style>
      <PanelSectionRow>
        <Focusable className="core-typing" flow-children="column">
          <Focusable className="core-typing-row" noFocusRing={true} onActivate={() => { /* Information only. */ }}>
            <span className="core-typing-title">{text.typing}</span>
            <span className={`core-typing-value${status.state === "paused" ? " paused" : ""}`} role="status">{typingStatus(status.state, locale, status.error ?? undefined)}</span>
          </Focusable>
          <ToggleField label={text.typing} description={text.typingHint} bottomSeparator="none" checked={typing.enabled()}
            disabled={status.state === "loading" && !typing.enabled()} onChange={(on: boolean) => void typing.setEnabled(on)} />
          {running && <ToggleField label={text.private} description={text.privateHint} bottomSeparator="none"
            checked={typing.settingsValue("privateMode") === true} onChange={(on: boolean) => typing.setSetting("privateMode", on)} />}
          {running && <ToggleField label={text.log} description={text.logHint} bottomSeparator="none"
            checked={typing.settingsValue("typingLog") === true} onChange={(on: boolean) => typing.setSetting("typingLog", on)} />}
          {running && <ButtonItem layout="below" bottomSeparator="none" onClick={() => void openSettings()}>{text.settings}</ButtonItem>}
          <ButtonItem layout="below" bottomSeparator="none" disabled={forgetting} onClick={() => void forget()}>
            {forgetting ? text.forgetting : confirm ? text.forgetConfirm : text.forget}
          </ButtonItem>
          <div className="core-typing-hint">{text.english}</div>
          <div className="core-typing-hint">{text.credit}</div>
        </Focusable>
      </PanelSectionRow>
    </PanelSection>
  );
}
