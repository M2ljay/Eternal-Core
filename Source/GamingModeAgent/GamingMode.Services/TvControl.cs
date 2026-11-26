using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;

namespace GamingMode.Services;

// Eternal Core Mark III: what Core does with the TV, and when. Platform-neutral; the
// Windows pieces (DPAPI key storage, ARP lookup, power events) are injected.
//
// Rules:
// - Wake/boot into Gaming Mode: only a TV that was off is taken over (Wake-on-LAN, wait,
//   switch to Core's HDMI). A TV already on is in use, so it is left on whatever it shows.
// - Sleep/shutdown: turn the TV off only while it shows Core's HDMI (ShouldTurnOff).
// - Every step is time-boxed and failures are logged without the client key. Nothing
//   here ever blocks Gaming Mode: callers run it beside the mode switch, not before it.
internal interface ITvPairingStore
{
	string? ClientKey { get; }
	string? CertificateSha256 { get; }
	void Save(string clientKey, string? certificateSha256);
	void Clear();
}

internal enum TvOutcome { Done, Disabled, NotConfigured, NotPaired, Unreachable, PromptTimedOut, Rejected, CertificateChanged, SkippedOtherInput, LeftOnInUse, Failed }

internal readonly record struct TvResult(TvOutcome Outcome, string? Detail = null, string? MacAddress = null)
{
	internal bool Ok => Outcome is TvOutcome.Done or TvOutcome.SkippedOtherInput or TvOutcome.LeftOnInUse;
}

internal sealed class TvController
{
	private readonly Func<TvSettings> _settings;
	private readonly ITvPairingStore _pairing;
	private readonly Action<string> _log;
	private readonly Func<string, byte[]?> _resolveMac;
	private readonly int _plainPort;
	private readonly SemaphoreSlim _gate = new(1, 1);

	internal TvController(Func<TvSettings> settings, ITvPairingStore pairing, Action<string> log,
		Func<string, byte[]?>? resolveMac = null, int plainPort = LgWebOsProtocol.PlainPort)
	{
		_settings = settings; _pairing = pairing; _log = log;
		_resolveMac = resolveMac ?? (_ => null);
		_plainPort = plainPort;
	}

	internal static string CoreInput(TvSettings settings) => LgWebOsProtocol.NormalizeInput(settings.CoreInput) ?? LgWebOsProtocol.DefaultCoreInput;

	// ---------- automatic ----------

