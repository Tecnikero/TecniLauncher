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
                LogService.Warn($"Versión remota inválida: '{versionRemota}'. Se ignora la actualización.");
                return false;
            }
            if (!TryParseVersion(versionLocal, out var local))
                return !string.Equals(versionRemota?.Trim(), versionLocal?.Trim(), StringComparison.OrdinalIgnoreCase);

            return remota > local;
        }
        private const string CLAVE_PUBLICA_FIRMA = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAED4piuCWl0GDxFLarf3pgUTDO/1k4+wFAK6tiiqYpMhKxg2TXPHONdRAWrvBNUQAJZpPB/0UCNm5yPNsgyH/Mjw==";
        private const int  MAX_ENTRADAS      = 20_000;
        private const long MAX_BYTES_TOTALES = 2L * 1024 * 1024 * 1024;

        private static ECDsa CargarClavePublica()
        {
            try
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(CLAVE_PUBLICA_FIRMA), out _);
                return ecdsa;
            }
            catch
            {
                throw new Exception("Error de seguridad: la clave pública de firma del launcher no está configurada. Actualización abortada.");
            }
        }

        public static async Task InstalarDesdeZipAsync(string urlDescarga, string hashEsperado, string firmaBase64,
            IProgress<string> progreso = null)
        {
            string rutaActual = Process.GetCurrentProcess().MainModule!.FileName;
            string directorio = Path.GetDirectoryName(rutaActual)!;
            string rutaZip = Path.Combine(directorio, "Update.zip");
            string carpetaTemp = Path.Combine(directorio, "Update_Temp");

            if (string.IsNullOrWhiteSpace(hashEsperado))
                throw new Exception("Error de seguridad: No se encontró el hash de verificación en el servidor. Actualización abortada.");
            if (string.IsNullOrWhiteSpace(firmaBase64))
                throw new Exception("Error de seguridad: La actualización no incluye firma digital. Actualización abortada.");
            using var clavePublica = CargarClavePublica();

            progreso?.Report("Descargando paquete de actualización...");
            await AppHttpClient.DescargarArchivoAsync(urlDescarga, rutaZip, TimeSpan.FromMinutes(30));

            try
            {
                progreso?.Report("Verificando integridad y firma digital (Seguridad)...");
                VerificarHashYFirma(rutaZip, hashEsperado, firmaBase64, clavePublica);

                progreso?.Report("Validando estructura del paquete...");
                ValidarEstructuraZip(rutaZip, carpetaTemp);

                progreso?.Report("Extrayendo archivos...");
                if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
                ExtraerZipSeguro(rutaZip, carpetaTemp);
            }
            catch
            {
                try { if (File.Exists(rutaZip)) File.Delete(rutaZip); } catch (Exception exLog) { LogService.Debug("No se pudo borrar Update.zip", exLog); }
                try { if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true); } catch (Exception exLog) { LogService.Debug("No se pudo borrar Update_Temp", exLog); }
                throw;
            }

            progreso?.Report("Preparando instalación...");
            LanzarScriptActualizacion(rutaActual, directorio, rutaZip, carpetaTemp);
        }

        private static void VerificarHashYFirma(string rutaZip, string hashEsperado, string firmaBase64, ECDsa clavePublica)
        {
            byte[] firma;
            try { firma = Convert.FromBase64String(firmaBase64.Trim()); }
            catch { throw new Exception("¡ALERTA DE SEGURIDAD! La firma digital de la actualización tiene un formato inválido."); }

            byte[] hashLocal;
            using (var stream = File.OpenRead(rutaZip))
                hashLocal = SHA256.HashData(stream);

            byte[] hashRemoto;
            try { hashRemoto = Convert.FromHexString(hashEsperado.Trim()); }
            catch { throw new Exception("¡ALERTA DE SEGURIDAD! El hash publicado tiene un formato inválido."); }

            if (!CryptographicOperations.FixedTimeEquals(hashLocal, hashRemoto))
                throw new Exception("¡ALERTA DE SEGURIDAD! El archivo descargado está corrupto o fue modificado. Actualización abortada para proteger tu PC.");

            using (var stream = File.OpenRead(rutaZip))
            {
                if (!clavePublica.VerifyData(stream, firma, HashAlgorithmName.SHA256))
                    throw new Exception("¡ALERTA DE SEGURIDAD! La firma digital de la actualización no es válida. Actualización abortada.");
            }
        }

        private static void ValidarEstructuraZip(string rutaZip, string carpetaDestino)
        {
            string raiz = Path.GetFullPath(carpetaDestino).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            try
            {
                using var zip = ZipFile.OpenRead(rutaZip);

                if (zip.Entries.Count == 0 || zip.Entries.Count > MAX_ENTRADAS)
                    throw new Exception("¡ALERTA DE SEGURIDAD! El paquete de actualización tiene una cantidad de archivos inválida.");

                long total = 0;
                foreach (var e in zip.Entries)
                {
                    total += e.Length;
                    if (total > MAX_BYTES_TOTALES)
                        throw new Exception("¡ALERTA DE SEGURIDAD! El paquete de actualización excede el tamaño permitido.");

                    string destino = Path.GetFullPath(Path.Combine(raiz, e.FullName));
                    if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("¡ALERTA DE SEGURIDAD! El paquete contiene rutas peligrosas. Actualización abortada.");
                }
            }
            catch (InvalidDataException)
            {
                throw new Exception("¡ALERTA DE SEGURIDAD! El paquete de actualización está dañado o no es un ZIP válido.");
            }
        }

        private static void ExtraerZipSeguro(string rutaZip, string carpetaDestino)
        {
            string raiz = Path.GetFullPath(carpetaDestino).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(raiz);

            using var zip = ZipFile.OpenRead(rutaZip);
            foreach (var e in zip.Entries)
            {
                string destino = Path.GetFullPath(Path.Combine(raiz, e.FullName));
                if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("¡ALERTA DE SEGURIDAD! Ruta peligrosa en el paquete. Actualización abortada.");

                if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(destino); continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                e.ExtractToFile(destino, overwrite: true);
            }
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