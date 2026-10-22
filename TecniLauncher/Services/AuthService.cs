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
                LogService.Warn("Error en AutoLoginMicrosoft", ex);
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

                _refreshToken = (string)authData.refresh_token;
                _expiraEn = CalcularExpiracion((int?)authData.expires_in);

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
                LogService.Error("Error crítico en LoginTecniStudio", ex);
                return null;
            }
        }
        public static bool EsUuidValido(string? uuid) =>
            !string.IsNullOrEmpty(uuid) && System.Text.RegularExpressions.Regex.IsMatch(uuid, "^[0-9a-fA-F]{32}$");
        private static readonly byte[] _entropiaSesion = Encoding.UTF8.GetBytes("TecniLauncher.TecniSession.v1");
        private static string RutaSesionTecni => Path.Combine(Core.RutaData, "tecni_session.bin");
        private static string RutaSesionTecniLegacy => Path.Combine(Core.RutaData, "tecni_session.json");

        private class SesionTecniGuardada
        {
            public MSession? Sesion { get; set; }
            public string? RefreshToken { get; set; }
            public DateTimeOffset ExpiraEn { get; set; }
        }

        // Supabase rota el refresh_token en cada uso: siempre hay que guardar el nuevo
        private static string? _refreshToken;
        private static DateTimeOffset _expiraEn = DateTimeOffset.MinValue;
        private static readonly SemaphoreSlim _lockRefresh = new(1, 1);
        private static readonly TimeSpan MargenRenovacion = TimeSpan.FromMinutes(5);

        private static DateTimeOffset CalcularExpiracion(int? expiresIn) =>
            DateTimeOffset.UtcNow.AddSeconds(expiresIn is > 0 ? expiresIn.Value : 3600);

        public enum EstadoToken { Valido, Expirado, SinConexion }

        public static async Task<EstadoToken> AsegurarTokenTecniAsync()
        {
            if (!Core.EsSesionTecniStudio) return EstadoToken.Valido;

            await _lockRefresh.WaitAsync();
            try
            {
                if (DateTimeOffset.UtcNow < _expiraEn - MargenRenovacion) return EstadoToken.Valido;
                if (string.IsNullOrEmpty(_refreshToken)) return EstadoToken.Expirado;

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/auth/v1/token?grant_type=refresh_token");
                    request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                    request.Content = new StringContent(JsonConvert.SerializeObject(new { refresh_token = _refreshToken }), Encoding.UTF8, "application/json");

                    using var response = await _http.SendAsync(request, cts.Token);
                    if ((int)response.StatusCode is 400 or 401 or 403)
                    {
                        LogService.Warn($"Refresh token de TecniStudio rechazado ({(int)response.StatusCode})");
                        return EstadoToken.Expirado;
                    }
                    if (!response.IsSuccessStatusCode) return EstadoToken.SinConexion;

                    dynamic data = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
                    string? nuevoAccess = data?.access_token;
                    string? nuevoRefresh = data?.refresh_token;
                    if (string.IsNullOrEmpty(nuevoAccess) || string.IsNullOrEmpty(nuevoRefresh)) return EstadoToken.SinConexion;

                    Core.SesionUsuario!.AccessToken = nuevoAccess;
                    _refreshToken = nuevoRefresh;
                    _expiraEn = CalcularExpiracion((int?)data.expires_in);
                    GuardarSesionTecni(Core.SesionUsuario);
                    return EstadoToken.Valido;
                }
                catch (Exception ex)
                {
                    LogService.Warn("No se pudo renovar el token de TecniStudio", ex);
                    return EstadoToken.SinConexion;
                }
            }
            finally { _lockRefresh.Release(); }
        }

        public static void GuardarSesionTecni(MSession sesion)
        {
            try
            {
                var guardada = new SesionTecniGuardada { Sesion = sesion, RefreshToken = _refreshToken, ExpiraEn = _expiraEn };
                byte[] datos = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(guardada));
                byte[] cifrado = ProtectedData.Protect(datos, _entropiaSesion, DataProtectionScope.CurrentUser);
                CryptographicOperations.ZeroMemory(datos);
                string tmp = RutaSesionTecni + ".tmp";
                File.WriteAllBytes(tmp, cifrado);
                File.Move(tmp, RutaSesionTecni, overwrite: true);

                BorrarSesionLegacy();
            }
            catch (Exception ex)
            {
                LogService.Warn("No se pudo guardar la sesión cifrada", ex);
            }
        }

        public static MSession CargarSesionTecni()
        {
            try
            {
                if (File.Exists(RutaSesionTecni))
                {
                    byte[] cifrado = File.ReadAllBytes(RutaSesionTecni);
                    byte[] datos = ProtectedData.Unprotect(cifrado, _entropiaSesion, DataProtectionScope.CurrentUser);
                    try
                    {
                        string json = Encoding.UTF8.GetString(datos);
                        var guardada = JsonConvert.DeserializeObject<SesionTecniGuardada>(json);
                        if (guardada?.Sesion != null)
                        {
                            _refreshToken = guardada.RefreshToken;
                            _expiraEn = guardada.ExpiraEn;
                            return guardada.Sesion;
                        }
                        // Formato anterior (solo MSession, sin refresh token): habrá que volver a iniciar sesión cuando caduque
                        _refreshToken = null;
                        _expiraEn = DateTimeOffset.MinValue;
                        return JsonConvert.DeserializeObject<MSession>(json);
                    }
                    finally { CryptographicOperations.ZeroMemory(datos); }
                }
                if (File.Exists(RutaSesionTecniLegacy))
                {
                    var legacy = JsonConvert.DeserializeObject<MSession>(File.ReadAllText(RutaSesionTecniLegacy));
                    if (legacy != null) GuardarSesionTecni(legacy);
                    BorrarSesionLegacy();
                    return legacy;
                }
            }
            catch (CryptographicException)
            {
                try { File.Delete(RutaSesionTecni); } catch (Exception exLog) { LogService.Debug("No se pudo borrar sesión corrupta", exLog); }
            }
            catch (Exception ex)
            {
                LogService.Warn("Error al cargar la sesión", ex);
            }
            return null;
        }

        private static void BorrarSesionLegacy()
        {
            try { if (File.Exists(RutaSesionTecniLegacy)) File.Delete(RutaSesionTecniLegacy); } catch (Exception exLog) { LogService.Debug("No se pudo borrar sesión antigua", exLog); }
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
                    LogService.Warn("No se pudo invalidar el token en Supabase", ex);
                }
            }

            CerrarSesionTecni();
        }

        public static void CerrarSesionTecni()
        {
            Core.SesionUsuario = null;
            Core.EsTecniStudio = false;
            _refreshToken = null;
            _expiraEn = DateTimeOffset.MinValue;

            try
            {
                if (File.Exists(RutaSesionTecni)) File.Delete(RutaSesionTecni);
                BorrarSesionLegacy();
            }
            catch (Exception ex)
            {
                LogService.Warn("Error al borrar sesión persistente", ex);
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