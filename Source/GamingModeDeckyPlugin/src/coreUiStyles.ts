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
// 3.3.8: set by the runtime on a Starfield canvas only while its surface is on screen; without it the canvas is hidden.
export const CORE_UI_LIVE_ATTRIBUTE = "data-core-ui-live";
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
  | "gamepadui" | "tabbedpage" | "library" | "appgrid" | "portrait" | "recentgames" | "carousel" | "home" | "gamepaddialog"
  // 3.3.9: the game page, Settings, Downloads, the overlay and the controller configurator, search.
  | "appheader" | "appdetails" | "appsection" | "playsection" | "settings" | "toggle" | "downloads" | "downloadbar"
  | "overlay" | "configurator" | "configsummary" | "searchbar" | "searchresults" | "suggestions";
export type CoreUiMaps = Partial<Record<CoreUiMapName, Record<string, string>>>;

// How each class map is recognised (3.3.8): a signature of keys, never one key. `all` must all be present (kept to keys
// Steam has carried for years), each `any` group needs at least one of its keys (a key Steam renamed or dropped has
// siblings that stand in for it), and no `none` key may be present. 3.3.8 because Steam dropped quickaccess's
// TabContentColumn and gamepaddialog's FieldChildren, and the one-key matchers then disabled all of Quick Access.
// The real key lists of both modules are test fixtures (tests/fixtures/steam-class-maps.json); CORE-UI.md has them.
export type CoreUiMapSignature = { all: string[]; any?: string[][]; none?: string[] };
export const CORE_UI_MAP_SIGNATURES: Record<CoreUiMapName, CoreUiMapSignature> = {
  // 3.3.9: the in-game variant is the same map (AppRunningControls, NavigationColumn, AppColumn...).
  mainmenu: { all: ["Menu", "ItemIcon", "ItemLabel"], any: [["ActiveDot", "FooterBoxShadow", "AppRunningControls", "NavigationColumn"]] },
  // @decky/ui's quickAccessMenuClasses (webpack 34544 on the November 2026 client: no TabContentColumn any more).
  quickaccess: { all: ["TabGroupPanel", "Tab", "Title"],
    any: [["Tabs", "TabsWithFooter", "BottomTabs", "TabList"], ["TabContentColumn", "AllTabContents", "ContentTransition"], ["QuickAccessMenu", "Menu", "Container"]] },
  controls: { all: ["PanelSection"], any: [["PanelSectionTitle", "PanelSectionRow"]], none: ["TabGroupPanel", "QuickAccessMenu"] },
  footer: { all: ["BasicFooter"], any: [["QuickAccessFooter", "Opaque"]] },
  header: { all: ["Header"], any: [["HeaderDownloadContainer", "HeaderOpaque"]] },
  dropdown: { all: ["DropDownControlButton"], none: ["GamepadDialogContent"] },
  // Big Picture's context menus (gamepadcontextmenu_*), not the desktop client's (no BasicContextMenuHeader).
  contextmenu: { all: ["contextMenuContents", "contextMenuItem", "BasicContextMenuHeader"], any: [["Focused", "active"]] },
  // Phase 2 (readable names from the retired Core CSS Loader theme and @decky/ui's static class maps).
  gamepadui: { all: ["BasicHome"], any: [["MainNavMenuAnchor", "OpaqueBackground"]] },
  tabbedpage: { all: ["TabRow", "Tab", "TabTitle"], any: [["TabHeaderRowWrapper", "GamepadTabbedPage"]] },
  library: { all: ["GamepadLibrary"] },
  appgrid: { all: ["AppGridSectionLabel"], any: [["LibraryImageBackgroundGlow", "AppGrid", "LibraryAppGrid"]] },
  portrait: { all: ["LibraryItemBox"], none: ["TabRow"] },
  recentgames: { all: ["RecentGamesContainer"], any: [["RecentGamesBackgroundContainer", "RecentGamesBackgroundImages"]] },
  carousel: { all: ["CarouselCapsuleBackgroundGlow"] },
  home: { all: ["TabbedContent"], none: ["TabRow", "Tab", "BasicHome"] },
  // 3.3.4: Steam's dialog fields (@decky/ui's gamepadDialogClasses): the rows ButtonItem draws, Decky's plugin list too.
  // Webpack 2053 on the November 2026 client: no FieldChildren (FieldChildrenInner / FieldChildrenWithIcon instead).
  gamepaddialog: { all: ["Field", "FieldLabel"],
    any: [["FieldChildren", "FieldChildrenInner", "FieldChildrenWithIcon"], ["FieldDescription", "FieldLeftColumn", "FieldRightColumn"]] },
  // 3.3.9, from the coordinator's live read (steam-class-signatures-2026-11-27; tests/fixtures/steam-class-maps.json).
  // The game page: Steam's header (sharedappdetailsheader), the page root and play bar (basicappdetailssectionstyler),
  // its sections (appdetailssection) and the play section's cloud status (appdetailsplaysection; key names from Clean
  // Gameview's readable class names, not yet read live: CORE-UI.md says how to confirm them from the snapshot).
  appheader: { all: ["TopCapsule", "HeaderBackgroundImage", "ImgSrc"], any: [["TopGradient", "BoxSizer"], ["FullscreenEnterStart", "FullscreenEnterDone"]] },
  appdetails: { all: ["AppDetailsContainer", "PlaySection"], any: [["AppActionButton", "ActionRow", "AppButtons"], ["AppDetailsRoot", "AppDetailsContent", "AppDetailSectionList"]] },
  appsection: { all: ["AppDetailsSection", "AppDetailsSectionContainer"], any: [["RightColumnSection", "AppDetailsSectionHasLabel", "Body"]] },
  playsection: { all: ["CloudStatusLabel"], any: [["CloudSyncProblem", "CloudSyncProblemIcon"]] },
  // Settings (the paged settings dialog; an older module has the keys without the PagedSettingsDialog_ prefix).
  settings: { all: [], any: [["PagedSettingsDialog_PageListColumn", "PageListColumn"], ["PagedSettingsDialog_PageListItem", "PageListItem"],
    ["PageListItem_Title", "PagedSettingsDialog_PageList", "PageList"]] },
  // The small toggle map (Toggle, ToggleRail, ToggleSwitch, ToggleRow); gamepaddialog carries its own copy plus On.
  toggle: { all: ["Toggle", "ToggleRail", "ToggleSwitch"], any: [["ToggleRow"]], none: ["Field"] },
  downloads: { all: ["DownloadsPage", "SectionItem"], any: [["SectionTitle", "ItemLists"], ["ProgressBar", "SectionItemStatus"]] },
  downloadbar: { all: ["BottomBar", "DownloadStatus"], any: [["DetailedDownloadProgress", "ProgressBarWrapper"]] },
  overlay: { all: ["OverlayPopup", "ControllerConfiguratorWrapper"], any: [["BackToGame", "ExitGameBtn", "Toolbar"]] },
  configurator: { all: ["ControllerConfiguratorMain", "ControllerConfiguratorGamepadPage"], any: [["TopSectionHeader", "ConfigurationName", "MainOptionsButton"]] },
  configsummary: { all: ["ControllerConfiguratorSummary", "ControllerLayout"], any: [["ControlsListSection", "StandardControl"]] },
  searchbar: { all: ["SearchBox", "SearchFieldBackground"], any: [["SearchIconLeft", "SearchIconRight"], ["ShowingSearch", "SearchAndTitleContainer"]] },
  searchresults: { all: ["GamepadSearch", "ResultsRow"], any: [["ResultTemplate", "ResultsGridWrapper"]] },
  suggestions: { all: ["Suggestions", "Suggestion"] },
};

export function matchesSignature(map: any, signature: CoreUiMapSignature): boolean {
  return signature.all.every(key => has(map, key)) && (signature.any ?? []).every(group => group.some(key => has(map, key)))
    && !(signature.none ?? []).some(key => has(map, key));
}

export const CORE_UI_MAP_FILTERS: Record<CoreUiMapName, (map: any) => boolean> = Object.fromEntries(
  (Object.keys(CORE_UI_MAP_SIGNATURES) as CoreUiMapName[]).map(name => [name, (map: any) => matchesSignature(map, CORE_UI_MAP_SIGNATURES[name])]),
) as Record<CoreUiMapName, (map: any) => boolean>;

function has(map: any, key: string): boolean {
  try { return !!map && typeof map === "object" && typeof map[key] === "string" && map[key].length > 0; } catch { return false; }
}

function attempt<T>(run: () => T): T | undefined {
  try { return run(); } catch { return undefined; }
}

// How a class map was found (recorded in the Core UI snapshot): Decky 3's class-map search, Decky 4's findVar or
// findModuleScopeByVar, or not at all.
export type CoreUiMapRoute = "classic" | "findVar" | "scope" | "missing";

// One class map: the class-map search (Decky 3's findClassModule / classMap, then findModule, which on Decky 4
// walks module scopes and variables), then Decky 4's findVar and findModuleScopeByVar directly.
export function findCoreUiMapRoute(ui: any, filter: (map: any) => boolean): { map?: Record<string, string>; route: CoreUiMapRoute } {
  const accept = (value: any) => (value && typeof value === "object" && attempt(() => filter(value)) === true ? value : undefined);
  const classic = accept(attempt(() => findClassModule(ui, filter)));
  if (classic) return { map: classic, route: "classic" };
  const variable = typeof ui?.findVar === "function" ? accept(attempt(() => ui.findVar((value: any) => accept(value) !== undefined))) : undefined;
  if (variable) return { map: variable, route: "findVar" };
  const scope = typeof ui?.findModuleScopeByVar === "function"
    ? attempt(() => ui.findModuleScopeByVar((value: any) => accept(value) !== undefined)) : undefined;
  if (scope && typeof scope === "object") {
    for (const value of attempt(() => Object.values(scope)) ?? []) { const hit = accept(value); if (hit) return { map: hit, route: "scope" }; }
  }
  return { route: "missing" };
}

