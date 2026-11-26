// Eternal Core: the Core palette, in one place. Every colour the plugin's own surfaces use comes
// from here (Quick Access sections, the Dashboard, Digital Stores, the Plugin Store, On this day,
// Lossless profiles, device settings). Approved 2026-11-26:
// - black background, gunmetal surfaces, steel secondary text, white primary text;
// - crimson is the one accent: selected/focused/active, and also problems and errors;
// - muted status colours: green = OK, amber = pending/warning/update, crimson = error,
//   steel = neutral/unknown;
// - no gold, no glow/haze/gradient backgrounds, no third-party brand colours as accents.
// Self-contained (no imports) so tests can load it on its own.

export const CORE = {
  black: "#000000",
  gunmetal: "#1A1D21",
  // The lighter step for nested surfaces, focused rows and borders.
  gunmetalRaised: "#2A2F36",
  steel: "#C9D1D9",
  hint: "#9AA4AE",
  white: "#FFFFFF",
  text: "#E6EAEE",
  crimson: "#D7263D",
  // Crimson as text on black/gunmetal: #D7263D is only 3.4:1 on gunmetal, this tint is 5:1.
  // Fills, bars, dots, rings and underlines use crimson itself.
  crimsonText: "#EA5A68",
  // Crimson pressed/hovered under white text (white keeps 6:1).
  crimsonDeep: "#B01F32",
  ok: "#6FA37F",
  warning: "#C98B4A",
  // Text on a green or amber fill (white there is about 2.9:1).
  onStatus: "#000000",
} as const;

export type CoreStatus = "ok" | "warning" | "error" | "neutral";

// Status as a colour for text or a dot on black/gunmetal (all 4.5:1 or better there).
export const CORE_STATUS: Record<CoreStatus, string> = {
  ok: CORE.ok,
  warning: CORE.warning,
  error: CORE.crimsonText,
  neutral: CORE.steel,
};

// "#RRGGBB" plus an opacity, as rgba() for CSS.
export function coreAlpha(hex: string, opacity: number): string {
  const value = Number.parseInt(hex.slice(1, 7), 16);
  return `rgba(${(value >> 16) & 255},${(value >> 8) & 255},${value & 255},${opacity})`;
}
