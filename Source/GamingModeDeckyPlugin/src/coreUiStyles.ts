// Eternal Core: Core UI (docs/eternal/CORE-UI.md). Builds the one stylesheet Core's plugin injects: phase 1 (the
// global chrome, the Steam menu, Quick Access, context menus) and phase 2 (Home, the Library's tab bar, grid and
// collection tiles).
//
// Steam's class names are hashed and change between builds, so no hashed name is written here. Every
// selector comes from one of Steam's class maps (readable key -> hashed class string), found through
// steamModules.ts: the classic class-map search on Decky 3, then the ES-module client's variables and module
// scopes (findVar / findModuleScopeByVar) on Decky 4. A rule whose map or key is missing on the running
// client is left out (and reported, so the runtime can log it once); the rest still apply.
//
// Every selector starts with html[data-core-ui] (set only while the "Core UI" setting is on) and repeats its
// target class, and every declaration is !important: Core UI wins over a CSS Loader theme on the areas it
// styles (see CORE-UI.md, "CSS Loader").
//
// Some rules key on attributes the runtime sets after looking at the live page (coreUi.ts, coreUiDom.ts,
// coreLibraryIcons.ts): which Quick Access element is the real panel, which large layers inside it are opaque,
// whether the top bar sits over art, which Library tab or collection tile is which console family.
//
// Pure on purpose (one import, steamModules.ts, itself pure): tests run it against fake Decky 3 and Decky 4
// libraries.

import { findClassModule } from "./steamModules";

export const CORE_UI_ATTRIBUTE = "data-core-ui";
export const CORE_UI_STYLE_ID = "core-ui-style";
export const CORE_UI_STARS_ATTRIBUTE = "data-core-ui-stars";
export const CORE_UI_HOST_ATTRIBUTE = "data-core-ui-host";
// Set by the runtime on large opaque layers inside a black panel (they would hide the stars).
export const CORE_UI_CLEAR_ATTRIBUTE = "data-core-ui-clear";
// Set by the runtime on the top bar and the button-hint bar: "black" when Steam paints them over a plain page,
// "clear" over game art.
export const CORE_UI_CHROME_ATTRIBUTE = "data-core-ui-chrome";
// Library decoration (coreLibraryIcons.ts): the tab or tile's family and key; the icon arrives as --core-icon.
export const CORE_UI_TAB_ATTRIBUTE = "data-core-tab";
export const CORE_UI_TILE_ATTRIBUTE = "data-core-collection";
export const CORE_UI_TONE_ATTRIBUTE = "data-core-tone";
export const CORE_UI_ICON_ATTRIBUTE = "data-core-icon";
const ROOT = `html[${CORE_UI_ATTRIBUTE}]`;

// The corner rule (CORE-UI.md, "Corners"): square (0) for the full-height side panels, the top bar, the footer and
// every focus or selection highlight; one token for floating things only (context menus, submenus, dialogs, modals,
// toasts, cards). Capsules and art keep Steam's own radius.
export const CORE_RADIUS_FLOAT_VAR = "--core-radius-float";
export const CORE_RADIUS_FLOAT = "10px";
const FLOAT = `var(${CORE_RADIUS_FLOAT_VAR}, ${CORE_RADIUS_FLOAT})`;
const SQUARE = "0";

// Colours: corePalette.ts (CORE), repeated here so this file stays loadable on its own.
const C = { black: "#000000", gunmetal: "#1A1D21", raised: "#2A2F36", steel: "#C9D1D9", hint: "#9AA4AE", text: "#E6EAEE", white: "#FFFFFF", crimson: "#D7263D", crimsonText: "#EA5A68" } as const;

// Console-family tones (Library tabs and collection tiles; MAPPING.md of the Core icon set). All Games, Installed,
// VR and Completed are steel with no tone.
export const CORE_FAMILY_TONES = { playstation: "#6E8CB8", nintendo: "#B07A84", sega: "#5E9E9A", xbox: "#7FA36B", retro: "#8F7CB0" } as const;

// Core's ring mark (assets/playhub-logo-decky.svg): the only logo on the Steam menu, top left, in its own space.
export const CORE_RING_MARK_SVG = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 450 450"><g transform="translate(225,225) scale(3.24812)"><circle r="62" fill="none" stroke="#3A3F46" stroke-width="9"/><circle r="62" fill="none" stroke="#D7263D" stroke-width="9" stroke-linecap="round" transform="rotate(180)" stroke-dasharray="389.6" stroke-dashoffset="194.8"/><g stroke="#C9D1D9" stroke-width="5" stroke-linecap="round"><line x1="0" y1="-48" x2="0" y2="-38"/><line x1="0" y1="38" x2="0" y2="48"/><line x1="-48" y1="0" x2="-38" y2="0"/><line x1="38" y1="0" x2="48" y2="0"/><line x1="-33.9" y1="-33.9" x2="-26.9" y2="-26.9"/><line x1="26.9" y1="26.9" x2="33.9" y2="33.9"/><line x1="33.9" y1="-33.9" x2="26.9" y2="-26.9"/><line x1="-26.9" y1="26.9" x2="-33.9" y2="33.9"/></g><circle r="22" fill="#C9D1D9"/><circle r="9" fill="#D7263D"/></g></svg>';
export const CORE_RING_MARK = "data:image/svg+xml," + encodeURIComponent(CORE_RING_MARK_SVG);
// The mark's reserved band at the top of the Steam menu: no row and no focus highlight is ever inside it.
export const CORE_MENU_LOGO_BAND = 80;
export const CORE_MENU_LOGO_SIZE = 44;

export type CoreUiMapName = "mainmenu" | "quickaccess" | "controls" | "footer" | "header" | "dropdown" | "contextmenu"
  | "gamepadui" | "tabbedpage" | "library" | "appgrid" | "portrait" | "recentgames" | "carousel" | "home";
