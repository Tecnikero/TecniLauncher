using System.Globalization;
using System.IO;

namespace TecniLauncher.Services
{
    public enum CategoriaCache { Instaladores, Logs, Temporales }

    public class EntradaCache
    {
        public string Ruta { get; init; } = "";
        public bool EsCarpeta { get; init; }
        public long Bytes { get; init; }
        public CategoriaCache Categoria { get; init; }
    }

    public class PlanLimpiezaCache
    {
        public List<EntradaCache> Entradas { get; } = new();
        public long Total => Entradas.Sum(e => e.Bytes);
        public bool EstaVacio => Entradas.Count == 0;
        public long BytesDe(CategoriaCache c) => Entradas.Where(e => e.Categoria == c).Sum(e => e.Bytes);
    }
    public static class CacheService
    {
        private static readonly TimeSpan EdadMinimaParciales = TimeSpan.FromMinutes(10);

        public static PlanLimpiezaCache Analizar()
        {
            var plan = new PlanLimpiezaCache();
            var instancias = ObtenerInstancias();
            string carpetaApp = AppContext.BaseDirectory;

            AgregarInstaladores(plan, Core.RutaGlobal);
            string versiones = Path.Combine(Core.RutaGlobal, "versions");
            if (Directory.Exists(versiones))
                foreach (string v in Directory.EnumerateDirectories(versiones))
                    AgregarInstaladores(plan, v);

            AgregarArchivo(plan, Path.Combine(carpetaApp, "Update.zip"), CategoriaCache.Instaladores);
            AgregarCarpeta(plan, Path.Combine(carpetaApp, "Update_Temp"), CategoriaCache.Instaladores);

            foreach (string inst in instancias)
                AgregarArchivo(plan, Path.Combine(inst, "modpack_temp.zip"), CategoriaCache.Instaladores);

            AgregarCarpeta(plan, Path.Combine(Path.GetTempPath(), "TecniLauncher"), CategoriaCache.Temporales);

            foreach (string inst in instancias)
            {
                AgregarCarpeta(plan, Path.Combine(inst, "mods.new"), CategoriaCache.Temporales);
                AgregarCarpeta(plan, Path.Combine(inst, "mods.old"), CategoriaCache.Temporales);

                foreach (string sub in new[] { "", "mods", "resourcepacks", "shaderpacks" })
                    AgregarParciales(plan, Path.Combine(inst, sub));
            }
            AgregarParciales(plan, Core.RutaGlobal);
            AgregarParciales(plan, carpetaApp);

            foreach (string inst in instancias)
            {
                string logs = Path.Combine(inst, "logs");
                if (!Directory.Exists(logs) || EsEnlace(logs)) continue;

                foreach (string archivo in Enumerar(logs, SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(archivo), "latest.log", StringComparison.OrdinalIgnoreCase)) continue;
                    AgregarArchivo(plan, archivo, CategoriaCache.Logs);
                }
            }

            if (Core.RutaLogs != null && Directory.Exists(Core.RutaLogs))
            {
                foreach (string archivo in Enumerar(Core.RutaLogs, SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(archivo, ConsolaJuegoService.RutaArchivoActual, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Path.GetFileName(archivo).StartsWith(LogService.PREFIJO, StringComparison.OrdinalIgnoreCase)) continue;
                    AgregarArchivo(plan, archivo, CategoriaCache.Logs);
                }
            }

            return plan;
        }
        public static long Limpiar(PlanLimpiezaCache plan)
        {
            long liberados = 0;

            foreach (var e in plan.Entradas)
            {
                try
                {
                    if (e.EsCarpeta)
                    {
                        if (!Directory.Exists(e.Ruta)) continue;
                        try
                        {
                            Directory.Delete(e.Ruta, true);
                            liberados += e.Bytes;
                        }
                        catch (Exception exLog)
                        {
                            LogService.Debug("Carpeta no eliminada por completo (en uso)", exLog);
                            liberados += Math.Max(0, e.Bytes - TamanoCarpeta(e.Ruta));
                        }
                    }
                    else if (File.Exists(e.Ruta))
                    {
                        File.Delete(e.Ruta);
                        liberados += e.Bytes;
                    }
                }
                catch (Exception exLog)
                {
                    LogService.Warn("No se pudo eliminar un elemento de caché", exLog);
                }
            }

            return liberados;
        }

