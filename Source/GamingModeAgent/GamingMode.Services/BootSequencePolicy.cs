using System;
using System.Collections.Generic;

namespace GamingMode.Services;

// Eternal Core Mark III: the boot sequence (Eternal's key, then Core's dial) shown on the
// Gaming Mode splash at power-on and on wake from sleep. Pure decisions; the drawing is
// BootSequenceVisual.cs and the input polling BootSequenceInput.cs.
//
// It never holds Gaming Mode back: at power-on it plays on the existing splash while Steam
// starts underneath, and the splash's own deadlines still apply; on wake it is a short,
// time-boxed overlay. Any failure falls back to the plain splash or to nothing.
internal enum BootSequenceTrigger { PowerOn, Wake }

internal static class BootSequencePolicy
{
	// Timeline of docs/eternal/brand/preview/index.html, which the boot sound is timed to.
	internal static readonly TimeSpan EternalEnds = TimeSpan.FromSeconds(4.1);
	internal static readonly TimeSpan CoreStarts = TimeSpan.FromSeconds(4.05);
	internal static readonly TimeSpan Length = TimeSpan.FromSeconds(7.6);
	// Waiting for the TV to show a picture before starting never takes longer than this.
	internal static readonly TimeSpan MaxStartDelay = TimeSpan.FromSeconds(6);
	// The splash never waits for the sequence longer than this after it was shown.
	internal static readonly TimeSpan HardLimit = TimeSpan.FromSeconds(16);
	// On wake the last frame stays this long before the overlay fades.
	internal static readonly TimeSpan WakeHold = TimeSpan.FromMilliseconds(600);

	internal readonly record struct Decision(bool Play, bool Sound, string Reason);

	internal static Decision Decide(BootSequenceTrigger trigger, bool sequenceEnabled, bool soundEnabled, bool splashEnabled,
		bool gamingMode, bool gameRunning, bool protectedGame)
	{
		if (!sequenceEnabled) return new(false, false, "turned off in settings");
		if (!gamingMode) return new(false, false, "not in Gaming Mode");
		// Anti-cheat safe mode: no overlay while a protected game runs; and none over any
		// game on wake, where the player returns to the game they left.
		if (protectedGame) return new(false, false, "a protected game is running");
		if (trigger == BootSequenceTrigger.Wake && gameRunning) return new(false, false, "a game is running");
		if (trigger == BootSequenceTrigger.PowerOn && !splashEnabled) return new(false, false, "the startup splash is off");
		return new(true, soundEnabled, "ok");
	}
}

// Any newly pressed controller button or key skips. "Newly": buttons and keys already held
// when the sequence started (a key stuck down, a controller resting on a button) are
// ignored until released, so the sequence is not skipped by accident.
internal sealed class BootSkipDetector
{
	private readonly Dictionary<int, ulong> _held = new();
	private bool _primed;

	// sources: one value per input source (controller slot, keyboard block), each bit a
	// button or key that is down right now.
	internal bool Observe(IReadOnlyList<ulong> sources)
	{
		bool pressed = false;
		for (int i = 0; i < sources.Count; i++)
		{
			ulong now = sources[i];
			ulong before = _held.TryGetValue(i, out ulong held) ? held : (_primed ? 0UL : now);
			if (_primed && (now & ~before) != 0) pressed = true;
			_held[i] = now;
		}
		if (!_primed) { _primed = true; return false; }
		return pressed;
	}
}
