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
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
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
        private static readonly Regex RxVersion = new(@"^[A-Za-z0-9._\-+]{1,48}$", RegexOptions.Compiled);
        private static readonly string[] LoadersValidos = { "Vanilla", "Fabric", "Forge", "NeoForge" };
        private const int MaxMods = 500;
        private const int MaxOtros = 200;

        public static bool CodigoTieneFormatoValido(string? codigo) =>
            codigo != null && RxCodigo.IsMatch(codigo);
        public static async Task<ResultadoExportacion> CompartirPerfilAsync(Perfil perfil, OpcionesCompartir opciones, CancellationToken ct = default)
        {
            var (manifest, omitidos) = await ConstruirManifestAsync(perfil, opciones, ct);
            string codigo = await SubirManifestAsync(manifest, ct);

            return new ResultadoExportacion
            {
                Codigo = codigo,
                ModsIncluidos = manifest.Mods.Count,
                ShadersIncluidos = manifest.Shaders.Count,
                TexturasIncluidas = manifest.Texturas.Count,
                ArchivosOmitidos = omitidos
            };
        }

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

            var hashes = await Task.Run(() => archivos
                .Select(f => CalcularSha1(f.FullName))
                .Where(h => h != null)
                .Select(h => h!)
                .Distinct()
                .ToList(), ct);

            var encontrados = await ObtenerVersionesPorHashAsync(hashes, ct)
                ?? throw new ShareException(ErrorCompartir.Red, "No se pudo consultar Modrinth.");

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pid, vid) in encontrados.Values)
            {
                if (vistos.Add(vid))
                    destino.Add(new ManifestMod { ProjectId = pid, VersionId = vid });
            }
            return Math.Max(0, archivos.Count - destino.Count);
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
                .Where(x => x != null && RxIdModrinth.IsMatch(x.VersionId ?? ""))
                .GroupBy(x => x.VersionId, StringComparer.OrdinalIgnoreCase)
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
            foreach (var m in manifest.Texturas) tipoPorVersion[m.VersionId] = TipoComplemento.Textura;
            foreach (var m in manifest.Shaders) tipoPorVersion[m.VersionId] = TipoComplemento.Shader;
            foreach (var m in manifest.Mods) tipoPorVersion[m.VersionId] = TipoComplemento.Mod;

            int total = tipoPorVersion.Count;
            if (total == 0) return new List<string>();

            progreso?.Report((0, total, "Modrinth"));

            var versiones = await ObtenerVersionesPorIdsAsync(tipoPorVersion.Keys, ct);
            if (versiones == null) throw new ShareException(ErrorCompartir.Red, "No se pudo consultar Modrinth.");

            var porId = versiones.ToDictionary(v => v.VersionId, StringComparer.OrdinalIgnoreCase);
            foreach (var id in tipoPorVersion.Keys)
                if (!porId.ContainsKey(id)) fallos.Add(id);

            int hechos = total - porId.Count;
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
