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
            if (!File.Exists(rutaCacheSesion)) return null;

            var handler = new JELoginHandlerBuilder()
                .WithAccountManager(rutaCacheSesion)
                .Build();

            return await handler.AuthenticateSilently();
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
                    AccessToken = "token_tecnistudio",
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

        public static async Task<string> ObtenerUuidTecniStudioAsync(string nombreUsuario)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/rest/v1/perfiles?username=eq.{nombreUsuario}&select=id");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {SUPABASE_ANON_KEY}");

                var response = await _http.SendAsync(request);

                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                dynamic resultado = JsonConvert.DeserializeObject(json);

                if (resultado.Count > 0)
                {
                    string idSupabase = resultado[0].id;
                    return idSupabase.Replace("-", "");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error obteniendo UUID de TecniStudio: " + ex.Message);
            }
            return null;
        }
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
        public static string GenerarUuidOffline(string nombreJugador)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + nombreJugador));

            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            return new Guid(hash).ToString("N");
        }

        public static MSession CrearSesionOffline(string nombre)
        {
            string uuid = GenerarUuidOffline(nombre);
            return new MSession
            {
                Username    = string.IsNullOrWhiteSpace(nombre) ? "Jugador" : nombre,
                UUID        = uuid,
                AccessToken = "token_offline",
                ClientToken = uuid,
                UserType    = "Legacy"
            };
        }
    }
}
