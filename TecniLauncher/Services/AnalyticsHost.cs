using TecniLauncher.Analytics;

namespace TecniLauncher.Services
{
    /// <summary>
    /// Punto único de acceso a las estadísticas voluntarias desde el launcher.
    /// Cada método está protegido: nunca lanza, nunca bloquea y nunca muestra errores.
    /// </summary>
    public static class AnalyticsHost
    {
        // Rellenar al desplegar la API (p. ej. "https://<worker>.workers.dev"). Con null la analítica queda inactiva.
        private const string? ENDPOINT = "https://tecnilauncher-analytics-api.benjamin14johan.workers.dev";

        private static AnalyticsService? _servicio;

        public static bool Disponible => Seguro(() => _servicio?.IsAvailable == true);
        public static bool Activada => Seguro(() => _servicio?.IsEnabled == true);
        public static bool NecesitaConsentimiento => Seguro(() => _servicio?.NeedsConsentPrompt == true);

        public static void Inicializar(string versionApp)
        {
            try
            {
                if (_servicio != null) return;
                _servicio = new AnalyticsService(new AnalyticsOptions
                {
                    Endpoint = string.IsNullOrWhiteSpace(ENDPOINT) ? null : new Uri(ENDPOINT),
                    AppVersion = versionApp,
                    OsFamily = "windows",
                    ConsentVersion = 1,
                    StorageDirectory = Core.RutaData,
                    DebugLog = m => LogService.Debug(m),
                }, new SecretStoreDpapi());
            }
            catch (Exception ex) { LogService.Debug($"Analytics: no se pudo inicializar ({ex.GetType().Name})"); }
        }

        public static void Start() => Proteger(() => _servicio?.Start());
        public static void Aceptar() => Proteger(() => _servicio?.EnableAnalytics());
        public static void Rechazar() => Proteger(() => _servicio?.DeclineAnalytics());
        public static void SesionTerminada() => Proteger(() => _servicio?.SessionEnded());
        public static void JuegoSolicitado(string? versionMinecraft, string? tipoLoader) => Proteger(() => _servicio?.GameLaunchRequested(versionMinecraft, tipoLoader));
        public static void ProcesoJuegoIniciado() => Proteger(() => _servicio?.GameProcessStarted());
        public static void FalloLanzamiento(Exception ex) => Proteger(() => _servicio?.GameLaunchFailed(ex));

        /// <summary>Desactiva en el acto. Devuelve true si el servidor confirmó el borrado.</summary>
        public static async Task<bool> Desactivar()
        {
            try { return _servicio == null || await _servicio.DisableAnalytics(); }
            catch (Exception ex) { LogService.Debug($"Analytics: fallo al desactivar ({ex.GetType().Name})"); return false; }
        }

        private static void Proteger(Action accion)
        {
            try { accion(); }
            catch (Exception ex) { LogService.Debug($"Analytics: operación ignorada ({ex.GetType().Name})"); }
        }

        private static bool Seguro(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }

        /// <summary>Adaptador ISecretStore → SecretsManager (DPAPI, usuario actual).</summary>
        private sealed class SecretStoreDpapi : ISecretStore
        {
            public string? Get(string key)
            {
                string v = SecretsManager.ObtenerSecreto(key);
                return string.IsNullOrEmpty(v) ? null : v;
            }

            public void Set(string key, string value) => SecretsManager.GuardarSecreto(key, value);

            public void Delete(string key) => SecretsManager.EliminarSecreto(key);
        }
    }
}
