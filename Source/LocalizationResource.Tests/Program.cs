using Playhub.Services;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Localization");
// Only the test output copies are modified, before the loader fills its caches.
foreach (var language in new[] { "en", "it" })
{
    var path = Path.Combine(directory, language + ".json");
    var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    data["Fixture.InvalidResource"] = language == "en" ? "{0} games" : "{1} giochi";
    data["Fixture.Count"] = language == "en" ? "Count {0}" : "Totale {0}";
    data["Fixture {0}"] = language == "en" ? "Fixture {0}" : "Fixture {1}";
    File.WriteAllText(path, data.ToJsonString());
}

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var english = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "en.json")))!.AsObject();
var pcKeys = english.Select(pair => pair.Key).Where(key =>
    key.StartsWith("SteamGridDb.", StringComparison.Ordinal) ||
    key.StartsWith("GamingMode.Optional.", StringComparison.Ordinal) ||
    key is "Import.Welcome.Pc" or "Import.CloseRunningGame").ToArray();
Check(pcKeys.Contains("Import.Welcome.Pc") && pcKeys.Contains("SteamGridDb.Connect") && pcKeys.Contains("GamingMode.Optional.Install"),
    "Actual PC onboarding, artwork connection and optional Gaming Mode resources are covered");

static string[] Tokens(string value) => Regex.Matches(value, @"(?<!\{)\{\d+(?:,-?\d+)?(?::[^{}]*)?\}(?!\})")
    .Select(match => match.Value).OrderBy(token => token, StringComparer.Ordinal).ToArray();

foreach (var language in LocalizationService.Languages)
{
    var data = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, language.Key + ".json")))!.AsObject();
    foreach (var key in pcKeys)
    {
        var translated = data[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(translated) || LocalizationService.Translate(language.Key, key) != translated)
            throw new Exception($"PC resource is absent or rejected: {language.Key}/{key}");
        if (!Tokens(translated).SequenceEqual(Tokens(english[key]!.GetValue<string>())))
            throw new Exception($"PC resource format token mismatch: {language.Key}/{key}");
    }
    Check(true, $"{language.Key}: {pcKeys.Length} PC resources load with matching placeholders");
    Check(LocalizationService.ResolveLanguage(language.Key + "-XX") == language.Key,
        language.Key + " regional language key resolves correctly");
}

Check(LocalizationService.Translate("it", "Fixture.InvalidResource") == "Fixture.InvalidResource" &&
    LocalizationService.Translate("it", "Fixture {0}") == "Fixture {0}", "Mismatched identifier and literal placeholders are rejected");
Check(LocalizationService.Format("it", "Fixture.Count", 48) == "Totale 48" &&
    LocalizationService.Format("en", "Fixture.Count", 48) == "Count 48", "Valid indexed resources format in the selected language");
Check(LocalizationService.Translate("en", "Fixture.UnmappedResource") == "Fixture.UnmappedResource" &&
    LocalizationService.Translate("it", null) == "" && LocalizationService.Translate("en", " ") == " ",
    "Unknown, null and blank translations retain their documented fallback");
// Eternal Core Mark II: anti-cheat safe mode and Dashboard controller-chord strings
// (MainWindow.xaml.cs) must exist in every language, or they fall back to Italian.
string[] eternalMarkII =
{
    "Protezione anti-cheat",
    "Alcuni anti-cheat bloccano i giochi quando Gaming Mode interviene su tastiera, finestre o processi.",
    "Modalit\u00e0 sicura anti-cheat",
    "Mentre \u00e8 aperto un gioco con anti-cheat, Gaming Mode sospende tasti del volume, finestre senza bordi, cursore nascosto e gestione energetica del gioco. Riprendono quando chiudi il gioco.",
    "Tratta ogni gioco come protetto",
    "Sospende le stesse funzioni con qualunque gioco, anche quando non viene riconosciuto un anti-cheat.",
    "Un eseguibile per riga, ad esempio cod.exe",
    "Giochi sempre protetti",
    "Il registro di Gaming Mode (%APPDATA%\\GamingMode\\agent.log) indica quando la protezione si attiva e perch\u00e9.",
    "Apri la Dashboard con il controller",
    "Core non legge il controller: lo gestisce Steam. Assegna in Steam un accordo del tasto Guida alla scorciatoia da tastiera della Dashboard.",
    "1. Apri gli accordi del tasto Guida con il pulsante qui sotto, oppure in Steam: Impostazioni > Controller > Layout accordi del tasto Guida > Modifica.",
    "2. Scegli un tasto libero, ad esempio Y, e assegnagli il tasto P della tastiera.",
    "3. Nelle opzioni di quel comando aggiungi Ctrl sinistro e Alt sinistro come comandi extra, cos\u00ec un'unica pressione invia Ctrl + Alt + P.",
    "4. Tieni premuto il tasto Guida e premi Y: la Dashboard si apre, anche durante il gioco.",
    "Apri gli accordi del tasto Guida in Steam",
    "Steam non ha aperto gli accordi del tasto Guida. Seguili da Impostazioni > Controller.",
    "Scorciatoia attuale: {0}",
    "La scorciatoia da tastiera della Dashboard \u00e8 disattivata."
};
foreach (var language in LocalizationService.Languages.Where(language => language.Key != "it"))
{
    foreach (var key in eternalMarkII)
    {
        var translated = LocalizationService.Translate(language.Key, key);
        if (string.IsNullOrWhiteSpace(translated) || translated == key)
            throw new Exception($"Mark II string is not translated: {language.Key}/{key}");
        if (!Tokens(translated).SequenceEqual(Tokens(key)))
            throw new Exception($"Mark II string format token mismatch: {language.Key}/{key}");
    }
    var shortcut = LocalizationService.Translate(language.Key, "Scorciatoia attuale: Ctrl+Alt+P");
    Check(shortcut.Contains("Ctrl+Alt+P") && !shortcut.StartsWith("Scorciatoia", StringComparison.Ordinal),
        $"{language.Key}: {eternalMarkII.Length} Mark II strings translate, and the formatted shortcut line too");
}
Check(eternalMarkII.All(key => LocalizationService.Translate("it", key) == key), "Mark II Italian source strings render as written");

