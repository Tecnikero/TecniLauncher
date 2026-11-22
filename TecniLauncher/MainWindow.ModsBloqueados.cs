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

                    foreach (var a in pendientes.ToList())
                    {
                        if (await Task.Run(() => CopiarDesdeDescargas(a, rutaBase, descargas)))
                        {
                            pendientes.Remove(a);
                            fallos.Remove(System.IO.Path.GetFileName(a.RutaDestino));
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
