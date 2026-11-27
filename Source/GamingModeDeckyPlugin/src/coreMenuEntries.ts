// Eternal Core: which entries Core adds to the Steam menu (Core UI phase 1, docs/eternal/CORE-UI.md).
//
// Awesome Mix (the Eternal-Mix Decky plugin, folder and name "Awesome Mix") gets a Steam menu entry that opens
// its full-screen YouTube Music page, only while Decky has the plugin loaded. Digital stores no longer has a
// Steam menu entry: it opens from Quick Access > Core (CoreUiSection.tsx).
//
// Pure on purpose (no imports): tests/coreUi.test.mjs checks the swap with and without the plugin.

export type CoreMenuEntry = { id: string; label: string; route: string; order: number };

export const AWESOME_MIX_PLUGIN = "Awesome Mix";
// Registered by Awesome Mix itself (Eternal-Mix src/index.tsx, YOUTUBE_MUSIC_BIG_PICTURE_ROUTE).
export const AWESOME_MIX_ROUTE = "/awesome-mix/youtube-music-big-picture";
// The product name; the same in every language.
export const AWESOME_MIX_ENTRY: CoreMenuEntry = { id: "awesome-mix", label: AWESOME_MIX_PLUGIN, route: AWESOME_MIX_ROUTE, order: 15 };
// The entry this replaces (StoreDigitalPortal registered it until Core UI phase 1).
export const RETIRED_MENU_ENTRIES = ["store-digital"];

type PluginLike = string | { name?: unknown } | null | undefined;
type DeckyPublicState = { plugins?: PluginLike[]; installedPlugins?: PluginLike[]; disabledPlugins?: PluginLike[] } | null | undefined;

const nameOf = (plugin: PluginLike) => String(typeof plugin === "string" ? plugin : plugin?.name ?? "").trim().toLowerCase();

// Loaded plugins count (their routes exist). Only a loader that does not list loaded plugins falls back to
// installed plugins that are not disabled.
export function awesomeMixInstalled(state: DeckyPublicState): boolean {
  try {
    const wanted = AWESOME_MIX_PLUGIN.toLowerCase();
    if (Array.isArray(state?.plugins)) return state!.plugins!.some(plugin => nameOf(plugin) === wanted);
    const disabled = new Set((Array.isArray(state?.disabledPlugins) ? state!.disabledPlugins! : []).map(nameOf));
    const installed = Array.isArray(state?.installedPlugins) ? state!.installedPlugins! : [];
    return installed.some(plugin => nameOf(plugin) === wanted && !disabled.has(wanted));
  } catch { return false; }
}

export function coreSteamMenuEntries(state: DeckyPublicState): CoreMenuEntry[] {
  return awesomeMixInstalled(state) ? [AWESOME_MIX_ENTRY] : [];
}