// Eternal Core Mark III: Gaming Mode page strings (MainWindow.MarkIII.cs and the Steam updates card),
// new ones and the upstream TV-card strings they reuse, must exist in every language.
string[] eternalMarkIII =
{
    "Windows tranquillo",
    "Spegne le interruzioni di Windows che possono comparire sulla TV. Ogni modifica viene salvata prima e si può annullare da qui.",
    "Nascondi gli avvisi di Windows in Gaming Mode",
    "Niente banner di Windows sulla TV; tornano sul desktop e restano nel centro notifiche. Gli avvisi di Steam e Decky non cambiano.",
    "Gli aggiornamenti di Windows aspettano la notte: quando serve un riavvio, Accesso rapido > Core propone di riavviare alle 5:00.",
    "Annulla le modifiche",
    "Non riesco ad applicare le modifiche.",
    "Applicato. Windows ha chiesto i permessi di amministratore per le impostazioni del PC.",
    "Le impostazioni dell'utente sono applicate; quelle del PC richiedono i permessi di amministratore.",
    "Modifiche annullate.",
    "Le impostazioni dell'utente sono annullate; quelle del PC richiedono i permessi di amministratore.",
    "Nessuna schermata \"Completiamo la configurazione\" dopo gli aggiornamenti",
    "Niente consigli, suggerimenti e pubblicità di Windows",
    "Niente finestre \"Il programma ha smesso di funzionare\"",
    "Niente finestre Tasti permanenti, filtro e interruttore",
    "Nessun riavvio per gli aggiornamenti mentre sei connesso",
    "Windows Update non cambia il driver grafico",
    "Niente risvegli notturni per manutenzione",
    "Nessuna password al risveglio",
    "Niente richieste di Edge al primo avvio o come browser predefinito",
    "Sequenza di avvio",
    "All'accensione e al risveglio mostra il logo Eternal e poi quello di Core prima della Big Picture. Un tasto qualsiasi del controller la salta.",
    "Mostra la sequenza di avvio",
    "Circa sette secondi su sfondo nero. Non ritarda mai Steam: se qualcosa non va, la Gaming Mode parte comunque.",
    "Suono di avvio",
    "Il suono arcade che accompagna la sequenza.",
    "Controllo della TV",
    "Core accende la TV e passa al suo ingresso quando entra in Gaming Mode, e la spegne quando va in sospensione o si spegne.",
    "Spegni la TV con Core",
    "Quando Core va in sospensione o si spegne, spegne la TV solo se sta mostrando Core. Se qualcuno guarda altro, la TV resta accesa.",
    "Cerca la TV",
    "Sulla TV attiva Impostazioni > Generali > Mobile TV On > Accendi tramite Wi-Fi, e Quick Start+. Al primo abbinamento accetta la richiesta sulla TV con il telecomando.",
    "Il servizio Gaming Mode non risponde.",
    "TV trovata: {0}",
    "Nessuna TV LG trovata in rete. Inserisci l'indirizzo IP a mano.",
    "Inserisci prima l'indirizzo IP della TV.",
    "Accetta la richiesta sulla TV con il telecomando.",
    "TV abbinata.",
    "Fatto.",
    "Abbina prima la TV.",
    "Abbinamento rifiutato sulla TV.",
    "La richiesta sulla TV non \u00e8 stata accettata in tempo.",
    "La TV non risponde. Controlla che sia accesa e sulla stessa rete.",
    "Non riesco a controllare la TV.",
    "Aggiorna Steam ora",
    "Apri Core Repair",
    "Se i plugin di Big Picture non si caricano, Core lo dice con un breve avviso. Core Repair, nella libreria di Steam, li ripara usando il controller, anche senza Decky.",
    "Avviso se i plugin non si caricano",
    "Un messaggio breve sopra Big Picture, mai durante un gioco. Steam e i giochi funzionano comunque.",
    "Un aggiornamento \u00e8 gi\u00e0 in corso.",
    "Aggiornamento di Steam avviato. Core salva prima una copia di Steam e torna tra qualche minuto.",
    "Steam resta a questa versione ancora per {0} giorni, o finch\u00e9 scegli di aggiornarlo.",
    "Trenta giorni passati: Core aggiorner\u00e0 Steam la prossima volta che non stai giocando.",
    "Steam si aggiorna da solo, come al solito.",
    "Core tiene ferma la versione di Steam finch\u00e9 scegli tu, al massimo per 30 giorni. Prima di aggiornare salva una copia di Steam e controlla che i plugin tornino; se si rompono puoi tornare indietro.",
    "Trattieni gli aggiornamenti di Steam",
    "Accensione via Wake-on-LAN, spegnimento e HDMI tramite abbinamento locale webOS.",
    "Indirizzo IP",
    "Indirizzo MAC",
    "Ingresso HDMI",
    "Accendi all'avvio del PC",
    "Passa a questo HDMI all'avvio",
    "Abbina",
    "Accendi ora",
    "Passa a HDMI",
    "Spegni"
};
foreach (var language in LocalizationService.Languages.Where(language => language.Key != "it"))
{
    foreach (var key in eternalMarkIII)
    {
        var translated = LocalizationService.Translate(language.Key, key);
        if (string.IsNullOrWhiteSpace(translated) || translated == key)
            throw new Exception($"Mark III string is not translated: {language.Key}/{key}");
        if (!Tokens(translated).SequenceEqual(Tokens(key)))
            throw new Exception($"Mark III string format token mismatch: {language.Key}/{key}");
    }
    Check(true, $"{language.Key}: {eternalMarkIII.Length} Mark III strings translate");
}
Check(eternalMarkIII.All(key => LocalizationService.Translate("it", key) == key), "Mark III Italian source strings render as written");

