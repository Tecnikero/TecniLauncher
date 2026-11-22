using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace TecniLauncher.Analytics
{
    /// <summary>
    /// Cliente de estadísticas voluntarias. Desactivado por defecto; nunca lanza excepciones
    /// ni bloquea a quien lo llama. Todo el tráfico de red ocurre en segundo plano.
    /// </summary>
    public sealed class AnalyticsService : IDisposable
    {
        internal const string TokenKey = "analytics_token";
        internal const string PendingTokenKey = "analytics_pending_revocation_token";
        internal const string StateFileName = "analytics.json";
        internal const string QueueFileName = "analytics-queue.json";
        private const int MaxSessionSeconds = 604800;

        private readonly AnalyticsOptions _o;
        private readonly ISecretStore _secrets;
        private readonly TimeProvider _time;
        private readonly HttpClient? _http;
        private readonly Uri? _base;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private readonly string _statePath;
        private readonly string _queuePath;
        private readonly DateTimeOffset _sessionStart;
        private CancellationTokenSource _cts = new();
        private AnalyticsState _state = new();
        private List<QueuedEvent> _queue = new();
        private Task _background = Task.CompletedTask;

        public AnalyticsService(AnalyticsOptions options, ISecretStore secrets,
            TimeProvider? timeProvider = null, HttpMessageHandler? httpHandler = null)
        {
            _o = options ?? new AnalyticsOptions();
            _secrets = secrets;
            _time = timeProvider ?? TimeProvider.System;
            _statePath = Path.Combine(_o.StorageDirectory ?? "", StateFileName);
            _queuePath = Path.Combine(_o.StorageDirectory ?? "", QueueFileName);
            SessionId = NewUuid();

            try { _sessionStart = _time.GetUtcNow(); } catch { _sessionStart = DateTimeOffset.UtcNow; }
            if (_o.Endpoint == null) return; // Inactiva: ni archivos ni red.

            try
            {
                string b = _o.Endpoint.AbsoluteUri;
                _base = new Uri(b.EndsWith('/') ? b : b + "/");

                HttpMessageHandler handler = httpHandler ?? new SocketsHttpHandler
                {
                    UseCookies = false,
                    AllowAutoRedirect = false,
                };
                _http = new HttpClient(handler, disposeHandler: httpHandler == null) { Timeout = _o.RequestTimeout };
                _http.DefaultRequestHeaders.UserAgent.Clear();
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("TecniLauncher-Analytics/1");
            }
            catch (Exception ex) { Log($"no se pudo crear el cliente HTTP ({ex.GetType().Name})"); _http = null; }

            try
            {
                _state = AtomicJsonFile.Read<AnalyticsState>(_statePath) ?? new AnalyticsState();
                if (_state.Consent is not (AnalyticsState.Granted or AnalyticsState.Denied or AnalyticsState.Unknown))
                    _state.Consent = AnalyticsState.Unknown;
                _queue = AtomicJsonFile.Read<List<QueuedEvent>>(_queuePath) ?? new List<QueuedEvent>();
                lock (_gate) { if (NormalizeQueueLocked()) SaveQueueLocked(); }
            }
            catch (Exception ex)
            {
                Log($"no se pudo cargar el estado ({ex.GetType().Name})");
                _state = new AnalyticsState();
                _queue = new List<QueuedEvent>();
            }
        }

        /// <summary>Identificador de esta ejecución del launcher.</summary>
        public string SessionId { get; }

        /// <summary>Hay endpoint configurado.</summary>
        public bool IsAvailable => _o.Endpoint != null;

        public bool IsEnabled { get { lock (_gate) return IsAvailable && _state.Consent == AnalyticsState.Granted; } }

        public bool NeedsConsentPrompt
        {
            get
            {
                if (!IsAvailable) return false;
                lock (_gate)
                    return _state.Consent == AnalyticsState.Unknown || _state.ConsentVersion < _o.ConsentVersion;
            }
        }

        public bool HasPendingRevocation { get { lock (_gate) return _state.PendingRevocationInstallId != null; } }

        internal int PendingEventCount { get { lock (_gate) return _queue.Count; } }
        internal IReadOnlyList<QueuedEvent> PendingEventsSnapshot { get { lock (_gate) return _queue.ToList(); } }
        internal string? InstallId { get { lock (_gate) return _state.InstallId; } }
        internal bool Registered { get { lock (_gate) return _state.Registered; } }
        internal int PendingRevocationAttempts { get { lock (_gate) return _state.PendingRevocationAttempts; } }

        /// <summary>Tareas en segundo plano lanzadas hasta ahora (para las pruebas).</summary>
        internal Task BackgroundTask { get { lock (_gate) return _background; } }

        // ------------------------------------------------------------------ ciclo de vida

        public void Start()
        {
            if (!IsAvailable) return;
            try
            {
                lock (_gate)
                {
                    string? previa = _state.LastVersionSeen;
                    if (previa != null && previa != _o.AppVersion && _state.Consent == AnalyticsState.Granted)
                        EnqueueLocked("launcher_updated", new JsonObject { ["previous_version"] = previa });

                    _state.LastVersionSeen = _o.AppVersion;
                    SaveStateLocked();
                }
                LauncherStarted();
                RunInBackground(async () =>
                {
                    await RetryPendingRevocationAsync().ConfigureAwait(false);
                    await FlushEventsAsync().ConfigureAwait(false);
                });
            }
            catch (Exception ex) { Log($"Start falló ({ex.GetType().Name})"); }
        }

        public void EnableAnalytics()
        {
            if (!IsAvailable) return;
            try
            {
                lock (_gate)
                {
                    bool reutilizar = _state.Consent == AnalyticsState.Granted
                                      && _state.InstallId != null
                                      && !string.IsNullOrEmpty(SafeGetSecret(TokenKey));

                    _state.Consent = AnalyticsState.Granted;
                    _state.ConsentVersion = _o.ConsentVersion;
                    if (!reutilizar) NewIdentityLocked();
                    SaveStateLocked();

                    if (_cts.IsCancellationRequested) { _cts.Dispose(); _cts = new CancellationTokenSource(); }
                    EnqueueLocked("consent_changed", new JsonObject { ["consent_state"] = "enabled" });
                }
                RunInBackground(FlushEventsAsync);
            }
            catch (Exception ex) { Log($"EnableAnalytics falló ({ex.GetType().Name})"); }
        }

        /// <summary>El usuario rechaza en la ventana de consentimiento. Sin tráfico de red.</summary>
        public void DeclineAnalytics()
        {
            if (!IsAvailable) return;
            try
            {
                bool habiaInstalacion;
                lock (_gate) habiaInstalacion = _state.InstallId != null;

                // Caso de una nueva versión del consentimiento tras haber aceptado: rechazar equivale a desactivar.
                if (habiaInstalacion) { _ = DisableAnalytics(); return; }

                lock (_gate)
                {
                    _state.Consent = AnalyticsState.Denied;
                    _state.ConsentVersion = _o.ConsentVersion;
                    SaveStateLocked();
                }
            }
            catch (Exception ex) { Log($"DeclineAnalytics falló ({ex.GetType().Name})"); }
        }

        /// <summary>
        /// Desactiva en el acto (deja de recopilar y vacía la cola) y luego pide al servidor el borrado.
        /// Devuelve true solo si el servidor confirmó el borrado.
        /// </summary>
        public async Task<bool> DisableAnalytics()
        {
            if (!IsAvailable) return true;
            bool hayRevocacion;
            try
            {
                CancellationTokenSource anterior;
                lock (_gate)
                {
                    _state.Consent = AnalyticsState.Denied;
                    _state.ConsentVersion = _o.ConsentVersion;
                    _queue.Clear();
                    Try(SaveQueueLocked);

                    if (_state.InstallId != null)
                    {
                        string? token = SafeGetSecret(TokenKey);
                        if (!string.IsNullOrEmpty(token))
                        {
                            _state.PendingRevocationInstallId = _state.InstallId;
                            _state.PendingRevocationAttempts = 0;
                            Try(() => _secrets.Set(PendingTokenKey, token));
                        }
                        Try(() => _secrets.Delete(TokenKey));
                    }
                    _state.InstallId = null;
                    _state.Registered = false;
                    Try(SaveStateLocked);

                    hayRevocacion = _state.PendingRevocationInstallId != null;
                    anterior = _cts;
                    _cts = new CancellationTokenSource();
                }
                Try(anterior.Cancel); // corta cualquier envío en curso
            }
            catch (Exception ex)
            {
                Log($"DisableAnalytics falló ({ex.GetType().Name})");
                return false;
            }

            if (!hayRevocacion) return true;

            try
            {
                using var presupuesto = new CancellationTokenSource(_o.RequestTimeout);
                var tarea = TryRevokeAsync(presupuesto.Token);
                lock (_gate) _background = Task.WhenAll(_background, tarea);
                return await tarea.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"revocación falló ({ex.GetType().Name})");
                return false;
            }
        }

        public void ClearPendingEvents()
        {
            try
            {
                lock (_gate)
                {
                    _queue.Clear();
                    SaveQueueLocked();
                }
            }
            catch (Exception ex) { Log($"ClearPendingEvents falló ({ex.GetType().Name})"); }
        }

        // ------------------------------------------------------------------ eventos tipados

        public void LauncherStarted() => TrackEvent("launcher_started", new JsonObject());

        /// <summary>Solo encola; no hace red (se llama al cerrar el launcher).</summary>
        public void SessionEnded()
        {
            try
            {
                double s = (_time.GetUtcNow() - _sessionStart).TotalSeconds;
                int segundos = (int)Math.Clamp(Math.Floor(s), 0, MaxSessionSeconds);
                TrackEvent("session_ended", new JsonObject { ["duration_seconds"] = segundos });
            }
            catch (Exception ex) { Log($"SessionEnded falló ({ex.GetType().Name})"); }
        }

        public void GameLaunchRequested(string? minecraftVersion, string? loaderType) =>
            TrackEvent("game_launch_requested", new JsonObject
            {
                ["minecraft_version"] = AnalyticsMappings.MinecraftVersion(minecraftVersion),
                ["loader_type"] = AnalyticsMappings.LoaderType(loaderType),
            });

        public void GameProcessStarted() => TrackEvent("game_process_started", new JsonObject());

        public void GameLaunchFailed(string? errorCategory) =>
            TrackEvent("game_launch_failed", new JsonObject
            {
                ["error_category"] = AnalyticsMappings.IsValidErrorCategory(errorCategory) ? errorCategory : "unknown",
            });

        public void GameLaunchFailed(Exception? ex) => GameLaunchFailed(AnalyticsMappings.ErrorCategory(ex));

        internal void TrackEvent(string eventType, JsonObject properties)
        {
            if (!IsAvailable) return;
            try
            {
                lock (_gate)
                {
                    if (_state.Consent != AnalyticsState.Granted) return;
                    EnqueueLocked(eventType, properties);
                }
            }
            catch (Exception ex) { Log($"TrackEvent falló ({ex.GetType().Name})"); }
        }

        // ------------------------------------------------------------------ envío

        /// <summary>Envía la cola. Un solo envío a la vez; nunca lanza excepciones.</summary>
        public async Task FlushEventsAsync()
        {
            if (!IsAvailable || _http == null) return;
            if (!IsEnabled) return;

            bool adquirido = false;
            try
            {
                adquirido = await _flushLock.WaitAsync(0).ConfigureAwait(false);
                if (!adquirido) return; // ya hay otro envío en curso

                CancellationToken ct;
                lock (_gate) ct = _cts.Token;

                if (!await EnsureRegisteredAsync(ct).ConfigureAwait(false)) return;

                bool reRegistrado = false;
                int maxLotes = Math.Max(1, _o.MaxQueue / Math.Max(1, _o.BatchSize) + 2);
                for (int i = 0; i < maxLotes; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    List<QueuedEvent> lote;
                    string? installId;
                    string? token;
                    lock (_gate)
                    {
                        if (_state.Consent != AnalyticsState.Granted) return;
                        if (NormalizeQueueLocked()) SaveQueueLocked();
                        lote = _queue.Take(Math.Max(1, _o.BatchSize)).ToList();
                        installId = _state.InstallId;
                        token = SafeGetSecret(TokenKey);
                    }
                    if (lote.Count == 0 || installId == null || string.IsNullOrEmpty(token)) return;

                    var cuerpo = new JsonObject
                    {
                        ["schema_version"] = 1,
                        ["install_id"] = installId,
                        ["events"] = new JsonArray(lote.Select(e => (JsonNode)e.ToWire(installId)).ToArray()),
                    };

                    HttpStatusCode codigo = await PostAsync("v1/events", cuerpo, token, ct).ConfigureAwait(false);

                    if (codigo == HttpStatusCode.OK)
                    {
                        // Aceptados, duplicados y rechazados se quitan: ninguno debe reintentarse.
                        var enviados = lote.Select(e => e.EventId).ToHashSet();
                        lock (_gate)
                        {
                            _queue.RemoveAll(e => enviados.Contains(e.EventId));
                            SaveQueueLocked();
                        }
                        continue;
                    }

                    if (codigo == HttpStatusCode.Unauthorized && !reRegistrado)
                    {
                        // La instalación ya no existe en el servidor: nueva identidad, la cola se conserva.
                        reRegistrado = true;
                        lock (_gate)
                        {
                            if (_state.Consent != AnalyticsState.Granted) return;
                            NewIdentityLocked();
                            SaveStateLocked();
                        }
                        Log("instalación desconocida para el servidor (401); se registra de nuevo");
                        if (!await EnsureRegisteredAsync(ct).ConfigureAwait(false)) return;
                        continue;
                    }

                    Log($"envío detenido (HTTP {(int)codigo}); se reintentará en el próximo arranque");
                    return;
                }
            }
            catch (OperationCanceledException) { Log("envío cancelado o agotado el tiempo"); }
            catch (Exception ex) { Log($"envío detenido ({ex.GetType().Name})"); }
            finally
            {
                if (adquirido) _flushLock.Release();
            }
        }

        /// <summary>Registra la instalación si aún no lo está. 409 → nuevo id y un reintento.</summary>
        private async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
        {
            for (int intento = 0; intento < 2; intento++)
            {
                string? installId;
                string? token;
                lock (_gate)
                {
                    if (_state.Consent != AnalyticsState.Granted) return false;
                    if (_state.Registered) return true;

                    if (_state.InstallId == null || string.IsNullOrEmpty(SafeGetSecret(TokenKey)))
                    {
                        NewIdentityLocked();
                        SaveStateLocked();
                    }
                    installId = _state.InstallId;
                    token = SafeGetSecret(TokenKey);
                }
                if (installId == null || string.IsNullOrEmpty(token)) return false;

                var cuerpo = new JsonObject
                {
                    ["schema_version"] = 1,
                    ["install_id"] = installId,
                    ["revocation_token"] = token,
                    ["app_version"] = _o.AppVersion,
                    ["consent_version"] = _o.ConsentVersion,
                };
                HttpStatusCode codigo = await PostAsync("v1/installations/register", cuerpo, null, ct).ConfigureAwait(false);

                if (codigo == HttpStatusCode.Created)
                {
                    lock (_gate)
                    {
                        // Si mientras tanto se desactivó o cambió la identidad, no se marca nada.
                        if (_state.Consent != AnalyticsState.Granted || _state.InstallId != installId) return false;
                        _state.Registered = true;
                        SaveStateLocked();
                    }
                    return true;
                }

                if (codigo == HttpStatusCode.Conflict && intento == 0)
                {
                    lock (_gate)
                    {
                        if (_state.Consent != AnalyticsState.Granted || _state.InstallId != installId) return false;
                        NewIdentityLocked();
                        SaveStateLocked();
                    }
                    Log("install_id en conflicto (409); se genera otro");
                    continue;
                }

                Log($"registro no completado (HTTP {(int)codigo})");
                return false;
            }
            return false;
        }

        // ------------------------------------------------------------------ revocación

        private async Task RetryPendingRevocationAsync()
        {
            try
            {
                using var presupuesto = new CancellationTokenSource(_o.RequestTimeout);
                await TryRevokeAsync(presupuesto.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { Log($"reintento de revocación falló ({ex.GetType().Name})"); }
        }

        /// <summary>Un intento de revocación de la instalación pendiente. Devuelve true si el servidor la confirmó.</summary>
        private async Task<bool> TryRevokeAsync(CancellationToken ct)
        {
            if (_http == null) return false;
            bool adquirido = false;
            try
            {
                // Espera a que termine (o se cancele) un envío en curso, para no cruzarse con un registro.
                adquirido = await _flushLock.WaitAsync(Timeout.Infinite, ct).ConfigureAwait(false);

                string? installId;
                string? token;
                lock (_gate)
                {
                    installId = _state.PendingRevocationInstallId;
                    if (installId == null) return false;
                    token = SafeGetSecret(PendingTokenKey);

                    if (string.IsNullOrEmpty(token) || _state.PendingRevocationAttempts >= _o.MaxRevocationAttempts)
                    {
                        AbandonRevocationLocked("sin token o sin intentos restantes");
                        return false;
                    }
                    _state.PendingRevocationAttempts++;
                    SaveStateLocked();
                }

                var cuerpo = new JsonObject { ["schema_version"] = 1, ["install_id"] = installId };
                HttpStatusCode codigo = await PostAsync("v1/installations/revoke", cuerpo, token, ct).ConfigureAwait(false);

                lock (_gate)
                {
                    if (_state.PendingRevocationInstallId != installId) return codigo == HttpStatusCode.OK;

                    if (codigo == HttpStatusCode.OK)
                    {
                        ClearPendingRevocationLocked();
                        return true;
                    }
                    if (codigo == HttpStatusCode.Unauthorized)
                    {
                        AbandonRevocationLocked("el servidor no reconoce el token (401)");
                        return false;
                    }
                    if (_state.PendingRevocationAttempts >= _o.MaxRevocationAttempts)
                        AbandonRevocationLocked("se agotaron los intentos");
                    else
                        Log($"revocación pendiente (HTTP {(int)codigo})");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                Log("revocación agotó el tiempo");
                lock (_gate)
                    if (_state.PendingRevocationInstallId != null && _state.PendingRevocationAttempts >= _o.MaxRevocationAttempts)
                        AbandonRevocationLocked("se agotaron los intentos");
                return false;
            }
            catch (Exception ex)
            {
                Log($"revocación falló ({ex.GetType().Name})");
                return false;
            }
            finally
            {
                if (adquirido) _flushLock.Release();
            }
        }

        private void ClearPendingRevocationLocked()
        {
            _state.PendingRevocationInstallId = null;
            _state.PendingRevocationAttempts = 0;
            Try(() => _secrets.Delete(PendingTokenKey));
            Try(SaveStateLocked);
        }

        private void AbandonRevocationLocked(string motivo)
        {
            Log($"revocación abandonada: {motivo} (los datos caducan en el servidor a los 30 días)");
            ClearPendingRevocationLocked();
        }

        // ------------------------------------------------------------------ utilidades

        private async Task<HttpStatusCode> PostAsync(string ruta, JsonObject cuerpo, string? bearer, CancellationToken ct)
        {
            if (_http == null || _base == null) return HttpStatusCode.ServiceUnavailable;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, ruta))
                {
                    Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                if (bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                return resp.StatusCode;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Error de red o timeout del HttpClient: se trata como servidor no disponible.
                Log($"petición sin respuesta ({ex.GetType().Name})");
                return HttpStatusCode.ServiceUnavailable;
            }
        }

        private void EnqueueLocked(string eventType, JsonObject properties)
        {
            _queue.Add(new QueuedEvent
            {
                EventId = NewUuid(),
                EventType = eventType,
                SessionId = SessionId,
                AppVersion = _o.AppVersion,
                OsFamily = _o.OsFamily,
                OccurredAt = QueuedEvent.FormatTimestamp(_time.GetUtcNow()),
                Properties = properties,
            });
            NormalizeQueueLocked();
            SaveQueueLocked();
        }

        /// <summary>Quita caducados, duplicados por event_id y los más antiguos por encima del máximo. Devuelve si cambió algo.</summary>
        private bool NormalizeQueueLocked()
        {
            int antes = _queue.Count;
            DateTimeOffset limite = _time.GetUtcNow() - _o.EventMaxAge;
            var vistos = new HashSet<string>();
            _queue = _queue
                .Where(e => e != null && !string.IsNullOrEmpty(e.EventId) && e.Properties != null
                            && e.TryGetOccurredAt(out var t) && t >= limite)
                .Where(e => vistos.Add(e.EventId))
                .ToList();

            int max = Math.Max(1, _o.MaxQueue);
            if (_queue.Count > max) _queue.RemoveRange(0, _queue.Count - max);
            return _queue.Count != antes;
        }

        private void NewIdentityLocked()
        {
            _state.InstallId = NewUuid();
            _state.Registered = false;
            _secrets.Set(TokenKey, NewToken());
        }

        private string? SafeGetSecret(string key)
        {
            try { return _secrets.Get(key); } catch { return null; }
        }

        private void SaveStateLocked()
        {
            try { AtomicJsonFile.Write(_statePath, _state); }
            catch (Exception ex) { Log($"no se pudo guardar el estado ({ex.GetType().Name})"); }
        }

        private void SaveQueueLocked()
        {
            try { AtomicJsonFile.Write(_queuePath, _queue); }
            catch (Exception ex) { Log($"no se pudo guardar la cola ({ex.GetType().Name})"); }
        }

        private void RunInBackground(Func<Task> trabajo)
        {
            var t = Task.Run(async () =>
            {
                try { await trabajo().ConfigureAwait(false); }
                catch (Exception ex) { Log($"tarea en segundo plano falló ({ex.GetType().Name})"); }
            });
            lock (_gate) _background = Task.WhenAll(_background, t);
        }

        private void Try(Action a)
        {
            try { a(); } catch (Exception ex) { Log($"operación ignorada ({ex.GetType().Name})"); }
        }

        private void Log(string mensaje)
        {
            try { _o.DebugLog?.Invoke("Analytics: " + mensaje); } catch { }
        }

        internal static string NewUuid() => Guid.NewGuid().ToString("D");

        internal static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _http?.Dispose(); } catch { }
        }
    }
}
