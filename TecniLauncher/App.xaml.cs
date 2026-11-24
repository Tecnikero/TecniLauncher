using System.Threading;
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

            if (!AdquirirInstanciaUnica())
            {
                Application.Current.Shutdown();
                return;
            }

            AnalyticsHost.Inicializar(TecniLauncher.MainWindow.VERSION_ACTUAL); AnalyticsHost.Start();

            var ventana = new MainWindow();
            ventana.Show();
            EscucharOtrasInstancias(ventana);
        }

        // ---------------------------------------------------------------- instancia única

        private const string NombreMutex = @"Local\TecniLauncher.InstanciaUnica";
        private const string NombreEvento = @"Local\TecniLauncher.MostrarVentana";
        private static Mutex? _mutex;
        private static EventWaitHandle? _evento;

        /// <summary>
        /// true si esta es la única instancia. Si ya hay otra, le pide que se muestre y espera
        /// unos segundos por si se está cerrando (p. ej. el actualizador reiniciando el launcher).
        /// </summary>
        private static bool AdquirirInstanciaUnica()
        {
            try
            {
                _mutex = new Mutex(false, NombreMutex);
                _evento = new EventWaitHandle(false, EventResetMode.AutoReset, NombreEvento);
                try { if (_mutex.WaitOne(0)) return true; }
                catch (AbandonedMutexException) { return true; }

                _evento.Set(); // que la ventana abierta se muestre
                try { return _mutex.WaitOne(TimeSpan.FromSeconds(3)); }
                catch (AbandonedMutexException) { return true; }
            }
            catch (Exception ex)
            {
                LogService.Warn("No se pudo comprobar la instancia única; se abre igualmente", ex);
                return true;
            }
        }

        private static void EscucharOtrasInstancias(Window ventana)
        {
            if (_evento == null) return;
            var hilo = new Thread(() =>
            {
                try
                {
                    while (_evento.WaitOne())
                    {
                        ventana.Dispatcher.BeginInvoke(() =>
                        {
                            if (!ventana.IsVisible) ventana.Show();
                            if (ventana.WindowState == WindowState.Minimized) ventana.WindowState = WindowState.Normal;
                            ventana.Activate();
                            ventana.Topmost = true; ventana.Topmost = false;
                        });
                    }
                }
                catch (Exception ex) { LogService.Debug("Escucha de instancia única terminada", ex); }
            }) { IsBackground = true, Name = "InstanciaUnica" };
            hilo.Start();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Error("Excepción no controlada en la interfaz (la app sigue abierta)", e.Exception);
            e.Handled = true;
        }
    }
}