import { CatalogPlugin, InstalledPlugin, findInstalledPlugin, normalizePluginIdentity } from "./pluginStoreCatalog";
import protectedForksData from "../../Shared/ProtectedPluginForks.json";

// Eternal Core: the user's own plugin forks (Source/Shared/ProtectedPluginForks.json, shared with the
// desktop app). Decky reports only an installed plugin's name and version, so a fork is recognised by
// its plugin name plus, where the rule has them, a version marker such as "-win." or "-anygame".
export interface ProtectedFork {
  id: string;
  displayName: string;
  repository: string;
  repositoryAliases: string[];
  supersedes: string[];
  folders: string[];
  pluginNames: string[];
  versionMarkers: string[];
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string" && item.trim().length > 0) : [];
}

export function parseProtectedForks(document: unknown): ProtectedFork[] {
  const root = document as { schemaVersion?: unknown; forks?: unknown } | null;
  if (!root || root.schemaVersion !== 1 || !Array.isArray(root.forks)) throw new Error("Unsupported protected fork list.");
  return root.forks.map((item: any) => ({
    id: String(item?.id ?? ""),
    displayName: String(item?.displayName ?? ""),
    repository: String(item?.repository ?? ""),
    repositoryAliases: strings(item?.repositoryAliases),
    supersedes: strings(item?.supersedes),
    folders: strings(item?.folders),
    pluginNames: strings(item?.pluginNames),
    versionMarkers: strings(item?.versionMarkers),
  })).filter((fork) => fork.id && fork.repository);
}

export const PROTECTED_FORKS: readonly ProtectedFork[] = parseProtectedForks(protectedForksData);

export function protectedForkFor(
  plugin: InstalledPlugin,
  forks: readonly ProtectedFork[] = PROTECTED_FORKS,
): ProtectedFork | undefined {
  const name = normalizePluginIdentity(plugin.name);
  const version = plugin.version.toLocaleLowerCase("en-US");
  return forks.find((fork) =>
    fork.pluginNames.some((candidate) => normalizePluginIdentity(candidate) === name) &&
    (!fork.versionMarkers.length || fork.versionMarkers.some((marker) => version.includes(marker.toLocaleLowerCase("en-US")))));
}

/** True when installing or updating this catalogue entry could replace one of the user's forks. */
export function isShieldedByInstalledFork(
  plugin: CatalogPlugin,
  installed: InstalledPlugin[],
  forks: readonly ProtectedFork[] = PROTECTED_FORKS,
): boolean {
  const repository = String(plugin.repository ?? "").toLocaleLowerCase("en-US");
  const current = findInstalledPlugin(plugin, installed);
  if (current && protectedForkFor(current, forks)) return true;
  return installed.some((entry) => {
    const fork = protectedForkFor(entry, forks);
    return Boolean(fork && [fork.repository, ...fork.repositoryAliases, ...fork.supersedes]
      .some((value) => value.toLocaleLowerCase("en-US") === repository));
  });
}

/** Catalogue without the entries that an installed fork replaces; the fork itself stays as installed-only. */
export function withoutShieldedPlugins(
  plugins: CatalogPlugin[],
  installed: InstalledPlugin[],
  forks: readonly ProtectedFork[] = PROTECTED_FORKS,
): CatalogPlugin[] {
  return plugins.filter((plugin) => !isShieldedByInstalledFork(plugin, installed, forks));
}
