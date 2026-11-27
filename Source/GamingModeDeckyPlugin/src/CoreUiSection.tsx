import { DFL, SP_REACT as React } from "./decky";
import { coreUiSetting } from "./coreUiSetting";
import { coreUiCopy } from "./coreUiLocale";
import { coreScreensaverControl } from "./circlesScreensaver";
import { storeDigitalCopy } from "./StoreDigitalLocale";
import { StoreDigitalStoreIcon } from "./StoreDigitalPage";
import { STORE_DIGITAL_ROUTE } from "./StoreDigitalNavigation";
import { railGlyphHtml, railIconBox, CORE_RAIL_ICON_ATTRIBUTE } from "./coreRailIcons";
import { coreUiSnapshotControl } from "./coreUiSnapshot";

// Eternal Core (Core UI phase 1, docs/eternal/CORE-UI.md): Quick Access > Core.
// - CoreUiSection: the one "Core UI" setting, on by default; off removes everything Core UI adds.
// - DigitalStoresEntry: Digital stores now opens from here (the Steam menu entry became Awesome Mix).
const { PanelSection, PanelSectionRow, ToggleField, ButtonItem, Navigation } = DFL as any;

export function CoreUiSection({ locale }: { locale: string }) {
  const on = React.useSyncExternalStore(coreUiSetting.subscribe, coreUiSetting.get, coreUiSetting.get);
  const text = coreUiCopy(locale);
  return (
    <PanelSection title={text.section}>
      <PanelSectionRow>
        <ToggleField bottomSeparator="none" label={text.label} description={text.hint} checked={on} onChange={(value: boolean) => coreUiSetting.set(value)} />
      </PanelSectionRow>
      <ScreensaverRow locale={locale} />
      <SnapshotRow locale={locale} />
    </PanelSection>
  );
}

// 3.3.4: a rail icon at Steam's own size while Core UI is on (coreRailIcons.ts), 1em off.
export function CoreRailIcon({ children, glyph }: { children?: any; glyph?: string }) {
  const on = React.useSyncExternalStore(coreUiSetting.subscribe, coreUiSetting.get, coreUiSetting.get);
  const props: any = { [CORE_RAIL_ICON_ATTRIBUTE]: "1", "aria-hidden": "true", style: railIconBox(on) };
  if (glyph) return <span {...props} dangerouslySetInnerHTML={{ __html: railGlyphHtml(glyph) }} />;
  return <span {...props}>{children}</span>;
}

// "Save Core UI snapshot": writes the diagnostic snapshot now (coreUiSnapshot.ts); the line under it says where.
function SnapshotRow({ locale }: { locale: string }) {
  const text = coreUiCopy(locale);
  const [state, setState] = React.useState<"idle" | "busy" | "saved" | "failed">("idle");
  const [path, setPath] = React.useState("");
  if (!coreUiSnapshotControl.available()) return null;
  const press = async () => {
    setState("busy");
    try {
      const answer = await coreUiSnapshotControl.saveNow();
      setPath(answer?.path ?? ""); setState(answer?.ok ? "saved" : "failed");
    } catch { setState("failed"); }
  };
  const description = state === "saved" ? `${text.snapshotSaved}${path ? ` ${path}` : ""}` : state === "failed" ? text.snapshotFailed : text.snapshotHint;
  return (
    <PanelSectionRow>
      <ButtonItem bottomSeparator="none" layout="below" description={description} disabled={state === "busy"} onClick={press}>{text.snapshot}</ButtonItem>
    </PanelSectionRow>
  );
}

// "Use Core Starfield as screensaver": selects Core Starfield in Steam's screensaver list, or goes back to Steam's
// slideshow. Shown only once Steam's settings store is found.
function ScreensaverRow({ locale }: { locale: string }) {
  const [, redraw] = React.useState(0);
  React.useEffect(() => coreScreensaverControl.subscribe(() => redraw(n => n + 1)), []);
  React.useEffect(() => { const timer = window.setInterval(() => redraw(n => n + 1), 3000); return () => window.clearInterval(timer); }, []);
  if (!coreScreensaverControl.available()) return null;
  const text = coreUiCopy(locale);
  return (
    <PanelSectionRow>
      <ToggleField bottomSeparator="none" label={text.screensaver} description={text.screensaverHint} checked={coreScreensaverControl.get()}
        onChange={(value: boolean) => { coreScreensaverControl.set(value); }} />
    </PanelSectionRow>
  );
}

export function openDigitalStores() {
  try {
    Navigation?.CloseSideMenus?.();
    window.setTimeout(() => Navigation?.Navigate?.(STORE_DIGITAL_ROUTE), 40);
  } catch (error) {
    console.warn("Core Digital stores: route not reachable", error);
  }
}

export function DigitalStoresEntry({ locale }: { locale: string }) {
  const text = storeDigitalCopy(locale);
  return (
    <PanelSectionRow>
      <ButtonItem bottomSeparator="none" layout="below" description={text.subtitle} onClick={openDigitalStores}>
        <span style={{ display: "inline-flex", alignItems: "center", gap: "10px" }}>
          <span style={{ display: "inline-flex", width: "1em", height: "1em" }}><StoreDigitalStoreIcon /></span>
          <span>{text.title}</span>
        </span>
      </ButtonItem>
    </PanelSectionRow>
  );
}
