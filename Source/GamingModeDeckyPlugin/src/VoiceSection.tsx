import { DFL, SP_REACT as React, toaster } from "./decky";
import { call } from "./controlBackend";
import { discordInstalled, micView, partyMessage, voiceCopy, type PartyAnswer, type VoiceAnswer } from "./voiceReport";

// Eternal Core: the Voice section of Quick Access > Core. A big microphone button (Core mutes the
// default Console and Communications microphones and restores each exactly), the Xbox party button
// (opens the Xbox Game Bar over Big Picture; Microsoft offers no party API) and a Discord row that
// only says whether Core's Discord plugin is installed: Decky has no supported call that opens
// another plugin's panel. The agent does the work (GamingMode.Services/MarkIIIHost.Voice.cs).
const { PanelSection, PanelSectionRow, Focusable, DialogButton } = DFL as any;

const BLACK = "#000000", CRIMSON = "#D7263D", STEEL = "#C9D1D9", GUNMETAL = "#1A1D21";

const style = `
.core-voice{background:${BLACK};border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:8px;width:100%}
.core-voice .core-voice-button{width:100%!important;min-width:0!important;max-width:none!important;box-sizing:border-box;background:${GUNMETAL}!important;color:#fff!important;border-radius:6px!important;padding:12px 14px!important;display:flex;align-items:center;gap:12px;text-align:left;box-shadow:none!important;text-shadow:none!important;filter:none!important;transform:none!important}
.core-voice .core-voice-button.gpfocus,.core-voice .core-voice-button:focus-visible{outline:2px solid ${STEEL};outline-offset:-2px}
.core-voice .core-voice-button.muted{background:${CRIMSON}!important}
.core-voice-icon{width:28px;height:28px;flex:0 0 auto}
.core-voice-text{display:flex;flex-direction:column;gap:2px;min-width:0}
.core-voice-label{font-size:15px;font-weight:650;line-height:1.2}
.core-voice-state{font-size:12px;color:${STEEL};line-height:1.3}
.core-voice .muted .core-voice-state{color:#fff}
.core-voice-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${STEEL}}
.core-voice-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;display:flex;justify-content:space-between;align-items:baseline;gap:10px}
.core-voice-row-title{font-size:13px;color:#fff;font-weight:600}
.core-voice-row-value{font-size:12px;color:${STEEL};text-align:right}
.core-voice-note{padding:0 12px;font-size:12px;color:${CRIMSON}}
`;

// Lucide Mic and MicOff (ISC; see quick_settings/THIRD-PARTY-NOTICES.md).
function MicIcon({ off }: { off: boolean }) {
  return (
    <svg className="core-voice-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {off ? <>
        <path d="M2 2l20 20" /><path d="M18.89 13.23A7.12 7.12 0 0 0 19 12v-2" /><path d="M5 10v2a7 7 0 0 0 12 5" />
        <path d="M15 9.34V5a3 3 0 0 0-5.68-1.33" /><path d="M9 9v3a3 3 0 0 0 5.12 2.12" /><path d="M12 19v3" />
      </> : <>
        <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" /><path d="M19 10v2a7 7 0 0 1-14 0v-2" /><path d="M12 19v3" />
      </>}
    </svg>
  );
}

// Lucide Users (ISC).
function PartyIcon() {
  return (
    <svg className="core-voice-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M22 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" />
    </svg>
  );
}

function readDiscord(): boolean {
  try { return discordInstalled((window as any).DeckyPluginLoader?.deckyState?.publicState?.()); }
  catch { return false; }
}

export function VoiceSection({ locale }: { locale: string }) {
  const [answer, setAnswer] = React.useState<VoiceAnswer | null | undefined>(undefined);
  const [busy, setBusy] = React.useState(false);
  const [note, setNote] = React.useState("");
  const [discord, setDiscord] = React.useState(readDiscord);
  const text = voiceCopy(locale);
  React.useEffect(() => {
    let alive = true;
    const load = () => {
      call<[], VoiceAnswer>("core_voice_state")
        .then(value => { if (alive) setAnswer(value && value.mic ? value : null); })
        .catch(() => { if (alive) setAnswer(null); });
      if (alive) setDiscord(readDiscord());
    };
    load();
    const timer = window.setInterval(load, 4000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);

  const toggleMic = async () => {
    setBusy(true);
    try { const value = await call<[string], VoiceAnswer>("core_voice_mic", "toggle"); if (value && value.mic) setAnswer(value); }
    catch { /* The next poll shows the actual state. */ }
    finally { setBusy(false); }
  };

  const openParty = async () => {
    setNote("");
    if (answer?.gameBar?.installed === false) { setNote(text.notInstalled); return; }
    // Quick Access closes first: the Game Bar opens over Big Picture, and coming back lands on it.
    try { (DFL as any).Navigation?.CloseSideMenus?.(); } catch { /* Still try the bar. */ }
    let result: PartyAnswer | undefined;
    try { result = await call<[], PartyAnswer>("core_voice_party"); } catch { result = undefined; }
    const message = partyMessage(result, locale);
    if (message) toaster.toast({ title: text.party, body: message });
  };

  const mic = micView(answer, locale);
  return (
    <PanelSection title={text.title}>
      <style>{style}</style>
      <PanelSectionRow>
        <Focusable className="core-voice" flow-children="column">
          <DialogButton className={`core-voice-button${mic.muted ? " muted" : ""}`} disabled={busy || !answer || mic.state === "none"}
            onClick={toggleMic} aria-pressed={mic.muted}>
            <MicIcon off={mic.muted} />
            <span className="core-voice-text">
              <span className="core-voice-label">{answer === null ? text.agentDown : mic.button}</span>
              <span className="core-voice-state" role="status">{answer === undefined ? "…" : mic.line}</span>
            </span>
          </DialogButton>
          {mic.detail && <div className="core-voice-hint">{mic.detail}</div>}
          <DialogButton className="core-voice-button" disabled={!answer} onClick={openParty}>
            <PartyIcon />
            <span className="core-voice-text">
              <span className="core-voice-label">{text.party}</span>
            </span>
          </DialogButton>
          <div className="core-voice-hint">{text.partyHint}</div>
          {note && <div className="core-voice-note" role="alert">{note}</div>}
          <div className="core-voice-row">
            <span className="core-voice-row-title">{text.discord}</span>
            <span className="core-voice-row-value">{discord ? text.discordInstalled : text.discordMissing}</span>
          </div>
        </Focusable>
      </PanelSectionRow>
    </PanelSection>
  );
}