export function findCoreUiMap(ui: any, filter: (map: any) => boolean): Record<string, string> | undefined {
  return findCoreUiMapRoute(ui, filter).map;
}

// 3.3.8: for a map that was not found, the class map that comes closest to its signature (the share of the signature's
// keys it has), with that module's key names only (never its hashed classes), for the snapshot: the next fix is one look.
export type CoreUiMapCandidate = { score: number; matched: string[]; missing: string[]; keys: string[] };
export const CORE_UI_CANDIDATE_KEYS = 120;

function signatureKeys(signature: CoreUiMapSignature): string[] {
  return [...new Set([...signature.all, ...(signature.any ?? []).flat()])];
}

function looksLikeClassMap(value: any): boolean {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const entries = attempt(() => Object.entries(value)) ?? [];
  return entries.length > 0 && entries.length <= 600 && entries.every(([, v]) => typeof v === "string");
}

export function scoreCandidate(map: any, signature: CoreUiMapSignature): CoreUiMapCandidate | undefined {
  if (!looksLikeClassMap(map)) return undefined;
  const wanted = signatureKeys(signature);
  const matched = wanted.filter(key => has(map, key));
  if (!matched.length) return undefined;
  const keys = (attempt(() => Object.keys(map)) ?? []).slice(0, CORE_UI_CANDIDATE_KEYS);
  return { score: Math.round(matched.length / wanted.length * 100) / 100, matched, missing: wanted.filter(key => !matched.includes(key)), keys };
}

// One walk over every class map the loader exposes (the filter never accepts, so the search visits them all), scoring
// each against every missing signature.
export function closestCoreUiCandidates(ui: any, names: CoreUiMapName[]): Partial<Record<CoreUiMapName, CoreUiMapCandidate>> {
  const best: Partial<Record<CoreUiMapName, CoreUiMapCandidate>> = {};
  if (!names.length) return best;
  const seen = new Set<any>();
  const visit = (value: any) => {
    if (!value || typeof value !== "object" || seen.has(value)) return false;
    seen.add(value);
    for (const name of names) {
      const found = attempt(() => scoreCandidate(value, CORE_UI_MAP_SIGNATURES[name]));
      if (found && (!best[name] || found.score > best[name]!.score)) best[name] = found;
    }
    return false;
  };
  attempt(() => findClassModule(ui, visit));
  if (typeof ui?.findVar === "function") attempt(() => ui.findVar(visit));
  if (typeof ui?.findModuleScopeByVar === "function") attempt(() => ui.findModuleScopeByVar((value: any) => { visit(value); return false; }));
  return best;
}

export function findCoreUiMapsDetailed(ui: any, options: { candidates?: boolean } = {}): {
  maps: CoreUiMaps; routes: Record<string, CoreUiMapRoute>; candidates: Partial<Record<CoreUiMapName, CoreUiMapCandidate>>;
} {
  const maps: CoreUiMaps = {}, routes: Record<string, CoreUiMapRoute> = {};
  const missing: CoreUiMapName[] = [];
  for (const name of Object.keys(CORE_UI_MAP_FILTERS) as CoreUiMapName[]) {
    const found = findCoreUiMapRoute(ui, CORE_UI_MAP_FILTERS[name]);
    routes[name] = found.route;
    if (found.map) maps[name] = found.map; else missing.push(name);
  }
  const candidates = options.candidates === false ? {} : closestCoreUiCandidates(ui, missing);
  return { maps, routes, candidates };
}

export function findCoreUiMaps(ui: any): CoreUiMaps {
  return findCoreUiMapsDetailed(ui).maps;
}

const CLASS_NAME = /^-?[A-Za-z_][\w-]*$/;

// 3.3.4: every target also carries an id-level boost, `:not(#core-ui-boost)` (no element has that id, so it matches
// everything and adds an id's weight). A CSS Loader theme left on (the old Core theme was) used class selectors with
// !important; with the boost Core UI's !important rules out-rank any of them, however many classes deep.
export const CORE_UI_BOOST = ":not(#core-ui-boost)";

// ".hashed.hashed:not(#core-ui-boost)": the class from the map, repeated once, plus the boost. Undefined when the key
// is missing or its value is not a plain class list.
export function classSelector(map: Record<string, string> | undefined, key: string): string | undefined {
  if (!has(map, key)) return undefined;
  const names = String(map![key]).trim().split(/\s+/);
  if (!names.length || !names.every(name => CLASS_NAME.test(name))) return undefined;
  return "." + names.join(".") + "." + names[0] + CORE_UI_BOOST;
}

// ---- The selector table (3.3.4) ----
//
// Every element Core UI aims at, by name, with each way of finding it, best first. A strategy is a selector
// template: `{map.Key}` stands for that class map's class (dropped when the map or key is missing), anything else is
// plain CSS (Steam's unhashed globals, attributes, links). Rules and the snapshot (coreUiSnapshot.ts) read this one
// table, so fixing a selector after the snapshot is one line here.
export const CORE_UI_TARGETS: Record<string, string[]> = {
  header: ["{header.Header}"],
  footer: ["{footer.BasicFooter}"],
  menu: ["{mainmenu.Menu}"],
  menuItem: ["{mainmenu.Item}"],
  qamMenu: ["{quickaccess.Menu}"],
  qamPanel: ["{quickaccess.Container}", "{quickaccess.QuickAccessMenu}", "{quickaccess.ViewPlaceholder}"],
  qamRail: ["{quickaccess.Tabs}", "{quickaccess.TabsWithFooter}", "{quickaccess.TabList}", "{quickaccess.BottomTabs}"],
  qamTab: ["{quickaccess.Tab}"],
  qamTabSelected: ["{quickaccess.Tab}{quickaccess.Selected}", '{quickaccess.Tab}[aria-selected="true"]', "{quickaccess.Tab}.Selected", "{quickaccess.Tab}.Active"],
  qamTitle: ["{quickaccess.Title}", "{quickaccess.HeaderContainer}"],
  qamContent: ["{quickaccess.TabContentColumn}", "{quickaccess.AllTabContents}", "{quickaccess.ContentTransition}"],
  // Decky's plugin list and every ButtonItem: Steam's dialog field rows and their DialogButton.
  // 3.3.8: each also under QuickAccessMenu and Container, so one missing key never drops the rows.
  qamRowField: ["{quickaccess.Menu} {gamepaddialog.Field}", "{quickaccess.QuickAccessMenu} {gamepaddialog.Field}", "{quickaccess.Container} {gamepaddialog.Field}"],
  qamRowButton: ['{quickaccess.Menu} .DialogButton:not([style*="background"])', "{quickaccess.Menu} {dropdown.DropDownControlButton}",
    '{quickaccess.QuickAccessMenu} .DialogButton:not([style*="background"])', "{quickaccess.QuickAccessMenu} {dropdown.DropDownControlButton}",
    '{quickaccess.Container} .DialogButton:not([style*="background"])'],
  qamTitleButton: ["{quickaccess.Title} .DialogButton"],
  context: ["{contextmenu.contextMenuContents}"],
  home: ["{gamepadui.BasicHome}"],
  library: ["{library.GamepadLibrary}"],
  libraryTabRow: ["{tabbedpage.TabRow}", "{tabbedpage.TabHeaderRowWrapper}"],
  libraryTab: ["{tabbedpage.Tab}"],
  libraryTabSelected: ["{tabbedpage.Tab}{tabbedpage.Selected}", '{tabbedpage.Tab}[aria-selected="true"]'],
  libraryTabTitle: ["{tabbedpage.TabTitle}"],
  // Home's tab row (3.3.8): Steam's tabbed page that holds Core Home's tab content (CoreHomeTabs marks each tab's
  // content with data-core-home-tab), so the Library's and a game page's tab bars are not touched.
  homeTabPage: ["{tabbedpage.GamepadTabbedPage}:has([data-core-home-tab])", "{gamepadui.BasicHome}:has([data-core-home-tab])"],
  libraryGrid: ["{library.GamepadLibrary} {appgrid.AppGrid}", "{library.GamepadLibrary} {appgrid.LibraryAppGrid}", "{library.GamepadLibrary}"],
  libraryCapsule: ["{library.GamepadLibrary} {portrait.LibraryItemBox}"],
  collectionTile: ['a[href*="/library/collection/"]', "[data-collection-id]"],
  // Hosts from other plugins (3.3.7): The Collector's ROM game page (CORE_UI_HOSTS, "collector-rom-page").
  collectorRomPage: [".romm-core-page"],
};

const TOKEN = /\{([a-z]+)\.([A-Za-z0-9_]+)\}/g;

// One template against the maps: the strong form (with Core UI's specificity) and the plain form (querySelector).
// Undefined when any {map.Key} in it is missing on this client.
export function resolveTemplate(maps: CoreUiMaps, template: string, plain = false): string | undefined {
  let missing = false;
  const out = template.replace(TOKEN, (_all, map: string, key: string) => {
    const value = plain ? plainSelector((maps as any)[map], key) : classSelector((maps as any)[map], key);
    if (!value) missing = true;
    return value ?? "";
  });
  return missing ? undefined : out;
}

// Every strategy of a target that resolves on this client, best first.
export function targetSelectors(maps: CoreUiMaps, id: string, plain = false): string[] {
  return (CORE_UI_TARGETS[id] ?? []).map(template => resolveTemplate(maps, template, plain)).filter((value): value is string => !!value);
}

// The whole table resolved, for the snapshot: per target, each strategy and whether it resolved.
export function resolvedTargetTable(maps: CoreUiMaps): Record<string, Array<{ strategy: string; selector?: string }>> {
  const out: Record<string, Array<{ strategy: string; selector?: string }>> = {};
  for (const [id, list] of Object.entries(CORE_UI_TARGETS)) out[id] = list.map(strategy => ({ strategy, selector: resolveTemplate(maps, strategy, true) }));
  return out;
}

