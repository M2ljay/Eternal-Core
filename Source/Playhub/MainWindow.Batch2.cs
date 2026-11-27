using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Playhub.Services;
using Playhub.Shared;

namespace Playhub;

// Eternal Core batch 2 on the Gaming Mode page (docs/eternal/BATCH-2.md): the Home Assistant bridge (MQTT), the
// automations editor with sleep after streaming, and swipe typing. The agent does the work (MarkIIIHost.Batch2.cs) and
// Core's Decky plugin shows a summary in Quick Access; this page edits config.json and stores the MQTT password with
// DPAPI (Shared/CoreSecretStore.cs), never in config.json.
public sealed partial class MainWindow
{
    private TextBox _haHostBox = new();
    private TextBox _haPortBox = new();
    private TextBox _haUserBox = new();
    private PasswordBox _haPasswordBox = new();
    private readonly TextBlock _haStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private ComboBox _streamSleepCombo = new();
    private readonly StackPanel _automationList = new() { Spacing = 10 };
    private List<CoreAutomationRule> _automationRules = CoreAutomations.Defaults();
    private bool _automationsEdited;
    private bool _buildingAutomations;

    private static readonly ComboOption[] StreamSleepOptions =
    {
        new("5", "5 min"), new("10", "10 min"), new("15", "15 min"), new("20", "20 min"), new("30", "30 min"), new("60", "60 min")
    };

    private static readonly ComboOption[] AutomationTriggerOptions =
    {
        new(CoreAutomations.ControllerConnected, "Quando si collega un controller"),
        new(CoreAutomations.Wake, "Quando Core si risveglia"),
        new(CoreAutomations.GameStart, "Quando parte un gioco"),
        new(CoreAutomations.GameStop, "Quando finisce un gioco"),
        new(CoreAutomations.StreamOn, "Quando inizia uno streaming"),
        new(CoreAutomations.StreamOff, "Quando finisce uno streaming"),
    };

    private static readonly (string Key, string Label)[] AutomationActionLabels =
    {
        (CoreAutomations.TvWake, "Accendi la TV e passa a Core"),
        (CoreAutomations.DisplayRestore, "Ripristina lo schermo normale"),
        (CoreAutomations.SoundRestore, "Ripristina l'uscita audio normale"),
        (CoreAutomations.StreamStart, "Lascia stare la TV (modalità streaming)"),
        (CoreAutomations.StreamStop, "Restituisci la TV"),
        (CoreAutomations.HaEvent, "Avvisa Home Assistant"),
    };

    // ---------- Home Assistant (MQTT) ----------