export type CoreUiMaps = Partial<Record<CoreUiMapName, Record<string, string>>>;

// How each class map is recognised: keys only that map has (from Steam's readable module names, e.g.
// mainmenu_ItemIcon, quickaccessmenu_TabGroupPanel). quickaccess matches @decky/ui's quickAccessMenuClasses.
export const CORE_UI_MAP_FILTERS: Record<CoreUiMapName, (map: any) => boolean> = {
  mainmenu: map => has(map, "ItemIcon") && has(map, "ItemLabel") && has(map, "ActiveDot") && has(map, "Menu"),
  quickaccess: map => has(map, "TabGroupPanel") && has(map, "TabContentColumn") && has(map, "Tab") && has(map, "Title"),
  controls: map => has(map, "PanelSection") && has(map, "PanelSectionTitle") && !has(map, "TabGroupPanel") && !has(map, "QuickAccessMenu"),
  footer: map => has(map, "BasicFooter") && has(map, "QuickAccessFooter"),
  header: map => has(map, "Header") && has(map, "HeaderDownloadContainer"),
  dropdown: map => has(map, "DropDownControlButton") && !has(map, "GamepadDialogContent"),
  // Big Picture's context menus (gamepadcontextmenu_*), not the desktop client's (no BasicContextMenuHeader).
  contextmenu: map => has(map, "contextMenuContents") && has(map, "contextMenuItem") && has(map, "BasicContextMenuHeader") && has(map, "Focused"),
  // Phase 2 (readable names from the retired Core CSS Loader theme and @decky/ui's static class maps).
  gamepadui: map => has(map, "BasicHome") && has(map, "MainNavMenuAnchor") && has(map, "OpaqueBackground"),
  tabbedpage: map => has(map, "TabRow") && has(map, "TabTitle") && has(map, "TabHeaderRowWrapper") && has(map, "Tab"),
  library: map => has(map, "GamepadLibrary") && has(map, "CollectionHeader"),
  appgrid: map => has(map, "AppGridSectionLabel") && has(map, "LibraryImageBackgroundGlow"),
  portrait: map => has(map, "LibraryItemBox") && !has(map, "TabRow"),
  recentgames: map => has(map, "RecentGamesContainer") && has(map, "RecentGamesBackgroundContainer"),
  carousel: map => has(map, "CarouselCapsuleBackgroundGlow"),
  home: map => has(map, "TabbedContent") && !has(map, "TabRow") && !has(map, "Tab") && !has(map, "BasicHome"),
};

function has(map: any, key: string): boolean {
  try { return !!map && typeof map === "object" && typeof map[key] === "string" && map[key].length > 0; } catch { return false; }
}

function attempt<T>(run: () => T): T | undefined {
  try { return run(); } catch { return undefined; }
}

// One class map: the class-map search (Decky 3's findClassModule / classMap, then findModule, which on Decky 4
// walks module scopes and variables), then Decky 4's findVar and findModuleScopeByVar directly.
export function findCoreUiMap(ui: any, filter: (map: any) => boolean): Record<string, string> | undefined {
  const accept = (value: any) => (value && typeof value === "object" && attempt(() => filter(value)) === true ? value : undefined);
  const classic = accept(attempt(() => findClassModule(ui, filter)));
  if (classic) return classic;
  const variable = typeof ui?.findVar === "function" ? accept(attempt(() => ui.findVar((value: any) => accept(value) !== undefined))) : undefined;
  if (variable) return variable;
  const scope = typeof ui?.findModuleScopeByVar === "function"
    ? attempt(() => ui.findModuleScopeByVar((value: any) => accept(value) !== undefined)) : undefined;
  if (scope && typeof scope === "object") {
    for (const value of attempt(() => Object.values(scope)) ?? []) { const hit = accept(value); if (hit) return hit; }
  }
  return undefined;
}

export function findCoreUiMaps(ui: any): CoreUiMaps {
  const maps: CoreUiMaps = {};
  for (const name of Object.keys(CORE_UI_MAP_FILTERS) as CoreUiMapName[]) {
    const map = findCoreUiMap(ui, CORE_UI_MAP_FILTERS[name]);
    if (map) maps[name] = map;
  }
  return maps;
}

const CLASS_NAME = /^-?[A-Za-z_][\w-]*$/;

// ".hashed.hashed": the class from the map, repeated once for specificity. Undefined when the key is missing
// or its value is not a plain class list.
export function classSelector(map: Record<string, string> | undefined, key: string): string | undefined {
  if (!has(map, key)) return undefined;
  const names = String(map![key]).trim().split(/\s+/);
  if (!names.length || !names.every(name => CLASS_NAME.test(name))) return undefined;
  return "." + names.join(".") + "." + names[0];
}

type Need = [CoreUiMapName, string];
type Rule = { id: string; needs: Need[]; css: (s: (map: CoreUiMapName, key: string) => string) => string };

const imp = (block: Record<string, string>) => Object.entries(block).map(([key, value]) => `${key}:${value} !important`).join(";");
const CLEAR = { "background-color": "transparent", "background-image": "none", "box-shadow": "none" };
// Pure black, opaque: no translucency, no tint, no blur behind, no gradient or shadow (item 6 of the TV list).
const SOLID_BLACK = { "background-color": C.black, "background-image": "none", "box-shadow": "none", "backdrop-filter": "none", filter: "none", opacity: "1" };

