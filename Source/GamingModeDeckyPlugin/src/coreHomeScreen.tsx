// Eternal Core 3.3.10: Home's screen one, for both pages, and the Consoles page's screen two (docs/eternal/CORE-UI.md,
// "Home in 3.3.10"; the approved boards Main and HomeConsolesPage).
//
// Screen one is one screen tall: the focused item's art fills it with a dark fade toward the bottom, and a row sits on
// the footer. Home: the recent games Steam lists (portrait, Steam's own list and order), A opens the game. Consoles: one
// card per console, most recently played first (the console's ColorGrade hero with its logo centred), A opens that
// console's collection. No game or console name, no count and no status line anywhere on the screen (a console's name
// is drawn only when it has no art, no logo and no icon: never an empty box). The focused tile is
// larger and lifts; the others are dimmed. No outline. Home and Consoles sit at the top left, switched with L1 and R1.
//
// Everything renders inside HomeScreenBoundary: on any error, Steam's own row (`native`) is shown instead.

import { SP_REACT as React, DFL } from "./decky";
import { coreUiSetting } from "./coreUiSetting";
import { coreHomeCopy } from "./coreHomeLocale";
import { favoriteArt, favoriteLayers } from "./coreFavoriteArt";
import {
  HERO_DELAY_MS, HERO_FADE_MS, HOME_ROW, consolePicks, homeIconSet, homePage, neighbours, pageForButton, setFocusedConsole, setHomePage,
  subscribeHomePage, focusedConsoleId, type ConsolePick, type HomePage,
} from "./coreHomePages";
import { collectionRoute, consoleCards, type ConsoleCard } from "./coreConsoles";
import { collectionIcon, matchCollection } from "./coreLibraryIcons";
import { CoreStars } from "./CoreStars";
import { clubState, loadClub, subscribeClub } from "./club/clubStore";
import { assembleEventFor } from "./club/clubAssemble";
import { matchPick } from "./club/clubMatch";
import { readLibrary } from "./club/clubLibrary";
import { timeLeft } from "./club/clubTime";
import { consoleHero, consolePack, loadConsolePack, subscribeConsoleHeroes, subscribeConsolePack, wantConsoleHeroes } from "./coreConsolePack";

const { Focusable, Navigation } = DFL as any;

export const steamWindow = () => window as any;
export function userCollections(): any[] {
  try { const list = steamWindow().collectionStore?.userCollections; return Array.isArray(list) ? list : list && typeof list.values === "function" ? Array.from(list.values()) : []; } catch { return []; }
}
const appName = (appid: number) => { try { return String(steamWindow().appStore?.GetAppOverviewByAppID?.(appid)?.display_name ?? ""); } catch { return ""; } };
const openGame = (appid: number) => { try { Navigation?.Navigate?.(`/library/app/${appid}`); } catch { /* Stay on Home. */ } };

