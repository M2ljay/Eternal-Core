// Only the network and user-data boundaries are replaced; production sources compile unchanged.
namespace EternalTests
{
    internal static class State
    {
        public static readonly string Root = Path.Combine(Path.GetTempPath(), "eternal-release-tests-" + Guid.NewGuid().ToString("N"));
        public static Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply =
            _ => throw new InvalidOperationException("External network access is forbidden in these tests.");
    }
}

namespace Playhub.Services
{
    public sealed class HttpClient : System.Net.Http.HttpClient
    {
        public HttpClient() : base(new OfflineHandler()) { }
        private sealed class OfflineHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                EternalTests.State.Reply(request);
        }
    }

    public static class AppPaths
    {
        public static string DownloadsRoot => Path.Combine(EternalTests.State.Root, "downloads");
        public static void EnsureRoots() => Directory.CreateDirectory(DownloadsRoot);
    }
}
