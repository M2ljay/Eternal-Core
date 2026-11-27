// Eternal Core: the Club tab's shared state. The backend (quick_settings/club_feed.py) owns fetching, caching and
// politeness; this only asks it for the cached feed (cheap), or for a refresh when the user presses Refresh.

import { call } from "../controlBackend";

export type ClubEventCode = "gotw" | "gotm" | "rhotm" | "gots" | "special";
export type ClubFirst = { name: string; at: number | null } | null;
// Fields after `beaten` are additive (3.3.6): older caches may lack them.
export type ClubPick = { kind: string; label: string; title: string; slug: string; platform: string; romhack: boolean;
  url: string; image: string; ends_at: number | null; beaten: number | null; event?: ClubEventCode; cover?: string;
  at100?: number | null; rating?: number | null; hours?: number | null; first_clear?: ClubFirst; first_100?: ClubFirst };
export type ClubRow = { rank: number; name: string; slug: string; beaten: number; hours?: number | null;
  hardcore?: number | null; completed?: number | null; mastered?: number | null; firsts?: number | null;
  firsthundred?: number | null; by_event?: Record<string, number> };
export type ClubNomination = { kind: string; label: string; opens_at: number | null; count: number | null;
  event?: string; period?: string; closes_at?: number | null;
  items: { title: string; platform: string; romhack: boolean; nominator: string; hours: number | null;
    nominator_slug?: string; order?: number | null; ra_game_id?: number | null; cover?: string }[] };
export type ClubScheduleItem = { at: number; text: string; title?: string; tags?: { kind: string; label: string }[]; until?: number | null };
export type ClubMember = { name: string; slug: string; rank: number | null; of: number | null; beaten: number | null;
  streak: { event: string; count: number; unit: string; best: number } | null; crowns: { crown: string; tier: string; period: string }[];
  beaten_slugs: string[] };
export type ClubSnapshot = { picks: ClubPick[]; top: ClubRow[]; leaderboard: ClubRow[]; schedule: ClubScheduleItem[];
  nominations: ClubNomination[]; member: ClubMember | null; fetched_at: number; source: string;
  stats?: Record<string, number>; carried?: string[] };
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