const R = HOME_ROW;
export const homeScreenCss = `
.core-screen{position:relative;width:100%;height:100vh;min-height:100vh;overflow:visible;isolation:isolate;background:#000000}
.core-screen-hero{position:absolute;inset:0;z-index:0;overflow:hidden;background:#000000;isolation:isolate}
.core-screen-layer{position:absolute;inset:0}
.core-screen-layer.core-fade-in{animation:core-hero-in ${HERO_FADE_MS}ms ease-out both}
@keyframes core-hero-in{from{opacity:0}to{opacity:1}}
.core-screen-hero img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:center;display:block}
.core-screen-hero canvas{position:absolute;inset:0;width:100%;height:100%}
.core-screen-fade{position:absolute;inset:0;z-index:1;pointer-events:none;background:linear-gradient(180deg,rgba(0,0,0,.35) 0%,rgba(0,0,0,0) 22%,rgba(0,0,0,0) 45%,rgba(0,0,0,.85) 82%,#000000 100%)}
.core-pages{position:absolute;left:${R.insetVw}vw;top:0;height:40px;z-index:3;display:flex;align-items:center;gap:17px;pointer-events:none}
.core-pages .core-key{display:inline-flex;align-items:center;justify-content:center;height:17px;padding:0 5px;border:1.5px solid #9AA4AE;font-size:9px;font-weight:700;color:#9AA4AE;line-height:1}
.core-pages .core-page{position:relative;height:28px;display:inline-flex;align-items:center;font-size:13px;font-weight:500;letter-spacing:.1em;text-transform:uppercase;color:#9AA4AE}
.core-pages .core-page.active{color:#FFFFFF;font-weight:600}
.core-pages .core-page.active::after{content:"";position:absolute;left:0;right:0;bottom:0;height:3px;background:#D7263D}
.core-row{position:absolute;left:0;right:0;bottom:calc(${R.footerPx}px + ${R.aboveFooterVh}vh);z-index:2;display:flex;align-items:flex-end;gap:${R.gapVw}vw;padding:12px ${R.insetVw}vw 0;overflow-x:auto;overflow-y:visible;scrollbar-width:none;scroll-padding-inline:${R.insetVw}vw}
.core-row::-webkit-scrollbar{display:none}
.core-tile{position:relative;flex:0 0 auto;height:${R.gameVh}vh;aspect-ratio:2/3;border-radius:6px;overflow:hidden;opacity:${R.dim};outline:none;
  background:linear-gradient(160deg,#2A2F36 0%,#15181C 100%);box-shadow:0 10px 24px rgba(0,0,0,.6);transition:height .15s ease-out,translate .15s ease-out,opacity .15s ease-out}
.core-tile.core-console{aspect-ratio:16/9;height:${R.consoleVh}vh}
.core-tile.gpfocus,.core-tile:focus-visible,.core-tile:focus{opacity:1;height:${R.gameFocusedVh}vh;translate:0 -${R.liftPx}px;outline:none;box-shadow:0 14px 30px rgba(0,0,0,.75)}
.core-tile.core-console.gpfocus,.core-tile.core-console:focus-visible,.core-tile.core-console:focus{height:${R.consoleFocusedVh}vh}
.core-tile img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block}
.core-tile .core-tile-shade{position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,0) 45%,rgba(0,0,0,.6) 100%)}
.core-tile img.core-tile-logo{inset:0;margin:auto;width:auto;height:auto;max-width:70%;max-height:46%;object-fit:contain}
.core-tile.core-plain{background:#1A1D21}
.core-tile .core-tile-name{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;padding:0 10px;text-align:center;font-size:15px;font-weight:700;color:#FFFFFF}
.core-tile .core-tile-tag{position:absolute;left:0;top:0;z-index:2;padding:4px 8px;font-size:9px;font-weight:600;letter-spacing:.12em;text-transform:uppercase;color:#000000;border-radius:0}
.core-two{position:relative;padding:28px ${R.insetVw}vw 64px;min-height:60vh}
.core-two .core-two-label{font-size:11px;font-weight:600;letter-spacing:.14em;text-transform:uppercase;color:#C9D1D9;margin:0 0 14px}
.core-two .core-two-row{display:flex;gap:16px;overflow-x:auto;padding:10px 2px 14px;scrollbar-width:none}
.core-two .core-two-row::-webkit-scrollbar{display:none}
.core-two .core-tile{height:auto;width:145px;aspect-ratio:2/3;opacity:1}
.core-two .core-tile.gpfocus,.core-two .core-tile:focus-visible{height:auto;translate:0 -${R.liftPx}px}
.core-two .core-two-empty{font-size:16px;color:#9AA4AE}
@media (prefers-reduced-motion: reduce){.core-tile{transition:none}.core-screen-layer.core-fade-in{animation:none}}
`;

// L1 HOME CONSOLES R1, top left. Not focusable: the bumpers switch pages.
function PageTabs({ page, locale }: { page: HomePage; locale: string }) {
  const text = coreHomeCopy(locale);
  return <div className="core-pages" aria-hidden="true">
    <span className="core-key">L1</span>
    <span className={`core-page${page === "home" ? " active" : ""}`}>{text.home}</span>
    <span className={`core-page${page === "consoles" ? " active" : ""}`}>{text.consoles}</span>
    <span className="core-key">R1</span>
  </div>;
}

// One image from a list of sources, the next on each failure; nothing when all fail.
function Art({ sources, className }: { sources: string[]; className?: string }) {
  const [failed, setFailed] = React.useState(0);
  React.useEffect(() => setFailed(0), [sources.join("|")]);
  const src = sources[failed];
  return src ? <img key={src} className={className} alt="" src={src} onError={() => setFailed(n => n + 1)} /> : null;
}

