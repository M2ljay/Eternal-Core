import { DFL, SP_REACT as React } from "./decky";
import { coreUiSetting } from "./coreUiSetting";
import { coreUiCopy } from "./coreUiLocale";
import { coreScreensaverControl } from "./circlesScreensaver";
import { storeDigitalCopy } from "./StoreDigitalLocale";
import { StoreDigitalStoreIcon } from "./StoreDigitalPage";
import { STORE_DIGITAL_ROUTE } from "./StoreDigitalNavigation";

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
    </PanelSection>
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
