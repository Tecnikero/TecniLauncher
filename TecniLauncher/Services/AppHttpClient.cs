using System.Net.Http;

namespace TecniLauncher.Services
{
    public static class AppHttpClient
    {
        public static readonly HttpClient Instance;

        static AppHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            };

            Instance = new HttpClient(handler);
            Instance.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0 (Johan/t3cnikero)");
            Instance.Timeout = TimeSpan.FromSeconds(30);
        }
    }
}
