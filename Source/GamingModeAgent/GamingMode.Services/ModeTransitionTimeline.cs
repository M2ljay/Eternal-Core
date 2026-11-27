using System;

namespace GamingMode.Services;

// Eternal Core: the timing of the mode-switch transitions, as pure decisions (no WPF), so
// MarkIII.Tests can check them on Linux. The drawing is ModeTransitionVisual.cs.
//
// Into Gaming Mode the Core mark powers up as it does at the end of the boot sequence, in
// a shorter cut: the stars fade in, the crimson ring charges from nine o'clock with the
// boot's ease-in-out, each tick lights as the charge reaches it, the core pulses with the
// screensaver's corner-flash glow, then "CORE" and the steel line. To Desktop is the same
// run backwards: the charge drains, the ticks go dark in reverse and the stars fade.
//
// The transition never holds the switch back: the switch hides it whenever it is done, and
// the hand-off fade to black runs from whatever frame is on screen. The power-up is short
// enough to finish inside the quickest switch (Floor + HandoffFade); a slower switch holds
// on a soft pulse that chases round the ticks, never a spinner. Reduced motion (Windows'
// animation effects off, or Core's "Gaming Mode animation" toggle off) shows the finished
// logo still, with a fade.
internal enum TransitionDirection { ToGaming, ToDesktop }

internal enum TransitionMotion { Full, Static }

internal enum TransitionPhase { Intro, Hold, Still }

internal readonly record struct TransitionSpan(double Begin, double Length)
{
	internal double End => Begin + Length;
}

internal static class ModeTransitionTimeline
{
	internal const int TickCount = 8;

	// The hand-off: the callers hide the surface with a 300 ms fade (ModeManager,
	// SteamThemeStartupCover), which fades the scene to black before Big Picture or the
	// desktop shows.
	internal static readonly TimeSpan HandoffFade = TimeSpan.FromMilliseconds(300);

	// The quickest each switch can take before the hand-off starts: SteamThemeStartupCover's
	// minimum with the default GamingSplashSettings.MinVisibleMs (1200), and ModeManager's
	// minVisibleMs for Desktop (700).
	internal static readonly TimeSpan GamingFloor = TimeSpan.FromMilliseconds(1200);
	internal static readonly TimeSpan DesktopFloor = TimeSpan.FromMilliseconds(700);

	// Reduced motion: the still logo fades in over this.
	internal static readonly TimeSpan StaticFade = TimeSpan.FromMilliseconds(250);

	// The screensaver's corner flash (screensaver/main.js): a white glow that decays over 1.4 s.
	internal const double FlashLength = 1.4, FlashBlur = 32, FlashOpacity = 0.85;

	// Into Gaming Mode (seconds from the first frame).
	internal static readonly TransitionSpan StarsIn = new(0, 0.35);
	internal static readonly TransitionSpan MarkIn = new(0.05, 0.2);
	internal static readonly TransitionSpan Charge = new(0.15, 0.9);
	internal const double TickLight = 0.12;
	internal static double Lock => Charge.End;
	internal static readonly TransitionSpan Pulse = new(Charge.End, 0.3);
	internal static readonly TransitionSpan Wordmark = new(0.8, 0.3);
	internal static readonly TransitionSpan Line = new(0.9, 0.3);

	// To Desktop.
	internal static readonly TransitionSpan Appear = new(0, 0.2);
	internal static readonly TransitionSpan Drain = new(0.15, 0.7);
	internal static readonly TransitionSpan CentreDim = new(0.8, 0.2);
	internal static readonly TransitionSpan StarsOut = new(0.4, 0.6);
	// The stars fade to this, not to nothing: every black background in Core keeps its stars.
	internal const double StarsRest = 0.3;

	// Tick opacities, as in the boot sequence (unlit .3, lit 1).
	internal const double TickUnlit = 0.3, TickLit = 1;
	internal const double CentreDimmed = 0.35;

	// The hold: each tick dips and returns in turn, round the dial.
	internal const double PulseStagger = 0.2, PulseLength = 0.5, PulseDepth = 0.5;
	internal static double PulsePeriod => PulseStagger * TickCount;

	internal static TransitionMotion MotionFor(bool animationEnabled, bool systemAnimations) =>
		animationEnabled && systemAnimations ? TransitionMotion.Full : TransitionMotion.Static;

	// When the power-up (or power-down) is complete.
	internal static double IntroLength(TransitionDirection direction, TransitionMotion motion) => motion == TransitionMotion.Static
		? StaticFade.TotalSeconds
		: direction == TransitionDirection.ToGaming
			? Math.Max(Math.Max(Charge.End, Pulse.End), Math.Max(Wordmark.End, Line.End))
			: Math.Max(Math.Max(Drain.End, CentreDim.End), StarsOut.End);

	internal static TimeSpan Floor(TransitionDirection direction) => direction == TransitionDirection.ToGaming ? GamingFloor : DesktopFloor;

	// The pulse starts a moment after the intro, so a quick switch never sees it.
	internal static double HoldBegins(TransitionDirection direction, TransitionMotion motion) =>
		motion == TransitionMotion.Static ? double.PositiveInfinity : IntroLength(direction, motion) + 0.4;

	internal static TransitionPhase PhaseAt(TransitionDirection direction, TransitionMotion motion, TimeSpan elapsed)
	{
		if (motion == TransitionMotion.Static) return TransitionPhase.Still;
		return elapsed.TotalSeconds < HoldBegins(direction, motion) ? TransitionPhase.Intro : TransitionPhase.Hold;
	}

	// Tick `index` (clockwise from nine o'clock, CoreMarkDrawing.Tick) changes when the
	// charge reaches it: lighting on the way up, going dark on the way down. The charge runs
	// with the boot's cubic ease-in-out, so the times follow its inverse.
	internal static double TickChange(TransitionDirection direction, int index)
	{
		if (index < 0 || index >= TickCount) throw new ArgumentOutOfRangeException(nameof(index));
		double at = (double)index / TickCount;
		return direction == TransitionDirection.ToGaming
			? Charge.Begin + Charge.Length * InverseCubicInOut(at)
			: Drain.Begin + Drain.Length * InverseCubicInOut(1 - at);
	}

	// The tick's pulse in the hold, in the direction the charge last moved.
	internal static double PulseBegins(TransitionDirection direction, int index)
	{
		if (index < 0 || index >= TickCount) throw new ArgumentOutOfRangeException(nameof(index));
		int step = direction == TransitionDirection.ToGaming ? index : TickCount - 1 - index;
		return HoldBegins(direction, TransitionMotion.Full) + step * PulseStagger;
	}

	// WPF's CubicEase EaseInOut, and its inverse.
	internal static double CubicInOut(double t)
	{
		t = Math.Clamp(t, 0, 1);
		return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
	}

	internal static double InverseCubicInOut(double progress)
	{
		progress = Math.Clamp(progress, 0, 1);
		return progress < 0.5 ? Math.Cbrt(progress / 4) : 1 - Math.Cbrt(2 * (1 - progress)) / 2;
	}
}
