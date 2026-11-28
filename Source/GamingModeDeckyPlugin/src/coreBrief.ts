// Eternal Core: the brief's data (docs/eternal/CORE-UI.md, "Phase 2: Home", 3.3.4). Dormant since 3.3.8: the brief tab
// and its hero left Home (the user's call); kept, pure and tested, for the next Home pieces that need the same data
// (Steam's stores, the Decky weather plugin's top-bar state, the Club, Forge). Missing sources leave their part out.
//
// Pure (two imports, both pure): tests load it with fakes.

import { coreHomeCopy, greetingKey } from "./coreHomeLocale";
import { coreBriefCopy, type BriefCopy } from "./coreBriefLocale";

export type BriefGame = { appid: number; name: string; minutes?: number; hero?: string[] };
export type BriefClubPick = { title: string; kind?: string; label?: string; endsAt?: number | null; art?: string; appId?: number };
export type BriefInputs = {
  now: Date; locale: string;
  last?: BriefGame;
  // window.__deckyWeatherTopbarState (the Decky weather plugin): { enabled, iconText, tempText, ... }.
  weather?: any;
  club?: BriefClubPick;
  // Core's agent (/steam/update/state): a hold phase means an update is waiting.
  steamUpdate?: { holdPhase?: string } | null;
  downloads?: number;
  mods?: number;
  achievements?: number;
};
export type BriefChip = { id: "steam-update" | "downloads" | "mods" | "achievements"; text: string; tone: "amber" | "steel" | "green" };
export type BriefModel = {
  greeting: string; date: string; time: string; weather?: string;
  continue?: { appid: number; name: string; played: string; hero: string[] };
  club?: { title: string; label: string; left?: string; art?: string; appId?: number };
  chips: BriefChip[];
};

const fill = (template: string, count: number) => template.replace("{count}", String(count));

export function playedText(minutes: number | undefined, text: BriefCopy): string {
  const value = Number(minutes);
  if (!Number.isFinite(value) || value <= 0) return text.neverPlayed;
  if (value < 60) return fill(text.minutes, Math.max(1, Math.round(value)));
  const hours = Math.round(value / 6) / 10;
  if (hours === 1) return text.hoursOne;
  return fill(text.hours, hours >= 10 ? Math.round(hours) : hours);
}

// The weather plugin's top-bar text ("☀ 18°"), only while it is switched on; at most 40 characters.
export function weatherLine(state: any): string | undefined {
  try {
    if (!state || state.enabled === false) return undefined;
    const parts = [state.iconText, state.tempText, state.conditionText ?? state.summary].filter(part => typeof part === "string" && part.trim()).map((part: string) => part.trim());
    const line = parts.join(" ").replace(/\s+/g, " ");
    return line ? line.slice(0, 40) : undefined;
  } catch { return undefined; }
}

// Days left on a pick ending at `endsAt` (Unix seconds).
export function leftText(endsAt: number | null | undefined, nowMs: number, text: BriefCopy): string | undefined {
  if (typeof endsAt !== "number" || !Number.isFinite(endsAt)) return undefined;
  const remaining = endsAt * 1000 - nowMs;
  if (remaining <= 0) return undefined;
  const days = Math.floor(remaining / 86_400_000);
  return days === 0 ? text.lastDay : days === 1 ? text.dayLeft : fill(text.daysLeft, days);
}

function format(now: Date, locale: string, options: Intl.DateTimeFormatOptions, fallback: () => string): string {
  try { return new Intl.DateTimeFormat(locale || "en", options).format(now); } catch { return fallback(); }
}

