using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TecniLauncher.Analytics.Tests
{
    public class AnalyticsServiceTests
    {
        private static readonly Regex UuidV4 = new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

        private static List<JsonObject> SentEvents(FakeHandler h) =>
            h.To("/v1/events").SelectMany(r => r.Json["events"]!.AsArray().Select(e => e!.AsObject())).ToList();

        // ---------------------------------------------------------------- consentimiento

        [Fact]
        public void Consentimiento_desactivado_al_inicio()
        {
            using var env = new Env();
            var s = env.Create();

            Assert.False(s.IsEnabled);
            Assert.True(s.NeedsConsentPrompt);
            Assert.Null(s.InstallId);
            Assert.Empty(env.Secrets.Values);
        }

        [Fact]
        public async Task Sin_consentimiento_Start_no_genera_trafico_ni_cola()
        {
            using var env = new Env();
            var s = env.Create();
            s.Start();
            s.GameLaunchRequested("1.21.1", "Fabric");
            await Wait.Background(s);
            await s.FlushEventsAsync();

            Assert.Empty(env.Http.Requests);
            Assert.Equal(0, s.PendingEventCount);
        }

        [Fact]
        public async Task Sin_endpoint_queda_inactiva_y_no_pide_consentimiento()
        {
            using var env = new Env();
            var s = env.Create(sinEndpoint: true);

            Assert.False(s.IsAvailable);
            Assert.False(s.NeedsConsentPrompt);
            s.Start();
            s.EnableAnalytics();
            s.LauncherStarted();
            s.SessionEnded();
            await s.FlushEventsAsync();
            Assert.True(await s.DisableAnalytics());

            Assert.False(s.IsEnabled);
            Assert.Empty(env.Http.Requests);
            Assert.False(File.Exists(env.StateFile));
            Assert.False(File.Exists(env.QueueFile));
        }

        [Fact]
        public async Task Rechazar_no_genera_ninguna_peticion_HTTP()
        {
            using var env = new Env();
            var s = env.Create();
            s.Start();
            s.DeclineAnalytics();
            s.LauncherStarted();
            s.GameLaunchRequested("1.20.1", "Forge");
            s.GameProcessStarted();
            s.GameLaunchFailed("timeout");
            s.SessionEnded();
            await s.FlushEventsAsync();
            await Wait.Background(s);

            // Un segundo arranque tras rechazar tampoco contacta con el servidor.
            var s2 = env.Create();
            Assert.False(s2.NeedsConsentPrompt);
            s2.Start();
            await Wait.Background(s2);
            await s2.FlushEventsAsync();

            Assert.Empty(env.Http.Requests);
            Assert.False(s.NeedsConsentPrompt);
            Assert.False(s.IsEnabled);
            Assert.Equal(0, s.PendingEventCount);
            Assert.Empty(env.Secrets.Values);
        }

        [Fact]
        public async Task Aceptar_activa_la_analitica_y_registra()
        {
            using var env = new Env();
            var s = env.Create();
            s.Start();
            s.EnableAnalytics();
            await Wait.Background(s);

            Assert.True(s.IsEnabled);
            Assert.False(s.NeedsConsentPrompt);
            Assert.Matches(UuidV4, s.InstallId!);
            Assert.True(s.Registered);

            string token = env.Secrets.Get(AnalyticsService.TokenKey)!;
            Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
            Assert.DoesNotContain(token, File.ReadAllText(env.StateFile));
            Assert.DoesNotContain(token, File.ReadAllText(env.QueueFile));

            var reg = Assert.Single(env.Http.To("/v1/installations/register"));
            var cuerpo = reg.Json;
            Assert.Equal(new[] { "schema_version", "install_id", "revocation_token", "app_version", "consent_version" },
                cuerpo.Select(p => p.Key).ToArray());
            Assert.Equal(s.InstallId, (string)cuerpo["install_id"]!);
            Assert.Equal(token, (string)cuerpo["revocation_token"]!);
            Assert.Null(reg.Authorization);

            var ev = Assert.Single(SentEvents(env.Http));
            Assert.Equal("consent_changed", (string)ev["event_type"]!);
            Assert.Equal("enabled", (string)ev["properties"]!["consent_state"]!);
            Assert.Equal($"Bearer {token}", env.Http.To("/v1/events").First().Authorization);
            Assert.Equal(0, s.PendingEventCount);

            // Persistencia: otro arranque recuerda la decisión.
            var s2 = env.Create();
            Assert.True(s2.IsEnabled);
            Assert.False(s2.NeedsConsentPrompt);
        }

        [Fact]
        public async Task Desactivar_detiene_en_el_acto_y_vacia_la_cola()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable); // así la cola no se vacía sola
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            for (int i = 0; i < 10; i++) s.GameProcessStarted();
            Assert.True(s.PendingEventCount > 0);
            string idAnterior = s.InstallId!;

            // La revocación se queda colgada; aun así el efecto local es inmediato.
            var liberar = new TaskCompletionSource();
            env.Http.Responder = async (_, ct) => { await liberar.Task.WaitAsync(ct); return new HttpResponseMessage(HttpStatusCode.OK); };

            Task<bool> resultado = s.DisableAnalytics();

            Assert.False(s.IsEnabled);
            Assert.Equal(0, s.PendingEventCount);
            Assert.Null(s.InstallId);
            Assert.Equal("[]", File.ReadAllText(env.QueueFile));
            Assert.Null(env.Secrets.Get(AnalyticsService.TokenKey));
            Assert.True(s.HasPendingRevocation);

            s.GameLaunchRequested("1.21", "Vanilla");
            Assert.Equal(0, s.PendingEventCount);

            liberar.SetResult();
            Assert.True(await resultado);
            Assert.False(s.HasPendingRevocation);
            Assert.Null(env.Secrets.Get(AnalyticsService.PendingTokenKey));

            var rev = env.Http.To("/v1/installations/revoke").Last();
            Assert.Equal(idAnterior, (string)rev.Json["install_id"]!);
            Assert.StartsWith("Bearer ", rev.Authorization);
        }

        [Fact]
        public async Task Desactivar_sin_conexion_devuelve_false_y_reintenta_en_el_arranque()
        {
            using var env = new Env();
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            Assert.False(await s.DisableAnalytics());
            Assert.False(s.IsEnabled);
            Assert.True(s.HasPendingRevocation);

            env.Http.Respond(FakeHandler.DefaultOk);
            var s2 = env.Create();
            s2.Start();
            await Wait.Background(s2);
            Assert.False(s2.HasPendingRevocation);
            Assert.Null(env.Secrets.Get(AnalyticsService.PendingTokenKey));
            Assert.Equal(2, env.Http.To("/v1/installations/revoke").Count());
            Assert.DoesNotContain(env.Http.To("/v1/events"), r => r.Json["install_id"] == null);
        }

        [Fact]
        public async Task La_revocacion_se_abandona_tras_5_intentos()
        {
            using var env = new Env();
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            Assert.False(await s.DisableAnalytics()); // intento 1

            for (int i = 0; i < 6; i++)
            {
                var si = env.Create();
                si.Start();
                await Wait.Background(si);
            }

            Assert.Equal(5, env.Http.To("/v1/installations/revoke").Count());
            Assert.False(env.Create().HasPendingRevocation);
            Assert.Null(env.Secrets.Get(AnalyticsService.PendingTokenKey));
        }

        // ---------------------------------------------------------------- cola

        [Fact]
        public async Task La_cola_no_supera_500_eventos_y_descarta_los_mas_antiguos()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            for (int i = 0; i < 600; i++)
            {
                env.Time.Advance(TimeSpan.FromSeconds(1));
                s.GameProcessStarted();
            }

            Assert.Equal(500, s.PendingEventCount);
            var cola = s.PendingEventsSnapshot;
            Assert.All(cola, e => Assert.Equal("game_process_started", e.EventType)); // consent_changed (el más antiguo) se descartó
            Assert.Equal(cola.OrderBy(e => e.OccurredAt).Select(e => e.EventId), cola.Select(e => e.EventId));

            var s2 = env.Create();
            Assert.Equal(500, s2.PendingEventCount);
        }

        [Fact]
        public async Task No_se_envian_eventos_de_mas_de_7_dias()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            s.GameLaunchRequested("1.21.1", "Fabric");

            env.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
            env.Http.Respond(FakeHandler.DefaultOk);
            var s2 = env.Create(); // la caducidad se aplica también al cargar de disco
            s2.GameProcessStarted();
            await s2.FlushEventsAsync();

            var enviados = SentEvents(env.Http);
            var ev = Assert.Single(enviados);
            Assert.Equal("game_process_started", (string)ev["event_type"]!);
            Assert.Equal(0, s2.PendingEventCount);
        }

        [Fact]
        public async Task Los_event_id_son_unicos_y_uuid_v4()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            for (int i = 0; i < 300; i++) s.GameProcessStarted();

            var ids = s.PendingEventsSnapshot.Select(e => e.EventId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Matches(UuidV4, id));

            env.Http.Respond(FakeHandler.DefaultOk);
            await s.FlushEventsAsync();
            var enviados = SentEvents(env.Http).Select(e => (string)e["event_id"]!).ToList();
            Assert.Equal(enviados.Count, enviados.Distinct().Count());
            Assert.Equal(ids.Count, enviados.Count);
        }

        [Fact]
        public async Task La_cola_elimina_duplicados_por_event_id_al_cargar()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            s.GameProcessStarted();

            var arr = JsonNode.Parse(File.ReadAllText(env.QueueFile))!.AsArray();
            arr.Add(arr[0]!.DeepClone());
            arr.Add(arr[1]!.DeepClone());
            File.WriteAllText(env.QueueFile, arr.ToJsonString());

            Assert.Equal(2, env.Create().PendingEventCount);
        }

        [Fact]
        public async Task Los_eventos_en_cola_no_guardan_install_id()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            s.GameProcessStarted();

            string cola = File.ReadAllText(env.QueueFile);
            Assert.DoesNotContain("install_id", cola);
            Assert.DoesNotContain(s.InstallId!, cola);
        }

        [Fact]
        public async Task Los_lotes_son_de_25_y_200_vacia_la_cola()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            for (int i = 0; i < 59; i++) s.GameProcessStarted(); // 60 con consent_changed

            env.Http.Respond(FakeHandler.DefaultOk);
            await s.FlushEventsAsync();

            var lotes = env.Http.To("/v1/events").Select(r => r.Json["events"]!.AsArray().Count).ToList();
            Assert.Equal(new[] { 25, 25, 10 }, lotes);
            Assert.Equal(0, s.PendingEventCount);
        }

        // ---------------------------------------------------------------- red

        [Fact]
        public async Task Errores_de_red_o_timeout_no_lanzan_ni_bloquean()
        {
            using var env = new Env();
            var s = env.Create(timeout: TimeSpan.FromMilliseconds(300));
            env.Http.Responder = (_, _) => throw new HttpRequestException("sin red");

            var sw = Stopwatch.StartNew();
            s.Start();
            s.EnableAnalytics();
            s.GameLaunchRequested("1.21.1", "NeoForge");
            Assert.True(sw.ElapsedMilliseconds < 1000, "Las llamadas públicas no deben esperar a la red");
            await s.FlushEventsAsync();
            await Wait.Background(s);
            Assert.True(s.PendingEventCount >= 2);

            // Servidor que nunca responde: el timeout de 300 ms corta la petición.
            env.Http.Responder = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(HttpStatusCode.OK); };
            sw.Restart();
            await s.FlushEventsAsync();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));

            sw.Restart();
            bool confirmado = await s.DisableAnalytics();
            Assert.False(confirmado);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
            Assert.False(s.IsEnabled);
        }

        [Fact]
        public async Task Con_la_API_caida_todo_lo_demas_sigue_funcionando()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.InternalServerError);
            var s = env.Create();
            s.Start();
            s.EnableAnalytics();
            s.LauncherStarted();
            s.GameLaunchRequested("24w14a", "Quilt");
            s.GameProcessStarted();
            s.GameLaunchFailed(new HttpRequestException());
            s.SessionEnded();
            await s.FlushEventsAsync();
            await Wait.Background(s);

            Assert.True(s.IsEnabled);
            Assert.False(s.Registered);
            Assert.Equal(6, s.PendingEventCount); // no se pierde nada: se reintentará

            // 429 y 5xx con el registro hecho: se para sin vaciar la cola.
            env.Http.Respond(r => r.Path.EndsWith("/register") ? HttpStatusCode.Created : HttpStatusCode.TooManyRequests);
            await s.FlushEventsAsync();
            Assert.True(s.Registered);
            Assert.Equal(6, s.PendingEventCount);
            Assert.Single(env.Http.To("/v1/events")); // no reintenta en bucle

            s.ClearPendingEvents();
            Assert.Equal(0, s.PendingEventCount);
        }

        [Fact]
        public async Task Respuesta_401_vuelve_a_registrar_con_nuevo_id_y_conserva_la_cola()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            env.Http.Respond(r => r.Path.EndsWith("/register") ? HttpStatusCode.Created : HttpStatusCode.OK);
            await s.FlushEventsAsync();
            string idViejo = s.InstallId!;
            string tokenViejo = env.Secrets.Get(AnalyticsService.TokenKey)!;

            s.GameProcessStarted();
            s.GameProcessStarted();
            int registrosPrevios = env.Http.To("/v1/installations/register").Count();
            int llamadasEvents = 0;
            env.Http.Respond(r =>
            {
                if (r.Path.EndsWith("/register")) return HttpStatusCode.Created;
                return Interlocked.Increment(ref llamadasEvents) == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            });
            await s.FlushEventsAsync();

            Assert.NotEqual(idViejo, s.InstallId);
            Assert.NotEqual(tokenViejo, env.Secrets.Get(AnalyticsService.TokenKey));
            Assert.True(s.Registered);
            var registros = env.Http.To("/v1/installations/register").Skip(registrosPrevios).ToList();
            var nuevo = Assert.Single(registros); // un único re-registro
            Assert.Equal(s.InstallId, (string)nuevo.Json["install_id"]!);

            var lotes = env.Http.To("/v1/events").ToList();
            var ultimo = lotes[^1];
            Assert.Equal(s.InstallId, (string)ultimo.Json["install_id"]!);
            Assert.Equal(2, ultimo.Json["events"]!.AsArray().Count); // la cola se conservó y se reenvió
            Assert.All(ultimo.Json["events"]!.AsArray(), e => Assert.Equal(s.InstallId, (string)e!["install_id"]!));
            Assert.Equal(0, s.PendingEventCount);
        }

        [Fact]
        public async Task Respuesta_401_repetida_no_entra_en_bucle()
        {
            using var env = new Env();
            env.Http.Respond(r => r.Path.EndsWith("/register") ? HttpStatusCode.Created : HttpStatusCode.Unauthorized);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            Assert.Equal(2, env.Http.To("/v1/installations/register").Count());
            Assert.Equal(2, env.Http.To("/v1/events").Count());
            Assert.Equal(1, s.PendingEventCount);
        }

        [Fact]
        public async Task Respuesta_409_genera_nuevo_id_y_reintenta_una_vez()
        {
            using var env = new Env();
            int registros = 0;
            env.Http.Respond(r =>
            {
                if (r.Path.EndsWith("/register"))
                    return Interlocked.Increment(ref registros) == 1 ? HttpStatusCode.Conflict : HttpStatusCode.Created;
                return HttpStatusCode.OK;
            });
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            var regs = env.Http.To("/v1/installations/register").ToList();
            Assert.Equal(2, regs.Count);
            string id1 = (string)regs[0].Json["install_id"]!, id2 = (string)regs[1].Json["install_id"]!;
            Assert.NotEqual(id1, id2);
            Assert.NotEqual((string)regs[0].Json["revocation_token"]!, (string)regs[1].Json["revocation_token"]!);
            Assert.Equal(id2, s.InstallId);
            Assert.True(s.Registered);
            Assert.Equal(id2, (string)env.Http.To("/v1/events").Single().Json["install_id"]!);
        }

        [Fact]
        public async Task Respuesta_409_repetida_se_rinde_sin_bucle()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.Conflict);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            Assert.Equal(2, env.Http.To("/v1/installations/register").Count());
            Assert.Empty(env.Http.To("/v1/events"));
            Assert.False(s.Registered);
        }

        [Fact]
        public async Task Solo_un_flush_a_la_vez()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);

            var liberar = new TaskCompletionSource();
            int enCurso = 0, maximo = 0;
            env.Http.Responder = async (r, ct) =>
            {
                int n = Interlocked.Increment(ref enCurso);
                maximo = Math.Max(maximo, n);
                await liberar.Task.WaitAsync(ct);
                Interlocked.Decrement(ref enCurso);
                return new HttpResponseMessage(FakeHandler.DefaultOk(r));
            };
            var flushes = Enumerable.Range(0, 5).Select(_ => Task.Run(s.FlushEventsAsync)).ToArray();
            await Task.Delay(200);
            liberar.SetResult();
            await Task.WhenAll(flushes);
            Assert.Equal(1, maximo);
        }

        // ---------------------------------------------------------------- actualización y sesión

        [Fact]
        public async Task Detecta_actualizacion_y_envia_launcher_updated()
        {
            using var env = new Env();
            var s = env.Create("1.5.5");
            s.Start();
            s.EnableAnalytics();
            await Wait.Background(s);

            var s2 = env.Create("1.5.6");
            s2.Start();
            await Wait.Background(s2);

            var upd = SentEvents(env.Http).Single(e => (string)e["event_type"]! == "launcher_updated");
            Assert.Equal("1.5.5", (string)upd["properties"]!["previous_version"]!);
            Assert.Equal("1.5.6", (string)upd["app_version"]!);

            var s3 = env.Create("1.5.6");
            s3.Start();
            await Wait.Background(s3);
            Assert.Single(SentEvents(env.Http), e => (string)e["event_type"]! == "launcher_updated");
        }

        [Fact]
        public async Task Sin_consentimiento_no_hay_launcher_updated_pero_se_guarda_la_version()
        {
            using var env = new Env();
            env.Create("1.5.5").Start();
            var s2 = env.Create("1.5.6");
            s2.Start();
            await Wait.Background(s2);
            Assert.Contains("\"last_version_seen\":\"1.5.6\"", File.ReadAllText(env.StateFile));
            Assert.Empty(env.Http.Requests);
        }

        [Fact]
        public async Task SessionEnded_calcula_la_duracion_localmente_y_no_hace_red()
        {
            using var env = new Env();
            env.Http.Respond(_ => HttpStatusCode.ServiceUnavailable);
            var s = env.Create();
            s.EnableAnalytics();
            await Wait.Background(s);
            int peticiones = env.Http.Requests.Count;

            env.Time.Advance(TimeSpan.FromSeconds(3725.9));
            s.SessionEnded();

            var ev = s.PendingEventsSnapshot.Last();
            Assert.Equal("session_ended", ev.EventType);
            Assert.Equal(3725, (int)ev.Properties["duration_seconds"]!);
            await Task.Delay(100);
            Assert.Equal(peticiones, env.Http.Requests.Count);

            env.Time.Advance(TimeSpan.FromDays(30));
            s.SessionEnded();
            Assert.Equal(604800, (int)s.PendingEventsSnapshot.Last().Properties["duration_seconds"]!);
        }

        [Fact]
        public void Si_cambia_la_version_del_consentimiento_se_vuelve_a_preguntar()
        {
            using var env = new Env();
            var s = env.Create();
            s.DeclineAnalytics();
            Assert.False(s.NeedsConsentPrompt);

            var v2 = new AnalyticsService(new AnalyticsOptions
            {
                Endpoint = new Uri("https://analytics.example.test"),
                AppVersion = "1.5.5",
                StorageDirectory = env.Dir,
                ConsentVersion = 2,
            }, env.Secrets, env.Time, env.Http);
            Assert.True(v2.NeedsConsentPrompt);
            v2.Dispose();
        }

        [Fact]
        public void Archivos_corruptos_no_rompen_nada()
        {
            using var env = new Env();
            File.WriteAllText(env.StateFile, "{esto no es json");
            File.WriteAllText(env.QueueFile, "[[[");
            var s = env.Create();
            Assert.False(s.IsEnabled);
            Assert.True(s.NeedsConsentPrompt);
            Assert.Equal(0, s.PendingEventCount);
        }
    }
}
