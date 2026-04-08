using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;

namespace TecniLauncher
{
    public class ModrinthSearchResponse
    {
        [JsonProperty("hits")]
        public List<ModpackProject> Resultados { get; set; }
    }

    public class ModpackProject
    {
        [JsonProperty("project_id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Titulo { get; set; }

        [JsonProperty("description")]
        public string Descripcion { get; set; }

        [JsonProperty("author")]
        public string Autor { get; set; }

        [JsonProperty("icon_url")]
        public string IconoUrl { get; set; }
    }

    public static class ModpacksApi
    {
        private static readonly HttpClient _client = new HttpClient();

        static ModpacksApi()
        {

            _client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0 (Johan/t3cnikero)");
        }

        public static async Task<List<ModpackProject>> BuscarModpacksAsync(string busqueda, int limite = 20)
        {
            try
            {
                string tipoOrden = string.IsNullOrWhiteSpace(busqueda) ? "downloads" : "relevance";

                string url = $"https://api.modrinth.com/v2/search?query={busqueda}&index={tipoOrden}&facets=[[\"project_type:modpack\"]]&limit={limite}";

                string jsonRespuesta = await _client.GetStringAsync(url);
                var datos = JsonConvert.DeserializeObject<ModrinthSearchResponse>(jsonRespuesta);

                return datos?.Resultados ?? new List<ModpackProject>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error buscando modpacks: {ex.Message}");
                return new List<ModpackProject>();
            }
        }
        public class ModpackDetalleCompleto
        {
            [JsonProperty("body")]
            public string DescripcionCompleta { get; set; }
        }

        public static async Task<string> ObtenerDescripcionCompletaAsync(string idProyecto)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{idProyecto}";
                string jsonRespuesta = await _client.GetStringAsync(url);

                var datos = JsonConvert.DeserializeObject<ModpackDetalleCompleto>(jsonRespuesta);
                return datos?.DescripcionCompleta ?? "No hay descripción disponible.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener detalles: {ex.Message}");
                return "Error al cargar la descripción del modpack.";
            }
        }
        public class ModpackVersion
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("version_number")]
            public string NumeroVersion { get; set; }

            [JsonProperty("game_versions")]
            public List<string> VersionesMinecraft { get; set; }

            [JsonProperty("files")]
            public List<ModpackFile> Archivos { get; set; }

            public string NombreAgradable => $"{NumeroVersion} - [{string.Join(", ", VersionesMinecraft ?? new List<string>())}]";
        }

        public class ModpackFile
        {
            [JsonProperty("url")]
            public string UrlDescarga { get; set; }

            [JsonProperty("primary")]
            public bool EsPrimario { get; set; }
        }

        public static async Task<List<ModpackVersion>> ObtenerVersionesAsync(string idProyecto)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{idProyecto}/version";
                string jsonRespuesta = await _client.GetStringAsync(url);

                var versiones = JsonConvert.DeserializeObject<List<ModpackVersion>>(jsonRespuesta);
                return versiones ?? new List<ModpackVersion>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error obteniendo versiones: {ex.Message}");
                return new List<ModpackVersion>();
            }
        }

        public class MrPackIndex
        {
            [JsonProperty("name")]
            public string Nombre { get; set; }

            [JsonProperty("dependencies")]
            public Dictionary<string, string> Dependencias { get; set; }

            [JsonProperty("files")]
            public List<MrPackFile> ArchivosMod { get; set; }
        }

        public class MrPackFile
        {
            [JsonProperty("path")]
            public string RutaDestino { get; set; }

            [JsonProperty("downloads")]
            public List<string> UrlsDescarga { get; set; }
        }

        public static async Task<MrPackIndex> PrepararInstalacionModpackAsync(string urlDescarga, string rutaCarpetaPerfil)
        {
            try
            {
                string rutaTemp = Path.Combine(Path.GetTempPath(), "TecniLauncher", "TempModpack");

                if (Directory.Exists(rutaTemp)) Directory.Delete(rutaTemp, true);
                Directory.CreateDirectory(rutaTemp);

                string rutaMrPack = Path.Combine(rutaTemp, "paquete.zip");

                var bytes = await _client.GetByteArrayAsync(urlDescarga);
                File.WriteAllBytes(rutaMrPack, bytes);

                ZipFile.ExtractToDirectory(rutaMrPack, rutaTemp);

                string rutaOverrides = Path.Combine(rutaTemp, "overrides");
                if (Directory.Exists(rutaOverrides))
                {
                    CopiarDirectorio(rutaOverrides, rutaCarpetaPerfil);
                }

                string rutaIndex = Path.Combine(rutaTemp, "modrinth.index.json");
                string jsonContenido = File.ReadAllText(rutaIndex);

                var receta = JsonConvert.DeserializeObject<MrPackIndex>(jsonContenido);

                return receta;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al preparar modpack: {ex.Message}");
                return null;
            }
        }
        private static void CopiarDirectorio(string dirOrigen, string dirDestino)
        {
            Directory.CreateDirectory(dirDestino);

            foreach (var archivo in Directory.GetFiles(dirOrigen))
            {
                string destinoArchivo = Path.Combine(dirDestino, Path.GetFileName(archivo));
                File.Copy(archivo, destinoArchivo, true);
            }

            foreach (var carpeta in Directory.GetDirectories(dirOrigen))
            {
                string destinoCarpeta = Path.Combine(dirDestino, Path.GetFileName(carpeta));
                CopiarDirectorio(carpeta, destinoCarpeta);
            }
        }
    }
}