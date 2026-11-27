namespace Playhub.Shared;

// Eternal Core release identity: the single source of truth for the fork's version.
// Source/EternalRelease.props reads `Version` out of this file to set <Version> in the
// app and setup projects, so keep the declaration on one line in this exact form.
// Mark N is 3.N.x: the minor number sorts every fork release above upstream Playhub
// 3.0.0, and patch releases of a Mark keep its name (3.1.1 is still Mark I).
internal static class EternalRelease
{
    internal const string Version = "3.3.7";
    internal const string MarkName = "Mark III";

    internal const string AssemblyVersion = Version + ".0";
    internal const string DisplayVersion = MarkName + " (" + Version + ")";

    // GitHub repository whose releases/latest feeds the self-updater. Tags must stay
    // numeric (v3.1.0): PlayhubUpdateService.ParseVersion ignores anything else.
    internal const string UpdateRepository = "M2ljay/Eternal-Core";

    // Value that every Playhub 3.0 install persisted in settings.json.
    internal const string UpstreamUpdateRepository = "LoZazaMastro/Playhub";

    // Name the user sees (window title, installer, shortcuts, Installed apps, taskbar).
    // Internal identifiers keep "Playhub" so upgrades work: install and data folders, the
    // uninstall key, AppUserModelID, executable and assembly names, plugin IDs, pipes.
    // See docs/eternal/REBRAND.md before renaming any of those.
    internal const string DisplayName = "Core";

    // Maker brand (Sony to Core's PlayStation): shown where a publisher is named to the user,
    // as in "Core by Eternal" (About card, Installed apps "Publisher"). Not an identifier.
    internal const string MakerName = "Eternal";
}