type Need = [CoreUiMapName, string];
// needs: every class must exist. needsAny (3.3.8): at least one must (a rule with fallbacks never hangs on one key).
type Rule = { id: string; needs: Need[]; needsAny?: Need[]; targets?: string[]; css: (s: (map: CoreUiMapName, key: string) => string, t: (id: string) => string[]) => string };

const imp = (block: Record<string, string>) => Object.entries(block).map(([key, value]) => `${key}:${value} !important`).join(";");
const CLEAR = { "background-color": "transparent", "background-image": "none", "box-shadow": "none" };
// Pure black, opaque: no translucency, no tint, no blur behind, no gradient or shadow (item 6 of the TV list).
const SOLID_BLACK = { "background-color": C.black, "background-image": "none", "box-shadow": "none", "backdrop-filter": "none", filter: "none", opacity: "1" };

function rules(mark: string | undefined): Rule[] {
  // Rule-local helper: one ROOT-prefixed list for a target.
  const under = (list: string[], tail = "") => list.map(sel => `${ROOT} ${sel}${tail}`).join(",");
  const list: Rule[] = [
    // The float radius token, inherited by everything in the page; Steam's global confirm dialogs use it.
    { id: "corners", needs: [], css: () =>
      `${ROOT} body{${imp({ [CORE_RADIUS_FLOAT_VAR]: CORE_RADIUS_FLOAT })}}${ROOT} .GenericConfirmDialog{${imp({ "border-radius": FLOAT })}}` },
    // Global chrome: the top bar and the button-hint bar are clear over game art and pure black anywhere else
    // (the runtime says which: data-core-ui-chrome), and whenever Steam paints them opaque.
    // 3.3.9 (TV: "either fully black or fully transparent, including search"): the top bar is fully transparent
    // everywhere, over art and over black alike: no bar, no blur, no gradient, whatever Steam marks it (HeaderOpaque
    // included). Clock, icons and avatar steel or white, with one soft text shadow so they read over bright art.
    { id: "chrome-header", needs: [["header", "Header"]], css: s => {
      const header = `${ROOT} ${s("header", "Header")}`;
      const opaque = s("header", "HeaderOpaque") ? `${ROOT} ${s("header", "HeaderOpaque")}` : "";
      const all = [header, ...(opaque ? [opaque] : [])];
      return `${all.flatMap(sel => [sel, `${sel}::before`, `${sel}::after`, `${sel}[${CORE_UI_CHROME_ATTRIBUTE}]`]).join(",")}{${imp({ ...CLEAR, "backdrop-filter": "none", filter: "none",
          "border-bottom-color": "transparent", "border-radius": SQUARE, opacity: "1" })}}` +
        `${header}{${imp({ color: C.steel })}}${header} :is(svg,span,div){${imp({ "text-shadow": CORE_HEADER_TEXT_SHADOW })}}`;
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

    // The top bar's universal search (3.3.8, TV: "the search bar is ugly"). Found through the header's class map plus
    // structure only (the field is the header's element that holds the <input>), never a hashed class: a square
    // gunmetal field with steel text, a #9AA4AE placeholder and a steel magnifier; focused, a 2px crimson underline and
    // no glow. Its height is the top bar's (Steam already sizes it to the bar); Steam's blue is gone.
    // 3.3.9: transparent like the bar: the steel magnifier and text over a 1px steel underline, 2px crimson when focused.
    { id: "search-field", needs: [["header", "Header"]], css: s => {
      const header = `${ROOT} ${s("header", "Header")}`;
      const field = `${header} div:has(> input)`;
      return `${field}{${imp({ "background-color": "transparent", "background-image": "none", "border-radius": SQUARE, "box-shadow": `inset 0 -1px 0 ${C.steel}`, border: "0", outline: "none", filter: "none", "backdrop-filter": "none" })}}` +
        `${field} > input{${imp({ color: C.steel, "background-color": "transparent", "background-image": "none", "box-shadow": "none", border: "0", outline: "none", "caret-color": C.crimson })}}` +
        `${field} > input::placeholder{${imp({ color: C.hint, opacity: "1" })}}` +
        `${field} > svg,${field} > svg *{${imp({ color: C.steel, fill: C.steel })}}` +
        `${field}:focus-within,${field}:has(> input:focus),${field}.gpfocuswithin{${imp({ "background-color": "transparent", "box-shadow": `inset 0 -2px 0 ${C.crimson}`, outline: "none", filter: "none" })}}`;
    } },
    // Steam's dialog field rows everywhere (3.3.8: Settings rows, Downloads, any page built from DialogField): no blue
    // band; the focused row gunmetal with the square crimson left bar, like the Steam menu. Quick Access keeps its own
    // (more specific) row rules.
    { id: "dialog-fields", needs: [["gamepaddialog", "Field"]], css: s => {
      const field = `${ROOT} ${s("gamepaddialog", "Field")}`;
      const focus = `${field}:is(.gpfocus,.gpfocuswithin,:focus-visible)`;
      return `${field}::before,${field}::after{${imp({ "background-image": "none", "box-shadow": "none" })}}` +
        `${focus}{${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": `inset 3px 0 0 ${C.crimson}`, "border-radius": SQUARE, filter: "none", animation: "none", outline: "none" })}}` +
        `${focus}::before,${focus}::after{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": "none" })}}` +
        `${focus} ${s("gamepaddialog", "FieldLabel")}{${imp({ color: C.white })}}`;
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
    { id: "qam-panel", needs: [], needsAny: [["quickaccess", "Menu"], ["quickaccess", "QuickAccessMenu"], ["quickaccess", "Container"]], css: s => {
      const candidates = ["Container", "QuickAccessMenu", "ViewPlaceholder"].filter(key => s("quickaccess", key) !== "")
        .map(key => `${ROOT} ${s("quickaccess", key)}`);
      return (s("quickaccess", "Menu") ? `${ROOT} ${s("quickaccess", "Menu")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none", "border-radius": SQUARE })}}` : "") +
        (candidates.length ? `${candidates.join(",")}{${imp({ ...CLEAR, "backdrop-filter": "none", "border-radius": SQUARE })}}` +
          `${candidates.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}="1"]`).join(",")}{${imp({ "background-color": C.black, "background-image": "none" })}}` : "");
    } },
    // Everything over the stars is clear: the content column, plugin panels (Decky's and every plugin's own,
    // CSS Loader's included), and any large opaque layer the runtime finds inside the panel.
    // 3.3.8: TabContentColumn is gone on the November 2026 client; any one of the content layers is enough.
    { id: "qam-clear", needs: [], needsAny: [["quickaccess", "TabContentColumn"], ["quickaccess", "AllTabContents"], ["quickaccess", "ContentTransition"], ["quickaccess", "TabGroupPanel"]], css: s => {
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
    // 3.3.8: the rail's inner list and bottom group (TabList, BottomTabs; new on the November 2026 client) go clear too.
    { id: "qam-tabs", needs: [["quickaccess", "Tab"]], needsAny: [["quickaccess", "Tabs"], ["quickaccess", "TabsWithFooter"], ["quickaccess", "TabList"]], css: s =>
      (s("quickaccess", "Tabs") ? `${ROOT} ${s("quickaccess", "Tabs")}{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": `inset -1px 0 0 ${C.gunmetal}` })}}` : "") +
      (["TabList", "BottomTabs"].filter(key => s("quickaccess", key)).map(key => `${ROOT} ${s("quickaccess", key)}`).join(",") || `${ROOT} [data-core-ui-none]`) +
      `{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": "none" })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}:is(.gpfocus,:focus-visible){${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", filter: "none", "border-radius": SQUARE })}}` },
    // The open tab: gunmetal, never Steam's grey-blue gradient (TV photo, 3.3.3). Every way Steam may mark it is in the
    // selector table (qamTabSelected).
    { id: "qam-tab-selected", needs: [], targets: ["qamTabSelected"], css: (_s, t) =>
      `${under(t("qamTabSelected"))}{${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", filter: "none", "border-radius": SQUARE })}}` },
    // Rail icons (3.3.4): Core's and the Eternal plugins' glyphs fill the cell like Steam's own (CORE_RAIL_ICON_PX).
    { id: "qam-rail-icons", needs: [], targets: ["qamTab"], css: (_s, t) =>
      `${under(t("qamTab"), " [data-core-rail-icon]")}{${imp({ display: "flex", width: `${CORE_RAIL_ICON_PX}px`, height: `${CORE_RAIL_ICON_PX}px`,
        "min-width": `${CORE_RAIL_ICON_PX}px`, "min-height": `${CORE_RAIL_ICON_PX}px`, "align-items": "center", "justify-content": "center" })}}` +
      `${under(t("qamTab"), " [data-core-rail-icon] svg")}{${imp({ width: "100%", height: "100%", "shape-rendering": "geometricPrecision" })}}` },
    { id: "qam-tabs-footer", needs: [["quickaccess", "TabsWithFooter"]], css: s =>
      `${ROOT} ${s("quickaccess", "TabsWithFooter")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` },
    // The footer bar: black.
    { id: "qam-footer", needs: [["footer", "QuickAccessFooter"]], css: s =>
      `${ROOT} ${s("footer", "QuickAccessFooter")},${ROOT} ${s("footer", "QuickAccessFooter")}::before{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },
    { id: "qam-footer-shadow", needs: [["quickaccess", "FooterBoxShadow"]], css: s =>
      `${ROOT} ${s("quickaccess", "FooterBoxShadow")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` },
    // Plugin content: Steam's buttons and dropdowns inside Quick Access lose the grey-blue gradient. Gunmetal,
    // raised plus a 2px crimson ring when focused. DialogButton and gpfocus are Steam's unhashed global classes.
    // 3.3.4 (TV photo of Decky's main page): rows are gunmetal with steel text, a focused row raised gunmetal with the
    // square crimson left bar (never Steam's white); the title's Store / Settings buttons gunmetal with the same bar.
    // A button that sets its own background inline (Jarvis's crimson "listening" buttons) keeps it.
    { id: "qam-controls", needs: [], targets: ["qamRowButton"], css: (_s, t) => qamRowCss(t("qamRowButton"), t("qamRowField")) },

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
    // Home is first about the art (3.3.9, TV: "more inspiration from the Art Hero theme", with the user's Art Hero options:
    // Expanded Hero, Linear Gradient, carousel height 5, larger carousel font, font height -4, cover/center). The focused
    // game's hero fills Home (the recent section is one screen tall) at full brightness, with a short fade at the bottom
    // only; the carousel sits low over the art, the focused game's name in larger type above its capsule; Steam's
    // "Recent games" header and the green play triangle are hidden. The tabs and shelves are below the fold, on black
    // with stars (the home host). Measures: CORE_HOME_ART.
    { id: "home-hero", needs: [["recentgames", "RecentGamesBackgroundContainer"]], css: s => {
      const a = CORE_HOME_ART;
      const r = (key: string) => s("recentgames", key) ? `${ROOT} ${s("recentgames", key)}` : "";
      const k = (key: string) => s("carousel", key) ? `${ROOT} ${s("carousel", key)}` : "";
      const mask = `linear-gradient(180deg, #000000 ${a.fadeFrom}%, transparent 100%)`;
      const art = [r("RecentGamesBackgroundContainer"), r("RecentGamesBackgroundImages")].filter(Boolean);
      const headers = [r("RecentGamesHeader"), r("RecentGamesHeaderLabel")].filter(Boolean);
      const label = k("CarouselGameLabelWrapper");
      const shown = label ? [`${ROOT} .gpfocuswithin > ${s("carousel", "CarouselGameLabelWrapper")}`,
        ...(s("carousel", "ShowAsHovered") ? [`${label}${s("carousel", "ShowAsHovered")}`] : [])] : [];
      return (s("home", "RecentSection") ? `${ROOT} ${s("home", "RecentSection")}{${imp({ height: "100vh", padding: "0" })}}` : "") +
        `${art.join(",")}{${imp({ "-webkit-mask-image": mask, "mask-image": mask, "min-height": "100vh", "z-index": "1" })}}` +
        (r("RecentGamesBackground") ? `${r("RecentGamesBackground")}{${imp({ position: "absolute", left: "0", width: "100vw", "min-width": "100vw", height: "100vh", "max-height": "none",
          opacity: "1", filter: "none", "border-radius": SQUARE })}}` : "") +
        (r("RecentGamesBackgroundImage") ? `${r("RecentGamesBackgroundImage")}{${imp({ "object-fit": "cover", "object-position": "center", opacity: "1", filter: "none" })}}` : "") +
        (headers.length ? `${headers.join(",")}{${imp({ display: "none" })}}` : "") +
        (r("RecentGamesContainer") ? `${r("RecentGamesContainer")}{${imp({ "margin-top": "-1vh", "padding-top": "0" })}}` : "") +
        (r("RecentGamesInnerContainer") ? `${r("RecentGamesInnerContainer")}{${imp({ position: "relative", top: `${a.carouselTop}vh`, height: `${a.carouselHeight}vh` })}}` : "") +
        (shown.length ? `${shown.join(",")}{${imp({ position: "absolute", top: `-${a.labelEm}em`, visibility: "visible", "font-size": `${a.labelPx}px`, color: C.white })}}` : "") +
        (k("Play") && s("carousel", "ActionIcon") ? `${k("Play")} ${s("carousel", "ActionIcon")}{${imp({ display: "none" })}}` : "");
    } },
    { id: "home-glow", needs: [["carousel", "CarouselCapsuleBackgroundGlow"]], css: s =>
      `${ROOT} ${s("carousel", "CarouselCapsuleBackgroundGlow")}{${imp({ opacity: "0" })}}` },

    // ---- Phase 2: the Library (the Library board: black grid, tabs like Quick Access, crimson focus ring). ----
    { id: "library-page", needs: [["library", "GamepadLibrary"]], css: s => {
      const library = `${ROOT} ${s("library", "GamepadLibrary")}`;
      return `${library}{${imp({ "background-color": C.black, "background-image": "none" })}}` +
        (s("library", "CollectionHeader") ? `${ROOT} ${s("library", "CollectionHeader")}{${imp({ color: C.steel, "font-size": "12px", "letter-spacing": "0.1em", "text-transform": "uppercase" })}}` : "");
    } },
    { id: "library-grid", needs: [["appgrid", "AppGridSectionLabel"]], css: s =>
      (s("appgrid", "LibraryImageBackgroundGlow") ? `${ROOT} ${s("appgrid", "LibraryImageBackgroundGlow")}{${imp({ opacity: "0" })}}` : "") +
      `${ROOT} ${s("appgrid", "AppGridSectionLabel")}{${imp({ color: C.steel, "font-size": "12px", "letter-spacing": "0.1em", "text-transform": "uppercase" })}}` +
      (s("appgrid", "Rule") ? `${ROOT} ${s("appgrid", "Rule")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "border-color": C.gunmetal })}}` : "") },
    // Focused capsule. The Library: a 3px crimson ring, no glow, lifted 6px (still under reduced motion). Home (3.3.8,
    // TV at 1348 x 758: the focused capsule grew out of its row and off the screen): capsules drawn at
    // CORE_HOME_CAPSULE_SCALE inside their slots, the ring drawn inside the capsule and no lift, so focus never reaches
    // past the row or the screen's edges.
    { id: "capsule-focus", needs: [["portrait", "LibraryItemBox"]], css: s => {
      const box = s("portrait", "LibraryItemBox");
      const libraryScopes = [s("library", "GamepadLibrary")].filter(Boolean);
      const homeScopes = [s("home", "TabbedContent"), s("recentgames", "RecentGamesContainer"), s("gamepadui", "BasicHome")].filter(Boolean);
      if (!libraryScopes.length && !homeScopes.length) return "";
      const items = libraryScopes.map(scope => `${ROOT} ${scope} ${box}`);
      const focused = items.map(item => `${item}:is(.gpfocus,:focus)`);
      // Never a Library capsule (Steam may nest the Library inside BasicHome's tree).
      const notLibrary = libraryScopes.length ? `:not(${libraryScopes[0]} *)` : "";
      const home = homeScopes.map(scope => `${ROOT} ${scope} ${box}${notLibrary}`);
      const homeFocused = home.map(item => `${item}:is(.gpfocus,:focus)`);
      return (items.length ? `${items.join(",")}{${imp({ transition: "translate 0.15s ease-out" })}}` +
        `${focused.join(",")}{${imp({ outline: `3px solid ${C.crimson}`, "outline-offset": "0", animation: "none", filter: "none", translate: "0 -6px" })}}` +
        `@media (prefers-reduced-motion: reduce){${[...items, ...focused].join(",")}{${imp({ transition: "none", translate: "none" })}}}` : "") +
        (home.length ? `${home.join(",")}{${imp({ scale: String(CORE_HOME_CAPSULE_SCALE), "transform-origin": "50% 50%", translate: "none" })}}` +
          `${homeFocused.join(",")}{${imp({ outline: `3px solid ${C.crimson}`, "outline-offset": "-3px", animation: "none", filter: "none", translate: "none", "box-shadow": "none" })}}` : "");
    } },
    // The Library's tab bar (the game page has its own rule since 3.3.9, game-screen-two). 3.3.4, from the TV photos: one marker
    // per tab and no more. Built by libraryTabCss (below), from the selector table.
    { id: "library-tabs", needs: [["library", "GamepadLibrary"]], targets: ["libraryTab"], css: (s, t) => {
      const lib = s("library", "GamepadLibrary");
      const page = s("tabbedpage", "GamepadTabbedPage");
      const scopes = [`${ROOT} ${lib}`, ...(page ? [`${ROOT} ${page}:has(${lib})`] : [])];
      return libraryTabCss({ scopes, tab: t("libraryTab"), selected: t("libraryTabSelected"), title: t("libraryTabTitle"),
        row: t("libraryTabRow"), count: s("tabbedpage", "TabCount") || undefined });
    } },
    // Home's tab row (3.3.8, TV: Steam's pill still showed on Jarvis brief / Consoles / Club / On this day): the same
    // single marker as the Library, over a clear row so Home's stars show through it.
    { id: "home-tabs", needs: [], targets: ["homeTabPage", "libraryTab"], css: (_s, t) =>
      libraryTabCss({ scopes: t("homeTabPage").map(sel => `${ROOT} ${sel}`), tab: t("libraryTab"), selected: t("libraryTabSelected"), title: t("libraryTabTitle"),
        row: t("libraryTabRow"), rowFill: "transparent" }) },
    // ---- 3.3.9: Steam's game page as the clean game view (CORE-UI.md, "The game view"), matching The Collector's ROM
    // page. Screen one: the art fills the view above the footer (cover, 60% brightness, Steam's top gradient cleared),
    // the logo locked bottom-left at most 120px, an 80px transparent play bar. Measures: CORE_GAME_VIEW.
    { id: "game-hero", needs: [["appheader", "TopCapsule"], ["appheader", "ImgSrc"]], css: s => {
      const g = CORE_GAME_VIEW, screen = `calc(100vh - ${g.playbar + g.footer}px)`;
      const cap = `${ROOT} ${s("appheader", "TopCapsule")}`;
      const state = (key: string) => s("appheader", key) ? `${cap}${s("appheader", key)}` : "";
      const full = ["FullscreenEnterActive", "FullscreenEnterDone", "FullscreenExitStart"].map(state).filter(Boolean);
      const back = ["FullscreenExitActive", "FullscreenExitDone"].map(state).filter(Boolean);
      // Steam's fullscreen enter (Play pressed): Clean Gameview's fix as a transition from screen one's height to the
      // full screen and back, so the art grows instead of jumping (transitions out-rank !important in the cascade).
      return `${cap}{${imp({ height: screen, transition: `height ${g.enterMs}ms ease-out` })}}` +
        (full.length ? `${full.join(",")}{${imp({ height: "100vh" })}}` : "") +
        (back.length ? `${back.join(",")}{${imp({ height: screen })}}` : "") +
        (s("appheader", "HeaderBackgroundImage") ? `${ROOT} ${s("appheader", "HeaderBackgroundImage")}{${imp({ height: "100vh" })}}` : "") +
        `${ROOT} ${s("appheader", "ImgSrc")}{${imp({ "object-fit": "cover", "object-position": "center", filter: `brightness(${g.brightness})` })}}` +
        (s("appheader", "TopGradient") ? `${ROOT} ${s("appheader", "TopGradient")}{${imp({ "background-color": "transparent", "background-image": "none", background: "transparent" })}}` : "") +
        (s("appheader", "BoxSizer") ? `${ROOT} ${s("appheader", "BoxSizer")}{${imp({ top: `calc(100vh - ${g.logo}px * 1.3 - ${g.playbar + g.footer}px)`, "max-height": `${g.logo}px`, "z-index": "2" })}}` : "") +
        `@media (prefers-reduced-motion: reduce){${cap}{${imp({ transition: "none" })}}}`;
    } },
    { id: "game-playbar", needs: [["appdetails", "PlaySection"]], css: s => {
      const g = CORE_GAME_VIEW;
      const bar = `${ROOT} ${s("appdetails", "PlaySection")}`;
      const play = s("appdetails", "AppActionButton") ? `${ROOT} ${s("appdetails", "AppActionButton")}` : "";
      const buttons = s("appdetails", "AppButtons") ? `${ROOT} ${s("appdetails", "AppButtons")}` : "";
      const square = buttons ? [`${buttons} > *`, `${buttons} .DialogButton`] : [];
      return `${bar}{${imp({ height: `${g.playbar + g.footer}px`, "min-height": `${g.playbar + g.footer}px`, "padding-bottom": `${g.footer}px`, "box-sizing": "border-box",
          "background-color": "transparent", "background-image": "none", "backdrop-filter": "none", "box-shadow": "none", color: C.steel })}}` +
        (s("appdetails", "AppDetailsRoot") ? `${ROOT} ${s("appdetails", "AppDetailsRoot")}{${imp({ "background-color": "transparent", "background-image": "none", "backdrop-filter": "none" })}}` : "") +
        (play ? `${play}{${imp({ "background-color": C.crimson, "background-image": "none", color: C.white, "border-radius": SQUARE, "box-shadow": "none", filter: "none" })}}` +
          `${play} *{${imp({ color: C.white })}}` +
          `${play}:is(.gpfocus,:focus-visible,:focus){${imp({ outline: `3px solid ${C.white}`, "outline-offset": "0", "box-shadow": "none", filter: "none", animation: "none" })}}` : "") +
        (square.length ? `${square.join(",")}{${imp({ "min-width": `${g.button}px`, height: `${g.button}px`, "background-color": C.black, "background-image": "none", color: C.steel, "border-radius": SQUARE, "box-shadow": "none" })}}` +
          `${square.map(sel => `${sel}:is(.gpfocus,:focus-visible)`).join(",")}{${imp({ "background-color": C.gunmetal, color: C.white, outline: `2px solid ${C.crimson}`, "outline-offset": "0", filter: "none" })}}` : "");
    } },
    // The cloud as a button (Clean Gameview's "cloud as a button"): the status text hidden, its holder a 48px square.
    { id: "game-cloud", needs: [["playsection", "CloudStatusLabel"]], css: s => {
      const label = s("playsection", "CloudStatusLabel");
      const holder = `${ROOT} :has(> ${label})`;
      return `${ROOT} ${label}{${imp({ display: "none" })}}` +
        `${holder}{${imp({ width: `${CORE_GAME_VIEW.button}px`, height: `${CORE_GAME_VIEW.button}px`, "box-sizing": "border-box", display: "flex", "align-items": "center",
          "justify-content": "center", "background-color": C.black, "background-image": "none", "border-radius": SQUARE, "box-shadow": "none" })}}` +
        `${holder}::before,${holder}::after{${imp({ "background-color": "transparent", "background-image": "none" })}}` +
        `${holder}:is(.gpfocus,:focus-visible){${imp({ "background-color": C.gunmetal, outline: `2px solid ${C.crimson}`, "outline-offset": "0" })}}`;
    } },
    // Screen two: Steam's sections and tabs over black and the Starfield ("game-page" host, stars only below the play
    // bar). The container itself stays clear, so the art behind the play bar is never covered.
    { id: "game-screen-two", needs: [["appdetails", "AppDetailsContainer"]], targets: ["libraryTab"], css: (s, t) => {
      const box = `${ROOT} ${s("appdetails", "AppDetailsContainer")}`;
      return `${box}{${imp({ "background-color": "transparent", "background-image": "none" })}}` +
        libraryTabCss({ scopes: [box], tab: t("libraryTab"), selected: t("libraryTabSelected"), title: t("libraryTabTitle"), row: t("libraryTabRow"), rowFill: "transparent" });
    } },
    { id: "game-sections", needs: [["appsection", "AppDetailsSectionContainer"]], css: s => {
      const cards = ["AppDetailsSectionContainer", "RightColumnSection"].filter(key => s("appsection", key)).map(key => `${ROOT} ${s("appsection", key)}`);
      return `${cards.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "border-radius": FLOAT, "box-shadow": "none", color: C.steel })}}` +
        `${cards.map(sel => `${sel}:is(.gpfocus,.gpfocuswithin,:focus-visible)`).join(",")}{${imp({ "background-color": C.raised, outline: `2px solid ${C.crimson}`, "outline-offset": "0", filter: "none" })}}`;
    } },

    // ---- 3.3.9: the surfaces still in Steam's blue (CORE-UI.md, "Steam's other pages"): black pages, gunmetal rows,
    // the square crimson focus bar, steel text.
    { id: "settings-pages", needs: [], needsAny: [["settings", "PagedSettingsDialog_PageListColumn"], ["settings", "PageListColumn"]], css: s => {
      const pick = (...keys: string[]) => keys.map(key => s("settings", key)).filter(Boolean).map(sel => `${ROOT} ${sel}`);
      const black = pick("PagedSettingsDialog", "PagedSettingsDialog_PageListColumn", "PageListColumn", "PagedSettingDialog_ContentColumn", "PagedSettingsDialog_PageContent");
      const items = pick("PagedSettingsDialog_PageListItem", "PageListItem");
      const active = s("settings", "Active") ? items.map(item => `${item}${s("settings", "Active")}`) : [];
      const focus = items.map(item => `${item}:is(.gpfocus,:focus-visible)`);
      return (black.length ? `${black.join(",")}{${imp(SOLID_BLACK)}}` : "") +
        (items.length ? `${items.join(",")}{${imp({ "background-color": "transparent", "background-image": "none", color: C.steel, "border-radius": SQUARE, "box-shadow": "none" })}}` +
          `${items.map(item => `${item} *`).join(",")}{${imp({ color: "inherit" })}}` : "") +
        (active.length ? `${active.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}` })}}` : "") +
        (focus.length ? `${focus.join(",")}{${imp({ "background-color": C.raised, "background-image": "none", color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}`, outline: "none", filter: "none" })}}` : "");
    } },
    // Toggles (gamepaddialog's copy with On, and the small toggle map): the rail gunmetal, crimson when on, a white knob.
    { id: "toggles", needs: [], needsAny: [["gamepaddialog", "ToggleRail"], ["toggle", "ToggleRail"]], css: s => {
      const out: string[] = [];
      for (const map of ["gamepaddialog", "toggle"] as CoreUiMapName[]) {
        if (!s(map, "ToggleRail")) continue;
        const rail = `${ROOT} ${s(map, "ToggleRail")}`;
        out.push(`${rail}{${imp({ "background-color": C.raised, "background-image": "none", "box-shadow": "none" })}}`);
        if (s(map, "ToggleSwitch")) out.push(`${ROOT} ${s(map, "ToggleSwitch")}{${imp({ "background-color": C.white, "background-image": "none", "box-shadow": "none" })}}`);
        const on = s("gamepaddialog", "On");
        if (on && s(map, "Toggle")) out.push(`${ROOT} ${s(map, "Toggle")}${on} ${s(map, "ToggleRail")},${rail}${on}{${imp({ "background-color": C.crimson, "background-image": "none" })}}`);
      }
      return out.join("");
    } },
    { id: "downloads-page", needs: [["downloads", "DownloadsPage"]], css: s => {
      const d = (key: string) => s("downloads", key) ? `${ROOT} ${s("downloads", key)}` : "";
      const item = d("SectionItem");
      return `${d("DownloadsPage")}{${imp(SOLID_BLACK)}}` +
        (item ? `${item}{${imp({ "background-color": C.gunmetal, "background-image": "none", "border-radius": SQUARE, "box-shadow": "none", color: C.steel })}}` +
          `${item}:is(.gpfocus,.gpfocuswithin,:focus-visible){${imp({ "background-color": C.raised, "box-shadow": `inset 3px 0 0 ${C.crimson}`, filter: "none", outline: "none" })}}` : "") +
        (d("HoverRing") ? `${d("HoverRing")}{${imp({ display: "none" })}}` : "") +
        (d("SectionTitle") ? `${d("SectionTitle")}{${imp({ color: C.steel, "letter-spacing": "0.1em", "text-transform": "uppercase" })}}` : "") +
        (d("ProgressBar") ? `${d("ProgressBar")}{${imp({ "background-color": C.raised, "background-image": "none" })}}${d("ProgressBar")} > *{${imp({ "background-color": C.crimson, "background-image": "none" })}}` : "");
    } },
    { id: "download-bar", needs: [["downloadbar", "BottomBar"]], css: s => {
      const b = (key: string) => s("downloadbar", key) ? `${ROOT} ${s("downloadbar", key)}` : "";
      return `${b("BottomBar")}{${imp(SOLID_BLACK)}}` +
        (b("ProgressBar") ? `${b("ProgressBar")}{${imp({ "background-color": C.raised, "background-image": "none" })}}${b("ProgressBar")} > *{${imp({ "background-color": C.crimson, "background-image": "none" })}}` : "");
    } },
    // The Steam menu while a game runs: its three columns black, the rows gunmetal on focus with the crimson bar.
    { id: "ingame-menu", needs: [], needsAny: [["mainmenu", "AppColumn"], ["mainmenu", "NavigationColumn"]], css: s => {
      const m = (key: string) => s("mainmenu", key) ? `${ROOT} ${s("mainmenu", key)}` : "";
      const black = ["AppRunningControls", "NavigationColumn", "NavigationBox", "AppColumn", "AppColumnContent", "FocusedColumn", "NavColumnFocused", "ControllerColumnFocused", "ActiveContent"].map(m).filter(Boolean);
      const rows = [m("NavigationMenuItem"), m("SelectableAppWindow")].filter(Boolean);
      return (black.length ? `${black.join(",")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none", "backdrop-filter": "none" })}}` : "") +
        (m("CurrentGameBackground") ? `${m("CurrentGameBackground")}{${imp({ opacity: "0.35", filter: "none" })}}` : "") +
        (rows.length ? `${rows.join(",")}{${imp({ "background-color": "transparent", "background-image": "none", color: C.steel, "border-radius": SQUARE, "box-shadow": "none" })}}` +
          `${rows.map(row => `${row}:is(.gpfocus,:focus-visible,:focus-within)`).join(",")}{${imp({ "background-color": C.gunmetal, color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}`, filter: "none", outline: "none" })}}` : "") +
        (m("NavigationMenuItemSeparator") ? `${m("NavigationMenuItemSeparator")}{${imp({ "background-color": C.gunmetal, "background-image": "none" })}}` : "");
    } },
    { id: "overlay-configurator", needs: [], needsAny: [["overlay", "OverlayPopup"], ["configurator", "ControllerConfiguratorMain"], ["configsummary", "ControllerConfiguratorSummary"]], css: s => {
      const k = (map: CoreUiMapName, key: string) => s(map, key) ? `${ROOT} ${s(map, key)}` : "";
      const black = [k("overlay", "OverlayPopup"), k("overlay", "OverlayPopupBody"), k("overlay", "Toolbar"), k("overlay", "SettingsModalRoot"),
        k("configurator", "ControllerConfiguratorBackgroundContainer"), k("configurator", "ControllerConfiguratorMain"), k("configsummary", "ControllerConfiguratorSummary")].filter(Boolean);
      const rows = [k("overlay", "ToolbarButton"), k("overlay", "BackToGame"), k("overlay", "ExitGameBtn"), k("configurator", "CurrentConfigurationButton"),
        k("configurator", "MainOptionsButton"), k("configsummary", "StandardControl")].filter(Boolean);
      return (black.length ? `${black.join(",")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none", "backdrop-filter": "none" })}}` : "") +
        (rows.length ? `${rows.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", color: C.steel, "border-radius": SQUARE, "box-shadow": "none" })}}` +
          `${rows.map(row => `${row}:is(.gpfocus,:focus-visible)`).join(",")}{${imp({ "background-color": C.raised, color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}`, filter: "none", outline: "none" })}}` : "");
    } },
    // Search: the field by its own map now (the 3.3.8 structural rule stays as the fallback), results and suggestions.
    { id: "search-pages", needs: [], needsAny: [["searchbar", "SearchFieldBackground"], ["searchresults", "GamepadSearch"], ["suggestions", "Suggestions"]], css: s => {
      const k = (map: CoreUiMapName, key: string) => s(map, key) ? `${ROOT} ${s(map, key)}` : "";
      const field = k("searchbar", "SearchFieldBackground");
      const results = [k("searchresults", "ResultTemplate"), k("suggestions", "Suggestion")].filter(Boolean);
      return (field ? `${field}{${imp({ "background-color": "transparent", "background-image": "none", "border-radius": SQUARE, "box-shadow": `inset 0 -1px 0 ${C.steel}`, border: "0", "backdrop-filter": "none" })}}` +
          `${field}:focus-within{${imp({ "box-shadow": `inset 0 -2px 0 ${C.crimson}` })}}` : "") +
        (k("searchbar", "SearchBox") ? `${k("searchbar", "SearchBox")}{${imp({ color: C.steel, "background-color": "transparent", "caret-color": C.crimson })}}` : "") +
        [k("searchbar", "SearchIconLeft"), k("searchbar", "SearchIconRight")].filter(Boolean).map(sel => `${sel},${sel} *{${imp({ color: C.steel, fill: C.steel })}}`).join("") +
        ([k("searchresults", "GamepadSearch"), k("suggestions", "Suggestions")].filter(Boolean).map(sel => `${sel}{${imp(SOLID_BLACK)}}`).join("")) +
        (results.length ? `${results.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", color: C.steel, "border-radius": SQUARE, "box-shadow": "none" })}}` +
          `${results.map(row => `${row}:is(.gpfocus,:focus-visible,:hover)`).join(",")}{${imp({ "background-color": C.raised, color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}`, filter: "none", outline: "none" })}}` : "");
    } },
    // Library grid (3.3.4): capsules drawn about 16% smaller inside their slots, so even black gutters (and the stars)
    // show between them. Steam's slots and focus order are untouched; art stays art (no stars behind it).
    { id: "library-capsules", needs: [], targets: ["libraryCapsule"], css: (_s, t) => libraryCapsuleCss(t("libraryCapsule")) },
  ];
  return list;
}

// ---- Builders (pure; tests call them directly) ----

export const CORE_RAIL_ICON_PX = 30;
// 3.3.9: Home after Art Hero with the user's options: the art fades out over its last 12%, the carousel 50vh down in a
// 70vh row (carousel height 5), the focused game's name 4em above its capsule (font height -4) at 22px (larger).
export const CORE_HOME_ART = { fadeFrom: 88, carouselTop: 50, carouselHeight: 70, labelEm: 4, labelPx: 22 } as const;
// 3.3.9: the top bar's only shadow, for text over bright art (soft, dark, no glow colour).
export const CORE_HEADER_TEXT_SHADOW = "0 1px 3px rgba(0,0,0,0.65)";
// 3.3.9: the clean game view's measures (Clean Gameview v1.8 Lz's defaults, as The Collector's GAME_VIEW): an 80px play bar
// over the 40px footer, the logo at most 120px, the art at 60% brightness, 48px square buttons, a 600ms fullscreen enter.
export const CORE_GAME_VIEW = { playbar: 80, footer: 40, logo: 120, brightness: 0.6, button: 48, enterMs: 600 } as const;
export const CORE_LIBRARY_CAPSULE_SCALE = 0.84;
// Home's capsules (3.3.8): smaller at the TV's 1348 x 758 viewport, so Steam's focus growth stays inside the row.
export const CORE_HOME_CAPSULE_SCALE = 0.8;
export const CORE_TAB_ICON_PX = 24;

// Quick Access rows (Decky's plugin list, every ButtonItem, dropdowns) and their dialog fields.
export function qamRowCss(buttons: string[], fields: string[] = []): string {
  if (!buttons.length) return "";
  const at = (list: string[], tail = "") => list.map(sel => `${ROOT} ${sel}${tail}`).join(",");
  const focus = ":is(.gpfocus,:focus-visible,:focus)";
  return `${at(buttons)}{${imp({ "background-color": C.gunmetal, "background-image": "none", color: C.steel, "box-shadow": "none", "text-shadow": "none", "border-radius": SQUARE, filter: "none" })}}` +
    `${at(buttons, focus)}{${imp({ "background-color": C.raised, "background-image": "none", color: C.white, "box-shadow": `inset 3px 0 0 ${C.crimson}`, filter: "none", animation: "none", "border-radius": SQUARE, outline: "none" })}}` +
    `${at(buttons, `${focus} *`)}{${imp({ color: C.white })}}` +
    (fields.length ? `${at(fields)},${at(fields, "::before")},${at(fields, "::after")}{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": "none" })}}` +
      `${at(fields, ":is(.gpfocuswithin,:focus-within)")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` : "");
}

