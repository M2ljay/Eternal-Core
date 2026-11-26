using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

/// <summary>
/// Eternal Core Mark III: headless "Update Decky" for Core Repair. The Gaming Mode agent
/// starts <c>Playhub.exe decky-update &lt;id&gt;</c> and waits for the receipt; the install
/// itself is the app's own verified Decky installer, unchanged.
/// </summary>
public static class CoreRepairDeckyCommand
{
    public static async Task RunAsync(Guid requestId)
    {
        var folder = Path.Combine(AppPaths.LocalDataRoot, "CoreRepair");
        Directory.CreateDirectory(folder);
        var receipt = Path.Combine(folder, requestId.ToString("N") + ".json");
        object result;
        try
        {
            var message = await new DeckyInstallerService().InstallOrUpdateLatestAsync();
            result = new { ok = true, message };
        }
        catch (Exception error)
        {
            Diag.Crash("Core Repair Decky update", error);
            result = new { ok = false, message = error.Message };
        }
        var temporary = receipt + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result));
        File.Move(temporary, receipt, true);
    }
}
