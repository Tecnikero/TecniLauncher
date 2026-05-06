using System.Windows;

namespace TecniLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
    }
}