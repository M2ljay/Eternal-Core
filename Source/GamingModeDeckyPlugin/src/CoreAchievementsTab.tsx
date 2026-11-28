// Eternal Core 3.3.10: Home's Achievements tab (docs/eternal/CORE-UI.md, "Home in 3.3.10"; board HomeAchievements).
// "Recent unlocks" on the left (icon, name, game, when, a source tag: Steam raised grey, RetroAchievements amber with
// black text; rarity or Hardcore in crimson text), "Closest to 100%" on the right (crimson bars). Data rules are in
// coreAchievements.ts. Read when the tab opens and every ten minutes while it is open; nothing is ever written.

import { SP_REACT as React, DFL } from "./decky";
import { coreHomeCopy } from "./coreHomeLocale";
import { gameMenuProps } from "./coreGameMenu";
import {
  CLOSEST_LIMIT, COLLECTOR_ACHIEVEMENTS_READY, MODEL_KEY, RARITY_KEY, RECENT_LIMIT, collectorBridge, fromBridge, mergeClosest, mergeUnlocks, percentOf, raNearlyDone, raUnlock,
  rarityCache, rarityStale, ratesFrom, readJson, steamClosest, steamUnlocks, storeRates, storedModel, unlockNote, whenText, withRarity, writeJson,
  type NearlyDone, type RarityCache, type Unlock,
} from "./coreAchievements";

const { Focusable, Navigation } = DFL as any;

// Steam's achievement progress cache (index.tsx hands in its finder: it is a Steam store found by module search).
let progressSource: () => any = () => undefined;
export function setAchievementProgressSource(source: () => any) { progressSource = source; }

const STEAM_GAMES = 8;       // most recently played Steam games asked for their unlocks
const TIMEOUT_MS = 5000;
const REFRESH_MS = 10 * 60_000;
const SHORTCUT_BASE = 0x80000000; // non-Steam shortcuts have no Steam achievements

const css = `
.core-ach{display:grid;grid-template-columns:minmax(0,1.6fr) minmax(0,1fr);gap:28px;padding:50px 2px 24px;color:#C9D1D9}
.core-ach h3{margin:0 0 14px;font-size:11px;font-weight:600;letter-spacing:.14em;text-transform:uppercase;color:#C9D1D9}
.core-ach-list{display:flex;flex-direction:column;gap:10px}
.core-ach-row{display:flex;align-items:center;gap:14px;min-height:64px;box-sizing:border-box;padding:0 15px;background:#1A1D21;border-radius:var(--core-radius-float,10px);transition:translate .15s ease-out;outline:none}
.core-ach-row.gpfocus,.core-ach-row:focus-visible{background:#2A2F36;translate:0 -4px;outline:none}
.core-ach-icon{width:42px;height:42px;flex:none;background:linear-gradient(135deg,#2A2F36,#1A1D21);object-fit:cover}
.core-ach-text{flex:1;min-width:0}
.core-ach-name{font-size:15px;font-weight:600;color:#FFFFFF;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.core-ach-sub{font-size:12px;color:#9AA4AE;margin-top:3px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.core-ach-note{font-size:11px;color:#EA5A68;white-space:nowrap}
.core-ach-tag{height:18px;padding:0 7px;display:inline-flex;align-items:center;font-size:9px;font-weight:600;white-space:nowrap;background:#2A2F36;color:#C9D1D9}
.core-ach-tag.ra{background:#C98B4A;color:#000000}
.core-ach-card{box-sizing:border-box;padding:13px 15px;background:#1A1D21;border-radius:var(--core-radius-float,10px);transition:translate .15s ease-out;outline:none}
.core-ach-card.gpfocus,.core-ach-card:focus-visible{background:#2A2F36;translate:0 -4px;outline:none}
.core-ach-head{display:flex;justify-content:space-between;gap:12px;font-size:15px;font-weight:600;color:#FFFFFF}
.core-ach-bar{margin-top:10px;height:6px;background:#2A2F36}
.core-ach-bar i{display:block;height:6px;background:#D7263D}
.core-ach-left{margin-top:7px;font-size:11px;color:#9AA4AE}
.core-ach-empty{font-size:15px;color:#9AA4AE;padding:8px 2px;text-align:left;outline:none;background:transparent}
@media (prefers-reduced-motion: reduce){.core-ach-row,.core-ach-card{transition:none}}
`;

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T | undefined> {
  return new Promise(resolve => {
    const timer = setTimeout(() => resolve(undefined), ms);
    promise.then(value => { clearTimeout(timer); resolve(value); }, () => { clearTimeout(timer); resolve(undefined); });
  });
}