// The Library's tab bar: icon (24px) and label on one line with even spacing; transparent tabs in steel; one marker
// per tab, a short 3px underline centred under the label, shown only on hover or focus (the family's tone) and on
// the open tab (crimson, which wins). Steam's or another theme's own underlines and markers on the tab are removed.
export function libraryTabCss(parts: { scopes: string[]; tab: string[]; selected?: string[]; title?: string[]; row?: string[]; count?: string; rowFill?: string }): string {
  const { scopes, tab } = parts;
  if (!scopes.length || !tab.length) return "";
  const each = (inner: string[], tail = "") => scopes.flatMap(scope => inner.map(sel => `${scope} ${sel}${tail}`)).join(",");
  const title = parts.title ?? [], selected = parts.selected ?? [], row = parts.row ?? [];
  const tagged = tab.map(sel => `${sel}[${CORE_UI_TAB_ATTRIBUTE}]`);
  const label = title.length ? tab.flatMap(sel => title.map(inner => `${sel} ${inner}`)) : tab;
  const hover = tab.map(sel => `${sel}:is(:hover,.gpfocus,:focus-visible)`);
  const hoverLabel = title.length ? hover.flatMap(sel => title.map(inner => `${sel} ${inner}`)) : hover;
  const activeLabel = title.length ? selected.flatMap(sel => title.map(inner => `${sel} ${inner}`)) : selected;
  const marker = { content: '""', position: "absolute", left: "50%", bottom: "-8px", width: "28px", height: "3px", "margin-left": "-14px", "border-radius": SQUARE,
    "background-image": "none", "pointer-events": "none", opacity: "0" };
  return (row.length ? `${each(row)},${each(row, "::before")},${each(row, "::after")}{${imp({ "background-color": parts.rowFill ?? C.black, "background-image": "none", "backdrop-filter": "none", "box-shadow": "none" })}}` : "") +
    `${each(tab)}{${imp({ position: "relative", "background-color": "transparent", "background-image": "none", color: C.steel, "box-shadow": "none",
      "border-radius": SQUARE, "border-bottom-color": "transparent", "text-decoration": "none" })}}` +
    // Nothing else draws on the tab: no second underline, no per-icon marker.
    `${each(tab, "::before")},${each(tab, "::after")}{${imp({ content: "none", display: "none" })}}` +
    `${each(label)}{${imp({ position: "relative", display: "inline-flex", "align-items": "center", gap: "8px", "line-height": `${CORE_TAB_ICON_PX}px`, "vertical-align": "middle",
      "border-bottom-color": "transparent", "text-decoration": "none", "box-shadow": "none", "background-image": "none" })}}` +
    `${each(label, "::after")}{${imp({ ...marker, "background-color": `var(--core-tone, ${C.steel})` })}}` +
    `${each(hoverLabel, "::after")}{${imp({ opacity: "1" })}}` +
    `${each(hover)}{${imp({ "background-color": C.gunmetal, color: C.white })}}` +
    (activeLabel.length ? `${each(selected)}{${imp({ "background-color": "transparent", "background-image": "none", color: C.steel, "box-shadow": "none" })}}` +
      `${each(activeLabel, "::after")}{${imp({ opacity: "1", "background-color": C.crimson })}}` : "") +
    // The icon (the tagged tab's --core-icon) before the label, on the same line.
    (title.length ? `${each(tagged.flatMap(sel => title.map(inner => `${sel}[${CORE_UI_ICON_ATTRIBUTE}] ${inner}::before`)))}` : `${each(tagged.map(sel => `${sel}[${CORE_UI_ICON_ATTRIBUTE}]::before`))}`) +
    `{${imp({ content: '""', display: "inline-block", flex: `0 0 ${CORE_TAB_ICON_PX}px`, width: `${CORE_TAB_ICON_PX}px`, height: `${CORE_TAB_ICON_PX}px`, margin: "0", position: "static",
      background: "var(--core-icon) center / contain no-repeat" })}}` +
    toneVariables(scopes.flatMap(scope => tagged.map(sel => `${scope} ${sel}`))) +
    (parts.count ? `${each([parts.count])}{${imp({ display: "none" })}}` : "");
}

