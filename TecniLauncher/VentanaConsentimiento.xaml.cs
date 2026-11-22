using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using TecniLauncher.Services;

namespace TecniLauncher
{
    /// <summary>Pregunta única por las estadísticas voluntarias. Cerrarla sin elegir no decide nada.</summary>
    public partial class VentanaConsentimiento : Window
    {
        public VentanaConsentimiento()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        /// <summary>Muestra la ventana si hace falta y aplica la decisión. Nunca lanza.</summary>
        public static void Preguntar(Window? propietario)
        {
            try
            {
                if (!AnalyticsHost.NecesitaConsentimiento) return;
                var v = new VentanaConsentimiento();
                if (propietario != null && propietario.IsVisible) v.Owner = propietario;
                else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                v.ShowDialog();
            }
            catch (Exception ex) { LogService.Debug($"Analytics: no se pudo mostrar el consentimiento ({ex.GetType().Name})"); }
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e)
        {
            AnalyticsHost.Aceptar();
            Close();
        }

        private void BtnNoParticipar_Click(object sender, RoutedEventArgs e)
        {
            AnalyticsHost.Rechazar();
            Close();
        }

        private void Enlace_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true }); }
            catch (Exception ex) { LogService.Debug($"No se pudo abrir el enlace de privacidad ({ex.GetType().Name})"); }
            e.Handled = true;
        }
    }
}
