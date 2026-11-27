import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { CORE } from "./corePalette";
import { automationLines, batchTwoCopy, bridgeLine, sleepLine, type AutomationsAnswer } from "./batchTwoReport";

// Eternal Core batch 2 (B17, B8, B13): a read-only summary in Quick Access > Core: the automations that are on, the
// Home Assistant bridge and sleep after streaming. Editing happens in the desktop app (Gaming Mode > Automations).
const { PanelSection, PanelSectionRow, Focusable } = DFL as any;
const { black: BLACK, steel: STEEL, gunmetal: GUNMETAL, white: WHITE, crimson: CRIMSON, hint: HINT } = CORE;

const style = `
.core-autos{background:transparent;border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:6px;width:100%}
.core-autos-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;display:flex;justify-content:space-between;align-items:baseline;gap:10px;outline:none}
.core-autos-row.gpfocus,.core-autos-row:focus-visible{outline:2px solid ${CRIMSON};outline-offset:-2px}
.core-autos-title{font-size:13px;color:${WHITE};font-weight:600;white-space:nowrap}
.core-autos-value{font-size:12px;color:${STEEL};text-align:right}
.core-autos-rule{padding:0 12px;font-size:12px;line-height:1.45;color:${STEEL}}
.core-autos-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${HINT}}
`;

export function AutomationsSection({ locale }: { locale: string }) {
  const text = batchTwoCopy(locale);
  const [answer, setAnswer] = React.useState<AutomationsAnswer | null | undefined>(undefined);
  React.useEffect(() => {
    let alive = true;
    const load = () => {
      call<[], AutomationsAnswer>("core_automations")
        .then(value => { if (alive) setAnswer(value && Array.isArray(value.rules) ? value : null); })
        .catch(() => { if (alive) setAnswer(null); });
    };
    load();
    const timer = window.setInterval(load, 15000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  if (!answer) return null;
  const lines = automationLines(answer, locale);
  return (
    <PanelSection title={text.autos}>
      <style>{style}</style>
      <PanelSectionRow>
        <Focusable className="core-autos" flow-children="column">
          {(lines.length ? lines : [text.none]).map((line, index) => <div key={index} className="core-autos-rule">{line}</div>)}
          <Focusable className="core-autos-row" noFocusRing={true} onActivate={() => { /* Information only. */ }}>
            <span className="core-autos-title">{text.ha}</span>
            <span className="core-autos-value" role="status">{bridgeLine(answer, locale)}</span>
          </Focusable>
          <Focusable className="core-autos-row" noFocusRing={true} onActivate={() => { /* Information only. */ }}>
            <span className="core-autos-title">{text.sleep}</span>
            <span className="core-autos-value">{sleepLine(answer, locale)}</span>
          </Focusable>
          <div className="core-autos-hint">{text.edit}</div>
        </Focusable>
      </PanelSectionRow>
    </PanelSection>
  );
}
