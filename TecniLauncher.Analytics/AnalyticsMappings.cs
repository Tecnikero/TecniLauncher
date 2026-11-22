using System.ComponentModel;
using System.Text.RegularExpressions;

namespace TecniLauncher.Analytics
{
    /// <summary>Convierte datos del launcher a los valores cerrados que acepta la API.</summary>
    public static class AnalyticsMappings
    {
        public const string CategoryKey = "analytics_category";

        public static readonly IReadOnlyList<string> ErrorCategories = new[]
        {
            "authentication", "java_missing", "invalid_configuration", "download_failure",
            "process_start_failure", "timeout", "unknown",
        };

        // Mismo patrón que la especificación, con \z en vez de $ (en .NET, $ acepta un "\n" final) y solo dígitos ASCII.
        private static readonly Regex MinecraftVersionRegex = new(
            @"^([0-9]{1,2}\.[0-9]{1,2}(\.[0-9]{1,2})?(-(pre|rc|snapshot)-?[0-9]{1,2})?|[0-9]{2}w[0-9]{2}[a-z])\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static string LoaderType(string? loader)
        {
            if (string.IsNullOrWhiteSpace(loader)) return "unknown";
            return loader.Trim().ToLowerInvariant() switch
            {
                "vanilla" => "vanilla",
                "forge" => "forge",
                "neoforge" => "neoforge",
                "fabric" => "fabric",
                "quilt" => "quilt",
                _ => "other",
            };
        }

        public static string MinecraftVersion(string? version)
        {
            if (string.IsNullOrEmpty(version)) return "other";
            try { return MinecraftVersionRegex.IsMatch(version) ? version : "other"; }
            catch (RegexMatchTimeoutException) { return "other"; }
        }

        public static bool IsValidErrorCategory(string? category) =>
            category != null && ErrorCategories.Contains(category);

        public static string ErrorCategory(Exception? ex)
        {
            if (ex == null) return "unknown";
            try
            {
                if (ex.Data[CategoryKey] is string marcada && IsValidErrorCategory(marcada)) return marcada;
            }
            catch { }

            return ex switch
            {
                HttpRequestException => "download_failure",
                Win32Exception => "process_start_failure",
                TimeoutException or TaskCanceledException => "timeout",
                FormatException or ArgumentException or KeyNotFoundException => "invalid_configuration",
                _ when EsJsonException(ex) => "invalid_configuration",
                _ => "unknown",
            };
        }

        // System.Text.Json.JsonException y Newtonsoft.Json.JsonException (y sus derivadas), sin depender de Newtonsoft.
        private static bool EsJsonException(Exception ex)
        {
            for (Type? t = ex.GetType(); t != null && t != typeof(Exception); t = t.BaseType)
                if (t.Name == "JsonException") return true;
            return false;
        }
    }
}
