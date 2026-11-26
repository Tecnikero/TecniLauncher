using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using TecniLauncher.Services;

namespace TecniLauncher
{
    /// <summary>
    /// Mods de CurseForge cuyo autor desactivó la descarga desde launchers: la API no da URL,
    /// así que se abre su página oficial para que el usuario los descargue y se copian desde Descargas.
    /// </summary>
    public partial class MainWindow
    {
        private async Task<List<string>> CompletarDescargasManualesAsync(ModpacksApi.MrPackIndex receta, string rutaBase, List<string> fallos)
        {
            try
            {
                var pendientes = (receta.ArchivosMod ?? new())
                    .Where(a => !string.IsNullOrEmpty(a.PaginaDescargaManual) && fallos.Contains(System.IO.Path.GetFileName(a.RutaDestino)))
                    .ToList();
                if (pendientes.Count == 0) return fallos;

                string titulo = Core.T("Txt_TituloInstalacionIncompleta");
                if (VentanaMensaje.Mostrar(Core.T("Msg_ModsBloqueados", pendientes.Count, ListaNombres(pendientes)), titulo, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return fallos;

                foreach (var a in pendientes)
                {
                    try { Process.Start(new ProcessStartInfo { FileName = a.PaginaDescargaManual!, UseShellExecute = true }); }
                    catch (Exception ex) { LogService.Warn($"No se pudo abrir {a.PaginaDescargaManual}", ex); }
                    await Task.Delay(400); // que el navegador no descarte pestañas abiertas de golpe
                }

                string descargas = CarpetaDescargas();
                while (pendientes.Count > 0)
                {
                    if (VentanaMensaje.Mostrar(Core.T("Msg_ModsBloqueadosEsperando", pendientes.Count, descargas, ListaNombres(pendientes)), titulo, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        break;

                    foreach (string carpeta in CarpetasDondeBuscar())
                    {
                        if (pendientes.Count == 0) break;
                        await BuscarEnCarpetaAsync(pendientes, fallos, rutaBase, carpeta);
                    }

                    // No aparecieron: que la persona elija los archivos (la carpeta puede estar en otro sitio).
                    if (pendientes.Count > 0
                        && VentanaMensaje.Mostrar(Core.T("Msg_ModsElegirArchivos", pendientes.Count, ListaNombres(pendientes)), titulo, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Multiselect = true,
                            Filter = "Mods (*.jar;*.zip)|*.jar;*.zip|*.*|*.*",
                            InitialDirectory = Directory.Exists(descargas) ? descargas : ""
                        };
                        if (dlg.ShowDialog(this) == true && dlg.FileNames.Length > 0)
                        {
                            string temporal = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TecniLauncher", "ElegidosManual");
                            try
                            {
                                if (Directory.Exists(temporal)) Directory.Delete(temporal, true);
                                Directory.CreateDirectory(temporal);
                                foreach (string f in dlg.FileNames)
                                    File.Copy(f, System.IO.Path.Combine(temporal, System.IO.Path.GetFileName(f)), overwrite: true);
                                await BuscarEnCarpetaAsync(pendientes, fallos, rutaBase, temporal);
                            }
                            finally { try { Directory.Delete(temporal, true); } catch { } }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("Error completando descargas manuales", ex);
            }
            return fallos;
        }

        /// <summary>Copia los que encuentre en <paramref name="carpeta"/>: por nombre o hash, y si no, otra versión del mismo mod.</summary>
        private static async Task BuscarEnCarpetaAsync(List<ModpacksApi.MrPackFile> pendientes, List<string> fallos, string rutaBase, string carpeta)
        {
            if (!Directory.Exists(carpeta)) return;
            foreach (var a in pendientes.ToList())
            {
                if (await Task.Run(() => CopiarDesdeDescargas(a, rutaBase, carpeta)))
                {
                    pendientes.Remove(a);
                    fallos.Remove(System.IO.Path.GetFileName(a.RutaDestino));
                }
            }
            if (pendientes.Count == 0) return;
            foreach (var (a, archivo) in await Task.Run(() => BuscarMismoModEnDescargasAsync(pendientes, carpeta)))
            {
                if (CopiarArchivoAlPerfil(a, archivo, rutaBase))
                {
                    pendientes.Remove(a);
                    fallos.Remove(System.IO.Path.GetFileName(a.RutaDestino));
                }
            }
            LogService.Info($"Descargas manuales: buscado en una carpeta, quedan {pendientes.Count}");
        }

        /// <summary>Descargas de Windows y las ubicaciones habituales donde el navegador puede guardar.</summary>
        private static IEnumerable<string> CarpetasDondeBuscar()
        {
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var lista = new List<string> { CarpetaDescargas() };
            foreach (string n in new[] { "Downloads", "Descargas", "Transferências" }) lista.Add(System.IO.Path.Combine(perfil, n));
            string? onedrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(onedrive))
                foreach (string n in new[] { "Downloads", "Descargas", "Desktop", "Escritorio" }) lista.Add(System.IO.Path.Combine(onedrive, n));
            lista.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            return lista.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                        .Select(d => System.IO.Path.GetFullPath(d).TrimEnd('\\'))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ListaNombres(List<ModpacksApi.MrPackFile> archivos) =>
            string.Join("\n", archivos.Take(10).Select(a => "• " + System.IO.Path.GetFileName(a.RutaDestino)))
            + (archivos.Count > 10 ? $"\n... +{archivos.Count - 10}" : "");

        /// <summary>Busca el archivo en Descargas (por nombre o por SHA-1) y lo copia al perfil.</summary>
        private static bool CopiarDesdeDescargas(ModpacksApi.MrPackFile archivo, string rutaBase, string descargas)
        {
            try
            {
                string? destino = ModpacksApi.ResolverRutaSegura(rutaBase, archivo.RutaDestino);
                if (destino == null || !Directory.Exists(descargas)) return false;
                archivo.Hashes.TryGetValue("sha1", out var sha1Esperado);

                string nombre = System.IO.Path.GetFileName(archivo.RutaDestino);
                string exacto = System.IO.Path.Combine(descargas, nombre);
                string? origen = File.Exists(exacto) && CoincideSha1(exacto, sha1Esperado) ? exacto : null;

                // El navegador puede renombrar ("mod (1).jar") o cambiar caracteres raros: buscar por hash.
                if (origen == null && !string.IsNullOrEmpty(sha1Esperado))
                {
                    string ext = System.IO.Path.GetExtension(nombre);
                    var limite = DateTime.Now.AddDays(-2);
                    origen = new DirectoryInfo(descargas).EnumerateFiles("*" + ext)
                        .Where(f => f.LastWriteTime >= limite && f.Length < 1024L * 1024 * 1024)
                        .OrderByDescending(f => f.LastWriteTime)
                        .Select(f => f.FullName)
                        .FirstOrDefault(f => CoincideSha1(f, sha1Esperado));
                }
                if (origen == null) return false;

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destino)!);
                File.Copy(origen, destino, overwrite: true);
                LogService.Info($"Copiado desde Descargas: {archivo.RutaDestino}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warn($"No se pudo copiar {archivo.RutaDestino} desde Descargas", ex);
                return false;
            }
        }

        /// <summary>Archivos recientes de Descargas que CurseForge identifica como el mismo mod que falta.</summary>
        private static async Task<List<(ModpacksApi.MrPackFile Falta, string Archivo)>> BuscarMismoModEnDescargasAsync(List<ModpacksApi.MrPackFile> pendientes, string descargas)
        {
            var resultado = new List<(ModpacksApi.MrPackFile, string)>();
            try
            {
                var buscados = pendientes.Where(p => p.CfModId > 0).ToList();
                if (buscados.Count == 0 || !Directory.Exists(descargas) || !CurseForgeAPI.Disponible) return resultado;

                var limite = DateTime.Now.AddDays(-2);
                var candidatos = new DirectoryInfo(descargas).EnumerateFiles("*.*")
                    .Where(f => f.LastWriteTime >= limite && f.Length < 1024L * 1024 * 1024
                                && (f.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => f.LastWriteTime).Take(200).ToList();
                if (candidatos.Count == 0) return resultado;

                var huellas = new Dictionary<uint, string>();
                foreach (var f in candidatos)
                {
                    try { huellas.TryAdd(CurseForgeAPI.CalcularFingerprint(f.FullName), f.FullName); } catch { }
                }
                var ids = await CurseForgeAPI.ObtenerModIdsPorFingerprintAsync(huellas.Keys.ToList());
                if (ids == null) return resultado;

                foreach (var falta in buscados)
                {
                    var coincidencia = ids.FirstOrDefault(kv => kv.Value == falta.CfModId.ToString());
                    if (coincidencia.Value != null && huellas.TryGetValue(coincidencia.Key, out var ruta))
                        resultado.Add((falta, ruta));
                }
            }
            catch (Exception ex) { LogService.Warn("No se pudo identificar los archivos de Descargas en CurseForge", ex); }
            return resultado;
        }

        /// <summary>Copia un archivo de Descargas a la carpeta del mod que falta, con su propio nombre.</summary>
        private static bool CopiarArchivoAlPerfil(ModpacksApi.MrPackFile falta, string origen, string rutaBase)
        {
            try
            {
                string carpeta = System.IO.Path.GetDirectoryName(falta.RutaDestino.Replace('\\', '/')) ?? "mods";
                string? destino = ModpacksApi.ResolverRutaSegura(rutaBase, System.IO.Path.Combine(carpeta, System.IO.Path.GetFileName(origen)));
                if (destino == null) return false;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destino)!);
                File.Copy(origen, destino, overwrite: true);
                LogService.Info($"Copiado desde Descargas (otra versión del mismo mod): {System.IO.Path.GetFileName(origen)} en lugar de {falta.RutaDestino}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warn($"No se pudo copiar {origen} desde Descargas", ex);
                return false;
            }
        }

        private static bool CoincideSha1(string ruta, string? sha1Esperado)
        {
            if (string.IsNullOrEmpty(sha1Esperado)) return true;
            using var fs = File.OpenRead(ruta);
            return string.Equals(Convert.ToHexString(SHA1.HashData(fs)), sha1Esperado, StringComparison.OrdinalIgnoreCase);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out string pszPath);

        private static string CarpetaDescargas()
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var ruta) == 0 && !string.IsNullOrEmpty(ruta))
                    return ruta;
            }
            catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
