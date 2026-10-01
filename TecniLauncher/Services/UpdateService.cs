using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using TecniLauncher.Models;

namespace TecniLauncher.Services
{
    public static class UpdateService
    {
        private const string URL_VERSION =
            "https://raw.githubusercontent.com/johan12390785/TecniLauncher-Data/refs/heads/main/LauncherUpdate/versionV2.json";

        private static readonly HttpClient _http = AppHttpClient.Instance;

        public static async Task<DatosUpdate> ObtenerDatosUpdateAsync()
        {
            string json = await _http.GetStringAsync(URL_VERSION);
            return JsonConvert.DeserializeObject<DatosUpdate>(json);
        }

        public static bool TryParseVersion(string? texto, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(texto)) return false;

            string s = texto.Trim().TrimStart('v', 'V');
            int corte = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (corte >= 0) s = s.Substring(0, corte);
            if (!s.Contains('.')) s += ".0";

            if (!Version.TryParse(s, out var p)) return false;
            version = new Version(p.Major, p.Minor, Math.Max(p.Build, 0), Math.Max(p.Revision, 0));
            return true;
        }

        public static bool HayActualizacion(string? versionRemota, string versionLocal)
        {
            if (!TryParseVersion(versionRemota, out var remota))
            {
                Debug.WriteLine($"Versión remota inválida: '{versionRemota}'. Se ignora la actualización.");
                return false;
            }
            if (!TryParseVersion(versionLocal, out var local))
                return !string.Equals(versionRemota?.Trim(), versionLocal?.Trim(), StringComparison.OrdinalIgnoreCase);

            return remota > local;
        }

        public static async Task InstalarDesdeZipAsync(string urlDescarga, string hashEsperado,
            IProgress<string> progreso = null)
        {
            string rutaActual = Process.GetCurrentProcess().MainModule!.FileName;
            string directorio = Path.GetDirectoryName(rutaActual)!;
            string rutaZip = Path.Combine(directorio, "Update.zip");
            string carpetaTemp = Path.Combine(directorio, "Update_Temp");

            if (string.IsNullOrWhiteSpace(hashEsperado))
            {
                throw new Exception("Error de seguridad: No se encontró el hash de verificación en el servidor. Actualización abortada.");
            }

            progreso?.Report("Descargando paquete de actualización...");
            await AppHttpClient.DescargarArchivoAsync(urlDescarga, rutaZip, TimeSpan.FromMinutes(30));

            progreso?.Report("Verificando integridad del archivo (Seguridad)...");

            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(rutaZip))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                string hashLocal = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                string hashComparar = hashEsperado.Trim().ToLower();

                if (hashLocal != hashComparar)
                {
                    stream.Close();
                    File.Delete(rutaZip);
                    throw new Exception("¡ALERTA DE SEGURIDAD! El archivo descargado está corrupto o fue modificado. Actualización abortada para proteger tu PC.");
                }
            }

            progreso?.Report("Archivo verificado con éxito.");

            progreso?.Report("Extrayendo archivos...");
            if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
            ZipFile.ExtractToDirectory(rutaZip, carpetaTemp);

            progreso?.Report("Preparando instalación...");
            LanzarScriptActualizacion(rutaActual, directorio, rutaZip, carpetaTemp);
        }
        private static string EscBat(string s) => s.Replace("%", "%%");

        private static void LanzarScriptActualizacion(
            string rutaActual, string directorio, string rutaZip, string carpetaTemp)
        {
            string nombreExe = Path.GetFileName(rutaActual);
            string nombreBat = "update_script.bat";
            string rutaBat = Path.Combine(directorio, nombreBat);

            string script = $"""
                @echo off
                chcp 65001 >nul
                cd /d "{EscBat(directorio)}"
                taskkill /f /im "{EscBat(nombreExe)}" >nul 2>&1
                timeout /t 2 /nobreak >nul
                xcopy /y /e "{EscBat(carpetaTemp)}\*" "{EscBat(directorio)}\"
                timeout /t 1 /nobreak >nul
                start "" "{EscBat(nombreExe)}"
                rd /s /q "{EscBat(carpetaTemp)}"
                del "{EscBat(rutaZip)}"
                del "%~f0"
                """;

            File.WriteAllText(rutaBat, script, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {nombreBat}",
                WorkingDirectory = directorio,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}