function rules(mark: string | undefined): Rule[] {
  const list: Rule[] = [
    // The float radius token, inherited by everything in the page; Steam's global confirm dialogs use it.
    { id: "corners", needs: [], css: () =>
      `${ROOT} body{${imp({ [CORE_RADIUS_FLOAT_VAR]: CORE_RADIUS_FLOAT })}}${ROOT} .GenericConfirmDialog{${imp({ "border-radius": FLOAT })}}` },
    // Global chrome: the top bar and the button-hint bar are clear over game art and pure black anywhere else
    // (the runtime says which: data-core-ui-chrome), and whenever Steam paints them opaque.
    { id: "chrome-header", needs: [["header", "Header"]], css: s => {
      const header = `${ROOT} ${s("header", "Header")}`;
      return `${header},${header}::before,${header}::after{${imp({ ...CLEAR, "backdrop-filter": "none", filter: "none", "border-bottom-color": "transparent" })}}` +
        `${header}[${CORE_UI_CHROME_ATTRIBUTE}="black"]{${imp(SOLID_BLACK)}}${header}{${imp({ "border-radius": SQUARE })}}`;
    } },
    { id: "chrome-header-opaque", needs: [["header", "HeaderOpaque"]], css: s => {
      const opaque = `${ROOT} ${s("header", "HeaderOpaque")}`;
      return `${opaque}{${imp(SOLID_BLACK)}}${opaque}::before,${opaque}::after{${imp({ ...CLEAR, "backdrop-filter": "none" })}}`;
    } },
    { id: "chrome-footer", needs: [["footer", "BasicFooter"]], css: s => {
      const footer = `${ROOT} ${s("footer", "BasicFooter")}`;
      return `${footer},${footer}::before,${footer}::after{${imp({ ...CLEAR, "backdrop-filter": "none", filter: "none", "border-top-color": "transparent" })}}` +
        `${footer}[${CORE_UI_CHROME_ATTRIBUTE}="black"]{${imp(SOLID_BLACK)}}${footer}{${imp({ "border-radius": SQUARE })}}`;
    } },
    { id: "chrome-footer-opaque", needs: [["footer", "Opaque"]], css: s => {
      const opaque = `${ROOT} ${s("footer", "Opaque")}`;
      return `${opaque}{${imp(SOLID_BLACK)}}${opaque}::before,${opaque}::after{${imp({ ...CLEAR, "backdrop-filter": "none" })}}`;
    } },

    // Steam menu: AMOLED black panel (stars drawn behind the rows by coreUi.ts). Core's ring mark sits top left in a
    // band of its own: an in-flow block before every row, so no row and no focus fill can ever overlap it (the
    // runtime also checks the first row starts below it; coreUiDom.ts, keepBelowLogo).
    { id: "menu-panel", needs: [["mainmenu", "Menu"]], css: s => {
      const menu = `${ROOT} ${s("mainmenu", "Menu")}`;
      return `${menu}{${imp({ position: "relative", "background-color": C.black, "background-image": "none", "box-shadow": "none", "border-radius": SQUARE })}}` +
        (mark ? `${menu}::before{${imp({ content: '""', display: "block", position: "relative", order: "-1", flex: `0 0 ${CORE_MENU_LOGO_BAND}px`,
          "box-sizing": "border-box", width: "100%", height: `${CORE_MENU_LOGO_BAND}px`, "min-height": `${CORE_MENU_LOGO_BAND}px`, margin: "0", padding: "0",
          background: `url(${JSON.stringify(mark)}) 24px center / ${CORE_MENU_LOGO_SIZE}px ${CORE_MENU_LOGO_SIZE}px no-repeat`, "pointer-events": "none", "z-index": "1" })}}` : "");
    } },
    // One inset and a fixed-width icon cell for every row, so icons and labels line up. No transforms: the
    // rows must not drift sideways.
    { id: "menu-rows", needs: [["mainmenu", "Menu"], ["mainmenu", "Item"], ["mainmenu", "ItemIcon"], ["mainmenu", "ItemLabel"]], css: s => {
      const menu = `${ROOT} ${s("mainmenu", "Menu")}`;
      const outer = s("mainmenu", "ItemOuter") === "" ? "" : `${menu} ${s("mainmenu", "ItemOuter")}{${imp({ "margin-left": "0", "padding-left": "0", transform: "none", left: "auto" })}}`;
      return outer +
        `${menu} ${s("mainmenu", "Item")}{${imp({ display: "flex", "align-items": "center", "box-sizing": "border-box", "min-height": "48px", "margin-left": "0",
          "padding-left": "24px", "padding-right": "16px", transform: "none", left: "auto", "background-color": "transparent", "background-image": "none",
          "box-shadow": "none", "border-radius": "0", outline: "none", animation: "none" })}}` +
        `${menu} ${s("mainmenu", "Item")}::before,${menu} ${s("mainmenu", "Item")}::after{${imp({ "background-image": "none", "box-shadow": "none" })}}` +
        `${menu} ${s("mainmenu", "ItemIcon")}{${imp({ flex: "0 0 32px", width: "32px", "min-width": "32px", "max-width": "32px", margin: "0 16px 0 0", padding: "0",
          display: "flex", "align-items": "center", "justify-content": "center", transform: "none", position: "static" })}}` +
        `${menu} ${s("mainmenu", "ItemIcon")} :is(svg,img){${imp({ width: "24px", height: "24px", "max-width": "24px", "max-height": "24px" })}}` +
        `${menu} ${s("mainmenu", "ItemLabel")}{${imp({ margin: "0", padding: "0", "text-align": "left", transform: "none", color: C.steel, "font-size": "16px" })}}`;
    } },
    // Focused row: gunmetal with a 3px crimson bar on its left edge. No glow, no underline.
    { id: "menu-focus", needs: [["mainmenu", "Menu"], ["mainmenu", "Item"]], css: s => {
      const menu = `${ROOT} ${s("mainmenu", "Menu")}`;
      const item = s("mainmenu", "Item");
      return `${menu} ${item}:is(.gpfocus,:focus,:focus-visible,:focus-within){${imp({ "background-color": C.gunmetal, "background-image": "none",
          "box-shadow": `inset 3px 0 0 ${C.crimson}`, outline: "none", animation: "none", filter: "none", "border-radius": SQUARE })}}` +
        `${menu} ${item}:is(.gpfocus,:focus,:focus-visible,:focus-within) *{${imp({ color: C.white, "text-shadow": "none" })}}`;
    } },
    // The page you are on (Home's marker): crimson, not Steam's blue.
    { id: "menu-active", needs: [["mainmenu", "ActiveDot"]], css: s =>
      `${ROOT} ${s("mainmenu", "ActiveDot")}{${imp({ "background-color": C.crimson, "background-image": "none", "box-shadow": "none", "border-color": C.crimson })}}` },
    // The "MENU" footer under the panel: black.
    { id: "menu-footer", needs: [["mainmenu", "FooterBoxShadow"]], css: s =>
      `${ROOT} ${s("mainmenu", "FooterBoxShadow")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },
    { id: "menu-footer-bar", needs: [["mainmenu", "Menu"], ["footer", "BasicFooter"]], css: s =>
      `${ROOT} ${s("mainmenu", "Menu")} ${s("footer", "BasicFooter")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },

    // Quick Access: one black panel with the Starfield behind everything. Which element is the real panel differs
    // between clients (Menu, QuickAccessMenu or Container): each is a candidate, the runtime picks the outermost one
    // that is panel-sized (CORE_UI_HOSTS) and only that one is painted black; the others go clear.
    { id: "qam-panel", needs: [["quickaccess", "Menu"]], css: s => {
      const candidates = ["Container", "QuickAccessMenu", "ViewPlaceholder"].filter(key => s("quickaccess", key) !== "")
        .map(key => `${ROOT} ${s("quickaccess", key)}`);
      return `${ROOT} ${s("quickaccess", "Menu")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none", "border-radius": SQUARE })}}` +
        (candidates.length ? `${candidates.join(",")}{${imp({ ...CLEAR, "backdrop-filter": "none", "border-radius": SQUARE })}}` +
          `${candidates.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}="1"]`).join(",")}{${imp({ "background-color": C.black, "background-image": "none" })}}` : "");
    } },
    // Everything over the stars is clear: the content column, plugin panels (Decky's and every plugin's own,
    // CSS Loader's included), and any large opaque layer the runtime finds inside the panel.
    { id: "qam-clear", needs: [["quickaccess", "TabContentColumn"]], css: s => {
      const keys = ["TabContentColumn", "AllTabContents", "ContentTransition", "TabGroupPanel", "PanelSection", "PanelOuterNav", "PopupBody", "FriendsListTabPanel"];
      const list = keys.filter(key => s("quickaccess", key) !== "").map(key => `${ROOT} ${s("quickaccess", key)}`);
      if (s("controls", "PanelSection") !== "") list.push(`${ROOT} ${s("controls", "PanelSection")}`);
      list.push(`${ROOT} [${CORE_UI_CLEAR_ATTRIBUTE}]`);
      return `${list.join(",")}{${imp({ ...CLEAR, "backdrop-filter": "none" })}}`;
    } },
    // The title strip: clear over the panel's black and stars (no gradient bars above or under it, no logo).
    { id: "qam-title", needs: [["quickaccess", "Title"]], css: s => {
      const keys = ["Title", "HeaderContainer"].filter(key => s("quickaccess", key) !== "").map(key => `${ROOT} ${s("quickaccess", key)}`);
      const pseudo = keys.flatMap(key => [`${key}::before`, `${key}::after`]);
      return `${keys.join(",")}{${imp({ ...CLEAR, "border-color": "transparent", "backdrop-filter": "none" })}}` +
        `${pseudo.join(",")}{${imp({ "background-image": "none", "background-color": "transparent", "box-shadow": "none" })}}`;
    } },
    // The tab rail: clear over the panel's black and stars, a thin gunmetal divider; the open tab gunmetal (its
    // crimson bar is Core's own).
    { id: "qam-tabs", needs: [["quickaccess", "Tabs"], ["quickaccess", "Tab"]], css: s =>
      `${ROOT} ${s("quickaccess", "Tabs")}{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": `inset -1px 0 0 ${C.gunmetal}` })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}:is(.gpfocus,:focus-visible){${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", filter: "none", "border-radius": SQUARE })}}` },
    { id: "qam-tab-selected", needs: [["quickaccess", "Tab"], ["quickaccess", "Selected"]], css: s =>
      `${ROOT} ${s("quickaccess", "Tab")}${s("quickaccess", "Selected")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", "border-radius": SQUARE })}}` },
    { id: "qam-tabs-footer", needs: [["quickaccess", "TabsWithFooter"]], css: s =>
      `${ROOT} ${s("quickaccess", "TabsWithFooter")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` },
    // The footer bar: black.
    { id: "qam-footer", needs: [["footer", "QuickAccessFooter"]], css: s =>
      `${ROOT} ${s("footer", "QuickAccessFooter")},${ROOT} ${s("footer", "QuickAccessFooter")}::before{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },
    { id: "qam-footer-shadow", needs: [["quickaccess", "FooterBoxShadow"]], css: s =>
      `${ROOT} ${s("quickaccess", "FooterBoxShadow")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },
    // Plugin content: Steam's buttons and dropdowns inside Quick Access lose the grey-blue gradient. Gunmetal,
    // raised plus a 2px crimson ring when focused. DialogButton and gpfocus are Steam's unhashed global classes.
    { id: "qam-controls", needs: [["quickaccess", "Menu"]], css: s => {
      const scope = `${ROOT} ${s("quickaccess", "Menu")}`;
      const dropdown = s("dropdown", "DropDownControlButton");
      // A button that sets its own background inline (Jarvis's crimson "listening" buttons) keeps it.
      const buttons = [`${scope} .DialogButton:not([style*="background"])`, ...(dropdown ? [`${scope} ${dropdown}`] : [])];
      const focused = buttons.map(sel => `${sel}:is(.gpfocus,:focus-visible)`);
      return `${buttons.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", color: C.text, "box-shadow": "none", "text-shadow": "none" })}}` +
        `${focused.join(",")}{${imp({ "background-color": C.raised, "background-image": "none", color: C.white, "box-shadow": `inset 0 0 0 2px ${C.crimson}`, filter: "none", animation: "none", "border-radius": SQUARE })}}`;
    } },

    // Context menus and their submenus (a game's Options / "..." menu, Manage, and the menus the Steam menu and
    // Quick Access open). The Context board: black panel with a 1px gunmetal edge and 10px corners, 8px top and
    // bottom, 48px rows with a 22px inset and 16px text, a 13px hint-grey header, hairline gunmetal separators.
    { id: "context-panel", needs: [["contextmenu", "contextMenuContents"]], css: s =>
      `${ROOT} ${s("contextmenu", "contextMenuContents")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": `inset 0 0 0 1px ${C.gunmetal}`,
        "border-radius": FLOAT, overflow: "hidden", "padding-top": "8px", "padding-bottom": "8px", filter: "none", "backdrop-filter": "none" })}}` +
      (s("contextmenu", "contextMenu") ? `${ROOT} ${s("contextmenu", "contextMenu")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` : "") },
    { id: "context-header", needs: [["contextmenu", "BasicContextMenuHeader"]], css: s => {
      const headers = ["BasicContextMenuHeader", "MenuSectionHeader"].filter(key => s("contextmenu", key)).map(key => `${ROOT} ${s("contextmenu", key)}`);
      return `${headers.join(",")}{${imp({ "background-color": "transparent", "background-image": "none", color: C.hint, "font-size": "13px", padding: "12px 22px 10px" })}}`;
    } },
    { id: "context-separator", needs: [["contextmenu", "ContextMenuSeparator"]], css: s =>
      `${ROOT} ${s("contextmenu", "ContextMenuSeparator")}{${imp({ "background-color": C.gunmetal, "background-image": "none", height: "1px", "min-height": "1px", margin: "6px 0", border: "0", opacity: "1" })}}` },
    { id: "context-items", needs: [["contextmenu", "contextMenuItem"]], css: s => {
      const item = `${ROOT} ${s("contextmenu", "contextMenuItem")}`;
      return `${item}{${imp({ display: "flex", "align-items": "center", "box-sizing": "border-box", "min-height": "48px", padding: "0 22px", "font-size": "16px",
          color: C.steel, "background-color": "transparent", "background-image": "none", "box-shadow": "none", "border-radius": "0", "text-shadow": "none", animation: "none" })}}` +
        `${item} svg{${imp({ flex: "0 0 auto", "margin-right": "12px", fill: "currentColor" })}}` +
        `${item}:is([disabled],[aria-disabled="true"])${s("contextmenu", "Disabled") ? `,${item}${s("contextmenu", "Disabled")}` : ""}{${imp({ color: C.hint })}}`;
    } },
    // Focused (and an open submenu's item): gunmetal with the Steam menu's crimson left bar. No glow.
    { id: "context-focus", needs: [["contextmenu", "contextMenuItem"], ["contextmenu", "Focused"]], css: s => {
      const item = `${ROOT} ${s("contextmenu", "contextMenuItem")}`;
      const open = s("contextmenu", "SubMenu") && s("contextmenu", "active") ? `,${item}${s("contextmenu", "SubMenu")}${s("contextmenu", "active")}` : "";
      return `${item}${s("contextmenu", "Focused")},${item}:is(.gpfocus,:focus-visible)${open}{${imp({ "background-color": C.gunmetal, "background-image": "none",
        "box-shadow": `inset 3px 0 0 ${C.crimson}`, color: C.white, filter: "none", outline: "none", "border-radius": SQUARE })}}`;
    } },
    // Danger items (Steam marks them Destructive: Uninstall, Remove...): crimson text, focused or not.
    { id: "context-danger", needs: [["contextmenu", "contextMenuItem"], ["contextmenu", "Destructive"]], css: s =>
      `${ROOT} ${s("contextmenu", "contextMenuItem")}${s("contextmenu", "Destructive")}{${imp({ color: C.crimsonText })}}` },

    // ---- Phase 2: Home (the Home board: more art, less chrome, black with stars behind the shelves). ----
    { id: "home-page", needs: [["gamepadui", "BasicHome"]], css: s => {
      const home = `${ROOT} ${s("gamepadui", "BasicHome")}`;
      return `${home}{${imp({ "background-color": C.black, "background-image": "none" })}}` +
        (s("home", "TabbedContent") ? `${ROOT} ${s("home", "TabbedContent")}{${imp(CLEAR)}}` : "");
    } },
    // The hero: Steam's recent-games art runs taller (more art) and fades into the black through a mask, so
    // nothing is painted over it.
    { id: "home-hero", needs: [["recentgames", "RecentGamesBackgroundContainer"]], css: s => {
      const keys = ["RecentGamesBackgroundContainer", "RecentGamesBackgroundImages"].filter(key => s("recentgames", key) !== "")
        .map(key => `${ROOT} ${s("recentgames", key)}`);
      return `${keys.join(",")}{${imp({ "-webkit-mask-image": "linear-gradient(180deg, #000000 45%, transparent 100%)",
        "mask-image": "linear-gradient(180deg, #000000 45%, transparent 100%)", "min-height": "62vh" })}}` +
        (s("recentgames", "RecentGamesHeaderLabel") ? `${ROOT} ${s("recentgames", "RecentGamesHeaderLabel")}{${imp({ color: C.steel, "font-size": "12px", "letter-spacing": "0.1em", "text-transform": "uppercase" })}}` : "");
    } },
    { id: "home-glow", needs: [["carousel", "CarouselCapsuleBackgroundGlow"]], css: s =>
      `${ROOT} ${s("carousel", "CarouselCapsuleBackgroundGlow")}{${imp({ opacity: "0" })}}` },

    // ---- Phase 2: the Library (the Library board: black grid, tabs like Quick Access, crimson focus ring). ----
    { id: "library-page", needs: [["library", "GamepadLibrary"]], css: s => {
      const library = `${ROOT} ${s("library", "GamepadLibrary")}`;
      return `${library}{${imp({ "background-color": C.black, "background-image": "none" })}}` +
        `${ROOT} ${s("library", "CollectionHeader")}{${imp({ color: C.steel, "font-size": "12px", "letter-spacing": "0.1em", "text-transform": "uppercase" })}}`;
    } },
    { id: "library-grid", needs: [["appgrid", "AppGridSectionLabel"]], css: s =>
      `${ROOT} ${s("appgrid", "LibraryImageBackgroundGlow")}{${imp({ opacity: "0" })}}` +
      `${ROOT} ${s("appgrid", "AppGridSectionLabel")}{${imp({ color: C.steel, "font-size": "12px", "letter-spacing": "0.1em", "text-transform": "uppercase" })}}` +
      (s("appgrid", "Rule") ? `${ROOT} ${s("appgrid", "Rule")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "border-color": C.gunmetal })}}` : "") },
    // Focused capsule (Home and the Library): a 3px crimson ring, no glow, lifted 6px (still under reduced motion).
    { id: "capsule-focus", needs: [["portrait", "LibraryItemBox"]], css: s => {
      const scopes = [s("library", "GamepadLibrary"), s("home", "TabbedContent"), s("recentgames", "RecentGamesContainer"), s("gamepadui", "BasicHome")].filter(Boolean);
      if (!scopes.length) return "";
      const box = s("portrait", "LibraryItemBox");
      const items = scopes.map(scope => `${ROOT} ${scope} ${box}`);
      const focused = items.map(item => `${item}:is(.gpfocus,:focus)`);
      return `${items.join(",")}{${imp({ transition: "translate 0.15s ease-out" })}}` +
        `${focused.join(",")}{${imp({ outline: `3px solid ${C.crimson}`, "outline-offset": "0", animation: "none", filter: "none", translate: "0 -6px" })}}` +
        `@media (prefers-reduced-motion: reduce){${[...items, ...focused].join(",")}{${imp({ transition: "none", translate: "none" })}}}`;
    } },
    // The tab bar (Library only; the game page keeps Steam's tabs in this phase): black row, steel tabs, the open one
    // gunmetal with a 3px crimson underline, a focused one also gets a 2px crimson ring.
    { id: "library-tabs", needs: [["tabbedpage", "Tab"], ["library", "GamepadLibrary"]], css: s => {
      const lib = s("library", "GamepadLibrary");
      const page = s("tabbedpage", "GamepadTabbedPage");
      const scope = (inner: string) => [`${ROOT} ${lib} ${inner}`, ...(page ? [`${ROOT} ${page}:has(${lib}) ${inner}`] : [])].join(",");
      const tab = s("tabbedpage", "Tab");
      const selected = s("tabbedpage", "Selected");
      const wrapper = s("tabbedpage", "TabHeaderRowWrapper");
      return (wrapper ? `${scope(wrapper)},${scope(`${wrapper}::before`)}{${imp({ "background-color": C.black, "background-image": "none", "backdrop-filter": "none" })}}` : "") +
        `${scope(s("tabbedpage", "TabRow"))}{${imp({ "box-shadow": `inset 0 -1px 0 ${C.gunmetal}` })}}` +
        `${scope(tab)}{${imp({ position: "relative", "background-color": "transparent", "background-image": "none", color: C.steel, "box-shadow": "none" })}}` +
        (selected ? `${scope(`${tab}${selected}`)}{${imp({ "background-color": C.gunmetal, color: C.white, "border-radius": SQUARE, "box-shadow": `inset 0 -3px 0 ${C.crimson}` })}}` : "") +
        `${scope(`${tab}.gpfocus`)}{${imp({ "background-color": C.gunmetal, color: C.white, "border-radius": SQUARE, "box-shadow": `inset 0 -3px 0 ${C.crimson}, inset 0 0 0 2px ${C.crimson}` })}}` +
        (s("tabbedpage", "TabCount") ? `${scope(s("tabbedpage", "TabCount"))}{${imp({ display: "none" })}}` : "");
    } },
    // Library tab icons and family tones, keyed by tab title (coreLibraryIcons.ts sets data-core-tab, data-core-tone
    // and --core-icon on each matched tab). The icon sits before the label; the tone is a short 24px marker under
    // it, above the active tab's crimson underline.
    { id: "library-tab-icons", needs: [["tabbedpage", "Tab"]], css: s => {
      const tab = `${ROOT} ${s("tabbedpage", "Tab")}[${CORE_UI_TAB_ATTRIBUTE}]`;
      const title = s("tabbedpage", "TabTitle");
      const icon = title ? `${tab}[${CORE_UI_ICON_ATTRIBUTE}] ${title}::before` : `${tab}[${CORE_UI_ICON_ATTRIBUTE}]::before`;
      return `${icon}{${imp({ content: '""', display: "inline-block", width: "26px", height: "26px", margin: "0 8px 0 0", "vertical-align": "-7px",
          background: "var(--core-icon) center / contain no-repeat" })}}` +
        `${tab}[${CORE_UI_TONE_ATTRIBUTE}]::after{${imp({ content: '""', position: "absolute", left: "50%", bottom: "6px", width: "24px", height: "3px", "margin-left": "-12px",
          "border-radius": "2px", "background-color": "var(--core-tone)", "background-image": "none", "pointer-events": "none" })}}` + toneVariables(tab);
    } },
  ];
  return list;
}

// --core-tone per family, on any element the runtime tagged with data-core-tone.
function toneVariables(prefix: string): string {
  return Object.entries(CORE_FAMILY_TONES).map(([family, tone]) => `${prefix}[${CORE_UI_TONE_ATTRIBUTE}="${family}"]{${imp({ "--core-tone": tone })}}`).join("");
}

// Collection tiles in Steam's Collections view (tagged by coreLibraryIcons.ts by collection name): the console's
// icon over a gunmetal tile, Steam's collage hidden, the family tone as a short bar, a crimson ring on focus.
function collectionTileCss(): string {
  const tile = `${ROOT} [${CORE_UI_TILE_ATTRIBUTE}]`;
  return `/* library-collection-tiles */${tile}{${imp({ position: "relative", "border-radius": FLOAT, overflow: "hidden", "box-shadow": "none" })}}` +
    `${tile}[${CORE_UI_ICON_ATTRIBUTE}]{${imp({ "background-color": C.gunmetal, "background-image": "none" })}}` +
    `${tile}[${CORE_UI_ICON_ATTRIBUTE}] img{${imp({ opacity: "0" })}}` +
    `${tile}[${CORE_UI_ICON_ATTRIBUTE}]::before{${imp({ content: '""', position: "absolute", left: "18%", right: "18%", top: "10%", bottom: "24%", background: "var(--core-icon) center / contain no-repeat",
      "pointer-events": "none", "z-index": "2" })}}` +
    `${tile}[${CORE_UI_TONE_ATTRIBUTE}]::after{${imp({ content: '""', position: "absolute", left: "50%", bottom: "10px", width: "40px", height: "3px", "margin-left": "-20px",
      "border-radius": "2px", "background-color": "var(--core-tone)", "pointer-events": "none", "z-index": "2" })}}` +
    `${tile}:is(.gpfocus,:focus){${imp({ outline: `3px solid ${C.crimson}`, "outline-offset": "0", filter: "none", animation: "none" })}}` + toneVariables(tile);
}

// Every surface Core UI paints black, and so gets the Starfield. Footers and the opaque top bar are thin:
// they get stars only when large enough (minArea, CSS pixels) to read as a surface. The clear top bar and
// hint bar over game art are not hosts: no stars behind art or video.
// fullHeight: the Steam menu and Quick Access run from the top edge of the screen to the bottom one (coreFullHeight.ts
// moves the panel; the canvas extension stays as the fallback).
// panel: only an element at most 60% of the screen wide counts (a Quick Access candidate that turns out to be a
// full-screen wrapper is never painted black). Earlier entries of one group are tried as the outer host first.
export const CORE_UI_HOSTS: Array<{ id: string; map: CoreUiMapName; key: string; minArea: number; fullHeight?: boolean; panel?: boolean; page?: boolean }> = [
  { id: "menu", map: "mainmenu", key: "Menu", minArea: 0, fullHeight: true, panel: true },
  { id: "qam-container", map: "quickaccess", key: "Container", minArea: 0, fullHeight: true, panel: true },
  { id: "qam-panel", map: "quickaccess", key: "QuickAccessMenu", minArea: 0, fullHeight: true, panel: true },
  { id: "qam", map: "quickaccess", key: "Menu", minArea: 0, fullHeight: true, panel: true },
  { id: "context", map: "contextmenu", key: "contextMenuContents", minArea: 0 },
  { id: "menu-footer", map: "mainmenu", key: "FooterBoxShadow", minArea: 24000 },
  { id: "qam-footer", map: "footer", key: "QuickAccessFooter", minArea: 24000 },
  { id: "qam-footer-shadow", map: "quickaccess", key: "FooterBoxShadow", minArea: 24000 },
  { id: "footer-opaque", map: "footer", key: "Opaque", minArea: 24000 },
  { id: "header-opaque", map: "header", key: "HeaderOpaque", minArea: 24000 },
  // Phase 2 pages: the canvas covers the visible part of the page only.
  { id: "home", map: "gamepadui", key: "BasicHome", minArea: 0, page: true },
  { id: "library", map: "library", key: "GamepadLibrary", minArea: 0, page: true },
];

export type CoreUiHost = { id: string; selector: string; minArea: number; fullHeight: boolean; panel: boolean; page: boolean };
export type CoreUiSheet = {
  css: string; applied: string[]; skipped: string[]; hosts: CoreUiHost[]; clock?: string;
  // Selectors the runtime needs (plain, no specificity repeat).
  dom: { header?: string; footer?: string; menu?: string; menuItem?: string; qamTab?: string; tab?: string; tabTitle?: string; tabSelected?: string; library?: string; tabbedPage?: string };
};

// Jarvis's presence orb in the top bar, next to Steam's clock (jarvisPresence.ts decides its state). Dim steel ring
// when idle, a slow crimson pulse (opacity only, no glow) while listening or thinking, a crimson ring when an answer
// is ready. Still under reduced motion.
export const CORE_UI_ORB_ID = "core-jarvis-orb";
function orbCss(): string {
  const orb = `${ROOT} #${CORE_UI_ORB_ID}`;
  return `/* jarvis-orb */${orb}{${imp({ display: "inline-block", width: "12px", height: "12px", margin: "0 10px", "border-radius": "50%", "box-sizing": "border-box",
      "vertical-align": "middle", flex: "none", border: `2px solid ${C.steel}`, "background-color": "transparent", opacity: "0.45", "box-shadow": "none", animation: "none" })}}` +
    `${orb}[data-state="hidden"]{${imp({ display: "none" })}}` +
    `${orb}[data-state="busy"]{${imp({ "border-color": C.crimson, "background-color": C.crimson, opacity: "1", animation: "core-jarvis-pulse 1.6s ease-in-out infinite" })}}` +
    `${orb}[data-state="ready"]{${imp({ "border-color": C.crimson, "background-color": "transparent", opacity: "1" })}}` +
    `@keyframes core-jarvis-pulse{0%,100%{opacity:0.4}50%{opacity:1}}` +
    `@media (prefers-reduced-motion: reduce){${orb}[data-state="busy"]{animation:none !important}}`;
}

// The stylesheet for the maps found, plus where the Starfield goes. `skipped` names each rule whose map or
// class is missing on this client. `mark` is the Steam menu's logo (Core's ring mark by default; `wordmark`, the
// phase 1 name, is accepted too).
export function buildCoreUiCss(maps: CoreUiMaps, options: { mark?: string; wordmark?: string } = {}): CoreUiSheet {
  const applied: string[] = [], skipped: string[] = [], parts: string[] = [];
  const candidate = options.mark ?? options.wordmark ?? CORE_RING_MARK;
  const mark = typeof candidate === "string" && /^(https?:|data:image\/)/.test(candidate) ? candidate : undefined;
  for (const rule of rules(mark)) {
    if (!rule.needs.every(([map, key]) => classSelector(maps[map], key))) { skipped.push(rule.id); continue; }
    const select = (map: CoreUiMapName, key: string) => classSelector(maps[map], key) ?? "";
    let css: string | undefined;
    try { css = rule.css(select); } catch { css = undefined; }
    if (!css) { skipped.push(rule.id); continue; }
    parts.push(`/* ${rule.id} */${css}`);
    applied.push(rule.id);
  }
  // Nothing of Steam found (no class map at all): no stylesheet, not even the always-on parts.
  if (applied.length === 1 && applied[0] === "corners") { parts.length = 0; applied.length = 0; }
  const hosts: CoreUiHost[] = [];
  for (const host of CORE_UI_HOSTS) {
    const selector = plainSelector(maps[host.map], host.key);
    if (selector) hosts.push({ id: host.id, selector, minArea: host.minArea, fullHeight: host.fullHeight === true, panel: host.panel === true, page: host.page === true });
  }
  // Black means starfield: every black surface above is a host. The runtime puts one canvas in each (the
  // shared field), behind the surface's content; a host inside another host stays clear instead.
  if (hosts.length) {
    const all = hosts.map(host => `${ROOT} ${host.selector}`);
    const strong = hosts.map(host => `${ROOT} ${classSelector(maps[CORE_UI_HOSTS.find(item => item.id === host.id)!.map], CORE_UI_HOSTS.find(item => item.id === host.id)!.key)}`);
    parts.push(`/* starfield-hosts */${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}]`).join(",")}{${imp({ isolation: "isolate" })}}` +
      `${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}] > canvas[${CORE_UI_STARS_ATTRIBUTE}]`).join(",")}{${imp({ position: "absolute", left: "0", right: "auto", bottom: "auto", width: "100%",
        // The canvas follows the surface's scroll position and, for full-height surfaces, reaches the screen's edges.
        top: "calc(var(--core-ui-scroll, 0px) - var(--core-ui-extend-top, 0px))",
        height: "calc(100% + var(--core-ui-extend-top, 0px) + var(--core-ui-extend-bottom, 0px))",
        "z-index": "-1", "pointer-events": "none", margin: "0", padding: "0", border: "0", "border-radius": "inherit", "background-color": C.black })}}` +
      // A page host's canvas covers only what is on screen (--core-ui-page-height), not a tall scrolling page.
      `${hosts.filter(host => host.page).map(host => `${ROOT} ${host.selector}[${CORE_UI_HOST_ATTRIBUTE}] > canvas[${CORE_UI_STARS_ATTRIBUTE}]`).join(",") || `${ROOT} [data-core-ui-none]`}{${imp({ height: "var(--core-ui-page-height, 100vh)" })}}` +
      // A host inside another host (and a panel candidate that is not the panel) stays clear: the outer surface's
      // stars show through it. Repeats the host's class so it out-ranks the host's own black rule.
      `${strong.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}="nested"]`).join(",")}{${imp({ "background-color": "transparent", "background-image": "none" })}}`);
  }
  if (applied.length) parts.push(collectionTileCss());
  const clock = plainSelector(maps.header, "Clock");
  if (clock) parts.push(orbCss());
  const dom: CoreUiSheet["dom"] = {
    header: plainSelector(maps.header, "Header"), footer: plainSelector(maps.footer, "BasicFooter"),
    menu: plainSelector(maps.mainmenu, "Menu"), menuItem: plainSelector(maps.mainmenu, "Item"), qamTab: plainSelector(maps.quickaccess, "Tab"),
    tab: plainSelector(maps.tabbedpage, "Tab"), tabTitle: plainSelector(maps.tabbedpage, "TabTitle"), tabSelected: plainSelector(maps.tabbedpage, "Selected"),
    library: plainSelector(maps.library, "GamepadLibrary"), tabbedPage: plainSelector(maps.tabbedpage, "GamepadTabbedPage"),
  };
  return { css: parts.join("\n"), applied, skipped, hosts, clock, dom };
}

// The same class without the specificity repeat, for querySelector.
export function plainSelector(map: Record<string, string> | undefined, key: string): string | undefined {
  if (!has(map, key)) return undefined;
  const names = String(map![key]).trim().split(/\s+/);
  return names.every(name => CLASS_NAME.test(name)) ? "." + names.join(".") : undefined;
}
