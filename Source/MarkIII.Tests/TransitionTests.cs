using GamingMode.Services;

// Eternal Core tests: the mode-switch transitions (timing, hold, reduced motion, their text).
static class TransitionTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		const TransitionDirection Gaming = TransitionDirection.ToGaming, Desktop = TransitionDirection.ToDesktop;
		const TransitionMotion Full = TransitionMotion.Full, Static = TransitionMotion.Static;
		static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

		// reduced motion
		Check(ModeTransitionTimeline.MotionFor(true, true) == Full, "transition animates when both Core and Windows allow it");
		Check(ModeTransitionTimeline.MotionFor(true, false) == Static, "Windows animation effects off: still logo");
		Check(ModeTransitionTimeline.MotionFor(false, true) == Static, "Core's Gaming Mode animation toggle off: still logo");
		foreach (var direction in new[] { Gaming, Desktop })
		{
			Check(ModeTransitionTimeline.PhaseAt(direction, Static, At(0)) == TransitionPhase.Still
				&& ModeTransitionTimeline.PhaseAt(direction, Static, At(60)) == TransitionPhase.Still, $"{direction}: reduced motion never pulses");
			Check(ModeTransitionTimeline.IntroLength(direction, Static) == ModeTransitionTimeline.StaticFade.TotalSeconds
				&& ModeTransitionTimeline.StaticFade <= ModeTransitionTimeline.HandoffFade, $"{direction}: reduced motion is only a short fade");
		}

		// fits inside the switch
		foreach (var direction in new[] { Gaming, Desktop })
		{
			double intro = ModeTransitionTimeline.IntroLength(direction, Full);
			Check(intro <= (ModeTransitionTimeline.Floor(direction) + ModeTransitionTimeline.HandoffFade).TotalSeconds,
				$"{direction}: the animation ends by the time the quickest switch has faded out ({intro:0.00} s)");
			Check(ModeTransitionTimeline.PhaseAt(direction, Full, At(0)) == TransitionPhase.Intro
				&& ModeTransitionTimeline.PhaseAt(direction, Full, At(intro)) == TransitionPhase.Intro, $"{direction}: no pulse during the intro");
			Check(ModeTransitionTimeline.HoldBegins(direction, Full) >= (ModeTransitionTimeline.Floor(direction) + ModeTransitionTimeline.HandoffFade).TotalSeconds,
				$"{direction}: the quickest switch never shows the hold pulse");
			Check(ModeTransitionTimeline.PhaseAt(direction, Full, At(30)) == TransitionPhase.Hold, $"{direction}: a slow switch holds on the tick pulse");
		}
		Check(ModeTransitionTimeline.Wordmark.End <= ModeTransitionTimeline.GamingFloor.TotalSeconds
			&& ModeTransitionTimeline.Line.End <= ModeTransitionTimeline.GamingFloor.TotalSeconds
			&& ModeTransitionTimeline.Charge.End <= ModeTransitionTimeline.GamingFloor.TotalSeconds,
			"into Gaming Mode: ring, CORE and the line are fully in before the hand-off can start");
		Check(ModeTransitionTimeline.Drain.End <= ModeTransitionTimeline.DesktopFloor.TotalSeconds + 0.2,
			"to Desktop: the ring has drained by the time the hand-off can start");
		Check(ModeTransitionTimeline.HandoffFade == TimeSpan.FromMilliseconds(300), "hand-off fade matches the callers' 300 ms hide");

		// sequence
		Check(ModeTransitionTimeline.StarsIn.Begin == 0 && ModeTransitionTimeline.MarkIn.Begin < ModeTransitionTimeline.Charge.Begin
			&& ModeTransitionTimeline.Charge.End <= ModeTransitionTimeline.Pulse.Begin && ModeTransitionTimeline.Wordmark.Begin < ModeTransitionTimeline.Line.Begin,
			"into Gaming Mode: stars, ring, lock, CORE, then the line");
		Check(ModeTransitionTimeline.StarsRest > 0 && ModeTransitionTimeline.StarsRest < 1, "to Desktop: the stars fade but never leave a bare black screen");
		bool lightsInOrder = true, darkensInReverse = true, insideCharge = true;
		for (int i = 0; i < ModeTransitionTimeline.TickCount; i++)
		{
			double up = ModeTransitionTimeline.TickChange(Gaming, i), down = ModeTransitionTimeline.TickChange(Desktop, i);
			if (i > 0 && up <= ModeTransitionTimeline.TickChange(Gaming, i - 1)) lightsInOrder = false;
			if (i > 0 && down >= ModeTransitionTimeline.TickChange(Desktop, i - 1)) darkensInReverse = false;
			if (up < ModeTransitionTimeline.Charge.Begin || up + ModeTransitionTimeline.TickLight > ModeTransitionTimeline.Charge.End + 0.05
				|| down < ModeTransitionTimeline.Drain.Begin || down > ModeTransitionTimeline.Drain.End) insideCharge = false;
		}
		Check(lightsInOrder, "ticks light in sequence, clockwise from nine o'clock");
		Check(darkensInReverse, "ticks go dark in reverse");
		Check(insideCharge, "every tick changes while the charge is moving");
		// A tick changes exactly when the eased charge reaches it.
		double third = ModeTransitionTimeline.TickChange(Gaming, 2);
		double reached = ModeTransitionTimeline.CubicInOut((third - ModeTransitionTimeline.Charge.Begin) / ModeTransitionTimeline.Charge.Length);
		Check(Math.Abs(reached - 2.0 / ModeTransitionTimeline.TickCount) < 1e-9, "tick timing follows the boot's ease-in-out charge");
		bool inverse = true;
		for (double p = 0; p <= 1.0001; p += 0.05)
			if (Math.Abs(ModeTransitionTimeline.CubicInOut(ModeTransitionTimeline.InverseCubicInOut(p)) - Math.Min(p, 1)) > 1e-9) inverse = false;
		Check(inverse, "the ease's inverse is exact");

		// the hold
		Check(ModeTransitionTimeline.PulseLength < ModeTransitionTimeline.PulsePeriod,
			"the hold pulse is a soft dip that leaves most ticks at rest at any moment");
		Check(Math.Abs(ModeTransitionTimeline.PulseBegins(Gaming, 1) - ModeTransitionTimeline.PulseBegins(Gaming, 0) - ModeTransitionTimeline.PulseStagger) < 1e-9
			&& ModeTransitionTimeline.PulseBegins(Desktop, 0) > ModeTransitionTimeline.PulseBegins(Desktop, 7),
			"the pulse chases round the dial in the direction the charge last moved");
		bool threw = false;
		try { ModeTransitionTimeline.TickChange(Gaming, 8); } catch (ArgumentOutOfRangeException) { threw = true; }
		Check(threw, "there are eight ticks");

		// text
		Check(CoreText.Get("en", "transition.gaming") == "Gaming Mode" && CoreText.Get("en", "transition.desktop") == "Desktop",
			"the steel lines read Gaming Mode and Desktop");
		Check(CoreText.Get("it", "transition.desktop") == "Desktop" && CoreText.Get("fr", "transition.desktop") == "Bureau"
			&& CoreText.Get("ja", "transition.gaming") == "ゲーミングモード" && CoreText.Get("ua", "transition.desktop") == "Робочий стіл",
			"the steel lines follow Core's language");
		bool noPlayhub = true;
		foreach (string key in new[] { "transition.gaming", "transition.desktop" })
			foreach (string language in CoreText.Languages)
				if (CoreText.Get(language, key).Contains("Playhub", StringComparison.OrdinalIgnoreCase)) noPlayhub = false;
		Check(noPlayhub, "no Playhub on the transitions");
	}
}
