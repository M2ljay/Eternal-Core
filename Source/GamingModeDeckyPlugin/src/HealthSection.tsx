import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { CORE, CORE_STATUS } from "./corePalette";
import { healthCopy, healthLines, healthSummary, type HealthAction, type HealthAnswer, type HealthLine } from "./healthReport";

// Eternal Core: the Health section of Quick Access > Core. One line for the whole setup, then a
// row each for the home server's backup, restore check, drive and RomM (from the server's
// core-status.json), Windows restart, update night, the save backup before it (Ludusavi), Decky and the
// plugins whose logs report errors, the TV, streaming (Stream mode), the microphone, the PC's disk and
// Core's version (batch 1: docs/eternal/BATCH-1.md).
// The agent does the gathering (GamingMode.Services/MarkIIIHost.Health.cs); this only renders.
// Rows are focusable: D-pad moves between them, A shows or hides a row's details, and a row with
// an active alert offers "Dismiss alert" (it comes back only if the problem changes).
// Console guards (CONSOLE-GUARDS.md): a row that needs a fix offers its one button under the list
// ("Restore display settings", "Switch sound back"...); an open row also offers its other actions
// ("Save current display as normal"...). The agent runs them (POST /health/action) and answers with
// the new rows.
const { PanelSection, PanelSectionRow, ButtonItem, Focusable } = DFL as any;

const { black: BLACK, crimson: CRIMSON, crimsonText: CRIMSON_TEXT, steel: STEEL, gunmetal: GUNMETAL, white: WHITE } = CORE;

const style = `
.core-health{background:transparent;border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:6px;width:100%}
.core-health-summary{padding:8px 12px;font-size:14px;font-weight:650;color:${WHITE}}
.core-health-summary.problem{color:${CRIMSON_TEXT}}
.core-health-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;outline:none;box-shadow:none;text-shadow:none}
.core-health-row.gpfocus,.core-health-row:focus-visible{outline:2px solid ${CRIMSON};outline-offset:-2px}
.core-health-head{display:flex;justify-content:space-between;align-items:baseline;gap:10px}
.core-health-title{font-size:13px;color:${WHITE};font-weight:600;white-space:nowrap}
.core-health-value{font-size:12px;color:${STEEL};text-align:right;overflow:hidden;text-overflow:ellipsis}
.core-health-row.ok .core-health-value{color:${CORE_STATUS.neutral}}
.core-health-row.info .core-health-value{color:${CORE_STATUS.warning}}
.core-health-row.attention .core-health-value,.core-health-row.crimson .core-health-value{color:${CORE_STATUS.error};font-weight:600}
.core-health-detail{margin-top:6px;font-size:11px;line-height:1.45;color:${STEEL};word-break:break-word}
.core-health-result{padding:0 12px 6px;font-size:12px;color:${STEEL}}
.core-health-result.problem{color:${CORE_STATUS.error}}
`;

type ActionAnswer = { ok?: boolean; action?: string; result?: string; health?: HealthAnswer };

function Row({ line, open, onToggle }: { line: HealthLine; open: boolean; onToggle: () => void }) {
  return (
    <Focusable className={`core-health-row ${line.level}${line.tone ? " " + line.tone : ""}`} onActivate={onToggle}
      aria-expanded={open} noFocusRing={true}>
      <div className="core-health-head">
        <span className="core-health-title">{line.title}</span>
        <span className="core-health-value">{line.value}</span>
      </div>
      {open && line.details.length > 0 && <div className="core-health-detail">{line.details.map((detail, index) => <div key={index}>{detail}</div>)}</div>}
    </Focusable>
  );
}

export function HealthSection({ locale }: { locale: string }) {
  const [answer, setAnswer] = React.useState<HealthAnswer | null | undefined>(undefined);
  const [open, setOpen] = React.useState<Record<string, boolean>>({});
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ text: string; problem: boolean } | null>(null);
  const text = healthCopy(locale);
  React.useEffect(() => {
    let alive = true;
    const load = () => {
      call<[], HealthAnswer>("core_health")
        .then(value => { if (alive) setAnswer(value && Array.isArray(value.rows) ? value : null); })
        .catch(() => { if (alive) setAnswer(null); });
    };
    load();
    const timer = window.setInterval(load, 60000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  const dismiss = async (id: string) => {
    setBusy(true);
    try { const value = await call<[string], HealthAnswer>("core_health_dismiss", id); if (value && Array.isArray(value.rows)) setAnswer(value); }
    catch { /* The next poll shows the current state. */ }
    finally { setBusy(false); }
  };
  const act = async (action: HealthAction) => {
    setBusy(true);
    setResult(null);
    try {
      const value = await call<[string], ActionAnswer>("core_health_action", action.id);
      if (value?.health && Array.isArray(value.health.rows)) setAnswer(value.health);
      const ok = value?.ok === true;
      setResult({ text: ok ? `${action.label}: ${text.aDone}` : value?.result === "not-while-playing" ? text.aNotWhilePlaying : `${action.label}: ${text.aFailed}`, problem: !ok });
    }
    catch { setResult({ text: `${action.label}: ${text.aFailed}`, problem: true }); }
    finally { setBusy(false); }
  };
  const lines = answer ? healthLines(answer, locale) : [];
  // Fixes for rows that need them, then the other actions of the rows that are open.
  const buttons = lines.flatMap(line => (line.actions ?? [])
    .filter(action => action.fix ? line.level === "attention" || line.level === "info" : !!open[line.id])
    .map(action => ({ line, action })));
  const summary = answer ? healthSummary(answer, locale) : undefined;
  return (
    <PanelSection title={text.title}>
      <style>{style}</style>
      <PanelSectionRow>
        <Focusable className="core-health" flow-children="column">
          <div className={`core-health-summary${summary && summary.attention > 0 ? " problem" : ""}`} role="status">
            {answer === undefined ? text.checking : answer === null ? text.agentDown : summary!.line}
          </div>
          {lines.map(line => (
            <Row key={line.id} line={line} open={!!open[line.id]} onToggle={() => setOpen(state => ({ ...state, [line.id]: !state[line.id] }))} />
          ))}
          {result && <div className={`core-health-result${result.problem ? " problem" : ""}`} role="status">{result.text}</div>}
        </Focusable>
      </PanelSectionRow>
      {buttons.map(({ line, action }) => (
        <PanelSectionRow key={`action-${line.id}-${action.id}`}>
          <ButtonItem layout="below" disabled={busy} onClick={() => act(action)}>
            {action.label}
          </ButtonItem>
        </PanelSectionRow>
      ))}
      {lines.filter(line => line.alertId).map(line => (
        <PanelSectionRow key={`dismiss-${line.alertId}`}>
          <ButtonItem layout="below" disabled={busy} onClick={() => dismiss(line.alertId!)}>
            {`${text.dismiss}: ${line.title}`}
          </ButtonItem>
        </PanelSectionRow>
      ))}
    </PanelSection>
  );
}
