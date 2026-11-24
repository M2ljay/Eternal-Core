namespace Playhub.Shared;

// Eternal Core release identity: the single source of truth for the fork's version.
// Source/EternalRelease.props reads `Version` out of this file to set <Version> in the
// app and setup projects, so keep the declaration on one line in this exact form.
// Mark N is 3.N.x: the minor number sorts every fork release above upstream Playhub
// 3.0.0, and patch releases of a Mark keep its name (3.1.1 is still Mark I).
internal static class EternalRelease
{
    internal const string Version = "3.1.0";
    internal const string MarkName = "Mark I";

    internal const string AssemblyVersion = Version + ".0";
    internal const string DisplayVersion = MarkName + " (" + Version + ")";

    // GitHub repository whose releases/latest feeds the self-updater. Tags must stay
    // numeric (v3.1.0): PlayhubUpdateService.ParseVersion ignores anything else.
    internal const string UpdateRepository = "M2ljay/Eternal-Core";

    // Value that every Playhub 3.0 install persisted in settings.json.
    internal const string UpstreamUpdateRepository = "LoZazaMastro/Playhub";
}
