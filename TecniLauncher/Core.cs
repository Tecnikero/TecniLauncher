using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TecniLauncher
{
    public static class Core
    {
        public static string RutaGlobal { get; private set; }
        public static string RutaData { get; private set; }
        public static MSession? SesionUsuario { get; set; }
        public static MinecraftLauncher? LauncherGlobal { get; set; }
        public static bool MostrarSnapshots { get; set; } = false;
        public static List<Perfil> Perfiles { get; set; } = new List<Perfil>();
        public static string UltimoNombreOffline { get; set; } = "Jugador";
        public static string RutaSesion => Path.Combine(RutaData, "tcl_session.json");
        public static int JuegoAncho { get; set; } = 854;
        public static int JuegoAlto { get; set; } = 480;
        public static bool PantallaCompleta { get; set; } = false;
        public static bool EsElyBy { get; set; } = false;
        public static string IdiomaActual { get; set; } = "es-ES";

        public static async Task<bool> IntentarAutoLogin()
        {
            try
            {
                if (File.Exists(RutaSesion))
                {
                    var loginHandler = new JELoginHandlerBuilder()
                        .WithAccountManager(RutaSesion)
                        .Build();

                    var session = await loginHandler.AuthenticateSilently();

                    if (session != null)
                    {
                        SesionUsuario = session;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static void Inicializar()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string raiz = Path.Combine(appData, ".TecniLauncher");

            RutaData = Path.Combine(raiz, "Data");
            RutaGlobal = Path.Combine(raiz, "Global");

            if (!Directory.Exists(RutaData)) Directory.CreateDirectory(RutaData);
            if (!Directory.Exists(RutaGlobal)) Directory.CreateDirectory(RutaGlobal);

            var pathGlobal = new MinecraftPath(RutaGlobal);
            LauncherGlobal = new MinecraftLauncher(pathGlobal);

            CargarPerfiles();
        }
        public static bool EsVersionInstaladaGlobalmente(string idVersion)
        {
            try
            {
                string rutaVersionJson = Path.Combine(RutaGlobal, "versions", idVersion, $"{idVersion}.json");
                string rutaVersionJar = Path.Combine(RutaGlobal, "versions", idVersion, $"{idVersion}.jar");

                return File.Exists(rutaVersionJson);
            }
            catch
            {
                return false;
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
                    EsElyBy,
                    IdiomaActual
                };
                string json = JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(archivo, json);
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
                    if (doc.RootElement.TryGetProperty("EsElyBy", out var ely)) EsElyBy = ely.GetBoolean();
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
                string json = JsonSerializer.Serialize(Perfiles, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(archivo, json);
            }
            catch { }
        }

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