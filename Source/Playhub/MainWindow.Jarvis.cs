using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Playhub.Services;
using Playhub.Shared;

namespace Playhub;

// Jarvis on the Gaming Mode page (docs/eternal/JARVIS.md): which Jarvis answers (Home Assistant's agent or the Claude API
// with the user's own key), the token and keys (DPAPI-sealed in jarvis-secrets.json, Shared/JarvisSecretStore.cs, never
// in config.json or a log), "Test connection" (free checks only: no conversation is started, nothing is billed), the
// three controller combos, spoiler-safe mode and the voice. The agent does the work (MarkIIIHost.Jarvis.cs) and Core's
// Decky plugin shows the chat in Quick Access.
public sealed partial class MainWindow
{
    private ComboBox _jarvisBackendCombo = new();
    private TextBox _jarvisHaUrlBox = new();
    private TextBox _jarvisHaAgentBox = new();
    private TextBox _jarvisModelBox = new();
    private PasswordBox _jarvisTokenBox = new();
    private PasswordBox _jarvisAnthropicBox = new();
    private PasswordBox _jarvisOpenAiBox = new();
    private TextBox _jarvisDictateBox = new();
    private TextBox _jarvisTalkBox = new();
    private TextBox _jarvisScreenBox = new();
    private readonly TextBlock _jarvisStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    private static readonly ComboOption[] JarvisBackendOptions =
    {
        new(JarvisConfig.BackendHomeAssistant, "Jarvis di Home Assistant (consigliato)"),
        new(JarvisConfig.BackendClaude, "API di Claude diretta"),
    };

