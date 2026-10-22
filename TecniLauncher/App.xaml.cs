using System.Windows;
using System.Windows.Threading;
using TecniLauncher.Services;

namespace TecniLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
                LogService.Fatal("Excepción no controlada (la app puede cerrarse)", ev.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, ev) =>
            {
                LogService.Error("Excepción de tarea no observada", ev.Exception);
                ev.SetObserved();
            };

            Core.Inicializar();
            LogService.Iniciar();
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
            LogService.Error("Excepción no controlada en la interfaz (la app sigue abierta)", e.Exception);
            e.Handled = true;
        }
    }
}