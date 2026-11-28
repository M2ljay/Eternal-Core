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
  activeHomeTab, availableHomeTabs, composeHomeTabs, consoleShelves, homeTabsRevision, registerHomeTab,
  subscribeHomeTabs, CONSOLES_ID, HOME_TAB_ORDER, type HomeApp, type ShelfFamily,
} from "./coreHomeTabs";
import { matchCollection, matchTab, tabIcon, CORE_FAMILY_TONES, type CoreIconSet } from "./coreLibraryIcons";
import { clubHomeTab } from "./club";

const { Focusable, Navigation } = DFL as any;

// The Club tab (src/club/): registered through Home's registry like any other module's tab; shown once the Club is on
// and has data (clubAvailable), re-checked whenever the Club's store changes.
try {
  registerHomeTab({ id: clubHomeTab.id, label: clubHomeTab.title, order: HOME_TAB_ORDER.club, render: clubHomeTab.render,
    isAvailable: clubHomeTab.available, subscribe: clubHomeTab.subscribe });
} catch { /* No Club tab. */ }

let iconSet: () => CoreIconSet | undefined = () => undefined;
export function setCoreHomeIcons(source: () => CoreIconSet | undefined) { iconSet = source; }

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
@media (prefers-reduced-motion: reduce){.core-home .core-home-tile{transition:none}.core-home .core-home-tile.gpfocus{translate:none}}
`;

const steamWindow = () => window as any;
function userCollections(): any[] {
  const host = steamWindow();
  try { const list = host.collectionStore?.userCollections; return Array.isArray(list) ? list : list && typeof list.values === "function" ? Array.from(list.values()) : []; } catch { return []; }
}

// The art Steam shows for an app: its own portrait capsule URLs where the store offers them, then the local cache.
function portraitSources(appid: number): string[] {
  const host = steamWindow(), out: string[] = [];
  try {
    const overview = host.appStore?.GetAppOverviewByAppID?.(appid);
    for (const name of ["GetCustomVerticalCapsuleURLs", "GetVerticalCapsuleURLs", "GetCustomPortraitImageURLs", "GetCachedPortraitImageURLs"]) {
      const urls = host.appStore?.[name]?.(overview);
      if (Array.isArray(urls)) out.push(...urls.filter((url: unknown) => typeof url === "string"));
    }
  } catch { /* The local cache below. */ }
  out.push(`https://steamloopback.host/assets/${appid}/library_600x900.jpg`, `https://steamloopback.host/customimages/${appid}p.png`, `https://steamloopback.host/customimages/${appid}p.jpg`);
  return [...new Set(out)];
}

function GameTile({ app }: { app: HomeApp }) {
  const sources = React.useMemo(() => portraitSources(app.appid), [app.appid]);
  const [index, setIndex] = React.useState(0);
  return <Focusable className="core-home-tile" focusable={true} aria-label={app.name}
    onActivate={() => { try { Navigation?.Navigate?.(`/library/app/${app.appid}`); } catch { /* Stay on Home. */ } }}>
    {index < sources.length ? <img alt="" loading="lazy" src={sources[index]} onError={() => setIndex(i => i + 1)} /> : null}
    {index >= sources.length ? <span>{app.name}</span> : null}
  </Focusable>;
}


const familyName: Record<ShelfFamily, string> = { playstation: "PlayStation", nintendo: "Nintendo", sega: "Sega", xbox: "Xbox", retro: "Retro", vr: "VR" };
function shelfFamily(name: string): ShelfFamily | undefined {
  const found = matchCollection(name);
  if (found?.key === "vr") return "vr";
  return found?.family;
}

function Consoles({ locale }: { locale: string }) {
  const text = coreHomeCopy(locale);
  const shelves = React.useMemo(() => consoleShelves(userCollections(), shelfFamily), []);
  const icons = iconSet();
  if (!shelves.length) return <div className="core-home"><style>{css}</style><p className="core-home-note">{text.empty}</p></div>;
  return <div className="core-home"><style>{css}</style>
    {shelves.map(shelf => {
      const label = shelf.family === "vr" ? text.vr : shelf.family === "retro" ? text.retro : familyName[shelf.family];
      const icon = tabIcon(familyName[shelf.family], matchTab(familyName[shelf.family]), icons);
      const tone = shelf.family === "vr" ? "#C9D1D9" : CORE_FAMILY_TONES[shelf.family];
      return <section key={shelf.family} style={{ ["--core-tone" as any]: tone }} aria-label={label}>
        <div className="core-home-label">{icon ? <img alt="" src={icon} /> : null}<span>{label}</span><i /></div>
        {shelf.apps.length
          ? <Focusable className="core-home-row" flow-children="row">{shelf.apps.map(app => <GameTile key={app.appid} app={app} />)}</Focusable>
          : <p className="core-home-note">{text.empty}</p>}
      </section>;
    })}
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
    if ("strActiveTab" in native.props) props.strActiveTab = activeHomeTab(native.props.strActiveTab, next);
    tree = React.cloneElement(native, props);
  } catch (error) {
    console.warn("[Core Home] Steam's Home kept", error);
    return fallback;
  }
  return <HomeBoundary fallback={fallback}>{render(tree)}</HomeBoundary>;
}
