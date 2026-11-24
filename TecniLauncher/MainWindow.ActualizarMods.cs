using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TecniLauncher.Services;

namespace TecniLauncher
{
    /// <summary>
    /// "Buscar actualizaciones" en la pestaña Instalados: compara cada archivo del perfil con
    /// Modrinth (por SHA-1) y, si no está ahí, con CurseForge (por huella), y reemplaza los que
    /// tienen una versión más nueva para la versión de Minecraft y el loader del perfil.
    /// </summary>
    public partial class MainWindow
    {
        private sealed record ActualizacionPendiente(string RutaActual, string Titulo, string Url, string NombreNuevo, string? Sha1Nuevo, string Version);

        private const int MaxConsultasCurseForge = 60;
        private bool _buscandoActualizaciones;

        private async void BtnActualizarMods_Click(object sender, RoutedEventArgs e)
        {
            if (_buscandoActualizaciones) return;
            if (comboPerfilesMods.SelectedItem is not Perfil perfil) { MostrarToast(Core.T("Mods_SinPerfil"), false, true); return; }

            var tipo = _tipo;
            string carpeta = Path.Combine(perfil.RutaCarpeta, tipo.Carpeta());
            if (!Directory.Exists(carpeta)) { MostrarToast(Core.T("Mods_AlDia")); return; }

            string? loader = tipo.UsaLoader() ? perfil.TipoLoader : null;
            if (tipo.UsaLoader() && string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase)) { MostrarToast(Core.T("Mods_AlDia")); return; }

