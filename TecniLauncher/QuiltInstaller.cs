using TecniLauncher.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;

namespace TecniLauncher
{
    /// <summary>
    /// Instala Quilt Loader usando la API meta de QuiltMC (mismo esquema que la de Fabric):
    /// descarga el perfil JSON de la versión y lo deja en versions/ para que CmlLib lo lance.
    /// </summary>
    public class QuiltInstaller
    {
        private const string META = "https://meta.quiltmc.org/v3/versions/loader";

        private readonly MinecraftLauncher _launcher;
        private static readonly HttpClient _httpClient = CrearCliente();

        private static HttpClient CrearCliente()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0");
            return http;
        }

        public QuiltInstaller(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        private static string Limpiar(string mcVersion) =>
            mcVersion.Replace("release", "").Replace("snapshot", "").Trim();

        private static bool EsEstable(string version) => !version.Contains('-');

        private static Version Numerica(string version)
        {
            string s = version;
            int corte = s.IndexOf('-');
            if (corte >= 0) s = s[..corte];
            return Version.TryParse(s, out var v) ? v : new Version(0, 0);
        }

        /// <summary>Versiones de Quilt Loader para esa versión de Minecraft: estables primero, de la más nueva a la más antigua.</summary>
        public async Task<List<string>> ObtenerVersiones(string mcVersion, CancellationToken ct = default)
        {
            try
            {
                string url = $"{META}/{Uri.EscapeDataString(Limpiar(mcVersion))}";

                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return new List<string>();

                string json = await response.Content.ReadAsStringAsync(ct);
                var lista = new List<string>();

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return lista;

                    foreach (var elemento in doc.RootElement.EnumerateArray())
                    {
                        if (elemento.TryGetProperty("loader", out var loaderProp) &&
                            loaderProp.TryGetProperty("version", out var versionProp))
                        {
                            string? v = versionProp.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) lista.Add(v);
                        }
                    }
                }

                return lista
                    .Distinct()
                    .OrderByDescending(EsEstable)
                    .ThenByDescending(Numerica)
                    .ThenByDescending(v => v, StringComparer.Ordinal)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exLog)
            {
                LogService.Warn("No se pudo obtener la lista de versiones de Quilt", exLog);
                return new List<string>();
            }
        }

        public async Task<string> InstallAsync(string mcVersion, string? loaderVersionEspecifica = null)
        {
            if (string.IsNullOrEmpty(mcVersion))
                throw new Exception("Error crítico: La versión de Minecraft no fue especificada (mcVersion es null).");

            string versionLimpia = Limpiar(mcVersion);

            if (string.IsNullOrEmpty(loaderVersionEspecifica))
            {
                var versiones = await ObtenerVersiones(versionLimpia);
                if (versiones.Count > 0) loaderVersionEspecifica = versiones[0];
                else throw new Exception($"No se encontraron cargadores de Quilt para la versión {versionLimpia}.");
            }

            string versionId = $"quilt-loader-{loaderVersionEspecifica}-{versionLimpia}";
            string versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionId);
            string versionPath = Path.Combine(versionDir, $"{versionId}.json");

            if (File.Exists(versionPath)) return versionId;

            string profileUrl = $"{META}/{Uri.EscapeDataString(versionLimpia)}/{Uri.EscapeDataString(loaderVersionEspecifica)}/profile/json";

            try
            {
                string profileJson = await _httpClient.GetStringAsync(profileUrl);

                // El id del perfil debe coincidir con la carpeta para que CmlLib lo encuentre.
                using (JsonDocument doc = JsonDocument.Parse(profileJson))
                {
                    if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetString() is string idReal && idReal != versionId && System.Text.RegularExpressions.Regex.IsMatch(idReal, @"^[A-Za-z0-9._+\-]{1,100}$"))
                    {
                        versionId = idReal;
                        versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionId);
                        versionPath = Path.Combine(versionDir, $"{versionId}.json");
                    }
                }

                Directory.CreateDirectory(versionDir);
                File.WriteAllText(versionPath, profileJson);
                return versionId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al descargar el perfil de Quilt: {ex.Message}");
            }
        }
    }
}