function GameTile({ appid, autoFocus, onFocus }: { appid: number; autoFocus?: boolean; onFocus: () => void }) {
  const art = React.useMemo(() => favoriteArt(appid, steamWindow().appStore), [appid]);
  const [failed, setFailed] = React.useState({ portrait: 0, hero: 0, logo: 0 });
  const layers = favoriteLayers(art, failed);
  const fail = (kind: "portrait" | "hero" | "logo") => () => setFailed(prior => ({ ...prior, [kind]: prior[kind] + 1 }));
  return <Focusable className="core-tile" focusable={true} aria-label={appName(appid)} autoFocus={autoFocus}
    onActivate={() => openGame(appid)} onClick={() => openGame(appid)} onGamepadFocus={onFocus} onFocus={onFocus}>
    {layers.kind === "portrait"
      ? <img key={layers.src} alt="" loading="lazy" src={layers.src} onError={fail("portrait")} />
      : <>{layers.hero && <img key={layers.hero} alt="" src={layers.hero} onError={fail("hero")} />}
        {layers.logo && <img key={layers.logo} className="core-tile-logo" alt="" src={layers.logo} onError={fail("logo")} />}</>}
  </Focusable>;
}

// A console's tile: its ColorGrade hero with its logo; without the hero, gunmetal with the logo, else the console's
// icon (the user's Library icon set or Core's own drawing), else its name. Never an empty box. A club pick held by this
// console carries the pick's tag.
function ConsoleTile({ card, logo, hero, tag, autoFocus, onFocus }: { card: ConsoleCard; logo?: string; hero?: string; tag?: ConsolePick; autoFocus?: boolean; onFocus: () => void }) {
  const open = () => { try { Navigation?.Navigate?.(collectionRoute(card.id)); } catch { /* Stay on Home. */ } };
  const [logoFailed, setLogoFailed] = React.useState(false);
  const icon = React.useMemo(() => { try { return collectionIcon(card.name, matchCollection(card.name), homeIconSet()); } catch { return undefined; } }, [card.name]);
  const mark = !logoFailed && logo ? logo : icon;
  return <Focusable className={`core-tile core-console${hero ? "" : " core-plain"}`} focusable={true} aria-label={card.name} autoFocus={autoFocus}
    onActivate={open} onClick={open} onGamepadFocus={onFocus} onFocus={onFocus}>
    {hero && <img alt="" src={hero} />}
    {hero && <span className="core-tile-shade" />}
    {mark ? <img className="core-tile-logo" alt="" src={mark} onError={() => setLogoFailed(true)} /> : <span className="core-tile-name">{card.name}</span>}
    {tag && <span className="core-tile-tag" style={{ background: tag.tone }}>{tag.label}</span>}
  </Focusable>;
}

// A club pick the user owns, leading the Consoles row: the game's hero (else its box art) at the console tiles' size,
// the pick's tag at the top; A opens the game page.
function PickTile({ pick, autoFocus, onFocus }: { pick: ConsolePick; autoFocus?: boolean; onFocus: () => void }) {
  const art = React.useMemo(() => favoriteArt(pick.appId, steamWindow().appStore), [pick.appId]);
  return <Focusable className="core-tile core-console core-plain" focusable={true} aria-label={`${pick.label}: ${pick.title}`} autoFocus={autoFocus}
    onActivate={() => openGame(pick.appId)} onClick={() => openGame(pick.appId)} onGamepadFocus={onFocus} onFocus={onFocus}>
    <Art sources={[...art.hero, ...art.portrait]} />
    <span className="core-tile-shade" />
    <span className="core-tile-tag" style={{ background: pick.tone }}>{pick.label}</span>
  </Focusable>;
}

// The club's owned picks for the Consoles row, from the Club's own data (refreshed hourly by clubStore.ts; no extra fetch).
export function useConsolePicks(): ConsolePick[] {
  const club = React.useSyncExternalStore(subscribeClub, clubState, clubState);
  React.useEffect(() => { if (!clubState().feed) void loadClub(false); }, []);
  const picks = club.feed?.snapshot?.picks;
  return React.useMemo(() => {
    if (!picks?.length) return [];
    const library = readLibrary();
    return consolePicks(picks, pick => assembleEventFor(pick), pick => matchPick({ title: pick.title, platform: pick.platform }, library),
      pick => timeLeft(pick.ends_at, Date.now()).state === "ended");
  }, [picks]);
}