// Library capsules drawn smaller inside Steam's slots (about 16%), so the gutters are even and black.
export function libraryCapsuleCss(capsules: string[], factor = CORE_LIBRARY_CAPSULE_SCALE): string {
  if (!capsules.length) return "";
  const list = capsules.map(sel => `${ROOT} ${sel}`).join(",");
  return `${list}{${imp({ scale: String(factor), "transform-origin": "50% 50%" })}}`;
}

// --core-tone per family, on any element the runtime tagged with data-core-tone.
function toneVariables(prefix: string | string[]): string {
  const prefixes = Array.isArray(prefix) ? prefix : [prefix];
  if (!prefixes.length) return "";
  return Object.entries(CORE_FAMILY_TONES).map(([family, tone]) => `${prefixes.map(p => `${p}[${CORE_UI_TONE_ATTRIBUTE}="${family}"]`).join(",")}{${imp({ "--core-tone": tone })}}`).join("");
}

// Collection tiles in Steam's Collections view (tagged by coreLibraryIcons.ts by collection name). 3.3.4: one card shape
// for every console tile: gunmetal, the console's icon centred, its name below (data-core-name, set by the decorator),
// the family tone as a short bar along the bottom, a crimson ring on focus. Steam's collage and its own label are
// hidden only on a tile Core draws an icon for; a tile without one keeps Steam's look plus the tone bar.
export function collectionTileCss(tiles: string[] = [""]): string {
  const all = (tail = "") => tiles.map(sel => `${ROOT} ${sel}[${CORE_UI_TILE_ATTRIBUTE}]${CORE_UI_BOOST}${tail}`).join(",");
  const iconed = (tail = "") => tiles.map(sel => `${ROOT} ${sel}[${CORE_UI_TILE_ATTRIBUTE}][${CORE_UI_ICON_ATTRIBUTE}]${CORE_UI_BOOST}${tail}`).join(",");
  const toned = tiles.map(sel => `${ROOT} ${sel}[${CORE_UI_TILE_ATTRIBUTE}]${CORE_UI_BOOST}`);
  return `/* library-collection-tiles */${all()}{${imp({ position: "relative", "border-radius": FLOAT, overflow: "hidden", "box-shadow": "none" })}}` +
    `${iconed()}{${imp({ "background-color": C.gunmetal, "background-image": "none", "min-height": "160px" })}}` +
    `${iconed(" > *")}{${imp({ opacity: "0" })}}` +
    `${iconed("::before")}{${imp({ content: '""', position: "absolute", left: "50%", top: "18%", width: "44%", height: "44%", "max-width": "112px", "max-height": "112px", translate: "-50% 0",
      background: "var(--core-icon) center / contain no-repeat", "pointer-events": "none", "z-index": "2" })}}` +
    `${iconed("[data-core-name]::after")}{${imp({ content: "attr(data-core-name)", position: "absolute", left: "8px", right: "8px", top: "68%", "text-align": "center", color: C.steel,
      "font-size": "15px", "line-height": "20px", "white-space": "nowrap", overflow: "hidden", "text-overflow": "ellipsis", "pointer-events": "none", "z-index": "2" })}}` +
    // The tone: a 3px bar along the bottom edge (a background layer, so ::before and ::after stay free for icon and name).
    `${toned.map(sel => `${sel}[${CORE_UI_TONE_ATTRIBUTE}]`).join(",")}{${imp({ "box-shadow": "inset 0 -3px 0 var(--core-tone)" })}}` +
    `${all(":is(.gpfocus,:focus)")}{${imp({ outline: `3px solid ${C.crimson}`, "outline-offset": "0", filter: "none", animation: "none" })}}` + toneVariables(toned);
}