            _buscandoActualizaciones = true;
            btnActualizarMods.IsEnabled = false;
            MostrarToast(Core.T("Mods_Buscando"), true);
            try
            {
                var pendientes = await Task.Run(() => BuscarActualizacionesAsync(carpeta, tipo, perfil.Version, loader));
                if (pendientes.Count == 0) { MostrarToast(Core.T("Mods_AlDia")); return; }

                string lista = string.Join("\n", pendientes.Take(12).Select(p => $"• {p.Titulo}  →  {p.Version}"))
                               + (pendientes.Count > 12 ? $"\n... +{pendientes.Count - 12}" : "");
                if (VentanaMensaje.Mostrar(Core.T("Mods_ConfirmarActualizar", pendientes.Count, lista), Core.T("Mods_BuscarActualizaciones"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;

                MostrarToast(Core.T("Mods_Actualizando", pendientes.Count), true);
                var (ok, fallos) = await Task.Run(() => AplicarActualizacionesAsync(pendientes));

                CargarModsLocal(perfil);
                await RefrescarInstaladosOnlineAsync(perfil, soloMarcar: false);

                if (fallos == 0) MostrarToast(Core.T("Mods_Actualizados", ok));
                else MostrarToast(Core.T("Mods_ActualizadosConFallos", ok, fallos), false, true);
            }
            catch (Exception ex)
            {
                LogService.Warn("Error buscando actualizaciones de mods", ex);
                MostrarToast(Core.T("Mods_ErrorActualizar"), false, true);
            }
            finally
            {
                _buscandoActualizaciones = false;
                btnActualizarMods.IsEnabled = true;
            }
        }

        private static string SinDeshabilitado(string nombre) =>
            nombre.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? nombre[..^".disabled".Length] : nombre;

        private static async Task<List<ActualizacionPendiente>> BuscarActualizacionesAsync(string carpeta, TipoComplemento tipo, string versionMc, string? loader)
        {
            var archivos = ListarArchivosDeCarpeta(carpeta, tipo).ToList();
            var resultado = new List<ActualizacionPendiente>();
            if (archivos.Count == 0) return resultado;

            var conHash = archivos.Select(f => (Archivo: f, Sha1: CalcularSha1(f.FullName))).Where(x => x.Sha1 != null).ToList();

            // 1) Modrinth: una sola consulta para todos.
            var enModrinth = await ModrinthAPI.ObtenerProjectIdsPorHashAsync(conHash.Select(x => x.Sha1!).Distinct().ToList()) ?? new();
            var nuevasMr = await ModrinthAPI.BuscarActualizacionesAsync(conHash.Select(x => x.Sha1!), versionMc, tipo, loader);
            foreach (var (archivo, sha1) in conHash)
            {
                if (!nuevasMr.TryGetValue(sha1!, out var n) || !ModpacksApi.EsUrlPermitida(n.Url)) continue;
                if (string.Equals(SinDeshabilitado(archivo.Name), n.NombreArchivo, StringComparison.OrdinalIgnoreCase)) continue;
                resultado.Add(new ActualizacionPendiente(archivo.FullName, SinDeshabilitado(archivo.Name), n.Url, n.NombreArchivo, n.Sha1, n.Version));
            }

            // 2) CurseForge: solo para los que Modrinth no reconoce.
            if (!CurseForgeAPI.Disponible) return resultado;
            var restantes = conHash.Where(x => !enModrinth.ContainsKey(x.Sha1!)).Take(MaxConsultasCurseForge).ToList();
            if (restantes.Count == 0) return resultado;

            var huellas = new List<(FileInfo Archivo, uint Huella)>();
            foreach (var (archivo, _) in restantes)
            {
                try { huellas.Add((archivo, CurseForgeAPI.CalcularFingerprint(archivo.FullName))); } catch { }
            }
            var ids = await CurseForgeAPI.ObtenerModIdsPorFingerprintAsync(huellas.Select(h => h.Huella).Distinct().ToList());
            if (ids == null) return resultado;

            var encontrados = new System.Collections.Concurrent.ConcurrentBag<ActualizacionPendiente>();
            await Parallel.ForEachAsync(huellas.Where(h => ids.ContainsKey(h.Huella)), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (h, _) =>
            {
                try
                {
                    var versiones = await CurseForgeAPI.ObtenerListaVersiones(ids[h.Huella], versionMc, loader);
                    var ultima = versiones.FirstOrDefault(v => v.Tipo == "release") ?? versiones.FirstOrDefault();
                    if (ultima == null || string.IsNullOrEmpty(ultima.UrlDescarga) || !ModpacksApi.EsUrlPermitida(ultima.UrlDescarga)) return;
                    if (string.Equals(SinDeshabilitado(h.Archivo.Name), ultima.NombreArchivo, StringComparison.OrdinalIgnoreCase)) return;
                    encontrados.Add(new ActualizacionPendiente(h.Archivo.FullName, SinDeshabilitado(h.Archivo.Name), ultima.UrlDescarga, ultima.NombreArchivo, null, ultima.NombreVersion));
                }
                catch (Exception ex) { LogService.Debug($"No se pudo consultar CurseForge para {h.Archivo.Name}", ex); }
            });
            resultado.AddRange(encontrados);
            return resultado;
        }

        /// <summary>Descarga cada versión nueva; solo borra la antigua si la nueva llegó entera y con el hash correcto.</summary>
        private static async Task<(int Ok, int Fallos)> AplicarActualizacionesAsync(List<ActualizacionPendiente> pendientes)
        {
            int ok = 0, fallos = 0;
            foreach (var p in pendientes)
            {
                string carpeta = Path.GetDirectoryName(p.RutaActual)!;
                bool deshabilitado = p.RutaActual.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                string nombre = Path.GetFileName(p.NombreNuevo) + (deshabilitado ? ".disabled" : "");
                string? destino = ModpacksApi.ResolverRutaSegura(carpeta, nombre);
                if (destino == null) { fallos++; continue; }

                string temporal = destino + ".nuevo";
                try
                {
                    await AppHttpClient.DescargarArchivoAsync(p.Url, temporal);
                    if (p.Sha1Nuevo != null && !string.Equals(CalcularSha1(temporal), p.Sha1Nuevo, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El hash de la descarga no coincide");

                    File.Delete(p.RutaActual);
                    File.Move(temporal, destino, overwrite: true);
                    ok++;
                }
                catch (Exception ex)
                {
                    fallos++;
                    LogService.Warn($"No se pudo actualizar {p.Titulo}", ex);
                    try { if (File.Exists(temporal)) File.Delete(temporal); } catch { }
                }
            }
            return (ok, fallos);
        }
    }
}
