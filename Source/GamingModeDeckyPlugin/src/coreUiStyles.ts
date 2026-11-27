// Eternal Core: Core UI, phase 1 (docs/eternal/CORE-UI.md). Builds the one stylesheet Core's plugin injects
// for the global chrome (top bar, button-hint bar), the Steam menu and Quick Access.
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
// Pure on purpose (one import, steamModules.ts, itself pure): tests/coreUi.test.mjs runs it against fake
// Decky 3 and Decky 4 libraries.

import { findClassModule } from "./steamModules";

export const CORE_UI_ATTRIBUTE = "data-core-ui";
export const CORE_UI_STYLE_ID = "core-ui-style";
export const CORE_UI_STARS_ATTRIBUTE = "data-core-ui-stars";
const ROOT = `html[${CORE_UI_ATTRIBUTE}]`;

// Colours: corePalette.ts (CORE), repeated here so this file stays loadable on its own.
const C = { black: "#000000", gunmetal: "#1A1D21", raised: "#2A2F36", steel: "#C9D1D9", hint: "#9AA4AE", text: "#E6EAEE", white: "#FFFFFF", crimson: "#D7263D", crimsonText: "#EA5A68" } as const;

export type CoreUiMapName = "mainmenu" | "quickaccess" | "controls" | "footer" | "header" | "dropdown" | "contextmenu";
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

