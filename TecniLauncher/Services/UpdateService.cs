using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
        public static async Task InstalarDesdeZipAsync(string urlDescarga,
            IProgress<string> progreso = null)
        {
            string rutaActual  = Process.GetCurrentProcess().MainModule!.FileName;
            string directorio  = Path.GetDirectoryName(rutaActual)!;
            string rutaZip     = Path.Combine(directorio, "Update.zip");
            string carpetaTemp = Path.Combine(directorio, "Update_Temp");

            progreso?.Report("Descargando paquete de actualización...");
            byte[] bytes = await _http.GetByteArrayAsync(urlDescarga);
            await File.WriteAllBytesAsync(rutaZip, bytes);

            progreso?.Report("Extrayendo archivos...");
            if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
            ZipFile.ExtractToDirectory(rutaZip, carpetaTemp);

            progreso?.Report("Preparando instalación...");
            LanzarScriptActualizacion(rutaActual, directorio, rutaZip, carpetaTemp);
        }

        private static void LanzarScriptActualizacion(
            string rutaActual, string directorio, string rutaZip, string carpetaTemp)
        {
            string nombreExe = Path.GetFileName(rutaActual);
            string rutaBat   = Path.Combine(directorio, "update_script.bat");

            string script = $"""
                @echo off
                cd /d "{directorio}"
                taskkill /f /im "{nombreExe}" >nul 2>&1
                timeout /t 2 /nobreak >nul
                xcopy /y /e "{carpetaTemp}\*" "{directorio}\"
                timeout /t 1 /nobreak >nul
                start "" "{nombreExe}"
                rd /s /q "{carpetaTemp}"
                del "{rutaZip}"
                del "%~f0"
                """;

            File.WriteAllText(rutaBat, script);

            Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/c \"\"{rutaBat}\"\"",
                CreateNoWindow  = true,
                UseShellExecute = false,
                WindowStyle     = ProcessWindowStyle.Hidden
            });
        }
    }
}
