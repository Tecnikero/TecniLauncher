using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace TecniLauncher
{
    public class ModInfo
    {
        public string title { get; set; }
        public string description { get; set; }
        public string icon_url { get; set; }
        public string project_id { get; set; }
        public string author { get; set; }
        public string slug { get; set; }
    }

    public class ModVersion
    {
        public string NombreVersion { get; set; }
        public string Tipo { get; set; }
        public string Fecha { get; set; }
        public string NombreArchivo { get; set; }
        public string UrlDescarga { get; set; }
        public string ColorTipo => Tipo == "release" ? "#2ecc71" : (Tipo == "beta" ? "#f1c40f" : "#e74c3c");
    }

    public class ModrinthAPI
    {
        private static readonly HttpClient client = new HttpClient();

        private static void AsegurarUserAgent()
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0 (Johan/t3cnikero)");
        }

        public static async Task<List<ModInfo>> BuscarMods(string busqueda, string loader, string version)
        {
            if (string.IsNullOrEmpty(loader) || string.IsNullOrEmpty(version)) return new List<ModInfo>();

            try
            {
                AsegurarUserAgent();
                string facets = $"[[\"categories:{loader.ToLower()}\"],[\"versions:{version}\"],[\"project_type:mod\"]]";
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(busqueda)}&facets={facets}&limit=20";
                string json = await client.GetStringAsync(url);

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var hits = doc.RootElement.GetProperty("hits");
                    return JsonSerializer.Deserialize<List<ModInfo>>(hits.GetRawText()) ?? new List<ModInfo>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error al buscar en Modrinth: " + ex.Message);
                return new List<ModInfo>();
            }
        }

        public static async Task<List<ModVersion>> ObtenerListaVersiones(string projectId, string versionMC, string loader)
        {
            if (string.IsNullOrEmpty(projectId)) return new List<ModVersion>();

            try
            {
                AsegurarUserAgent();
                string url = $"https://api.modrinth.com/v2/project/{projectId}/version?loaders=[\"{loader.ToLower()}\"]&game_versions=[\"{versionMC}\"]";
                string json = await client.GetStringAsync(url);
                var listaResultados = new List<ModVersion>();

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var versionJson in doc.RootElement.EnumerateArray())
                        {
                            var archivos = versionJson.GetProperty("files");
                            JsonElement archivoTarget;
                            try
                            {
                                archivoTarget = archivos.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("primary", out var p) && p.GetBoolean());
                                if (archivoTarget.ValueKind == JsonValueKind.Undefined)
                                    archivoTarget = archivos.EnumerateArray().First();
                            }
                            catch { continue; }

                            string fechaRaw = versionJson.GetProperty("date_published").GetString();
                            string fechaBonita = DateTime.TryParse(fechaRaw, out DateTime d) ? d.ToString("dd/MM/yyyy") : fechaRaw;

                            listaResultados.Add(new ModVersion
                            {
                                NombreVersion = versionJson.GetProperty("name").GetString(),
                                Tipo = versionJson.GetProperty("version_type").GetString(),
                                Fecha = fechaBonita,
                                NombreArchivo = archivoTarget.GetProperty("filename").GetString(),
                                UrlDescarga = archivoTarget.GetProperty("url").GetString()
                            });
                        }
                    }
                }
                return listaResultados;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo versiones: {ex.Message}");
                return new List<ModVersion>();
            }
        }
        public static async Task<string> ObtenerLinkDescarga(string projectId, string versionMC, string loader, string versionFija = null)
        {
            var versiones = await ObtenerListaVersiones(projectId, versionMC, loader);

            if (!string.IsNullOrEmpty(versionFija))
            {
                var target = versiones.FirstOrDefault(v => v.NombreVersion.Contains(versionFija));
                return target?.UrlDescarga;
            }

            if (versiones.Count > 0)
            {
                return versiones[0].UrlDescarga;
            }
            return null;
        }
    }
}