// The full-screen art: changes only after focus has rested HERO_DELAY_MS on a tile, crossfading over HERO_FADE_MS. No
// art (a console without a ColorGrade hero): black with the Starfield.
function Hero({ sources }: { sources: string[] }) {
  const key = sources.join("|");
  const [layers, setLayers] = React.useState<Array<{ id: number; sources: string[] }>>([]);
  const next = React.useRef(0);
  React.useEffect(() => {
    setLayers(prior => [...prior.slice(-1), { id: ++next.current, sources }]);
    const timer = window.setTimeout(() => setLayers(prior => prior.slice(-1)), HERO_FADE_MS + 50);
    return () => window.clearTimeout(timer);
  }, [key]);
  const top = layers[layers.length - 1];
  return <div className="core-screen-hero">
    {!top?.sources.length ? <CoreStars /> : null}
    {layers.filter(layer => layer.sources.length).map((layer, i, all) => <div key={layer.id} className={`core-screen-layer${i === all.length - 1 && layers.length > 1 ? " core-fade-in" : ""}`}>
      <Art sources={layer.sources} /></div>)}
  </div>;
}

// Loads images ahead (the art of the neighbouring tiles); the browser keeps them for the crossfade.
function preload(urls: string[]) {
  for (const url of urls.slice(0, 8)) { try { const image = new Image(); image.decoding = "async"; image.src = url; } catch { /* Loaded when shown. */ } }
}

export function useConsoleCards(): ConsoleCard[] {
  const pack = React.useSyncExternalStore(subscribeConsolePack, consolePack, consolePack);
  React.useEffect(() => { if (!consolePack()) void loadConsolePack(false); }, []);
  const slugs = React.useMemo(() => new Set(pack?.slugs ?? []), [pack]);
  // Every game kept (screen two shows the first 16; a club pick's console is found among all of them).
  return React.useMemo(() => consoleCards(userCollections(), slugs, 5000), [slugs]);
}

type RowItem = { kind: "game"; appid: number } | { kind: "pick"; pick: ConsolePick } | { kind: "console"; card: ConsoleCard; tag?: ConsolePick };
const itemKey = (item: RowItem) => item.kind === "game" ? `g${item.appid}` : item.kind === "pick" ? `p${item.pick.appId}` : `c${item.card.id}`;

