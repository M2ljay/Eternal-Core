import { registerQamVisibilityDocument } from './qamTabVisibility';
import { call } from "./controlBackend";
import { CORE } from "./corePalette";
import { DFL, SP_REACT as React, toaster } from "./decky";
import { TbPower, TbDeviceTv, TbVolume, TbDeviceGamepad2, TbSettings, TbRestore, TbGauge, TbBolt, TbEye, TbDownload, TbChevronRight } from "react-icons/tb";
import { controlLocale, controlStatus } from "./controlCenterLocale";
import { normalizeControlPreferences, visibleControlTabs, TAB_GROUP, type ControlPreferences, type ControlTab, type CoreGroup } from "./controlCenterState";
import { topbarDateCopy, topbarDateOptions } from "./topbarDateLocale";
import { QuickSettingsContent } from "./quickSettings";
import { ControlSection } from "./ControlSection";
import { getDeckyHostSnapshot, subscribeDeckyHost, setDeckyHostEnabled, setDeckyNativeHidden } from "./deckyHost";
import { getOnboardingExtraCopy } from "./onboardingLocale";
import { registerOnboardingQamDocument, replayPlayhubOnboarding } from "./onboardingIntegration";
import { useOnboardingControlShowcase } from "./onboardingControlShowcase";
import { reportPlayhubOnboardingAction } from "./onboardingRuntime";
import { DeviceSettings } from "./DeviceSettings";
import { useControlSections } from "./useControlSections";
import { quickSettingsGroups } from "./quickSettings/groups";
import { MsiChargeLimit } from "./quickSettings/MsiChargeLimit";

import { QamSettings } from "./QamSettings";
import { coreQamCopy } from "./coreQamLocale";
import { CoreUiSection } from "./CoreUiSection";
import { useStoreDownloadsRunning } from "./storeDownloadMenu";

const { Focusable, DialogButton, ToggleField, DropdownItem, ScrollPanel, GamepadButton } = DFL as any;
let prefs = normalizeControlPreferences({});
let loaded = false;
let loading: Promise<void> | undefined;
let write = Promise.resolve();
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };

function TopbarDateSettings({ state, update, locale }: { state: ControlPreferences; update: (next: ControlPreferences, locale: string) => void; locale: string }) {
  const text = topbarDateCopy(locale);
  return <Focusable flow-children="column" style={{ padding: "8px 0", display: "flex", flexDirection: "column", gap: 10 }}>
    <ToggleField label={text.dateLabel} description={text.dateDescription} bottomSeparator="none"
      checked={state.topbarDateEnabled} onChange={(enabled: boolean) => update({ ...state, topbarDateEnabled: enabled }, locale)} />
    <ToggleField bottomSeparator="none" label={text.leftLabel} description={text.leftDescription} checked={state.topbarClockLeft} onChange={(enabled: boolean) => update({ ...state, topbarClockLeft: enabled }, locale)} />
    <DropdownItem label={text.formatLabel} layout="below" bottomSeparator="none" selectedOption={state.topbarDateFormat}
      disabled={!state.topbarDateEnabled} rgOptions={topbarDateOptions(locale)}
      onChange={(option: { data: string }) => update({ ...state, topbarDateFormat: option.data }, locale)} />
  </Focusable>;
}

