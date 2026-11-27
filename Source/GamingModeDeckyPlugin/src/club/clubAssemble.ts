// Eternal Core: logging a club completion through Eternal-Assemble (the Discord plugin), later.
//
// TODO(club-assemble): once Assemble exposes a "send slash command" bridge, Core will offer "Log this win" on a
// matched pick: show the exact command in a confirmation dialog, and only after the user confirms ask Assemble to
// send it in the game's thread. Nothing is sent from Core today: this hook reports "unavailable", and the Club
// tab shows the manual steps instead.

// The club's completion command (playretrogamesclub.com/rules/, checked 2026-11-27):
// /completed [event] [ra_username] [screenshot], run in the game's thread on the club's Discord.
export const CLUB_COMPLETION_COMMAND = "/completed";
export const CLUB_COMPLETION_OPTIONS = ["event", "ra_username", "screenshot"] as const;

export type CompletionRequest = { event: string; title: string; confirmedByUser: true };
export type CompletionResult = { sent: false; reason: "not_implemented" };

export async function requestAssembleCompletion(_request: CompletionRequest): Promise<CompletionResult> {
  return { sent: false, reason: "not_implemented" };
}
