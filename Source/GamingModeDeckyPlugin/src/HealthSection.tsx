import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { healthCopy, healthLines, healthSummary, type HealthAnswer, type HealthLine } from "./healthReport";

// Eternal Core: the Health section of Quick Access > Core. One line for the whole setup, then a
// row each for the home server's backup, restore check, drive and RomM (from the server's
// core-status.json), Windows restart, update night, the TV, the PC's disk and Core's version.
// The agent does the gathering (GamingMode.Services/MarkIIIHost.Health.cs); this only renders.
// Rows are focusable: D-pad moves between them, A shows or hides a row's details, and a row with
// an active alert offers "Dismiss alert" (it comes back only if the problem changes).
const { PanelSection, PanelSectionRow, ButtonItem, Focusable } = DFL as any;

const BLACK = "#000000", CRIMSON = "#D7263D", STEEL = "#C9D1D9", GUNMETAL = "#1A1D21";

const style = `
.core-health{background:${BLACK};border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:6px;width:100%}
.core-health-summary{padding:8px 12px;font-size:14px;font-weight:650;color:#fff}
.core-health-summary.problem{color:${CRIMSON}}
.core-health-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;outline:none;box-shadow:none;text-shadow:none}
.core-health-row.gpfocus,.core-health-row:focus-visible{outline:2px solid ${STEEL};outline-offset:-2px}
.core-health-head{display:flex;justify-content:space-between;align-items:baseline;gap:10px}
.core-health-title{font-size:13px;color:#fff;font-weight:600;white-space:nowrap}
.core-health-value{font-size:12px;color:${STEEL};text-align:right;overflow:hidden;text-overflow:ellipsis}
.core-health-row.attention .core-health-value{color:${CRIMSON};font-weight:600}
.core-health-detail{margin-top:6px;font-size:11px;line-height:1.45;color:${STEEL};word-break:break-word}
`;

function Row({ line, open, onToggle }: { line: HealthLine; open: boolean; onToggle: () => void }) {
  return (
    <Focusable className={`core-health-row ${line.level}`} onActivate={onToggle}
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
  const lines = answer ? healthLines(answer, locale) : [];
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
        </Focusable>
      </PanelSectionRow>
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
