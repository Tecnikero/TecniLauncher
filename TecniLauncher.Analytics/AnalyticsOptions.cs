namespace TecniLauncher.Analytics
{
    /// <summary>Configuración del cliente de estadísticas voluntarias.</summary>
    public sealed class AnalyticsOptions
    {
        /// <summary>URL base de la API. Si es null, la analítica queda inactiva y no se pide consentimiento.</summary>
        public Uri? Endpoint { get; init; }

        public string AppVersion { get; init; } = "0.0.0";
        public string OsFamily { get; init; } = "windows";
        public int ConsentVersion { get; init; } = 1;

        /// <summary>Carpeta donde se guardan analytics.json y analytics-queue.json.</summary>
        public string StorageDirectory { get; init; } = "";

        public int MaxQueue { get; init; } = 500;
        public TimeSpan EventMaxAge { get; init; } = TimeSpan.FromDays(7);
        public int BatchSize { get; init; } = 25;

        /// <summary>Tiempo máximo de cada petición HTTP.</summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>Intentos totales de revocación antes de abandonarla (los datos caducan en el servidor a los 30 días).</summary>
        public int MaxRevocationAttempts { get; init; } = 5;

        /// <summary>Registro de depuración. Nunca recibe datos de eventos, identificadores ni tokens.</summary>
        public Action<string>? DebugLog { get; init; }
    }

    /// <summary>Almacén de secretos (en el launcher, un adaptador sobre SecretsManager con DPAPI).</summary>
    public interface ISecretStore
    {
        string? Get(string key);
        void Set(string key, string value);
        void Delete(string key);
    }
}