// Screen one. `games` are Steam's recent games for the Home page; the Consoles page reads its consoles (and the club's
// owned picks) itself.
export function HomeScreenOne({ games, autoFocus, getLocale }: { games: number[]; autoFocus?: boolean; getLocale: () => string }) {
  const page = React.useSyncExternalStore(subscribeHomePage, homePage, homePage);
  React.useSyncExternalStore(subscribeConsoleHeroes, () => consoleHeroRevision(), () => consoleHeroRevision());
  const cards = useConsoleCards();
  const picks = useConsolePicks();
  const [focused, setFocused] = React.useState(0);
  const [rested, setRested] = React.useState(0);
  const root = React.useRef<HTMLDivElement | null>(null);
  const pack = consolePack();
  const items: RowItem[] = React.useMemo(() => page === "consoles"
    ? [...picks.map(pick => ({ kind: "pick" as const, pick })),
      ...cards.map(card => ({ kind: "console" as const, card, tag: picks.find(pick => card.games.some(game => game.appid === pick.appId)) }))]
    : games.map(appid => ({ kind: "game" as const, appid })), [page, picks, cards, games]);
  const index = Math.min(focused, Math.max(0, items.length - 1));
  const heroAt = Math.min(rested, Math.max(0, items.length - 1));
  // The art follows focus once it has rested on a tile (HERO_DELAY_MS).
  React.useEffect(() => { const timer = window.setTimeout(() => setRested(index), HERO_DELAY_MS); return () => window.clearTimeout(timer); }, [index]);
  const heroOf = (item: RowItem | undefined): string[] => {
    if (!item) return [];
    if (item.kind === "console") { const url = consoleHero(item.card.slug); return url ? [url] : []; }
    const art = favoriteArt(item.kind === "game" ? item.appid : item.pick.appId, steamWindow().appStore);
    return item.kind === "pick" ? [...art.hero, ...art.portrait] : art.hero;
  };
  // Heroes: the focused console's first, then its neighbours, then the rest; game art of the neighbours preloaded.
  React.useEffect(() => {
    if (page !== "consoles") return;
    const near = [index, ...neighbours(index, items.length)].map(at => items[at]).filter(item => item?.kind === "console") as Array<{ kind: "console"; card: ConsoleCard }>;
    wantConsoleHeroes([...near.map(item => item.card.slug), ...cards.map(card => card.slug)]);
  }, [page, cards, index, items.length]);
  React.useEffect(() => { preload(neighbours(index, items.length).flatMap(at => items[at]?.kind === "console" ? [] : heroOf(items[at]).slice(0, 1))); }, [index, items]);
  // Screen two shows the focused console's games (for a club pick, the console that holds the game).
  React.useEffect(() => {
    if (page !== "consoles") return;
    const item = items[index];
    setFocusedConsole(item?.kind === "console" ? item.card.id : item?.kind === "pick" ? cards.find(card => card.games.some(game => game.appid === item.pick.appId))?.id ?? "" : "");
  }, [page, items, index, cards]);
  // A page switch moves focus to the new row's first tile (the old tile is gone).
  const switched = React.useRef(false);
  React.useEffect(() => {
    if (!switched.current) return;
    switched.current = false;
    setFocused(0); setRested(0);
    const timer = window.setTimeout(() => { try { root.current?.querySelector<HTMLElement>(".core-tile")?.focus(); } catch { /* Steam keeps its focus. */ } }, 0);
    return () => window.clearTimeout(timer);
  }, [page]);
  const onButtonDown = (event: any) => {
    const next = pageForButton(homePage(), event?.detail?.button);
    if (!next) return;
    event?.preventDefault?.(); event?.stopPropagation?.();
    switched.current = true;
    setHomePage(next);
  };
  const locale = getLocale();
  return <Focusable className="core-screen" ref={root} data-core-home-one={page} onButtonDown={onButtonDown}>
    <style>{homeScreenCss}</style>
    <Hero sources={heroOf(items[heroAt])} />
    <div className="core-screen-fade" />
    <PageTabs page={page} locale={locale} />
    {!items.length
      ? <div className="core-row"><Focusable className={`core-tile${page === "consoles" ? " core-console" : ""} core-plain`} focusable={true} autoFocus={autoFocus}
        aria-label={page === "consoles" ? coreHomeCopy(locale).consolesEmpty : ""} /></div>
      : <Focusable className="core-row" flow-children="row">
        {items.map((item, i) => {
          const first = autoFocus && i === 0, onFocus = () => setFocused(i);
          if (item.kind === "pick") return <PickTile key={itemKey(item)} pick={item.pick} autoFocus={first} onFocus={onFocus} />;
          if (item.kind === "console") return <ConsoleTile key={itemKey(item)} card={item.card} logo={item.card.slug ? pack?.logos?.[item.card.slug] : undefined}
            hero={consoleHero(item.card.slug)} tag={item.tag} autoFocus={first} onFocus={onFocus} />;
          return <GameTile key={itemKey(item)} appid={item.appid} autoFocus={first} onFocus={onFocus} />;
        })}
      </Focusable>}
  </Focusable>;
}

let heroRevision = 0;
subscribeConsoleHeroes(() => { heroRevision++; });
const consoleHeroRevision = () => heroRevision;

// The Consoles page's screen two: the focused console's recently played games as a portrait row; nothing else.
export function ConsoleScreenTwo({ getLocale }: { getLocale: () => string }) {
  React.useSyncExternalStore(subscribeHomePage, focusedConsoleId, focusedConsoleId);
  const cards = useConsoleCards();
  const text = coreHomeCopy(getLocale());
  const card = cards.find(item => item.id === focusedConsoleId()) ?? cards[0];
  const games = (card?.games ?? []).slice(0, 16);
  return <div className="core-two" data-core-home-two="consoles">
    <style>{homeScreenCss}</style>
    <p className="core-two-label">{text.recentlyPlayed}</p>
    {games.length ? <Focusable className="core-two-row" flow-children="row">
      {games.map(game => <GameTile key={game.appid} appid={game.appid} onFocus={() => {}} />)}
    </Focusable> : <p className="core-two-empty">{text.empty}</p>}
  </div>;
}

class HomeScreenBoundary extends React.Component<{ fallback: any; children?: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: unknown) { console.warn("[Core Home] Steam's recent games restored", error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}

// What replaces Steam's recent games section: Core's screen one while Core UI is on and Steam's list was found,
// Steam's own row otherwise.
export function CoreHomeScreen({ native, games, autoFocus, getLocale }: { native: any; games?: number[]; autoFocus?: boolean; getLocale: () => string }) {
  const on = React.useSyncExternalStore(coreUiSetting.subscribe, coreUiSetting.get, coreUiSetting.get);
  if (!on || !games) return native;
  return <HomeScreenBoundary fallback={native}><HomeScreenOne games={games} autoFocus={autoFocus} getLocale={getLocale} /></HomeScreenBoundary>;
}
