using System;
using System.Collections.Generic;
using System.Linq;
using Playhub.Shared;

namespace GamingMode.Services;

// Jarvis in the agent (docs/eternal/JARVIS.md): the pure rules, tested on Linux (CoreHealth.Tests, JarvisTests.cs).
// - JarvisMicPolicy: which microphone push-to-talk records from, under Core Voice's mute scope and calls mic.
// - JarvisCombosView: the three combos handed to Core's Decky plugin, cleaned, with clashing ones turned off.
// - JarvisScreenSize: the size a screenshot is scaled to before it is sent.
// - JarvisListenPolicy: what happens when push-to-talk is released (too short, silent, too long).
// - JarvisHealth: the Health row.
// The Windows half (capture, screenshot, playback, typing, the indicator) is Jarvis.Windows.cs; the wiring and the
// endpoints MarkIIIHost.Jarvis.cs.

// One capture endpoint as Core Voice sees it (MicEndpoint, reduced to what this policy needs).
internal sealed record JarvisMic(string Id, bool Muted, bool IsCommunications, bool IsConsole, string? Name = null);

internal sealed record JarvisMicChoice(string? Id, string? Name, string? Refusal, bool PartyHears = false);

internal static class JarvisMicPolicy
{
	// Core Voice's calls mic first (the DJI receiver, when chosen and connected), then the Communications default, then
	// the Console default. Never a muted microphone:
	// - "Mute everything" with Core's mute on, or any default microphone muted while the scope is everything: refused.
	// - "Mute calls only": the calls microphone is out while it is muted; the games-and-apps microphone may record,
	//   since that is exactly what that scope keeps live for apps.
	// An Xbox party in the Game Bar does not stop it, but the party hears what is said (shared-mode capture).
	internal static JarvisMicChoice Choose(IReadOnlyList<JarvisMic> mics, bool callsOnlyScope, bool coreMuted, string? callsMicId, bool partyOpen)
	{
		if (mics.Count == 0) return new JarvisMicChoice(null, null, "no-mic");
		if (!callsOnlyScope && (coreMuted || mics.Any(mic => (mic.IsConsole || mic.IsCommunications) && mic.Muted))) return new JarvisMicChoice(null, null, "muted");
		var candidates = new List<JarvisMic>();
		void Add(JarvisMic? mic) { if (mic is not null && !candidates.Contains(mic)) candidates.Add(mic); }
		Add(string.IsNullOrEmpty(callsMicId) ? null : mics.FirstOrDefault(mic => mic.Id == callsMicId));
		Add(mics.FirstOrDefault(mic => mic.IsCommunications));
		Add(mics.FirstOrDefault(mic => mic.IsConsole));
		if (candidates.Count == 0) return new JarvisMicChoice(null, null, "no-mic");
		bool callsMuted = coreMuted || candidates.Any(mic => (mic.IsCommunications || mic.Id == callsMicId) && mic.Muted);
		foreach (JarvisMic mic in candidates)
		{
			if (mic.Muted) continue;
			bool isCalls = mic.IsCommunications || mic.Id == callsMicId;
			if (callsOnlyScope && callsMuted && isCalls) continue;
			return new JarvisMicChoice(mic.Id, mic.Name, null, partyOpen);
		}
		return new JarvisMicChoice(null, null, "muted");
	}
}

internal sealed record JarvisCombosView(string? Dictate, string? Talk, string? Screen);

internal static class JarvisCombosPolicy
{
	// Cleaned combos; when two clash (one contains the other), the later one in dictate, talk, screen order is off.
	internal static JarvisCombosView Clean(string? dictate, string? talk, string? screen)
	{
		var chosen = new List<JarvisCombo>();
		string? Keep(string? text, string fallback)
		{
			string? clean = JarvisCombos.Clean(text, fallback);
			JarvisCombo? combo = JarvisCombos.Parse(clean);
			if (combo is null || chosen.Any(other => JarvisCombos.Overlap(other, combo))) return null;
			chosen.Add(combo);
			return combo.Text;
		}
		// 3.3.5: only the talk combo exists; dictation and the screen helper are on the Jarvis tab's buttons.
		_ = dictate; _ = screen;
		return new JarvisCombosView(null, Keep(talk, JarvisConfig.DefaultTalkCombo), null);
	}
}

// ---------- tap to talk: the input level, the end of speech and the listening pill's bars ----------

