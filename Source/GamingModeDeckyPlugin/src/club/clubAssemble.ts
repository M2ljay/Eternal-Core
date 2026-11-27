// Eternal Core: logging a Club completion through Eternal-Assemble (the Discord plugin).
//
// Assemble publishes a bridge on window when it loads:
//   window.__eternalAssemble = { version: 1, openClubCompletion(req) }  and fires "eternal_assemble_ready".
// openClubCompletion opens the pick's Discord thread (or the club channel) in Assemble's Discord view with
// /completed ready. The user picks the options and sends it themselves: nothing is ever sent from Core or
// automatically. Without Assemble the Club tab shows the manual steps instead.
// Self-contained (no imports): tests/club.test.mjs loads it alone.

// The club's completion command (playretrogamesclub.com/rules/, checked 2026-11-27):
// /completed [event] [ra_username] [screenshot], run in the game's thread on the club's Discord.
export const CLUB_COMPLETION_COMMAND = "/completed";
export const CLUB_COMPLETION_OPTIONS = ["event", "ra_username", "screenshot"] as const;

export const ASSEMBLE_READY_EVENT = "eternal_assemble_ready";

export type AssembleEvent = "gotw" | "gotm" | "rhotm" | "gots" | "special";
export type ClubCompletionRequest = { event: AssembleEvent; title: string; raUsername?: string };
export type ClubCompletionResult =
  | { success: true; opened: "thread" | "channel" }
  | { success: false; reason: string; message: string };
export type AssembleBridge = { version: number; openClubCompletion(request: ClubCompletionRequest): Promise<unknown> };

const EVENTS: Record<string, AssembleEvent> = { week: "gotw", month: "gotm", romhack: "rhotm", season: "gots", special: "special" };
const CODES = new Set<string>(Object.values(EVENTS));

type BridgeHost = { __eternalAssemble?: unknown; addEventListener?: Function; removeEventListener?: Function };
const host = (): BridgeHost | undefined => (typeof window === "undefined" ? undefined : (window as unknown as BridgeHost));

// Assemble's event code for a pick: its own `event` when the feed has one, else from its kind.
export function assembleEventFor(pick: { kind?: string; event?: string } | null | undefined): AssembleEvent | null {
  if (pick?.event && CODES.has(pick.event)) return pick.event as AssembleEvent;
  return (pick?.kind && EVENTS[pick.kind]) || null;
}

// The bridge when Assemble has published a version-1-compatible one, else null. Never throws.
export function assembleBridge(target: BridgeHost | undefined = host()): AssembleBridge | null {
  try {
    const bridge = target?.__eternalAssemble as AssembleBridge | undefined;
    if (!bridge || typeof bridge !== "object") return null;
    if (typeof bridge.version !== "number" || bridge.version < 1) return null;
    return typeof bridge.openClubCompletion === "function" ? bridge : null;
  } catch {
    return null;
  }
}

// Calls back when Assemble (re)loads and republishes its bridge. Returns the unsubscribe function.
export function subscribeAssemble(listener: () => void, target: BridgeHost | undefined = host()): () => void {
  if (!target?.addEventListener) return () => {};
  const handler = () => listener();
  target.addEventListener(ASSEMBLE_READY_EVENT, handler);
  return () => target.removeEventListener?.(ASSEMBLE_READY_EVENT, handler);
}

const failure = (reason: string, message = ""): ClubCompletionResult => ({ success: false, reason, message });

// Asks Assemble to open the pick's thread with /completed ready. Always resolves; the message on a failure is
// Assemble's own (it may be empty: the tab then shows its generic line).
export async function openClubCompletion(request: ClubCompletionRequest, target: BridgeHost | undefined = host()): Promise<ClubCompletionResult> {
  const bridge = assembleBridge(target);
  if (!bridge) return failure("assemble_unavailable");
  if (!CODES.has(request.event) || !request.title) return failure("invalid_request");
  const payload: ClubCompletionRequest = { event: request.event, title: String(request.title).slice(0, 120) };
  if (request.raUsername) payload.raUsername = String(request.raUsername).slice(0, 64);
  let reply: any;
  try {
    reply = await bridge.openClubCompletion(payload);
  } catch (error: any) {
    return failure("bridge_error", typeof error?.message === "string" ? error.message.slice(0, 300) : "");
  }
  if (reply && reply.success === true && (reply.opened === "thread" || reply.opened === "channel")) {
    return { success: true, opened: reply.opened };
  }
  if (reply && reply.success === false) {
    return failure(typeof reply.reason === "string" && reply.reason ? reply.reason : "failed",
      typeof reply.message === "string" ? reply.message.slice(0, 300) : "");
  }
  return failure("bad_reply");
}
