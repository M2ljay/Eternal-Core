// Eternal Core: matching a club pick to the user's Steam library. Pure (no imports): tests/club.test.mjs loads it.
//
// The Collector (Eternal-Collector) creates non-Steam shortcuts named after ROMs and files them in per-platform
// collections; Steam games are matched too. Titles are compared after normalising accents, punctuation, a leading
// or trailing "The", "&"/"and", roman numerals and ROM tags such as "(USA)", "(Rev 1)" or "[!]". Numbers must agree,
// so "Mega Man" never matches "Mega Man 2".

export type LibraryEntry = { appId: number; name: string; shortcut: boolean; platforms: string[] };
export type PickRef = { title: string; platform?: string };
export type Match = { appId: number; name: string; score: number };

const ROMAN: Record<string, string> = { ii: "2", iii: "3", iv: "4", v: "5", vi: "6", vii: "7", viii: "8", ix: "9", x: "10", xi: "11", xii: "12" };
// ROM-set and region tags in brackets: dropped whole.
const TAG = /[([](?:[^)\]]*\b(?:usa|us|europe|eu|japan|jp|jpn|world|en|fr|de|es|it|ja|rev\s*\w+|v\d[\w.]*|beta|proto|demo|hack|unl|translated|t\+\w+|pal|ntsc(?:-[uj])?|[!abcfhopt]|\d{4})\b[^)\]]*)[)\]]/gi;

export function normalizeTitle(value: unknown): string {
  let text = String(value ?? "").normalize("NFKD").replace(/[̀-ͯ]/g, "").toLowerCase();
  text = text.replace(/[™®©]/g, "").replace(/\[[^\]]{0,8}\]/g, " ").replace(/\([a-z]{1,3}\)/g, " ").replace(TAG, " ").replace(/\.(?:sfc|smc|nes|gba|gbc|gb|n64|z64|v64|nds|iso|chd|cue|bin|zip|7z|rvz|pbp)$/i, "");
  // No-Intro moves a leading article behind the main title: "Legend of Zelda, The - A Link to the Past".
  text = text.replace(/,\s*the\b(?=\s*(?:[-:(]|$))/g, "");
  text = text.replace(/&/g, " and ").replace(/['’`]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
  text = text.replace(/^the\s+/, "").replace(/\s+the$/, "");
  return text.split(/\s+/).filter(Boolean).map(word => ROMAN[word] ?? word).join(" ");
}

function numbers(text: string): string {
  return (text.match(/\d+/g) ?? []).join(",");
}

function levenshtein(a: string, b: string): number {
  if (a === b) return 0;
  const row = Array.from({ length: b.length + 1 }, (_, index) => index);
  for (let i = 1; i <= a.length; i++) {
    let previous = row[0];
    row[0] = i;
    for (let j = 1; j <= b.length; j++) {
      const saved = row[j];
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, previous + (a[i - 1] === b[j - 1] ? 0 : 1));
      previous = saved;
    }
  }
  return row[b.length];
}

// 1 = same title; 0 = different. Requires the same numbers, then a close spelling (typos, a missing word joiner).
export function titleSimilarity(left: unknown, right: unknown): number {
  const a = normalizeTitle(left), b = normalizeTitle(right);
  if (!a || !b) return 0;
  if (a === b) return 1;
  if (numbers(a) !== numbers(b)) return 0;
  if (a.replace(/ /g, "") === b.replace(/ /g, "")) return 0.98;
  const longest = Math.max(a.length, b.length);
  if (longest < 6) return 0;
  const ratio = 1 - levenshtein(a, b) / longest;
  return ratio >= 0.9 ? ratio : 0;
}

// Platform words in collection names (The Collector's "SNES", "RomM: SNES", "Super Nintendo", ...) -> club codes.
const PLATFORM_WORDS: [RegExp, string][] = [
  [/\b(snes|super nintendo|super famicom|sfc)\b/i, "SNES"], [/\b(nes|famicom|nintendo entertainment system)\b/i, "NES"],
  [/\b(gba|game ?boy advance)\b/i, "GBA"], [/\b(gbc|game ?boy colou?r)\b/i, "GBC"], [/\b(gb|game ?boy)\b/i, "GB"],
  [/\b(n64|nintendo 64)\b/i, "N64"], [/\b(nds|nintendo ds|ds)\b/i, "NDS"], [/\b(3ds|nintendo 3ds)\b/i, "3DS"],
  [/\b(gc|ngc|gamecube)\b/i, "GC"], [/\bwii\b/i, "Wii"], [/\b(ps2|playstation 2)\b/i, "PS2"], [/\b(psp|playstation portable)\b/i, "PSP"],
  [/\b(ps1|psx|playstation)\b/i, "PS1"], [/\b(md|genesis|mega ?drive)\b/i, "MD"], [/\b(sms|master system)\b/i, "SMS"],
  [/\b(dc|dreamcast)\b/i, "DC"], [/\b(sat|saturn)\b/i, "SAT"], [/\b(pce|pc ?engine|turbografx(?:-16)?)\b/i, "PCE"],
  [/\b(arcade|mame|fbneo|fba)\b/i, "Arcade"],
];

export function platformOf(collectionName: unknown): string {
  const name = String(collectionName ?? "");
  for (const [pattern, code] of PLATFORM_WORDS) if (pattern.test(name)) return code;
  return "";
}

export function platformKey(value: unknown): string {
  const code = String(value ?? "").replace(/\s+romhack$/i, "").trim();
  return platformOf(code) || code.toUpperCase();
}

// Best library entry for a pick, or null. A shortcut on the pick's platform beats a shortcut whose platform is
// unknown, which beats a Steam game (a re-release with the same name); a shortcut filed under another platform
// never matches.
export function matchPick(pick: PickRef, library: LibraryEntry[]): Match | null {
  const wanted = pick.platform ? platformKey(pick.platform) : "";
  let best: Match | null = null;
  for (const entry of library) {
    const similarity = titleSimilarity(pick.title, entry.name);
    if (!similarity) continue;
    let weight: number;
    if (entry.shortcut) {
      const platforms = entry.platforms.map(platformKey).filter(Boolean);
      if (wanted && platforms.length && !platforms.includes(wanted)) continue;
      weight = wanted && platforms.includes(wanted) ? 3 : 2;
    } else {
      weight = 1;
    }
    const score = weight + similarity;
    if (!best || score > best.score) best = { appId: entry.appId, name: entry.name, score };
  }
  return best;
}