// The most recently played Steam games (no shortcuts), for their unlocks.
function recentSteamGames(): Array<{ appid: number; name: string }> {
  try {
    const all = (window as any).appStore?.allApps;
    const list: any[] = Array.isArray(all) ? all : all && typeof all.values === "function" ? Array.from(all.values()) : [];
    return list.filter(app => Number(app?.appid) > 0 && Number(app.appid) < SHORTCUT_BASE && Number(app?.rt_last_time_played) > 0)
      .sort((a, b) => Number(b.rt_last_time_played) - Number(a.rt_last_time_played)).slice(0, STEAM_GAMES)
      .map(app => ({ appid: Number(app.appid), name: String(app.display_name ?? "") }));
  } catch { return []; }
}
const nameOf = (appid: number) => { try { return String((window as any).appStore?.GetAppOverviewByAppID?.(appid)?.display_name ?? ""); } catch { return ""; } };

const storage = (): Storage | undefined => { try { return window.localStorage; } catch { return undefined; } };
// The rarity cache, shared by every render (coreAchievements.ts: per game, at most every 24 hours).
let rarity: RarityCache = rarityCache(readJson(storage(), RARITY_KEY));
const rarityListeners = new Set<() => void>();
let rarityRevision = 0;
function keepRates(appId: number, rates: Record<string, number>) {
  if (!Object.keys(rates).length) return;
  rarity = storeRates(rarity, appId, rates, Math.floor(Date.now() / 1000));
  writeJson(storage(), RARITY_KEY, rarity);
  rarityRevision++;
  for (const listener of [...rarityListeners]) { try { listener(); } catch { /* Others still hear it. */ } }
}
const subscribeRarity = (listener: () => void) => { rarityListeners.add(listener); return () => { rarityListeners.delete(listener); }; };

// Read in the background; each game's rarity is kept as its answer arrives (only when its cached rates are stale).
export async function loadAchievements(): Promise<{ recent: Unlock[]; closest: NearlyDone[] }> {
  const apps = (window as any).SteamClient?.Apps;
  const steam = await Promise.all(recentSteamGames().map(async game => {
    if (typeof apps?.GetMyAchievementsForApp !== "function") return [];
    const answer = await withTimeout(Promise.resolve().then(() => apps.GetMyAchievementsForApp(String(game.appid))), TIMEOUT_MS);
    if (answer !== undefined && rarityStale(rarity, game.appid, Math.floor(Date.now() / 1000))) keepRates(game.appid, ratesFrom(answer));
    return steamUnlocks(answer, game);
  }));
  let cache: any;
  try { cache = progressSource()?.m_achievementProgress?.mapCache; } catch { cache = undefined; }
  const bridge = collectorBridge(window);
  const [raRecent, raClosest] = await Promise.all([
    withTimeout(fromBridge(bridge, "getRecentUnlocks", RECENT_LIMIT, raUnlock), TIMEOUT_MS),
    withTimeout(fromBridge(bridge, "getClosestToComplete", CLOSEST_LIMIT, raNearlyDone), TIMEOUT_MS),
  ]);
  return { recent: mergeUnlocks([...steam, raRecent ?? []]), closest: mergeClosest([steamClosest(cache, nameOf), raClosest ?? []]) };
}

const openAchievements = (appId?: number) => { if (appId) { try { Navigation?.Navigate?.(`/library/app/${appId}/achievements`); } catch { /* Stay on Home. */ } } };

