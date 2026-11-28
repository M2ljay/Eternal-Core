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
  subscribeHomeTabs, CONSOLES_ID, FAVORITES_ID, HOME_TAB_ORDER, favoriteApps, favoritesCollection, type HomeApp,
} from "./coreHomeTabs";
import { CORE_FAMILY_TONES, type CoreIconSet } from "./coreLibraryIcons";
import { collectionRoute, consoleTiles, CONSOLE_TILE } from "./coreConsoles";

// 3.3.9: Favorites as big portrait art, six across at the TV's 1348 px (about 200 x 300 px each), 16px apart.
export const FAVORITE_TILE = { across: 6, gap: 16 } as const;
import { consolePack, loadConsolePack, subscribeConsolePack } from "./coreConsolePack";
import { clubHomeTab } from "./club";

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
export function setCoreHomeIcons(source: () => CoreIconSet | undefined) { iconSet = source; }
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
.core-home .core-favorites{display:grid;grid-template-columns:repeat(${FAVORITE_TILE.across},minmax(0,1fr));gap:calc(${FAVORITE_TILE.gap} / 1348 * 100vw);padding:8px 2px 12px}
.core-home .core-favorite{position:relative;aspect-ratio:2/3;background:#1A1D21;border-radius:var(--core-radius-float,10px);overflow:hidden;display:flex;align-items:flex-end}
.core-home .core-favorite img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block}
.core-home .core-favorite span{padding:10px;font-size:15px;font-weight:600;color:#FFFFFF}
.core-home .core-favorite.gpfocus,.core-home .core-favorite:focus-visible{outline:3px solid #D7263D;outline-offset:-3px}
.core-home .core-home-empty{display:flex;align-items:center;gap:12px;font-size:18px;color:#C9D1D9;padding:24px 2px}
.core-home .core-home-prompt{display:inline-flex;align-items:center;justify-content:center;width:30px;height:30px;border:2px solid #C9D1D9;border-radius:50%;font-size:15px;font-weight:700}
.core-home .core-consoles{display:grid;grid-template-columns:repeat(${CONSOLE_TILE.across},minmax(0,1fr));gap:calc(${CONSOLE_TILE.gap} / 1920 * 100vw);padding:8px 2px 12px}
.core-home .core-console{position:relative;aspect-ratio:${CONSOLE_TILE.width}/${CONSOLE_TILE.height};background:#1A1D21;border-radius:var(--core-radius-float,10px);overflow:hidden;display:flex;align-items:center;justify-content:center;box-shadow:inset 0 -${CONSOLE_TILE.toneBar}px 0 var(--core-tone,#C9D1D9)}
.core-home .core-console img{max-width:78%;max-height:52%;object-fit:contain;display:block}
.core-home .core-console span{padding:0 8px;font-size:15px;font-weight:700;color:#FFFFFF;text-align:center;line-height:1.2}
.core-home .core-console.gpfocus,.core-home .core-console:focus-visible{outline:3px solid #D7263D;outline-offset:-3px}
@media (prefers-reduced-motion: reduce){.core-home .core-home-tile{transition:none}.core-home .core-home-tile.gpfocus{translate:none}}
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
function userCollections(): any[] {
  const host = steamWindow();
  try { const list = host.collectionStore?.userCollections; return Array.isArray(list) ? list : list && typeof list.values === "function" ? Array.from(list.values()) : []; } catch { return []; }
}

// 3.3.9: the Favorites tab (first, the default): Steam's own Favorites collection, read from the collection store on
// every render (never a copy), most recently played first, as big portrait art in a grid: each game's library capsule,
// or for a ROM shortcut its portrait grid image (where The Collector publishes its case art). No counts, no text, no
// clutter; the crimson ring sits inside the tile. A opens the game view. (Y to add or remove a favourite is left out:
// Steam offers no documented, simple call for it, and Steam's own Options menu already does it.)
function portraitSources(appid: number): string[] {
  const host = steamWindow(), out: string[] = [];
  try {
    const overview = host.appStore?.GetAppOverviewByAppID?.(appid);
    for (const name of ["GetCustomVerticalCapsuleURLs", "GetVerticalCapsuleURLs", "GetCustomPortraitImageURLs", "GetCachedPortraitImageURLs"]) {
      const urls = host.appStore?.[name]?.(overview);
      if (Array.isArray(urls)) out.push(...urls.filter((url: unknown) => typeof url === "string"));
    }
  } catch { /* The local cache below. */ }
  out.push(`https://steamloopback.host/customimages/${appid}p.png`, `https://steamloopback.host/customimages/${appid}p.jpg`, `https://steamloopback.host/assets/${appid}/library_600x900.jpg`);
  return [...new Set(out)];
}

function FavoriteTile({ app }: { app: HomeApp }) {
  const sources = React.useMemo(() => portraitSources(app.appid), [app.appid]);
  const [index, setIndex] = React.useState(0);
  const open = () => { try { Navigation?.Navigate?.(`/library/app/${app.appid}`); } catch { /* Stay on Home. */ } };
  return <Focusable className="core-favorite" focusable={true} aria-label={app.name} onActivate={open} onClick={open}>
    {index < sources.length ? <img alt="" loading="lazy" src={sources[index]} onError={() => setIndex(i => i + 1)} /> : <span>{app.name}</span>}
  </Focusable>;
}

function Favorites({ locale }: { locale: string }) {
  const text = coreHomeCopy(locale);
  const apps = favoriteApps(favoritesCollection(steamWindow().collectionStore));
  if (!apps.length) return <div className="core-home"><style>{css}</style>
    <p className="core-home-empty"><span className="core-home-prompt" aria-hidden="true">≡</span>{text.favoritesEmpty}</p></div>;
  return <div className="core-home"><style>{css}</style>
    <Focusable className="core-favorites" flow-children="grid">{apps.map(app => <FavoriteTile key={app.appid} app={app} />)}</Focusable>
  </div>;
}

// 3.3.9 (board 1): every console in the library as one tile, eight across (scaled to the view), by family. The tile is
// the console's white logo from the user's ColorGrade pack on gunmetal with the family's tone bar: at TV size the logo
// reads at a glance where the pack's dimmed hero art turns to noise (and each hero is megabytes; the logos are a few
// tens of kilobytes). Without a pack logo, the console's name. A opens that console's collection.
function Consoles({ locale }: { locale: string }) {
  const text = coreHomeCopy(locale);
  const pack = React.useSyncExternalStore(subscribeConsolePack, consolePack, consolePack);
  React.useEffect(() => { if (!consolePack()) void loadConsolePack(false); }, []);
  const slugs = React.useMemo(() => new Set(pack?.slugs ?? []), [pack]);
  const tiles = React.useMemo(() => consoleTiles(userCollections(), slugs), [slugs]);
  if (!tiles.length) return <div className="core-home"><style>{css}</style><p className="core-home-note">{text.empty}</p></div>;
  return <div className="core-home"><style>{css}</style>
    <Focusable className="core-consoles" flow-children="grid">
      {tiles.map(tile => {
        const logo = tile.slug ? pack?.logos?.[tile.slug] : undefined;
        return <Focusable key={tile.id} className="core-console" focusable={true} aria-label={tile.name}
          style={{ ["--core-tone" as any]: CORE_FAMILY_TONES[tile.family] }}
          onActivate={() => { try { Navigation?.Navigate?.(collectionRoute(tile.id)); } catch { /* Stay on Home. */ } }}
          onClick={() => { try { Navigation?.Navigate?.(collectionRoute(tile.id)); } catch { /* Stay on Home. */ } }}>
          {logo ? <img alt="" src={logo} /> : <span>{tile.name}</span>}
        </Focusable>;
      })}
    </Focusable>
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
  const fallback = render(native);
  if (!on) return fallback;
  let tree: any;
  try {
    const locale = getLocale();
    const text = coreHomeCopy(locale);
    const tabs: any[] = native.props.tabs;
    const core: any[] = [
      { id: FAVORITES_ID, title: text.favorites, content: <Favorites locale={locale} />, order: HOME_TAB_ORDER.favorites },
      { id: CONSOLES_ID, title: text.consoles, content: <Consoles locale={locale} />, order: HOME_TAB_ORDER.consoles },
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
