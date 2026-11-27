// Eternal Core: Core's logos for the user's Eternal plugin forks (docs/eternal/CORE-UI.md, "Plugin logos"; credited in
// docs/eternal/REBRAND.md). Matched by the Decky plugin's name, so a renamed or absent plugin keeps its own icon.
//
//   Discord                       -> assemble       (Eternal-Assemble)
//   Awesome Mix, Now Playing Plus -> awesome-mix    (Eternal-Mix)
//   Nexus Mods, Forge             -> forge          (Eternal-Forge)
//   romm-dock, RomM-Dock, The Collector -> the-collector (Eternal-Collector)
//
// Pure (one import, coreArtwork.ts, itself pure): tests check the matching and the fallbacks.

import { CORE_PLUGIN_GLYPHS, CORE_PLUGIN_TILES, svgDataUrl } from "./coreArtwork";

export type CorePluginLogo = "assemble" | "awesome-mix" | "forge" | "the-collector";

const BY_NAME: Record<string, CorePluginLogo> = {
  "discord": "assemble",
  "awesome mix": "awesome-mix",
  "now playing plus": "awesome-mix",
  "nexus mods": "forge",
  "forge": "forge",
  "romm-dock": "the-collector",
  "romm dock": "the-collector",
  "the collector": "the-collector",
};

// The plugin repositories (Source/Shared/ProtectedPluginForks.json, with their aliases) for Core's store page.
const BY_REPOSITORY: Record<string, CorePluginLogo> = {
  "m2ljay/eternal-assemble": "assemble",
  "m2ljay/eternal-mix": "awesome-mix",
  "m2ljay/eternal-forge": "forge",
  "m2ljay/vibe-nexus": "forge",
  "m2ljay/eternal-collector": "the-collector",
  "m2ljay/eternal-romm": "the-collector",
};

const normal = (value: unknown) => String(value ?? "").normalize("NFKC").trim().replace(/\s+/g, " ").toLowerCase();

// The Core logo for a Decky plugin name, or undefined (the plugin keeps its own icon).
export function corePluginLogo(name: unknown): CorePluginLogo | undefined {
  const key = normal(name);
  return Object.prototype.hasOwnProperty.call(BY_NAME, key) ? BY_NAME[key] : undefined;
}

export function corePluginLogoForRepository(repository: unknown): CorePluginLogo | undefined {
  const key = normal(repository);
  return Object.prototype.hasOwnProperty.call(BY_REPOSITORY, key) ? BY_REPOSITORY[key] : undefined;
}

// The rail glyph's SVG text (single colour, currentColor) for a plugin name, or undefined.
export function corePluginGlyph(name: unknown): string | undefined {
  const logo = corePluginLogo(name);
  return logo ? CORE_PLUGIN_GLYPHS[logo] : undefined;
}

// The full tile as an image URL, by plugin name or by repository.
export function corePluginTile(nameOrRepository: unknown): string | undefined {
  const logo = corePluginLogo(nameOrRepository) ?? corePluginLogoForRepository(nameOrRepository);
  return logo ? svgDataUrl(CORE_PLUGIN_TILES[logo]) : undefined;
}
