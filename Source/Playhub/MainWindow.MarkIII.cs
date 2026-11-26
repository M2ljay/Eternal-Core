using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Playhub.Services;
using Playhub.Shared;

namespace Playhub;

// Eternal Core Mark III on the Gaming Mode page: boot sequence, TV control, Steam update
// hold and Core Repair. The work happens in the Gaming Mode agent (MarkIIIHost.cs); this
// page only edits the shared config.json and calls the agent's local endpoints.
public sealed partial class MainWindow
{
    private TextBox _tvAddressBox = new();
    private TextBox _healthServerBox = new();
    private readonly TextBlock _healthStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private TextBox _tvMacBox = new();
    private ComboBox _tvInputCombo = new();
    private readonly TextBlock _tvStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _steamHoldStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    private static readonly ComboOption[] TvInputOptions =
    {
        new("HDMI_1", "HDMI 1"), new("HDMI_2", "HDMI 2"), new("HDMI_3", "HDMI 3"), new("HDMI_4", "HDMI 4")
    };

    // ---------- boot sequence ----------

    private FluentCard BuildBootSequenceCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE7E8).ToString(), "Sequenza di avvio",
            "All'accensione e al risveglio mostra il logo Eternal e poi quello di Core prima della Big Picture. Un tasto qualsiasi del controller la salta."));
        AddExplainedToggle(card, "Mostra la sequenza di avvio",
            "Circa sette secondi su sfondo nero. Non ritarda mai Steam: se qualcosa non va, la Gaming Mode parte comunque.", "bootSequence");
        AddExplainedToggle(card, "Suono di avvio",
            "Il suono arcade che accompagna la sequenza.", "bootSound");
        return card;
    }

    // ---------- TV (LG webOS) ----------

    private FluentCard BuildTvCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE7F4).ToString(), "LG webOS",
            "Accensione via Wake-on-LAN, spegnimento e HDMI tramite abbinamento locale webOS."));
        AddExplainedToggle(card, "Controllo della TV",
            "Core accende la TV e passa al suo ingresso quando entra in Gaming Mode, e la spegne quando va in sospensione o si spegne.", "tvEnabled");
        _tvAddressBox = TextBox("192.168.1.50");
        _tvMacBox = TextBox("A8:23:FE:01:02:03");
        _tvInputCombo = ChoiceCombo(TvInputOptions);
        card.Children.Add(TwoColumn(Labeled("Indirizzo IP", _tvAddressBox), Labeled("Indirizzo MAC", _tvMacBox)));
        card.Children.Add(Labeled("Ingresso HDMI", _tvInputCombo));
        AddExplainedToggle(card, "Accendi all'avvio del PC", "Passa a questo HDMI all'avvio", "tvOn");
        AddExplainedToggle(card, "Spegni la TV con Core",
            "Quando Core va in sospensione o si spegne, spegne la TV solo se sta mostrando Core. Se qualcuno guarda altro, la TV resta accesa.", "tvOff");
        card.Children.Add(ActionRow(
            Button("Cerca la TV", FindTvAsync),
            Button("Abbina", PairTvAsync, primary: true),
            Button("Accendi ora", () => TvCommandAsync("/tv/on")),
            Button("Passa a HDMI", () => TvCommandAsync("/tv/input")),
            Button("Spegni", () => TvCommandAsync("/tv/off"))));
        card.Children.Add(Body("Sulla TV attiva Impostazioni > Generali > Mobile TV On > Accendi tramite Wi-Fi, e Quick Start+. Al primo abbinamento accetta la richiesta sulla TV con il telecomando."));
        card.Children.Add(_tvStatus);
        _tvInputCombo.SelectionChanged += (_, _) => AutoSaveGaming();
        foreach (var box in new[] { _tvAddressBox, _tvMacBox }) box.LostFocus += (_, _) => AutoSaveGaming();
        return card;
    }

    private void ReadMarkIIIIntoConfig()
    {
        var gaming = _gamingConfig.Gaming;
        gaming.Splash.BootSequenceEnabled = GetToggle("bootSequence");
        gaming.Splash.BootSoundEnabled = GetToggle("bootSound");
        gaming.Tv ??= new();
        gaming.Tv.Enabled = GetToggle("tvEnabled");
        gaming.Tv.TurnOnWithCore = GetToggle("tvOn");
        gaming.Tv.TurnOffWithCore = GetToggle("tvOff");
        gaming.Tv.Address = EmptyToNull(_tvAddressBox.Text.Trim());
        gaming.Tv.MacAddress = EmptyToNull(_tvMacBox.Text.Trim());
        gaming.Tv.CoreInput = GetComboKey(_tvInputCombo) ?? "HDMI_2";
        gaming.DeckyHealthNoticeEnabled = GetToggle("deckyNotice");
        gaming.QuietWindowsToasts = GetToggle("quietToasts");
        gaming.CoreHealthServer = EmptyToNull(_healthServerBox.Text.Trim());
        gaming.CoreHealthAlertsEnabled = GetToggle("healthAlerts");
    }

    private void WriteMarkIIIIntoControls()
    {
        var gaming = _gamingConfig.Gaming;
        gaming.Tv ??= new();
        SetToggle("bootSequence", gaming.Splash.BootSequenceEnabled);
        SetToggle("bootSound", gaming.Splash.BootSoundEnabled);
        SetToggle("tvEnabled", gaming.Tv.Enabled);
        SetToggle("tvOn", gaming.Tv.TurnOnWithCore);
        SetToggle("tvOff", gaming.Tv.TurnOffWithCore);
        SetToggle("deckyNotice", gaming.DeckyHealthNoticeEnabled);
        SetToggle("quietToasts", gaming.QuietWindowsToasts);
        SetToggle("healthAlerts", gaming.CoreHealthAlertsEnabled);
        _healthServerBox.Text = gaming.CoreHealthServer ?? "";
        RefreshQuietWindowsStatus();
        _tvAddressBox.Text = gaming.Tv.Address ?? "";
        _tvMacBox.Text = gaming.Tv.MacAddress ?? "";
        SelectComboKey(_tvInputCombo, string.IsNullOrWhiteSpace(gaming.Tv.CoreInput) ? "HDMI_2" : gaming.Tv.CoreInput);
        _ = RefreshMarkIIIStatusAsync();
    }

    private async Task FindTvAsync()
    {
        var reply = await AgentAsync(HttpMethod.Post, "/tv/discover", TimeSpan.FromSeconds(10));
        if (reply is null) { ShowTvStatus("Il servizio Gaming Mode non risponde."); return; }
        if (reply.Value.TryGetProperty("tvs", out var tvs) && tvs.ValueKind == JsonValueKind.Array && tvs.GetArrayLength() > 0)
        {
            var first = tvs[0];
            _tvAddressBox.Text = first.GetProperty("address").GetString() ?? "";
            await SaveGamingConfigAsync();
            ShowTvStatus(string.Format(CultureInfo.CurrentCulture, T("TV trovata: {0}"), first.GetProperty("name").GetString() ?? _tvAddressBox.Text), raw: true);
        }
        else ShowTvStatus("Nessuna TV LG trovata in rete. Inserisci l'indirizzo IP a mano.");
    }

    private async Task PairTvAsync()
    {
        await SaveGamingConfigAsync();
        if (string.IsNullOrWhiteSpace(_tvAddressBox.Text)) { ShowTvStatus("Inserisci prima l'indirizzo IP della TV."); return; }
        ShowTvStatus("Accetta la richiesta sulla TV con il telecomando.");
        var reply = await AgentAsync(HttpMethod.Post, "/tv/pair", TimeSpan.FromSeconds(80));
        if (reply is { } result && result.TryGetProperty("macAddress", out var mac) && mac.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(mac.GetString()) && string.IsNullOrWhiteSpace(_tvMacBox.Text))
        {
            _tvMacBox.Text = mac.GetString()!;
            await SaveGamingConfigAsync();
        }
        ShowTvOutcome(reply, "TV abbinata.");
    }

    private async Task TvCommandAsync(string path)
    {
        await SaveGamingConfigAsync();
        ShowTvOutcome(await AgentAsync(HttpMethod.Post, path, TimeSpan.FromSeconds(20)), "Fatto.");
    }

    private void ShowTvOutcome(JsonElement? reply, string success)
    {
        if (reply is null) { ShowTvStatus("Il servizio Gaming Mode non risponde."); return; }
        string outcome = reply.Value.TryGetProperty("outcome", out var value) ? value.GetString() ?? "" : "";
        ShowTvStatus(outcome switch
        {
            "Done" => success,
            "NotConfigured" => "Inserisci prima l'indirizzo IP della TV.",
            "NotPaired" or "CertificateChanged" => "Abbina prima la TV.",
            "Rejected" => "Abbinamento rifiutato sulla TV.",
            "PromptTimedOut" => "La richiesta sulla TV non è stata accettata in tempo.",
            "Unreachable" => "La TV non risponde. Controlla che sia accesa e sulla stessa rete.",
            _ => "Non riesco a controllare la TV."
        });
    }

    private void ShowTvStatus(string text, bool raw = false) => _tvStatus.Text = raw ? text : T(text);

    // ---------- Steam update hold and Core Repair ----------

    private FrameworkElement BuildSteamHoldActions()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_steamHoldStatus);
        panel.Children.Add(ActionRow(
            Button("Aggiorna Steam ora", UpdateSteamNowAsync),
            Button("Apri Core Repair", OpenCoreRepairAsync)));
        return panel;
    }

    private FluentCard BuildCoreRepairCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE90F).ToString(), "Core Repair",
            "Se i plugin di Big Picture non si caricano, Core lo dice con un breve avviso. Core Repair, nella libreria di Steam, li ripara usando il controller, anche senza Decky."));
        AddExplainedToggle(card, "Avviso se i plugin non si caricano",
            "Un messaggio breve sopra Big Picture, mai durante un gioco. Steam e i giochi funzionano comunque.", "deckyNotice");
        card.Children.Add(ActionRow(Button("Apri Core Repair", OpenCoreRepairAsync)));
        return card;
    }

    private async Task UpdateSteamNowAsync()
    {
        var reply = await AgentAsync(HttpMethod.Post, "/steam/update/now", TimeSpan.FromSeconds(10));
        if (reply is null) { SetStatus(T("Il servizio Gaming Mode non risponde."), InfoBarSeverity.Warning); return; }
        bool busy = reply.Value.TryGetProperty("busy", out var b) && b.ValueKind == JsonValueKind.True;
        SetStatus(T(busy ? "Un aggiornamento è già in corso." : "Aggiornamento di Steam avviato. Core salva prima una copia di Steam e torna tra qualche minuto."),
            busy ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    private Task OpenCoreRepairAsync()
    {
        string exe = _gamingMode.InstalledExe;
        if (!File.Exists(exe)) { SetStatus(T("Il servizio Gaming Mode non risponde."), InfoBarSeverity.Warning); return Task.CompletedTask; }
        ProcessService.StartDetached(exe, "repair", Path.GetDirectoryName(exe));
        return Task.CompletedTask;
    }

    private async Task RefreshMarkIIIStatusAsync()
    {
        var reply = await AgentAsync(HttpMethod.Get, "/steam/update/state", TimeSpan.FromSeconds(4));
        if (reply is not { } state) { _steamHoldStatus.Text = ""; return; }
        string phase = state.TryGetProperty("holdPhase", out var p) ? p.GetString() ?? "" : "";
        int days = state.TryGetProperty("daysLeft", out var d) && d.TryGetInt32(out int left) ? left : 0;
        _steamHoldStatus.Text = phase switch
        {
            "Holding" => string.Format(CultureInfo.CurrentCulture, T("Steam resta a questa versione ancora per {0} giorni, o finché scegli di aggiornarlo."), days),
            "Expired" => T("Trenta giorni passati: Core aggiornerà Steam la prossima volta che non stai giocando."),
            _ => T("Steam si aggiorna da solo, come al solito.")
        };
        var tv = await AgentAsync(HttpMethod.Get, "/tv/state", TimeSpan.FromSeconds(4));
        if (tv is { } tvState && tvState.TryGetProperty("paired", out var paired))
            _tvStatus.Text = T(paired.ValueKind == JsonValueKind.True ? "TV abbinata." : "Abbina prima la TV.");
    }

    // ---------- Core Health ----------

    // The home server's name for Quick Access > Core > Health (the agent reads
    // \\<server>\CoreStatus\core-status.json, written by server/backup on the server).
    private FluentCard BuildCoreHealthCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE95E).ToString(), "Core Health",
            "Accesso rapido > Core mostra se backup, server, TV e PC sono a posto."));
        _healthServerBox = TextBox("V3");
        card.Children.Add(Labeled("Server di casa", _healthServerBox));
        card.Children.Add(Body("Il nome del PC server, per esempio V3. Core legge il suo stato dalla cartella condivisa CoreStatus."));
        AddExplainedToggle(card, "Avvisi di salute",
            "Un breve avviso in Gaming Mode se il backup fallisce due notti di fila, se il disco del server dà problemi o se il server tace da più di un giorno e mezzo. Al massimo una volta al giorno, mai durante un gioco.", "healthAlerts");
        card.Children.Add(ActionRow(Button("Controlla ora", CheckCoreHealthAsync)));
        card.Children.Add(_healthStatus);
        _healthServerBox.LostFocus += (_, _) => AutoSaveGaming();
        return card;
    }

    private async Task CheckCoreHealthAsync()
    {
        await SaveGamingConfigAsync();
        var reply = await AgentAsync(HttpMethod.Get, "/health", TimeSpan.FromSeconds(8));
        if (reply is not { } health || !health.TryGetProperty("attention", out var attention) || !attention.TryGetInt32(out int count))
        {
            _healthStatus.Text = T("Il servizio Gaming Mode non risponde.");
            return;
        }
        _healthStatus.Text = count == 0 ? T("Tutto a posto.") : string.Format(CultureInfo.CurrentCulture, T("Cose da controllare: {0}"), count);
    }

    // ---------- quiet Windows (couch hardening) ----------

    private readonly StackPanel _quietList = new() { Spacing = 4 };
    private readonly TextBlock _quietStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private static string QuietUserBackup => Path.Combine(AppPaths.LocalDataRoot, "couch-user.json");

    private FluentCard BuildQuietWindowsCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE7ED).ToString(), "Windows tranquillo",
            "Spegne le interruzioni di Windows che possono comparire sulla TV. Ogni modifica viene salvata prima e si può annullare da qui."));
        AddExplainedToggle(card, "Nascondi gli avvisi di Windows in Gaming Mode",
            "Niente banner di Windows sulla TV; tornano sul desktop e restano nel centro notifiche. Gli avvisi di Steam e Decky non cambiano.", "quietToasts");
        card.Children.Add(_quietList);
        card.Children.Add(Body("Gli aggiornamenti di Windows aspettano la notte: quando serve un riavvio, Accesso rapido > Core propone di riavviare alle 5:00."));
        card.Children.Add(ActionRow(
            Button("Applica", ApplyQuietWindowsAsync, primary: true),
            Button("Annulla le modifiche", UndoQuietWindowsAsync)));
        card.Children.Add(_quietStatus);
        return card;
    }

    private void RefreshQuietWindowsStatus()
    {
        _quietList.Children.Clear();
        if (!OperatingSystem.IsWindows()) return;
        var registry = new CouchRegistryWindows();
        foreach (var fix in CouchHardening.Fixes)
            _quietList.Children.Add(new TextBlock { Text = (CouchHardening.IsApplied(registry, fix) ? "✓  " : "○  ") + T(fix.Label), TextWrapping = TextWrapping.Wrap });
    }

    private async Task ApplyQuietWindowsAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { CouchHardening.Apply(new CouchRegistryWindows(), CouchHardening.Values(false), QuietUserBackup); }
        catch (Exception error) { Diag.Crash("Quiet Windows apply (user)", error); _quietStatus.Text = T("Non riesco ad applicare le modifiche."); return; }
        bool machine = await RunElevatedCouchAsync("apply");
        _quietStatus.Text = T(machine ? "Applicato. Windows ha chiesto i permessi di amministratore per le impostazioni del PC."
            : "Le impostazioni dell'utente sono applicate; quelle del PC richiedono i permessi di amministratore.");
        RefreshQuietWindowsStatus();
    }

    private async Task UndoQuietWindowsAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { CouchHardening.Undo(new CouchRegistryWindows(), false, QuietUserBackup); }
        catch (Exception error) { Diag.Crash("Quiet Windows undo (user)", error); }
        bool machine = await RunElevatedCouchAsync("undo");
        _quietStatus.Text = T(machine ? "Modifiche annullate."
            : "Le impostazioni dell'utente sono annullate; quelle del PC richiedono i permessi di amministratore.");
        RefreshQuietWindowsStatus();
    }

    // The machine half runs in this same app, restarted elevated: one Windows prompt.
    private static async Task<bool> RunElevatedCouchAsync(string verb)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "", "couch-harden " + verb) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // the prompt was declined
        catch (Exception error) { Diag.Crash("Quiet Windows elevated " + verb, error); return false; }
    }

    private async Task<JsonElement?> AgentAsync(HttpMethod method, string path, TimeSpan timeout)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };
            using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{_gamingMode.ReadConfiguredApiPort()}{path}");
            using var response = await client.SendAsync(request);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
        catch (Exception error)
        {
            Diag.Crash("MainWindow.MarkIII " + path, error);
            return null;
        }
    }
}
