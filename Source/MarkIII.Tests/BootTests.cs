using GamingMode.Services;

// Eternal Core Mark III tests: boot sequence.
static class BootTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		// boot sequence
		{
			var power = BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, true, true, true, true, false, false);
			Check(power.Play && power.Sound, "power-on into Gaming Mode: play with sound");
			Check(BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, true, false, true, true, false, false) is { Play: true, Sound: false }, "sound can be off on its own");
			Check(!BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, false, true, true, true, false, false).Play, "sequence can be off");
			Check(!BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, true, true, false, true, false, false).Play, "no sequence when the startup splash itself is off");
			Check(!BootSequencePolicy.Decide(BootSequenceTrigger.Wake, true, true, true, false, false, false).Play, "not in Desktop Mode");
			Check(!BootSequencePolicy.Decide(BootSequenceTrigger.Wake, true, true, true, true, true, false).Play, "wake with a game open: no overlay");
			Check(!BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, true, true, true, true, false, true).Play
				&& !BootSequencePolicy.Decide(BootSequenceTrigger.Wake, true, true, true, true, false, true).Play, "anti-cheat safe mode engaged: never an overlay");
			Check(BootSequencePolicy.Length < BootSequencePolicy.HardLimit && BootSequencePolicy.MaxStartDelay + BootSequencePolicy.Length <= BootSequencePolicy.HardLimit, "the sequence fits inside its hard time box");

			var skip = new BootSkipDetector();
			Check(!skip.Observe(new ulong[] { 0, 0b100 }), "first sample only records what is already held");
			Check(!skip.Observe(new ulong[] { 0, 0b100 }), "a key held since the start does not skip");
			Check(skip.Observe(new ulong[] { 0x1000, 0b100 }), "a newly pressed controller button skips");
			var again = new BootSkipDetector();
			again.Observe(new ulong[] { 0b1 });
			again.Observe(new ulong[] { 0 });
			Check(again.Observe(new ulong[] { 0b1 }), "releasing and pressing again skips");
			var late = new BootSkipDetector();
			late.Observe(new ulong[] { 0 });
			Check(late.Observe(new ulong[] { 0, 0x10 }), "a controller connected during the sequence can skip too");
		}
	}
}
