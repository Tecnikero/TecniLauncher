using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using TecniLauncher.Models;

namespace TecniLauncher.Services
{
    public static class ConsolaJuegoService
    {
        private static readonly ConcurrentQueue<LineaLog> _cola = new();
        private static readonly object _lockArchivo = new();
        private static StreamWriter? _archivo;

        public static string? RutaArchivoActual { get; private set; }

        public static event Action? SesionIniciada;

        public static event Action<int?>? SesionFinalizada;

        public static void IniciarSesion(string nombrePerfil)
        {
            lock (_lockArchivo)
            {
                CerrarArchivo();
                try
                {
                    Directory.CreateDirectory(Core.RutaLogs);
                    string seguro = string.Concat(nombrePerfil.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    RutaArchivoActual = Path.Combine(Core.RutaLogs, $"consola-{seguro}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    _archivo = new StreamWriter(
                        new FileStream(RutaArchivoActual, FileMode.Create, FileAccess.Write, FileShare.Read),
                        new UTF8Encoding(false));
                }
                catch (Exception exLog)
                {
                    LogService.Warn("No se pudo crear el archivo de log de la consola", exLog);
                    _archivo = null;
                    RutaArchivoActual = null;
                }
            }

            SesionIniciada?.Invoke();
        }

        public static void Adjuntar(Process proceso)
        {
            proceso.OutputDataReceived += (_, e) => { if (e.Data != null) Registrar(e.Data); };
            proceso.ErrorDataReceived += (_, e) => { if (e.Data != null) Registrar(e.Data, null, true); };

            proceso.EnableRaisingEvents = true;
            proceso.Exited += (_, _) =>
            {
                int? codigo = null;
                try
                {
                    proceso.WaitForExit();
                    codigo = proceso.ExitCode;
                }
                catch (Exception exLog) { LogService.Debug("No se pudo leer el código de salida del juego", exLog); }
                FinalizarSesion(codigo);
            };

            proceso.BeginOutputReadLine();
            proceso.BeginErrorReadLine();
        }

        public static void Registrar(string texto, NivelLog? nivel = null, bool deStderr = false)
        {
            var linea = new LineaLog(texto, nivel ?? Detectar(texto, deStderr));
            _cola.Enqueue(linea);

            lock (_lockArchivo)
            {
                try { _archivo?.WriteLine(texto); } catch (Exception exLog) { LogService.Debug("No se pudo escribir en el log de consola", exLog); }
            }
        }

        public static List<LineaLog> Drenar(int maximo = 1000)
        {
            var lote = new List<LineaLog>();
            while (lote.Count < maximo && _cola.TryDequeue(out var linea))
                lote.Add(linea);

            if (lote.Count > 0)
            {
                lock (_lockArchivo)
                {
                    try { _archivo?.Flush(); } catch (Exception exLog) { LogService.Debug("No se pudo vaciar el log de consola", exLog); }
                }
            }
            return lote;
        }

        public static void Cerrar()
        {
            lock (_lockArchivo) CerrarArchivo();
        }

        private static void FinalizarSesion(int? codigo)
        {
            if (codigo == 0)
                Registrar(Texto("Console_ExitOk"), NivelLog.Sistema);
            else
                Registrar(Texto("Console_ExitError", codigo?.ToString() ?? "?"), NivelLog.Error);

            lock (_lockArchivo) CerrarArchivo();
            SesionFinalizada?.Invoke(codigo);
        }

        private static void CerrarArchivo()
        {
            try
            {
                _archivo?.Flush();
                _archivo?.Dispose();
            }
            catch (Exception exLog) { LogService.Debug("No se pudo cerrar el log de consola", exLog); }
            _archivo = null;
        }

        private static string Texto(string clave, params object[] args)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return clave;
            return dispatcher.CheckAccess() ? Core.T(clave, args) : dispatcher.Invoke(() => Core.T(clave, args));
        }

        private static NivelLog Detectar(string t, bool deStderr)
        {
            if (t.Contains("/FATAL]") || t.Contains("/ERROR]")) return NivelLog.Error;
            if (t.Contains("/WARN]")) return NivelLog.Advertencia;
            if (t.Contains("/INFO]") || t.Contains("/DEBUG]")) return NivelLog.Info;

            if (t.StartsWith("\tat ") || t.StartsWith("Caused by:") || t.Contains("Exception") || t.Contains("Error:"))
                return NivelLog.Error;

            return deStderr ? NivelLog.Error : NivelLog.Info;
        }
    }
}
