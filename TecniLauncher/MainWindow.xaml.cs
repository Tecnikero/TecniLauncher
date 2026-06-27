using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using DiscordRPC;
using DiscordRPC.Logging;
using Markdig;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TecniLauncher;
using TecniLauncher.Helpers;
using TecniLauncher.Models;
using TecniLauncher.Services;
using static System.Net.WebRequestMethods;
using static TecniLauncher.ModpacksApi;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Path = System.IO.Path;

namespace TecniLauncher
{

    public partial class MainWindow : Window
    {
        #region Variables Privadas y Estado
        private bool esPremium = false;
        private Perfil perfilAEditar = null;
        private bool modoOnline = true;
        private List<Noticia> listaNoticias = new List<Noticia>();
        private int indiceActual = 0;
        private const string VERSION_ACTUAL = "1.4.4";
        private CancellationTokenSource ctsActualizacion;
        private bool estaCargando = false;
        private DispatcherTimer _timerNoticias;
        public DiscordRpcClient client;
        private Rect _tamanoNormal;
        private bool _esFalsoMaximizado = false;
        private static readonly HttpClient _httpClient = new HttpClient();
        private int _offsetMods = 0;
        private bool _hayMasResultados = true;
        private readonly SemaphoreSlim _semMods = new SemaphoreSlim(1, 1);
        private System.Collections.ObjectModel.ObservableCollection<ModInfo> _listaModsActual
            = new System.Collections.ObjectModel.ObservableCollection<ModInfo>();
        private int _offsetModpacks = 0;
        private bool _hayMasModpacks = true;
        private readonly SemaphoreSlim _semModpacks = new SemaphoreSlim(1, 1);
        private System.Collections.ObjectModel.ObservableCollection<ModpackProject> _listaModpacksActual = new();
        #endregion

        private void AplicarMaximizadoManual()
        {
            if (!_esFalsoMaximizado)
                _tamanoNormal = new Rect(this.Left, this.Top, this.Width, this.Height);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            var areaTrabajo = WindowHelper.ObtenerAreaTrabajo(this);

            this.Left = areaTrabajo.Left;
            this.Top = areaTrabajo.Top;
            this.Width = areaTrabajo.Width;
            this.Height = areaTrabajo.Height;

            _esFalsoMaximizado = true;
            btnMaximizar.Content = "❐";
        }
        public MainWindow()
        {
            InitializeComponent();
            InicializarLauncher();
            IniciarDiscordRPC();
            this.StateChanged += Window_StateChanged;
        }

        private void InicializarLauncher()
        {
            Core.CambiarIdioma(Core.IdiomaActual);
            SeleccionarIdiomaEnCombo();

            ComprobarActualizaciones();
            TxtVersion.Text = $"v{VERSION_ACTUAL}";
            if (TxtVersionAjustes != null)
            {
                TxtVersionAjustes.Text = $"TecniLauncher v{VERSION_ACTUAL} - Desarrollado por Johan";
            }
            CargarNoticiasGitHub();

            _timerNoticias = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timerNoticias.Tick += (s, e) => BtnSiguiente_Click(null, null);
            _timerNoticias.Start();

            this.Loaded += MainWindow_Loaded;

            this.Loaded += (s, e) => _ = CargarVersionesVanilla();

            ActualizarListaPerfiles();

            txtOfflineName.Text = Core.UltimoNombreOffline;
            txtUsuario.Text = Core.UltimoNombreOffline;
            chkSnapshots.IsChecked = Core.MostrarSnapshots;
        }
        void IniciarDiscordRPC()
        {
            client = new DiscordRpcClient("1495130192572580012");

            client.Initialize();

            client.SetPresence(new RichPresence()
            {
                Details = "En el Menú Principal",
                State = $"V{VERSION_ACTUAL}",
                Assets = new Assets()
                {
                    LargeImageKey = "tecnilogo",
                    LargeImageText = "TecniLauncher"
                },
                Timestamps = Timestamps.Now
            });
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (client != null)
            {
                client.Dispose();
            }
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarVersionesVanilla();
            ActualizarListaPerfiles();
            Core.CargarConfiguracion();
            ConfigurarRamAutomatica();

            txtUsuario.Text = "Verificando sesión...";

            var sesion = await AuthService.AutoLoginMicrosoftAsync(Core.RutaSesion);

            if (sesion != null)
            {
                LoguearUsuarioPremium(sesion);
            }
            else
            {
                var sesionTS = AuthService.CargarSesionTecni();

                if (sesionTS != null)
                {
                    Core.EsTecniStudio = true;
                    this.esPremium = false;

                    Core.SesionUsuario = sesionTS;

                    txtUsuario.Text = sesionTS.Username;
                    txtOfflineName.Text = sesionTS.Username;

                    CargarSkinEnInterfaz(sesionTS.Username);

                    GridLogin.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Core.EsTecniStudio = false;
                    this.esPremium = false;

                    txtUsuario.Text = string.IsNullOrEmpty(Core.UltimoNombreOffline) ? "Sin sesión" : Core.UltimoNombreOffline;
                    CargarSkinEnInterfaz(Core.UltimoNombreOffline);
                }
            }

            this.StateChanged += (s, e_state) => {
                btnMaximizar.Content = (this.WindowState == WindowState.Maximized) ? "❐" : "☐";
            };
        }
        #region Navegacion
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            AbrirLink("https://discord.gg/U8E2tV3WMG");
        }

        private void BtnGithub_Click(object sender, RoutedEventArgs e)
        {
            AbrirLink("https://github.com/johan12390785/TecniLauncher-Data");
        }

