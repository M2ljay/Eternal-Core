// Eternal Core: Core Home, Core UI phase 2 (docs/eternal/CORE-UI.md, "Phase 2: Home"). Extends how Core's plugin already
// renders Home (HomeNews.tsx patches /library/home): the bottom tabs become Consoles (the default) and any registered
// tab (Club); Steam's What's new, Recommended and Friends go. 3.3.8: the Jarvis brief and On this day tabs are gone
// (the user's call). Jarvis stays in the top bar's orb, his Quick Access tab and the listening pill.
//
// Fail soft: everything renders inside an error boundary whose fallback is Steam's own tab list (with the news), and
// Core UI off gives Steam's tabs back at once.

import { SP_REACT as React, DFL } from "./decky";
import { coreUiSetting } from "./coreUiSetting";
import { coreHomeCopy } from "./coreHomeLocale";
import {
  activeHomeTab, availableHomeTabs, composeHomeTabs, homeTabsRevision, registerHomeTab,
  subscribeHomeTabs, ACHIEVEMENTS_ID, FAVORITES_ID, HOME_TAB_ORDER, favoriteApps, favoritesCollection, collectorFavorites, type HomeApp,
} from "./coreHomeTabs";
import { type CoreIconSet } from "./coreLibraryIcons";
import { CoreAchievementsTab } from "./CoreAchievementsTab";
import { ConsoleScreenTwo, userCollections } from "./coreHomeScreen";
import { homePage, setHomeIconSource, subscribeHomePage } from "./coreHomePages";

// 3.3.9: Favorites as big portrait art. 3.3.10 (the approved board): eight across, 20px apart at 1920 px (206 x 309), so
// about 145 x 217 px at the TV's 1348 px.
export const FAVORITE_TILE = { across: 8, gap: 20 } as const;
import { clubHomeTab } from "./club";
import { favoriteArt, favoriteLayers } from "./coreFavoriteArt";

const { Focusable, Navigation } = DFL as any;

// The Club tab (src/club/): registered through Home's registry like any other module's tab; shown once the Club is on
// and has data (clubAvailable), re-checked whenever the Club's store changes.
try {
  registerHomeTab({ id: clubHomeTab.id, label: clubHomeTab.title, order: HOME_TAB_ORDER.club, render: clubHomeTab.render,
    isAvailable: clubHomeTab.available, subscribe: clubHomeTab.subscribe });
} catch { /* No Club tab. */ }

// The user's Library icon set (index.tsx hands it in). 3.3.9: the Consoles tiles take the ColorGrade pack instead;
// kept for the next Home pieces.
let iconSet: () => CoreIconSet | undefined = () => undefined;
export function setCoreHomeIcons(source: () => CoreIconSet | undefined) { iconSet = source; setHomeIconSource(source); }
export const coreHomeIcons = () => iconSet();

