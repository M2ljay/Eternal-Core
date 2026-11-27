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
    private ComboBox _steamSoakCombo = new();
    // "Update Steam now": how many times the user has confirmed the warning in front of them (agent SteamManualPolicy).
    private int _steamConfirmations;
    private DateTimeOffset _steamConfirmedAt;

    // Days a new Steam version waits before Core installs it (agent SteamUpdateHoldPolicy, 1-60; 10 by default).
    private static readonly ComboOption[] SteamSoakOptions =
    {
        new("3", "3"), new("5", "5"), new("7", "7"), new("10", "10"), new("14", "14"), new("21", "21"), new("30", "30")
    };

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
        gaming.ConsoleLauncherFocusGuard = GetToggle("consoleLauncherFocus");
        gaming.ConsoleWakeChecks = GetToggle("consoleWakeChecks");
        if (int.TryParse(GetComboKey(_steamSoakCombo), NumberStyles.None, CultureInfo.InvariantCulture, out int soak)) gaming.SteamUpdateSoakDays = soak;
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
        SetToggle("consoleLauncherFocus", gaming.ConsoleLauncherFocusGuard);
        SetToggle("consoleWakeChecks", gaming.ConsoleWakeChecks);
        _healthServerBox.Text = gaming.CoreHealthServer ?? "";
        int soakDays = gaming.SteamUpdateSoakDays is >= 1 and <= 60 ? gaming.SteamUpdateSoakDays : 10;
        string soakKey = SteamSoakOptions.Select(option => option.Key).OrderBy(key => Math.Abs(int.Parse(key, CultureInfo.InvariantCulture) - soakDays)).First();
        SelectComboKey(_steamSoakCombo, soakKey);
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
            // Stream mode (agent CoreStreamMode.cs): the PC streams elsewhere or "Keep TV out of it" is on.
            "StreamMode" => "Modalità streaming attiva: Core lascia stare la TV.",
            _ => "Non riesco a controllare la TV."
        });
    }

    private void ShowTvStatus(string text, bool raw = false) => _tvStatus.Text = raw ? text : T(text);

    // ---------- Steam update hold and Core Repair ----------

    private FrameworkElement BuildSteamHoldActions()
    {
        var panel = new StackPanel { Spacing = 8 };
        _steamSoakCombo = ChoiceCombo(SteamSoakOptions);
        _steamSoakCombo.SelectionChanged += (_, _) => AutoSaveGaming();
        panel.Children.Add(Labeled("Giorni di attesa per una nuova versione di Steam", _steamSoakCombo));
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

    // Same safety net as the automatic update (backup, smoke test, rollback). The agent's gate may ask for a
    // confirmation first: one when it cannot tell whether the new Steam works with Decky, two when it knows it does not.
    private async Task UpdateSteamNowAsync()
    {
        if (DateTimeOffset.Now - _steamConfirmedAt > TimeSpan.FromMinutes(2)) _steamConfirmations = 0;
        var reply = await AgentAsync(HttpMethod.Post, "/steam/update/now?confirm=" + _steamConfirmations.ToString(CultureInfo.InvariantCulture), TimeSpan.FromSeconds(20));
        if (reply is null) { SetStatus(T("Il servizio Gaming Mode non risponde."), InfoBarSeverity.Warning); return; }
        if (reply.Value.TryGetProperty("confirm", out var confirm) && confirm.ValueKind == JsonValueKind.String)
        {
            _steamConfirmations++;
            _steamConfirmedAt = DateTimeOffset.Now;
            SetStatus(T(confirm.GetString() switch
            {
                "confirm-incompatible" => "Il nuovo Steam non funziona con la versione di Decky installata: i plugin smetteranno di caricarsi. Premi di nuovo se vuoi aggiornare lo stesso.",
                "confirm-incompatible-again" => "Ultima conferma: premi ancora una volta per aggiornare Steam sapendo che i plugin non funzioneranno.",
                _ => "Core non sa se il nuovo Steam funziona con Decky. Premi di nuovo per aggiornare comunque: Core salva prima una copia e torna indietro se qualcosa non va."
            }), InfoBarSeverity.Warning);
            return;
        }
        _steamConfirmations = 0;
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
        string reason = state.TryGetProperty("postponedReason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
        string because = T(reason switch
        {
            "pending-unsupported" => "il nuovo Steam non funziona con la versione di Decky installata.",
            "pending-unknown" => "Core non sa ancora se il nuovo Steam funziona con Decky.",
            _ => "Core non riesce a verificare la compatibilità con Decky."
        });
        _steamHoldStatus.Text = phase switch
        {
            "Current" => T("Steam è aggiornato."),
            "Soaking" => string.Format(CultureInfo.CurrentCulture, T("C'è una nuova versione di Steam: Core la installa tra {0} giorni, di notte, se va d'accordo con Decky."), days),
            "Due" => T("Core aggiornerà Steam la prossima notte degli aggiornamenti, se non stai giocando."),
            "Postponed" => string.Format(CultureInfo.CurrentCulture, T("Aggiornamento di Steam rimandato: {0}"), because),
            "WaitingForChange" => T("L'ultimo aggiornamento di Steam non ha superato il controllo ed è stato annullato. Core riprova quando arriva una correzione."),
            _ => T("Steam si aggiorna da solo, come al solito.")
        };
        var tv = await AgentAsync(HttpMethod.Get, "/tv/state", TimeSpan.FromSeconds(4));
        if (tv is { } tvState && tvState.TryGetProperty("paired", out var paired))
            _tvStatus.Text = T(paired.ValueKind == JsonValueKind.True ? "TV abbinata." : "Abbina prima la TV.");
    }

    // ---------- Core Health ----------

    // The home server's name for Quick Access > Core > Health (the agent reads
    // http://<server>:8123/local/core-status.json, written by Aincrad's nightly backup).
    private FluentCard BuildCoreHealthCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE95E).ToString(), "Core Health",
            "Accesso rapido > Core mostra se backup, server, TV e PC sono a posto."));
        _healthServerBox = TextBox("serverv3");
        card.Children.Add(Labeled("Server di casa", _healthServerBox));
        card.Children.Add(Body("Il nome del server di casa, per esempio serverv3. Core legge lo stato del backup da Home Assistant su quel server."));
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
        // The fixed list plus the update policy that fits this PC's edition (feature-update pin, or Home's driver switch).
        foreach (var fix in CouchHardening.Fixes.Concat(CouchHardening.EditionFixes(registry)))
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

    // ---------- console guards (agent ConsoleGuards.cs, docs/eternal/CONSOLE-GUARDS.md) ----------

    private readonly TextBlock _consoleStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    // The PC stays a console: the switches for the two active guards, and the BitLocker parts that need
    // administrator rights (asked here on the desktop, never at the TV). Everything else is read by the
    // agent and shown in Quick Access > Core > Health; Windows' setup screens and the update policy are
    // applied and undone with Quiet Windows above.
    private FluentCard BuildConsoleGuardsCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE7FC).ToString(), "Resta una console",
            "Core controlla che il PC del salotto non chieda mai tastiera, mouse o password e che la TV resti a posto. Lo stato è in Accesso rapido > Core > Salute."));
        AddExplainedToggle(card, "Rimanda dietro la Big Picture le finestre dei launcher",
            "Quando Epic, EA app, Ubisoft Connect, l'app Xbox o Battle.net si aprono da sole sopra la Big Picture e non stai giocando, Core le sposta dietro senza chiuderle. Se ne riporti una davanti tre volte in dieci minuti, Core la lascia stare.", "consoleLauncherFocus");
        AddExplainedToggle(card, "Controlli al risveglio",
            "Dopo il risveglio Core verifica che la Big Picture sia in primo piano, che la TV sia sull'ingresso di Core e che la rete via cavo funzioni. Se serve, riprende il primo piano o ripete il cambio di ingresso una sola volta.", "consoleWakeChecks");
        card.Children.Add(Body("BitLocker: il controllo della chiave di ripristino e la protezione per gli aggiornamenti del firmware chiedono i permessi di amministratore una sola volta, qui sul desktop. Core non legge né salva mai la chiave; conferma il backup su account.microsoft.com/devices/recoverykey."));
        card.Children.Add(ActionRow(
            Button("Controlla BitLocker", CheckBitLockerAsync),
            Button("Attiva la protezione BitLocker", BitLockerGuardOnAsync),
            Button("Rimuovi la protezione BitLocker", BitLockerGuardOffAsync)));
        card.Children.Add(Body("Accesso automatico: Core non salva password. Per configurarlo di nuovo usa Autologon di Microsoft Sysinternals, che fa conservare la password a Windows in forma cifrata, e in Impostazioni > Account > Opzioni di accesso disattiva l'accesso solo con Windows Hello."));
        card.Children.Add(ActionRow(Button("Dimentica schermo e audio normali", ForgetConsoleNormalAsync)));
        card.Children.Add(_consoleStatus);
        return card;
    }

    private async Task CheckBitLockerAsync()
    {
        bool ok = await RunElevatedAsync("console-guard bitlocker-check", "Console guards BitLocker check");
        _consoleStatus.Text = T(ok ? "BitLocker controllato. Il risultato è in Accesso rapido > Core > Salute."
            : "Il controllo di BitLocker non è riuscito o il permesso di amministratore è stato negato.");
    }

    private async Task BitLockerGuardOnAsync()
    {
        // The task lets this user (not whoever approves the prompt) start it from the Gaming Mode agent.
        string? sid = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value : null;
        if (!BitLockerGuard.IsUserSid(sid)) { _consoleStatus.Text = T("Serve il permesso di amministratore."); return; }
        bool ok = await RunElevatedAsync("console-guard bitlocker-guard-on " + sid, "Console guards BitLocker guard on");
        _consoleStatus.Text = T(ok ? "Protezione BitLocker attiva: prima di un aggiornamento del firmware Core sospende BitLocker per un solo riavvio."
            : "Serve il permesso di amministratore.");
    }

    private async Task BitLockerGuardOffAsync()
    {
        bool ok = await RunElevatedAsync("console-guard bitlocker-guard-off", "Console guards BitLocker guard off");
        _consoleStatus.Text = T(ok ? "Protezione BitLocker rimossa." : "Serve il permesso di amministratore.");
    }

    private async Task ForgetConsoleNormalAsync()
    {
        var reply = await AgentAsync(HttpMethod.Post, "/health/action?id=normal-forget", TimeSpan.FromSeconds(8));
        _consoleStatus.Text = T(reply is { } answer && answer.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            ? "Schermo, audio e controller normali dimenticati." : "Il servizio Gaming Mode non risponde.");
    }

    private static async Task<bool> RunElevatedAsync(string arguments, string what)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "", arguments) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // the prompt was declined
        catch (Exception error) { Diag.Crash(what, error); return false; }
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
