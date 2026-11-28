// Eternal Core 3.3.10: where a Favorites tile's art comes from (docs/eternal/CORE-UI.md, "Favorites"). TV: Street
// Fighter 6 was an empty gunmetal box, because every portrait URL the tile tried failed. Now the tile tries Steam's own
// portrait first (what the Library draws, GetVerticalCapsuleURLForApp included), and when no portrait loads it draws the
// game's hero art with its logo on top, else the hero with the name, else the name over a gunmetal-to-black gradient:
// never an empty box.
//
// Pure: tests hand it a fake appStore.

export type FavoriteArt = { portrait: string[]; hero: string[]; logo: string[] };

const LOOPBACK = "https://steamloopback.host";

function urls(value: unknown): string[] {
  if (typeof value === "string") return value ? [value] : [];
  return Array.isArray(value) ? value.filter((url): url is string => typeof url === "string" && url.length > 0) : [];
}

function ask(store: any, names: string[], overview: unknown, appid: number): string[] {
  const out: string[] = [];
  for (const name of names) {
    try { if (typeof store?.[name] === "function") out.push(...urls(store[name](overview ?? appid))); } catch { /* Next source. */ }
  }
  return out;
}

export function favoriteArt(appid: number, store: any): FavoriteArt {
  let overview: unknown;
  try { overview = store?.GetAppOverviewByAppID?.(appid); } catch { overview = undefined; }
  const id = Number.isInteger(appid) && appid > 0 ? appid : 0;
  const portrait = [
    ...ask(store, ["GetCustomVerticalCapsuleURLs", "GetVerticalCapsuleURLs", "GetCustomPortraitImageURLs", "GetCachedPortraitImageURLs"], overview, id),
    ...ask(store, ["GetVerticalCapsuleURLForApp"], overview, id),
    ...(id ? [`${LOOPBACK}/customimages/${id}p.png`, `${LOOPBACK}/customimages/${id}p.jpg`, `${LOOPBACK}/assets/${id}/library_600x900.jpg`] : []),
  ];
  const hero = [
    ...ask(store, ["GetCustomHeroImageURLs", "GetHeroImageURLs", "GetCachedHeroImageURLs"], overview, id),
    ...ask(store, ["GetHeroImageURLForApp"], overview, id),
    ...(id ? [`${LOOPBACK}/customimages/${id}_hero.png`, `${LOOPBACK}/customimages/${id}_hero.jpg`, `${LOOPBACK}/assets/${id}/library_hero.jpg`] : []),
  ];
  const logo = [
    ...ask(store, ["GetCustomLogoImageURLs", "GetLogoImageURLs", "GetCachedLogoImageURLs"], overview, id),
    ...ask(store, ["GetLogoImageURLForApp"], overview, id),
    ...(id ? [`${LOOPBACK}/customimages/${id}_logo.png`, `${LOOPBACK}/assets/${id}/logo.png`] : []),
  ];
  return { portrait: [...new Set(portrait)], hero: [...new Set(hero)], logo: [...new Set(logo)] };
}

// What the tile draws once some sources have failed: the first portrait still untried, else the fallback layers.
export type FavoriteLayers = { kind: "portrait"; src: string } | { kind: "fallback"; hero?: string; logo?: string };
export function favoriteLayers(art: FavoriteArt, failed: { portrait: number; hero: number; logo: number }): FavoriteLayers {
  if (failed.portrait < art.portrait.length) return { kind: "portrait", src: art.portrait[failed.portrait] };
  return { kind: "fallback", hero: art.hero[failed.hero], logo: art.logo[failed.logo] };
}
