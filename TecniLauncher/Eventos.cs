using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.IO;

namespace TecniLauncher
{
    public class ArchivoEvento
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string RutaRelativa { get; set; }
        public string UrlDescarga { get; set; }
        public string HashSHA1 { get; set; }
        public long TamanoBytes { get; set; }
        public bool Descomprimir { get; set; }
    }

    public class EventoModelo : INotifyPropertyChanged
    {
        public string Id { get; set; }

        [JsonPropertyName("Titulo")]
        public string Nombre { get; set; }

        public string VersionEvento { get; set; }
        public string VersionMinecraft { get; set; }

        public string Loader { get; set; }
        public string VersionLoader { get; set; }

        public int MemoriaMinima { get; set; }

        public string UrlIcono { get; set; }
        public string UrlFondo { get; set; }
        public string DiscordLink { get; set; }

        public List<ArchivoEvento> Archivos { get; set; } = new List<ArchivoEvento>();

        public string RutaCarpeta => Path.Combine(
            Directory.GetParent(Core.RutaData).FullName, "GameData", "Eventos", Id ?? "Evento_Temp");

        private int _memoriaRam = 4096;
        public int MemoriaRam
        {
            get => _memoriaRam;
            set
            {
                if (_memoriaRam != value)
                {
                    _memoriaRam = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MemoriaRamGB));
                }
            }
        }

        public int MemoriaRamGB
        {
            get => MemoriaRam / 1024;
            set => MemoriaRam = value * 1024;
        }

        public List<int> OpcionesRamGB { get; } = new List<int> { 2, 4, 6, 8, 10, 12, 16, 24, 32 };
        public string MemoriaRamTexto => $"{MemoriaRam / 1024} GB";

        private string _textoBoton = "CARGANDO...";
        public string TextoBoton { get => _textoBoton; set { _textoBoton = value; OnPropertyChanged(); } }

        private Brush _colorEstado = Brushes.Gray;
        public Brush ColorEstado { get => _colorEstado; set { _colorEstado = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class EventosManager
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly JsonSerializerOptions OpcionesJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        static EventosManager()
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher-App");
        }

        public static async Task<List<EventoModelo>> CargarTodosLosEventos(string urlIndiceEventos)
        {
            var listaResultados = new List<EventoModelo>();

            try
            {
                string rawIndice = await client.GetStringAsync(urlIndiceEventos);

                var urlsEventos = JsonSerializer.Deserialize<List<string>>(rawIndice, OpcionesJson);

                if (urlsEventos == null) return listaResultados;

                foreach (string urlEvento in urlsEventos)
                {
                    try
                    {
                        string jsonEvento = await client.GetStringAsync(urlEvento);
                        var evento = JsonSerializer.Deserialize<EventoModelo>(jsonEvento, OpcionesJson);

                        if (evento != null)
                        {
                            if (string.IsNullOrEmpty(evento.Id)) evento.Id = "sin_id_" + Guid.NewGuid();

                            if (evento.MemoriaMinima > 4096)
                            {
                                evento.MemoriaRam = evento.MemoriaMinima;
                            }

                            listaResultados.Add(evento);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error cargando evento individual ({urlEvento}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cargando índice maestro: {ex.Message}");
            }

            return listaResultados;
        }
    }
}