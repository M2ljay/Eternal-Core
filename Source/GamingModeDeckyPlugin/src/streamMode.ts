// Eternal Core: Stream mode, Steam's half. The Gaming Mode agent cannot see Steam Remote Play (Steam
// Link, a Steam Frame or another Steam client streaming from this PC) from outside Steam, so this
// frontend, which runs inside Steam, watches SteamClient.RemotePlay and tells the agent through the
// backend (core_stream_report -> POST /stream/steam) on every change and every 30 s. The agent forgets
// a report after 90 s, so a plugin that stops loading cannot leave the TV held.
//
// What Steam exposes is not documented by Valve. The shape used here comes from the community typings
// in @decky/ui (globals/steam-client/RemotePlay.ts): RegisterForDevicesChanges hands over the paired
// devices, each with clientName, status and unStreamingSessionID, which is non-zero while that device
// streams from this PC. Remote Play Together guests are not in that list, so a friend joining a game
// that is played on the TV does not turn Stream mode on. To be confirmed on the PC (docs/eternal/CORE-STREAM-MODE.md).
// Self-contained on purpose: tests/coreStreamMode.test.mjs loads this file alone.

export type RemotePlayDevice = { clientId?: unknown; clientName?: unknown; status?: unknown; formFactor?: unknown; unStreamingSessionID?: unknown };
export type SteamStreamReport = { active: boolean; device: string };

function cleanName(value: unknown): string {
  if (typeof value !== "string") return "";
  return Array.from(value).filter(character => character >= " " && character !== "\u007f").join("").trim().slice(0, 64);
}

function sessionOf(device: RemotePlayDevice | null | undefined): number {
  const session = Number(device?.unStreamingSessionID);
  return Number.isInteger(session) && session > 0 ? session : 0;
}

export function steamStreamReport(devices: readonly (RemotePlayDevice | null | undefined)[] | null | undefined): SteamStreamReport {
  const list = Array.isArray(devices) ? devices : [];
  const streaming = list.find(device => sessionOf(device) > 0);
  return streaming ? { active: true, device: cleanName(streaming.clientName) } : { active: false, device: "" };
}

type Unregisterable = { unregister?: () => void } | null | undefined;
export type StreamWatchDeps = {
  remotePlay: () => any;
  report: (report: SteamStreamReport) => Promise<unknown> | unknown;
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
  interval?: number;
};

export function installSteamStreamWatch(deps: StreamWatchDeps): () => void {
  let disposed = false, timer: unknown, devices: RemotePlayDevice[] = [], registration: Unregisterable, last = "";
  const interval = deps.interval ?? 30000;
  function send(force: boolean) {
    if (disposed) return;
    const report = steamStreamReport(devices);
    const key = `${report.active}|${report.device}`;
    if (!force && key === last) return;
    last = key;
    try { Promise.resolve(deps.report(report)).catch(() => { /* The agent may be restarting; the next report retries. */ }); }
    catch { /* Same. */ }
  }
  try {
    const remote = deps.remotePlay();
    if (typeof remote?.RegisterForDevicesChanges === "function") {
      registration = remote.RegisterForDevicesChanges((list: unknown) => {
        devices = Array.isArray(list) ? list as RemotePlayDevice[] : [];
        send(false);
      });
    }
  } catch { registration = undefined; }
  function tick() {
    if (disposed) return;
    send(true);
    timer = deps.schedule(tick, interval);
  }
  tick();
  return () => {
    disposed = true;
    deps.cancel(timer);
    try { registration?.unregister?.(); } catch { /* Steam may already be gone. */ }
  };
}
