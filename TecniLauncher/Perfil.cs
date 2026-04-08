using System;
using System.IO;

namespace TecniLauncher
{
    public class Perfil
    {
        public string Nombre { get; set; }
        public string Version { get; set; }
        public string TipoLoader { get; set; }
        public string RutaCarpeta { get; set; }
        public int MemoriaRam { get; set; }
        public string VersionLoaderExacta { get; set; }
        public string IconoPath { get; set; } = "/Resources/Icons/icon1.png";

        public Perfil() { }

        public Perfil(string nombre, string version, string loader, int ramMB)
        {
            Nombre = nombre;
            Version = version;
            TipoLoader = loader;
            MemoriaRam = ramMB;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string carpetaInstances = Path.Combine(appData, ".TecniLauncher", "Instances");

            if (!Directory.Exists(carpetaInstances)) Directory.CreateDirectory(carpetaInstances);

            RutaCarpeta = Path.Combine(carpetaInstances, nombre);
        }

        public string DetallesVisuales
        {
            get
            {
                double gb = MemoriaRam / 1024.0;
                string textoLoader = TipoLoader;
                if (!string.IsNullOrEmpty(VersionLoaderExacta))
                {
                    textoLoader = $"{TipoLoader} {VersionLoaderExacta}";
                }
                return $"{textoLoader}  •  {gb:0.#} GB RAM";
            }
        }
    }
}