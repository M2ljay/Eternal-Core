// Eternal Core: the Jarvis brief as a hero panel on Core Home (docs/eternal/CORE-UI.md, "Phase 2: Home", 3.3.4).
// Follows the Home board: black with the Starfield behind it, a large greeting with the date, the time and the weather,
// a Continue card (the last game's hero art, A plays it), the Club's pick, small status chips, and Jarvis's orb with a
// Talk button. Glass only over game art; crimson focus; square highlights; 10px cards. Controller-navigable (one row
// of focusable cards, then the actions). The data comes from coreBrief.ts; this file only draws.

import { SP_REACT as React, DFL } from "./decky";
import { assembleBrief, clubPickFor, downloadCount, heroSources, minutesPlayed, type BriefModel } from "./coreBrief";
import { coreBriefCopy } from "./coreBriefLocale";
import { coreHomeCopy } from "./coreHomeLocale";
import { forgeUpdateCount, lastPlayedApp } from "./coreHomeTabs";
import { CoreStars } from "./CoreStars";
import { call as controlCall } from "./controlBackend";
import { clubState, ensureClubLoaded, subscribeClub } from "./club/clubStore";
import { clubAvailable } from "./club/clubAvailable";
import { matchPick } from "./club/clubMatch";
import { openGamePage, playApp, readLibrary, steamArtFor } from "./club/clubLibrary";

const { Focusable, DialogButton, Navigation } = DFL as any;

const css = `
.core-brief{position:relative;isolation:isolate;overflow:hidden;margin:0 0 24px;padding:36px 40px 32px;min-height:360px;box-sizing:border-box;color:#E6EAEE}
.core-brief-grid{display:grid;grid-template-columns:minmax(280px,1fr) minmax(0,1.6fr);gap:32px;align-items:start}
.core-brief-hello{font-size:44px;line-height:1.1;font-weight:600;letter-spacing:-.02em;color:#FFFFFF;margin:0 0 10px}
.core-brief-meta{display:flex;flex-wrap:wrap;gap:6px 14px;font-size:16px;color:#C9D1D9;margin:0 0 18px}
.core-brief-meta span+span::before{content:"\\00B7";margin-right:14px;color:#9AA4AE}
.core-brief-chips{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 22px}
.core-brief-chip{display:inline-flex;align-items:center;height:28px;padding:0 12px;font-size:13px;border-radius:0;background:#1A1D21;color:#C9D1D9}
.core-brief-chip[data-tone="amber"]{background:#C98B4A;color:#000000}
.core-brief-chip[data-tone="green"]{background:#6FA37F;color:#000000}
.core-brief-talk{display:flex;align-items:center;gap:16px}
.core-brief-talk .DialogButton{width:auto;min-width:200px;border-radius:0}
.core-brief-orb{flex:none;width:44px;height:44px;border-radius:50%;box-sizing:border-box;border:3px solid #C9D1D9;position:relative;opacity:.9}
.core-brief-orb::before{content:"";position:absolute;inset:-3px;border-radius:50%;border:3px solid transparent;border-top-color:#D7263D;border-left-color:#D7263D;transform:rotate(-45deg)}
.core-brief-orb::after{content:"";position:absolute;left:50%;top:50%;width:14px;height:14px;margin:-7px 0 0 -7px;border-radius:50%;background:#D7263D;animation:core-brief-pulse 3.2s ease-in-out infinite}
@keyframes core-brief-pulse{0%,100%{opacity:.55}50%{opacity:1}}
.core-brief-cards{display:flex;gap:16px;padding:8px 2px 10px;min-width:0}
.core-brief-card{position:relative;flex:1 1 0;min-width:0;height:230px;border-radius:10px;overflow:hidden;background:#1A1D21;transition:translate .15s ease-out}
.core-brief-card.core-brief-continue{flex-grow:1.7}
.core-brief-card img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block}
.core-brief-glass{position:absolute;left:0;right:0;bottom:0;padding:14px 16px 12px;background:rgba(0,0,0,.55);backdrop-filter:blur(12px)}
.core-brief-plain{position:absolute;left:0;right:0;bottom:0;padding:14px 16px 12px}
.core-brief-kicker{font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:#C9D1D9;margin:0 0 4px}
.core-brief-title{font-size:22px;font-weight:600;color:#FFFFFF;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.core-brief-sub{display:flex;align-items:center;gap:10px;font-size:14px;color:#C9D1D9;margin-top:4px}
.core-brief-a{display:inline-flex;align-items:center;justify-content:center;width:20px;height:20px;border-radius:50%;background:#C9D1D9;color:#000000;font-size:12px;font-weight:700}
.core-brief-card.gpfocus,.core-brief-card:focus-visible{outline:3px solid #D7263D;outline-offset:0;translate:0 -6px}
@media (prefers-reduced-motion: reduce){.core-brief-orb::after{animation:none;opacity:1}.core-brief-card{transition:none}.core-brief-card.gpfocus{translate:none}}
`;

const host = () => window as any;

function useNow(ms = 30 * 1000): Date {
  const [now, setNow] = React.useState(() => new Date());
  React.useEffect(() => { const timer = window.setInterval(() => setNow(new Date()), ms); return () => window.clearInterval(timer); }, [ms]);
  return now;
}

