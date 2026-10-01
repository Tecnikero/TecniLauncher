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
        [JsonProperty("project_id")] public string Id { get; set; } = "";
        [JsonProperty("title")] public string Titulo { get; set; } = "";
        [JsonProperty("description")] public string Descripcion { get; set; } = "";
        [JsonProperty("author")] public string Autor { get; set; } = "";
        [JsonProperty("icon_url")] public string IconoUrl { get; set; } = "";
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
                var data = JsonConvert.DeserializeObject<ModpackDetalleCompleto>(json);
                return data?.DescripcionCompleta ?? "No hay descripción disponible.";
            }
            catch { return "Error al cargar la descripción."; }
        }

        public static async Task<List<ModpackVersion>> ObtenerVersionesAsync(string idProyecto)
        {
            try
            {
                string json = await _http.GetStringAsync($"https://api.modrinth.com/v2/project/{idProyecto}/version");
                var versiones = JsonConvert.DeserializeObject<List<ModpackVersion>>(json);
                return versiones ?? new();
            }
            catch { return new(); }
        }

        private static readonly HashSet<string> HostsPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            "cdn.modrinth.com",
            "github.com",
            "raw.githubusercontent.com",
            "gitlab.com"
        };
        private static readonly HashSet<string> HostsTecniClient = new(StringComparer.OrdinalIgnoreCase)
        {
            "pub-1f134b235eba49c2a2a60d16a2f41550.r2.dev"
        };

        public static bool EsUrlTecniClientPermitida(string? url)
        {
            if (EsUrlPermitida(url)) return true;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps && HostsTecniClient.Contains(uri.Host);
        }

        public static bool EsUrlPermitida(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps && HostsPermitidos.Contains(uri.Host);
        }

        public static string? ResolverRutaSegura(string rutaBase, string rutaRelativa)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rutaBase) || string.IsNullOrWhiteSpace(rutaRelativa)) return null;

                string baseCompleta = Path.GetFullPath(rutaBase)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                string destino = Path.GetFullPath(Path.Combine(baseCompleta, rutaRelativa));

                return destino.StartsWith(baseCompleta, StringComparison.OrdinalIgnoreCase) ? destino : null;
            }
            catch
            {
                return null;
            }
        }

        private static void ExtraerZipSeguro(string rutaZip, string directorioDestino)
        {
            using var zip = ZipFile.OpenRead(rutaZip);

            foreach (var entrada in zip.Entries)
            {
                string? destino = ResolverRutaSegura(directorioDestino, entrada.FullName);
                if (destino == null)
                    throw new InvalidDataException($"Entrada de zip insegura bloqueada: {entrada.FullName}");

                if (string.IsNullOrEmpty(entrada.Name))
                {
                    Directory.CreateDirectory(destino);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                entrada.ExtractToFile(destino, overwrite: true);
            }
        }

        public static async Task<MrPackIndex?> PrepararInstalacionModpackAsync(
            string urlDescarga,
            string rutaCarpetaPerfil,
            IProgress<(int actual, int total, string nombre)>? progreso = null)
        {
            if (!EsUrlPermitida(urlDescarga))
            {
                System.Diagnostics.Debug.WriteLine($"Modpack rechazado: host no permitido: {urlDescarga}");
                return null;
            }

            string rutaTemp = Path.Combine(Path.GetTempPath(), "TecniLauncher", "TempModpack");

            if (Directory.Exists(rutaTemp)) Directory.Delete(rutaTemp, true);
            Directory.CreateDirectory(rutaTemp);

            string rutaMrPack = Path.Combine(rutaTemp, "paquete.zip");
            await AppHttpClient.DescargarArchivoAsync(urlDescarga, rutaMrPack, TimeSpan.FromMinutes(30));

            try
            {
                ExtraerZipSeguro(rutaMrPack, rutaTemp);
            }
            catch (InvalidDataException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Modpack rechazado al extraer: {ex.Message}");
                return null;
            }

            string rutaIndex = Path.Combine(rutaTemp, "modrinth.index.json");
            if (!File.Exists(rutaIndex)) return null;

            var index = JsonConvert.DeserializeObject<MrPackIndex>(await File.ReadAllTextAsync(rutaIndex));
            if (index == null) return null;

            foreach (var mod in index.ArchivosMod ?? new())
            {
                if (ResolverRutaSegura(rutaCarpetaPerfil, mod.RutaDestino) == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Modpack rechazado: ruta fuera del perfil: {mod.RutaDestino}");
                    return null;
                }

                if (mod.UrlsDescarga == null || mod.UrlsDescarga.Count == 0 || !mod.UrlsDescarga.All(EsUrlPermitida))
                {
                    System.Diagnostics.Debug.WriteLine($"Modpack rechazado: URL de descarga no permitida en: {mod.RutaDestino}");
                    return null;
                }
            }

            if (index.Dependencias != null && index.Dependencias.ContainsKey("quilt-loader"))
                throw new NotSupportedException("Este modpack requiere Quilt, que el launcher todavía no soporta.");

            LimpiarModsAnteriores(rutaCarpetaPerfil);
            foreach (string carpeta in new[] { "overrides", "client-overrides" })
            {
                string rutaOverrides = Path.Combine(rutaTemp, carpeta);
                if (Directory.Exists(rutaOverrides))
                    CopiarDirectorio(rutaOverrides, rutaCarpetaPerfil);
            }

            return index;
        }
        private static void LimpiarModsAnteriores(string rutaCarpetaPerfil)
        {
            string? rutaMods = ResolverRutaSegura(rutaCarpetaPerfil, "mods");
            if (rutaMods == null || !Directory.Exists(rutaMods)) return;

            foreach (string archivo in Directory.GetFiles(rutaMods))
                File.Delete(archivo);
            foreach (string carpeta in Directory.GetDirectories(rutaMods))
                Directory.Delete(carpeta, true);
        }
        public static (string Loader, string Version) DetectarLoader(Dictionary<string, string>? dependencias)
        {
            if (dependencias != null)
            {
                if (dependencias.TryGetValue("fabric-loader", out var f)) return ("Fabric", f);
                if (dependencias.TryGetValue("neoforge", out var n)) return ("NeoForge", n);
                if (dependencias.TryGetValue("forge", out var fg)) return ("Forge", fg);
            }
            return ("Vanilla", "");
        }

        public static async Task<List<string>> DescargarArchivosModpackAsync(
            MrPackIndex receta,
            string rutaBase,
            IProgress<(int actual, int total, string nombre)>? progreso = null)
        {
            var archivos = receta.ArchivosMod ?? new();
            int total = archivos.Count;
            int procesados = 0;
            var fallos = new System.Collections.Concurrent.ConcurrentBag<string>();

            using var semaforo = new SemaphoreSlim(4, 4);

            var tareas = archivos.Select(async mod =>
            {
                await semaforo.WaitAsync();
                try
                {
                    string? destino = ResolverRutaSegura(rutaBase, mod.RutaDestino);
                    if (destino == null)
                        throw new InvalidDataException($"Ruta fuera del directorio base bloqueada: {mod.RutaDestino}");
                    Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

                    Exception? ultimo = null;
                    bool ok = false;
                    foreach (string url in mod.UrlsDescarga.Where(EsUrlPermitida))
                    {
                        try { await AppHttpClient.DescargarArchivoAsync(url, destino); ok = true; break; }
                        catch (Exception ex) { ultimo = ex; }
                    }
                    if (!ok) throw ultimo ?? new InvalidDataException($"Sin URL de descarga permitida: {mod.RutaDestino}");
                }
                catch (Exception ex)
                {
                    fallos.Add(Path.GetFileName(mod.RutaDestino));
                    System.Diagnostics.Debug.WriteLine($"Error descargando {mod.RutaDestino}: {ex.Message}");
                }
                finally
                {
                    int actual = Interlocked.Increment(ref procesados);
                    progreso?.Report((actual, total, Path.GetFileName(mod.RutaDestino)));
                    semaforo.Release();
                }
            });

            await Task.WhenAll(tareas);
            return fallos.ToList();
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
            [JsonProperty("id")] public string Id { get; set; } = "";
            [JsonProperty("version_number")] public string NumeroVersion { get; set; } = "";
            [JsonProperty("game_versions")] public List<string> VersionesMinecraft { get; set; } = new();
            [JsonProperty("files")] public List<ModpackFile> Archivos { get; set; } = new();

            public string NombreAgradable =>
                $"{NumeroVersion} - [{string.Join(", ", VersionesMinecraft)}]";
        }

        public class ModpackFile
        {
            [JsonProperty("url")] public string UrlDescarga { get; set; } = "";
            [JsonProperty("primary")] public bool EsPrimario { get; set; }
        }

        public class MrPackIndex
        {
            [JsonProperty("name")] public string Nombre { get; set; } = "";
            [JsonProperty("dependencies")] public Dictionary<string, string> Dependencias { get; set; } = new();
            [JsonProperty("files")] public List<MrPackFile> ArchivosMod { get; set; } = new();
        }

        public class MrPackFile
        {
            [JsonProperty("path")] public string RutaDestino { get; set; } = "";
            [JsonProperty("downloads")] public List<string> UrlsDescarga { get; set; } = new();
        }
    }
}