internal static class JarvisListenLevels
{
	// One level per ~50 ms window of the recording: the RMS of its samples (0..1 of full scale). A buffer Windows flags
	// silent, a muted or dead microphone, gives 0.
	internal const int WindowMs = 50;
	internal static float Rms(ReadOnlySpan<float> samples)
	{
		if (samples.Length == 0) return 0;
		double sum = 0;
		foreach (float sample in samples) sum += sample * sample;
		return (float)Math.Min(1, Math.Sqrt(sum / samples.Length));
	}

	// Tap to talk ends on a pause: once speech was heard (a level above SpeechLevel), SilenceAfterSpeech below it
	// ends the recording; with no speech at all it ends after NoSpeech. The 30-second cap and a mute end it too.
	internal const float SpeechLevel = 0.02f;
	internal static readonly TimeSpan SilenceAfterSpeech = TimeSpan.FromMilliseconds(1200), NoSpeech = TimeSpan.FromSeconds(8);

	internal sealed record SilenceState(bool HeardSpeech, TimeSpan QuietFor, TimeSpan Elapsed);

	internal static (SilenceState State, bool End) Next(SilenceState state, float level, TimeSpan window)
	{
		bool speech = level >= SpeechLevel;
		var next = new SilenceState(state.HeardSpeech || speech, speech ? TimeSpan.Zero : state.QuietFor + window, state.Elapsed + window);
		bool end = next.HeardSpeech ? next.QuietFor >= SilenceAfterSpeech : next.Elapsed >= NoSpeech;
		return (next, end);
	}

	// The pill's nine bars, 6 to 30 px (at 1080p), from the latest levels (newest last), smoothed so they do not
	// flicker; a level of 0.25 (loud speech) or more fills a bar. Reduced motion: static mid-height bars.
	internal const int Bars = 9;
	internal static double[] Heights(IReadOnlyList<float> levels, double[]? previous, bool reducedMotion)
	{
		var heights = new double[Bars];
		for (int i = 0; i < Bars; i++)
		{
			if (reducedMotion) { heights[i] = 18; continue; }
			int index = levels.Count - Bars + i;
			float level = index >= 0 && index < levels.Count ? levels[index] : 0;
			double target = 6 + 24 * Math.Clamp(Math.Sqrt(level / 0.25), 0, 1);
			double before = previous is { Length: Bars } ? previous[i] : 6;
			heights[i] = Math.Round(before + (target - before) * 0.6, 1);
		}
		return heights;
	}
}

internal static class JarvisScreenSize
{
	// Long edge at most 1280 px (about 1,200 visual tokens at 1280x720: ceil(w/28) x ceil(h/28)), never enlarged.
	internal const int LongEdge = 1280;
	internal const int JpegQuality = 80;

	internal static (int Width, int Height) Fit(int width, int height)
	{
		if (width <= 0 || height <= 0) return (0, 0);
		int longest = Math.Max(width, height);
		if (longest <= LongEdge) return (width, height);
		double scale = (double)LongEdge / longest;
		return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
	}

	internal static int VisualTokens(int width, int height) => (int)(Math.Ceiling(width / 28.0) * Math.Ceiling(height / 28.0));
}

internal enum JarvisListenMode { Dictate, Talk }

internal static class JarvisListenPolicy
{
	// "" = nothing to send. Codes: "too-short", "silent".
	internal static string? AfterRecording(short[] samples) =>
		samples.Length < JarvisAudio.SampleRate * JarvisAudio.MinimumSpeech.TotalSeconds ? "too-short" : JarvisAudio.IsSpeech(samples) ? null : "silent";

	internal static JarvisListenMode? ParseMode(string? text) => text switch { "dictate" => JarvisListenMode.Dictate, "talk" => JarvisListenMode.Talk, _ => null };
}

internal static class JarvisHealth
{
	internal static HealthRow Row(bool enabled, string backend, bool haToken, bool claudeKey, string? lastError, bool everAnswered, string haUrl)
	{
		var data = new Dictionary<string, object?> { ["backend"] = backend };
		if (backend == JarvisConfig.BackendHomeAssistant && Uri.TryCreate(haUrl, UriKind.Absolute, out Uri? uri)) data["host"] = uri.Host;
		if (!enabled) return new HealthRow("jarvis", HealthLevel.Info, "off", data);
		string code = JarvisStatus.Code(backend, haToken, claudeKey, lastError, everAnswered);
		data["screenHelper"] = claudeKey;
		return new HealthRow("jarvis", code == "connected" ? HealthLevel.Ok : HealthLevel.Info, code, data);
	}
}
