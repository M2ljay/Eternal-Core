import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { streamCopy, streamView, type StreamAnswer } from "./streamReport";

// Eternal Core: the Stream mode section of Quick Access > Core. One row says whether Stream mode is on
// (and to which device the PC streams, when known), a toggle forces it on ("Keep TV out of it"), and
// short lines say what happens to the PC's sound and whether Apollo's virtual display is active. While
// Stream mode is on, Core sends nothing to the TV (GamingMode.Services/MarkIIIHost.Stream.cs).
const { PanelSection, PanelSectionRow, Focusable, ToggleField } = DFL as any;

const BLACK = "#000000", CRIMSON = "#D7263D", STEEL = "#C9D1D9", GUNMETAL = "#1A1D21";

const style = `
.core-stream{background:${BLACK};border-radius:8px;padding:6px 0;display:flex;flex-direction:column;gap:6px;width:100%}
.core-stream-row{background:${GUNMETAL};border-radius:6px;padding:9px 12px;display:flex;justify-content:space-between;align-items:baseline;gap:10px;outline:none;box-shadow:none;text-shadow:none}
.core-stream-row.gpfocus,.core-stream-row:focus-visible{outline:2px solid ${STEEL};outline-offset:-2px}
.core-stream-row.on{border-left:3px solid ${CRIMSON}}
.core-stream-title{font-size:13px;color:#fff;font-weight:600;white-space:nowrap}
.core-stream-value{font-size:12px;color:${STEEL};text-align:right;overflow:hidden;text-overflow:ellipsis}
.core-stream-row.on .core-stream-value{color:${CRIMSON};font-weight:600}
.core-stream-hint{padding:0 12px;font-size:11px;line-height:1.45;color:${STEEL}}
.core-stream-settings{display:flex;flex-direction:column;gap:6px;padding:0 4px}
`;

export function StreamSection({ locale }: { locale: string }) {
  const [answer, setAnswer] = React.useState<StreamAnswer | null | undefined>(undefined);
  const [busy, setBusy] = React.useState(false);
  const text = streamCopy(locale);
  React.useEffect(() => {
    let alive = true;
    const load = () => {
      call<[], StreamAnswer>("core_stream_state")
        .then(value => { if (alive) setAnswer(value && typeof value.active === "boolean" ? value : null); })
        .catch(() => { if (alive) setAnswer(null); });
    };
    load();
    const timer = window.setInterval(load, 4000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);

  const keepTvOut = async (on: boolean) => {
    setBusy(true);
    try { const value = await call<[boolean], StreamAnswer>("core_stream_keep_tv_out", on); if (value && typeof value.active === "boolean") setAnswer(value); }
    catch { /* The next poll shows the actual state. */ }
    finally { setBusy(false); }
  };

  const view = streamView(answer, locale);
  return (
    <PanelSection title={text.title}>
      <style>{style}</style>
      <PanelSectionRow>
        <Focusable className="core-stream" flow-children="column">
          <Focusable className={`core-stream-row${view.state === "on" ? " on" : ""}`} noFocusRing={true} onActivate={() => { /* Information only. */ }}>
            <span className="core-stream-title">{text.title}</span>
            <span className="core-stream-value" role="status">{view.line}</span>
          </Focusable>
          {view.detail && <div className="core-stream-hint">{view.detail}</div>}
          {view.audio && <div className="core-stream-hint">{view.audio}</div>}
          <div className="core-stream-settings">
            <ToggleField label={text.keep} description={text.keepHint} bottomSeparator="none" checked={view.forced}
              disabled={busy || !answer} onChange={(enabled: boolean) => void keepTvOut(enabled)} />
          </div>
          {view.apollo && <div className="core-stream-hint">{view.apollo}</div>}
        </Focusable>
      </PanelSectionRow>
    </PanelSection>
  );
}