// Eternal Core rebrand (docs/eternal/REBRAND.md): the app is called Core everywhere the user reads it.
// "Playhub" stays only where it names something that still carries that name: the upstream project in
// the credits and the upstream Playhub-* plugin suite. Since rebrand phase 2 the app file is Core.exe, so
// "Playhub.exe" counts as a missed string too. Any other product-name use is a missed string.
var upstreamName = new Regex(@"(?<![A-Za-z0-9_./\\])Playhub(?!\.(?!exe\b)[A-Za-z]|[A-Za-z0-9_/\\]| (?:Metadata|Artworks?|Notifications|Surround|Discord)\b)");
var keptOnPurpose = new Regex(@"plugin di Playhub|suite Playhub|plugin Playhub"
    + @"|^Core di Eternal\. Basato su Playhub di Andrea Sgarro");
const string credit = "Core di Eternal. Basato su Playhub di Andrea Sgarro (LoZazaMastro), licenza MIT. © 2026 Andrea Sgarro (LoZazaMastro).";
foreach (var language in LocalizationService.Languages)
{
    var data = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, language.Key + ".json")))!.AsObject();
    foreach (var (key, value) in data)
    {
        if (keptOnPurpose.IsMatch(key)) continue;
        var text = value!.GetValue<string>();
        if (upstreamName.IsMatch(key) || upstreamName.IsMatch(text))
            throw new Exception($"Rebrand: '{language.Key}/{key}' still calls the app Playhub: {text}");
    }
    var translatedCredit = LocalizationService.Translate(language.Key, credit);
    if (language.Key != "it" && translatedCredit == credit)
        throw new Exception($"Rebrand: About credit is not translated: {language.Key}");
    Check(translatedCredit.Contains("Core") && translatedCredit.Contains("Eternal") && translatedCredit.Contains("Playhub") && translatedCredit.Contains("Andrea Sgarro (LoZazaMastro)")
        && translatedCredit.Contains("MIT") && translatedCredit.Contains("© 2026"),
        $"{language.Key}: the app is called Core, and the About card says Core by Eternal, based on Playhub by Andrea Sgarro under MIT");
}
// The support card and the donation reminder (Ko-fi link to the upstream author) were removed from Core;
// their strings must not come back in the code table or in any language file.
string[] removedSupport =
{
    "Supporto", "Fai una donazione", "Ti piace Playhub?", "Grazie per essere parte di Playhub",
    "Playhub è gratuito e open source. Lo sviluppo, i test e la manutenzione sono sostenuti da una sola persona.",
    "Playhub cresce grazie alla community e al tempo dedicato al progetto.",
    "Playhub è sviluppato e mantenuto da una sola persona.",
    "Se ti va di sostenerlo, anche una piccola donazione significa molto per me."
};
foreach (var language in LocalizationService.Languages)
{
    var data = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, language.Key + ".json")))!.AsObject();
    Check(removedSupport.All(key => LocalizationService.Translate(language.Key, key) == key && !data.ContainsKey(key))
        && !data.Any(pair => pair.Key.StartsWith("Se Playhub ti è utile", StringComparison.Ordinal)),
        $"{language.Key}: no support or donation strings remain");
}
Console.WriteLine($"{passed} PC and generic localization checks passed.");
