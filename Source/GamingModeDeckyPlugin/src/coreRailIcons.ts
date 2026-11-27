// Eternal Core: Quick Access rail icons at Steam's own size (docs/eternal/CORE-UI.md, "Plugin logos", 3.3.4). Core's
// tab, Jarvis's tab and the Eternal plugins' glyphs were drawn at 1em (about 16px, half of Steam's own icons); with
// Core UI on they fill the rail cell at CORE_RAIL_ICON_PX (30px in a 48-56px cell). Core UI off: 1em, as before.
//
// Pure (one import, coreUiStyles.ts, itself pure): tests check the box and the glyph markup.

import { CORE_RAIL_ICON_PX } from "./coreUiStyles";

export { CORE_RAIL_ICON_PX };
export const CORE_RAIL_ICON_ATTRIBUTE = "data-core-rail-icon";

export type RailIconBox = { display: "flex"; width: number | string; height: number | string; alignItems: "center"; justifyContent: "center"; flex: string; fontSize?: number };

// The box around a rail icon: 30px with Core UI on (its font size too, so an icon drawn at 1em fills it), 1em off.
export function railIconBox(on: boolean): RailIconBox {
  const size = on ? CORE_RAIL_ICON_PX : "1em";
  return { display: "flex", width: size, height: size, alignItems: "center", justifyContent: "center", flex: "none", ...(on ? { fontSize: CORE_RAIL_ICON_PX } : {}) };
}

// A glyph's SVG text made to fill its box crisply: 100% of the box, geometric precision, hidden from screen readers.
// Only Core's own artwork goes through here (coreArtwork.ts), never user input.
export function railGlyphHtml(svg: string): string {
  if (!/^<svg[\s>]/.test(svg)) return "";
  return svg.replace(/^<svg(?![^>]*\swidth=)/, '<svg width="100%" height="100%" shape-rendering="geometricPrecision" aria-hidden="true" focusable="false"');
}
