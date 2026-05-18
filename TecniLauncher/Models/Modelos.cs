namespace TecniLauncher.Models
{
    public class Noticia
    {
        public string Titulo       { get; set; } = "";
        public string Imagen       { get; set; } = "";
        public string Cuerpo       { get; set; } = "";
        public bool   MostrarBoton { get; set; }
        public string BotonTexto   { get; set; } = "";
        public string BotonUrl     { get; set; } = "";
        public string BotonColor   { get; set; } = "#5865F2";
    }
    public class DatosUpdate
    {
        public string VersionMasReciente { get; set; } = "";
        public bool   EsCritica          { get; set; }
        public string LinkDescarga       { get; set; } = "";
        public string Sha256 { get; set; }
    }
}
