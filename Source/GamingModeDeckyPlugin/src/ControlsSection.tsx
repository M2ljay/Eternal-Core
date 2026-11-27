import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { CORE } from "./corePalette";
import { controlsCopy, controlsRows, fillControls } from "./emulatorHotkeys";

// Eternal Core 3.3.5: Quick Access > Core > Controls. The one controller reference (the shared emulator hotkeys,
// exiting through Steam, the back paddles, Jarvis), Core's Exit for a game Steam lost track of, and the
// per-launcher switches for closing store launchers after a game (GamingMode.Services/MarkIIIHost.Emulators.cs).
const { PanelSection, PanelSectionRow, Focusable, ToggleField, ButtonItem } = DFL as any;

const { crimson: CRIMSON, steel: STEEL, gunmetal: GUNMETAL, white: WHITE } = CORE;

const style = `
.core-controls{display:flex;flex-direction:column;gap:4px;width:100%;padding:6px 0}
.core-controls-head{font-size:11px;color:${STEEL};padding:6px 12px 2px;text-transform:uppercase;letter-spacing:.04em}
.core-controls-row{background:${GUNMETAL};border-radius:6px;padding:7px 12px;display:flex;justify-content:space-between;align-items:baseline;gap:10px;outline:none}
.core-controls-row.gpfocus,.core-controls-row:focus-visible{outline:2px solid ${CRIMSON};outline-offset:-2px}
.core-controls-label{font-size:12px;color:${WHITE}}
.core-controls-buttons{font-size:12px;color:${STEEL};text-align:right}
.core-controls-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${STEEL}}
`;

type Launcher = { id: string; name: string; on: boolean };
type ControlsAnswer = { launchers?: Launcher[]; untracked?: string | null };

export function ControlsSection({ locale }: { locale: string }) {
  const text = controlsCopy(locale);
  const [answer, setAnswer] = React.useState<ControlsAnswer | null>(null);
  const [busy, setBusy] = React.useState(false);
  React.useEffect(() => {
    let alive = true;
    const load = () => call<[], ControlsAnswer>("core_emulators_state").then(value => { if (alive && value && Array.isArray(value.launchers)) setAnswer(value); }).catch(() => {});
    load();
    const timer = window.setInterval(load, 6000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  const toggle = async (id: string, on: boolean) => {
    setBusy(true);
    try { const value = await call<[string, boolean], ControlsAnswer>("core_launcher_auto_close", id, on); if (value && Array.isArray(value.launchers)) setAnswer(value); }
    catch { /* The next poll shows the actual state. */ }
    finally { setBusy(false); }
  };
  const exitNow = async () => {
    setBusy(true);
    try { await call<[string], unknown>("core_health_action", "game-exit-now"); } catch { } finally { setBusy(false); }
  };
  const rows = controlsRows(locale);
  const untracked = typeof answer?.untracked === "string" && answer.untracked ? answer.untracked.slice(0, 64) : "";
  return (
    <PanelSection title={text.title}>
      <style>{style}</style>
      {untracked && (
        <PanelSectionRow>
          <div className="core-controls-hint">{fillControls(text.exitUntracked, { title: untracked })}</div>
          <ButtonItem layout="below" disabled={busy} onClick={() => void exitNow()}>{fillControls(text.exitNow, { title: untracked })}</ButtonItem>
        </PanelSectionRow>
      )}
      <PanelSectionRow>
        <Focusable className="core-controls" flow-children="column">
          {(["emulators", "other"] as const).map(group => (
            <React.Fragment key={group}>
              <div className="core-controls-head">{group === "emulators" ? text.emulators : text.other}</div>
              {rows.filter(row => row.group === group).map(row => (
                <Focusable key={row.label} className="core-controls-row" noFocusRing={true} onActivate={() => { /* Reference only. */ }}>
                  <span className="core-controls-label">{row.label}</span>
                  <span className="core-controls-buttons">{row.buttons}</span>
                </Focusable>
              ))}
            </React.Fragment>
          ))}
        </Focusable>
      </PanelSectionRow>
      {answer?.launchers && (
        <PanelSectionRow>
          <div className="core-controls-head">{text.launchers}</div>
          <div className="core-controls-hint">{text.launchersHelp}</div>
          {answer.launchers.map(launcher => (
            <ToggleField key={launcher.id} label={launcher.name} bottomSeparator="none" checked={launcher.on} disabled={busy}
              onChange={(enabled: boolean) => void toggle(launcher.id, enabled)} />
          ))}
        </PanelSectionRow>
      )}
    </PanelSection>
  );
}