function OnboardingInputCapture({ active }: { active: boolean }) {
  const root = React.useRef<HTMLDivElement>(null);
  React.useLayoutEffect(() => {
    if (!active || !root.current) return;
    const button = root.current.querySelector("button") as HTMLButtonElement | null;
    const focus = () => button?.focus?.({ preventScroll: true });
    focus();
    const owner = root.current.ownerDocument.defaultView;
    const frame = owner?.requestAnimationFrame(focus);
    return () => { if (frame !== undefined) owner?.cancelAnimationFrame(frame); };
  }, [active]);
  if (!active) return null;
  const consume = (event: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); };
  return <div ref={root} data-playhub-onboarding-input-capture="true" style={{ position: "fixed", inset: 0,
    overflow: "hidden", opacity: 0.001, pointerEvents: "auto", zIndex: 2147483646 }}>
    <DialogButton tabIndex={0} onClick={(event: any) => { consume(event); reportPlayhubOnboardingAction("confirm"); }}
      onCancelButton={consume} style={{ width: "100%", height: "100%", minWidth: "100%", minHeight: "100%", padding: 0 }} />
  </div>;
}
function update(next: ControlPreferences, locale: string) {
  prefs = normalizeControlPreferences(next);
  listeners.forEach(listener => listener());
  const snapshot = prefs;
  write = write.catch(() => {}).then(async () => { await call("save_panel_preferences", snapshot); }).catch(() => {
    toaster.toast({ title: "Core", body: controlLocale(locale).saveError });
  });
}
// 3.3.11 (the approved QAMCore board): Quick Access > Core is one scrolling list. No inner icon row and no gear:
// a Quick block that is always open (Display, Sound, Performance, Power; each opens its picker or actions under it), then
// sections that open and close with A and keep their state (expandedSections): Sound, Display & graphics, Controller,
// Downloads (only while a store download runs), Look, System. Square gunmetal rows, the crimson focus bar, section
// headers with a hairline under them and a chevron. The Decky sub-tab is gone (Decky keeps Steam's own rail tab), and
// so are News and Club (Club lives on Home). Everything else the old tabs held is here.
const css = `
.ph-controls{width:100%;min-width:0;color:inherit;letter-spacing:0;padding:0 12px 24px}
.ph-controls *{box-sizing:border-box;letter-spacing:0}
.ph-controls [class*="PanelSection"]::after,.ph-controls [class*="Field"]::after,.ph-controls [class*="Field"]::before{display:none!important}
.ph-controls [class*="PanelSection"],.ph-controls [class*="Field"]{border-top:0!important;border-bottom:0!important}
.ph-controls hr,.ph-controls [role="separator"],.ph-controls [class*="FieldSeparator"],.ph-controls [class*="PanelSectionSeparator"],.ph-controls [class*="Divider"]:empty,.ph-controls [class*="divider"]:empty{display:none!important}
.ph-controls [class*="BottomSeparator"],.ph-controls [class*="WithSeparator"]{border-top:0!important;border-bottom:0!important;box-shadow:none!important}
.ph-controls [class*="BottomSeparator"]::before,.ph-controls [class*="BottomSeparator"]::after,.ph-controls [class*="WithSeparator"]::before,.ph-controls [class*="WithSeparator"]::after{display:none!important}
.ph-quick-label{margin:6px 2px 8px;font-size:11px;font-weight:600;letter-spacing:.14em!important;text-transform:uppercase;color:${CORE.hint}}
.ph-quick{display:flex;flex-direction:column;gap:4px;margin:0 0 14px}
.ph-controls .ph-quick-row{width:100%!important;min-width:0!important;max-width:100%!important;min-height:52px!important;margin:0!important;padding:0 14px!important;display:flex!important;align-items:center;gap:12px;
  background:${CORE.gunmetal}!important;color:${CORE.steel}!important;border:0!important;border-radius:0!important;box-shadow:none!important;transform:none!important;font-size:16px!important;text-align:left}
.ph-controls .ph-quick-row.gpfocus,.ph-controls .ph-quick-row:focus-visible,.ph-controls .ph-quick-row:hover{background:${CORE.gunmetalRaised}!important;color:${CORE.white}!important;box-shadow:inset 3px 0 0 ${CORE.crimson}!important}
.ph-quick-row svg{width:20px;height:20px;flex:none}
.ph-quick-row .ph-quick-value{margin-left:auto;display:inline-flex;align-items:center;gap:6px;font-size:14px;color:${CORE.hint};min-width:0;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}
.ph-quick-row .ph-quick-value svg{width:14px;height:14px}
.ph-quick-panel{padding:6px 4px 10px 14px}
.ph-power-actions{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:4px}
.ph-controls .ph-power-actions .DialogButton{min-width:0!important;height:44px!important;border-radius:0!important;background:${CORE.gunmetal}!important;color:${CORE.steel}!important;box-shadow:none!important}
.ph-controls .ph-power-actions .DialogButton.gpfocus{background:${CORE.gunmetalRaised}!important;color:${CORE.white}!important;box-shadow:inset 3px 0 0 ${CORE.crimson}!important}
.ph-control-section{width:100%;min-width:0;margin:0;border:0!important}
.ph-controls .ph-control-section-title{width:100%!important;max-width:100%!important;min-width:0!important;margin:0!important;min-height:48px!important;padding:10px 14px!important;display:flex!important;align-items:center;justify-content:space-between;gap:8px;border:0!important;
  background:transparent!important;color:${CORE.steel}!important;border-radius:0!important;transform:none!important;font-size:16px!important;white-space:normal!important}
.ph-controls .ph-control-section-title.gpfocus,.ph-controls .ph-control-section-title:focus-visible,.ph-controls .ph-control-section-title:hover{background:${CORE.gunmetalRaised}!important;color:${CORE.white}!important;box-shadow:inset 3px 0 0 ${CORE.crimson}!important}
.ph-control-section-title strong{font-weight:600;overflow-wrap:anywhere}
.ph-control-section-title>span{display:flex;gap:12px;align-items:center;text-align:left;min-width:0}
.ph-control-section-title svg{width:18px;height:18px;flex:none}
.ph-control-section-body{min-height:0;padding:4px 0 10px}
.ph-core-group>.ph-control-section>.ph-control-section-title{min-height:52px!important;box-shadow:inset 0 -1px 0 ${CORE.gunmetal}!important;font-size:17px!important}
.ph-core-group>.ph-control-section>.ph-control-section-title.gpfocus{box-shadow:inset 3px 0 0 ${CORE.crimson},inset 0 -1px 0 ${CORE.gunmetal}!important}
.ph-core-group .ph-control-section-body .ph-control-section-title{font-size:15px!important}
.ph-controls .ph-settings-action{width:100%!important;display:flex!important;align-items:center;gap:10px;white-space:normal!important;text-align:left;border-radius:0!important}
.ph-settings-action svg{flex:none;width:18px;height:18px}
.ph-controls .qsRedesign{padding:0 0 4px!important}
.ph-controls .qsCardBody{gap:12px;padding:8px 0!important}
.ph-core-subhead{margin:12px 2px 6px;font-size:11px;font-weight:600;letter-spacing:.14em!important;text-transform:uppercase;color:${CORE.hint}}
`;

