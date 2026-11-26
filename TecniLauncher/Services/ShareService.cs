﻿using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TecniLauncher.Services
{
    public class ManifestMod
    {
        public const string FuenteCurseForge = "curseforge";

        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";

        /// <summary>null = Modrinth (formato original). "curseforge": ProjectId = modId y VersionId = fileId.
        /// Los launchers anteriores descartan estas entradas sin fallar.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Fuente { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool EsCurseForge => string.Equals(Fuente, FuenteCurseForge, StringComparison.OrdinalIgnoreCase);
    }

    public class ManifestInstancia
    {
        public int Formato { get; set; } = 1;
        public string Nombre { get; set; } = "";
        public string VersionMinecraft { get; set; } = "";
        public string Loader { get; set; } = "Vanilla";
        public string VersionLoader { get; set; } = "";
        public List<ManifestMod> Mods { get; set; } = new();
        public List<ManifestMod> Shaders { get; set; } = new();
        public List<ManifestMod> Texturas { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore]
        public int TotalArchivos => Mods.Count + Shaders.Count + Texturas.Count;
    }

    public class OpcionesCompartir
    {
        public bool Mods { get; set; } = true;
        public bool Shaders { get; set; }
        public bool Texturas { get; set; }
    }

    public class ResultadoExportacion
    {
        public string Codigo { get; set; } = "";
        public int ModsIncluidos { get; set; }
        public int ShadersIncluidos { get; set; }
        public int TexturasIncluidas { get; set; }
        public int ArchivosOmitidos { get; set; }
    }

    public enum ErrorCompartir { Ninguno, Sesion, CodigoInvalido, CodigoNoExiste, Red, ManifestInvalido, NadaQueCompartir }

    public class ShareException : Exception
    {
        public ErrorCompartir Tipo { get; }
        public ShareException(ErrorCompartir tipo, string mensaje) : base(mensaje) { Tipo = tipo; }
    }

    public static class ShareService
    {
        private const string SUPABASE_URL = "https://kfxffvjakkcjbwkpvxtr.supabase.co";
        private static string AnonKey => SecretsManager.ObtenerSecreto("supabase_anon_key");

        private static readonly HttpClient _http = AppHttpClient.Instance;
        private static readonly Regex RxCodigo = new(@"^\d{6}$", RegexOptions.Compiled);
        private static readonly Regex RxIdModrinth = new(@"^[A-Za-z0-9]{8}$", RegexOptions.Compiled);
        private static readonly Regex RxIdCurseForge = new(@"^\d{1,10}$", RegexOptions.Compiled);
        private static readonly Regex RxVersion = new(@"^[A-Za-z0-9._\-+]{1,48}$", RegexOptions.Compiled);
        private static readonly string[] LoadersValidos = { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" };
        private const int MaxMods = 500;
        private const int MaxOtros = 200;

        public static bool CodigoTieneFormatoValido(string? codigo) =>
            codigo != null && RxCodigo.IsMatch(codigo);
        public static async Task<ResultadoExportacion> CompartirPerfilAsync(Perfil perfil, OpcionesCompartir opciones, CancellationToken ct = default)
        {
            var (manifest, omitidos) = await ConstruirManifestAsync(perfil, opciones, ct);
            string codigo;
            try
            {
                codigo = await SubirManifestAsync(manifest, ct);
            }
            catch (ShareException ex) when (ex.Tipo == ErrorCompartir.Red && ex.Message.StartsWith("Supabase respondió 4") && TieneCurseForge(manifest))
            {
                // Por si el servidor no acepta aún entradas de CurseForge: se comparte solo lo de Modrinth.
                LogService.Warn("El servidor rechazó el código con mods de CurseForge; se reintenta solo con Modrinth", ex);
                omitidos += QuitarCurseForge(manifest);
                if (manifest.TotalArchivos == 0) throw;
                codigo = await SubirManifestAsync(manifest, ct);
            }

            return new ResultadoExportacion
            {
                Codigo = codigo,
                ModsIncluidos = manifest.Mods.Count,
                ShadersIncluidos = manifest.Shaders.Count,
                TexturasIncluidas = manifest.Texturas.Count,
                ArchivosOmitidos = omitidos
            };
        }

        private static bool TieneCurseForge(ManifestInstancia m) =>
            m.Mods.Any(x => x.EsCurseForge) || m.Shaders.Any(x => x.EsCurseForge) || m.Texturas.Any(x => x.EsCurseForge);

        private static int QuitarCurseForge(ManifestInstancia m) =>
            m.Mods.RemoveAll(x => x.EsCurseForge) + m.Shaders.RemoveAll(x => x.EsCurseForge) + m.Texturas.RemoveAll(x => x.EsCurseForge);

        private static async Task<(ManifestInstancia, int omitidos)> ConstruirManifestAsync(
            Perfil perfil, OpcionesCompartir opciones, CancellationToken ct)
        {
            var manifest = new ManifestInstancia
            {
                Nombre = perfil.Nombre,
                VersionMinecraft = perfil.Version,
                Loader = perfil.TipoLoader,
                VersionLoader = perfil.VersionLoaderExacta ?? ""
            };

            int omitidos = 0;
            if (opciones.Mods) omitidos += await LlenarListaAsync(perfil, TipoComplemento.Mod, manifest.Mods, ct);
            if (opciones.Shaders) omitidos += await LlenarListaAsync(perfil, TipoComplemento.Shader, manifest.Shaders, ct);
            if (opciones.Texturas) omitidos += await LlenarListaAsync(perfil, TipoComplemento.Textura, manifest.Texturas, ct);

            if (manifest.TotalArchivos == 0)
                throw new ShareException(ErrorCompartir.NadaQueCompartir, "No hay nada que compartir.");

            return (manifest, omitidos);
        }
        private static async Task<int> LlenarListaAsync(Perfil perfil, TipoComplemento tipo, List<ManifestMod> destino, CancellationToken ct)
        {
            string carpeta = Path.Combine(perfil.RutaCarpeta, tipo.Carpeta());
            if (!Directory.Exists(carpeta)) return 0;

            string ext = tipo.Extensiones()[0];
            var archivos = new DirectoryInfo(carpeta).EnumerateFiles("*" + ext).ToList();
            if (archivos.Count == 0) return 0;

            var porArchivo = await Task.Run(() => archivos
                .Select(f => (Archivo: f, Sha1: CalcularSha1(f.FullName)))
                .ToList(), ct);
            var hashes = porArchivo.Where(x => x.Sha1 != null).Select(x => x.Sha1!).Distinct().ToList();

            var encontrados = await ObtenerVersionesPorHashAsync(hashes, ct)
                ?? throw new ShareException(ErrorCompartir.Red, "No se pudo consultar Modrinth.");

            int incluidos = 0;
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sinModrinth = new List<FileInfo>();
            foreach (var (archivo, sha1) in porArchivo)
            {
                if (sha1 != null && encontrados.TryGetValue(sha1, out var v))
                {
                    incluidos++;
                    if (vistos.Add(v.VersionId)) destino.Add(new ManifestMod { ProjectId = v.ProjectId, VersionId = v.VersionId });
                }
                else sinModrinth.Add(archivo);
            }

            // Lo que no está en Modrinth se busca en CurseForge por su huella.
            if (sinModrinth.Count > 0 && CurseForgeAPI.Disponible)
            {
                var huellas = await Task.Run(() =>
                {
                    var l = new List<(FileInfo Archivo, uint Huella)>();
                    foreach (var f in sinModrinth)
                    {
                        try { l.Add((f, CurseForgeAPI.CalcularFingerprint(f.FullName))); } catch { }
                    }
                    return l;
                }, ct);

                var cf = await CurseForgeAPI.ObtenerArchivosPorFingerprintAsync(huellas.Select(h => h.Huella).Distinct().ToList());
                if (cf != null)
                {
                    foreach (var (_, huella) in huellas)
                    {
                        if (!cf.TryGetValue(huella, out var a) || a.ModId <= 0 || a.FileId <= 0) continue;
                        incluidos++;
                        if (vistos.Add("cf:" + a.FileId))
                            destino.Add(new ManifestMod { ProjectId = a.ModId.ToString(), VersionId = a.FileId.ToString(), Fuente = ManifestMod.FuenteCurseForge });
                    }
                }
            }
            return Math.Max(0, archivos.Count - incluidos);
        }

        private static async Task<string> SubirManifestAsync(ManifestInstancia manifest, CancellationToken ct)
        {
            string key = AnonKey;
            if (string.IsNullOrEmpty(key))
                throw new ShareException(ErrorCompartir.Sesion, "Falta la clave de Supabase.");

            string cuerpo = JsonSerializer.Serialize(new { p_manifest = manifest });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/rpc/crear_perfil_compartido")
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", key);
            req.Headers.Add("Authorization", $"Bearer {key}");

            try
            {
                using var resp = await _http.SendAsync(req, ct);
                string texto = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new ShareException(ErrorCompartir.Red, $"Supabase respondió {(int)resp.StatusCode}: {texto}");

                string? codigo = JsonSerializer.Deserialize<string>(texto);
                if (!CodigoTieneFormatoValido(codigo))
                    throw new ShareException(ErrorCompartir.Red, "Código inválido devuelto por el servidor.");
                return codigo!;
            }
            catch (HttpRequestException ex) { throw new ShareException(ErrorCompartir.Red, ex.Message); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ShareException(ErrorCompartir.Red, "Tiempo de espera agotado."); }
        }
        public static async Task<ManifestInstancia> ObtenerManifestAsync(string codigo, CancellationToken ct = default)
        {
            codigo = (codigo ?? "").Trim();
            if (!CodigoTieneFormatoValido(codigo))
                throw new ShareException(ErrorCompartir.CodigoInvalido, "El código debe tener 6 dígitos.");

            string key = AnonKey;
            if (string.IsNullOrEmpty(key))
                throw new ShareException(ErrorCompartir.Sesion, "Falta la clave de Supabase.");

            string cuerpo = JsonSerializer.Serialize(new { p_codigo = codigo });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/rpc/obtener_perfil_compartido")
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", key);
            req.Headers.Add("Authorization", $"Bearer {key}");

            string json;
            try
            {
                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                    throw new ShareException(ErrorCompartir.Red, $"Supabase respondió {(int)resp.StatusCode}.");
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException ex) { throw new ShareException(ErrorCompartir.Red, ex.Message); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ShareException(ErrorCompartir.Red, "Tiempo de espera agotado."); }

            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                throw new ShareException(ErrorCompartir.CodigoNoExiste, "El código no existe o ya expiró.");

            ManifestInstancia? m;
            try
            {
                m = JsonSerializer.Deserialize<ManifestInstancia>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { throw new ShareException(ErrorCompartir.ManifestInvalido, "El manifest está dañado."); }

            return Validar(m);
        }

        private static ManifestInstancia Validar(ManifestInstancia? m)
        {
            if (m == null || m.Formato != 1)
                throw new ShareException(ErrorCompartir.ManifestInvalido, "Formato de instancia no soportado.");

            if (!RxVersion.IsMatch(m.VersionMinecraft ?? ""))
                throw new ShareException(ErrorCompartir.ManifestInvalido, "Versión de Minecraft inválida.");

            string? loader = LoadersValidos.FirstOrDefault(l => string.Equals(l, m.Loader, StringComparison.OrdinalIgnoreCase));
            if (loader == null)
                throw new ShareException(ErrorCompartir.ManifestInvalido, "Loader no soportado.");
            m.Loader = loader;

            m.VersionLoader ??= "";
            if (m.VersionLoader.Length > 0 && !RxVersion.IsMatch(m.VersionLoader))
                throw new ShareException(ErrorCompartir.ManifestInvalido, "Versión de loader inválida.");

            m.Mods = LimpiarLista(m.Mods, MaxMods);
            m.Shaders = LimpiarLista(m.Shaders, MaxOtros);
            m.Texturas = LimpiarLista(m.Texturas, MaxOtros);

            string nombre = (m.Nombre ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) nombre = nombre.Replace(c, '_');
            nombre = nombre.Trim('.', ' ');
            if (nombre.Length == 0) nombre = "Instancia importada";
            if (nombre.Length > 48) nombre = nombre[..48];
            m.Nombre = nombre;

            return m;
        }

        private static List<ManifestMod> LimpiarLista(List<ManifestMod>? lista, int max)
        {
            lista ??= new();
            if (lista.Count > max)
                throw new ShareException(ErrorCompartir.ManifestInvalido, "Demasiados archivos en el manifest.");

            return lista
                .Where(x => x != null && (x.EsCurseForge
                    ? RxIdCurseForge.IsMatch(x.VersionId ?? "") && RxIdCurseForge.IsMatch(x.ProjectId ?? "")
                    : x.Fuente == null && RxIdModrinth.IsMatch(x.VersionId ?? "")))
                .GroupBy(x => (x.EsCurseForge ? "cf:" : "") + x.VersionId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
        public static async Task<List<string>> DescargarModsAsync(
            ManifestInstancia manifest, string carpetaPerfil,
            IProgress<(int actual, int total, string nombre)>? progreso = null,
            CancellationToken ct = default)
        {
            var fallos = new ConcurrentBag<string>();

            var tipoPorVersion = new Dictionary<string, TipoComplemento>(StringComparer.OrdinalIgnoreCase);
            var tipoPorArchivoCf = new Dictionary<long, TipoComplemento>();
            void Agregar(List<ManifestMod> lista, TipoComplemento t)
            {
                foreach (var m in lista)
                {
                    if (m.EsCurseForge) { if (long.TryParse(m.VersionId, out long id)) tipoPorArchivoCf[id] = t; }
                    else tipoPorVersion[m.VersionId] = t;
                }
            }
            Agregar(manifest.Texturas, TipoComplemento.Textura);
            Agregar(manifest.Shaders, TipoComplemento.Shader);
            Agregar(manifest.Mods, TipoComplemento.Mod);

            int totalCf = tipoPorArchivoCf.Count;
            int total = tipoPorVersion.Count + totalCf;
            if (total == 0) return new List<string>();

            int hechosCf = 0;
            if (totalCf > 0)
            {
                foreach (var f in await DescargarDeCurseForgeAsync(tipoPorArchivoCf, carpetaPerfil, () =>
                         {
                             int n = Interlocked.Increment(ref hechosCf);
                             progreso?.Report((Math.Min(n, total), total, "CurseForge"));
                         }, ct))
                    fallos.Add(f);
                if (tipoPorVersion.Count == 0) return fallos.ToList();
            }

            progreso?.Report((0, total, "Modrinth"));

            var versiones = await ObtenerVersionesPorIdsAsync(tipoPorVersion.Keys, ct);
            if (versiones == null) throw new ShareException(ErrorCompartir.Red, "No se pudo consultar Modrinth.");

            var porId = versiones.ToDictionary(v => v.VersionId, StringComparer.OrdinalIgnoreCase);
            foreach (var id in tipoPorVersion.Keys)
                if (!porId.ContainsKey(id)) fallos.Add(id);

            int hechos = totalCf + (tipoPorVersion.Count - porId.Count);
            progreso?.Report((hechos, total, ""));

            await Parallel.ForEachAsync(porId.Values,
                new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
                async (v, token) =>
                {
                    try
                    {
                        var tipo = tipoPorVersion[v.VersionId];
                        string carpeta = Path.Combine(carpetaPerfil, tipo.Carpeta());

                        if (!ModpacksApi.EsUrlPermitida(v.UrlDescarga))
                            throw new InvalidOperationException("Host no permitido");

                        if (!tipo.Extensiones().Any(e => v.NombreArchivo.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Extensión inesperada para " + tipo);

                        string? destino = ModpacksApi.ResolverRutaSegura(carpeta, v.NombreArchivo);
                        if (destino == null) throw new InvalidOperationException("Ruta insegura");

                        Directory.CreateDirectory(carpeta);
                        if (!File.Exists(destino))
                            await AppHttpClient.DescargarArchivoAsync(v.UrlDescarga, destino, ct: token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        LogService.Warn($"Fallo al importar {v.NombreArchivo}", ex);
                        fallos.Add(string.IsNullOrEmpty(v.NombreArchivo) ? v.VersionId : v.NombreArchivo);
                    }
                    finally
                    {
                        int n = Interlocked.Increment(ref hechos);
                        progreso?.Report((Math.Min(n, total), total, v.NombreArchivo));
                    }
                });

            return fallos.ToList();
        }
        private static string? CalcularSha1(string ruta)
        {
            try
            {
                using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
            }
            catch { return null; }
        }
        private static async Task<Dictionary<string, (string ProjectId, string VersionId)>?> ObtenerVersionesPorHashAsync(
            List<string> hashes, CancellationToken ct)
        {
            var res = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            if (hashes.Count == 0) return res;

            try
            {
                string cuerpo = JsonSerializer.Serialize(new { hashes, algorithm = "sha1" });
                using var contenido = new StringContent(cuerpo, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync("https://api.modrinth.com/v2/version_files", contenido, ct);
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return res;

                foreach (var e in doc.RootElement.EnumerateObject())
                {
                    string? pid = e.Value.TryGetProperty("project_id", out var p) ? p.GetString() : null;
                    string? vid = e.Value.TryGetProperty("id", out var v) ? v.GetString() : null;
                    if (!string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(vid)) res[e.Name] = (pid, vid);
                }
                return res;
            }
            catch (Exception ex)
            {
                LogService.Warn("Error identificando mods por hash", ex);
                return null;
            }
        }
        /// <summary>
        /// Descarga los archivos de CurseForge de un código. Los que su autor no deja descargar desde
        /// launchers se buscan en Modrinth por hash; si tampoco están, se devuelven como fallos.
        /// </summary>
        private static async Task<List<string>> DescargarDeCurseForgeAsync(
            Dictionary<long, TipoComplemento> tipos, string carpetaPerfil, Action avance, CancellationToken ct)
        {
            var fallos = new ConcurrentBag<string>();
            var archivos = await CurseForgeAPI.ObtenerArchivosAsync(tipos.Keys);
            if (archivos == null)
            {
                foreach (var id in tipos.Keys) { fallos.Add("CurseForge #" + id); avance(); }
                return fallos.ToList();
            }
            archivos = archivos.Where(a => tipos.ContainsKey(a.FileId)).GroupBy(a => a.FileId).Select(g => g.First()).ToList();
            foreach (var id in tipos.Keys.Except(archivos.Select(a => a.FileId))) { fallos.Add("CurseForge #" + id); avance(); }

            var sinUrl = archivos.Where(a => string.IsNullOrEmpty(a.Url) && a.Sha1 != null).ToList();
            var enModrinth = sinUrl.Count > 0 ? await ModrinthAPI.ObtenerUrlsPorHashAsync(sinUrl.Select(a => a.Sha1!)) : new Dictionary<string, string>();

            await Parallel.ForEachAsync(archivos,
                new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
                async (a, token) =>
                {
                    try
                    {
                        var tipo = tipos[a.FileId];
                        string? url = !string.IsNullOrEmpty(a.Url) ? a.Url
                            : (a.Sha1 != null && enModrinth.TryGetValue(a.Sha1, out var u) ? u : null);
                        if (url == null) throw new InvalidOperationException("El autor no permite descargarlo desde launchers");
                        if (!ModpacksApi.EsUrlPermitida(url)) throw new InvalidOperationException("Host no permitido");
                        if (!tipo.Extensiones().Any(e => a.NombreArchivo.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Extensión inesperada para " + tipo);

                        string carpeta = Path.Combine(carpetaPerfil, tipo.Carpeta());
                        string? destino = ModpacksApi.ResolverRutaSegura(carpeta, a.NombreArchivo);
                        if (destino == null) throw new InvalidOperationException("Ruta insegura");

                        Directory.CreateDirectory(carpeta);
                        if (!File.Exists(destino))
                            await AppHttpClient.DescargarArchivoAsync(url, destino, ct: token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        LogService.Warn($"Fallo al importar {a.NombreArchivo} (CurseForge)", ex);
                        fallos.Add(string.IsNullOrEmpty(a.NombreArchivo) ? "CurseForge #" + a.FileId : a.NombreArchivo);
                    }
                    finally { avance(); }
                });
            return fallos.ToList();
        }

        private static async Task<List<ModVersion>?> ObtenerVersionesPorIdsAsync(IEnumerable<string> ids, CancellationToken ct)
        {
            var resultado = new List<ModVersion>();
            try
            {
                foreach (var lote in ids.Chunk(100))
                {
                    string lista = "[" + string.Join(",", lote.Select(i => $"\"{i}\"")) + "]";
                    string url = "https://api.modrinth.com/v2/versions?ids=" + Uri.EscapeDataString(lista);
                    string json = await _http.GetStringAsync(url, ct);

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var mv = ModrinthAPI.ParsearVersion(el);
                        if (mv != null) resultado.Add(mv);
                    }
                }
                return resultado;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogService.Warn("Error obteniendo versiones", ex);
                return null;
            }
        }
    }
}
