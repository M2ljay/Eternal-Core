// Eternal Core: the Club's line for Jarvis's brief, from a feed snapshot. Pure (no imports): tests load it alone.
// Jarvis speaks English, so the line is English (the brief's own language).

export type BriefPick = { kind: string; label?: string; slug?: string; ends_at?: number | null };
export type BriefSnapshot = { picks?: BriefPick[]; member?: { beaten_slugs?: string[] } | null } | null | undefined;

const WORDS = ["", "One", "Two", "Three"];
const WHOSE: Record<string, string> = { week: "this week's pick", month: "this month's pick", romhack: "this month's romhack", season: "this season's pick" };
// Only picks this close to their end make the brief.
export const BRIEF_DAYS = 3;

export function briefLineFor(snapshot: BriefSnapshot, nowMs: number): string | null {
  const beaten = new Set(snapshot?.member?.beaten_slugs ?? []);
  let chosen: { pick: BriefPick; remaining: number } | null = null;
  for (const pick of snapshot?.picks ?? []) {
    if (typeof pick?.ends_at !== "number" || (pick.slug && beaten.has(pick.slug))) continue;
    const remaining = pick.ends_at * 1000 - nowMs;
    if (remaining <= 0 || remaining >= (BRIEF_DAYS + 1) * 86_400_000) continue;
    if (!chosen || remaining < chosen.remaining) chosen = { pick, remaining };
  }
  if (!chosen) return null;
  const days = Math.floor(chosen.remaining / 86_400_000);
  const which = WHOSE[chosen.pick.kind] ?? (chosen.pick.label ? `the ${chosen.pick.label}` : "the club's pick");
  if (days === 0) return `Last day for ${which}, sir.`;
  return `${WORDS[days]} day${days === 1 ? "" : "s"} left on ${which}, sir.`;
}