export function assembleBrief(input: BriefInputs): BriefModel {
  const home = coreHomeCopy(input.locale), text = coreBriefCopy(input.locale);
  const now = input.now;
  const model: BriefModel = {
    greeting: home[greetingKey(now.getHours())],
    date: format(now, input.locale, { weekday: "long", day: "numeric", month: "long" }, () => now.toDateString()),
    time: format(now, input.locale, { hour: "numeric", minute: "2-digit" }, () => `${now.getHours()}:${String(now.getMinutes()).padStart(2, "0")}`),
    weather: weatherLine(input.weather),
    chips: [],
  };
  if (input.last && input.last.appid > 0 && input.last.name) {
    model.continue = { appid: input.last.appid, name: input.last.name.slice(0, 80), played: playedText(input.last.minutes, text), hero: (input.last.hero ?? []).filter(url => typeof url === "string" && !!url) };
  }
  if (input.club?.title) {
    model.club = { title: input.club.title.slice(0, 80), label: text.clubLabel, left: leftText(input.club.endsAt, now.getTime(), text), art: input.club.art || undefined, appId: input.club.appId };
  }
  if (input.steamUpdate?.holdPhase) model.chips.push({ id: "steam-update", text: text.steamUpdate, tone: "amber" });
  const downloads = Math.max(0, Math.floor(Number(input.downloads) || 0));
  if (downloads > 0) model.chips.push({ id: "downloads", text: downloads === 1 ? text.downloadsOne : fill(text.downloads, downloads), tone: "steel" });
  const mods = Math.max(0, Math.floor(Number(input.mods) || 0));
  if (mods > 0) model.chips.push({ id: "mods", text: mods === 1 ? text.modsOne : fill(text.mods, mods), tone: "steel" });
  const achievements = Math.max(0, Math.floor(Number(input.achievements) || 0));
  if (achievements > 0) model.chips.push({ id: "achievements", text: fill(text.achievements, achievements), tone: "green" });
  return model;
}

// The Club's pick for the hero: the one ending soonest that the member has not beaten yet (the brief line's rule
// without its three-day window), with art from the caller's library match.
export function clubPickFor(snapshot: any, nowMs: number, art?: (pick: any) => { art?: string; appId?: number } | undefined): BriefClubPick | undefined {
  try {
    const beaten = new Set<string>(snapshot?.member?.beaten_slugs ?? []);
    let best: any;
    for (const pick of snapshot?.picks ?? []) {
      if (!pick?.title || (pick.slug && beaten.has(pick.slug))) continue;
      const ends = typeof pick.ends_at === "number" ? pick.ends_at * 1000 : Infinity;
      if (ends <= nowMs) continue;
      if (!best || ends < (typeof best.ends_at === "number" ? best.ends_at * 1000 : Infinity)) best = pick;
    }
    if (!best) return undefined;
    let found: { art?: string; appId?: number } | undefined;
    try { found = art?.(best); } catch { found = undefined; }
    return { title: String(best.title), kind: best.kind, label: best.label, endsAt: typeof best.ends_at === "number" ? best.ends_at : null, art: found?.art, appId: found?.appId };
  } catch { return undefined; }
}

// Downloads in progress in Steam's download store (queued items that are not finished), or 0.
export function downloadCount(host: any): number {
  try {
    const store = host?.downloadsStore ?? host?.SteamUIStore?.DownloadsStore;
    const items = store?.m_DownloadItems ?? store?.m_rgDownloadItems ?? store?.DownloadItems;
    const list = Array.isArray(items) ? items : items && typeof items.values === "function" ? Array.from(items.values()) : [];
    return list.filter((item: any) => item && item.completed !== true && item.bCompleted !== true).length;
  } catch { return 0; }
}

// A game's wide hero art: Steam's custom and cached hero URLs where the store offers them, then the local cache.
export function heroSources(appid: number, store: any): string[] {
  const out: string[] = [];
  try {
    const overview = store?.GetAppOverviewByAppID?.(appid);
    for (const name of ["GetCustomHeroImageURLs", "GetCachedHeroImageURLs", "GetHeroImageURLs"]) {
      const urls = store?.[name]?.(overview);
      if (Array.isArray(urls)) out.push(...urls.filter((url: unknown) => typeof url === "string" && !!url));
    }
  } catch { /* The local cache below. */ }
  out.push(`https://steamloopback.host/assets/${appid}/library_hero.jpg`, `https://steamloopback.host/customimages/${appid}_hero.png`, `https://steamloopback.host/customimages/${appid}_hero.jpg`);
  return [...new Set(out)];
}

// Minutes played from Steam's app overview (minutes_playtime_forever), if known.
export function minutesPlayed(appid: number, store: any): number | undefined {
  try { const value = Number(store?.GetAppOverviewByAppID?.(appid)?.minutes_playtime_forever); return Number.isFinite(value) ? value : undefined; } catch { return undefined; }
}