const css = `
.core-home{padding:8px 0 32px;color:#E6EAEE;min-width:0}
.core-home h2{margin:0 0 6px;font-size:30px;font-weight:600;letter-spacing:-.01em;color:#FFFFFF}
.core-home .core-home-line{font-size:16px;color:#C9D1D9;margin:0 0 18px}
.core-home .core-home-label{display:flex;align-items:center;gap:10px;font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:#C9D1D9;margin:22px 0 12px}
.core-home .core-home-label img{width:26px;height:26px;object-fit:contain}
.core-home .core-home-label i{display:inline-block;width:24px;height:3px;background:var(--core-tone,#C9D1D9)}
.core-home .core-home-row{display:flex;gap:14px;overflow-x:auto;overflow-y:visible;padding:8px 2px 10px;scrollbar-width:none}
.core-home .core-home-tile{flex:0 0 120px;height:180px;position:relative;background:#1A1D21;border-radius:8px;overflow:hidden;transition:translate .15s ease-out}
.core-home .core-home-tile img{width:100%;height:100%;object-fit:cover;display:block}
.core-home .core-home-tile span{position:absolute;inset:auto 0 0 0;padding:8px;font-size:13px;color:#E6EAEE;background:#000000}
.core-home .core-home-tile.gpfocus,.core-home .core-home-tile:focus-visible{outline:3px solid #D7263D;outline-offset:-3px}
.core-home .core-home-actions{display:flex;gap:12px;flex-wrap:wrap}
.core-home .core-home-actions .DialogButton{width:auto;min-width:200px;border-radius:0}
.core-home .core-home-note{font-size:14px;color:#9AA4AE}
.core-home .core-favorites{display:grid;grid-template-columns:repeat(${FAVORITE_TILE.across},minmax(0,1fr));gap:calc(${FAVORITE_TILE.gap} / 1920 * 100vw);padding:8px 2px 12px}
.core-home .core-favorite{position:relative;aspect-ratio:2/3;background:linear-gradient(180deg,#1A1D21 0%,#000000 100%);border-radius:var(--core-radius-float,10px);overflow:hidden;display:flex;align-items:flex-end;transition:translate .15s ease-out}
.core-home .core-favorite img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block}
.core-home .core-favorite .core-favorite-hero{filter:brightness(.7)}
.core-home .core-favorite .core-favorite-logo{inset:auto 10% 0 10%;top:0;bottom:0;margin:auto;width:80%;height:40%;object-fit:contain}
.core-home .core-favorite span{position:relative;padding:12px;font-size:17px;font-weight:700;line-height:1.25;color:#FFFFFF;text-shadow:0 1px 3px rgba(0,0,0,.65)}
.core-home .core-favorite.gpfocus,.core-home .core-favorite:focus-visible{outline:none;translate:0 -6px}
.core-home .core-home-empty{font-size:15px;color:#9AA4AE;padding:8px 2px;text-align:left;outline:none;background:transparent}
.core-home .core-home-prompt{display:inline-flex;align-items:center;justify-content:center;width:30px;height:30px;border:2px solid #C9D1D9;border-radius:50%;font-size:15px;font-weight:700}
@media (prefers-reduced-motion: reduce){.core-home .core-home-tile,.core-home .core-favorite{transition:none}.core-home .core-home-tile.gpfocus{translate:none}}
`;

const steamWindow = () => window as any;

// 3.3.9: the first Home render after the update opens Favorites (the new default) once, whatever tab Steam remembers.
const FAVORITES_MIGRATED = "core.home.favoritesDefault";
let migrationAnswered = false;
function favoritesMigration(): boolean {
  if (migrationAnswered) return false;
  migrationAnswered = true;
  try {
    if (window.localStorage?.getItem(FAVORITES_MIGRATED) === "1") return false;
    window.localStorage?.setItem(FAVORITES_MIGRATED, "1");
    return true;
  } catch { return false; }
}

// 3.3.9: the Favorites tab (first, the default): Steam's own Favorites collection, read from the collection store on
// every render (never a copy), most recently played first, as big portrait art in a grid. No counts, no clutter. A opens
// the game view. (Y to add or remove a favourite is left out: Steam offers no documented, simple call for it, and
// Steam's own Options menu already does it.) 3.3.10: no focus ring (TV: rings sat offset from the art); the focused tile
// lifts 6px. A tile whose portrait never loads draws the game's hero with its logo (coreFavoriteArt.ts), else its name.
function FavoriteTile({ app }: { app: HomeApp }) {
  const art = React.useMemo(() => favoriteArt(app.appid, steamWindow().appStore), [app.appid]);
  const [failed, setFailed] = React.useState({ portrait: 0, hero: 0, logo: 0 });
  const fail = (kind: "portrait" | "hero" | "logo") => () => setFailed(prior => ({ ...prior, [kind]: prior[kind] + 1 }));
  const layers = favoriteLayers(art, failed);
  const open = () => { try { Navigation?.Navigate?.(`/library/app/${app.appid}`); } catch { /* Stay on Home. */ } };
  return <Focusable className="core-favorite" focusable={true} aria-label={app.name} onActivate={open} onClick={open}>
    {layers.kind === "portrait"
      ? <img key={layers.src} alt="" loading="lazy" src={layers.src} onError={fail("portrait")} />
      : <>
        {layers.hero && <img key={layers.hero} className="core-favorite-hero" alt="" loading="lazy" src={layers.hero} onError={fail("hero")} />}
        {layers.logo ? <img key={layers.logo} className="core-favorite-logo" alt="" loading="lazy" src={layers.logo} onError={fail("logo")} /> : <span>{app.name}</span>}
      </>}
  </Focusable>;
}

