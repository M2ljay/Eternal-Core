// Eternal Core batch 2 (B13, docs/eternal/BATCH-2.md): when Steam last saw controller input on this PC. Sleep after
// streaming (agent StreamPower.cs) cancels its timer when someone picks up a controller here; Windows' own idle time
// does not see a controller in Big Picture, Steam does. The heartbeat carries "seconds ago" to the agent.
// Self-contained on purpose: tests/coreBatchTwo.test.mjs loads this file alone.

export type LocalInputDeps = {
  input: () => any;
  now: () => number;
};

export type LocalInputWatch = { secondsAgo: () => number | undefined; stop: () => void };

export function installLocalInputWatch(deps: LocalInputDeps): LocalInputWatch {
  let last: number | undefined;
  const registrations: any[] = [];
  const note = () => { last = deps.now(); };
  try {
    const input = deps.input();
    // Button messages only: analog ones (sticks, gyro, a resting thumb on a trackpad) arrive constantly and would keep
    // the PC awake forever.
    if (typeof input?.RegisterForControllerInputMessages === "function") registrations.push(input.RegisterForControllerInputMessages(note));
  } catch { /* No Steam input API: the agent relies on Windows' idle time alone. */ }
  return {
    secondsAgo: () => {
      if (last === undefined) return undefined;
      const seconds = Math.floor((deps.now() - last) / 1000);
      return seconds >= 0 && seconds <= 86400 ? seconds : undefined;
    },
    stop: () => { for (const registration of registrations) { try { registration?.unregister?.(); } catch { /* Already gone. */ } } },
  };
}
