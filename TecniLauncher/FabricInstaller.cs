using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using CmlLib.Core;

namespace TecniLauncher
{
    public class FabricInstaller
    {
        private readonly MinecraftLauncher _launcher;
        private static readonly HttpClient _httpClient = new HttpClient();

        static FabricInstaller()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0");
        }

        public FabricInstaller(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public async Task<List<string>> ObtenerVersiones(string mcVersion)
        {
            try
            {
                string versionLimpia = mcVersion.Replace("release", "").Replace("snapshot", "").Trim();
                string url = $"https://meta.fabricmc.net/v2/versions/loader/{versionLimpia}";

                using (var response = await _httpClient.GetAsync(url))
                {
                    if (!response.IsSuccessStatusCode) return new List<string>();

                    string json = await response.Content.ReadAsStringAsync();
                    var lista = new List<string>();

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        foreach (var elemento in doc.RootElement.EnumerateArray())
                        {
                            if (elemento.TryGetProperty("loader", out var loaderProp) &&
                                loaderProp.TryGetProperty("version", out var versionProp))
                            {
                                lista.Add(versionProp.GetString());
                            }
                        }
                    }
                    return lista;
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<string> InstallAsync(string mcVersion, string loaderVersionEspecifica = null)
        {
            if (string.IsNullOrEmpty(mcVersion))
            {
                throw new Exception("Error crítico: La versión de Minecraft no fue especificada (mcVersion es null).");
            }
            string versionLimpia = mcVersion.Replace("release", "").Replace("snapshot", "").Trim();

            if (string.IsNullOrEmpty(loaderVersionEspecifica))
            {
                var versiones = await ObtenerVersiones(versionLimpia);
                if (versiones.Count > 0) loaderVersionEspecifica = versiones[0];
                else throw new Exception($"No se encontraron cargadores de Fabric para la versión {versionLimpia}.");
            }

            string versionId = $"fabric-loader-{loaderVersionEspecifica}-{versionLimpia}";

            var versionPath = Path.Combine(_launcher.MinecraftPath.Versions, versionId, $"{versionId}.json");

            if (File.Exists(versionPath)) return versionId;

            string profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{versionLimpia}/{loaderVersionEspecifica}/profile/json";

            try
            {
                var profileJson = await _httpClient.GetStringAsync(profileUrl);
                var versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionId);
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(versionPath, profileJson);
                return versionId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al descargar el perfil de Fabric: {ex.Message}");
            }
        }
    }
}