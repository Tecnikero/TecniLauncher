using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TecniLauncher.Services;
using static TecniLauncher.ModpacksApi;

namespace TecniLauncher
{
    public enum FuenteContenido { Modrinth, CurseForge }

    /// <summary>
    /// Cliente de la API oficial de CurseForge (api.curseforge.com/v1).
    /// Devuelve los mismos modelos que <see cref="ModrinthAPI"/> para que la UI no tenga que distinguir.
    /// Requiere una API key de https://console.curseforge.com (variable CURSEFORGE_API_KEY,
    /// o un archivo "curseforge.key" junto al ejecutable que se importa cifrado al primer arranque).
    /// </summary>
    public static class CurseForgeAPI
    {
        public const string ClaveSecreto = "curseforge_api_key";
        private const string Base = "https://api.curseforge.com/v1";
        private const int GameId = 432; // Minecraft

        public const int ClaseMods = 6;
        public const int ClaseTexturas = 12;
        public const int ClaseShaders = 6552;
        public const int ClaseModpacks = 4471;
        public const int ClaseMundos = 17;

        private static readonly HttpClient client = AppHttpClient.Instance;
        private static string? _apiKey;

        public static string ApiKey
        {
            get
            {
                if (!string.IsNullOrEmpty(_apiKey)) return _apiKey;
                string? env = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
                _apiKey = !string.IsNullOrWhiteSpace(env) ? env.Trim() : SecretsManager.ObtenerSecreto(ClaveSecreto);
                return _apiKey ?? "";
            }
        }

        public static bool Disponible => !string.IsNullOrEmpty(ApiKey);

        /// <summary>True si la última búsqueda fue rechazada con 403 (key sin permiso para /mods/search).</summary>
        public static bool BusquedaRechazada { get; private set; }

        private static bool Es403(Exception ex) =>
            ex is HttpRequestException h && h.StatusCode == System.Net.HttpStatusCode.Forbidden;

        public static int ClaseDe(TipoComplemento t) => t switch
        {
            TipoComplemento.Textura => ClaseTexturas,
            TipoComplemento.Shader => ClaseShaders,
            _ => ClaseMods
        };

        public static int? LoaderType(string? loader) => (loader ?? "").ToLowerInvariant() switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            "neoforge" => 6,
            _ => null
        };

        #region HTTP

