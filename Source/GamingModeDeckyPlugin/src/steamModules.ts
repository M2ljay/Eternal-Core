// Eternal Core: Steam module lookups that work on both Decky generations.
//
// Decky 3 (stable Steam, webpack UI) finds Steam's internals through webpack exports. The new
// Steam client (beta from November 2026) loads its UI as ES modules: most internals are no
// longer exports but local variables of a module, and Decky 4's @decky/ui 5 reaches them with
// findVar / findModuleScopeByVar / findScope (the approach Decky-Metadata v0.6.7 uses). The
// plugin is built against @decky/ui 4 and gets the running loader's library at run time, so
// every lookup tries the classic export search first and falls back to the scope search only
// when the loader offers it. Nothing here throws: a lookup that finds nothing returns
// undefined and the feature that needed it stays off.
//
// Pure on purpose (no imports): decky.ts wraps the loader's library with it, and
// tests/steamModules.test.mjs runs it against fake loaders of both generations.

export type SteamClientKind = "webpack" | "modules" | "unknown";

type Filter = (value: any, name?: any) => any;

function attempt<T>(run: () => T): T | undefined {
  try { return run(); } catch { return undefined; }
}

function has(ui: any, name: string): boolean {
  try { return typeof ui?.[name] === "function"; } catch { return false; }
}

// "webpack": the Steam UI Decky 3 understands. "modules": the ES-module client only Decky 4 reaches.
export function steamClientKind(host: any = typeof window === "undefined" ? undefined : window): SteamClientKind {
  try {
    if (!host || typeof host !== "object") return "unknown";
    if (host.webpackChunksteamui !== undefined && host.webpackChunksteamui !== null) return "webpack";
    const scripts = host.document?.querySelectorAll?.("script[type=module]");
    return scripts && scripts.length > 0 ? "modules" : "unknown";
  } catch { return "unknown"; }
}

export function findExport(ui: any, filter: Filter, minExports?: number): any {
  const found = has(ui, "findModuleExport") ? attempt(() => ui.findModuleExport(filter, minExports)) : undefined;
  if (found !== undefined && found !== null) return found;
  return has(ui, "findVar") ? attempt(() => ui.findVar(filter, minExports)) ?? undefined : undefined;
}

export function findModuleByExport(ui: any, filter: Filter, minExports?: number): any {
  const found = has(ui, "findModuleByExport") ? attempt(() => ui.findModuleByExport(filter, minExports)) : undefined;
  if (found !== undefined && found !== null) return found;
  return has(ui, "findModuleScopeByVar") ? attempt(() => ui.findModuleScopeByVar(filter, minExports)) ?? undefined : undefined;
}

// A whole-module predicate. On the ES-module client a "module" is either a scope or one of
// its variables (stores and class maps live in variables), so both are searched.
export function findModule(ui: any, filter: Filter): any {
  const found = has(ui, "findModule") ? attempt(() => ui.findModule(filter)) : undefined;
  if (found !== undefined && found !== null) return found;
  const scope = has(ui, "findScope") ? attempt(() => ui.findScope(filter)) : undefined;
  if (scope !== undefined && scope !== null) return scope;
  return has(ui, "findVar") ? attempt(() => ui.findVar((value: any) => filter(value))) ?? undefined : undefined;
}

// findModuleChild returns the finder's own result. The scope fallback hands the finder each
// module scope (and its default), whose keys are the module's variables.
export function findModuleChild(ui: any, find: Filter): any {
  const found = has(ui, "findModuleChild") ? attempt(() => ui.findModuleChild(find)) : undefined;
  if (found !== undefined && found !== null && found !== false) return found;
  const scopes = attempt(() => ui?.moduleScopes);
  if (!scopes || typeof scopes.values !== "function") return undefined;
  try {
    for (const scope of scopes.values()) {
      for (const candidate of [scope?.default, scope]) {
        if (!candidate) continue;
        const hit = attempt(() => find(candidate));
        if (hit) return hit;
      }
    }
  } catch { /* A scope map that cannot be walked finds nothing. */ }
  return undefined;
}

