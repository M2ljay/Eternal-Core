// Eternal Core: Core UI and CSS Loader's legacy Core themes (docs/eternal/CORE-UI.md, "CSS Loader", 3.3.4).
//
// The TV showed the old "Core" CSS Loader theme still active on top of Core UI (its wordmark, grey-blue focus and grey
// "MENU" footer), so none of Core UI's Steam menu changes were visible. While Core UI is on, Core switches the legacy
// themes off once per start (and whenever Core UI is switched on), and switching Core UI off switches back on exactly
// those Core switched off. The user's other themes (CapyMenu, Enhanced Library, ...) are never touched; they are listed
// in the snapshot so conflicts can be seen.
//
// Two halves:
// - main.py (`core_ui_legacy_themes`) sets "active": false in each legacy theme's config_USER.json and records what it
//   changed in %APPDATA%\GamingMode\core-ui-css-themes.json, so "restore" puts back only those.
// - Here: CSS Loader is asked to drop the theme at once through its own backend method (set_theme_state, through
//   Decky's plugin-call route, new API first, then the legacy one); if CSS Loader does not answer, it is reloaded so
//   it reads the configs again.
//
// Pure apart from what it is handed: tests drive it with fakes.

export const LEGACY_CORE_THEMES = ["Core", "Core Accents"] as const;

export type LegacyThemeAnswer = { ok?: boolean; changed?: string[]; restored?: string[]; themes?: Array<{ name: string; folder?: string; active?: boolean }>; dir?: string; error?: string };
export type CssLoaderBridge = {
  // CSS Loader's plugin name in Decky (normally "CSS Loader"), or undefined when it is not loaded.
  name(): string | undefined;
  call(route: string, ...args: unknown[]): Promise<any>;
};
export type LegacyThemeReport = { at?: number; action?: "disable" | "restore"; changed: string[]; restored: string[]; route?: string; error?: string; themes?: LegacyThemeAnswer["themes"] };

// CSS Loader's plugin name from Decky's state: the loaded plugin whose name reads "CSS Loader" (any case, any spacing).
export function cssLoaderName(state: any): string | undefined {
  try {
    for (const plugin of state?.plugins ?? []) { const name = typeof plugin === "string" ? plugin : plugin?.name; if (typeof name === "string" && /^css[\s_-]*loader$/i.test(name.trim())) return name; }
  } catch { /* Not found. */ }
  return undefined;
}

// Tells CSS Loader to set each theme's state at once. Answers the route that worked: "api", "legacy", "reload", or
// undefined when CSS Loader is not there.
export async function applyInCssLoader(bridge: CssLoaderBridge, names: string[], state: boolean): Promise<string | undefined> {
  const plugin = bridge.name();
  if (!plugin || !names.length) return undefined;
  let api = 0, legacy = 0;
  for (const theme of names) {
    try { await bridge.call("loader/call_plugin_method", plugin, "set_theme_state", theme, state, false, false); api++; continue; } catch { /* Try the legacy route. */ }
    try {
      const answer = await bridge.call("loader/call_legacy_plugin_method", plugin, "set_theme_state", { name: theme, state, set_deps: false, set_deps_value: false });
      if (answer?.success !== false) { legacy++; continue; }
    } catch { /* Fall through to a reload. */ }
  }
  if (api === names.length) return "api";
  if (api + legacy === names.length) return legacy ? "legacy" : "api";
  try { await bridge.call("loader/reload_plugin", plugin); return "reload"; } catch { return "none"; }
}

export function createLegacyThemeGuard(deps: {
  backend: (action: "status" | "disable" | "restore") => Promise<LegacyThemeAnswer>;
  bridge: CssLoaderBridge;
  now?: () => number;
  log?: (message: string) => void;
}) {
  let report: LegacyThemeReport = { changed: [], restored: [] };
  let busy: Promise<LegacyThemeReport> | undefined;
  const run = (action: "disable" | "restore") => {
    const next = (busy ?? Promise.resolve(report)).catch(() => report).then(async () => {
      try {
        const answer = await deps.backend(action);
        const names = action === "disable" ? answer?.changed ?? [] : answer?.restored ?? [];
        const route = names.length ? await applyInCssLoader(deps.bridge, names, action === "restore") : undefined;
        report = { at: deps.now?.(), action, changed: answer?.changed ?? [], restored: answer?.restored ?? [], route, error: answer?.ok === false ? answer.error ?? "failed" : undefined, themes: answer?.themes };
        if (names.length) deps.log?.(`[Core UI] CSS Loader legacy themes ${action === "disable" ? "switched off" : "switched back on"}: ${names.join(", ")} (${route ?? "no CSS Loader"})`);
      } catch (error) {
        report = { at: deps.now?.(), action, changed: [], restored: [], error: String((error as any)?.message ?? error).slice(0, 120) };
      }
      return report;
    });
    busy = next;
    return next;
  };
  return { disable: () => run("disable"), restore: () => run("restore"), report: () => report };
}
