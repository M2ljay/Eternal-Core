// Eternal Core Mark III: Decky health heartbeat and the Core Repair tile.
//
// Heartbeat: this frontend runs inside Steam, its backend inside Decky. Every 30 s the
// frontend asks the backend to tell the Gaming Mode agent "plugins are loaded". When the
// beats stop (or never start) after Big Picture is up, the agent shows a calm notice and
// points to Core Repair. Decky stays an extra: nothing here can affect Steam.
//
// Core Repair tile: a non-Steam shortcut to "GamingMode.exe repair", added once while
// Decky works, so it is still in the library on the day Decky does not load.
// Self-contained on purpose: the tests load this file alone.

export type HeartbeatDeps = {
  beat: () => Promise<unknown>;
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
  interval?: number;
};

export function installCoreHeartbeat(deps: HeartbeatDeps): () => void {
  let disposed = false, timer: unknown, busy = false;
  const interval = deps.interval ?? 30000;
  function tick() {
    if (disposed) return;
    if (!busy) {
      busy = true;
      let pending: Promise<unknown>;
      try { pending = Promise.resolve(deps.beat()); } catch { pending = Promise.resolve(); }
      pending.catch(() => { /* The agent may be restarting; the next beat retries. */ }).finally(() => { busy = false; });
    }
    timer = deps.schedule(tick, interval);
  }
  tick();
  return () => { disposed = true; deps.cancel(timer); };
}

export type CoreRepairShortcut = { name?: unknown; exe?: unknown; startDir?: unknown; launchOptions?: unknown };
type ShortcutApps = Record<string, ((...args: any[]) => unknown) | undefined>;
type Overview = { display_name?: string; app_type?: number; appid?: number } | null | undefined;
export type TileDeps = {
  info: () => Promise<CoreRepairShortcut | null | undefined>;
  apps: () => ShortcutApps | undefined;
  overview: (appId: number) => Overview;
  shortcuts: () => readonly Overview[];
  read: () => string | null;
  write: (value: string) => void;
  sleep: (ms: number) => Promise<void>;
};
export type TileResult = "present" | "created" | "unavailable" | "failed";

const SHORTCUT_TYPE = 1073741824;
const SETTERS = ["SetShortcutName", "SetShortcutExe", "SetShortcutStartDir", "SetShortcutLaunchOptions"] as const;

export async function ensureCoreRepairTile(deps: TileDeps): Promise<TileResult> {
  try {
    const info = await deps.info();
    const name = info?.name, exe = info?.exe, startDir = info?.startDir, launch = info?.launchOptions;
    if (name !== "Core Repair" || typeof exe !== "string" || !/^[A-Za-z]:\\.+\.exe$/i.test(exe)
      || typeof startDir !== "string" || launch !== "repair") return "unavailable";
    let remembered: number | undefined;
    try { remembered = Number(deps.read() ?? ""); } catch { remembered = undefined; }
    if (remembered && Number.isInteger(remembered) && deps.overview(remembered)?.display_name === name) return "present";
    let list: readonly Overview[] = [];
    try { list = deps.shortcuts() ?? []; } catch { list = []; }
    const existing = list.find(app => app?.display_name === name && app?.app_type === SHORTCUT_TYPE && Number.isInteger(app?.appid));
    if (existing) { try { deps.write(String(existing.appid)); } catch { /* Memory is optional. */ } return "present"; }
    const apps = deps.apps();
    if (!apps || typeof apps.AddShortcut !== "function" || SETTERS.some(method => typeof apps[method] !== "function")) return "unavailable";
    const created = await Promise.resolve(apps.AddShortcut(name, exe, startDir, launch));
    if (!Number.isInteger(created) || (created as number) <= 0 || (created as number) > 0xffffffff) return "failed";
    const appId = created as number;
    // Steam ignores most AddShortcut arguments until the overview exists; then set each.
    for (let attempt = 0; attempt < 40 && !deps.overview(appId); attempt++) await deps.sleep(250);
    const values: Record<(typeof SETTERS)[number], string> = {
      SetShortcutName: name, SetShortcutExe: exe, SetShortcutStartDir: startDir, SetShortcutLaunchOptions: launch,
    };
    for (const method of SETTERS) {
      await Promise.resolve(apps[method]!(appId, values[method]));
      await deps.sleep(50);
    }
    try { deps.write(String(appId)); } catch { /* Memory is optional. */ }
    return "created";
  } catch {
    return "failed";
  }
}
