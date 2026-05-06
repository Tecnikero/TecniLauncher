using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TecniLauncher.Services
{
    public static class SecretsManager
    {
        private static string RutaSecretos => Path.Combine(Core.RutaData, "secrets.bin");

        public static void GuardarSecreto(string clave, string valor)
        {
            var secretos = CargarTodos();
            secretos[clave] = valor;

            byte[] datos = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secretos));
            byte[] cifrado = ProtectedData.Protect(datos, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(RutaSecretos, cifrado);
        }

        public static string ObtenerSecreto(string clave)
        {
            try
            {
                if (!File.Exists(RutaSecretos)) return "";

                byte[] cifrado = File.ReadAllBytes(RutaSecretos);
                byte[] datos = ProtectedData.Unprotect(cifrado, null, DataProtectionScope.CurrentUser);
                var secretos = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    Encoding.UTF8.GetString(datos));

                return secretos?.GetValueOrDefault(clave) ?? "";
            }
            catch { return ""; }
        }

        private static Dictionary<string, string> CargarTodos()
        {
            try
            {
                if (!File.Exists(RutaSecretos)) return new();

                byte[] cifrado = File.ReadAllBytes(RutaSecretos);
                byte[] datos = ProtectedData.Unprotect(cifrado, null, DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                    Encoding.UTF8.GetString(datos)) ?? new();
            }
            catch { return new(); }
        }
    }
}