// Eternal Core 3.3.11: Steam's own game context menu for Core's tiles (Home's row, Favorites, the Achievements rows,
// the Consoles row's club picks), the same menu a Library capsule opens with the Options button. Steam's menu component
// is found the way losslessProfiles.tsx finds it to add its entry (the module that names "().LibraryContextMenu"; the
// export in it that hands Steam's navigator to the menu), and shown with DFL's showContextMenu for the app's overview.
// Nothing found (another Steam build), no overview: Options does nothing, as before.

import { SP_REACT as React, DFL } from "./decky";

type Finder = (filter: (exported: any) => boolean) => any;
let menuComponent: any, looked = false;

// The menu component, looked up once. `find` and `values` are handed in by the tests.
export function steamGameMenu(find: Finder | undefined = (DFL as any)?.findModuleByExport, refresh = false): any {
  if (looked && !refresh) return menuComponent;
  looked = true;
  menuComponent = undefined;
  if (typeof find !== "function") return undefined;
  try {
    const module = find((exported: any) => { try { return !!exported?.toString && String(exported.toString()).includes("().LibraryContextMenu"); } catch { return false; } });
    menuComponent = Object.values(module ?? {}).find((value: any) => { try { return typeof value === "function" && String(value.toString()).includes("navigator:"); } catch { return false; } });
  } catch { menuComponent = undefined; }
  return menuComponent;
}

// The element Steam's menu is shown with: its overview (what the Library hands it), for one app.
export function gameMenuElement(menu: any, overview: any): any {
  if (!menu || !overview || !(Number(overview.appid) > 0)) return undefined;
  return React.createElement(menu, { overview, appid: overview.appid });
}

export function openGameMenu(appid: number | undefined, event?: any): boolean {
  if (!(Number(appid) > 0)) return false;
  let overview: any;
  try { overview = (window as any).appStore?.GetAppOverviewByAppID?.(Number(appid)); } catch { overview = undefined; }
  const element = gameMenuElement(steamGameMenu(), overview);
  const show = (DFL as any)?.showContextMenu;
  if (!element || typeof show !== "function") return false;
  try {
    event?.preventDefault?.(); event?.stopPropagation?.();
    show(element, event?.currentTarget ?? event?.target ?? window);
    return true;
  } catch (error) {
    console.warn("[Core] Steam's game menu could not open", error);
    return false;
  }
}

// The props a Core game tile spreads on its Focusable: Options opens the menu.
export function gameMenuProps(appid: number | undefined): Record<string, unknown> {
  if (!(Number(appid) > 0)) return {};
  return { onMenuButton: (event: any) => { openGameMenu(appid, event); }, onMenuActionDescription: "Options" };
}