export function CoreAchievementsTab({ locale }: { locale: string }) {
  const text = coreHomeCopy(locale);
  // Drawn at once from the last model shown (never waiting on the network), then refreshed in the background.
  const [data, setData] = React.useState<{ recent: Unlock[]; closest: NearlyDone[] } | null>(() => storedModel(readJson(storage(), MODEL_KEY)) ?? null);
  React.useSyncExternalStore(subscribeRarity, () => rarityRevision, () => rarityRevision);
  React.useEffect(() => {
    let alive = true;
    const read = () => void loadAchievements().then(next => { if (alive) { setData(next); writeJson(storage(), MODEL_KEY, next); } })
      .catch(() => { if (alive) setData(prior => prior ?? { recent: [], closest: [] }); });
    read();
    const timer = window.setInterval(read, REFRESH_MS);
    // The Collector's bridge may register after Core: read again when it says it is ready.
    const ready = () => read();
    try { window.addEventListener(COLLECTOR_ACHIEVEMENTS_READY, ready); } catch { /* The next refresh reads it. */ }
    return () => { alive = false; window.clearInterval(timer); try { window.removeEventListener(COLLECTOR_ACHIEVEMENTS_READY, ready); } catch { /* Gone. */ } };
  }, []);
  const now = Date.now();
  const recent = (data?.recent ?? []).map(unlock => withRarity(unlock, rarity)), closest = data?.closest ?? [];
  // 3.3.10: nothing to show (once read): one line of hint text where the lists would start.
  if (data && !recent.length && !closest.length) return <div className="core-home"><style>{css}</style>
    <Focusable className="core-ach-empty" focusable={true}>{text.achievementsEmpty}</Focusable></div>;
  return <div className="core-home"><style>{css}</style>
    <Focusable className="core-ach" flow-children="row">
      <Focusable className="core-ach-col" flow-children="column">
        <h3>{text.recentUnlocks}</h3>
        {recent.length ? <div className="core-ach-list">{recent.map((unlock, i) => {
          const note = unlockNote(unlock, { hardcore: text.hardcore, players: text.players });
          return <Focusable key={`${unlock.source}-${unlock.appId ?? unlock.game}-${unlock.name}-${i}`} className="core-ach-row" focusable={true}
            aria-label={`${unlock.name}, ${unlock.game}`} onActivate={() => openAchievements(unlock.appId)} onClick={() => openAchievements(unlock.appId)} {...gameMenuProps(unlock.appId)}>
            {unlock.icon ? <img className="core-ach-icon" alt="" src={unlock.icon} /> : <span className="core-ach-icon" />}
            <div className="core-ach-text"><div className="core-ach-name">{unlock.name}</div>
              <div className="core-ach-sub">{[unlock.game, whenText(unlock.at, now, locale)].filter(Boolean).join(" · ")}</div></div>
            {note && <span className="core-ach-note">{note}</span>}
            <span className={`core-ach-tag${unlock.source === "ra" ? " ra" : ""}`}>{unlock.source === "ra" ? "RetroAchievements" : "Steam"}</span>
          </Focusable>;
        })}</div> : <Focusable className="core-ach-row" focusable={true}><span className="core-ach-empty">{data ? text.achievementsEmpty : "…"}</span></Focusable>}
      </Focusable>
      <Focusable className="core-ach-col" flow-children="column">
        <h3>{text.closest}</h3>
        <div className="core-ach-list">{closest.map((entry, i) => {
          const pct = percentOf(entry);
          const left = text.left.replace("{n}", String(entry.total - entry.unlocked));
          return <Focusable key={`${entry.source}-${entry.appId ?? entry.game}-${i}`} className="core-ach-card" focusable={true} aria-label={`${entry.game}, ${pct}%`}
            onActivate={() => openAchievements(entry.appId)} onClick={() => openAchievements(entry.appId)} {...gameMenuProps(entry.appId)}>
            <div className="core-ach-head"><span>{entry.game}</span><span>{pct}%</span></div>
            <div className="core-ach-bar"><i style={{ width: `${pct}%` }} /></div>
            <div className="core-ach-left">{entry.source === "ra" ? `RA · ${left}` : left}</div>
          </Focusable>;
        })}</div>
      </Focusable>
    </Focusable>
  </div>;
}
