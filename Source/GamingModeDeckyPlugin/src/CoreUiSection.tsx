import { DFL, SP_REACT as React } from "./decky";
import { coreUiSetting } from "./coreUiSetting";
import { coreUiCopy } from "./coreUiLocale";
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
    </PanelSection>
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