// Every surface Core UI paints black, and so gets the Starfield. Footers and the opaque top bar are thin:
// they get stars only when large enough (minArea, CSS pixels) to read as a surface. The clear top bar and
// hint bar over game art are not hosts: no stars behind art or video.
// fullHeight: the Steam menu and Quick Access run from the top edge of the screen to the bottom one (coreFullHeight.ts
// moves the panel; the canvas extension stays as the fallback).
// panel: only an element at most 60% of the screen wide counts (a Quick Access candidate that turns out to be a
// full-screen wrapper is never painted black). Earlier entries of one group are tried as the outer host first.
// selector (3.3.7): a host from another plugin, found by a fixed class selector instead of a Steam class map
// (CORE-UI.md, "Hosts from other plugins"). The plugin paints its own page black and keeps any art outside that
// element; Core only marks it (data-core-ui-host="1") and puts the Starfield inside it, over the part on screen.
// Such a host is never full height and never a panel, and is used only when Core UI found Steam's maps.
// 3.3.9: viewport (a Steam page that scrolls inside Steam's page, like a plugin's), exclude (a selector: an element
// holding one is not this host, e.g. a ROM page The Collector draws itself) and below (a selector, map tokens allowed:
// stars start under that child, so a transparent play bar over the art never gets any).
export type CoreUiMapHostSpec = { id: string; map: CoreUiMapName; key: string; minArea: number; fullHeight?: boolean; panel?: boolean; page?: boolean;
  viewport?: boolean; exclude?: string; below?: string };
