using CmlLib.Core;
using CmlLib.Core.Auth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TecniLauncher.Services;

namespace TecniLauncher
{
    public static class Core
    {
        public static string RutaGlobal { get; private set; }
        public static string RutaData { get; private set; }
        public static string RutaInstances { get; private set; }
        public static MSession? SesionUsuario { get; set; }
        public static MinecraftLauncher? LauncherGlobal { get; set; }
        public static bool MostrarSnapshots { get; set; } = false;
        public static List<Perfil> Perfiles { get; set; } = new List<Perfil>();
        public static string UltimoNombreOffline { get; set; } = "Player";
        public static string RutaSesion => Path.Combine(RutaData, "tcl_session.json");
        public static int JuegoAncho { get; set; } = 854;
        public static int JuegoAlto { get; set; } = 480;
        public static bool PantallaCompleta { get; set; } = false;
        public static bool EsTecniStudio { get; set; } = false;
        public static bool EsSesionTecniStudio =>
            EsTecniStudio && SesionUsuario != null && SesionUsuario.UserType == "mojang";
        public static string IdiomaActual { get; set; } = "en-US";
        public static string T(string clave, params object[] args)
        {
            string texto = System.Windows.Application.Current?.TryFindResource(clave) as string ?? clave;
            if (args == null || args.Length == 0) return texto;
            try { return string.Format(texto, args); }
            catch (FormatException) { return texto; }
        }

        public static void Inicializar()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string raiz = Path.Combine(appData, ".TecniLauncher");

            RutaData = Path.Combine(raiz, "Data");
            RutaGlobal = Path.Combine(raiz, "Global");
            RutaInstances = Path.Combine(raiz, "Instances");

            if (!Directory.Exists(RutaData)) Directory.CreateDirectory(RutaData);
            if (!Directory.Exists(RutaGlobal)) Directory.CreateDirectory(RutaGlobal);

            var pathGlobal = new MinecraftPath(RutaGlobal);
            LauncherGlobal = new MinecraftLauncher(pathGlobal);

            CargarPerfiles();

            try
            {
                if (string.IsNullOrEmpty(SecretsManager.ObtenerSecreto("supabase_anon_key")))
                {
                    string rutaSeed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.key");
                    if (File.Exists(rutaSeed))
                    {
                        string clave = File.ReadAllText(rutaSeed).Trim();
                        SecretsManager.GuardarSecreto("supabase_anon_key", clave);
                        File.Delete(rutaSeed);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error inicializando secretos: " + ex.Message);
            }
        }
        private static readonly object _lockArchivos = new object();
        private static void EscribirAtomico(string ruta, string contenido)
        {
            string tmp = ruta + ".tmp";
            string bak = ruta + ".bak";

            lock (_lockArchivos)
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.Write(contenido);
                    sw.Flush();
                    fs.Flush(true);
                }

                if (File.Exists(ruta))
                    File.Replace(tmp, ruta, bak);
                else
                    File.Move(tmp, ruta);
            }
        }

        public static void GuardarConfiguracion()
        {
            try
            {
                string archivo = Path.Combine(RutaData, "config.json");
                var datos = new
                {
                    UltimoNombreOffline,
                    MostrarSnapshots,
                    JuegoAncho,
                    JuegoAlto,
                    PantallaCompleta,
                    EsTecniStudio,
                    IdiomaActual
                };
                string json = JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true });
                EscribirAtomico(archivo, json);
            }
            catch { }
        }

        public static void CargarConfiguracion()
        {
            try
            {
                string archivo = Path.Combine(RutaData, "config.json");
                if (File.Exists(archivo))
                {
                    string json = File.ReadAllText(archivo);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("UltimoNombreOffline", out var nombre)) UltimoNombreOffline = nombre.GetString() ?? "Jugador";
                    if (doc.RootElement.TryGetProperty("MostrarSnapshots", out var snap)) MostrarSnapshots = snap.GetBoolean();
                    if (doc.RootElement.TryGetProperty("JuegoAncho", out var w)) JuegoAncho = w.GetInt32();
                    if (doc.RootElement.TryGetProperty("JuegoAlto", out var h)) JuegoAlto = h.GetInt32();
                    if (doc.RootElement.TryGetProperty("PantallaCompleta", out var f)) PantallaCompleta = f.GetBoolean();
                    if (doc.RootElement.TryGetProperty("EsTecniStudio", out var ely)) EsTecniStudio = ely.GetBoolean();
                    if (doc.RootElement.TryGetProperty("IdiomaActual", out var lang)) IdiomaActual = lang.GetString() ?? "es-ES";
                }
            }
            catch { }
        }

        public static void GuardarPerfiles()
        {
            try
            {
                string archivo = Path.Combine(RutaData, "perfiles.json");
                string json;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    json = SerializarPerfiles();
                else
                    json = dispatcher.Invoke(SerializarPerfiles);

                EscribirAtomico(archivo, json);
            }
            catch { }
        }

        private static string SerializarPerfiles()
            => JsonSerializer.Serialize(Perfiles, new JsonSerializerOptions { WriteIndented = true });

        public static void CargarPerfiles()
        {
            try
            {
                string archivo = Path.Combine(RutaData, "perfiles.json");
                if (File.Exists(archivo))
                {
                    string json = File.ReadAllText(archivo);
                    var listaCargada = JsonSerializer.Deserialize<List<Perfil>>(json);
                    if (listaCargada != null) Perfiles = listaCargada;
                }
            }
            catch { }
        }
        public static void CambiarIdioma(string cultura)
        {
            try
            {
                var appResources = System.Windows.Application.Current.Resources.MergedDictionaries;

                for (int i = appResources.Count - 1; i >= 0; i--)
                {
                    if (appResources[i].Source != null && appResources[i].Source.OriginalString.Contains("Lang/"))
                    {
                        appResources.RemoveAt(i);
                    }
                }

                var dict = new System.Windows.ResourceDictionary();
                dict.Source = new Uri($"Lang/{cultura}.xaml", UriKind.Relative);
                appResources.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error cambiando idioma: " + ex.Message);
            }
        }
    }
}