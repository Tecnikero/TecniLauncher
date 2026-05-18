using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using TecniLauncher.Services;

namespace TecniLauncher
{
    public class ModrinthSearchResponse
    {
        [JsonProperty("hits")]
        public List<ModpackProject> Resultados { get; set; } = new();
    }

    public class ModpackProject
    {
        [JsonProperty("project_id")] public string Id          { get; set; } = "";
        [JsonProperty("title")]       public string Titulo      { get; set; } = "";
        [JsonProperty("description")] public string Descripcion { get; set; } = "";
        [JsonProperty("author")]      public string Autor       { get; set; } = "";
        [JsonProperty("icon_url")]    public string IconoUrl    { get; set; } = "";
    }

    public static class ModpacksApi
    {
        private static readonly HttpClient _http = AppHttpClient.Instance;


        public static async Task<List<ModpackProject>> BuscarModpacksAsync(
    string busqueda, int offset = 0, int limite = 20)
        {
            try
            {
                string orden = string.IsNullOrWhiteSpace(busqueda) ? "downloads" : "relevance";
                string facets = Uri.EscapeDataString("[[\"project_type:modpack\"]]");
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(busqueda)}&index={orden}&facets={facets}&limit={limite}&offset={offset}";

                string json = await _http.GetStringAsync(url);
                var datos = JsonConvert.DeserializeObject<ModrinthSearchResponse>(json);
                return datos?.Resultados ?? new();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error buscando modpacks: {ex.Message}");
                return new();
            }
        }

        public static async Task<string> ObtenerDescripcionCompletaAsync(string idProyecto)
        {
            try
            {
                string json = await _http.GetStringAsync($"https://api.modrinth.com/v2/project/{idProyecto}");
                var    data = JsonConvert.DeserializeObject<ModpackDetalleCompleto>(json);
                return data?.DescripcionCompleta ?? "No hay descripción disponible.";
            }
            catch { return "Error al cargar la descripción."; }
        }

        public static async Task<List<ModpackVersion>> ObtenerVersionesAsync(string idProyecto)
        {
            try
            {
                string json      = await _http.GetStringAsync($"https://api.modrinth.com/v2/project/{idProyecto}/version");
                var    versiones = JsonConvert.DeserializeObject<List<ModpackVersion>>(json);
                return versiones ?? new();
            }
            catch { return new(); }
        }

        public static async Task<MrPackIndex?> PrepararInstalacionModpackAsync(
            string urlDescarga,
            string rutaCarpetaPerfil,
            IProgress<(int actual, int total, string nombre)>? progreso = null)
        {
            string rutaTemp = Path.Combine(Path.GetTempPath(), "TecniLauncher", "TempModpack");

            if (Directory.Exists(rutaTemp)) Directory.Delete(rutaTemp, true);
            Directory.CreateDirectory(rutaTemp);

            string rutaMrPack = Path.Combine(rutaTemp, "paquete.zip");
            byte[] bytes      = await _http.GetByteArrayAsync(urlDescarga);
            await File.WriteAllBytesAsync(rutaMrPack, bytes);

            ZipFile.ExtractToDirectory(rutaMrPack, rutaTemp);

            string rutaOverrides = Path.Combine(rutaTemp, "overrides");
            if (Directory.Exists(rutaOverrides))
                CopiarDirectorio(rutaOverrides, rutaCarpetaPerfil);

            string rutaIndex = Path.Combine(rutaTemp, "modrinth.index.json");
            if (!File.Exists(rutaIndex)) return null;

            return JsonConvert.DeserializeObject<MrPackIndex>(await File.ReadAllTextAsync(rutaIndex));
        }

        public static async Task DescargarArchivosModpackAsync(
            MrPackIndex receta,
            string rutaBase,
            IProgress<(int actual, int total, string nombre)>? progreso = null)
        {
            var archivos = receta.ArchivosMod ?? new();
            int total    = archivos.Count;
            int descargados = 0;

            var semaforo = new SemaphoreSlim(4, 4);

            var tareas = archivos.Select(async mod =>
            {
                await semaforo.WaitAsync();
                try
                {
                    string destino = Path.Combine(rutaBase, mod.RutaDestino);
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

                    byte[] datos = await _http.GetByteArrayAsync(mod.UrlsDescarga[0]);
                    await File.WriteAllBytesAsync(destino, datos);

                    int actual = Interlocked.Increment(ref descargados);
                    progreso?.Report((actual, total, Path.GetFileName(destino)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error descargando mod: {ex.Message}");
                }
                finally
                {
                    semaforo.Release();
                }
            });

            await Task.WhenAll(tareas);
        }

        private static void CopiarDirectorio(string origen, string destino)
        {
            Directory.CreateDirectory(destino);
            foreach (string archivo in Directory.GetFiles(origen))
                File.Copy(archivo, Path.Combine(destino, Path.GetFileName(archivo)), overwrite: true);
            foreach (string carpeta in Directory.GetDirectories(origen))
                CopiarDirectorio(carpeta, Path.Combine(destino, Path.GetFileName(carpeta)));
        }


        public class ModpackDetalleCompleto
        {
            [JsonProperty("body")] public string DescripcionCompleta { get; set; } = "";
        }

        public class ModpackVersion
        {
            [JsonProperty("id")]            public string Id                { get; set; } = "";
            [JsonProperty("version_number")] public string NumeroVersion    { get; set; } = "";
            [JsonProperty("game_versions")]  public List<string> VersionesMinecraft { get; set; } = new();
            [JsonProperty("files")]          public List<ModpackFile> Archivos       { get; set; } = new();

            public string NombreAgradable =>
                $"{NumeroVersion} - [{string.Join(", ", VersionesMinecraft)}]";
        }

        public class ModpackFile
        {
            [JsonProperty("url")]     public string UrlDescarga { get; set; } = "";
            [JsonProperty("primary")] public bool   EsPrimario  { get; set; }
        }

        public class MrPackIndex
        {
            [JsonProperty("name")]         public string Nombre      { get; set; } = "";
            [JsonProperty("dependencies")] public Dictionary<string, string> Dependencias { get; set; } = new();
            [JsonProperty("files")]        public List<MrPackFile> ArchivosMod { get; set; } = new();
        }

        public class MrPackFile
        {
            [JsonProperty("path")]      public string RutaDestino  { get; set; } = "";
            [JsonProperty("downloads")] public List<string> UrlsDescarga { get; set; } = new();
        }
    }
}
