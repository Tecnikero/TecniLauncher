using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using TecniLauncher.Services;

namespace TecniLauncher
{
    public class ModInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public string title { get; set; }
        public string description { get; set; }
        public string icon_url { get; set; }
        public string project_id { get; set; }
        public string author { get; set; }
        public string slug { get; set; }
        public long downloads { get; set; }
        public bool esRecomendado { get; set; } = false;

        private bool _estaInstalado;
        public bool estaInstalado
        {
            get => _estaInstalado;
            set { if (_estaInstalado == value) return; _estaInstalado = value; NotificarEstado(); }
        }

        private bool _instalando;
        public bool instalando
        {
            get => _instalando;
            set { if (_instalando == value) return; _instalando = value; NotificarEstado(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool PuedeInstalar => !_estaInstalado && !_instalando;

        [System.Text.Json.Serialization.JsonIgnore]
        public string BotonTexto =>
            _instalando ? Core.T("Mods_Instalando") :
            _estaInstalado ? Core.T("Mods_Instalado") :
            Core.T("Mods_Install");

        [System.Text.Json.Serialization.JsonIgnore]
        public string DescargasTexto =>
            downloads >= 1_000_000 ? $"{downloads / 1_000_000.0:0.#}M" :
            downloads >= 1_000 ? $"{downloads / 1_000.0:0.#}K" :
            downloads.ToString();

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void NotificarEstado()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(estaInstalado)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(instalando)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(PuedeInstalar)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BotonTexto)));
        }
    }

    public class FiltroItem
    {
        public string Clave { get; set; } = "";
        public string Texto { get; set; } = "";
        public override string ToString() => Texto;
    }

    public class ModLocal : System.ComponentModel.INotifyPropertyChanged
    {
        private const string SufijoDeshabilitado = ".disabled";

        public string Directorio { get; }
        public string NombreArchivo { get; private set; }
        public long Bytes { get; }
        public bool Habilitado { get; private set; }

        public ModLocal(System.IO.FileInfo f)
        {
            Directorio = f.DirectoryName ?? "";
            NombreArchivo = f.Name;
            Bytes = f.Length;
            Habilitado = !f.Name.EndsWith(SufijoDeshabilitado, StringComparison.OrdinalIgnoreCase);
        }

        public string RutaCompleta => System.IO.Path.Combine(Directorio, NombreArchivo);

        public string Titulo
        {
            get
            {
                string n = NombreArchivo;
                if (n.EndsWith(SufijoDeshabilitado, StringComparison.OrdinalIgnoreCase)) n = n[..^SufijoDeshabilitado.Length];
                if (n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
                return n;
            }
        }

        public string Detalle => Bytes >= 1024 * 1024 ? $"{Bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, Bytes / 1024)} KB";

        public bool Alternar(out string? error)
        {
            error = null;
            try
            {
                string destinoNombre = Habilitado ? NombreArchivo + SufijoDeshabilitado : NombreArchivo[..^SufijoDeshabilitado.Length];
                string destino = System.IO.Path.Combine(Directorio, destinoNombre);
                if (System.IO.File.Exists(destino))
                {
                    error = Core.T("Msg_ModDuplicado", destinoNombre);
                    Refrescar();
                    return false;
                }
                System.IO.File.Move(RutaCompleta, destino);
                NombreArchivo = destinoNombre;
                Habilitado = !Habilitado;
                Refrescar();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Refrescar();
                return false;
            }
        }

        public void Refrescar()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Habilitado)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NombreArchivo)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Titulo)));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class DependenciaMod
    {
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
    }

    public class ModVersion
    {
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string NombreVersion { get; set; }
        public string Tipo { get; set; }
        public string Fecha { get; set; }
        public string NombreArchivo { get; set; }
        public string UrlDescarga { get; set; }
        public string ColorTipo => Tipo == "release" ? "#2ecc71" : (Tipo == "beta" ? "#f1c40f" : "#e74c3c");
        public List<DependenciaMod> Requeridas { get; set; } = new List<DependenciaMod>();
        public List<string> DependenciasRequeridas => Requeridas
            .Where(d => !string.IsNullOrEmpty(d.ProjectId)).Select(d => d.ProjectId!).ToList();
    }

    public class ResultadoDependencias
    {
        public List<ModVersion> Descargas { get; set; } = new List<ModVersion>();
        public List<string> SinVersionCompatible { get; set; } = new List<string>();
    }

    public class ModrinthAPI
    {
        private static readonly HttpClient client = AppHttpClient.Instance;
        private const string Base = "https://api.modrinth.com/v2";

        private static string? Str(JsonElement e, string nombre) =>
            e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        public static async Task<List<ModInfo>> BuscarMods(string busqueda, string loader, string version, int offset = 0, int limite = 30, string? categoria = null)
        {
            if (string.IsNullOrEmpty(loader) || string.IsNullOrEmpty(version)) return new List<ModInfo>();

            try
            {
                string facetCategoria = string.IsNullOrEmpty(categoria) ? "" : $",[\"categories:{categoria.ToLower()}\"]";
                string facets = $"[[\"categories:{loader.ToLower()}\"],[\"versions:{version}\"],[\"project_type:mod\"]{facetCategoria}]";

                string indexOpt = string.IsNullOrEmpty(busqueda) ? "downloads" : "relevance";

                string url = $"{Base}/search?query={Uri.EscapeDataString(busqueda)}&facets={Uri.EscapeDataString(facets)}&index={indexOpt}&limit={limite}&offset={offset}";
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
        public static async Task<List<string>> ObtenerVersionesMinecraftAsync()
        {
            try
            {
                string json = await client.GetStringAsync($"{Base}/tag/game_version");
                using var doc = JsonDocument.Parse(json);
                var lista = new List<string>();
                foreach (var v in doc.RootElement.EnumerateArray())
                {
                    if (Str(v, "version_type") == "release" && Str(v, "version") is string nombre) lista.Add(nombre);
                }
                return lista;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error obteniendo versiones de Minecraft: " + ex.Message);
                return new List<string>();
            }
        }

        public static async Task<Dictionary<string, string>?> ObtenerProjectIdsPorHashAsync(List<string> hashesSha1)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (hashesSha1.Count == 0) return resultado;

            try
            {
                string cuerpo = JsonSerializer.Serialize(new { hashes = hashesSha1, algorithm = "sha1" });
                using var contenido = new StringContent(cuerpo, Encoding.UTF8, "application/json");
                using var respuesta = await client.PostAsync($"{Base}/version_files", contenido);
                if (!respuesta.IsSuccessStatusCode) return null;

                string json = await respuesta.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entrada in doc.RootElement.EnumerateObject())
                    {
                        string? pid = Str(entrada.Value, "project_id");
                        if (!string.IsNullOrEmpty(pid)) resultado[entrada.Name] = pid;
                    }
                }
                return resultado;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error identificando mods por hash: " + ex.Message);
                return null;
            }
        }
        private static ModVersion? ParsearVersion(JsonElement v)
        {
            try
            {
                if (!v.TryGetProperty("files", out var archivos) || archivos.ValueKind != JsonValueKind.Array || archivos.GetArrayLength() == 0)
                    return null;

                JsonElement archivo = archivos[0];
                foreach (var a in archivos.EnumerateArray())
                {
                    if (a.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True) { archivo = a; break; }
                }

                string? fechaRaw = Str(v, "date_published");
                string? fechaBonita = DateTime.TryParse(fechaRaw, out DateTime d) ? d.ToString("dd/MM/yyyy") : fechaRaw;

                var version = new ModVersion
                {
                    ProjectId = Str(v, "project_id") ?? "",
                    VersionId = Str(v, "id") ?? "",
                    NombreVersion = Str(v, "name") ?? "",
                    Tipo = Str(v, "version_type") ?? "release",
                    Fecha = fechaBonita ?? "",
                    NombreArchivo = Str(archivo, "filename") ?? "",
                    UrlDescarga = Str(archivo, "url") ?? ""
                };
                if (version.NombreArchivo == "" || version.UrlDescarga == "") return null;

                if (v.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in deps.EnumerateArray())
                    {
                        if (Str(dep, "dependency_type") != "required") continue;
                        string? pid = Str(dep, "project_id");
                        string? vid = Str(dep, "version_id");
                        if (pid == null && vid == null) continue;
                        version.Requeridas.Add(new DependenciaMod { ProjectId = pid, VersionId = vid });
                    }
                }
                return version;
            }
            catch { return null; }
        }

        public static async Task<List<ModVersion>> ObtenerListaVersiones(string projectId, string versionMC, string loader)
        {
            if (string.IsNullOrEmpty(projectId)) return new List<ModVersion>();

            try
            {
                string loaders = Uri.EscapeDataString($"[\"{loader.ToLower()}\"]");
                string versiones = Uri.EscapeDataString($"[\"{versionMC}\"]");
                string url = $"{Base}/project/{Uri.EscapeDataString(projectId)}/version?loaders={loaders}&game_versions={versiones}";
                string json = await client.GetStringAsync(url);
                var listaResultados = new List<ModVersion>();

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var versionJson in doc.RootElement.EnumerateArray())
                        {
                            var mv = ParsearVersion(versionJson);
                            if (mv != null) listaResultados.Add(mv);
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
        public static async Task<ModVersion?> ObtenerVersionPorIdAsync(string versionId)
        {
            try
            {
                string json = await client.GetStringAsync($"{Base}/version/{Uri.EscapeDataString(versionId)}");
                using var doc = JsonDocument.Parse(json);
                return ParsearVersion(doc.RootElement);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo versión {versionId}: {ex.Message}");
                return null;
            }
        }

        private static async Task<string> ObtenerTituloProyectoAsync(string projectId)
        {
            try
            {
                string json = await client.GetStringAsync($"{Base}/project/{Uri.EscapeDataString(projectId)}");
                using var doc = JsonDocument.Parse(json);
                return Str(doc.RootElement, "title") ?? projectId;
            }
            catch { return projectId; }
        }
        public static async Task<ResultadoDependencias> ResolverDependenciasAsync(
            ModVersion baseVersion, string versionMC, string loader, int maxMods = 50)
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
                    Debug.WriteLine($"Límite de {maxMods} mods alcanzado al resolver dependencias.");
                    break;
                }

                var actual = cola.Dequeue();
                resultado.Descargas.Add(actual);

                foreach (var dep in actual.Requeridas)
                {
                    ModVersion? elegida = null;
                    string? projectId = dep.ProjectId;

                    if (!string.IsNullOrEmpty(dep.VersionId))
                    {
                        elegida = await ObtenerVersionPorIdAsync(dep.VersionId);
                        if (elegida != null && !string.IsNullOrEmpty(elegida.ProjectId)) projectId = elegida.ProjectId;
                    }

                    if (string.IsNullOrEmpty(projectId)) continue;
                    if (!vistos.Add(projectId)) continue;

                    if (elegida == null)
                    {
                        var candidatas = await ObtenerListaVersiones(projectId, versionMC, loader);
                        elegida = candidatas.FirstOrDefault(v => v.Tipo == "release") ?? candidatas.FirstOrDefault();
                    }

                    if (elegida != null) cola.Enqueue(elegida);
                    else resultado.SinVersionCompatible.Add(await ObtenerTituloProyectoAsync(projectId));
                }
            }
            return resultado;
        }

    }
}