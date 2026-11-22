using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace TecniLauncher.Analytics.Tests
{
    internal sealed record CapturedRequest(string Method, string Path, string Headers, string? Authorization, string Body)
    {
        public JsonObject Json => JsonNode.Parse(Body)!.AsObject();
        public string AllText => Method + " " + Path + "\n" + Headers + "\n" + Body;
    }

    /// <summary>HttpMessageHandler falso que captura todas las peticiones.</summary>
    internal sealed class FakeHandler : HttpMessageHandler
    {
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        public Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; }

        public FakeHandler(Func<CapturedRequest, HttpStatusCode>? responder = null)
        {
            var r = responder ?? DefaultOk;
            Responder = (req, _) => Task.FromResult(new HttpResponseMessage(r(req)) { Content = new StringContent("{}") });
        }

        public static HttpStatusCode DefaultOk(CapturedRequest r) =>
            r.Path.EndsWith("/register") ? HttpStatusCode.Created : HttpStatusCode.OK;

        public void Respond(Func<CapturedRequest, HttpStatusCode> r) =>
            Responder = (req, _) => Task.FromResult(new HttpResponseMessage(r(req)) { Content = new StringContent("{}") });

        public IEnumerable<CapturedRequest> To(string suffix) => Requests.Where(r => r.Path.EndsWith(suffix));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var headers = new StringBuilder();
            foreach (var h in request.Headers.Where(h => h.Key != "Authorization"))
                headers.AppendLine($"{h.Key}: {string.Join(",", h.Value)}");
            if (request.Content != null)
                foreach (var h in request.Content.Headers)
                    headers.AppendLine($"{h.Key}: {string.Join(",", h.Value)}");

            var cap = new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, headers.ToString(),
                request.Headers.Authorization?.ToString(), body);
            Requests.Enqueue(cap);
            return await Responder(cap, ct);
        }
    }

    internal sealed class MemorySecretStore : ISecretStore
    {
        public ConcurrentDictionary<string, string> Values { get; } = new();
        public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => Values[key] = value;
        public void Delete(string key) => Values.TryRemove(key, out _);
    }

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan t) => _now += t;
    }

    /// <summary>Entorno de prueba: carpeta temporal, handler falso, reloj manual y almacén de secretos en memoria.</summary>
    internal sealed class Env : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "tl-analytics-tests", Guid.NewGuid().ToString("N"));
        public FakeHandler Http { get; } = new();
        public MemorySecretStore Secrets { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        private readonly List<AnalyticsService> _servicios = new();

        public Env() => Directory.CreateDirectory(Dir);

        public AnalyticsService Create(string version = "1.5.5", Uri? endpoint = null, bool sinEndpoint = false,
            TimeSpan? timeout = null, int maxQueue = 500)
        {
            var s = new AnalyticsService(new AnalyticsOptions
            {
                Endpoint = sinEndpoint ? null : endpoint ?? new Uri("https://analytics.example.test"),
                AppVersion = version,
                StorageDirectory = Dir,
                RequestTimeout = timeout ?? TimeSpan.FromSeconds(10),
                MaxQueue = maxQueue,
            }, Secrets, Time, Http);
            _servicios.Add(s);
            return s;
        }

        public string StateFile => Path.Combine(Dir, "analytics.json");
        public string QueueFile => Path.Combine(Dir, "analytics-queue.json");

        public void Dispose()
        {
            foreach (var s in _servicios) s.Dispose();
            try { Directory.Delete(Dir, true); } catch { }
        }
    }

    internal static class Wait
    {
        public static async Task Background(AnalyticsService s, int ms = 15000)
        {
            var t = s.BackgroundTask;
            var r = await Task.WhenAny(t, Task.Delay(ms));
            Assert.True(r == t, "Las tareas en segundo plano no terminaron a tiempo");
        }
    }
}
