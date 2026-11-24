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
    "Il registro di Gaming Mode (%APPDATA%\\GamingMode\\agent.log) indica quando la protezione si attiva e perch\u00e9."
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
    Check(true, $"{language.Key}: {eternalMarkII.Length} Mark II strings translate");
}
Check(eternalMarkII.All(key => LocalizationService.Translate("it", key) == key), "Mark II Italian source strings render as written");
Console.WriteLine($"{passed} PC and generic localization checks passed.");