    private FluentCard BuildHomeAssistantCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE80F).ToString(), "Home Assistant",
            "Core pubblica il suo stato con MQTT: gioco in corso, film in riproduzione, inattivo, in sospensione, modalità streaming e VR. Home Assistant può accendere la TV, avviare la modalità streaming e mettere il PC in sospensione. L'aggiornamento di Steam resta solo sul PC."));
        AddExplainedToggle(card, "Collega Core a Home Assistant",
            "Le entità compaiono da sole in Home Assistant (rilevamento MQTT) sotto il dispositivo Core.", "haEnabled");
        _haHostBox = TextBox("serverv3");
        _haPortBox = TextBox("1883");
        _haUserBox = TextBox("core");
        _haPasswordBox = new PasswordBox { PlaceholderText = T("Password"), PasswordRevealMode = PasswordRevealMode.Hidden, MinWidth = 220 };
        card.Children.Add(TwoColumn(Labeled("Server MQTT", _haHostBox), Labeled("Porta", _haPortBox)));
        card.Children.Add(TwoColumn(Labeled("Nome utente", _haUserBox), Labeled("Password", _haPasswordBox)));
        card.Children.Add(Body("La password resta su questo PC, cifrata per il tuo utente di Windows. Non finisce mai in config.json né nei log."));
        card.Children.Add(ActionRow(
            Button("Salva la password", SaveHaPasswordAsync, primary: true),
            Button("Collegati ora", ReconnectHomeAssistantAsync)));
        card.Children.Add(_haStatus);
        foreach (var box in new[] { _haHostBox, _haPortBox, _haUserBox }) box.LostFocus += (_, _) => AutoSaveGaming();
        return card;
    }

    private async Task SaveHaPasswordAsync()
    {
        try
        {
            CoreSecretStore.SavePassword(CoreSecretStore.DefaultDirectory, _haPasswordBox.Password);
            _haPasswordBox.Password = "";
            _haStatus.Text = T("Password salvata.");
        }
        catch (Exception error)
        {
            // The type only: the message could carry a path, never the password, but nothing else is needed.
            Diag.Crash("SaveHaPassword", error.GetType().Name);
            _haStatus.Text = T("Non riesco a salvare la password.");
            return;
        }
        await ReconnectHomeAssistantAsync();
    }

    private async Task ReconnectHomeAssistantAsync()
    {
        await SaveGamingConfigAsync();
        await AgentAsync(HttpMethod.Post, "/core/bridge/reconnect", TimeSpan.FromSeconds(4));
        await Task.Delay(2500);
        await RefreshHomeAssistantStatusAsync();
    }

    private async Task RefreshHomeAssistantStatusAsync()
    {
        var reply = await AgentAsync(HttpMethod.Get, "/core/bridge", TimeSpan.FromSeconds(4));
        if (reply is not { } bridge) { _haStatus.Text = T("Il servizio Gaming Mode non risponde."); return; }
        string state = bridge.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        string host = bridge.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "";
        bool saved = bridge.TryGetProperty("passwordSaved", out var p) && p.ValueKind == JsonValueKind.True;
        string line = state switch
        {
            "connected" => string.Format(CultureInfo.CurrentCulture, T("Collegato a {0}."), host),
            "refused" => T("Nome utente o password rifiutati dal server MQTT."),
            "unreachable" => string.Format(CultureInfo.CurrentCulture, T("Non riesco a raggiungere {0}."), host),
            "not-configured" => T("Inserisci il nome del server MQTT."),
            "off" => T("Collegamento spento."),
            _ => T("Collegamento in corso…"),
        };
        _haStatus.Text = saved || state == "off" ? line : line + " " + T("Nessuna password salvata.");
    }

    // ---------- automations (and sleep after streaming) ----------

    private FluentCard BuildAutomationsCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE945).ToString(), "Automazioni",
            "Quando succede qualcosa, Core fa qualcosa. Il riepilogo è in Accesso rapido > Core."));
        card.Children.Add(Body("La TV al risveglio segue l'opzione \"Accendi all'avvio del PC\" di LG webOS: una regola \"Quando Core si risveglia: accendi la TV\" usa la stessa strada, e la TV non viene mai accesa due volte di seguito."));
        card.Children.Add(_automationList);
        card.Children.Add(ActionRow(
            Button("Aggiungi una regola", AddAutomationRule),
            Button("Torna alle regole predefinite", ResetAutomationRules)));
        AddExplainedToggle(card, "Sospensione dopo lo streaming",
            "Quando lo streaming verso una console portatile finisce e nessuno usa Core qui, il PC va in sospensione dopo il tempo scelto. Il gioco resta sospeso in memoria; la console portatile risveglia il PC con Wake-on-LAN (\"Risveglia PC\" in Moonlight). La TV resta com'è.", "streamSleep");
        _streamSleepCombo = ChoiceCombo(StreamSleepOptions);
        _streamSleepCombo.SelectionChanged += (_, _) => AutoSaveGaming();
        card.Children.Add(Labeled("Dopo", _streamSleepCombo));
        card.Children.Add(Body("Per Wake-on-LAN collega il PC con un cavo di rete e attiva \"Wake on Magic Packet\" nella scheda di rete. Accesso rapido > Core > Salute dice se è pronto."));
        AddExplainedToggle(card, "Scrittura a scorrimento sulla tastiera di Steam",
            "Scrivi scorrendo sui trackpad, con suggerimenti e correzione automatica (basato su VaporType). Spento torna la tastiera normale di Steam.", "swipeTyping");
        return card;
    }

    private void RebuildAutomationList()
    {
        _buildingAutomations = true;
        try
        {
            _automationList.Children.Clear();
            foreach (CoreAutomationRule rule in _automationRules) _automationList.Children.Add(AutomationRow(rule));
            if (_automationRules.Count == 0) _automationList.Children.Add(Body("Nessuna regola."));
        }
        finally { _buildingAutomations = false; }
    }

    private FrameworkElement AutomationRow(CoreAutomationRule rule)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.35 } };
        var enabled = new ToggleSwitch { IsOn = rule.Enabled, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        ApplyToggleStateText(enabled);
        var trigger = ChoiceCombo(AutomationTriggerOptions);
        SelectComboKey(trigger, rule.Trigger);
        var remove = Button("Rimuovi", () => { _automationRules.Remove(rule); AutomationsChanged(); RebuildAutomationList(); });
        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(trigger, 1);
        Grid.SetColumn(remove, 2);
        top.Children.Add(enabled);
        top.Children.Add(trigger);
        top.Children.Add(remove);
        panel.Children.Add(top);
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 260 };
        var eventBox = TextBox("game-start");
        eventBox.Text = rule.Event ?? "";
        foreach ((string key, string label) in AutomationActionLabels)
        {
            var box = new CheckBox { Content = T(label), IsChecked = rule.Actions.Contains(key) };
            box.Checked += (_, _) => { if (!rule.Actions.Contains(key)) rule.Actions.Add(key); AutomationsChanged(); };
            box.Unchecked += (_, _) => { rule.Actions.Remove(key); AutomationsChanged(); };
            actions.Children.Add(box);
        }
        panel.Children.Add(actions);
        panel.Children.Add(Labeled("Evento di Home Assistant", eventBox));
        enabled.Toggled += (_, _) => { rule.Enabled = enabled.IsOn; AutomationsChanged(); };
        trigger.SelectionChanged += (_, _) => { rule.Trigger = GetComboKey(trigger) ?? rule.Trigger; AutomationsChanged(); };
        eventBox.LostFocus += (_, _) => { rule.Event = CoreAutomations.CleanEvent(eventBox.Text); eventBox.Text = rule.Event ?? ""; AutomationsChanged(); };
        return panel;
    }

    private void AutomationsChanged()
    {
        if (_buildingAutomations) return;
        _automationsEdited = true;
        AutoSaveGaming();
    }

    private void AddAutomationRule()
    {
        if (_automationRules.Count >= CoreAutomations.MaxRules) { SetStatus(T("Al massimo 20 regole."), InfoBarSeverity.Warning); return; }
        var ids = new HashSet<string>(_automationRules.Select(rule => rule.Id), StringComparer.Ordinal);
        _automationRules.Add(new CoreAutomationRule { Id = CoreAutomations.NewId(ids, CoreAutomations.GameStart), Enabled = false, Trigger = CoreAutomations.GameStart, Actions = { CoreAutomations.HaEvent }, Event = "game-start" });
        AutomationsChanged();
        RebuildAutomationList();
    }

    private void ResetAutomationRules()
    {
        _automationRules = CoreAutomations.Defaults();
        _automationsEdited = true;
        RebuildAutomationList();
        AutoSaveGaming();
    }

    // ---------- config.json ----------

    private void ReadBatchTwoIntoConfig()
    {
        var gaming = _gamingConfig.Gaming;
        gaming.HomeAssistantEnabled = GetToggle("haEnabled");
        gaming.HomeAssistantHost = string.IsNullOrWhiteSpace(_haHostBox.Text) ? "serverv3" : _haHostBox.Text.Trim();
        gaming.HomeAssistantPort = int.TryParse(_haPortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535 ? port : 1883;
        gaming.HomeAssistantUser = string.IsNullOrWhiteSpace(_haUserBox.Text) ? null : _haUserBox.Text.Trim();
        gaming.StreamSleepEnabled = GetToggle("streamSleep");
        if (int.TryParse(GetComboKey(_streamSleepCombo), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)) gaming.StreamSleepMinutes = minutes;
        gaming.SwipeTypingEnabled = GetToggle("swipeTyping");
        // Untouched rules stay as saved (null = the defaults, so future defaults still apply).
        if (_automationsEdited) gaming.Automations = CoreAutomations.Normalize(_automationRules).Select(rule => rule.Copy()).ToList();
    }

    private void WriteBatchTwoIntoControls()
    {
        var gaming = _gamingConfig.Gaming;
        SetToggle("haEnabled", gaming.HomeAssistantEnabled);
        SetToggle("streamSleep", gaming.StreamSleepEnabled);
        SetToggle("swipeTyping", gaming.SwipeTypingEnabled);
        _haHostBox.Text = string.IsNullOrWhiteSpace(gaming.HomeAssistantHost) ? "serverv3" : gaming.HomeAssistantHost;
        _haPortBox.Text = (gaming.HomeAssistantPort is >= 1 and <= 65535 ? gaming.HomeAssistantPort : 1883).ToString(CultureInfo.InvariantCulture);
        _haUserBox.Text = gaming.HomeAssistantUser ?? "";
        int minutes = gaming.StreamSleepMinutes is >= 1 and <= 120 ? gaming.StreamSleepMinutes : 10;
        string key = StreamSleepOptions.Select(option => option.Key).OrderBy(option => Math.Abs(int.Parse(option, CultureInfo.InvariantCulture) - minutes)).First();
        SelectComboKey(_streamSleepCombo, key);
        _automationRules = CoreAutomations.Normalize(gaming.Automations).Select(rule => rule.Copy()).ToList();
        _automationsEdited = false;
        RebuildAutomationList();
        _ = RefreshHomeAssistantStatusAsync();
    }
}
