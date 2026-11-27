// Eternal Core: the Club tab's shared state. The backend (quick_settings/club_feed.py) owns fetching, caching and
// politeness; this only asks it for the cached feed (cheap), or for a refresh when the user presses Refresh.

import { call } from "../controlBackend";

export type ClubPick = { kind: string; label: string; title: string; slug: string; platform: string; romhack: boolean;
  url: string; image: string; ends_at: number | null; beaten: number | null };
export type ClubRow = { rank: number; name: string; slug: string; beaten: number };
export type ClubNomination = { kind: string; label: string; opens_at: number | null; count: number | null;
  items: { title: string; platform: string; romhack: boolean; nominator: string; hours: number | null }[] };
export type ClubMember = { name: string; slug: string; rank: number | null; of: number | null; beaten: number | null;
  streak: { event: string; count: number; unit: string; best: number } | null; crowns: { crown: string; tier: string; period: string }[];
  beaten_slugs: string[] };
export type ClubSnapshot = { picks: ClubPick[]; top: ClubRow[]; leaderboard: ClubRow[]; schedule: { at: number; text: string }[];
  nominations: ClubNomination[]; member: ClubMember | null; fetched_at: number; source: string };
export type ClubFeedReply = { enabled: boolean; display_name?: string; snapshot: ClubSnapshot | null; updated?: number | null;
  stale?: boolean; error?: string; refresh_available_at?: number };
export type ClubSettingsValue = { enabled: boolean; display_name: string };
export type ClubState = { settings: ClubSettingsValue | null; feed: ClubFeedReply | null; loading: boolean; failed: boolean };

let state: ClubState = { settings: null, feed: null, loading: false, failed: false };
const listeners = new Set<() => void>();
let inflight: Promise<void> | null = null;

const set = (patch: Partial<ClubState>) => { state = { ...state, ...patch }; listeners.forEach(listener => listener()); };

export const subscribeClub = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
export const clubState = () => state;

export function loadClub(force = false): Promise<void> {
  if (inflight && !force) return inflight;
  let run: Promise<void> | null = null;
  run = (async () => {
    set({ loading: true });
    try {
      const [settings, feed] = await Promise.all([
        state.settings ? Promise.resolve(state.settings) : call<[], ClubSettingsValue>("get_club_settings"),
        call<[boolean], ClubFeedReply>("get_club_feed", force),
      ]);
      set({ settings, feed, failed: false });
    } catch {
      set({ failed: true });
    } finally {
      set({ loading: false });
      if (inflight === run) inflight = null;
    }
  })();
  inflight = run;
  return run;
}

// Starts one background load the first time anything asks; later calls just read the state.
let started = false;
export function ensureClubLoaded(): Promise<void> | null {
  if (started) return inflight;
  started = true;
  return loadClub(false);
}

export async function saveClubSettings(next: ClubSettingsValue): Promise<ClubSettingsValue> {
  const saved = await call<[boolean, string], ClubSettingsValue>("set_club_settings", next.enabled, next.display_name);
  set({ settings: saved });
  void loadClub(false);
  return saved;
}