export type CoreUiSelectorHostSpec = { id: string; selector: string; minArea: number; page?: boolean };
export type CoreUiHostSpec = CoreUiMapHostSpec | CoreUiSelectorHostSpec;
export const CORE_UI_HOSTS: CoreUiHostSpec[] = [
  { id: "menu", map: "mainmenu", key: "Menu", minArea: 0, fullHeight: true, panel: true },
  { id: "qam-container", map: "quickaccess", key: "Container", minArea: 0, fullHeight: true, panel: true },
  { id: "qam-panel", map: "quickaccess", key: "QuickAccessMenu", minArea: 0, fullHeight: true, panel: true },
  { id: "qam", map: "quickaccess", key: "Menu", minArea: 0, fullHeight: true, panel: true },
  { id: "context", map: "contextmenu", key: "contextMenuContents", minArea: 0 },
  { id: "menu-footer", map: "mainmenu", key: "FooterBoxShadow", minArea: 24000 },
  { id: "qam-footer", map: "footer", key: "QuickAccessFooter", minArea: 24000 },
  { id: "qam-footer-shadow", map: "quickaccess", key: "FooterBoxShadow", minArea: 24000 },
  { id: "footer-opaque", map: "footer", key: "Opaque", minArea: 24000 },
  // 3.3.9: the top bar is never black any more (chrome-header), so it is never a Starfield host.
  // Phase 2 pages: the canvas covers the visible part of the page only.
  { id: "home", map: "gamepadui", key: "BasicHome", minArea: 0, page: true },
  { id: "library", map: "library", key: "GamepadLibrary", minArea: 0, page: true },
  // 3.3.9: Steam's game page, screen two (CORE-UI.md, "The game view"): the app details container, below the play bar,
  // never a ROM page (The Collector's page is its own host).
  { id: "game-page", map: "appdetails", key: "AppDetailsContainer", minArea: 0, page: true, viewport: true, exclude: ".romm-core-page", below: "{appdetails.PlaySection}" },
  // Hosts from other plugins (3.3.7). The Collector's ROM game page (Eternal-Collector, corePageStyles.ts): the
  // wrapper in place of Steam's overview panel, below the hero art.
  { id: "collector-rom-page", selector: ".romm-core-page", minArea: 0, page: true },
];

