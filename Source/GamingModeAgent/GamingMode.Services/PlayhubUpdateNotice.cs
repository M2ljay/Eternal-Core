using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Services;
using Playhub.Shared;

namespace GamingMode.Services;

// One shared, bounded check serves every Big Picture window and entry.
internal sealed class PlayhubUpdateNotice
{
    readonly object gate = new();
    readonly Func<Task<PlayhubUpdateService.UpdateInfo?>> check;
    readonly Func<long> now;
    Task<PlayhubUpdateService.UpdateInfo?>? pending;
    long checkedAt;
    internal PlayhubUpdateNotice(Func<Task<PlayhubUpdateService.UpdateInfo?>> check, Func<long> now)
    { this.check = check; this.now = now; }
    internal Task<PlayhubUpdateService.UpdateInfo?> Read()
    {
        lock (gate)
        {
            long tick = now();
            if (pending is null || (pending.IsCompleted && tick >= checkedAt && tick - checkedAt >= 30 * 60 * 1000))
            { checkedAt = tick; pending = ReadSafe(); }
            return pending;
        }
    }
    // Core Health: the last finished check, without waiting. Starts a check only when Read would
    // (none yet, or the last one is over 30 minutes old), so opening Quick Access costs no request.
    internal (bool Known, PlayhubUpdateService.UpdateInfo? Info) Peek()
    {
        Task<PlayhubUpdateService.UpdateInfo?> current = Read();
        return current.IsCompletedSuccessfully ? (true, current.Result) : (false, null);
    }
    // The agent's one shared instance, set by Map (Core Health reads it).
    internal static PlayhubUpdateNotice? Shared { get; private set; }
    async Task<PlayhubUpdateService.UpdateInfo?> ReadSafe()
    { try { return await check().ConfigureAwait(false); } catch { return null; } }
    internal static void Map(WebApplication app, XboxShellBroker auth)
    {
        string current = EternalRelease.Version;
        var service = new PlayhubUpdateService();
        var notice = new PlayhubUpdateNotice(() => service.CheckAsync(EternalRelease.UpdateRepository, current), () => Environment.TickCount64);
        Shared = notice;
        app.MapPost("/playhub/update", (Func<HttpContext, Task<IResult>>)(async context =>
        {
            if (!auth.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                return Results.StatusCode(401);
            var info = await notice.Read().ConfigureAwait(false);
            return Results.Json(new { available = info?.IsNewer == true && !string.IsNullOrWhiteSpace(info.DownloadUrl),
                version = info?.LatestVersion, currentVersion = current });
        }));
    }
}