// Phase 1 rules. Spacing from the approved boards (Context board: 48px rows, 22px inset, 16px label,
// gunmetal fill with a 3px crimson marker; the Steam menu takes the left-bar form of that marker).
function rules(wordmark: string | undefined): Rule[] {
  const list: Rule[] = [
    // Global chrome: the top bar and the button-hint bar stay clear, so black or the game art shows through.
    { id: "chrome-header", needs: [["header", "Header"]], css: s =>
      `${ROOT} ${s("header", "Header")},${ROOT} ${s("header", "Header")}::before,${ROOT} ${s("header", "Header")}::after{${imp({
        "background-color": "transparent", "background-image": "none", "box-shadow": "none" })}}` },
    { id: "chrome-header-opaque", needs: [["header", "HeaderOpaque"]], css: s =>
      `${ROOT} ${s("header", "HeaderOpaque")}{${imp({ "background-color": C.black, "background-image": "none" })}}` },
    { id: "chrome-footer", needs: [["footer", "BasicFooter"]], css: s =>
      `${ROOT} ${s("footer", "BasicFooter")},${ROOT} ${s("footer", "BasicFooter")}::before,${ROOT} ${s("footer", "BasicFooter")}::after{${imp({
        "background-color": "transparent", "background-image": "none", "box-shadow": "none", "border-top-color": "transparent" })}}` },
    { id: "chrome-footer-opaque", needs: [["footer", "Opaque"]], css: s =>
      `${ROOT} ${s("footer", "Opaque")}{${imp({ "background-color": C.black, "background-image": "none" })}}` },

    // Steam menu: AMOLED black panel (stars drawn behind the rows by coreUi.ts), the Core wordmark above Home.
    { id: "menu-panel", needs: [["mainmenu", "Menu"]], css: s =>
      `${ROOT} ${s("mainmenu", "Menu")}{${imp({ position: "relative", "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` +
      (wordmark ? `${ROOT} ${s("mainmenu", "Menu")}::before{${imp({ content: '""', position: "absolute", top: "57px", left: "24px", width: "180px", height: "48px",
        background: `url(${JSON.stringify(wordmark)}) left center / contain no-repeat`, "pointer-events": "none", "z-index": "1" })}}` : "") },
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
        `${menu} ${s("mainmenu", "ItemLabel")}{${imp({ margin: "0", padding: "0", "text-align": "left", transform: "none", color: C.steel, "font-size": "16px" })}}`;
    } },
    // Focused row: gunmetal with a 3px crimson bar on its left edge. No glow, no underline.
    { id: "menu-focus", needs: [["mainmenu", "Menu"], ["mainmenu", "Item"]], css: s => {
      const menu = `${ROOT} ${s("mainmenu", "Menu")}`;
      const item = s("mainmenu", "Item");
      return `${menu} ${item}:is(.gpfocus,:focus,:focus-visible,:focus-within){${imp({ "background-color": C.gunmetal, "background-image": "none",
          "box-shadow": `inset 3px 0 0 ${C.crimson}`, outline: "none", animation: "none", filter: "none" })}}` +
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

    // Quick Access: one black panel with the Starfield behind the content.
    { id: "qam-panel", needs: [["quickaccess", "Menu"]], css: s => {
      const containers = ["Container", "QuickAccessMenu"].filter(key => s("quickaccess", key) !== "")
        .map(key => `${ROOT} ${s("quickaccess", key)}`).join(",");
      return `${ROOT} ${s("quickaccess", "Menu")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": "none" })}}` +
        (containers ? `${containers}{${imp({ "background-color": C.black, "background-image": "none" })}}` : "");
    } },
    // Everything over the stars is clear.
    { id: "qam-clear", needs: [["quickaccess", "TabContentColumn"]], css: s => {
      const keys = ["TabContentColumn", "AllTabContents", "ContentTransition", "TabGroupPanel", "PanelSection", "PanelOuterNav"];
      const list = keys.filter(key => s("quickaccess", key) !== "").map(key => `${ROOT} ${s("quickaccess", key)}`);
      if (s("controls", "PanelSection") !== "") list.push(`${ROOT} ${s("controls", "PanelSection")}`);
      return `${list.join(",")}{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": "none" })}}`;
    } },
    // The title strip: flat gunmetal, no gradient bars above or under it.
    { id: "qam-title", needs: [["quickaccess", "Title"]], css: s => {
      const keys = ["Title", "HeaderContainer"].filter(key => s("quickaccess", key) !== "").map(key => `${ROOT} ${s("quickaccess", key)}`);
      const pseudo = keys.flatMap(key => [`${key}::before`, `${key}::after`]);
      return `${keys.join(",")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", "border-color": "transparent" })}}` +
        `${pseudo.join(",")}{${imp({ "background-image": "none", "background-color": "transparent", "box-shadow": "none" })}}`;
    } },
    // The tab rail: clear over the panel's black and stars, a thin gunmetal divider; the open tab gunmetal (its
    // crimson bar is Core's own).
    { id: "qam-tabs", needs: [["quickaccess", "Tabs"], ["quickaccess", "Tab"]], css: s =>
      `${ROOT} ${s("quickaccess", "Tabs")}{${imp({ "background-color": "transparent", "background-image": "none", "box-shadow": `inset -1px 0 0 ${C.gunmetal}` })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}{${imp({ "background-color": "transparent", "background-image": "none" })}}` +
      `${ROOT} ${s("quickaccess", "Tab")}:is(.gpfocus,:focus-visible){${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none", filter: "none" })}}` },
    { id: "qam-tab-selected", needs: [["quickaccess", "Tab"], ["quickaccess", "Selected"]], css: s =>
      `${ROOT} ${s("quickaccess", "Tab")}${s("quickaccess", "Selected")}{${imp({ "background-color": C.gunmetal, "background-image": "none", "box-shadow": "none" })}}` },
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
        `${focused.join(",")}{${imp({ "background-color": C.raised, "background-image": "none", color: C.white, "box-shadow": `inset 0 0 0 2px ${C.crimson}`, filter: "none", animation: "none" })}}`;
    } },

    // Context menus and their submenus (a game's Options / "..." menu, Manage, and the menus the Steam menu and
    // Quick Access open). The Context board: black panel with a 1px gunmetal edge and 10px corners, 8px top and
    // bottom, 48px rows with a 22px inset and 16px text, a 13px hint-grey header, hairline gunmetal separators.
    { id: "context-panel", needs: [["contextmenu", "contextMenuContents"]], css: s =>
      `${ROOT} ${s("contextmenu", "contextMenuContents")}{${imp({ "background-color": C.black, "background-image": "none", "box-shadow": `inset 0 0 0 1px ${C.gunmetal}`,
        "border-radius": "10px", "padding-top": "8px", "padding-bottom": "8px", filter: "none", "backdrop-filter": "none" })}}` +
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
        "box-shadow": `inset 3px 0 0 ${C.crimson}`, color: C.white, filter: "none", outline: "none" })}}`;
    } },
    // Danger items (Steam marks them Destructive: Uninstall, Remove...): crimson text, focused or not.
    { id: "context-danger", needs: [["contextmenu", "contextMenuItem"], ["contextmenu", "Destructive"]], css: s =>
      `${ROOT} ${s("contextmenu", "contextMenuItem")}${s("contextmenu", "Destructive")}{${imp({ color: C.crimsonText })}}` },
  ];
  return list;
}

export const CORE_UI_HOST_ATTRIBUTE = "data-core-ui-host";

// Every surface Core UI paints black, and so gets the Starfield. Footers and the opaque top bar are thin:
// they get stars only when large enough (minArea, CSS pixels) to read as a surface. The clear top bar and
// hint bar over game art are not hosts: no stars behind art or video.
// fullHeight: the Steam menu and Quick Access run from the top edge of the screen to the bottom one; their
// stars and black reach past the panel over the top bar and button-hint rows (coreUi.ts measures the gaps).
export const CORE_UI_HOSTS: Array<{ id: string; map: CoreUiMapName; key: string; minArea: number; fullHeight?: boolean }> = [
  { id: "menu", map: "mainmenu", key: "Menu", minArea: 0, fullHeight: true },
  { id: "qam", map: "quickaccess", key: "Menu", minArea: 0, fullHeight: true },
  { id: "context", map: "contextmenu", key: "contextMenuContents", minArea: 0 },
  { id: "menu-footer", map: "mainmenu", key: "FooterBoxShadow", minArea: 24000 },
  { id: "qam-footer", map: "footer", key: "QuickAccessFooter", minArea: 24000 },
  { id: "qam-footer-shadow", map: "quickaccess", key: "FooterBoxShadow", minArea: 24000 },
  { id: "footer-opaque", map: "footer", key: "Opaque", minArea: 24000 },
  { id: "header-opaque", map: "header", key: "HeaderOpaque", minArea: 24000 },
];

export type CoreUiHost = { id: string; selector: string; minArea: number; fullHeight: boolean };
export type CoreUiSheet = { css: string; applied: string[]; skipped: string[]; hosts: CoreUiHost[]; clock?: string };

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
// class is missing on this client.
export function buildCoreUiCss(maps: CoreUiMaps, options: { wordmark?: string } = {}): CoreUiSheet {
  const applied: string[] = [], skipped: string[] = [], parts: string[] = [];
  const wordmark = typeof options.wordmark === "string" && /^(https?:|data:image\/)/.test(options.wordmark) ? options.wordmark : undefined;
  for (const rule of rules(wordmark)) {
    if (!rule.needs.every(([map, key]) => classSelector(maps[map], key))) { skipped.push(rule.id); continue; }
    const select = (map: CoreUiMapName, key: string) => classSelector(maps[map], key) ?? "";
    let css: string | undefined;
    try { css = rule.css(select); } catch { css = undefined; }
    if (!css) { skipped.push(rule.id); continue; }
    parts.push(`/* ${rule.id} */${css}`);
    applied.push(rule.id);
  }
  const hosts: CoreUiHost[] = [];
  for (const host of CORE_UI_HOSTS) {
    const selector = plainSelector(maps[host.map], host.key);
    if (selector) hosts.push({ id: host.id, selector, minArea: host.minArea, fullHeight: host.fullHeight === true });
  }
  // Black means starfield: every black surface above is a host. The runtime puts one canvas in each (the
  // shared field), behind the surface's content; a host inside another host stays clear instead.
  if (hosts.length) {
    const all = hosts.map(host => `${ROOT} ${host.selector}`);
    parts.push(`/* starfield-hosts */${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}]`).join(",")}{${imp({ isolation: "isolate" })}}` +
      `${all.map(sel => `${sel}[${CORE_UI_HOST_ATTRIBUTE}] > canvas[${CORE_UI_STARS_ATTRIBUTE}]`).join(",")}{${imp({ position: "absolute", left: "0", right: "auto", bottom: "auto", width: "100%",
        // The canvas follows the surface's scroll position and, for full-height surfaces, reaches the screen's edges.
        top: "calc(var(--core-ui-scroll, 0px) - var(--core-ui-extend-top, 0px))",
        height: "calc(100% + var(--core-ui-extend-top, 0px) + var(--core-ui-extend-bottom, 0px))",
        "z-index": "-1", "pointer-events": "none", margin: "0", padding: "0", border: "0", "border-radius": "inherit", "background-color": C.black })}}` +
      `${ROOT} [${CORE_UI_HOST_ATTRIBUTE}="nested"]{${imp({ "background-color": "transparent", "background-image": "none" })}}`);
  }
  const clock = plainSelector(maps.header, "Clock");
  if (clock) parts.push(orbCss());
  return { css: parts.join("\n"), applied, skipped, hosts, clock };
}

// The same class without the specificity repeat, for querySelector.
export function plainSelector(map: Record<string, string> | undefined, key: string): string | undefined {
  if (!has(map, key)) return undefined;
  const names = String(map![key]).trim().split(/\s+/);
  return names.every(name => CLASS_NAME.test(name)) ? "." + names.join(".") : undefined;
}
