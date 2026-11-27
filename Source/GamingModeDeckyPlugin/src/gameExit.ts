// Eternal Core 3.3.5: games are exited through Steam. This frontend runs inside Steam, so it tells the Gaming
// Mode agent what only Steam knows: a game started or stopped (SteamClient.GameSessions.
// RegisterForAppLifetimeNotifications) and the user chose Exit Game (SteamClient.Apps.TerminateApp, which Steam's
// own Exit Game in the Steam menu and Quick Access calls). The agent then closes what the launch left running
// (an emulator a wrapper started, a launcher's helper) and, when Steam lost track of a game still on screen, offers
// Core's own Exit (docs/eternal/CONSOLE-GUARDS.md, "Exiting games"). Steam's call is never changed or delayed: the
// original runs first and the report is fire-and-forget. Without Decky the agent falls back on Steam's process tree.
// Shapes from @decky/ui's community typings (GameSessions.ts, App.ts); not documented by Valve.
// Self-contained on purpose: tests/coreEmulators.test.mjs loads this file alone.

type Unregisterable = { unregister?: () => void } | null | undefined;

export type GameExitDeps = {
  steam: () => any;
  title: (appId: number) => string;
  lifetime: (appId: number, running: boolean, title: string) => unknown;
  exitChosen: () => unknown;
};

export function installGameExitWatch(deps: GameExitDeps): () => void {
  const steam = deps.steam();
  const undo: Array<() => void> = [];
  try {
    const register = steam?.GameSessions?.RegisterForAppLifetimeNotifications;
    if (typeof register === "function") {
      const handle: Unregisterable = register.call(steam.GameSessions, (event: any) => {
        const appId = Number(event?.unAppID);
        if (!Number.isInteger(appId) || appId < 0 || appId > 0xffffffff) return;
        try { void Promise.resolve(deps.lifetime(appId, event?.bRunning === true, safeTitle(deps, appId))).catch(() => {}); } catch {}
      });
      undo.push(() => { try { handle?.unregister?.(); } catch {} });
    }
  } catch {}
  try {
    const apps = steam?.Apps;
    const original = apps?.TerminateApp;
    if (typeof original === "function") {
      const patched = function (this: unknown, ...args: unknown[]) {
        const result = original.apply(this ?? apps, args);
        try { void Promise.resolve(deps.exitChosen()).catch(() => {}); } catch {}
        return result;
      };
      (patched as any).__coreGameExit = true;
      if (!(original as any).__coreGameExit) {
        apps.TerminateApp = patched;
        undo.push(() => { if (apps.TerminateApp === patched) apps.TerminateApp = original; });
      }
    }
  } catch {}
  return () => { while (undo.length) undo.pop()!(); };
}

function safeTitle(deps: GameExitDeps, appId: number): string {
  try { return String(deps.title(appId) ?? "").replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 64); } catch { return ""; }
}