function useClubRevision(): number {
  const [revision, setRevision] = React.useState(0);
  React.useEffect(() => { void ensureClubLoaded(); return subscribeClub(() => setRevision(n => n + 1)); }, []);
  return revision;
}

function useSteamUpdate(): { holdPhase?: string } | null {
  const [state, setState] = React.useState<{ holdPhase?: string } | null>(null);
  React.useEffect(() => {
    let alive = true;
    const load = () => { controlCall<[], any>("core_steam_update").then(value => { if (alive) setState(value && typeof value === "object" ? value : null); }).catch(() => undefined); };
    load();
    const timer = window.setInterval(load, 5 * 60 * 1000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  return state;
}

function apps(): any {
  try { return host().appStore?.allApps ?? host().collectionStore?.allAppsCollection?.allApps ?? []; } catch { return []; }
}

// Gathers the live sources (each fail-soft) and hands them to assembleBrief.
function useBrief(locale: string): BriefModel {
  const now = useNow();
  const club = useClubRevision();
  const steamUpdate = useSteamUpdate();
  return React.useMemo(() => {
    const store = host().appStore;
    const last = lastPlayedApp(apps());
    let weather: any;
    try { weather = host().__deckyWeatherTopbarState; } catch { weather = undefined; }
    let pick;
    try {
      if (clubAvailable()) {
        const library = readLibrary();
        pick = clubPickFor(clubState().feed?.snapshot, now.getTime(), p => {
          const match = matchPick({ title: p.title, platform: p.platform }, library);
          return match ? { art: steamArtFor(match.appId) || undefined, appId: match.appId } : undefined;
        });
      }
    } catch { pick = undefined; }
    return assembleBrief({
      now, locale, weather, club: pick, steamUpdate, downloads: downloadCount(host()), mods: forgeUpdateCount(host()),
      last: last ? { appid: last.appid, name: last.name, minutes: minutesPlayed(last.appid, store), hero: heroSources(last.appid, store) } : undefined,
    });
  }, [now, club, steamUpdate, locale]);
}

function Art({ sources }: { sources: string[] }) {
  const [index, setIndex] = React.useState(0);
  if (index >= sources.length) return null;
  return <img alt="" loading="lazy" src={sources[index]} onError={() => setIndex(i => i + 1)} />;
}

export function CoreBriefHero({ locale, onTalk }: { locale: string; onTalk: () => void }) {
  const model = useBrief(locale);
  const text = coreBriefCopy(locale), home = coreHomeCopy(locale);
  const navigate = (path: string) => Navigation?.Navigate?.(path);
  const cont = model.continue, club = model.club;
  return <section className="core-brief" aria-label={home.brief}><style>{css}</style><CoreStars />
    <div className="core-brief-grid">
      <div>
        <h2 className="core-brief-hello">{model.greeting}</h2>
        <div className="core-brief-meta"><span>{model.date}</span><span>{model.time}</span>{model.weather ? <span>{model.weather}</span> : null}</div>
        {model.chips.length ? <div className="core-brief-chips">{model.chips.map(chip => <span key={chip.id} className="core-brief-chip" data-tone={chip.tone}>{chip.text}</span>)}</div> : null}
        <Focusable className="core-brief-talk" flow-children="row">
          <span className="core-brief-orb" aria-hidden="true" />
          <DialogButton onClick={onTalk}>{home.talk}</DialogButton>
        </Focusable>
      </div>
      {cont || club ? <Focusable className="core-brief-cards" flow-children="row">
        {cont ? <Focusable className="core-brief-card core-brief-continue" focusable={true} aria-label={cont.name}
          onActivate={() => { if (!playApp(cont.appid)) navigate(`/library/app/${cont.appid}`); }}
          onSecondaryButton={() => navigate(`/library/app/${cont.appid}`)}>
          <Art sources={cont.hero} />
          <div className="core-brief-glass">
            <div className="core-brief-kicker">{text.continueLabel}</div>
            <div className="core-brief-title">{cont.name}</div>
            <div className="core-brief-sub"><span className="core-brief-a" aria-hidden="true">A</span><span>{text.play}</span><span>{cont.played}</span></div>
          </div>
        </Focusable> : null}
        {club ? <Focusable className="core-brief-card" focusable={true} aria-label={club.title}
          onActivate={() => { if (club.appId) openGamePage(club.appId, navigate); }}>
          {club.art ? <Art sources={[club.art]} /> : null}
          <div className={club.art ? "core-brief-glass" : "core-brief-plain"}>
            <div className="core-brief-kicker">{club.label}</div>
            <div className="core-brief-title">{club.title}</div>
            {club.left ? <div className="core-brief-sub"><span>{club.left}</span></div> : null}
          </div>
        </Focusable> : null}
      </Focusable> : null}
    </div>
  </section>;
}

// The hero never takes Home down: any error shows `fallback` (the plain brief) instead.
export class BriefBoundary extends React.Component<{ fallback: any; children?: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: unknown) { console.warn("[Core Home] Jarvis brief hero failed; the plain brief is shown", error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}