// Steam's own power calls (the same the Steam menu's Power entry makes).
const steamSystem = () => (window as any).SteamClient?.System;
const powerAction = (name: "SuspendPC" | "RestartPC" | "ShutdownPC") => () => { try { steamSystem()?.[name]?.(); } catch { /* Steam keeps running. */ } };

type QuickId = "display" | "sound" | "performance" | "power";

function QuickRow({ id, icon, title, value, open, onToggle, onboarding }: {
  id: QuickId; icon: React.ReactNode; title: string; value?: string; open: boolean; onToggle: (id: QuickId) => void; onboarding?: string;
}) {
  return <DialogButton className="ph-quick-row" aria-expanded={open} onClick={() => onToggle(id)} data-playhub-onboarding={onboarding}>
    {icon}<span>{title}</span><span className="ph-quick-value">{value ? <span>{value}</span> : null}<TbChevronRight /></span>
  </DialogButton>;
}

// The group ids each old tab showed (the onboarding tour still names tabs; it is pointed at their groups).
const groupFor = (tab: ControlTab): CoreGroup | undefined => TAB_GROUP[tab];

export function ControlCenter({ locale, origin, session, store, controller, settings, downloads, gameAppId = 0, frameTiming, gamingMode = false }: {
  locale: string; origin: "qam" | "decky"; session: React.ReactNode; store: React.ReactNode; controller: React.ReactNode; settings: React.ReactNode; downloads?: React.ReactNode; homeTitle?: string; gameAppId?: number; frameTiming?: React.ReactNode; gamingMode?: boolean;
}) {
  const state = React.useSyncExternalStore(subscribe, () => prefs);
  const onboardingRoot = React.useRef<HTMLDivElement>(null);
  React.useLayoutEffect(() => {
    if (origin === "qam" && onboardingRoot.current) {
      const doc=onboardingRoot.current.ownerDocument;
      const stopOnboarding=registerOnboardingQamDocument(doc);
      const stopVisibility=registerQamVisibilityDocument(doc);
      return ()=>{stopVisibility();stopOnboarding();};
    }
  }, [origin]);
  const [ready, setReady] = React.useState(loaded);
  const [quick, setQuick] = React.useState<QuickId | undefined>();
  const copy = controlLocale(locale);
  const text = coreQamCopy(locale);
  const deckyHost = React.useSyncExternalStore(subscribeDeckyHost, getDeckyHostSnapshot);
  const downloadsRunning = useStoreDownloadsRunning();
  React.useEffect(() => {
    // 3.3.11: no Decky inside Core: Decky's own tab on Steam's rail stays visible and nothing is hosted here.
    if (origin === "qam" && ready) { setDeckyNativeHidden(false); setDeckyHostEnabled(false); }
  }, [origin, ready]);
  React.useEffect(() => {
    let alive = true;
    if (!loading) loading = call<[], unknown>("get_panel_preferences").then(value => {
      prefs = normalizeControlPreferences(value); loaded = true; listeners.forEach(listener => listener());
    }).catch(() => { loading = undefined; }).finally(() => { if (alive) setReady(true); });
    else void loading.finally(() => { if (alive) setReady(true); });
    return () => { alive = false; };
  }, []);
  const visible = visibleControlTabs(state, false);
  const { active, customizing, showcasing } = useOnboardingControlShowcase(origin === "qam", ready, visible, state.active, false);
  const updateExpanded = React.useCallback((change: (current: string[]) => string[]) => {
    if (ready) update({ ...prefs, expandedSections: change(prefs.expandedSections) }, locale);
  }, [ready, locale]);
  const { expanded, toggle: toggleSection } = useControlSections(state.expandedSections, updateExpanded);
  // The tour shows a tab's content: its group is open while it does.
  const shown = showcasing ? (customizing ? "group/system" : groupFor(active)) : undefined;
  const isOpen = (group: CoreGroup) => group === shown || expanded.includes(group);
  const openGroup = (group: CoreGroup) => { if (!expanded.includes(group)) toggleSection(group); };
  const toggleQuick = (id: QuickId) => setQuick(current => current === id ? undefined : id);
  const group = (id: CoreGroup, title: string, icon: React.ReactNode, children: React.ReactNode, onboarding?: string) =>
    <div className="ph-core-group" data-core-group={id} data-playhub-onboarding={origin === "qam" ? onboarding : undefined}>
      <ControlSection id={id} title={title} icon={icon} locale={locale} collapsed={!isOpen(id)} onToggle={toggleSection}>
        <div data-playhub-onboarding-content={origin === "qam" && onboarding ? onboarding : undefined}>{children}</div>
      </ControlSection>
    </div>;
  return <Focusable className="ph-control-shell" flow-children="column"
    data-playhub-onboarding-controls={origin === "qam" ? "true" : undefined}
    data-onboarding-ready={ready} data-onboarding-decky-enabled={deckyHost.available}
    data-onboarding-decky-visible={false} data-onboarding-customizing={customizing}
    data-onboarding-active-tab={customizing ? "customize" : active}
    data-onboarding-store-visible={visible.includes("store")} data-onboarding-showcasing={showcasing}
    data-onboarding-visible-tabs={JSON.stringify(visible.filter(id => id !== "decky"))}
    data-onboarding-content-ready={ready}>
    <OnboardingInputCapture active={origin === "qam" && showcasing} />
    <div className="ph-controls" ref={onboardingRoot}><style>{((DFL as any).coreClassSelectors?.(css) ?? css)}</style>
    <ScrollPanel><Focusable flow-children="column">
      <QuickSettingsContent page="core" locale={locale} expanded={expanded} onToggle={toggleSection} gameAppId={gameAppId} gamingMode={gamingMode}
        layout={parts => <>
          {parts.status}
          <div className="ph-quick-label">{text.quick} · {text.alwaysOpen}</div>
          <Focusable className="ph-quick" flow-children="column">
            <QuickRow id="display" icon={<TbDeviceTv />} title={text.display} value={parts.quick.displayLabel} open={quick === "display"} onToggle={toggleQuick} />
            {quick === "display" && <div className="ph-quick-panel">{parts.quick.display}</div>}
            <QuickRow id="sound" icon={<TbVolume />} title={text.sound} value={parts.quick.soundLabel} open={quick === "sound"} onToggle={toggleQuick} />
            {quick === "sound" && <div className="ph-quick-panel">{parts.quick.sound}</div>}
            <QuickRow id="performance" icon={<TbBolt />} title={text.performance} open={quick === "performance"} onToggle={toggleQuick} />
            {quick === "performance" && <div className="ph-quick-panel"><div className="qsCardBody">{parts.quick.power}</div></div>}
            <QuickRow id="power" icon={<TbPower />} title={text.power} value={`${text.sleep} · ${text.restart} · ${text.shutDown}`} open={quick === "power"} onToggle={toggleQuick} />
            {quick === "power" && <Focusable className="ph-quick-panel ph-power-actions" flow-children="row">
              <DialogButton onClick={powerAction("SuspendPC")}>{text.sleep}</DialogButton>
              <DialogButton onClick={powerAction("RestartPC")}>{text.restart}</DialogButton>
              <DialogButton onClick={powerAction("ShutdownPC")}>{text.shutDown}</DialogButton>
            </Focusable>}
          </Focusable>
          {group("group/sound", text.sound, <TbVolume />, <>{parts.sections.mixer}{parts.sections.devices}</>, "performance")}
          {group("group/display", text.displayGraphics, <TbDeviceTv />, <>
            {parts.sections.display}
            {frameTiming && <ControlSection id="measurement" title={quickSettingsGroups(locale).measurement} icon={<TbGauge />} locale={locale}
              collapsed={!expanded.includes("measurement")} onToggle={toggleSection}>{frameTiming}</ControlSection>}
            {parts.sections.shaders}
            {parts.sections.nvidia}
            {parts.sections.radeon}
            {parts.sections.lossless}
          </>, "audio")}
          {group("group/controller", text.controller, <TbDeviceGamepad2 />, controller, "controller")}
          {downloadsRunning && group("group/downloads", text.downloads, <TbDownload />, downloads)}
          {group("group/look", text.look, <TbEye />, <>
            <CoreUiSection locale={locale} />
            <ControlSection id="topbar-date" title={topbarDateCopy(locale).title} locale={locale} icon={<TbSettings/>} collapsed={!expanded.includes('topbar-date')} onToggle={toggleSection}>
              <TopbarDateSettings state={state} update={update} locale={locale} /></ControlSection>
          </>)}
          {group("group/system", text.system, <TbSettings />, <>
            {!showcasing && <ControlSection id="device" title={copy.device} locale={locale} icon={<TbDeviceGamepad2/>} collapsed={!expanded.includes('device')} onToggle={toggleSection}>
              <DeviceSettings locale={locale} openVideo={() => openGroup("group/display")} openPerformance={() => { openGroup("group/display"); setQuick("performance"); }}
                detailsOpen={expanded.includes("device/details")} onDetailsToggle={() => toggleSection("device/details")} />
              <MsiChargeLimit locale={locale} />
            </ControlSection>}
            {parts.sections.agent}
            <div data-playhub-onboarding-content={origin === "qam" ? "home" : undefined}>{session}</div>
            <div className="ph-core-subhead">{text.stores}</div>
            <div data-playhub-onboarding-content={origin === "qam" ? "store" : undefined}>{store}</div>
            <div className="ph-core-subhead" data-playhub-onboarding={origin === "qam" ? "customize" : undefined}>{text.shortcuts}</div>
            <QamSettings locale={locale}>{settings}</QamSettings>
            <DialogButton className="ph-settings-action" onClick={() => replayPlayhubOnboarding()}>
              <TbRestore /> {getOnboardingExtraCopy(locale).replay}
            </DialogButton>
          </>, "home")}
        </>} />
    </Focusable></ScrollPanel>
    </div>
  </Focusable>;
}
