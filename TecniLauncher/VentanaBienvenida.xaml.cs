using System.Windows;
using System.Windows.Input;
using TecniLauncher.Services;

namespace TecniLauncher
{
    /// <summary>Novedades de una versión, una sola vez por instalación.</summary>
    public partial class VentanaBienvenida : Window
    {
        /// <summary>Versión cuyas novedades muestra esta ventana. Subirla cuando haya novedades nuevas que contar.</summary>
        public const string VersionNovedades = "1.6.0";

        public VentanaBienvenida()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        /// <summary>La muestra si este usuario aún no vio las novedades de <see cref="VersionNovedades"/>. Nunca lanza.</summary>
        public static void MostrarSiToca(Window? propietario)
        {
            try
            {
                if (Core.UltimasNovedadesVistas == VersionNovedades) return;
                Core.UltimasNovedadesVistas = VersionNovedades;
                Core.GuardarConfiguracion();

                var v = new VentanaBienvenida();
                if (propietario != null && propietario.IsVisible) v.Owner = propietario;
                else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                v.ShowDialog();
            }
            catch (Exception ex) { LogService.Debug("No se pudo mostrar la bienvenida", ex); }
        }

        private void BtnEmpezar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
