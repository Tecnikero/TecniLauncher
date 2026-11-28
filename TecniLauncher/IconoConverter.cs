using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TecniLauncher
{
    /// <summary>
    /// Convierte una URL de icono en una imagen decodificada al tamaño en que se muestra.
    /// Los iconos de Modrinth/CurseForge pueden ser de 512 px o más y se muestran a 40–64 px:
    /// decodificarlos enteros gasta mucha memoria y hace el scroll más pesado.
    /// El parámetro es el ancho en píxeles a decodificar (por defecto 96).
    /// </summary>
    public sealed class IconoConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            try
            {
                int ancho = parameter is string p && int.TryParse(p, out int n) ? n : 96;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = ancho;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

namespace TecniLauncher
{
    /// <summary>true → Collapsed, false → Visible.</summary>
    public sealed class BoolInversaVisibilidad : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type t, object p, System.Globalization.CultureInfo c) =>
            value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        public object ConvertBack(object value, System.Type t, object p, System.Globalization.CultureInfo c) => throw new System.NotSupportedException();
    }
}