        private void AbrirLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("No se pudo abrir el enlace: " + ex.Message);
            }
        }
        private void MoverVentana_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_esFalsoMaximizado)
                {
                    RestaurarManual();
                }
                this.DragMove();
            }
        }
        private void Cerrar_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;

                if (!_esFalsoMaximizado)
                {
                    AplicarMaximizadoManual();
                }
            }
        }

        private void Maximizar_Click(object sender, RoutedEventArgs e)
        {
            if (_esFalsoMaximizado)
                RestaurarManual();
            else
                AplicarMaximizadoManual();
        }

        private void RestaurarManual()
        {
            this.Left = _tamanoNormal.Left;
            this.Top = _tamanoNormal.Top;
            this.Width = _tamanoNormal.Width;
            this.Height = _tamanoNormal.Height;

            _esFalsoMaximizado = false;
            btnMaximizar.Content = "☐";
        }
        private void Minimizar_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void AbrirLogin_Click(object sender, RoutedEventArgs e) => GridLogin.Visibility = Visibility.Visible;
        private void BtnCerrarLogin_Click(object sender, RoutedEventArgs e) => GridLogin.Visibility = Visibility.Collapsed;

        private void MenuJugar_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(VistaJugar, VistaJugar, VistaPerfiles, VistaAjustes, VistaMods, vistaTecniClients, VistaModpacks);
            CargarNoticiasGitHub();
        }
        private void MenuPerfiles_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(VistaPerfiles, VistaJugar, VistaPerfiles, VistaAjustes, VistaMods, vistaTecniClients, VistaModpacks);
        }
        private void MenuMods_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(VistaMods, VistaJugar, VistaPerfiles,
                                     VistaAjustes, VistaMods, vistaTecniClients, VistaModpacks);

            comboPerfilesMods.ItemsSource = null;
            comboPerfilesMods.ItemsSource = Core.Perfiles;

            if (comboPerfilesMods.Items.Count > 0)
                comboPerfilesMods.SelectedIndex = 0;

            BtnBuscarOnline_Click(null, null);

        }
        private void MenuTecniClients_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(vistaTecniClients, VistaJugar, VistaPerfiles, VistaAjustes, VistaMods, VistaModpacks);
            CargarClientesDesdeInternet();
        }
        private void MenuModpacks_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(VistaModpacks, VistaJugar, VistaPerfiles, VistaAjustes, VistaMods, vistaTecniClients, VistaModpacks);
        }

        private void MenuAjustes_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.Navegar(VistaAjustes, VistaJugar, VistaPerfiles, VistaAjustes, VistaMods, vistaTecniClients, VistaModpacks);
            chkSnapshots.IsChecked = Core.MostrarSnapshots;
            chkFullscreen.IsChecked = Core.PantallaCompleta;
            txtResAncho.Text = Core.JuegoAncho.ToString();
            txtResAlto.Text = Core.JuegoAlto.ToString();
            bool esPantallaCompleta = Core.PantallaCompleta;
            txtResAncho.IsEnabled = !esPantallaCompleta;
            txtResAlto.IsEnabled = !esPantallaCompleta;

            if (!string.IsNullOrEmpty(txtUsuario.Text))
                CargarSkinEnInterfaz(txtUsuario.Text);
        }
        #endregion
        #region LoginM
        private async void BtnMicrosoft_Click(object sender, RoutedEventArgs e)
        {
            if (esPremium)
            {
                AuthService.CerrarSesionMicrosoft(Core.RutaSesion);

                esPremium = false;
                Core.SesionUsuario = null;

                btnMicrosoft.Content = "Iniciar con Microsoft";
                btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(0, 93, 166));
                txtOfflineName.IsEnabled = true;
                txtOfflineName.Text = "Jugador";
                txtUsuario.Text = "Sin Sesión";
                CargarSkinEnInterfaz(null);
                VentanaMensaje.Mostrar("Sesión cerrada.");
            }
            else
            {
                try
                {
                    btnMicrosoft.IsEnabled = false;
                    btnMicrosoft.Content = "Iniciando...";

                    var resultado = await AuthService.LoginMicrosoftAsync(Core.RutaSesion);

                    if (resultado != null)
                    {
                        LoguearUsuarioPremium(resultado);
                        GridLogin.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Login cancelado o fallido: " + ex.Message);
                    btnMicrosoft.Content = "Iniciar con Microsoft";
                }
                finally { btnMicrosoft.IsEnabled = true; }
            }
        }

        private void LoguearUsuarioPremium(MSession sesion)
        {
            Core.SesionUsuario = sesion;
            esPremium = true;

            txtUsuario.Text = sesion.Username;
            txtOfflineName.Text = sesion.Username;
            txtOfflineName.IsEnabled = false;

            btnMicrosoft.Content = "Cerrar Sesión";
            btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));

            CargarSkinEnInterfaz(sesion.UUID);
        }

        private void BtnLoginTecni_Click(object sender, RoutedEventArgs e)
        {
            PanelLoginPrincipal.Visibility = Visibility.Collapsed;
            PanelLoginTecni.Visibility = Visibility.Visible;

            txtUsuarioTecni.Text = "";
            txtPasswordTecni.Password = "";
            txtEstadoTecni.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelarTecni_Click(object sender, RoutedEventArgs e)
        {
            PanelLoginTecni.Visibility = Visibility.Collapsed;
            PanelLoginPrincipal.Visibility = Visibility.Visible;
        }
        private void BtnEntrarLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtOfflineName.Text)) return;
            Core.UltimoNombreOffline = txtOfflineName.Text;
            Core.SesionUsuario = null;
            Core.EsTecniStudio = false;
            Core.GuardarConfiguracion();
            txtUsuario.Text = txtOfflineName.Text;
            CargarSkinEnInterfaz(txtOfflineName.Text);
            GridLogin.Visibility = Visibility.Collapsed;
        }

        private async void BtnLoginTecniStudio_Click(object sender, RoutedEventArgs e)
        {
            string usuario = txtUsuarioTecni.Text.Trim();
            string password = txtPasswordTecni.Password;

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(password))
            {
                txtEstadoTecni.Text = "Por favor, llena todos los campos.";
                txtEstadoTecni.Visibility = Visibility.Visible;
                return;
            }

            txtEstadoTecni.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4facfe"));
            txtEstadoTecni.Text = "Conectando con TecniStudio...";
            txtEstadoTecni.Visibility = Visibility.Visible;
            btnLoginTecni.IsEnabled = false;

            try
            {
                var sesionTecni = await AuthService.LoginTecniStudioAsync(usuario, password);

                if (sesionTecni != null)
                {
                    Core.SesionUsuario = sesionTecni;
                    Core.EsTecniStudio = true;
                    Core.UltimoNombreOffline = sesionTecni.Username;

                    AuthService.GuardarSesionTecni(sesionTecni);

                    Core.GuardarConfiguracion();

                    GridLogin.Visibility = Visibility.Collapsed;
                    CargarSkinEnInterfaz(sesionTecni.Username);
                }
                else
                {
                    txtEstadoTecni.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5656"));
                    txtEstadoTecni.Text = "Credenciales incorrectas en TecniStudio.";
                }
            }
            catch (Exception)
            {
                txtEstadoTecni.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5656"));
                txtEstadoTecni.Text = "Error de conexión con la base de datos.";
            }
            finally
            {
                btnLoginTecni.IsEnabled = true;
            }
        }
        #endregion
        #region Skin

        private async void CargarSkinEnInterfaz(string usuario)
        {
            if (string.IsNullOrEmpty(usuario))
            {
                imgAvatar.Fill = new SolidColorBrush(Colors.Gray);
                return;
            }

            try
            {
                BitmapSource skinBitmap = await SkinUtils.ObtenerSkinOnline(usuario, this.esPremium);

                if (skinBitmap == null)
                    skinBitmap = new BitmapImage(new Uri("pack://application:,,,/Resources/steve.png"));

                imgAvatar.Fill = SkinUtils.RecortarParte(skinBitmap, 8, 8, 8, 8);

                try
                {
                    await VisorSkinWebView.EnsureCoreWebView2Async(null);

                    string urlDirectaSkin = await SkinUtils.ObtenerUrlDirecta(usuario, this.esPremium);

                    if (!string.IsNullOrEmpty(urlDirectaSkin))
                    {
                        string urlVisor = $"https://tecnistudio.online/embed-skin.html?url={urlDirectaSkin}";
                        VisorSkinWebView.CoreWebView2.Navigate(urlVisor);
                    }
                }
                catch (Exception exWebView)
                {
                    System.Diagnostics.Debug.WriteLine("Error al cargar el motor 3D: " + exWebView.Message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error visualizando skin: " + ex.Message);
            }
        }

        private void ZonaSkin_Click(object sender, RoutedEventArgs e)
        {
            string urlDestino = Core.EsTecniStudio
                ? "https://tecnistudio.online/minecraft/skin/"
                : "https://ely.by/";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = urlDestino,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al abrir el navegador: " + ex.Message);
            }
        }

        private void ZonaSkin_Drop(object sender, DragEventArgs e) { }

        private void BtnBorrarSkin_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(txtUsuario.Text) && txtUsuario.Text != "Sin sesión")
            {
                VentanaMensaje.Mostrar("Recargando skin desde la nube...", "ACTUALIZANDO", MessageBoxButton.OK);
                CargarSkinEnInterfaz(txtUsuario.Text);
            }
        }
        #endregion
        #region LanzarMine
        private async void BtnJugar_Click(object sender, RoutedEventArgs e)
        {
            var perfil = comboPerfilRapido.SelectedItem as Perfil;
            if (perfil == null)
            {
                VentanaMensaje.Mostrar("Por favor, selecciona un perfil primero.");
                return;
            }
            if (client != null && client.IsInitialized)
            {
                client.SetPresence(new RichPresence()
                {
                    Details = $"Jugando a {perfil.Nombre}",
                    State = $"Versión {perfil.Version}",
                    Assets = new Assets()
                    {
                        LargeImageKey = "tecnilogo",
                        LargeImageText = "TecniLauncher"
                    },
                    Timestamps = Timestamps.Now
                });
            }
            try
            {
                btnJugar.IsEnabled = false;
                PanelCarga.Visibility = Visibility.Visible;

                DateTime tiempoInicio = DateTime.Now;

                System.Diagnostics.Process procesoMinecraft = await LanzarMinecraft(perfil);
                PanelCarga.Visibility = Visibility.Collapsed;

                if (procesoMinecraft != null)
                {
                    if (chkOcultarLauncher.IsChecked == true)
                        this.Hide();
                    else
                        this.WindowState = WindowState.Minimized;

                    procesoMinecraft.EnableRaisingEvents = true;
                    procesoMinecraft.Exited += (s, ev) =>
                    {
                        DateTime tiempoFinal = DateTime.Now;
                        long segundosJugados = (long)(tiempoFinal - tiempoInicio).TotalSeconds;

                        perfil.SegundosJugados += segundosJugados;
                        Core.GuardarPerfiles();

                        Dispatcher.Invoke(() =>
                        {
                            ActualizarListaPerfiles();
                            this.Show();
                            this.WindowState = WindowState.Normal;
                            this.Activate();
                            if (client != null && client.IsInitialized)
                            {
                                client.SetPresence(new RichPresence()
                                {
                                    Details = "En el Menú Principal",
                                    State = $"V{VERSION_ACTUAL}",
                                    Assets = new Assets()
                                    {
                                        LargeImageKey = "tecnilogo",
                                        LargeImageText = "TecniLauncher"
                                    },
                                    Timestamps = Timestamps.Now
                                });
                            }
                        });
                    };
                }
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error al lanzar: " + ex.Message);
            }
            finally
            {
                btnJugar.IsEnabled = true;
                PanelCarga.Visibility = Visibility.Collapsed;
            }
        }
        private async Task<Process> LanzarMinecraft(Perfil perfil)
        {
            try
            {
                txtEstadoCarga.Text = "Iniciando...";
                barraCarga.Value = 0;

                if (!Directory.Exists(perfil.RutaCarpeta))
                    Directory.CreateDirectory(perfil.RutaCarpeta);

                var pathHibrido = new CmlLib.Core.MinecraftPath(perfil.RutaCarpeta);
                string rutaGlobal = Core.RutaGlobal;

                pathHibrido.Assets = System.IO.Path.Combine(rutaGlobal, "assets");
                pathHibrido.Library = System.IO.Path.Combine(rutaGlobal, "libraries");
                pathHibrido.Versions = System.IO.Path.Combine(rutaGlobal, "versions");
                pathHibrido.Runtime = System.IO.Path.Combine(rutaGlobal, "runtime");

                var launcher = new CmlLib.Core.MinecraftLauncher(pathHibrido);

                if (Core.SesionUsuario == null)
                {
                    string nombreFinal = string.IsNullOrEmpty(Core.UltimoNombreOffline) ? "Jugador" : Core.UltimoNombreOffline;
                    string uuidFijo = AuthService.GenerarUuidOffline(nombreFinal);

                    Core.SesionUsuario = new CmlLib.Core.Auth.MSession
                    {
                        Username = nombreFinal,
                        UUID = uuidFijo,
                        AccessToken = "token_offline",
                        ClientToken = uuidFijo,
                        UserType = "Legacy"
                    };
                }

                launcher.FileProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        barraCarga.Maximum = e.TotalTasks;
                        barraCarga.Value = e.ProgressedTasks;
                        txtEstadoCarga.Text = $"Verificando: {e.Name}";
                    });
                };

                string idVersion = await InstalarModLoader(perfil, launcher);

                var argumentosExtra = new System.Collections.Generic.List<CmlLib.Core.ProcessBuilder.MArgument>();

                if (Core.EsTecniStudio && Core.SesionUsuario?.AccessToken == "token_tecnistudio")
                {
                    Dispatcher.Invoke(() => txtEstadoCarga.Text = "Conectando al servidor de skins...");
                    string rutaJar = await PrepararAuthlibInjector(Core.RutaGlobal);

                    if (!string.IsNullOrEmpty(rutaJar))
                    {
                        string urlAPI = "https://kfxffvjakkcjbwkpvxtr.supabase.co/functions/v1/yggdrasil";
                        argumentosExtra.Add(new CmlLib.Core.ProcessBuilder.MArgument($"-javaagent:{rutaJar}={urlAPI}"));
                    }
                }

                if (perfil.TipoLoader == "Vanilla" && perfil.ModoRendimientoActivado)
                {
                    string[] banderasAikar = new string[]
                    {"-XX:+UseG1GC","-XX:+ParallelRefProcEnabled","-XX:MaxGCPauseMillis=200","-XX:+UnlockExperimentalVMOptions","-XX:+DisableExplicitGC","-XX:G1NewSizePercent=30","-XX:G1MaxNewSizePercent=40","-XX:G1HeapRegionSize=8M","-XX:G1ReservePercent=20","-XX:G1HeapWastePercent=5","-XX:G1MixedGCCountTarget=4","-XX:InitiatingHeapOccupancyPercent=15","-XX:G1MixedGCLiveThresholdPercent=90","-XX:G1RSetUpdatingPauseTimePercent=5","-XX:SurvivorRatio=32","-XX:+PerfDisableSharedMem","-XX:MaxTenuringThreshold=1"};

                    foreach (string bandera in banderasAikar)
                    {
                        argumentosExtra.Add(new CmlLib.Core.ProcessBuilder.MArgument(bandera));
                    }

                    Dispatcher.Invoke(() => txtEstadoCarga.Text = "Inyectando optimizaciones de memoria Aikar...");
                }

                var launchOption = new CmlLib.Core.ProcessBuilder.MLaunchOption
                {
                    MaximumRamMb = perfil.MemoriaRam,
                    Session = Core.SesionUsuario,
                    ScreenWidth = Core.JuegoAncho,
                    ScreenHeight = Core.JuegoAlto,
                    FullScreen = Core.PantallaCompleta,
                    ExtraJvmArguments = argumentosExtra
                };

                var process = await launcher.CreateProcessAsync(idVersion, launchOption);

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardError = false; 
                process.StartInfo.RedirectStandardOutput = false;

                StartProcess(process);

                return process;
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error crítico al lanzar: " + ex.Message);
                return null;
            }
        }
        private async Task<string> PrepararAuthlibInjector(string carpetaBase)
        {
            try
            {
                if (!Directory.Exists(carpetaBase))
                {
                    Directory.CreateDirectory(carpetaBase);
                }

                string rutaInjector = System.IO.Path.Combine(carpetaBase, "authlib-injector.jar");

                bool necesitaDescarga = !File.Exists(rutaInjector) || new FileInfo(rutaInjector).Length < 1000;

                if (necesitaDescarga)
                {
                    if (File.Exists(rutaInjector))
                    {
                        File.Delete(rutaInjector);
                    }

                    Dispatcher.Invoke(() => txtEstadoCarga.Text = "Descargando authlib-injector...");
                    string url = "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.7/authlib-injector-1.2.7.jar";

                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher-App");
                        var bytes = await client.GetByteArrayAsync(url);
                        File.WriteAllBytes(rutaInjector, bytes);
                    }
                }
                return rutaInjector;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ERROR FATAL AL DESCARGAR INYECTOR: " + ex.Message);
                return null;
            }
        }
        private async void StartProcess(Process process)
        {
            process.Start();

            await Task.Delay(3000);

            if (chkOcultarLauncher.IsChecked == true)
                this.Hide();
            else

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                });
            };
        }

        private async Task<string> InstalarModLoader(Perfil perfil, MinecraftLauncher launcher)
        {
            if (perfil.TipoLoader == "Vanilla") return perfil.Version;

            Dispatcher.Invoke(() =>
            {
                txtEstadoCarga.Text = $"Instalando {perfil.TipoLoader}...";
                barraCarga.IsIndeterminate = true;
            });

            try
            {
                string verInstalada = null;

                string versionExacta = string.IsNullOrEmpty(perfil.VersionLoaderExacta) ? null : perfil.VersionLoaderExacta;

                // === FABRIC ===
                if (perfil.TipoLoader == "Fabric")
                {
                    var installer = new TecniLauncher.FabricInstaller(launcher);
                    verInstalada = await installer.InstallAsync(perfil.Version, versionExacta);
                }
                // === FORGE ===
                else if (perfil.TipoLoader == "Forge")
                {
                    var installer = new ForgeInstaller(launcher);
                    if (!string.IsNullOrEmpty(versionExacta))
                        verInstalada = await installer.Install(perfil.Version, versionExacta);
                    else
                        verInstalada = await installer.Install(perfil.Version);
                }
                // === NEOFORGE ===
                else if (perfil.TipoLoader == "NeoForge")
                {
                    var installer = new NeoForgeInstaller(launcher);
                    if (!string.IsNullOrEmpty(versionExacta))
                        verInstalada = await installer.Install(perfil.Version, versionExacta);
                    else
                        verInstalada = await installer.Install(perfil.Version);
                }

                Dispatcher.Invoke(() => barraCarga.IsIndeterminate = false);
                return verInstalada;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => barraCarga.IsIndeterminate = false);
                throw new Exception($"Error instalando Loader: {ex.Message}");
            }
        }
        #endregion
        #region PerfilC
        private void ChkSnapshots_Click(object sender, RoutedEventArgs e)
        {
            Core.MostrarSnapshots = chkSnapshots.IsChecked == true;
            Core.GuardarConfiguracion();
            _ = CargarVersionesVanilla();
        }
        private void ActualizarListaPerfiles()
        {
            listaPerfilesUI.ItemsSource = null;
            listaPerfilesUI.ItemsSource = Core.Perfiles;

            comboPerfilRapido.ItemsSource = null;
            comboPerfilRapido.ItemsSource = Core.Perfiles;
            //comboPerfilRapido.DisplayMemberPath = "Nombre";

            if (comboPerfilRapido.Items.Count > 0) comboPerfilRapido.SelectedIndex = 0;
        }
        private async Task CargarVersionesVanilla()
        {
            try
            {
                comboVersiones.IsEnabled = false;
                comboVersiones.Items.Clear();
                comboVersiones.Items.Add("Cargando...");
                comboVersiones.SelectedIndex = 0;

                if (Core.LauncherGlobal == null) Core.Inicializar();

                var versiones = await Core.LauncherGlobal.GetAllVersionsAsync();

                comboVersiones.Items.Clear();
                foreach (var v in versiones)
                {
                    if (Core.MostrarSnapshots || v.Type == "release") comboVersiones.Items.Add(v.Name);
                }
                if (comboVersiones.Items.Count > 0) comboVersiones.SelectedIndex = 0;
            }
            catch { comboVersiones.Items.Add("Error"); }
            finally { comboVersiones.IsEnabled = true; }
        }
        private async void ComboLoader_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboLoader == null || comboLoader.SelectedItem == null) return;
            if (comboVersiones == null || comboVersiones.SelectedItem == null) return;

            var itemLoader = (ComboBoxItem)comboLoader.SelectedItem;
            string tipoLoader = itemLoader.Content.ToString();
            string versionMinecraft = comboVersiones.SelectedItem.ToString();

            if (tipoLoader == "Vanilla")
            {
                lblLoaderVersion.Visibility = Visibility.Collapsed;
                comboLoaderVersion.Visibility = Visibility.Collapsed;
                return;
            }
            if (chkModoRendimiento != null)
            {
                if (tipoLoader == "Vanilla")
                {
                    chkModoRendimiento.Visibility = Visibility.Visible;
                }
                else
                {
                    chkModoRendimiento.Visibility = Visibility.Collapsed;
                    chkModoRendimiento.IsChecked = false;
                }
            }

            lblLoaderVersion.Visibility = Visibility.Visible;
            comboLoaderVersion.Visibility = Visibility.Visible;
            comboLoaderVersion.IsEnabled = false;
            comboLoaderVersion.ItemsSource = new List<string> { "Buscando..." };
            comboLoaderVersion.SelectedIndex = 0;

            try
            {
                List<string> versionesEncontradas = new List<string>();

                // === LÓGICA FABRIC ===
                if (tipoLoader == "Fabric")
                {
                    var instaladorPropio = new TecniLauncher.FabricInstaller(Core.LauncherGlobal);

                    var listaFabric = await instaladorPropio.ObtenerVersiones(versionMinecraft);
                    versionesEncontradas.AddRange(listaFabric);
                }
                // === LÓGICA FORGE ===
                else if (tipoLoader == "Forge")
                {
                    var instalador = new CmlLib.Core.Installer.Forge.ForgeInstaller(Core.LauncherGlobal);
                    var datos = await instalador.GetForgeVersions(versionMinecraft);

                    foreach (var v in datos)
                    {
                        versionesEncontradas.Add(v.ForgeVersionName);
                    }
                }

                // === LÓGICA NEOFORGE ===
                else if (tipoLoader == "NeoForge")
                {
                    var instalador = new CmlLib.Core.Installer.NeoForge.NeoForgeInstaller(Core.LauncherGlobal);

                    var datos = await instalador.GetForgeVersions(versionMinecraft);

                    foreach (var v in datos)
                    {
                        versionesEncontradas.Insert(0, v.VersionName);
                    }
                }
                if (versionesEncontradas.Count > 0)
                {
                    comboLoaderVersion.ItemsSource = versionesEncontradas;
                    comboLoaderVersion.SelectedIndex = 0;
                    comboLoaderVersion.IsEnabled = true;
                }
                else
                {
                    comboLoaderVersion.ItemsSource = new List<string> { "No disponible" };
                }
            }
            catch
            {
                comboLoaderVersion.ItemsSource = new List<string> { "Error al buscar" };
            }
        }
        private void SliderRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblRam == null) return;
            lblRam.Text = $"{e.NewValue} GB";

        }



        private void ComboVersiones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboLoader_SelectionChanged(null, null);
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetPhysicallyInstalledSystemMemory(out long TotalMemoryInKilobytes);

        private void ConfigurarRamAutomatica()
        {
            long memKb;
            GetPhysicallyInstalledSystemMemory(out memKb);

            int ramTotalGB = (int)Math.Ceiling(memKb / 1024.0 / 1024.0);

            sliderRam.Minimum = 1;
            sliderRam.Maximum = ramTotalGB;

            if (sliderRam.Value == 0 || sliderRam.Value > ramTotalGB)
            {
                sliderRam.Value = ramTotalGB > 8 ? 4 : ramTotalGB / 2;
            }
            lblRam.Text = $"{sliderRam.Value} GB";
        }
        private void BtnCancelarPerfil_Click(object sender, RoutedEventArgs e) => GridCrearPerfil.Visibility = Visibility.Collapsed;

        private void BtnGuardarPerfil_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNombrePerfil.Text.Trim();
            if (string.IsNullOrEmpty(nombre)) { VentanaMensaje.Mostrar("Escribe un nombre"); return; }

            string iconoFinal = "/Resources/Icons/icon1.png";
            if (perfilAEditar != null)
            {
                iconoFinal = perfilAEditar.IconoPath;
            }
            if (listaIconos.SelectedIndex != -1 && listaIconos.SelectedItem is ListBoxItem seleccionado)
            {
                iconoFinal = $"/Resources/Icons/{seleccionado.Tag}";
            }

            Perfil duplicado = Core.Perfiles.Find(p => p.Nombre == nombre);
            if (perfilAEditar == null && duplicado != null) { VentanaMensaje.Mostrar("Ya existe ese nombre"); return; }
            if (perfilAEditar != null && duplicado != null && duplicado != perfilAEditar) { VentanaMensaje.Mostrar("Nombre ocupado"); return; }

            int ram = (int)sliderRam.Value * 1024;
            bool activarRendimiento = chkModoRendimiento?.IsChecked == true;
            string versionExactaCapturada = "";

            if (comboLoader.SelectedIndex > 0 && comboLoaderVersion.SelectedItem != null)
            {
                string seleccion = comboLoaderVersion.SelectedItem.ToString();
                if (seleccion != "Buscando..." && seleccion != "No disponible" && seleccion != "Error")
                {
                    versionExactaCapturada = seleccion;
                }
            }

            if (perfilAEditar == null) // CREAR NUEVO
            {
                if (comboVersiones.SelectedItem == null) return;
                string ver = comboVersiones.SelectedItem.ToString();
                string loader = ((ComboBoxItem)comboLoader.SelectedItem).Content.ToString();
                var nuevoPerfil = new Perfil(nombre, ver, loader, ram);
                nuevoPerfil.VersionLoaderExacta = versionExactaCapturada;
                nuevoPerfil.IconoPath = iconoFinal;
                nuevoPerfil.ModoRendimientoActivado = activarRendimiento;

                Core.Perfiles.Add(nuevoPerfil);
            }
            else // EDITAR
            {
                if (perfilAEditar.Nombre != nombre)
                {
                    try
                    {
                        string oldPath = perfilAEditar.RutaCarpeta;
                        string newPath = Path.Combine(Path.GetDirectoryName(oldPath), nombre);
                        if (Directory.Exists(oldPath)) Directory.Move(oldPath, newPath);
                        perfilAEditar.RutaCarpeta = newPath;
                    }
                    catch { VentanaMensaje.Mostrar("No se pudo renombrar carpeta."); return; }
                }
                perfilAEditar.Nombre = nombre;
                perfilAEditar.MemoriaRam = ram;
                perfilAEditar.IconoPath = iconoFinal;
                perfilAEditar.ModoRendimientoActivado = activarRendimiento;
            }
            Core.GuardarPerfiles();
            ActualizarListaPerfiles();
            GridCrearPerfil.Visibility = Visibility.Collapsed;
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            perfilAEditar = (Perfil)btn.Tag;

            txtNombrePerfil.Text = perfilAEditar.Nombre;
            sliderRam.Value = perfilAEditar.MemoriaRam / 1024;
            if (chkModoRendimiento != null)
            {
                chkModoRendimiento.IsChecked = perfilAEditar.ModoRendimientoActivado;

                if (perfilAEditar.TipoLoader == "Vanilla")
                {
                    chkModoRendimiento.Visibility = Visibility.Visible;
                }
                else
                {
                    chkModoRendimiento.Visibility = Visibility.Collapsed;
                }
            }
            comboVersiones.IsEnabled = false;
            comboLoader.IsEnabled = false;
            comboLoaderVersion.IsEnabled = false;
            comboVersiones.Items.Clear();
            comboVersiones.Items.Add(perfilAEditar.Version);
            comboVersiones.SelectedIndex = 0;

            if (!string.IsNullOrEmpty(perfilAEditar.VersionLoaderExacta))
            {
                comboLoaderVersion.ItemsSource = new List<string> { perfilAEditar.VersionLoaderExacta };
                comboLoaderVersion.SelectedIndex = 0;
            }
            else
            {
                comboLoaderVersion.ItemsSource = new List<string> { "Automático / Default" };
                comboLoaderVersion.SelectedIndex = 0;
            }
            listaIconos.SelectedIndex = -1;

            foreach (ListBoxItem item in listaIconos.Items)
            {
                if ($"/Resources/Icons/{item.Tag}" == perfilAEditar.IconoPath)
                {
                    listaIconos.SelectedItem = item;
                    break;
                }
            }

            GridCrearPerfil.Visibility = Visibility.Visible;
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var perfil = (Perfil)((System.Windows.Controls.Button)sender).Tag;
            if (VentanaMensaje.Mostrar($"¿Eliminar {perfil.Nombre}?", "Confirmar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (Directory.Exists(perfil.RutaCarpeta)) try { Directory.Delete(perfil.RutaCarpeta, true); } catch { }
                Core.Perfiles.Remove(perfil);
                Core.GuardarPerfiles();
                ActualizarListaPerfiles();
            }
        }

        private void BtnCarpeta_Click(object sender, RoutedEventArgs e)
        {
            var perfil = (Perfil)((System.Windows.Controls.Button)sender).Tag;
            if (!Directory.Exists(perfil.RutaCarpeta)) Directory.CreateDirectory(perfil.RutaCarpeta);
            Process.Start("explorer.exe", perfil.RutaCarpeta);
        }
        private void AbrirCrearPerfil_Click(object sender, RoutedEventArgs e)
        {
            perfilAEditar = null;
            txtNombrePerfil.Text = "";
            sliderRam.Value = 2;
            if (chkModoRendimiento != null)
            {
                chkModoRendimiento.IsChecked = false;
                chkModoRendimiento.Visibility = Visibility.Visible;
            }
            comboVersiones.IsEnabled = true;
            comboLoader.IsEnabled = true;
            comboLoader.SelectedIndex = 0;
            GridCrearPerfil.Visibility = Visibility.Visible;
            if (comboVersiones.Items.Count == 0 || comboVersiones.Items[0].ToString() == "Cargando...") _ = CargarVersionesVanilla();
        }
        #endregion
        #region Mods
        private void ComboPerfilesMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                txtModVersion.Text = p.Version;
                txtModLoader.Text = p.TipoLoader;

                if (modoOnline)
                    BtnBuscarOnline_Click(null, null);
                else
                    CargarModsLocal(p);
            }
        }

        private void TabOnlineBtn_Checked(object sender, RoutedEventArgs e)
        {
            modoOnline = true;

            if (PanelOnline == null || PanelInstalados == null) return;

            PanelOnline.Visibility = Visibility.Visible;
            PanelInstalados.Visibility = Visibility.Collapsed;

            listaModsGestor.ItemsSource = null;

            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                BtnBuscarOnline_Click(null, null);
            }
        }

        private void TabInstaladosBtn_Checked(object sender, RoutedEventArgs e)
        {
            modoOnline = false;

            if (PanelOnline == null || PanelInstalados == null) return;

            PanelOnline.Visibility = Visibility.Collapsed;
            PanelInstalados.Visibility = Visibility.Visible;

            if (comboPerfilesMods.SelectedItem is Perfil p)
                CargarModsLocal(p);
        }

        private async void BtnBuscarOnline_Click(object sender, RoutedEventArgs e)
        {
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            _offsetMods = 0;
            _hayMasResultados = true;
            _listaModsActual.Clear();
            listaModsGestor.ItemsSource = _listaModsActual;
            ScrollModsOnline.ScrollToTop();

            await FetchYAgregarMods(p, esPrimeraCarga: true);
        }
        private async Task FetchYAgregarMods(Perfil p, bool esPrimeraCarga = false)
        {
            if (!await _semMods.WaitAsync(0)) return;

            try
            {
                if (!_hayMasResultados) return;

                string busqueda = txtBuscadorMods?.Text ?? "";
                int limitePaginacion = 30;

                var res = await ModrinthAPI.BuscarMods(busqueda, p.TipoLoader, p.Version, _offsetMods, limitePaginacion);

                if (res == null || res.Count == 0)
                {
                    _hayMasResultados = false;
                    return;
                }

                string carpetaMods = Path.Combine(p.RutaCarpeta, "mods");
                var archivosLocales = Directory.Exists(carpetaMods)
                    ? new DirectoryInfo(carpetaMods).GetFiles("*.jar")
                        .Select(f => f.Name.ToLower()).ToList()
                    : new List<string>();

                foreach (var m in res)
                {
                    if (!_listaModsActual.Any(x => x.project_id == m.project_id))
                    {
                        string titulo = m.title.ToLower();

                        m.esRecomendado = titulo.Contains("sodium") ||
                                          titulo.Contains("iris") ||
                                          titulo.Contains("lithium");

                        if (string.IsNullOrEmpty(m.icon_url))
                            m.icon_url = "https://cdn.modrinth.com/assets/icon.png";

                        string palabraClave = titulo.Split(' ')[0];
                        m.estaInstalado = archivosLocales.Any(f => f.Contains(palabraClave));

                        _listaModsActual.Add(m);
                    }
                }

                _offsetMods += res.Count;

                if (res.Count < limitePaginacion) _hayMasResultados = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error cargando mods: " + ex.Message);
            }
            finally
            {
                _semMods.Release();
            }
        }
        private async void ListaMods_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange <= 0) return;
            if (!_hayMasResultados) return;

            bool cercaDelFinal = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 200;
            if (!cercaDelFinal) return;

            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            await FetchYAgregarMods(p);
        }
        private async void BtnAccionMod_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ModInfo mod) return;

                if (!modoOnline)
                {
                    if (File.Exists(mod.project_id))
                    {
                        File.Delete(mod.project_id);
                        VentanaMensaje.Mostrar("Mod eliminado: " + mod.title);
                        if (comboPerfilesMods.SelectedItem is Perfil p) CargarModsLocal(p);
                    }
                    return;
                }

                if (comboPerfilesMods.SelectedItem is Perfil perfilActual)
                {
                    System.Windows.Controls.Panel.SetZIndex(OverlayVersiones, 999);
                    OverlayVersiones.Visibility = Visibility.Visible;
                    listaArchivosVersion.ItemsSource = null;
                    txtNombreModVersiones.Text = $"Buscando versiones de {mod.title}...";

                    var versiones = await ModrinthAPI.ObtenerListaVersiones(mod.project_id, perfilActual.Version, perfilActual.TipoLoader);

                    if (versiones != null && versiones.Count > 0)
                    {
                        txtNombreModVersiones.Text = mod.title;
                        listaArchivosVersion.ItemsSource = versiones;
                    }
                    else
                    {
                        txtNombreModVersiones.Text = "Sin versiones compatibles.";
                    }
                }
                else
                {
                    VentanaMensaje.Mostrar("Selecciona un perfil primero.");
                }
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error de conexión al buscar versiones: " + ex.Message);
                OverlayVersiones.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnInstalarVersion_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            var versionPrincipal = (ModVersion)btn.Tag;

            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                try
                {
                    string carpetaMods = Path.Combine(p.RutaCarpeta, "mods");
                    if (!Directory.Exists(carpetaMods)) Directory.CreateDirectory(carpetaMods);

                    VentanaMensaje.Mostrar($"Instalando {versionPrincipal.NombreArchivo} y sus dependencias (esto puede tardar)...");
                    OverlayVersiones.Visibility = Visibility.Collapsed;

                    await DescargarModYDependencias(versionPrincipal, p.Version, p.TipoLoader, carpetaMods);

                    VentanaMensaje.Mostrar("¡Mod y dependencias instalados correctamente!");

                    if (!modoOnline) CargarModsLocal(p);
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Error en la descarga: " + ex.Message);
                    OverlayVersiones.Visibility = Visibility.Visible;
                }
            }
        }
        private async Task DescargarModYDependencias(ModVersion versionBase, string versionMC, string loader, string carpetaMods)
        {
            HashSet<string> idsProcesados = new HashSet<string>();

            Queue<ModVersion> colaDescargas = new Queue<ModVersion>();

            colaDescargas.Enqueue(versionBase);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher");

                while (colaDescargas.Count > 0)
                {
                    var modActual = colaDescargas.Dequeue();
                    string destino = Path.Combine(carpetaMods, modActual.NombreArchivo);

                    if (!File.Exists(destino))
                    {
                        System.Diagnostics.Debug.WriteLine($"Descargando: {modActual.NombreArchivo}");
                        var datos = await client.GetByteArrayAsync(modActual.UrlDescarga);
                        File.WriteAllBytes(destino, datos);
                    }

                    foreach (var depProjectId in modActual.DependenciasRequeridas)
                    {
                        if (!idsProcesados.Contains(depProjectId))
                        {
                            idsProcesados.Add(depProjectId);

                            var versionesDep = await ModrinthAPI.ObtenerListaVersiones(depProjectId, versionMC, loader);

                            if (versionesDep != null && versionesDep.Count > 0)
                            {
                                colaDescargas.Enqueue(versionesDep[0]);
                            }
                        }
                    }
                }
            }
        }

        private void BtnCerrarVersiones_Click(object sender, RoutedEventArgs e)
        {
            OverlayVersiones.Visibility = Visibility.Collapsed;
        }

        private void CargarModsLocal(Perfil p)
        {
            try
            {
                string path = Path.Combine(p.RutaCarpeta, "mods");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                var list = new List<ModInfo>();
                foreach (var f in new DirectoryInfo(path).GetFiles("*.jar"))
                {
                    list.Add(new ModInfo
                    {
                        title = f.Name,
                        description = $"Archivo local - {f.Length / 1024} KB",
                        author = "Mod Instalado",
                        project_id = f.FullName,
                        icon_url = "https://cdn-icons-png.flaticon.com/512/337/337941.png"
                    });
                }
                listaModsInstaladosGestor.ItemsSource = list;
            }
            catch { }
        }
        private void listaMods_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                var parent = ((Control)sender).Parent as UIElement;
                parent?.RaiseEvent(eventArg);
            }
        }
        #endregion
        #region ModPacks
        private async Task FetchYAgregarModpacks(string busqueda, bool esPrimeraCarga = false)
        {
            if (!await _semModpacks.WaitAsync(0)) return;

            try
            {
                if (!_hayMasModpacks) return;

                if (esPrimeraCarga)
                {
                    txtCargandoModpacks.Visibility = Visibility.Visible;
                    _offsetModpacks = 0;
                    _hayMasModpacks = true;
                    _listaModpacksActual.Clear();
                    listaModpacksUI.ItemsSource = _listaModpacksActual;
                }

                int limitePaginacion = 20;
                var resultados = await ModpacksApi.BuscarModpacksAsync(busqueda, _offsetModpacks, limitePaginacion);

                if (resultados == null || resultados.Count == 0)
                {
                    _hayMasModpacks = false;
                    return;
                }

                foreach (var mp in resultados)
                {
                    if (!_listaModpacksActual.Any(x => x.Id == mp.Id))
                    {
                        _listaModpacksActual.Add(mp);
                    }
                }

                _offsetModpacks += resultados.Count;

                if (resultados.Count < limitePaginacion) _hayMasModpacks = false;

            }
            finally
            {
                txtCargandoModpacks.Visibility = Visibility.Collapsed;
                btnBuscarModpacks.IsEnabled = true;
                _semModpacks.Release();
            }
        }

        private async void BtnBuscarModpacks_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string busqueda = txtBuscadorModpacks.Text.Trim();
            btnBuscarModpacks.IsEnabled = false;

            ScrollModpacks.ScrollToTop();

            await FetchYAgregarModpacks(busqueda, esPrimeraCarga: true);
        }
        private void TxtBuscadorModpacks_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                BtnBuscarModpacks_Click(sender, null);
            }
        }
        private async void BtnVerModpack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var modpackSeleccionado = btn.Tag as ModpackProject;

            btnInstalarModpack.Content = "INSTALAR";
            btnInstalarModpack.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27ae60"));
            btnInstalarModpack.IsEnabled = true;

            if (modpackSeleccionado != null)
            {
                txtDetalleTitulo.Text = modpackSeleccionado.Titulo;
                txtDetalleAutor.Text = $"Por {modpackSeleccionado.Autor}";

                await wvDetalleDescripcion.EnsureCoreWebView2Async(null);
                wvDetalleDescripcion.NavigateToString("<body style='background-color:#151515; color:#AAAAAA; font-family:sans-serif; text-align:center; padding-top:20px;'>Cargando toda la información...</body>");

                try
                {
                    if (!string.IsNullOrEmpty(modpackSeleccionado.IconoUrl))
                        imgDetalleModpack.ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(modpackSeleccionado.IconoUrl));
                    else
                        imgDetalleModpack.ImageSource = null;
                }
                catch { imgDetalleModpack.ImageSource = null; }

                PanelBusquedaModpacks.Visibility = System.Windows.Visibility.Collapsed;
                PanelDetalleModpack.Visibility = System.Windows.Visibility.Visible;

                string descripcionGigante = await ModpacksApi.ObtenerDescripcionCompletaAsync(modpackSeleccionado.Id);

                await wvDetalleDescripcion.EnsureCoreWebView2Async(null);

                var pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                string htmlCuerpo = Markdig.Markdown.ToHtml(descripcionGigante ?? "", pipeline);

                string htmlCompleto = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ 
            background-color: transparent;
            color: #F0F0F0; 
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; 
            padding: 20px; 
            font-size: 15px;
            line-height: 1.6;
        }}
        a {{ color: #4facfe; text-decoration: none; font-weight: bold; }}
        a:hover {{ text-decoration: underline; }}
        img {{ max-width: 100%; border-radius: 8px; margin-top: 10px; }}
        iframe {{ max-width: 100%; border-radius: 8px; margin-top: 10px; }}
        pre, code {{ background-color: #222; padding: 5px; border-radius: 5px; font-family: Consolas, monospace; }}
        h1, h2, h3 {{ border-bottom: 1px solid #333; padding-bottom: 5px; }}
        ::-webkit-scrollbar {{ width: 10px; }}
        ::-webkit-scrollbar-track {{ background: #151515; }}
        ::-webkit-scrollbar-thumb {{ background: #333; border-radius: 5px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: #555; }}
    </style>
</head>
<body>
    {htmlCuerpo}
</body>
</html>";

                wvDetalleDescripcion.NavigateToString(htmlCompleto);

                comboVersionesModpack.ItemsSource = null;
                var versiones = await ModpacksApi.ObtenerVersionesAsync(modpackSeleccionado.Id);

                comboVersionesModpack.ItemsSource = versiones;

                if (versiones.Count > 0) comboVersionesModpack.SelectedIndex = 0;

                btnInstalarModpack.Tag = modpackSeleccionado;
            }
        }

        private void BtnVolverModpacks_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            PanelDetalleModpack.Visibility = System.Windows.Visibility.Collapsed;
            PanelBusquedaModpacks.Visibility = System.Windows.Visibility.Visible;
        }
        private async void BtnInstalarModpack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var versionSeleccionada = comboVersionesModpack.SelectedItem as ModpackVersion;
            var modpackSeleccionado = btnInstalarModpack.Tag as ModpackProject;

            if (versionSeleccionada != null && modpackSeleccionado != null)
            {
                btnInstalarModpack.IsEnabled = false;
                btnInstalarModpack.Content = "PREPARANDO...";
                PanelProgresoDescarga.Visibility = System.Windows.Visibility.Visible;
                txtEstadoDescarga.Text = "Extrayendo configuraciones del Modpack...";
                barraDescarga.Value = 0;

                var archivoZip = versionSeleccionada.Archivos.Find(a => a.EsPrimario) ?? versionSeleccionada.Archivos[0];
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string nombreCarpeta = modpackSeleccionado.Titulo;

                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    nombreCarpeta = nombreCarpeta.Replace(c, '_');
                }

                string rutaBaseMinecraft = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances", nombreCarpeta);

                var receta = await ModpacksApi.PrepararInstalacionModpackAsync(archivoZip.UrlDescarga, rutaBaseMinecraft);

                if (receta != null && receta.ArchivosMod != null)
                {
 
                    barraDescarga.Maximum = receta.ArchivosMod.Count;
                    int descargados = 0;

                    using (var clienteWeb = new System.Net.Http.HttpClient())
                    {
                        foreach (var modFaltante in receta.ArchivosMod)
                        {
                            try
                            {
                                string urlDescarga = modFaltante.UrlsDescarga[0];

                                string rutaDestinoFinal = System.IO.Path.Combine(rutaBaseMinecraft, modFaltante.RutaDestino);

                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(rutaDestinoFinal));

                                txtEstadoDescarga.Text = $"Descargando ({descargados}/{receta.ArchivosMod.Count}): {System.IO.Path.GetFileName(rutaDestinoFinal)}";

                                byte[] bytesMod = await clienteWeb.GetByteArrayAsync(urlDescarga);
                                System.IO.File.WriteAllBytes(rutaDestinoFinal, bytesMod);
                            }
                            catch (Exception ex)
                            {
                                VentanaMensaje.Mostrar($"Falló la descarga de un mod: {ex.Message}");
                            }

                            descargados++;
                            barraDescarga.Value = descargados;
                        }
                    }

                    txtEstadoDescarga.Text = "¡Instalación Completada!";

                    string loaderUsado = "Vanilla";
                    string versionDelLoader = "";

                    if (receta.Dependencias != null)
                    {
                        if (receta.Dependencias.ContainsKey("fabric-loader"))
                        {
                            loaderUsado = "Fabric";
                            versionDelLoader = receta.Dependencias["fabric-loader"];
                        }
                        else if (receta.Dependencias.ContainsKey("forge"))
                        {
                            loaderUsado = "Forge";
                            versionDelLoader = receta.Dependencias["forge"];
                        }
                        else if (receta.Dependencias.ContainsKey("neoforge"))
                        {
                            loaderUsado = "NeoForge";
                            versionDelLoader = receta.Dependencias["neoforge"];
                        }
                    }

                    Perfil nuevoPerfil = new Perfil
                    {
                        Nombre = modpackSeleccionado.Titulo,
                        Version = versionSeleccionada.VersionesMinecraft[0],

                        RutaCarpeta = rutaBaseMinecraft,

                        TipoLoader = loaderUsado,
                        VersionLoaderExacta = versionDelLoader,

                        IconoPath = modpackSeleccionado.IconoUrl
                    };

                    Core.Perfiles.Add(nuevoPerfil);

                    ActualizarListaPerfiles();

                    VentanaMensaje.Mostrar($"¡El modpack '{modpackSeleccionado.Titulo}' se ha instalado correctamente!\nYa puedes seleccionarlo en la pestaña Jugar.", "¡Éxito!");

                    btnInstalarModpack.Content = "INSTALADO";
                    btnInstalarModpack.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D"));
                    btnInstalarModpack.IsEnabled = false;
                }
                else
                {
                    VentanaMensaje.Mostrar("Hubo un error al leer o descomprimir el modpack.", "Error");
                    btnInstalarModpack.Content = "INSTALAR";
                }
            }
        
        }
        private async void VistaModpacks_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (VistaModpacks.Visibility == Visibility.Visible && _listaModpacksActual.Count == 0)
            {
                await FetchYAgregarModpacks("", esPrimeraCarga: true);
            }
        }
        private async void ListaModpacks_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange <= 0) return;
            if (!_hayMasModpacks) return;

            bool cercaDelFinal = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 200;

            if (cercaDelFinal)
            {
                string busqueda = txtBuscadorModpacks.Text.Trim();
                await FetchYAgregarModpacks(busqueda, esPrimeraCarga: false);
            }
        }
        #endregion
        #region AjustesVarios
        private void TxtBuscador_GotFocus(object sender, RoutedEventArgs e) { if (txtBuscadorMods.Text.Contains("Buscar")) txtBuscadorMods.Text = ""; }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            AplicarSnapping();
        }

        private void AplicarSnapping()
        {
            double distanciaIman = 20.0;

            double anchoPantalla = SystemParameters.PrimaryScreenWidth;
            double altoPantalla = SystemParameters.PrimaryScreenHeight;

            if (Math.Abs(this.Left) < distanciaIman)
            {
                this.Left = 0;
            }

            if (Math.Abs(this.Top) < distanciaIman)
            {
                this.Top = 0;
            }

            if (Math.Abs((this.Left + this.Width) - anchoPantalla) < distanciaIman)
            {
                this.Left = anchoPantalla - this.Width;
            }

            if (Math.Abs((this.Top + this.Height) - altoPantalla) < distanciaIman)
            {
                this.Top = altoPantalla - this.Height;
            }
        }
        private void SoloNumeros_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void TxtRes_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtResAncho == null || txtResAlto == null) return;
            if (int.TryParse(txtResAncho.Text, out int w)) Core.JuegoAncho = w;
            if (int.TryParse(txtResAlto.Text, out int h)) Core.JuegoAlto = h;
            Core.GuardarConfiguracion();
        }

        private void ChkFullscreen_Click(object sender, RoutedEventArgs e)
        {
            bool esPantallaCompleta = chkFullscreen.IsChecked == true;

            Core.PantallaCompleta = esPantallaCompleta;
            Core.GuardarConfiguracion();

            if (txtResAncho != null && txtResAlto != null)
            {
                txtResAncho.IsEnabled = !esPantallaCompleta;
                txtResAlto.IsEnabled = !esPantallaCompleta;
            }
        }
        private void ComboIdiomas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            if (comboIdiomas.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string codigo = item.Tag.ToString();

                if (Core.IdiomaActual != codigo)
                {
                    Core.IdiomaActual = codigo;
                    Core.CambiarIdioma(codigo);
                    Core.GuardarConfiguracion();
                }
            }
        }
        private void SeleccionarIdiomaEnCombo()
        {
            if(comboIdiomas == null || comboIdiomas.Items == null)
                return;

            foreach (ComboBoxItem item in comboIdiomas.Items)
            {
                if (item.Tag?.ToString() == Core.IdiomaActual)
                {
                    comboIdiomas.SelectedItem = item;
                    break;
                }
            }
        }
        #endregion
        #region AutoUpdate

        private async void ComprobarActualizaciones()
        {
            try
            {
                var datos = await UpdateService.ObtenerDatosUpdateAsync();

                if (datos.VersionMasReciente == VERSION_ACTUAL) return;

                var res = VentanaMensaje.Mostrar($"¡Nueva versión {datos.VersionMasReciente} disponible!", "ACTUALIZACIÓN", MessageBoxButton.YesNo);

                if (res == MessageBoxResult.Yes)
                {
                    await UpdateService.InstalarDesdeZipAsync(
                        datos.LinkDescarga,
                        datos.Sha256,
                        new Progress<string>(msg => txtEstadoCarga.Text = msg)
                    );
                }
                else if (datos.EsCritica)
                {
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("SEGURIDAD") || ex.Message.Contains("seguridad"))
                {
                    VentanaMensaje.Mostrar(ex.Message, "ACTUALIZACIÓN ABORTADA", MessageBoxButton.OK);
                    txtEstadoCarga.Text = "Actualización cancelada por seguridad.";
                }
                else
                {
                    VentanaMensaje.Mostrar("Error: " + ex.Message, "DEBUG", MessageBoxButton.OK);
                }
            }
        }
        #endregion
        #region Noticias

        private async void CargarNoticiasGitHub()
        {
            try
            {
                var noticiasDescargadas = await NewsService.ObtenerNoticiasAsync();

                if (noticiasDescargadas == null || noticiasDescargadas.Count == 0) return;

                listaNoticias = noticiasDescargadas;

                GenerarPuntos();
                MostrarNoticia(0);
            }
            catch
            {
                
            }
        }
        private async void ActualizarNoticiaConEfecto(int indice)
        {
            var noticia = listaNoticias[indice];
            string urlNueva = noticia.Imagen;

            var bitmapNuevo = await NewsService.ObtenerImagenAsync(urlNueva);

            if (bitmapNuevo != null)
            {
                ImgNoticiaFondoBrush.ImageSource = bitmapNuevo;

                TxtTitulo.Text = noticia.Titulo;
                TxtCuerpo.Text = noticia.Cuerpo;

                DoubleAnimation fadeOut = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(500)
                };

                fadeOut.Completed += (s, e) =>
                {
                    ImgNoticiaBrush.ImageSource = bitmapNuevo;
                    ImgNoticiaBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, null);
                    ImgNoticiaBrush.Opacity = 1.0;
                };
                ImgNoticiaBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, fadeOut);
            }

            MostrarNoticia(indice);
            ActualizarPuntos();
        }

        private void DetenerCarrusel(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_timerNoticias != null)
            {
                _timerNoticias.Stop();
            }
        }

        private void IniciarCarrusel(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_timerNoticias != null)
            {
                _timerNoticias.Start();
            }
        }

        private async void MostrarNoticia(int index)
        {
            if (listaNoticias == null || listaNoticias.Count == 0) return;

            if (index < 0) index = listaNoticias.Count - 1;
            if (index >= listaNoticias.Count) index = 0;

            indiceActual = index;
            var dato = listaNoticias[indiceActual];

            TxtTitulo.Text = dato.Titulo;
            TxtCuerpo.Text = dato.Cuerpo;

            if (dato.MostrarBoton == true)
            {
                BtnNoticiaAccion.Visibility = Visibility.Visible;
                BtnNoticiaAccion.Content = dato.BotonTexto;
                BtnNoticiaAccion.Tag = dato.BotonUrl;
                if (!string.IsNullOrEmpty(dato.BotonColor))
                {
                    try
                    {
                        BtnNoticiaAccion.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(dato.BotonColor);
                    }
                    catch { }
                }
            }
            else
            {
                BtnNoticiaAccion.Visibility = Visibility.Collapsed;
            }

            try
            {
                if (!string.IsNullOrEmpty(dato.Imagen))
                {
                    var nuevaImagen = await NewsService.ObtenerImagenAsync(dato.Imagen);

                    if (nuevaImagen != null)
                    {
                        ImgNoticiaBrush.ImageSource = nuevaImagen;
                        ImgNoticiaFondoBrush.ImageSource = nuevaImagen;

                        ImgNoticiaBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, null);
                        ImgNoticiaBrush.Opacity = 1.0;
                    }
                }
            }
            catch { }

            ActualizarPuntos();
        }

        private void BtnNoticia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void GenerarPuntos()
        {
            PanelPuntos.Children.Clear();

            foreach (var n in listaNoticias)
            {
                System.Windows.Shapes.Ellipse punto = new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Margin = new Thickness(5),
                    Fill = System.Windows.Media.Brushes.Gray
                };
                PanelPuntos.Children.Add(punto);
            }
        }

        private void ActualizarPuntos()
        {
            if (PanelPuntos == null) return;

            for (int i = 0; i < PanelPuntos.Children.Count; i++)
            {
                if (PanelPuntos.Children[i] is System.Windows.Shapes.Ellipse punto)
                {
                    if (i == indiceActual)
                    {
                        punto.Fill = System.Windows.Media.Brushes.White;
                        punto.Opacity = 1.0;
                    }
                    else
                    {
                        punto.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 255));
                        punto.Opacity = 0.5;
                    }
                }
            }
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            indiceActual--;
            if (indiceActual < 0)
            {
                indiceActual = listaNoticias.Count - 1;
            }
            ActualizarNoticiaConEfecto(indiceActual);
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            indiceActual++;
            if (indiceActual >= listaNoticias.Count) indiceActual = 0;
            ActualizarNoticiaConEfecto(indiceActual);
        }
        #endregion
        #region TecniClients
        public async Task CargarClientesDesdeInternet()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = "https://raw.githubusercontent.com/Tecnikero/TecniLauncher-Data/refs/heads/main/tecniclient/tecniclients.json";
                    string json = await client.GetStringAsync(url);

                    var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var clientes = JsonSerializer.Deserialize<List<TecniClientModel>>(json, opciones);

                    ListTecniClients.ItemsSource = clientes;
                }
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error cargando los clientes: " + ex.Message, "Error", MessageBoxButton.OK);
            }
        }

        private async void BtnInstalarTecniClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;

            if (btn.DataContext is TecniClientModel clienteSeleccionado)
            {
                btn.IsEnabled = false;

                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string carpetaInstancia = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances", clienteSeleccionado.Id);
                    string carpetaMods = System.IO.Path.Combine(carpetaInstancia, "mods");
                    string archivoZipTemp = System.IO.Path.Combine(carpetaInstancia, "modpack_temp.zip");
                    string archivoVersion = System.IO.Path.Combine(carpetaInstancia, "version.txt");

                    string versionLocal = "";
                    if (System.IO.Directory.Exists(carpetaInstancia) && System.IO.File.Exists(archivoVersion))
                    {
                        versionLocal = System.IO.File.ReadAllText(archivoVersion);
                    }
                    else
                    {
                        System.IO.Directory.CreateDirectory(carpetaInstancia);
                    }

                    if (versionLocal != clienteSeleccionado.Version)
                    {
                        btn.Content = "Descargando Mods...";
                        PanelCarga.Visibility = Visibility.Visible;
                        txtEstadoCarga.Text = $"Descargando {clienteSeleccionado.Name}...";
                        barraCarga.IsIndeterminate = true;

                        if (System.IO.Directory.Exists(carpetaMods))
                            System.IO.Directory.Delete(carpetaMods, true);

                        System.IO.Directory.CreateDirectory(carpetaMods);

                        using (HttpClient clientHttp = new HttpClient())
                        {
                            byte[] zipBytes = await clientHttp.GetByteArrayAsync(clienteSeleccionado.ModpackUrl);
                            await System.IO.File.WriteAllBytesAsync(archivoZipTemp, zipBytes);
                        }

                        System.IO.Compression.ZipFile.ExtractToDirectory(archivoZipTemp, carpetaMods, true);
                        System.IO.File.Delete(archivoZipTemp);

                        System.IO.File.WriteAllText(archivoVersion, clienteSeleccionado.Version);
                    }

                    btn.Content = "Iniciando...";
                    PanelCarga.Visibility = Visibility.Visible;
                    barraCarga.IsIndeterminate = false;

                    if (client != null && client.IsInitialized)
                    {
                        client.SetPresence(new DiscordRPC.RichPresence()
                        {
                            Details = $"Jugando a {clienteSeleccionado.Name}",
                            State = "TecniClient Oficial",
                            Assets = new DiscordRPC.Assets() { LargeImageKey = "tecnilogo", LargeImageText = "TecniLauncher" },
                            Timestamps = DiscordRPC.Timestamps.Now
                        });
                    }

                    Perfil perfilCliente = new Perfil()
                    {
                        Nombre = clienteSeleccionado.Name,
                        Version = clienteSeleccionado.MinecraftVersion,
                        TipoLoader = clienteSeleccionado.Loader,
                        VersionLoaderExacta = clienteSeleccionado.LoaderVersion,
                        MemoriaRam = (int)(clienteSeleccionado.SelectedRam * 1024),
                        RutaCarpeta = carpetaInstancia,
                        ModoRendimientoActivado = true
                    };

                    System.Diagnostics.Process procesoMinecraft = await LanzarMinecraft(perfilCliente);

                    PanelCarga.Visibility = Visibility.Collapsed;

                    if (procesoMinecraft != null)
                    {
                        if (chkOcultarLauncher.IsChecked == true)
                            this.Hide();
                        else
                            this.WindowState = WindowState.Minimized;

                        procesoMinecraft.EnableRaisingEvents = true;
                        procesoMinecraft.Exited += (s, ev) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                this.Show();
                                this.WindowState = WindowState.Normal;
                                this.Activate();
                                btn.Content = "Jugar";

                                if (client != null && client.IsInitialized)
                                {
                                    client.SetPresence(new DiscordRPC.RichPresence()
                                    {
                                        Details = "En el Menú Principal",
                                        State = $"V{VERSION_ACTUAL}",
                                        Assets = new DiscordRPC.Assets() { LargeImageKey = "tecnilogo", LargeImageText = "TecniLauncher" },
                                        Timestamps = DiscordRPC.Timestamps.Now
                                    });
                                }
                            });
                        };
                    }
                    else
                    {
                        btn.Content = "Jugar";
                    }
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Error: " + ex.Message, "Error", MessageBoxButton.OK);
                    btn.Content = "Jugar";
                }
                finally
                {
                    btn.IsEnabled = true;
                    PanelCarga.Visibility = Visibility.Collapsed;
                }
            }
        }
        #endregion
    }
}