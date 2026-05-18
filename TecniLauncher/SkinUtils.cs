using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TecniLauncher.Services;
using Newtonsoft.Json;

namespace TecniLauncher
{
    public static class SkinUtils
    {
        private static readonly HttpClient _http = AppHttpClient.Instance;

        public static async Task<BitmapImage?> ObtenerSkinOnline(string usuario, bool esPremium)
        {
            if (string.IsNullOrEmpty(usuario)) return null;

            if (Core.EsTecniStudio && Core.SesionUsuario?.AccessToken == "token_tecnistudio")
                return await BuscarEnTecniStudio(Core.SesionUsuario.UUID);

            if (esPremium)
                return await BuscarEnMojang(usuario);

            return null;
        }

        private static async Task<BitmapImage?> BuscarEnTecniStudio(string uuidSinGuiones)
        {
            try
            {
                string url = $"https://kfxffvjakkcjbwkpvxtr.supabase.co/functions/v1/yggdrasil/sessionserver/session/minecraft/profile/{uuidSinGuiones}";
                string json = await _http.GetStringAsync(url);

                dynamic perfil = JsonConvert.DeserializeObject(json);
                if (perfil?.properties == null || perfil.properties.Count == 0) return null;

                string base64 = perfil.properties[0].value;
                string texturaJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                dynamic texturas = JsonConvert.DeserializeObject(texturaJson);

                string skinUrl = texturas?.textures?.SKIN?.url;
                if (string.IsNullOrEmpty(skinUrl)) return null;

                byte[] datos = await _http.GetByteArrayAsync(skinUrl);
                return BufToImage(datos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error cargando skin TecniStudio: " + ex.Message);
                return null;
            }
        }

        private static async Task<BitmapImage?> BuscarEnMojang(string usuario)
        {
            try
            {
                var response = await _http.GetAsync($"https://minotar.net/skin/{usuario}");
                if (!response.IsSuccessStatusCode) return null;
                return BufToImage(await response.Content.ReadAsByteArrayAsync());
            }
            catch { return null; }
        }

        private static BitmapImage BufToImage(byte[] buffer)
        {
            using var stream = new MemoryStream(buffer);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static ImageBrush? RecortarParte(BitmapSource skinCompleta, int x, int y, int w, int h)
        {
            if (skinCompleta == null) return null;
            try
            {
                var recorte = new CroppedBitmap(skinCompleta, new Int32Rect(x, y, w, h));
                var brush = new ImageBrush(recorte) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                return brush;
            }
            catch { return null; }
        }
        public static async Task<string> ObtenerUrlDirecta(string usuario, bool esPremium)
        {
            if (string.IsNullOrEmpty(usuario)) return null;

            if (Core.EsTecniStudio && Core.SesionUsuario != null)
            {
                try
                {
                    string url = $"https://kfxffvjakkcjbwkpvxtr.supabase.co/functions/v1/yggdrasil/sessionserver/session/minecraft/profile/{Core.SesionUsuario.UUID}";
                    string json = await _http.GetStringAsync(url);
                    dynamic perfil = JsonConvert.DeserializeObject(json);

                    if (perfil?.properties != null && perfil.properties.Count > 0)
                    {
                        string base64 = perfil.properties[0].value;
                        string texturaJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                        dynamic texturas = JsonConvert.DeserializeObject(texturaJson);

                        return texturas.textures.SKIN.url;
                    }
                }
                catch
                {
                }
            }

            if (esPremium)
            {
                return $"https://minotar.net/skin/{usuario}";
            }

            return "https://minotar.net/skin/steve";
        }
    }
}