    private FluentCard BuildJarvisCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE720).ToString(), "Jarvis",
            "Jarvis in Core: chat in Accesso rapido, aiuto con quello che c'è sullo schermo e dettatura con il microfono. Non viene inviato niente finché non lo chiedi."));
        AddExplainedToggle(card, "Attiva Jarvis", "La sezione Jarvis in Accesso rapido e le combinazioni del controller.", "jarvisEnabled");
        _jarvisBackendCombo = ChoiceCombo(JarvisBackendOptions);
        _jarvisBackendCombo.SelectionChanged += (_, _) => AutoSaveGaming();
        card.Children.Add(Labeled("Chi risponde", _jarvisBackendCombo));
        card.Children.Add(Body("Jarvis di Home Assistant ha la stessa memoria e la stessa personalità degli altoparlanti e può comandare la casa. L'API diretta usa la tua chiave di Anthropic e non comanda la casa. Serrature, garage e allarme non sono mai disponibili da Core: richiedono sempre la conferma sul telefono."));
        _jarvisHaUrlBox = TextBox(JarvisConfig.DefaultHaUrl);
        _jarvisHaAgentBox = TextBox(JarvisConfig.DefaultHaAgent);
        card.Children.Add(TwoColumn(Labeled("Indirizzo di Home Assistant", _jarvisHaUrlBox), Labeled("Agente di conversazione", _jarvisHaAgentBox)));
        _jarvisTokenBox = Secret("Token di accesso di Home Assistant");
        _jarvisModelBox = TextBox(JarvisConfig.DefaultModel);
        card.Children.Add(TwoColumn(Labeled("Token di accesso di Home Assistant", _jarvisTokenBox), Labeled("Modello", _jarvisModelBox)));
        _jarvisAnthropicBox = Secret("Chiave API di Anthropic");
        _jarvisOpenAiBox = Secret("Chiave per Whisper nel cloud (facoltativa)");
        card.Children.Add(TwoColumn(Labeled("Chiave API di Anthropic", _jarvisAnthropicBox), Labeled("Chiave per Whisper nel cloud (facoltativa)", _jarvisOpenAiBox)));
        card.Children.Add(Body("Il token di Home Assistant si crea nel tuo profilo di Home Assistant > Sicurezza > Token di accesso a lunga durata. L'aiuto sullo schermo usa sempre la chiave di Anthropic, perché Home Assistant non accetta immagini nella conversazione. Token e chiavi restano su questo PC, cifrati per il tuo utente di Windows: non finiscono mai in config.json né nei log."));
        card.Children.Add(ActionRow(
            Button("Salva token e chiavi", SaveJarvisSecretsAsync, primary: true),
            Button("Prova la connessione", TestJarvisAsync),
            Button("Dimentica token e chiavi", ForgetJarvisSecretsAsync)));
        card.Children.Add(_jarvisStatus);
        _jarvisDictateBox = TextBox(JarvisConfig.DefaultDictateCombo);
        _jarvisTalkBox = TextBox(JarvisConfig.DefaultTalkCombo);
        _jarvisScreenBox = TextBox(JarvisConfig.DefaultScreenCombo);
        card.Children.Add(TwoColumn(Labeled("Dettatura (tieni premuto)", _jarvisDictateBox), Labeled("Parla con Jarvis (tieni premuto)", _jarvisTalkBox)));
        card.Children.Add(Labeled("Cosa c'è sullo schermo?", _jarvisScreenBox));
        card.Children.Add(Body("Da due a quattro pulsanti premuti insieme, per esempio L5+R5. Vuoto spegne la combinazione. I quattro pulsanti posteriori dello Steam Controller sono liberi in Steam Input se non li assegni tu."));
        AddExplainedToggle(card, "Modalità senza spoiler", "La prima risposta sullo schermo è solo un indizio; \"Altro\" mostra la risposta completa.", "jarvisSpoiler");
        AddExplainedToggle(card, "Risposte a voce", "Parlando con Jarvis, la risposta si sente con la voce Piper di Home Assistant, oppure con la voce inglese britannica di Windows.", "jarvisVoice");
        AddExplainedToggle(card, "Leggi ad alta voce l'aiuto sullo schermo", "Anche le risposte su quello che c'è sullo schermo si sentono.", "jarvisSpeakScreen");
        AddExplainedToggle(card, "Whisper nel cloud se il server non risponde", "Solo con la chiave per Whisper nel cloud. Di solito la voce si trascrive con Whisper sul server di casa, gratis e in locale.", "jarvisCloudWhisper");
        card.Children.Add(Body("Costi: ogni messaggio a Claude si paga a consumo, sia con l'agente di Home Assistant sia con l'API diretta. Uno screenshot vale circa 1.200 token in più. Trascrizione e voce sul server di casa sono gratis."));
        foreach (var box in new[] { _jarvisHaUrlBox, _jarvisHaAgentBox, _jarvisModelBox, _jarvisDictateBox, _jarvisTalkBox, _jarvisScreenBox }) box.LostFocus += (_, _) => AutoSaveGaming();
        return card;
    }

    private PasswordBox Secret(string placeholder) => new() { PlaceholderText = T(placeholder), PasswordRevealMode = PasswordRevealMode.Hidden, MinWidth = 220 };

    // Only filled boxes are saved: an empty box keeps what was saved before ("Forget" removes everything).
    private async Task SaveJarvisSecretsAsync()
    {
        try
        {
            string directory = CoreSecretStore.DefaultDirectory;
            if (_jarvisTokenBox.Password.Length > 0) JarvisSecretStore.Save(directory, JarvisSecretStore.HaToken, _jarvisTokenBox.Password);
            if (_jarvisAnthropicBox.Password.Length > 0) JarvisSecretStore.Save(directory, JarvisSecretStore.AnthropicKey, _jarvisAnthropicBox.Password);
            if (_jarvisOpenAiBox.Password.Length > 0) JarvisSecretStore.Save(directory, JarvisSecretStore.OpenAiKey, _jarvisOpenAiBox.Password);
            _jarvisTokenBox.Password = _jarvisAnthropicBox.Password = _jarvisOpenAiBox.Password = "";
            _jarvisStatus.Text = T("Salvato.");
        }
        catch (Exception error)
        {
            // The type only: nothing else is needed, and a value must never reach a log.
            Diag.Crash("SaveJarvisSecrets", error.GetType().Name);
            _jarvisStatus.Text = T("Non riesco a salvare.");
            return;
        }
        await SaveGamingConfigAsync();
        await AgentAsync(HttpMethod.Post, "/jarvis/reload", TimeSpan.FromSeconds(4));
    }

    private async Task ForgetJarvisSecretsAsync()
    {
        try
        {
            foreach (string name in new[] { JarvisSecretStore.HaToken, JarvisSecretStore.AnthropicKey, JarvisSecretStore.OpenAiKey })
                JarvisSecretStore.Save(CoreSecretStore.DefaultDirectory, name, null);
            _jarvisStatus.Text = T("Token e chiavi dimenticati.");
        }
        catch (Exception error)
        {
            Diag.Crash("ForgetJarvisSecrets", error.GetType().Name);
            _jarvisStatus.Text = T("Non riesco a salvare.");
        }
        await AgentAsync(HttpMethod.Post, "/jarvis/reload", TimeSpan.FromSeconds(4));
    }

    // Free checks only: Home Assistant's /api/ and the agent's state; the Claude API's model lookup.
    private async Task TestJarvisAsync()
    {
        await SaveGamingConfigAsync();
        var gaming = _gamingConfig.Gaming;
        string directory = CoreSecretStore.DefaultDirectory;
        var lines = new List<string>();
        using var client = new JarvisClient();
        if (JarvisConfig.CleanBackend(gaming.JarvisBackend) == JarvisConfig.BackendHomeAssistant)
        {
            string? url = JarvisConfig.CleanBaseUrl(gaming.JarvisHaUrl);
            string agent = JarvisConfig.CleanEntity(gaming.JarvisHaAgent, "conversation") ?? JarvisConfig.DefaultHaAgent;
            string? token = JarvisSecretStore.Read(directory, JarvisSecretStore.HaToken);
            if (url is null) lines.Add(T("Indirizzo di Home Assistant non valido."));
            else if (token is null) lines.Add(T("Nessun token salvato."));
            else
            {
                JarvisReply reply = await client.HaTestAsync(url, token, agent, TimeSpan.FromSeconds(8));
                lines.Add(reply.Ok ? string.Format(CultureInfo.CurrentCulture, T("Collegato a Home Assistant: l'agente {0} risponde."), agent)
                    : reply.Error switch
                    {
                        "agent-missing" => string.Format(CultureInfo.CurrentCulture, T("Home Assistant risponde, ma non trovo l'agente {0}."), agent),
                        "token-refused" => T("Token rifiutato da Home Assistant."),
                        "not-home-assistant" => T("L'indirizzo non sembra Home Assistant."),
                        _ => T("Non riesco a raggiungere Home Assistant."),
                    });
            }
        }
        string model = JarvisConfig.Model(gaming.JarvisModel);
        string? key = JarvisSecretStore.Read(directory, JarvisSecretStore.AnthropicKey);
        if (key is null) lines.Add(JarvisConfig.CleanBackend(gaming.JarvisBackend) == JarvisConfig.BackendClaude ? T("Nessuna chiave salvata.") : T("Aiuto sullo schermo: nessuna chiave di Anthropic salvata."));
        else
        {
            JarvisReply reply = await client.ClaudeTestAsync(key, model, TimeSpan.FromSeconds(8));
            lines.Add(reply.Ok ? string.Format(CultureInfo.CurrentCulture, T("Chiave di Anthropic valida: modello {0}."), model)
                : reply.Error switch
                {
                    "key-refused" => T("Chiave di Anthropic rifiutata."),
                    "model-unknown" => string.Format(CultureInfo.CurrentCulture, T("Modello sconosciuto: {0}."), model),
                    _ => T("Non riesco a raggiungere l'API di Claude."),
                });
        }
        foreach (string? text in new[] { gaming.JarvisDictateCombo, gaming.JarvisTalkCombo, gaming.JarvisScreenCombo })
            if (!string.IsNullOrWhiteSpace(text) && JarvisCombos.Parse(text) is null) lines.Add(string.Format(CultureInfo.CurrentCulture, T("Combinazione non valida: {0}"), text.Trim()));
        _jarvisStatus.Text = string.Join(" ", lines);
        await AgentAsync(HttpMethod.Post, "/jarvis/reload", TimeSpan.FromSeconds(4));
    }

    // ---------- config.json ----------

    private void ReadJarvisIntoConfig()
    {
        var gaming = _gamingConfig.Gaming;
        gaming.JarvisEnabled = GetToggle("jarvisEnabled");
        gaming.JarvisBackend = JarvisConfig.CleanBackend(GetComboKey(_jarvisBackendCombo));
        gaming.JarvisHaUrl = JarvisConfig.CleanBaseUrl(_jarvisHaUrlBox.Text) ?? JarvisConfig.DefaultHaUrl;
        gaming.JarvisHaAgent = string.IsNullOrWhiteSpace(_jarvisHaAgentBox.Text) ? JarvisConfig.DefaultHaAgent : JarvisConfig.CleanEntity(_jarvisHaAgentBox.Text, "conversation") ?? JarvisConfig.DefaultHaAgent;
        gaming.JarvisModel = JarvisConfig.Model(_jarvisModelBox.Text);
        // A box left empty turns that combo off; text that is not a combo keeps the saved one.
        gaming.JarvisDictateCombo = ComboText(_jarvisDictateBox.Text, gaming.JarvisDictateCombo);
        gaming.JarvisTalkCombo = ComboText(_jarvisTalkBox.Text, gaming.JarvisTalkCombo);
        gaming.JarvisScreenCombo = ComboText(_jarvisScreenBox.Text, gaming.JarvisScreenCombo);
        gaming.JarvisSpoilerSafe = GetToggle("jarvisSpoiler");
        gaming.JarvisVoice = GetToggle("jarvisVoice");
        gaming.JarvisSpeakScreen = GetToggle("jarvisSpeakScreen");
        gaming.JarvisCloudWhisper = GetToggle("jarvisCloudWhisper");
    }

    private static string? ComboText(string text, string? saved) =>
        string.IsNullOrWhiteSpace(text) ? "" : JarvisCombos.Parse(text)?.Text ?? saved;

    private void WriteJarvisIntoControls()
    {
        var gaming = _gamingConfig.Gaming;
        SetToggle("jarvisEnabled", gaming.JarvisEnabled);
        SetToggle("jarvisSpoiler", gaming.JarvisSpoilerSafe);
        SetToggle("jarvisVoice", gaming.JarvisVoice);
        SetToggle("jarvisSpeakScreen", gaming.JarvisSpeakScreen);
        SetToggle("jarvisCloudWhisper", gaming.JarvisCloudWhisper);
        SelectComboKey(_jarvisBackendCombo, JarvisConfig.CleanBackend(gaming.JarvisBackend));
        _jarvisHaUrlBox.Text = JarvisConfig.HaUrl(gaming.JarvisHaUrl);
        _jarvisHaAgentBox.Text = gaming.JarvisHaAgent ?? JarvisConfig.DefaultHaAgent;
        _jarvisModelBox.Text = JarvisConfig.Model(gaming.JarvisModel);
        _jarvisDictateBox.Text = gaming.JarvisDictateCombo ?? JarvisConfig.DefaultDictateCombo;
        _jarvisTalkBox.Text = gaming.JarvisTalkCombo ?? JarvisConfig.DefaultTalkCombo;
        _jarvisScreenBox.Text = gaming.JarvisScreenCombo ?? JarvisConfig.DefaultScreenCombo;
        IReadOnlyDictionary<string, bool> saved = JarvisSecretStore.Saved(CoreSecretStore.DefaultDirectory);
        var missing = new List<string>();
        if (!saved[JarvisSecretStore.HaToken]) missing.Add(T("Nessun token salvato."));
        if (!saved[JarvisSecretStore.AnthropicKey]) missing.Add(T("Aiuto sullo schermo: nessuna chiave di Anthropic salvata."));
        _jarvisStatus.Text = missing.Count == 0 ? T("Token e chiavi salvati.") : string.Join(" ", missing);
    }
}