// Steam's CSS class map for a set of class names (both loaders build one; on the new client
// it is the only way to name a class, because the class strings are hashed).
export function findClassModule(ui: any, filter: (module: any) => boolean): Record<string, string> | undefined {
  const found = has(ui, "findClassModule") ? attempt(() => ui.findClassModule(filter)) : undefined;
  if (found) return found as Record<string, string>;
  const maps = attempt(() => ui?.classMap);
  if (Array.isArray(maps)) {
    const hit = maps.find(map => map && typeof map === "object" && attempt(() => filter(map)));
    if (hit) return hit;
  }
  const fromModule = findModule(ui, filter);
  return fromModule && typeof fromModule === "object" ? fromModule : undefined;
}

// Every class string whose original name contains `fragment`, the class-map equivalent of a
// [class*="fragment"] selector. Bounded: a fragment that matches too much stays attribute-only.
export function classNamesContaining(ui: any, fragment: string, limit = 160): string[] {
  const out = new Set<string>();
  const maps = attempt(() => ui?.classMap);
  if (!Array.isArray(maps) || !fragment) return [];
  try {
    for (const map of maps) {
      if (!map || typeof map !== "object") continue;
      for (const key of Object.keys(map)) {
        if (!key.includes(fragment)) continue;
        const value = map[key];
        if (typeof value !== "string") continue;
        for (const name of value.split(/\s+/)) if (/^[A-Za-z_][\w-]*$/.test(name)) out.add(name);
        if (out.size > limit) return [];
      }
    }
  } catch { return []; }
  return Array.from(out);
}

const ATTRIBUTE_SELECTOR = /\[class\*=(["'])([A-Za-z0-9_-]+)\1\]/g;

// Rewrites each [class*="Name"] in a stylesheet to :is([class*="Name"], .mapped, ...). The
// attribute selector still works on the stable client (its class strings keep readable names);
// the mapped classes reach the new client. Same specificity either way, and with no class map
// the CSS is returned unchanged.
export function withClassMapSelectors(ui: any, css: string): string {
  const cache = new Map<string, string>();
  try {
    return css.replace(ATTRIBUTE_SELECTOR, (whole: string, _quote: string, fragment: string) => {
      if (!cache.has(fragment)) {
        const names = classNamesContaining(ui, fragment).filter(name => !name.includes(fragment));
        cache.set(fragment, names.length ? `:is(${whole},${names.map(name => "." + name).join(",")})` : whole);
      }
      return cache.get(fragment)!;
    });
  } catch { return css; }
}

// Name of the extra method decky.ts's DFL carries. Call sites use it optionally,
// ((DFL as any).coreClassSelectors?.(css) ?? css), so a test's fake library (or any loader
// without the wrapper) keeps the stylesheet exactly as written.
export const CLASS_SELECTORS_METHOD = "coreClassSelectors";

export function withModuleFallbacks<T extends object>(ui: T): T {
  if (!ui || typeof ui !== "object" || typeof Proxy !== "function") return ui;
  const mapped = new Map<string, string>();
  const overrides: Record<string, Function> = {
    [CLASS_SELECTORS_METHOD]: (css: string) => {
      if (typeof css !== "string") return css;
      let out = mapped.get(css);
      if (out === undefined) { out = withClassMapSelectors(ui, css); if (mapped.size < 256) mapped.set(css, out); }
      return out;
    },
    findModuleExport: (filter: Filter, minExports?: number) => findExport(ui, filter, minExports),
    findModuleByExport: (filter: Filter, minExports?: number) => findModuleByExport(ui, filter, minExports),
    findModule: (filter: Filter) => findModule(ui, filter),
    findModuleChild: (find: Filter) => findModuleChild(ui, find),
  };
  try {
    return new Proxy(ui, {
      get(target: any, key: string | symbol, receiver: any) {
        if (typeof key === "string" && Object.prototype.hasOwnProperty.call(overrides, key)) return overrides[key];
        return Reflect.get(target, key, receiver);
      },
    });
  } catch { return ui; }
}
