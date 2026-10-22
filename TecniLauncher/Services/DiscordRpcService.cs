using DiscordRPC;

namespace TecniLauncher.Services
{
    public static class DiscordRpcService
    {
        private const string AppId = "1495130192572580012";

        private static readonly object _lock = new();
        private static DiscordRpcClient? _client;
        private static string _details = "";
        private static string _state = "";

        public static void Establecer(string details, string state)
        {
            lock (_lock)
            {
                _details = details;
                _state = state;
                EnviarPresencia();
            }
        }

        public static void Aplicar(bool activo)
        {
            lock (_lock)
            {
                if (activo) Conectar();
                else Desconectar();
            }
        }

        public static void Detener()
        {
            lock (_lock) Desconectar();
        }

        private static void Conectar()
        {
            if (_client != null) return;

            try
            {
                _client = new DiscordRpcClient(AppId);
                _client.Initialize();
                EnviarPresencia();
            }
            catch (Exception ex)
            {
                LogService.Warn("Error iniciando Discord RPC", ex);
                try { _client?.Dispose(); } catch (Exception exLog) { LogService.Debug("Error liberando cliente Discord", exLog); }
                _client = null;
            }
        }

        private static void Desconectar()
        {
            if (_client == null) return;

            try { _client.ClearPresence(); } catch (Exception exLog) { LogService.Debug("Error limpiando presencia de Discord", exLog); }
            try { _client.Dispose(); } catch (Exception exLog) { LogService.Debug("Error liberando cliente Discord", exLog); }
            _client = null;
        }

        private static void EnviarPresencia()
        {
            if (_client == null || !_client.IsInitialized) return;

            try
            {
                _client.SetPresence(new RichPresence
                {
                    Details = _details,
                    State = _state,
                    Assets = new Assets
                    {
                        LargeImageKey = "tecnilogo",
                        LargeImageText = "TecniLauncher"
                    },
                    Timestamps = Timestamps.Now
                });
            }
            catch (Exception ex)
            {
                LogService.Warn("Error actualizando Discord RPC", ex);
            }
        }
    }
}
