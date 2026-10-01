using System.Windows;
using System.Windows.Threading;

namespace TecniLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;

            Core.Inicializar();
            Core.CargarConfiguracion();

            if (e.Args.Length > 0 && e.Args[0] == "--init-secrets")
            {
                Application.Current.Shutdown();
                return;
            }

            var ventana = new MainWindow();
            ventana.Show();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {e.Exception}");
            e.Handled = true;
        }
    }
}