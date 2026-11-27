// Eternal Core: whether Home shows the Club tab. False while the "Show the Club tab" setting is off, and until
// there is club data (cached data counts, even when stale). The first call starts one background load;
// subscribe with subscribeClub (./clubStore) to hear when the answer changes.

import { clubState, ensureClubLoaded, type ClubState } from "./clubStore";

export function isClubAvailable(state: ClubState): boolean {
  if (state.settings?.enabled === false || state.feed?.enabled === false) return false;
  return Boolean(state.feed?.snapshot?.picks?.length);
}

export function clubAvailable(): boolean {
  void ensureClubLoaded();
  return isClubAvailable(clubState());
}
