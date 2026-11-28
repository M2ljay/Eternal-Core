// Eternal Core 3.3.9: the Consoles tab's icon pack, as the plugin backend answers it (get_colorgrade_pack). One shared
// answer for the tab and Quick Access > Core > Look ("Re-import icon pack"), loaded once and on demand.

import { call } from "./controlBackend";
import type { ColorGradePack } from "./coreConsoles";

export type PackAnswer = ColorGradePack & { import?: { ok?: boolean; imported?: boolean; reason?: string; slugs?: number } };
let pack: PackAnswer | undefined;
let pending: Promise<PackAnswer | undefined> | undefined;
const listeners = new Set<() => void>();
const notify = () => { for (const listener of [...listeners]) { try { listener(); } catch { /* Others still hear it. */ } } };

export const consolePack = () => pack;
export function subscribeConsolePack(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function loadConsolePack(force = false): Promise<PackAnswer | undefined> {
  if (pending && !force) return pending;
  const run = (async () => {
    try { pack = await call<[boolean], PackAnswer>("get_colorgrade_pack", force); } catch { /* The tiles keep their names. */ }
    notify();
    return pack;
  })();
  pending = run;
  void run.finally(() => { if (pending === run) pending = undefined; });
  return run;
}