        private static async Task<JsonDocument> GetAsync(string ruta)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Base + ruta);
            req.Headers.Add("x-api-key", ApiKey);
            req.Headers.Add("Accept", "application/json");
            using var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }

        private static async Task<JsonDocument> PostAsync(string ruta, object cuerpo)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Base + ruta);
            req.Headers.Add("x-api-key", ApiKey);
            req.Headers.Add("Accept", "application/json");
            req.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }

        private static string? Str(JsonElement e, string nombre) =>
            e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static long Num(JsonElement e, string nombre) =>
            e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : 0;

        private static bool Bool(JsonElement e, string nombre) =>
            e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.True;

        private static JsonElement Data(JsonDocument doc) => doc.RootElement.GetProperty("data");

        #endregion

        #region Búsqueda y categorías

        private static string UrlBusqueda(int claseId, string busqueda, string? version, int? loaderType, string? categoriaId, int offset, int limite)
        {
            // La API rechaza index + pageSize > 10000.
            limite = Math.Clamp(limite, 1, 50);
            if (offset + limite > 10000) limite = Math.Max(0, 10000 - offset);

            var sb = new StringBuilder($"/mods/search?gameId={GameId}&classId={claseId}&sortField=2&sortOrder=desc&index={offset}&pageSize={limite}");
            if (!string.IsNullOrWhiteSpace(busqueda)) sb.Append("&searchFilter=").Append(Uri.EscapeDataString(busqueda));
            if (!string.IsNullOrEmpty(version)) sb.Append("&gameVersion=").Append(Uri.EscapeDataString(version));
            if (loaderType != null) sb.Append("&modLoaderType=").Append(loaderType.Value);
            if (!string.IsNullOrEmpty(categoriaId)) sb.Append("&categoryId=").Append(Uri.EscapeDataString(categoriaId));
            return sb.ToString();
        }

        private static ModInfo ParsearProyecto(JsonElement m)
        {
            string? icono = null;
            if (m.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
                icono = Str(logo, "thumbnailUrl") ?? Str(logo, "url");

            string autor = "";
            if (m.TryGetProperty("authors", out var autores) && autores.ValueKind == JsonValueKind.Array && autores.GetArrayLength() > 0)
                autor = Str(autores[0], "name") ?? "";

            return new ModInfo
            {
                project_id = Num(m, "id").ToString(),
                title = Str(m, "name") ?? "",
                description = Str(m, "summary") ?? "",
                icon_url = icono ?? "",
                author = autor,
                slug = Str(m, "slug") ?? "",
                downloads = Num(m, "downloadCount"),
                Fuente = FuenteContenido.CurseForge
            };
        }

        public static async Task<List<ModInfo>> BuscarMods(string busqueda, string? loader, string version, int offset, int limite, string? categoriaId, TipoComplemento tipo)
        {
            if (!Disponible || string.IsNullOrEmpty(version)) return new List<ModInfo>();
            int? loaderType = tipo.UsaLoader() ? LoaderType(loader) : null;
            if (tipo.UsaLoader() && loaderType == null) return new List<ModInfo>();

            try
            {
                string url = UrlBusqueda(ClaseDe(tipo), busqueda, version, loaderType, categoriaId, offset, limite);
                if (url.Contains("pageSize=0")) return new List<ModInfo>();
                using var doc = await GetAsync(url);
                BusquedaRechazada = false;
                return Data(doc).EnumerateArray().Select(ParsearProyecto).ToList();
            }
            catch (Exception ex)
            {
                BusquedaRechazada = Es403(ex);
                LogService.Warn("Error al buscar en CurseForge", ex);
                return new List<ModInfo>();
            }
        }

        private static readonly Dictionary<int, List<FiltroItem>> _cacheCategorias = new();

        public static async Task<List<FiltroItem>> ObtenerCategoriasAsync(int claseId)
        {
            if (_cacheCategorias.TryGetValue(claseId, out var cache)) return cache;
            if (!Disponible) return new List<FiltroItem>();

            try
            {
                using var doc = await GetAsync($"/categories?gameId={GameId}&classId={claseId}");
                var lista = Data(doc).EnumerateArray()
                    .Where(c => !Bool(c, "isClass"))
                    .Select(c => new FiltroItem { Clave = Num(c, "id").ToString(), Texto = Str(c, "name") ?? "" })
                    .Where(c => c.Texto.Length > 0)
                    .OrderBy(c => c.Texto, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _cacheCategorias[claseId] = lista;
                return lista;
            }
            catch (Exception ex)
            {
                LogService.Warn("Error obteniendo categorías de CurseForge", ex);
                return new List<FiltroItem>();
            }
        }

        #endregion

        #region Archivos / versiones

        private static ModVersion? ParsearArchivo(JsonElement f)
        {
            string? url = Str(f, "downloadUrl");
            string? nombre = Str(f, "fileName");
            if (string.IsNullOrEmpty(nombre)) return null;

            string? fechaRaw = Str(f, "fileDate");
            string fecha = DateTime.TryParse(fechaRaw, out var d) ? d.ToString("dd/MM/yyyy") : (fechaRaw ?? "");

            var v = new ModVersion
            {
                ProjectId = Num(f, "modId").ToString(),
                VersionId = Num(f, "id").ToString(),
                NombreVersion = Str(f, "displayName") ?? nombre,
                Tipo = Num(f, "releaseType") switch { 2 => "beta", 3 => "alpha", _ => "release" },
                Fecha = fecha,
                NombreArchivo = nombre,
                UrlDescarga = url ?? "",
                Fuente = FuenteContenido.CurseForge
            };

            if (f.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    if (Num(dep, "relationType") != 3) continue; // 3 = RequiredDependency
                    long modId = Num(dep, "modId");
                    if (modId > 0) v.Requeridas.Add(new DependenciaMod { ProjectId = modId.ToString() });
                }
            }
            return v;
        }

        /// <summary>Versiones descargables (los autores pueden bloquear la descarga desde terceros: esas se omiten).</summary>
        public static async Task<List<ModVersion>> ObtenerListaVersiones(string modId, string versionMC, string? loader)
        {
            var (descargables, _) = await ObtenerListaVersionesConBloqueo(modId, versionMC, loader);
            return descargables;
        }

        private static async Task<(List<ModVersion> Descargables, bool HabiaBloqueadas)> ObtenerListaVersionesConBloqueo(string modId, string versionMC, string? loader)
        {
            var lista = new List<ModVersion>();
            if (!Disponible || string.IsNullOrEmpty(modId)) return (lista, false);

            try
            {
                string url = $"/mods/{Uri.EscapeDataString(modId)}/files?pageSize=50";
                if (!string.IsNullOrEmpty(versionMC)) url += "&gameVersion=" + Uri.EscapeDataString(versionMC);
                int? lt = LoaderType(loader);
                if (lt != null) url += "&modLoaderType=" + lt.Value;

                using var doc = await GetAsync(url);
                bool bloqueadas = false;
                var archivos = Data(doc).EnumerateArray()
                    .OrderByDescending(f => DateTime.TryParse(Str(f, "fileDate"), out var d) ? d : DateTime.MinValue);
                foreach (var f in archivos)
                {
                    if (Bool(f, "isServerPack")) continue;
                    var v = ParsearArchivo(f);
                    if (v == null) continue;
                    if (v.UrlDescarga == "") { bloqueadas = true; continue; }
                    lista.Add(v);
                }
                return (lista, bloqueadas);
            }
            catch (Exception ex)
            {
                LogService.Warn("Error obteniendo archivos de CurseForge", ex);
                return (lista, false);
            }
        }

        private static async Task<string> ObtenerNombreProyectoAsync(string modId)
        {
            try
            {
                using var doc = await GetAsync($"/mods/{Uri.EscapeDataString(modId)}");
                return Str(Data(doc), "name") ?? modId;
            }
            catch { return modId; }
        }

        public static string UrlProyecto(string slug, TipoComplemento tipo) => tipo switch
        {
            TipoComplemento.Textura => $"https://www.curseforge.com/minecraft/texture-packs/{slug}",
            TipoComplemento.Shader => $"https://www.curseforge.com/minecraft/shaders/{slug}",
            _ => $"https://www.curseforge.com/minecraft/mc-mods/{slug}"
        };

        public static async Task<ResultadoDependencias> ResolverDependenciasAsync(ModVersion baseVersion, string versionMC, string loader, int maxMods = 50)
        {
            var resultado = new ResultadoDependencias();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cola = new Queue<ModVersion>();

            if (!string.IsNullOrEmpty(baseVersion.ProjectId)) vistos.Add(baseVersion.ProjectId);
            cola.Enqueue(baseVersion);

            while (cola.Count > 0)
            {
                if (resultado.Descargas.Count >= maxMods)
                {
                    LogService.Info($"Límite de {maxMods} mods alcanzado al resolver dependencias (CurseForge).");
                    break;
                }

                var actual = cola.Dequeue();
                resultado.Descargas.Add(actual);

                foreach (var dep in actual.Requeridas)
                {
                    string? modId = dep.ProjectId;
                    if (string.IsNullOrEmpty(modId) || !vistos.Add(modId)) continue;

                    var (candidatas, bloqueadas) = await ObtenerListaVersionesConBloqueo(modId, versionMC, loader);
                    var elegida = candidatas.FirstOrDefault(v => v.Tipo == "release") ?? candidatas.FirstOrDefault();

                    if (elegida != null) cola.Enqueue(elegida);
                    else
                    {
                        string nombre = await ObtenerNombreProyectoAsync(modId);
                        resultado.SinVersionCompatible.Add(bloqueadas ? Core.T("CF_DescargaManual", nombre) : nombre);
                    }
                }
            }
            return resultado;
        }

        /// <summary>True si el mod tiene archivos compatibles pero su autor bloqueó la descarga desde launchers.</summary>
        public static async Task<bool> TieneSoloDescargasBloqueadasAsync(string modId, string versionMC, string? loader)
        {
            var (lista, bloqueadas) = await ObtenerListaVersionesConBloqueo(modId, versionMC, loader);
            return lista.Count == 0 && bloqueadas;
        }

        #endregion

        #region Identificación de archivos locales (fingerprint murmur2)

        /// <summary>Fingerprint de CurseForge: MurmurHash2 (semilla 1) ignorando bytes de espacio en blanco (9, 10, 13, 32).</summary>
        public static uint CalcularFingerprint(string ruta)
        {
            byte[] bruto = File.ReadAllBytes(ruta);
            var buf = new byte[bruto.Length];
            int len = 0;
            foreach (byte b in bruto)
                if (b != 9 && b != 10 && b != 13 && b != 32) buf[len++] = b;

            unchecked
            {
                const uint m = 0x5bd1e995;
                const int r = 24;
                uint h = 1u ^ (uint)len;
                int i = 0;
                while (len - i >= 4)
                {
                    uint k = (uint)(buf[i] | buf[i + 1] << 8 | buf[i + 2] << 16 | buf[i + 3] << 24);
                    k *= m; k ^= k >> r; k *= m;
                    h *= m; h ^= k;
                    i += 4;
                }
                switch (len - i)
                {
                    case 3: h ^= (uint)buf[i + 2] << 16; goto case 2;
                    case 2: h ^= (uint)buf[i + 1] << 8; goto case 1;
                    case 1: h ^= buf[i]; h *= m; break;
                }
                h ^= h >> 13; h *= m; h ^= h >> 15;
                return h;
            }
        }

        /// <summary>Devuelve fingerprint → modId para los archivos que CurseForge reconoce. null si falla la red.</summary>
        public static async Task<Dictionary<uint, string>?> ObtenerModIdsPorFingerprintAsync(List<uint> fingerprints)
        {
            var resultado = new Dictionary<uint, string>();
            if (fingerprints.Count == 0) return resultado;
            if (!Disponible) return null;

            try
            {
                using var doc = await PostAsync($"/fingerprints/{GameId}", new { fingerprints });
                var data = Data(doc);
                if (data.TryGetProperty("exactMatches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                {
                    foreach (var match in matches.EnumerateArray())
                    {
                        if (!match.TryGetProperty("file", out var file)) continue;
                        long fp = Num(file, "fileFingerprint");
                        long modId = Num(match, "id");
                        if (fp > 0 && modId > 0) resultado[(uint)fp] = modId.ToString();
                    }
                }
                return resultado;
            }
            catch (Exception ex)
            {
                LogService.Warn("Error identificando archivos en CurseForge", ex);
                return null;
            }
        }

        #endregion

        #region Modpacks

        public static async Task<List<ModpackProject>> BuscarModpacksAsync(string busqueda, int offset, int limite)
        {
            if (!Disponible) return new();
            try
            {
                string url = UrlBusqueda(ClaseModpacks, busqueda, null, null, null, offset, limite);
                if (url.Contains("pageSize=0")) return new();
                using var doc = await GetAsync(url);
                BusquedaRechazada = false;
                return Data(doc).EnumerateArray().Select(m =>
                {
                    var info = ParsearProyecto(m);
                    return new ModpackProject
                    {
                        Id = info.project_id,
                        Titulo = info.title,
                        Descripcion = info.description,
                        Autor = info.author,
                        IconoUrl = info.icon_url,
                        Fuente = FuenteContenido.CurseForge
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                BusquedaRechazada = Es403(ex);
                LogService.Warn("Error buscando modpacks en CurseForge", ex);
                return new();
            }
        }

        /// <summary>Descripción del proyecto en HTML (CurseForge no usa Markdown).</summary>
        public static async Task<string> ObtenerDescripcionHtmlAsync(string modId)
        {
            try
            {
                using var doc = await GetAsync($"/mods/{Uri.EscapeDataString(modId)}/description");
                return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
            }
            catch (Exception ex)
            {
                LogService.Warn("Error obteniendo descripción de CurseForge", ex);
                return "";
            }
        }

        private static readonly System.Text.RegularExpressions.Regex RxVersionMc = new(@"^\d+\.\d+(\.\d+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static async Task<List<ModpackVersion>> ObtenerVersionesModpackAsync(string modId)
        {
            try
            {
                using var doc = await GetAsync($"/mods/{Uri.EscapeDataString(modId)}/files?pageSize=50");
                return Data(doc).EnumerateArray()
                    .Where(f => !Bool(f, "isServerPack"))
                    .OrderByDescending(f => DateTime.TryParse(Str(f, "fileDate"), out var d) ? d : DateTime.MinValue)
                    .Select(f => new ModpackVersion
                    {
                        Id = Num(f, "id").ToString(),
                        NumeroVersion = Str(f, "displayName") ?? Str(f, "fileName") ?? "",
                        VersionesMinecraft = f.TryGetProperty("gameVersions", out var gv) && gv.ValueKind == JsonValueKind.Array
                            ? gv.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => RxVersionMc.IsMatch(x)).ToList()
                            : new List<string>(),
                        Archivos = string.IsNullOrEmpty(Str(f, "downloadUrl"))
                            ? new List<ModpackFile>()
                            : new List<ModpackFile> { new ModpackFile { UrlDescarga = Str(f, "downloadUrl")!, EsPrimario = true } }
                    })
                    .Where(v => v.Archivos.Count > 0)
                    .ToList();
            }
            catch (Exception ex)
            {
                LogService.Warn("Error obteniendo versiones de modpack en CurseForge", ex);
                return new();
            }
        }

        private static string CarpetaPorClase(long claseId) => claseId switch
        {
            ClaseTexturas => "resourcepacks",
            ClaseShaders => "shaderpacks",
            ClaseMundos => "saves",
            _ => "mods"
        };

        /// <summary>
        /// Descarga y extrae un modpack de CurseForge (manifest.json) y lo convierte a la misma receta
        /// que usan los .mrpack, para reutilizar la descarga y la creación de perfil.
        /// Los archivos cuyo autor bloquea descargas de terceros quedan sin URL y se reportan como fallos.
        /// </summary>
        public static async Task<MrPackIndex?> PrepararInstalacionModpackAsync(string urlDescarga, string rutaCarpetaPerfil)
        {
            if (!EsUrlPermitida(urlDescarga))
            {
                LogService.Warn($"Modpack CurseForge rechazado: host no permitido: {urlDescarga}");
                return null;
            }

            string rutaTemp = Path.Combine(Path.GetTempPath(), "TecniLauncher", "TempModpackCF");
            if (Directory.Exists(rutaTemp)) Directory.Delete(rutaTemp, true);
            Directory.CreateDirectory(rutaTemp);

            string rutaZip = Path.Combine(rutaTemp, "paquete.zip");
            await AppHttpClient.DescargarArchivoAsync(urlDescarga, rutaZip, TimeSpan.FromMinutes(30));

            try { ExtraerZipSeguro(rutaZip, rutaTemp); }
            catch (InvalidDataException ex)
            {
                LogService.Warn("Modpack CurseForge rechazado al extraer", ex);
                return null;
            }

            string rutaManifest = Path.Combine(rutaTemp, "manifest.json");
            if (!File.Exists(rutaManifest)) return null;

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(rutaManifest));
            var raiz = manifest.RootElement;
            if (!raiz.TryGetProperty("minecraft", out var mc)) return null;

            var index = new MrPackIndex { Nombre = Str(raiz, "name") ?? "" };
            index.Dependencias["minecraft"] = Str(mc, "version") ?? "";

            if (mc.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
            {
                var lista = loaders.EnumerateArray().ToList();
                var principal = lista.FirstOrDefault(l => Bool(l, "primary"));
                if (principal.ValueKind == JsonValueKind.Undefined && lista.Count > 0) principal = lista[0];
                string id = principal.ValueKind == JsonValueKind.Object ? Str(principal, "id") ?? "" : "";
                int guion = id.IndexOf('-');
                if (guion > 0)
                {
                    string nombre = id[..guion].ToLowerInvariant();
                    string version = id[(guion + 1)..];
                    switch (nombre)
                    {
                        case "forge": index.Dependencias["forge"] = version; break;
                        case "neoforge": index.Dependencias["neoforge"] = version; break;
                        case "fabric": index.Dependencias["fabric-loader"] = version; break;
                        case "quilt": throw new NotSupportedException("Este modpack requiere Quilt, que el launcher todavía no soporta.");
                    }
                }
            }

            var referencias = new List<(long ProjectId, long FileId)>();
            if (raiz.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.False) continue;
                    long pid = Num(f, "projectID"), fid = Num(f, "fileID");
                    if (pid > 0 && fid > 0) referencias.Add((pid, fid));
                }
            }

            // Resolver metadatos de archivos y clase (mod / textura / shader) por lotes.
            var archivos = new Dictionary<long, JsonElement>();
            var clases = new Dictionary<long, long>();
            var docs = new List<JsonDocument>();
            try
            {
                foreach (var lote in referencias.Chunk(200))
                {
                    var d1 = await PostAsync("/mods/files", new { fileIds = lote.Select(r => r.FileId).ToArray() });
                    docs.Add(d1);
                    foreach (var f in Data(d1).EnumerateArray()) archivos[Num(f, "id")] = f;

                    var d2 = await PostAsync("/mods", new { modIds = lote.Select(r => r.ProjectId).Distinct().ToArray() });
                    docs.Add(d2);
                    foreach (var m in Data(d2).EnumerateArray()) clases[Num(m, "id")] = Num(m, "classId");
                }

                foreach (var (pid, fid) in referencias)
                {
                    if (!archivos.TryGetValue(fid, out var f))
                    {
                        index.ArchivosMod.Add(new MrPackFile { RutaDestino = $"mods/{pid}-{fid}.jar" });
                        continue;
                    }
                    string nombre = Str(f, "fileName") ?? $"{pid}-{fid}.jar";
                    string carpeta = CarpetaPorClase(clases.GetValueOrDefault(pid, ClaseMods));
                    if (carpeta == "saves") continue; // los mundos vienen comprimidos; no se instalan automáticamente

                    string ruta = $"{carpeta}/{nombre}";
                    if (ResolverRutaSegura(rutaCarpetaPerfil, ruta) == null)
                    {
                        LogService.Warn($"Modpack CurseForge rechazado: ruta fuera del perfil: {ruta}");
                        return null;
                    }

                    string? url = Str(f, "downloadUrl");
                    var item = new MrPackFile { RutaDestino = ruta };
                    if (!string.IsNullOrEmpty(url))
                    {
                        if (!EsUrlPermitida(url))
                        {
                            LogService.Warn($"Modpack CurseForge rechazado: URL no permitida: {url}");
                            return null;
                        }
                        item.UrlsDescarga.Add(url);
                    }
                    index.ArchivosMod.Add(item);
                }
            }
            finally
            {
                foreach (var d in docs) d.Dispose();
            }

            LimpiarModsAnteriores(rutaCarpetaPerfil);
            string carpetaOverrides = Str(raiz, "overrides") ?? "overrides";
            string? rutaOverrides = ResolverRutaSegura(rutaTemp, carpetaOverrides);
            if (rutaOverrides != null && Directory.Exists(rutaOverrides))
                CopiarDirectorio(rutaOverrides, rutaCarpetaPerfil);

            return index;
        }

        #endregion
    }
}