function Favorites({ locale }: { locale: string }) {
  const text = coreHomeCopy(locale);
  // 3.3.10: Steam's Favorites and The Collector's Favourites collection in one grid (each game once).
  const apps = favoriteApps(favoritesCollection(steamWindow().collectionStore), 200, collectorFavorites(userCollections()));
  // 3.3.10: empty, one line of hint text where the grid would start.
  if (!apps.length) return <div className="core-home"><style>{css}</style>
    <Focusable className="core-home-empty" focusable={true}>{text.favoritesEmpty}</Focusable></div>;
  return <div className="core-home"><style>{css}</style>
    <Focusable className="core-favorites" flow-children="grid">{apps.map(app => <FavoriteTile key={app.appid} app={app} />)}</Focusable>
  </div>;
}

class HomeBoundary extends React.Component<{ fallback: any; children?: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: unknown) { console.warn("[Core Home] Steam's Home restored", error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}

// `native` is Steam's tab container (props.tabs), with What's new already carrying Core's news; `render` draws the
// final tree. Answers Core Home, or what Core rendered before on any failure.
export function CoreHomeTabs({ native, getLocale, render }: { native: any; getLocale: () => string; render: (tree: any) => any }) {
  const on = React.useSyncExternalStore(coreUiSetting.subscribe, coreUiSetting.get, coreUiSetting.get);
  React.useSyncExternalStore(subscribeHomeTabs, homeTabsRevision, homeTabsRevision);
  const page = React.useSyncExternalStore(subscribeHomePage, homePage, homePage);
  const fallback = render(native);
  if (!on) return fallback;
  // 3.3.10: on the Consoles page, screen two is the focused console's recent games, in place of the tabs.
  if (page === "consoles") return <HomeBoundary fallback={fallback}><ConsoleScreenTwo getLocale={getLocale} /></HomeBoundary>;
  let tree: any;
  try {
    const locale = getLocale();
    const text = coreHomeCopy(locale);
    const tabs: any[] = native.props.tabs;
    const core: any[] = [
      { id: FAVORITES_ID, title: text.favorites, content: <Favorites locale={locale} />, order: HOME_TAB_ORDER.favorites },
      { id: ACHIEVEMENTS_ID, title: text.achievements, content: <CoreAchievementsTab locale={locale} />, order: HOME_TAB_ORDER.achievements },
      ...availableHomeTabs().map(tab => {
        const Component = tab.component as any;
        const content = tab.render ? tab.render({ locale, getLocale }) : Component ? <Component locale={locale} getLocale={getLocale} /> : null;
        return { id: tab.id, title: typeof tab.label === "function" ? tab.label(locale) : tab.label, content, order: tab.order };
      }),
    ];
    // Each tab's content carries data-core-home-tab (display: contents, so no box), the Core UI stylesheet's scope for
    // Home's tab row ("home-tabs" in coreUiStyles.ts): the Library's and a game page's tab bars stay out of it.
    const next = composeHomeTabs(tabs, core).map(tab => ({ ...tab, content: <div data-core-home-tab={String(tab.id)} style={{ display: "contents" }}>{tab.content}</div> }));
    const props: any = { tabs: next };
    if ("strActiveTab" in native.props) props.strActiveTab = activeHomeTab(native.props.strActiveTab, next, favoritesMigration());
    tree = React.cloneElement(native, props);
  } catch (error) {
    console.warn("[Core Home] Steam's Home kept", error);
    return fallback;
  }
  return <HomeBoundary fallback={fallback}>{render(tree)}</HomeBoundary>;
}
