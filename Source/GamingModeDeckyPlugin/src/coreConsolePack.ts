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

// 3.3.10: the Consoles page's heroes, one per call (get_colorgrade_hero; each is megabytes, never fetched in bulk), one
// at a time, kept as blob URLs (the data URL's text is dropped once decoded). The pack is the user's own: read in place
// on the PC by the backend, never stored anywhere by Core.
const heroes = new Map<string, string | null>();
const queue: string[] = [];
let heroBusy = false;
const heroListeners = new Set<() => void>();
const SLUG = /^[a-z0-9][a-z0-9_-]{0,40}$/;
export const consoleHero = (slug: string | undefined) => (slug ? heroes.get(slug) ?? undefined : undefined);
export function subscribeConsoleHeroes(listener: () => void) { heroListeners.add(listener); return () => { heroListeners.delete(listener); }; }

export function blobUrlFromDataUrl(url: string, make: (blob: Blob) => string = blob => URL.createObjectURL(blob)): string | null {
  const match = /^data:(image\/png);base64,([A-Za-z0-9+/=]+)$/.exec(url ?? "");
  if (!match) return null;
  try {
    const bytes = atob(match[2]);
    const array = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) array[i] = bytes.charCodeAt(i);
    return make(new Blob([array], { type: match[1] }));
  } catch { return null; }
}

async function pump() {
  if (heroBusy) return;
  heroBusy = true;
  try {
    while (queue.length) {
      const slug = queue.shift()!;
      if (heroes.has(slug)) continue;
      let answer: { ok?: boolean; url?: string | null } | undefined;
      try { answer = await call<[string], { ok?: boolean; url?: string | null }>("get_colorgrade_hero", slug); } catch { answer = undefined; }
      heroes.set(slug, answer?.ok && typeof answer.url === "string" ? blobUrlFromDataUrl(answer.url) : null);
      for (const listener of [...heroListeners]) { try { listener(); } catch { /* Others still hear it. */ } }
    }
  } finally { heroBusy = false; }
}

// Asks for these heroes, first ones first; ones already known or queued are not asked again.
export function wantConsoleHeroes(slugs: Array<string | undefined>) {
  for (const slug of slugs) if (slug && SLUG.test(slug) && !heroes.has(slug) && !queue.includes(slug)) queue.push(slug);
  void pump();
}
