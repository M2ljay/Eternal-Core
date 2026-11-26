/**
 * Rebrand phase 2: Decky lists this plugin as "Core" (plugin.json and definePlugin name).
 * Before Core 3.4 it was "Playhub". Code that matches Decky's plugin name accepts both, and
 * Decky's own plugin order and hidden list, which Decky keys by plugin name, are renamed once.
 *
 * Not renamed: the QAM bridge registration ("Playhub"), the saved QAM layout entry
 * "Playhub" and the tab key "shortcut:Playhub". Those are this plugin's own stored keys,
 * never shown, and keeping them needs no migration. The plugin folder stays "gaming-mode", so
 * Decky's per-plugin settings, data and logs are unaffected.
 *
 * Self-contained on purpose: the tests load single source files.
 */
export const PLUGIN_NAME = "Core";
export const LEGACY_PLUGIN_NAME = "Playhub";
export const OWN_PLUGIN_NAMES: readonly string[] = [PLUGIN_NAME, LEGACY_PLUGIN_NAME];

export const isOwnPluginName = (name: unknown): boolean =>
  typeof name === "string" && OWN_PLUGIN_NAMES.includes(name);

/**
 * The list with "Playhub" replaced by "Core" in the same position, or null when nothing
 * needs to change. If Decky already appended "Core" (the renamed plugin loaded once before
 * this ran), that later entry is dropped so Core keeps the place the user gave Playhub.
 */
export function renameOwnPlugin(list: unknown): string[] | null {
  if (!Array.isArray(list) || list.length > 1024 || !list.every(item => typeof item === "string")) return null;
  if (!list.includes(LEGACY_PLUGIN_NAME)) return null;
  const next: string[] = [];
  for (const name of list) {
    if (name === PLUGIN_NAME) continue;
    const renamed = name === LEGACY_PLUGIN_NAME ? PLUGIN_NAME : name;
    if (!next.includes(renamed)) next.push(renamed);
  }
  return next;
}

// Decky 3.3.1 setting names (frontend plugin-loader) and the matching in-memory setters.
const DECKY_LISTS = [["pluginOrder", "setPluginOrder"], ["hiddenPlugins", "setHiddenPlugins"]] as const;

/** Renames this plugin in Decky's order and hidden lists. Best effort; returns the lists changed. */
export async function migrateDeckyPluginName(host: any = typeof window === "undefined" ? globalThis : window): Promise<string[]> {
  const backend = host?.DeckyBackend;
  if (typeof backend?.call !== "function") return [];
  const changed: string[] = [];
  for (const [setting, setter] of DECKY_LISTS) {
    try {
      const next = renameOwnPlugin(await backend.call("utilities/settings/get", setting, []));
      if (!next) continue;
      await backend.call("utilities/settings/set", setting, next);
      changed.push(setting);
      const state = host.DeckyPluginLoader?.deckyState;
      if (typeof state?.[setter] === "function") state[setter](next);
    } catch { /* Decky keeps its own order; nothing in Core depends on it. */ }
  }
  return changed;
}
