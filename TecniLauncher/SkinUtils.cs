using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace TecniLauncher
{
    public static class SkinUtils
    {
        public static async Task<BitmapImage> ObtenerSkinOnline(string usuario, bool esPremium)
        {
            if (string.IsNullOrEmpty(usuario)) return null;

            if (esPremium)
            {
                var skin = await BuscarEnMojang(usuario);
                if (skin != null) return skin;

                return await BuscarEnElyBy(usuario);
            }
            else
            {
                var skin = await BuscarEnElyBy(usuario);
                if (skin != null) return skin;

                return await BuscarEnMojang(usuario);
            }
        }
        private static async Task<BitmapImage> BuscarEnElyBy(string usuario)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher");
                    string url = $"https://skinsystem.ely.by/skins/{usuario}.png";
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        return BufferToImage(data);
                    }
                }
            }
            catch { }
            return null;
        }
        private static async Task<BitmapImage> BuscarEnMojang(string usuario)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher");
                    string url = $"https://minotar.net/skin/{usuario}";
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        return BufferToImage(data);
                    }
                }
            }
            catch { }
            return null;
        }
        private static BitmapImage BufferToImage(byte[] buffer)
        {
            using (var stream = new MemoryStream(buffer))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        public static ImageBrush RecortarParte(BitmapSource skinCompleta, int x, int y, int w, int h)
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
    }
}