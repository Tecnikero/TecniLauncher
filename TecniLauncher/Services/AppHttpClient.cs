using System.IO;
using System.Net.Http;

namespace TecniLauncher.Services
{
    public static class AppHttpClient
    {
        public static readonly HttpClient Instance;
        public static readonly HttpClient Descargas;

        static AppHttpClient()
        {
            Instance = new HttpClient(CrearHandler());
            Instance.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0 (Johan/t3cnikero)");
            Instance.Timeout = TimeSpan.FromSeconds(30);

            Descargas = new HttpClient(CrearHandler());
            Descargas.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0 (Johan/t3cnikero)");
            Descargas.Timeout = Timeout.InfiniteTimeSpan;
        }

        private static SocketsHttpHandler CrearHandler() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        public static async Task DescargarArchivoAsync(
            string url, string destino, TimeSpan? limite = null, CancellationToken ct = default, IProgress<int>? porcentaje = null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(limite ?? TimeSpan.FromMinutes(10));

            string parcial = destino + ".part";
            try
            {
                using var resp = await Descargas.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;

                await using (var origen = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                await using (var archivo = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    if (porcentaje == null || total is not > 0)
                    {
                        await origen.CopyToAsync(archivo, cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        var buffer = new byte[81920];
                        long leidos = 0;
                        int ultimo = -1, n;
                        while ((n = await origen.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
                        {
                            await archivo.WriteAsync(buffer.AsMemory(0, n), cts.Token).ConfigureAwait(false);
                            leidos += n;
                            int pct = (int)(leidos * 100 / total.Value);
                            if (pct != ultimo) { ultimo = pct; porcentaje.Report(Math.Min(pct, 100)); }
                        }
                    }
                }
                File.Move(parcial, destino, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(parcial)) File.Delete(parcial); } catch (Exception exLog) { LogService.Debug("No se pudo borrar archivo parcial de descarga", exLog); }
                throw;
            }
        }
    }
}