using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TecniLauncher.Analytics
{
    /// <summary>Contenido de analytics.json. El token nunca se guarda aquí (va en ISecretStore).</summary>
    internal sealed class AnalyticsState
    {
        public const string Unknown = "unknown";
        public const string Granted = "granted";
        public const string Denied = "denied";

        [JsonPropertyName("consent")] public string Consent { get; set; } = Unknown;
        [JsonPropertyName("consent_version")] public int ConsentVersion { get; set; }
        [JsonPropertyName("install_id")] public string? InstallId { get; set; }
        [JsonPropertyName("registered")] public bool Registered { get; set; }
        [JsonPropertyName("last_version_seen")] public string? LastVersionSeen { get; set; }
        [JsonPropertyName("pending_revocation_install_id")] public string? PendingRevocationInstallId { get; set; }
        [JsonPropertyName("pending_revocation_attempts")] public int PendingRevocationAttempts { get; set; }
    }

    /// <summary>Evento en cola. No guarda install_id: se añade al enviar.</summary>
    internal sealed class QueuedEvent
    {
        [JsonPropertyName("event_id")] public string EventId { get; set; } = "";
        [JsonPropertyName("event_type")] public string EventType { get; set; } = "";
        [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
        [JsonPropertyName("app_version")] public string AppVersion { get; set; } = "";
        [JsonPropertyName("os_family")] public string OsFamily { get; set; } = "";
        [JsonPropertyName("occurred_at")] public string OccurredAt { get; set; } = "";
        [JsonPropertyName("properties")] public JsonObject Properties { get; set; } = new();

        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public static string FormatTimestamp(DateTimeOffset t) =>
            t.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        public bool TryGetOccurredAt(out DateTimeOffset value) =>
            DateTimeOffset.TryParseExact(OccurredAt, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

        /// <summary>Evento tal como lo espera la API: todos los campos y en el orden del contrato.</summary>
        public JsonObject ToWire(string installId) => new()
        {
            ["schema_version"] = 1,
            ["event_id"] = EventId,
            ["event_type"] = EventType,
            ["install_id"] = installId,
            ["session_id"] = SessionId,
            ["app_version"] = AppVersion,
            ["os_family"] = OsFamily,
            ["occurred_at"] = OccurredAt,
            ["properties"] = Properties.DeepClone(),
        };
    }

    internal static class AtomicJsonFile
    {
        private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = false };

        public static T? Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opciones);
            }
            catch { return null; }
        }

        /// <summary>Escribe en un .tmp y lo sustituye por el original, para no dejar nunca un archivo a medias.</summary>
        public static void Write<T>(string path, T value)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opciones));
            if (File.Exists(path))
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }
    }
}
