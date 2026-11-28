// Eternal Core: the Club tab's public surface for Home and Jarvis.
//
// Home tab registry (being added on core-ui-2; order: Jarvis brief, Consoles, Club, On this day): register
// `clubHomeTab`, or use ClubTab + clubAvailable + subscribeClub directly.
// Today (the brief): `clubBriefLine()` returns e.g. "Two days left on this week's pick." or null.

import { SP_REACT as React } from "../decky";
import { ClubTab } from "./ClubTab";
import { clubAvailable } from "./clubAvailable";
import { briefLineFor } from "./clubBrief";
import { clubCopy } from "./clubLocale";
import { clubState, ensureClubLoaded, subscribeClub } from "./clubStore";

export { ClubTab } from "./ClubTab";
export { ClubSettings } from "./ClubSettings";
export { clubAvailable } from "./clubAvailable";
export { subscribeClub, ensureClubLoaded, loadClub } from "./clubStore";

// Null when the Club is off, has no data yet, or nothing ends within three days (picks already beaten are skipped).
export function clubBriefLine(now: number = Date.now()): string | null {
  void ensureClubLoaded();
  if (!clubAvailable()) return null;
  return briefLineFor(clubState().feed?.snapshot, now);
}

export const clubHomeTab = {
  id: "club",
  order: 30,
  title: (locale: string) => clubCopy(locale).tab,
  available: clubAvailable,
  subscribe: subscribeClub,
  render: (props: { locale?: string; getLocale?: () => string }) => React.createElement(ClubTab, props),
};
