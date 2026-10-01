using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace TecniLauncher.Services
{
    public static class AuthService
    {
        private static readonly HttpClient _http = AppHttpClient.Instance;
        private static readonly string SUPABASE_URL = "https://kfxffvjakkcjbwkpvxtr.supabase.co";
        private static string SUPABASE_ANON_KEY => SecretsManager.ObtenerSecreto("supabase_anon_key");

        public static async Task<MSession> LoginMicrosoftAsync(string rutaCacheSesion)
        {
            var handler = new JELoginHandlerBuilder()
                .WithAccountManager(rutaCacheSesion)
                .Build();

            return await handler.AuthenticateInteractively();
        }

        public static async Task<MSession> AutoLoginMicrosoftAsync(string rutaCacheSesion)
        {
            try
            {
                if (!File.Exists(rutaCacheSesion)) return null;

                var handler = new JELoginHandlerBuilder()
                    .WithAccountManager(rutaCacheSesion)
                    .Build();

                return await handler.AuthenticateSilently();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en AutoLoginMicrosoft: " + ex.Message);
                return null;
            }
        }

        public static void CerrarSesionMicrosoft(string rutaCacheSesion)
        {
            var handler = new JELoginHandlerBuilder()
                .WithAccountManager(rutaCacheSesion)
                .Build();

            handler.Signout();

            if (File.Exists(rutaCacheSesion))
                File.Delete(rutaCacheSesion);
        }

        public static async Task<MSession> LoginTecniStudioAsync(string email, string password)
        {
            try
            {
                var body = new { email = email, password = password };
                var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                var authRequest = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/auth/v1/token?grant_type=password");
                authRequest.Headers.Add("apikey", SUPABASE_ANON_KEY);
                authRequest.Content = content;

                var authResponse = await _http.SendAsync(authRequest);

                if (!authResponse.IsSuccessStatusCode) return null;

                dynamic authData = JsonConvert.DeserializeObject(await authResponse.Content.ReadAsStringAsync());
                string userId = authData.user.id;
                string accessToken = authData.access_token;

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(userId)) return null;

                var perfilRequest = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/rest/v1/perfiles?id=eq.{userId}&select=username");
                perfilRequest.Headers.Add("apikey", SUPABASE_ANON_KEY);
                perfilRequest.Headers.Add("Authorization", $"Bearer {accessToken}");

                var perfilResponse = await _http.SendAsync(perfilRequest);

                string username = "JugadorTecni";
                if (perfilResponse.IsSuccessStatusCode)
                {
                    dynamic perfilData = JsonConvert.DeserializeObject(await perfilResponse.Content.ReadAsStringAsync());
                    if (perfilData != null && perfilData.Count > 0)
                    {
                        username = perfilData[0].username;
                    }
                }

                return new MSession
                {
                    Username = username,
                    UUID = userId.Replace("-", ""),
                    AccessToken = accessToken,
                    ClientToken = Guid.NewGuid().ToString("N"),
                    UserType = "mojang"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error crítico en LoginTecniStudio: " + ex.Message);
                return null;
            }
        }
        public static bool EsUuidValido(string? uuid) =>
            !string.IsNullOrEmpty(uuid) && System.Text.RegularExpressions.Regex.IsMatch(uuid, "^[0-9a-fA-F]{32}$");

        public static void GuardarSesionTecni(MSession sesion)
        {
            try
            {
                string ruta = Path.Combine(Core.RutaData, "tecni_session.json");
                string json = JsonConvert.SerializeObject(sesion, Formatting.Indented);
                File.WriteAllText(ruta, json);
            }
            catch { }
        }

        public static MSession CargarSesionTecni()
        {
            try
            {
                string ruta = Path.Combine(Core.RutaData, "tecni_session.json");
                if (File.Exists(ruta))
                {
                    string json = File.ReadAllText(ruta);
                    return JsonConvert.DeserializeObject<MSession>(json);
                }
            }
            catch { }
            return null;
        }
        public static async Task CerrarSesionTecniAsync()
        {
            string? token = Core.EsSesionTecniStudio ? Core.SesionUsuario?.AccessToken : null;

            if (!string.IsNullOrEmpty(token) && token.Split('.').Length == 3)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/auth/v1/logout?scope=local");
                    request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                    request.Headers.Add("Authorization", $"Bearer {token}");
                    await _http.SendAsync(request, cts.Token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("No se pudo invalidar el token en Supabase: " + ex.Message);
                }
            }

            CerrarSesionTecni();
        }

        public static void CerrarSesionTecni()
        {
            Core.SesionUsuario = null;
            Core.EsTecniStudio = false;

            try
            {
                string ruta = Path.Combine(Core.RutaData, "tecni_session.json");
                if (File.Exists(ruta))
                {
                    File.Delete(ruta);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al borrar sesión persistente: " + ex.Message);
            }
        }
        public static string GenerarUuidOffline(string nombreJugador)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + nombreJugador));

            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            return Convert.ToHexString(hash).ToLowerInvariant();
        }

    }
}