// A plugin host's selector: one or more plain class names, nothing else (no combinators, attributes or ids).
const PLUGIN_HOST_SELECTOR = /^(\.[A-Za-z_][\w-]*)+$/;

// Where a host is found: plain (querySelector) and strong (stylesheet: the class repeated plus the boost). Undefined
// when its class map or key is missing on this client, or a plugin host's selector is not a plain class list.
export function resolveHostSelector(maps: CoreUiMaps, host: CoreUiHostSpec): { plain: string; strong: string } | undefined {
  if ("selector" in host) {
    const plain = String(host.selector ?? "").trim();
    if (!PLUGIN_HOST_SELECTOR.test(plain)) return undefined;
    const first = plain.slice(0, plain.indexOf(".", 1) > 0 ? plain.indexOf(".", 1) : undefined);
    return { plain, strong: plain + first + CORE_UI_BOOST };
  }
  const plain = plainSelector(maps[host.map], host.key), strong = classSelector(maps[host.map], host.key);
  return plain && strong ? { plain, strong } : undefined;
}

// source: "steam" (a class map) or "plugin" (a fixed selector from a cooperating plugin). viewport: the canvas covers
// only the part of the element on screen (a plugin page scrolls inside Steam's page, not inside itself).
export type CoreUiHost = { id: string; selector: string; strong: string; minArea: number; fullHeight: boolean; panel: boolean; page: boolean; source: "steam" | "plugin"; viewport: boolean;
  exclude?: string; below?: string };
export type CoreUiSheet = {
  css: string; applied: string[]; skipped: string[]; hosts: CoreUiHost[]; clock?: string;
  // 3.3.4, for the snapshot: the maps found, how each was found, and per rule the plain selectors it targets.
  maps?: CoreUiMaps; routes?: Record<string, CoreUiMapRoute>; ruleTargets?: Record<string, string>;
  // 3.3.8: per missing map, the closest class map's key names (closestCoreUiCandidates).
  candidates?: Partial<Record<CoreUiMapName, CoreUiMapCandidate>>;
  // Selectors the runtime needs (plain, no specificity repeat).
  dom: { header?: string; footer?: string; menu?: string; menuItem?: string; qamTab?: string; tab?: string; tabTitle?: string; tabSelected?: string; library?: string; tabbedPage?: string;
    // 3.3.9: the game view's two screens (coreUi.ts, gameViewScroll).
    playSection?: string; appContainer?: string; tabRow?: string };
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
  const target = (id: string) => targetSelectors(maps, id);
  for (const rule of rules(mark)) {
    const anyOk = !rule.needsAny?.length || rule.needsAny.some(([map, key]) => classSelector(maps[map], key));
    if (!anyOk || !rule.needs.every(([map, key]) => classSelector(maps[map], key)) || !(rule.targets ?? []).every(id => target(id).length > 0)) { skipped.push(rule.id); continue; }
    const select = (map: CoreUiMapName, key: string) => classSelector(maps[map], key) ?? "";
    let css: string | undefined;
    try { css = rule.css(select, target); } catch { css = undefined; }
    if (!css) { skipped.push(rule.id); continue; }
    parts.push(`/* ${rule.id} */${css}`);
    applied.push(rule.id);
  }
  // Nothing of Steam found (no class map at all): no stylesheet, not even the always-on parts.
  if (applied.length === 1 && applied[0] === "corners") { parts.length = 0; applied.length = 0; }
  const hosts: CoreUiHost[] = [];
  for (const host of CORE_UI_HOSTS) {
    const plugin = "selector" in host;
    // A plugin's page is a host only next to Steam's own surfaces: without any class map there is no Core UI.
    if (plugin && !applied.length) continue;
    const found = resolveHostSelector(maps, host);
    if (!found) continue;
    hosts.push({ id: host.id, selector: found.plain, strong: found.strong, minArea: host.minArea,
      fullHeight: !plugin && (host as CoreUiMapHostSpec).fullHeight === true, panel: !plugin && (host as CoreUiMapHostSpec).panel === true,
      page: host.page === true, source: plugin ? "plugin" : "steam", viewport: plugin || (host as CoreUiMapHostSpec).viewport === true,
      ...(!plugin && (host as CoreUiMapHostSpec).exclude ? { exclude: (host as CoreUiMapHostSpec).exclude } : {}),
      ...(!plugin && (host as CoreUiMapHostSpec).below ? { below: resolveTemplate(maps, (host as CoreUiMapHostSpec).below!, true) } : {}) });
  }
  // Black means starfield: every black surface above is a host. The runtime puts one canvas in each (the
  // shared field), behind the surface's content; a host inside another host stays clear instead.
  if (hosts.length) {
    const all = hosts.map(host => `${ROOT} ${host.selector}`);
    const strong = hosts.map(host => `${ROOT} ${host.strong}`);
    parts.push(`/* starfield-hosts */${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}]`).join(",")}{${imp({ isolation: "isolate" })}}` +
      `${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}] > canvas[${CORE_UI_STARS_ATTRIBUTE}]`).join(",")}{${imp({ position: "absolute", left: "0", right: "auto", bottom: "auto", width: "100%",
        // The canvas follows the surface's scroll position and, for full-height surfaces, reaches the screen's edges.
        top: "calc(var(--core-ui-scroll, 0px) - var(--core-ui-extend-top, 0px))",
        height: "calc(100% + var(--core-ui-extend-top, 0px) + var(--core-ui-extend-bottom, 0px))",
        "z-index": "-1", "pointer-events": "none", margin: "0", padding: "0", border: "0", "border-radius": "inherit", "background-color": C.black })}}` +
      // A page host's canvas covers only what is on screen (--core-ui-page-height, 0 until measured), never more than
      // its host (max-height 100% of the host, its containing block): 3.3.8, no viewport-tall fallback.
      `${hosts.filter(host => host.page).map(host => `${ROOT} ${host.selector}[${CORE_UI_HOST_ATTRIBUTE}] > canvas[${CORE_UI_STARS_ATTRIBUTE}]`).join(",") || `${ROOT} [data-core-ui-none]`}{${imp({ height: "var(--core-ui-page-height, 0px)", "max-height": "100%" })}}` +
      // 3.3.8: a canvas whose surface is not on screen (or that belongs to no live surface) is never drawn.
      `${ROOT} canvas[${CORE_UI_STARS_ATTRIBUTE}]:not([${CORE_UI_LIVE_ATTRIBUTE}]){${imp({ display: "none" })}}` +
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
    playSection: plainSelector(maps.appdetails, "PlaySection"), appContainer: plainSelector(maps.appdetails, "AppDetailsContainer"), tabRow: plainSelector(maps.tabbedpage, "TabRow"),
  };
  const css = parts.join("\n");
  return { css, applied, skipped, hosts, clock, dom, maps, ruleTargets: ruleTargetsFromCss(css) };
}

const STATE = /^(\.gpfocus|\.gpfocuswithin|:hover|:focus|:focus-visible|:focus-within)$/;

// Splits a selector list on its top-level commas (not those inside :is(), :not() or :has()).
export function splitTopLevel(list: string): string[] {
  const out: string[] = [];
  let depth = 0, current = "";
  for (const ch of list) {
    if (ch === "(") depth++;
    else if (ch === ")") depth = Math.max(0, depth - 1);
    if (ch === "," && depth === 0) { out.push(current); current = ""; } else current += ch;
  }
  if (current.trim()) out.push(current);
  return out;
}

// Per rule (the `/* id */` that starts each part of the stylesheet), one selector list Element.matches can test:
// pseudo-elements and the state pseudo-classes dropped, @media / @keyframes skipped. For the snapshot only.
export function ruleTargetsFromCss(css: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const part of String(css).split("\n")) {
    const head = /^\/\* ([\w-]+) \*\//.exec(part);
    if (!head) continue;
    const found = new Set<string>();
    let depth = 0, selector = "";
    const body = part.slice(head[0].length);
    for (let i = 0; i < body.length; i++) {
      const ch = body[i];
      if (ch === "{") {
        if (depth === 0 && !selector.trim().startsWith("@")) {
          for (const one of splitTopLevel(selector)) {
            const clean = one.replace(/::?(before|after|placeholder|-webkit-[\w-]+)/g, "")
              .replace(/:is\(([^()]*)\)/g, (all, inner: string) => inner.split(",").every(part => STATE.test(part.trim())) ? "" : all)
              .replace(/:(hover|focus|focus-visible|focus-within)(?![\w-])/g, "").trim();
            if (clean && !clean.startsWith("@")) found.add(clean);
          }
        }
        depth++; selector = "";
      } else if (ch === "}") { depth = Math.max(0, depth - 1); selector = ""; }
      else if (depth === 0) selector += ch;
    }
    if (found.size) out[head[1]] = [...found].join(",");
  }
  return out;
}

// The same class without the specificity repeat, for querySelector.
export function plainSelector(map: Record<string, string> | undefined, key: string): string | undefined {
  if (!has(map, key)) return undefined;
  const names = String(map![key]).trim().split(/\s+/);
  return names.every(name => CLASS_NAME.test(name)) ? "." + names.join(".") : undefined;
}
