using System.ComponentModel;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TecniLauncher.Analytics.Tests
{
    public class PrivacyAndFormatTests
    {
        private static readonly string[] CamposEvento =
            { "schema_version", "event_id", "event_type", "install_id", "session_id", "app_version", "os_family", "occurred_at", "properties" };

        private static readonly Dictionary<string, string[]> PropiedadesPorTipo = new()
        {
            ["launcher_started"] = Array.Empty<string>(),
            ["session_ended"] = new[] { "duration_seconds" },
            ["game_launch_requested"] = new[] { "minecraft_version", "loader_type" },
            ["game_process_started"] = Array.Empty<string>(),
            ["game_launch_failed"] = new[] { "error_category" },
            ["launcher_updated"] = new[] { "previous_version" },
            ["consent_changed"] = new[] { "consent_state" },
        };

        [Fact]
        public async Task Ninguna_peticion_contiene_datos_personales_y_cada_evento_solo_tiene_campos_permitidos()
        {
            const string usuario = "SteveJugador_92";
            const string correo = "steve.jugador@example.com";
            const string tokenMicrosoft = "eyJhbGciOiJSUzI1NiJ9.MS-ACCESS-TOKEN-123";
            const string uuidCuenta = "069a79f444e94726a5befca90e38aaf5";
            const string uuidCuentaGuiones = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
            const string nombrePerfil = "Mi Perfil Secreto";
            string rutaPerfil = $@"C:\Users\{usuario}\AppData\Roaming\.TecniLauncher\Instances\{nombrePerfil}";

            using var env = new Env();
            // El launcher se actualiza de 1.5.4 a 1.5.5 con la analítica activa.
            var antes = env.Create("1.5.4");
            antes.Start();
            antes.EnableAnalytics();
            await Wait.Background(antes);

            var s = env.Create("1.5.5");
            s.Start();

            // Simulación de uso real: el launcher pasa lo que tiene a mano; la librería debe filtrarlo.
            s.GameLaunchRequested("1.21.1", "Fabric");
            s.GameLaunchRequested(nombrePerfil, usuario);           // datos basura en los parámetros
            s.GameLaunchRequested(rutaPerfil, correo);
            s.GameLaunchRequested(null, null);
            s.GameProcessStarted();
            s.GameLaunchFailed(new Win32Exception($"No se encontró {rutaPerfil} para {correo} ({uuidCuenta})"));
            s.GameLaunchFailed(new InvalidOperationException($"token {tokenMicrosoft} de {usuario}") { Data = { ["analytics_category"] = "authentication" } });
            s.GameLaunchFailed(new Exception(Environment.MachineName) { Data = { ["analytics_category"] = usuario } });
            s.GameLaunchFailed(tokenMicrosoft);
            env.Time.Advance(TimeSpan.FromMinutes(42));
            s.SessionEnded();
            await s.FlushEventsAsync();
            await Wait.Background(s);
            Assert.True(await s.DisableAnalytics());

            var peticiones = env.Http.Requests.ToList();
            Assert.Contains(peticiones, r => r.Path.EndsWith("/v1/events"));
            Assert.Contains(peticiones, r => r.Path.EndsWith("/register"));
            Assert.Contains(peticiones, r => r.Path.EndsWith("/revoke"));

            string[] prohibidos =
            {
                usuario, correo, tokenMicrosoft, uuidCuenta, uuidCuentaGuiones, nombrePerfil,
                @"C:\", @"C:\\", "Users", "AppData", Environment.MachineName, Environment.UserName,
            };
            foreach (var r in peticiones)
            {
                Assert.Equal("POST", r.Method);
                Assert.Contains("User-Agent: TecniLauncher-Analytics/1", r.Headers);
                Assert.DoesNotContain("Cookie", r.Headers, StringComparison.OrdinalIgnoreCase);
                foreach (var p in prohibidos)
                {
                    Assert.DoesNotContain(p, r.AllText, StringComparison.OrdinalIgnoreCase);
                    if (r.Authorization != null) Assert.DoesNotContain(p, r.Authorization, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Formato estricto de cada evento.
            var eventos = env.Http.To("/v1/events")
                .SelectMany(r => r.Json["events"]!.AsArray().Select(e => (e!.AsObject(), r.Json))).ToList();
            Assert.NotEmpty(eventos);
            var iso = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$");
            var uuid = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
            foreach (var (ev, lote) in eventos)
            {
                Assert.Equal(new[] { "schema_version", "install_id", "events" }, lote.Select(p => p.Key).ToArray());
                Assert.Equal(CamposEvento.OrderBy(x => x), ev.Select(p => p.Key).OrderBy(x => x));
                Assert.Equal(1, (int)ev["schema_version"]!);
                Assert.Matches(uuid, (string)ev["event_id"]!);
                Assert.Matches(uuid, (string)ev["install_id"]!);
                Assert.Matches(uuid, (string)ev["session_id"]!);
                Assert.Equal((string)lote["install_id"]!, (string)ev["install_id"]!);
                Assert.Equal("windows", (string)ev["os_family"]!);
                Assert.Matches(iso, (string)ev["occurred_at"]!);

                string tipo = (string)ev["event_type"]!;
                Assert.True(PropiedadesPorTipo.ContainsKey(tipo), $"tipo no permitido: {tipo}");
                var props = ev["properties"]!.AsObject();
                Assert.Equal(PropiedadesPorTipo[tipo].OrderBy(x => x), props.Select(p => p.Key).OrderBy(x => x));
            }

            var tipos = eventos.Select(e => (string)e.Item1["event_type"]!).ToHashSet();
            Assert.Subset(new HashSet<string>(PropiedadesPorTipo.Keys), tipos);
            Assert.Contains("launcher_updated", tipos);
            Assert.Contains("session_ended", tipos);

            var lanzados = eventos.Where(e => (string)e.Item1["event_type"]! == "game_launch_requested")
                .Select(e => ((string)e.Item1["properties"]!["minecraft_version"]!, (string)e.Item1["properties"]!["loader_type"]!)).ToList();
            Assert.Equal(new[] { ("1.21.1", "fabric"), ("other", "other"), ("other", "other"), ("other", "unknown") }, lanzados);

            var fallos = eventos.Where(e => (string)e.Item1["event_type"]! == "game_launch_failed")
                .Select(e => (string)e.Item1["properties"]!["error_category"]!).ToList();
            Assert.Equal(new[] { "process_start_failure", "authentication", "unknown", "unknown" }, fallos);

            // Una sola sesión por ejecución.
            Assert.Single(eventos.Where(e => (string)e.Item1["app_version"]! == "1.5.5").Select(e => (string)e.Item1["session_id"]!).Distinct());
        }

        [Fact]
        public async Task El_estado_local_no_guarda_el_token()
        {
            using var env = new Env();
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            var estado = JsonNode.Parse(File.ReadAllText(env.StateFile))!.AsObject();
            Assert.Equal(
                new[] { "consent", "consent_version", "install_id", "registered", "last_version_seen", "pending_revocation_install_id", "pending_revocation_attempts" }.OrderBy(x => x),
                estado.Select(p => p.Key).OrderBy(x => x));
            Assert.DoesNotContain(env.Secrets.Get(AnalyticsService.TokenKey)!, File.ReadAllText(env.StateFile));
            Assert.Equal("granted", (string)estado["consent"]!);
        }

        // ---------------------------------------------------------------- mapeos

        [Theory]
        [InlineData("Vanilla", "vanilla")]
        [InlineData("Forge", "forge")]
        [InlineData("NeoForge", "neoforge")]
        [InlineData("Fabric", "fabric")]
        [InlineData("Quilt", "quilt")]
        [InlineData("fabric", "fabric")]
        [InlineData(null, "unknown")]
        [InlineData("", "unknown")]
        [InlineData("   ", "unknown")]
        [InlineData("OptiFine", "other")]
        public void LoaderType(string? entrada, string esperado) => Assert.Equal(esperado, AnalyticsMappings.LoaderType(entrada));

        [Theory]
        [InlineData("1.21.1", "1.21.1")]
        [InlineData("1.8.9", "1.8.9")]
        [InlineData("26.1", "26.1")]
        [InlineData("1.21-pre1", "1.21-pre1")]
        [InlineData("1.20.5-rc-2", "1.20.5-rc-2")]
        [InlineData("26.1-snapshot-3", "26.1-snapshot-3")]
        [InlineData("24w14a", "24w14a")]
        [InlineData("1.21.1-fabric", "other")]
        [InlineData("Mi Perfil", "other")]
        [InlineData("1.21.1\n", "other")]
        [InlineData(null, "other")]
        [InlineData("", "other")]
        public void MinecraftVersion(string? entrada, string esperado) => Assert.Equal(esperado, AnalyticsMappings.MinecraftVersion(entrada));

        public static IEnumerable<object[]> Excepciones() => new[]
        {
            new object[] { new HttpRequestException(), "download_failure" },
            new object[] { new Win32Exception(2), "process_start_failure" },
            new object[] { new TimeoutException(), "timeout" },
            new object[] { new TaskCanceledException(), "timeout" },
            new object[] { new System.Text.Json.JsonException(), "invalid_configuration" },
            new object[] { new FormatException(), "invalid_configuration" },
            new object[] { new ArgumentException(), "invalid_configuration" },
            new object[] { new ArgumentNullException("x"), "invalid_configuration" },
            new object[] { new KeyNotFoundException(), "invalid_configuration" },
            new object[] { new InvalidOperationException(), "unknown" },
            new object[] { new Exception(), "unknown" },
            new object[] { new HttpRequestException { Data = { ["analytics_category"] = "java_missing" } }, "java_missing" },
            new object[] { new InvalidOperationException { Data = { ["analytics_category"] = "authentication" } }, "authentication" },
            new object[] { new HttpRequestException { Data = { ["analytics_category"] = "inventada" } }, "download_failure" },
        };

        [Theory]
        [MemberData(nameof(Excepciones))]
        public void ErrorCategory(Exception ex, string esperado) => Assert.Equal(esperado, AnalyticsMappings.ErrorCategory(ex));

        [Fact]
        public void El_token_tiene_43_caracteres_base64url_y_los_ids_son_uuid_v4_en_minusculas()
        {
            for (int i = 0; i < 50; i++)
            {
                Assert.Matches("^[A-Za-z0-9_-]{43}$", AnalyticsService.NewToken());
                Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", AnalyticsService.NewUuid());
            }
        }
    }
}