	internal async Task<TvResult> WakeAndSwitchAsync(TimeSpan budget, CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (!settings.Enabled || !settings.TurnOnWithCore) return new(TvOutcome.Disabled);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		deadline.CancelAfter(budget);
		try
		{
			// Asked before Wake-on-LAN: a TV that is already on is someone's, and Core does not
			// switch it away from what they are watching. Unknown (not paired, not answering,
			// a firmware without the power API) is treated as off.
			if (await IsInUseAsync(settings, TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(false))
			{
				_log("TV: already on and in use; left as it is.");
				return new(TvOutcome.LeftOnInUse);
			}
			if (LgWebOsProtocol.TryParseMac(settings.MacAddress, out byte[] mac))
			{
				int sent = await LgWebOsClient.SendWakeAsync(mac, settings.Address, deadline.Token).ConfigureAwait(false);
				_log($"TV: Wake-on-LAN sent ({sent} packets).");
			}
			else _log("TV: no valid MAC address saved; skipping Wake-on-LAN and trying the input switch only.");
			if (string.IsNullOrWhiteSpace(settings.Address)) return new(TvOutcome.NotConfigured);
			if (_pairing.ClientKey is null) return new(TvOutcome.NotPaired);
			// The TV needs a few seconds after Wake-on-LAN before its websocket answers.
			while (true)
			{
				TvResult result = await SwitchCoreInputCoreAsync(settings, TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(false);
				if (result.Outcome != TvOutcome.Unreachable) return result;
				await Task.Delay(1500, deadline.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
		{
			_log("TV: did not answer before the wake deadline.");
			return new(TvOutcome.Unreachable);
		}
	}

	internal async Task<TvResult> TurnOffIfShowingCoreAsync(TimeSpan budget, CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (!settings.Enabled || !settings.TurnOffWithCore) return new(TvOutcome.Disabled);
		if (string.IsNullOrWhiteSpace(settings.Address)) return new(TvOutcome.NotConfigured);
		if (_pairing.ClientKey is not { } key) return new(TvOutcome.NotPaired);
		string input = CoreInput(settings);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		deadline.CancelAfter(budget);
		try
		{
		// Each step gets half the budget, so a TV that ignores the saved key is recognised
		// (and reported) before the overall deadline.
		TimeSpan step = TimeSpan.FromTicks(budget.Ticks / 2);
		return await WithSessionAsync(settings.Address!, key, step, deadline.Token, async session =>
		{
			var foreground = await session.RequestAsync(LgWebOsProtocol.ForegroundAppMessage, step, deadline.Token).ConfigureAwait(false);
			if (!LgWebOsProtocol.ShouldTurnOff(foreground.AppId, input))
			{
				_log($"TV: left on; it shows \"{Safe(foreground.AppId)}\", not Core's {input}.");
				return new TvResult(TvOutcome.SkippedOtherInput, foreground.AppId);
			}
			var off = await session.RequestAsync(LgWebOsProtocol.TurnOffMessage, step, deadline.Token).ConfigureAwait(false);
			_log(off.ReturnValue == true ? $"TV: turned off (it was showing Core's {input})." : $"TV: turn-off request answered {Safe(off.Error)}.");
			return new TvResult(off.ReturnValue == true ? TvOutcome.Done : TvOutcome.Failed, off.Error);
		}).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
		{
			_log("TV: did not answer before the sleep/shutdown deadline; left as it is.");
			return new(TvOutcome.Unreachable);
		}
	}

	// ---------- from the Gaming Mode page ----------

	internal async Task<TvResult> PairAsync(Action? onPrompt, CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (string.IsNullOrWhiteSpace(settings.Address)) return new(TvOutcome.NotConfigured);
		await _gate.WaitAsync(cancellation).ConfigureAwait(false);
		try
		{
			// A new pairing re-pins the certificate, so no pinned value is passed here.
			await using var session = await LgWebOsClient.Session.ConnectAsync(settings.Address!, null, _plainPort, TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
			var result = await session.RegisterAsync(null, TimeSpan.FromSeconds(60), onPrompt, cancellation).ConfigureAwait(false);
			switch (result.Outcome)
			{
				case LgWebOsClient.RegisterOutcome.Registered:
					_pairing.Save(result.ClientKey!, session.Secure ? session.CertificateSha256 : null);
					string? mac = null;
					if (!LgWebOsProtocol.TryParseMac(settings.MacAddress, out _) && _resolveMac(settings.Address!) is { Length: 6 } found)
						mac = LgWebOsProtocol.FormatMac(found);
					_log($"TV: paired over {(session.Secure ? "wss (port 3001)" : "ws (port 3000)")}.{(mac is null ? "" : " MAC address read from the network.")}");
					return new(TvOutcome.Done, null, mac);
				case LgWebOsClient.RegisterOutcome.Rejected: _log("TV: pairing was declined on the TV."); return new(TvOutcome.Rejected);
				case LgWebOsClient.RegisterOutcome.PromptTimedOut: _log("TV: pairing prompt was not accepted in time."); return new(TvOutcome.PromptTimedOut);
				default: _log("TV: pairing failed: " + Safe(result.Detail)); return new(TvOutcome.Failed, result.Detail);
			}
		}
		catch (Exception error) when (IsNetwork(error))
		{
			_log("TV: not reachable for pairing: " + Safe(error.Message));
			return new(TvOutcome.Unreachable, error.Message);
		}
		finally { _gate.Release(); }
	}

	internal async Task<TvResult> TurnOnAsync(CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (!LgWebOsProtocol.TryParseMac(settings.MacAddress, out byte[] mac)) return new(TvOutcome.NotConfigured);
		await LgWebOsClient.SendWakeAsync(mac, settings.Address, cancellation).ConfigureAwait(false);
		return new(TvOutcome.Done);
	}

	internal async Task<TvResult> SwitchCoreInputAsync(CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (string.IsNullOrWhiteSpace(settings.Address)) return new(TvOutcome.NotConfigured);
		if (_pairing.ClientKey is null) return new(TvOutcome.NotPaired);
		return await SwitchCoreInputCoreAsync(settings, TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
	}

	// The page's own "Turn off" button is an explicit request, so it does not check the input.
	internal async Task<TvResult> TurnOffNowAsync(CancellationToken cancellation)
	{
		TvSettings settings = _settings();
		if (string.IsNullOrWhiteSpace(settings.Address)) return new(TvOutcome.NotConfigured);
		if (_pairing.ClientKey is not { } key) return new(TvOutcome.NotPaired);
		return await WithSessionAsync(settings.Address!, key, TimeSpan.FromSeconds(5), cancellation, async session =>
		{
			var off = await session.RequestAsync(LgWebOsProtocol.TurnOffMessage, TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
			return new TvResult(off.ReturnValue == true ? TvOutcome.Done : TvOutcome.Failed, off.Error);
		}).ConfigureAwait(false);
	}

	internal Task<IReadOnlyList<LgWebOsProtocol.DiscoveredTv>> DiscoverAsync(CancellationToken cancellation) =>
		LgWebOsClient.DiscoverAsync(TimeSpan.FromSeconds(3), cancellation);

	// ---------- shared ----------

	private async Task<bool> IsInUseAsync(TvSettings settings, TimeSpan step, CancellationToken cancellation)
	{
		if (string.IsNullOrWhiteSpace(settings.Address) || _pairing.ClientKey is not { } key) return false;
		try
		{
			TvResult probe = await WithSessionAsync(settings.Address!, key, step, cancellation, async session =>
			{
				var power = await session.RequestAsync(LgWebOsProtocol.PowerStateMessage, step, cancellation).ConfigureAwait(false);
				bool? inUse = LgWebOsProtocol.TvInUse(power.PowerState);
				_log($"TV: power state before wake is \"{Safe(power.PowerState)}\".");
				return new TvResult(inUse == true ? TvOutcome.LeftOnInUse : TvOutcome.Done);
			}).ConfigureAwait(false);
			return probe.Outcome == TvOutcome.LeftOnInUse;
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return false; }
	}

	private async Task<TvResult> SwitchCoreInputCoreAsync(TvSettings settings, TimeSpan step, CancellationToken cancellation)
	{
		string input = CoreInput(settings);
		return await WithSessionAsync(settings.Address!, _pairing.ClientKey!, step, cancellation, async session =>
		{
			var foreground = await session.RequestAsync(LgWebOsProtocol.ForegroundAppMessage, step, cancellation).ConfigureAwait(false);
			if (LgWebOsProtocol.ForegroundIsInput(foreground.AppId, input))
			{
				_log($"TV: already on Core's {input}.");
				return new TvResult(TvOutcome.Done);
			}
			var reply = await session.RequestAsync(id => LgWebOsProtocol.SwitchInputMessage(id, input), step, cancellation).ConfigureAwait(false);
			_log(reply.ReturnValue == true ? $"TV: switched to {input}." : $"TV: input switch answered {Safe(reply.Error)}.");
			return new TvResult(reply.ReturnValue == true ? TvOutcome.Done : TvOutcome.Failed, reply.Error);
		}).ConfigureAwait(false);
	}

	private async Task<TvResult> WithSessionAsync(string address, string key, TimeSpan step, CancellationToken cancellation,
		Func<LgWebOsClient.Session, Task<TvResult>> work)
	{
		await _gate.WaitAsync(cancellation).ConfigureAwait(false);
		try
		{
			string? pinned = _pairing.CertificateSha256;
			await using var session = await LgWebOsClient.Session.ConnectAsync(address, pinned, pinned is null ? _plainPort : null, step, cancellation).ConfigureAwait(false);
			var registered = await session.RegisterAsync(key, step, null, cancellation).ConfigureAwait(false);
			if (registered.Outcome != LgWebOsClient.RegisterOutcome.Registered)
			{
				// A key the TV no longer accepts makes it show a pairing prompt; say so once.
				_log("TV: the saved pairing was not accepted. Pair the TV again from Core's Gaming Mode page.");
				return new(TvOutcome.NotPaired);
			}
			return await work(session).ConfigureAwait(false);
		}
		catch (TvCertificateChangedException error)
		{
			_log("TV: " + error.Message);
			return new(TvOutcome.CertificateChanged);
		}
		catch (Exception error) when (IsNetwork(error) && !cancellation.IsCancellationRequested)
		{
			return new(TvOutcome.Unreachable, error.Message);
		}
		finally { _gate.Release(); }
	}

	private static bool IsNetwork(Exception error) =>
		error is WebSocketException or SocketException or System.IO.IOException or System.Net.Http.HttpRequestException
			or OperationCanceledException or TimeoutException;

	// Log text from the TV: short and single-line. Never used for the client key.
	private static string Safe(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "(nothing)";
		string line = text.Replace('\r', ' ').Replace('\n', ' ');
		return line.Length > 120 ? line[..120] : line;
	}
}
