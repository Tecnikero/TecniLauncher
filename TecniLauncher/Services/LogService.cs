using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TecniLauncher.Services
{
    public enum SeveridadLog { Debug = 0, Info = 1, Warn = 2, Error = 3, Fatal = 4 }

    public static class LogService
    {
        private const long MAX_BYTES = 2L * 1024 * 1024;
        private const int DIAS_RETENCION = 7;
        public const string PREFIJO = "launcher-";

        private static readonly object _lock = new();
        private static bool _iniciado;

#if DEBUG
        public static SeveridadLog NivelMinimo { get; set; } = SeveridadLog.Debug;
#else
        public static SeveridadLog NivelMinimo { get; set; } = SeveridadLog.Info;
#endif

        private static string CarpetaLogs =>
            Core.RutaLogs ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".TecniLauncher", "Logs");

        public static string RutaActual => Path.Combine(CarpetaLogs, $"{PREFIJO}{DateTime.Now:yyyyMMdd}.log");

        public static void Iniciar()
        {
            lock (_lock)
            {
                if (_iniciado) return;
                _iniciado = true;
            }
            try
            {
                Directory.CreateDirectory(CarpetaLogs);
                var limite = DateTime.Now.AddDays(-DIAS_RETENCION);
                foreach (string f in Directory.EnumerateFiles(CarpetaLogs, PREFIJO + "*.log*"))
                {
                    try { if (File.GetLastWriteTime(f) < limite) File.Delete(f); } catch { }
                }
            }
            catch { }
            Info("=== Launcher iniciado ===");
        }

        public static void Debug(string mensaje, Exception? ex = null, [CallerMemberName] string miembro = "", [CallerFilePath] string archivo = "")
            => Escribir(SeveridadLog.Debug, mensaje, ex, miembro, archivo);
        public static void Info(string mensaje, Exception? ex = null, [CallerMemberName] string miembro = "", [CallerFilePath] string archivo = "")
            => Escribir(SeveridadLog.Info, mensaje, ex, miembro, archivo);
        public static void Warn(string mensaje, Exception? ex = null, [CallerMemberName] string miembro = "", [CallerFilePath] string archivo = "")
            => Escribir(SeveridadLog.Warn, mensaje, ex, miembro, archivo);
        public static void Error(string mensaje, Exception? ex = null, [CallerMemberName] string miembro = "", [CallerFilePath] string archivo = "")
            => Escribir(SeveridadLog.Error, mensaje, ex, miembro, archivo);
        public static void Fatal(string mensaje, Exception? ex = null, [CallerMemberName] string miembro = "", [CallerFilePath] string archivo = "")
            => Escribir(SeveridadLog.Fatal, mensaje, ex, miembro, archivo);

        private static void Escribir(SeveridadLog nivel, string mensaje, Exception? ex, string miembro, string archivo)
        {
            try
            {
                if (nivel < NivelMinimo) return;

                string origen = $"{Path.GetFileNameWithoutExtension(archivo)}.{miembro}";
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(nivel.ToString().ToUpperInvariant().PadRight(5)).Append("] [")
                  .Append(origen).Append("] ").Append(Sanitizar(mensaje));

                if (ex != null)
                    sb.AppendLine().Append("    ").Append(Sanitizar(ex.ToString()).Replace("\n", "\n    "));

                sb.AppendLine();
                string linea = sb.ToString();

                Trace.Write(linea);

                lock (_lock)
                {
                    Directory.CreateDirectory(CarpetaLogs);
                    string ruta = RutaActual;
                    Rotar(ruta);
                    File.AppendAllText(ruta, linea, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private static void Rotar(string ruta)
        {
            try
            {
                var fi = new FileInfo(ruta);
                if (!fi.Exists || fi.Length < MAX_BYTES) return;
                string anterior = ruta + ".1";
                if (File.Exists(anterior)) File.Delete(anterior);
                File.Move(ruta, anterior);
            }
            catch { }
        }

        private static readonly Regex _jwt =
            new(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.Compiled);
        private static readonly Regex _bearer =
            new(@"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled);
        private static readonly Regex _campos =
            new("(?i)(\"?(?:access_?token|refresh_?token|password|passwd|apikey|api_key|secret|client_?token)\"?\\s*[:=]\\s*\"?)[^\"\\s,;&}]+", RegexOptions.Compiled);

        internal static string Sanitizar(string? texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            texto = _jwt.Replace(texto, "[JWT-OCULTO]");
            texto = _bearer.Replace(texto, "$1[OCULTO]");
            texto = _campos.Replace(texto, "$1[OCULTO]");
            return texto;
        }
    }
}
