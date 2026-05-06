using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using TecniLauncher.Models;

namespace TecniLauncher.Services
{
    public static class NewsService
    {
        private const string URL_NOTICIAS =
            "https://raw.githubusercontent.com/johan12390785/TecniLauncher-Data/refs/heads/main/Web/noticias.json";

        private const int MAX_NOTICIAS = 5;

        private static readonly HttpClient _http = AppHttpClient.Instance;

        private static readonly Dictionary<string, BitmapImage> _cacheImagenes = new();

        private static List<Noticia>? _noticiasCache = null;

        public static async Task<List<Noticia>> ObtenerNoticiasAsync(bool forzarRecarga = false)
        {
            if (_noticiasCache != null && !forzarRecarga)
                return _noticiasCache;

            string json   = await _http.GetStringAsync(URL_NOTICIAS);
            var    todas  = JsonConvert.DeserializeObject<List<Noticia>>(json)!;
            _noticiasCache = todas.Take(MAX_NOTICIAS).ToList();
            return _noticiasCache;
        }

        public static async Task<BitmapImage?> ObtenerImagenAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            if (_cacheImagenes.TryGetValue(url, out var cached))
                return cached;

            BitmapImage? imagen = await DescargarImagenAsync(url);

            if (imagen != null)
                _cacheImagenes[url] = imagen;

            return imagen;
        }

        private static async Task<BitmapImage?> DescargarImagenAsync(string url)
        {
            try
            {
                byte[] datos = await _http.GetByteArrayAsync(url);

                BitmapImage? resultado = null;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    using var stream = new MemoryStream(datos);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption   = BitmapCacheOption.OnLoad;
                    img.DecodePixelWidth = 900;
                    img.StreamSource  = stream;
                    img.EndInit();
                    img.Freeze();
                    resultado = img;
                });

                return resultado;
            }
            catch
            {
                return null;
            }
        }

        public static void LimpiarCache()
        {
            _noticiasCache = null;
            _cacheImagenes.Clear();
        }
    }
}
