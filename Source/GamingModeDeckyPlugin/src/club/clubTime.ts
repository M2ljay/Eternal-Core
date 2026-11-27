// Eternal Core: the Club tab's time maths. Pure (no imports) so tests/club.test.mjs loads it alone.
// The club's picks end at midnight UTC; the feed gives epoch seconds.

export type TimeLeft = { state: "unknown" } | { state: "ended" } | { state: "last"; hours: number } | { state: "days"; days: number };

const DAY = 86_400_000;

// Whole days left, as the club site leads with them ("1 day 16 hours left" is 1 day). Under a day is the last day.
export function timeLeft(endsAtSeconds: number | null | undefined, nowMs: number): TimeLeft {
  if (typeof endsAtSeconds !== "number" || !Number.isFinite(endsAtSeconds) || endsAtSeconds <= 0) return { state: "unknown" };
  const remaining = endsAtSeconds * 1000 - nowMs;
  if (remaining <= 0) return { state: "ended" };
  if (remaining < DAY) return { state: "last", hours: Math.max(1, Math.ceil(remaining / 3_600_000)) };
  return { state: "days", days: Math.floor(remaining / DAY) };
}

export type Ago = { value: number; unit: "minute" | "hour" | "day" };

// "Updated X ago": a negative count for Intl.RelativeTimeFormat. Null when there is no timestamp.
export function updatedAgo(updatedSeconds: number | null | undefined, nowMs: number): Ago | null {
  if (typeof updatedSeconds !== "number" || !Number.isFinite(updatedSeconds) || updatedSeconds <= 0) return null;
  const minutes = Math.max(0, Math.round((nowMs - updatedSeconds * 1000) / 60_000));
  if (minutes < 60) return { value: -minutes, unit: "minute" };
  const hours = Math.round(minutes / 60);
  if (hours < 48) return { value: -hours, unit: "hour" };
  return { value: -Math.round(hours / 24), unit: "day" };
}

export function formatAgo(locale: string, ago: Ago): string {
  try { return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(ago.value, ago.unit); }
  catch { return `${-ago.value} ${ago.unit}${ago.value === -1 ? "" : "s"} ago`; }
}

export function formatClubDate(locale: string, seconds: number | null | undefined): string {
  if (typeof seconds !== "number" || !Number.isFinite(seconds)) return "";
  try { return new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }).format(new Date(seconds * 1000)); }
  catch { return new Date(seconds * 1000).toUTCString(); }
}