        public static string FormatearBytes(long bytes)
        {
            string[] unidades = { "B", "KB", "MB", "GB", "TB" };
            double valor = bytes;
            int i = 0;
            while (valor >= 1024 && i < unidades.Length - 1)
            {
                valor /= 1024;
                i++;
            }
            return i == 0
                ? $"{bytes} B"
                : $"{valor.ToString("0.#", CultureInfo.CurrentCulture)} {unidades[i]}";
        }

        private static List<string> ObtenerInstancias()
        {
            var rutas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Core.RutaInstances != null && Directory.Exists(Core.RutaInstances))
                    foreach (string d in Directory.EnumerateDirectories(Core.RutaInstances))
                        rutas.Add(d);
            }
            catch (Exception exLog) { LogService.Warn("No se pudieron enumerar las instancias", exLog); }
            Perfil[] perfiles;
            try { perfiles = Core.Perfiles.ToArray(); }
            catch { perfiles = Array.Empty<Perfil>(); }

            foreach (var p in perfiles)
            {
                if (!string.IsNullOrWhiteSpace(p.RutaCarpeta) && Directory.Exists(p.RutaCarpeta))
                    rutas.Add(p.RutaCarpeta);
            }

            return rutas.ToList();
        }

        private static void AgregarInstaladores(PlanLimpiezaCache plan, string carpeta)
        {
            foreach (string archivo in Enumerar(carpeta, SearchOption.TopDirectoryOnly))
            {
                string nombre = Path.GetFileName(archivo);
                string ext = Path.GetExtension(nombre);
                bool esInstalador = nombre.Contains("installer", StringComparison.OrdinalIgnoreCase)
                    && (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase) || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase));
                if (esInstalador)
                    AgregarArchivo(plan, archivo, CategoriaCache.Instaladores);
            }
        }

        private static void AgregarParciales(PlanLimpiezaCache plan, string carpeta)
        {
            foreach (string archivo in Enumerar(carpeta, SearchOption.TopDirectoryOnly, "*.part"))
            {
                try
                {
                    if (DateTime.Now - File.GetLastWriteTime(archivo) < EdadMinimaParciales) continue;
                }
                catch { continue; }
                AgregarArchivo(plan, archivo, CategoriaCache.Temporales);
            }
        }

        private static void AgregarArchivo(PlanLimpiezaCache plan, string ruta, CategoriaCache categoria)
        {
            try
            {
                var info = new FileInfo(ruta);
                if (!info.Exists) return;
                plan.Entradas.Add(new EntradaCache { Ruta = ruta, Bytes = info.Length, Categoria = categoria });
            }
            catch (Exception exLog) { LogService.Debug("No se pudo leer información de archivo", exLog); }
        }

        private static void AgregarCarpeta(PlanLimpiezaCache plan, string ruta, CategoriaCache categoria)
        {
            try
            {
                if (!Directory.Exists(ruta) || EsEnlace(ruta)) return;
                plan.Entradas.Add(new EntradaCache { Ruta = ruta, EsCarpeta = true, Bytes = TamanoCarpeta(ruta), Categoria = categoria });
            }
            catch (Exception exLog) { LogService.Warn("No se pudo agregar carpeta al plan de limpieza", exLog); }
        }

        private static IEnumerable<string> Enumerar(string carpeta, SearchOption opcion, string patron = "*")
        {
            try
            {
                if (!Directory.Exists(carpeta)) return Enumerable.Empty<string>();
                return Directory.EnumerateFiles(carpeta, patron, opcion).ToList();
            }
            catch (Exception exLog)
            {
                LogService.Warn("No se pudo enumerar archivos", exLog);
                return Enumerable.Empty<string>();
            }
        }

        private static long TamanoCarpeta(string ruta)
        {
            long total = 0;
            foreach (string archivo in Enumerar(ruta, SearchOption.AllDirectories))
            {
                try { total += new FileInfo(archivo).Length; } catch (Exception exLog) { LogService.Debug("No se pudo leer el tamaño de un archivo", exLog); }
            }
            return total;
        }
        private static bool EsEnlace(string ruta)
        {
            try { return new DirectoryInfo(ruta).Attributes.HasFlag(FileAttributes.ReparsePoint); }
            catch { return true; }
        }
    }
}
