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
        private CancellationTokenSource _ctsLoaderVersiones;
        private readonly Dictionary<string, string> _cacheProyectosLocales = new();
        private DispatcherTimer _timerNoticias;
        public DiscordRpcClient client;
        private Rect _tamanoNormal;
        private bool _esFalsoMaximizado = false;
        private int _offsetMods = 0;
        private bool _hayMasResultados = true;
        private readonly SemaphoreSlim _semMods = new SemaphoreSlim(1, 1);
        private System.Collections.ObjectModel.ObservableCollection<ModInfo> _listaModsActual
            = new System.Collections.ObjectModel.ObservableCollection<ModInfo>();
        private bool _modpackInstalando = false;
        private int _detalleModpackToken = 0;
        private bool _tecniClientOcupado = false;
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
            this.Closing += Window_Closing;
        }

        private void InicializarLauncher()
        {
            Core.CambiarIdioma(Core.IdiomaActual);
            SeleccionarIdiomaEnCombo();

            ComprobarActualizaciones();
            TxtVersion.Text = $"v{VERSION_ACTUAL}";
            if (TxtVersionAjustes != null)
            {
                TxtVersionAjustes.Text = Core.T("Txt_DesarrolladoPor", VERSION_ACTUAL);
            }
            CargarNoticiasGitHub();

            _timerNoticias = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timerNoticias.Tick += (s, e) =>
            {
                if (listaNoticias.Count == 0) return;
                BtnSiguiente_Click(null, null);
            };
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

            txtUsuario.Text = Core.T("Txt_VerificandoSesion");

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

                    txtUsuario.Text = string.IsNullOrEmpty(Core.UltimoNombreOffline) ? Core.T("Txt_SinSesion") : Core.UltimoNombreOffline;
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
                VentanaMensaje.Mostrar(Core.T("Msg_EnlaceError", ex.Message));
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

        private void AbrirLogin_Click(object sender, RoutedEventArgs e)
        {
            btnCerrarSesionTecni.Visibility = Core.EsSesionTecniStudio ? Visibility.Visible : Visibility.Collapsed;
            GridLogin.Visibility = Visibility.Visible;
        }
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

                btnMicrosoft.Content = Core.T("Txt_IniciarMicrosoft");
                btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(0, 93, 166));
                txtOfflineName.IsEnabled = true;
                txtOfflineName.Text = "Jugador";
                txtUsuario.Text = Core.T("Txt_SinSesion");
                CargarSkinEnInterfaz(null);
                VentanaMensaje.Mostrar(Core.T("Msg_SesionCerrada"));
            }
            else
            {
                try
                {
                    btnMicrosoft.IsEnabled = false;
                    btnMicrosoft.Content = Core.T("Txt_Iniciando");

                    var resultado = await AuthService.LoginMicrosoftAsync(Core.RutaSesion);

                    if (resultado != null)
                    {
                        LoguearUsuarioPremium(resultado);
                        GridLogin.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar(Core.T("Msg_LoginFallido", ex.Message));
                    btnMicrosoft.Content = Core.T("Txt_IniciarMicrosoft");
                }
                finally { btnMicrosoft.IsEnabled = true; }
            }
        }

        private async void BtnCerrarSesionTecni_Click(object sender, RoutedEventArgs e)
        {
            btnCerrarSesionTecni.IsEnabled = false;
            try
            {
                await AuthService.CerrarSesionTecniAsync();

                esPremium = false;
                Core.UltimoNombreOffline = "Jugador";
                Core.GuardarConfiguracion();

                btnCerrarSesionTecni.Visibility = Visibility.Collapsed;
                txtOfflineName.IsEnabled = true;
                txtOfflineName.Text = "Jugador";
                txtUsuario.Text = Core.T("Txt_SinSesion");
                CargarSkinEnInterfaz(null);
                VentanaMensaje.Mostrar(Core.T("Msg_SesionTecniCerrada"));
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar(Core.T("Msg_CerrarSesionError", ex.Message));
            }
            finally { btnCerrarSesionTecni.IsEnabled = true; }
        }

        private void LoguearUsuarioPremium(MSession sesion)
        {
            Core.SesionUsuario = sesion;
            Core.EsTecniStudio = false;
            esPremium = true;

            txtUsuario.Text = sesion.Username;
            txtOfflineName.Text = sesion.Username;
            txtOfflineName.IsEnabled = false;

            btnMicrosoft.Content = Core.T("Txt_CerrarSesion");
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
                txtEstadoTecni.Text = Core.T("Txt_LlenaCampos");
                txtEstadoTecni.Visibility = Visibility.Visible;
                return;
            }

            txtEstadoTecni.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4facfe"));
            txtEstadoTecni.Text = Core.T("Txt_ConectandoTecni");
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
                    txtEstadoTecni.Text = Core.T("Txt_CredencialesIncorrectas");
                }
            }
            catch (Exception)
            {
                txtEstadoTecni.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5656"));
                txtEstadoTecni.Text = Core.T("Txt_ErrorConexionBD");
            }
            finally
            {
                btnLoginTecni.IsEnabled = true;
            }
        }
        #endregion
        #region Skin

        private bool _webViewDetalleConfigurado = false;
        private bool _avisoWebViewMostrado = false;
        private async Task<bool> AsegurarWebView2Async(Microsoft.Web.WebView2.Wpf.WebView2 visor)
        {
            try
            {
                await visor.EnsureCoreWebView2Async(null);
                return visor.CoreWebView2 != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WebView2 no disponible: " + ex.Message);
                return false;
            }
        }

        private static void AbrirEnlaceExterno(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("No se pudo abrir el enlace: " + ex.Message); }
        }

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
                    if (!await AsegurarWebView2Async(VisorSkinWebView)) return;

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

        private void BtnBorrarSkin_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(txtUsuario.Text) && txtUsuario.Text != Core.T("Txt_SinSesion"))
            {
                VentanaMensaje.Mostrar(Core.T("Msg_RecargandoSkin"), Core.T("Txt_TituloActualizando"), MessageBoxButton.OK);
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
                VentanaMensaje.Mostrar(Core.T("Msg_SeleccionaPerfil"));
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
            bool esperandoCierreJuegoJugar = false;
            try
            {
                btnJugar.IsEnabled = false;
                PanelCarga.Visibility = Visibility.Visible;

                System.Diagnostics.Process procesoMinecraft = await LanzarMinecraft(perfil);
                PanelCarga.Visibility = Visibility.Collapsed;

                if (procesoMinecraft != null)
                {
                    esperandoCierreJuegoJugar = true;
                    var cronometro = Stopwatch.StartNew();

                    if (chkOcultarLauncher.IsChecked == true)
                        this.Hide();
                    else
                        this.WindowState = WindowState.Minimized;

                    procesoMinecraft.EnableRaisingEvents = true;
                    procesoMinecraft.Exited += (s, ev) =>
                    {
                        long segundosJugados = (long)cronometro.Elapsed.TotalSeconds;

                        Dispatcher.Invoke(() =>
                        {
                            btnJugar.IsEnabled = true;
                            perfil.SegundosJugados += segundosJugados;
                            Core.GuardarPerfiles();
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
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorLanzar", ex.Message));
            }
            finally
            {
                if (!esperandoCierreJuegoJugar) btnJugar.IsEnabled = true;
                PanelCarga.Visibility = Visibility.Collapsed;
            }
        }
        private async Task<Process> LanzarMinecraft(Perfil perfil)
        {
            try
            {
                txtEstadoCarga.Text = Core.T("Txt_Iniciando");
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
                        txtEstadoCarga.Text = Core.T("Txt_VerificandoArchivo", e.Name);
                    });
                };

                string idVersion = await InstalarModLoader(perfil, launcher);

                var argumentosExtra = new System.Collections.Generic.List<CmlLib.Core.ProcessBuilder.MArgument>();

                if (Core.EsSesionTecniStudio)
                {
                    Dispatcher.Invoke(() => txtEstadoCarga.Text = Core.T("Txt_ConectandoSkins"));
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

                    Dispatcher.Invoke(() => txtEstadoCarga.Text = Core.T("Txt_InyectandoAikar"));
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
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorCriticoLanzar", ex.Message));
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

                    Dispatcher.Invoke(() => txtEstadoCarga.Text = Core.T("Txt_DescargandoAuthlib"));
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
                txtEstadoCarga.Text = Core.T("Txt_InstalandoLoader", perfil.TipoLoader);
                barraCarga.IsIndeterminate = true;
            });

            try
            {
                string verInstalada = null;

                string versionExactaValida = LoaderVersionValida(perfil.VersionLoaderExacta);
                string versionExacta = versionExactaValida.Length == 0 ? null : versionExactaValida;

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
        private const string TXT_LOADER_BUSCANDO = "Buscando...";
        private const string TXT_LOADER_NO_DISPONIBLE = "No disponible";
        private const string TXT_LOADER_ERROR = "Error al buscar";
        private const string TXT_LOADER_AUTOMATICO = "Automático / Default";

        private static readonly string[] _textosPlaceholderLoader =
            { TXT_LOADER_BUSCANDO, TXT_LOADER_NO_DISPONIBLE, TXT_LOADER_ERROR, "Error", TXT_LOADER_AUTOMATICO };

        private static string LoaderVersionValida(string? valor) =>
            string.IsNullOrWhiteSpace(valor) || _textosPlaceholderLoader.Contains(valor) ? "" : valor;

        private async void ComboLoader_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ctsLoaderVersiones?.Cancel();
            _ctsLoaderVersiones = new CancellationTokenSource();
            var token = _ctsLoaderVersiones.Token;

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
            comboLoaderVersion.ItemsSource = new List<string> { TXT_LOADER_BUSCANDO };
            comboLoaderVersion.SelectedIndex = 0;

            try
            {
                List<string> versionesEncontradas = new List<string>();

                // === LÓGICA FABRIC ===
                if (tipoLoader == "Fabric")
                {
                    var instaladorPropio = new TecniLauncher.FabricInstaller(Core.LauncherGlobal);

                    var listaFabric = await instaladorPropio.ObtenerVersiones(versionMinecraft, token);
                    versionesEncontradas.AddRange(listaFabric);
                }
                // === LÓGICA FORGE ===
                else if (tipoLoader == "Forge")
                {
                    var instalador = new CmlLib.Core.Installer.Forge.ForgeInstaller(Core.LauncherGlobal);
                    var datos = await Task.Run(async () => await instalador.GetForgeVersions(versionMinecraft), token).WaitAsync(token);

                    foreach (var v in datos)
                    {
                        versionesEncontradas.Add(v.ForgeVersionName);
                    }
                }

                // === LÓGICA NEOFORGE ===
                else if (tipoLoader == "NeoForge")
                {
                    var instalador = new CmlLib.Core.Installer.NeoForge.NeoForgeInstaller(Core.LauncherGlobal);

                    var datos = await Task.Run(async () => await instalador.GetForgeVersions(versionMinecraft), token).WaitAsync(token);

                    foreach (var v in datos)
                    {
                        versionesEncontradas.Insert(0, v.VersionName);

                    }
                }

                token.ThrowIfCancellationRequested();

                if (versionesEncontradas.Count > 0)
                {
                    comboLoaderVersion.ItemsSource = versionesEncontradas;
                    comboLoaderVersion.SelectedIndex = 0;
                    comboLoaderVersion.IsEnabled = true;
                }
                else
                {
                    comboLoaderVersion.ItemsSource = new List<string> { TXT_LOADER_NO_DISPONIBLE };
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (token.IsCancellationRequested) return;
                comboLoaderVersion.ItemsSource = new List<string> { TXT_LOADER_ERROR };
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

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusExNativo
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusExNativo lpBuffer);

        private static long ObtenerRamFisicaBytes()
        {
            var estado = new MemoryStatusExNativo { dwLength = (uint)Marshal.SizeOf<MemoryStatusExNativo>() };
            if (GlobalMemoryStatusEx(ref estado) && estado.ullTotalPhys > 0)
                return (long)estado.ullTotalPhys;

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        private void ConfigurarRamAutomatica()
        {
            int ramTotalGB = Math.Max(1, (int)Math.Ceiling(ObtenerRamFisicaBytes() / 1024.0 / 1024.0 / 1024.0));

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
            if (string.IsNullOrEmpty(nombre)) { VentanaMensaje.Mostrar(Core.T("Msg_EscribeNombre")); return; }

            if (nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nombre == "." || nombre == "..")
            {
                VentanaMensaje.Mostrar(Core.T("Msg_NombreInvalido"));
                return;
            }

            string iconoFinal = "/Resources/Icons/icon1.png";
            if (perfilAEditar != null)
            {
                iconoFinal = perfilAEditar.IconoPath;
            }
            if (listaIconos.SelectedIndex != -1 && listaIconos.SelectedItem is ListBoxItem seleccionado)
            {
                iconoFinal = $"/Resources/Icons/{seleccionado.Tag}";
            }

            Perfil duplicado = Core.Perfiles.Find(p => string.Equals(p.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (perfilAEditar == null && duplicado != null) { VentanaMensaje.Mostrar(Core.T("Msg_NombreExiste")); return; }
            if (perfilAEditar != null && duplicado != null && duplicado != perfilAEditar) { VentanaMensaje.Mostrar(Core.T("Msg_NombreOcupado")); return; }

            int ram = (int)sliderRam.Value * 1024;
            bool activarRendimiento = chkModoRendimiento?.IsChecked == true;
            string versionExactaCapturada = "";

            if (comboLoader.SelectedIndex > 0 && comboLoaderVersion.SelectedItem != null)
            {
                versionExactaCapturada = LoaderVersionValida(comboLoaderVersion.SelectedItem.ToString());
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
                    catch { VentanaMensaje.Mostrar(Core.T("Msg_RenombrarError")); return; }
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

            string loaderGuardado = LoaderVersionValida(perfilAEditar.VersionLoaderExacta);
            if (loaderGuardado.Length > 0)
            {
                comboLoaderVersion.ItemsSource = new List<string> { loaderGuardado };
                comboLoaderVersion.SelectedIndex = 0;
            }
            else
            {
                comboLoaderVersion.ItemsSource = new List<string> { TXT_LOADER_AUTOMATICO };
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
            if (VentanaMensaje.Mostrar(Core.T("Msg_EliminarPerfil", perfil.Nombre), Core.T("Txt_TituloConfirmar"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                bool rutaSegura = false;
                string rutaCompleta = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(perfil.RutaCarpeta))
                    {
                        rutaCompleta = Path.GetFullPath(perfil.RutaCarpeta);
                        string baseInstances = Path.GetFullPath(Core.RutaInstances).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        rutaSegura = rutaCompleta.StartsWith(baseInstances, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { rutaSegura = false; }

                if (rutaSegura)
                {
                    if (Directory.Exists(rutaCompleta)) try { Directory.Delete(rutaCompleta, true); } catch { }
                }
                else
                {
                    VentanaMensaje.Mostrar(Core.T("Msg_CarpetaFueraInstances"));
                }
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
        private static readonly string[] _categoriasMods =
        {
            "adventure", "cursed", "decoration", "economy", "equipment", "food", "game-mechanics",
            "library", "magic", "management", "minigame", "mobs", "optimization", "social",
            "storage", "technology", "transportation", "utility", "worldgen"
        };
        private static readonly (string Clave, string Texto)[] _loadersMods =
        {
            ("fabric", "Fabric"), ("forge", "Forge"), ("neoforge", "NeoForge"), ("quilt", "Quilt")
        };

        private bool _filtrosInicializados = false;
        private bool _cargandoFiltros = false;
        private List<FiltroItem>? _versionesMcFiltro;
        private int _busquedaId = 0;
        private List<ModLocal> _modsLocales = new();

        private readonly SemaphoreSlim _semInstalaciones = new SemaphoreSlim(2, 2);
        private int _instalacionesActivas = 0;
        private CancellationTokenSource? _ctsToast;

        private static string NombreCategoria(string slug)
        {
            if (System.Windows.Application.Current?.TryFindResource("Cat_" + slug.Replace('-', '_')) is string t) return t;
            string s = slug.Replace('-', ' ');
            return char.ToUpper(s[0]) + s[1..];
        }

        private void InicializarFiltrosMods()
        {
            if (_filtrosInicializados) return;
            _filtrosInicializados = true;
            _cargandoFiltros = true;
            try
            {
                var categorias = new List<FiltroItem> { new FiltroItem { Clave = "", Texto = Core.T("Mods_FiltroTodas") } };
                categorias.AddRange(_categoriasMods.Select(c => new FiltroItem { Clave = c, Texto = NombreCategoria(c) }));
                comboFiltroCategoria.ItemsSource = categorias;
                comboFiltroCategoria.SelectedIndex = 0;

                comboFiltroLoader.ItemsSource = _loadersMods.Select(l => new FiltroItem { Clave = l.Clave, Texto = l.Texto }).ToList();
            }
            finally { _cargandoFiltros = false; }
        }

        private async Task AplicarPerfilAFiltrosAsync(Perfil p)
        {
            InicializarFiltrosMods();

            if (_versionesMcFiltro == null)
            {
                var vs = await ModrinthAPI.ObtenerVersionesMinecraftAsync();
                if (vs.Count > 0) _versionesMcFiltro = vs.Select(v => new FiltroItem { Clave = v, Texto = v }).ToList();
            }

            _cargandoFiltros = true;
            try
            {
                var versiones = new List<FiltroItem>(_versionesMcFiltro ?? new List<FiltroItem>());
                if (!string.IsNullOrEmpty(p.Version) && !versiones.Any(v => v.Clave == p.Version))
                    versiones.Insert(0, new FiltroItem { Clave = p.Version, Texto = p.Version });

                comboFiltroVersion.ItemsSource = versiones;
                comboFiltroVersion.SelectedValue = p.Version;
                comboFiltroLoader.SelectedValue = (p.TipoLoader ?? "").ToLowerInvariant();
            }
            finally { _cargandoFiltros = false; }

            ActualizarAvisoFiltros(p);
        }

        private (string Loader, string Version, string? Categoria) ObtenerFiltrosActuales(Perfil p)
        {
            string loader = comboFiltroLoader.SelectedValue as string ?? "";
            string version = comboFiltroVersion.SelectedValue as string ?? p.Version;
            string? categoria = comboFiltroCategoria.SelectedValue as string;
            if (string.IsNullOrEmpty(categoria)) categoria = null;
            return (loader, version, categoria);
        }

        private void ActualizarAvisoFiltros(Perfil p)
        {
            var f = ObtenerFiltrosActuales(p);
            bool distinto = !string.Equals(f.Loader, p.TipoLoader, StringComparison.OrdinalIgnoreCase) || f.Version != p.Version;
            txtAvisoFiltros.Text = distinto ? Core.T("Mods_AvisoFiltros", p.TipoLoader, p.Version) : "";
            txtAvisoFiltros.Visibility = distinto ? Visibility.Visible : Visibility.Collapsed;
            btnResetFiltros.Visibility = distinto ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros || !_filtrosInicializados || !modoOnline) return;
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;
            ActualizarAvisoFiltros(p);
            await BuscarOnlineAsync();
        }

        private async void BtnResetFiltros_Click(object sender, RoutedEventArgs e)
        {
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;
            await AplicarPerfilAFiltrosAsync(p);
            await BuscarOnlineAsync();
        }

        private async void ComboPerfilesMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            txtModVersion.Text = p.Version;
            txtModLoader.Text = p.TipoLoader;

            _busquedaId++;
            _offsetMods = 0;
            _hayMasResultados = true;
            _listaModsActual.Clear();
            await AplicarPerfilAFiltrosAsync(p);
            if (!ReferenceEquals(comboPerfilesMods.SelectedItem, p)) return;

            CargarModsLocal(p);
            if (modoOnline) await BuscarOnlineAsync();
        }

        private async void TabOnlineBtn_Checked(object sender, RoutedEventArgs e)
        {
            modoOnline = true;

            if (PanelOnline == null || PanelInstalados == null) return;

            PanelOnline.Visibility = Visibility.Visible;
            PanelInstalados.Visibility = Visibility.Collapsed;

            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            if (_listaModsActual.Count == 0) await BuscarOnlineAsync();
            else await RefrescarInstaladosOnlineAsync(p, soloMarcar: false);
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

        private async void BtnBuscarOnline_Click(object? sender, RoutedEventArgs? e) => await BuscarOnlineAsync();

        private void TxtBuscadorMods_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = BuscarOnlineAsync();
        }

        private async Task BuscarOnlineAsync()
        {
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            _busquedaId++;
            _offsetMods = 0;
            _hayMasResultados = true;
            _listaModsActual.Clear();
            listaModsGestor.ItemsSource = _listaModsActual;
            ScrollModsOnline.ScrollToTop();

            await FetchYAgregarMods(p, esPrimeraCarga: true);
        }

        private void MostrarEstadoMods(string? texto)
        {
            txtEstadoMods.Text = texto ?? "";
            txtEstadoMods.Visibility = string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task FetchYAgregarMods(Perfil p, bool esPrimeraCarga = false)
        {
            if (esPrimeraCarga) await _semMods.WaitAsync();
            else if (!await _semMods.WaitAsync(0)) return;

            try
            {
                if (!_hayMasResultados) return;

                int idBusqueda = _busquedaId;
                string busqueda = txtBuscadorMods?.Text ?? "";
                int limitePaginacion = 30;
                var (loader, version, categoria) = ObtenerFiltrosActuales(p);

                if (string.IsNullOrEmpty(loader))
                {
                    MostrarEstadoMods(Core.T("Mods_SinLoader"));
                    _hayMasResultados = false;
                    return;
                }

                if (esPrimeraCarga) MostrarEstadoMods(Core.T("Mods_Cargando"));

                var res = await ModrinthAPI.BuscarMods(busqueda, loader, version, _offsetMods, limitePaginacion, categoria);

                if (idBusqueda != _busquedaId) return;

                if (res == null || res.Count == 0)
                {
                    _hayMasResultados = false;
                    MostrarEstadoMods(_listaModsActual.Count == 0 ? Core.T("Mods_SinResultados") : null);
                    return;
                }

                string carpetaMods = Path.Combine(p.RutaCarpeta, "mods");
                var proyectosLocales = await ObtenerProjectIdsInstaladosAsync(carpetaMods);

                if (idBusqueda != _busquedaId || !ReferenceEquals(comboPerfilesMods.SelectedItem, p)) return;

                foreach (var m in res)
                {
                    if (_listaModsActual.Any(x => x.project_id == m.project_id)) continue;

                    string titulo = (m.title ?? "").ToLower();
                    m.esRecomendado = titulo.Contains("sodium") || titulo.Contains("iris") || titulo.Contains("lithium");

                    if (string.IsNullOrEmpty(m.icon_url))
                        m.icon_url = "https://cdn.modrinth.com/assets/icon.png";

                    m.estaInstalado = !string.IsNullOrEmpty(m.project_id) && proyectosLocales.Contains(m.project_id);

                    _listaModsActual.Add(m);
                }

                _offsetMods += res.Count;
                if (res.Count < limitePaginacion) _hayMasResultados = false;

                MostrarEstadoMods(null);
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

        private static string? CalcularSha1(string ruta)
        {
            try
            {
                using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
            }
            catch { return null; }
        }

        private static IEnumerable<FileInfo> ListarJarsDeCarpeta(string carpetaMods)
        {
            return new DirectoryInfo(carpetaMods).EnumerateFiles()
                .Where(f => f.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                         || f.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<HashSet<string>> ObtenerProjectIdsInstaladosAsync(string carpetaMods)
        {
            var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(carpetaMods)) return resultado;

            try
            {
                var jars = ListarJarsDeCarpeta(carpetaMods).ToList();
                var claves = jars.Select(f => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}").ToList();

                var nuevos = jars.Zip(claves).Where(x => !_cacheProyectosLocales.ContainsKey(x.Second)).ToList();
                if (nuevos.Count > 0)
                {
                    var hashes = await Task.Run(() => nuevos
                        .Select(x => (Clave: x.Second, Sha1: CalcularSha1(x.First.FullName)))
                        .Where(x => x.Sha1 != null)
                        .ToList());

                    var proyectos = await ModrinthAPI.ObtenerProjectIdsPorHashAsync(hashes.Select(h => h.Sha1!).Distinct().ToList());

                    if (proyectos != null)
                    {
                        foreach (var h in hashes)
                            _cacheProyectosLocales[h.Clave] = proyectos.TryGetValue(h.Sha1!, out var pid) ? pid : "";
                    }
                }

                foreach (var clave in claves)
                {
                    if (_cacheProyectosLocales.TryGetValue(clave, out var id) && id.Length > 0)
                        resultado.Add(id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error identificando mods instalados: " + ex.Message);
            }
            return resultado;
        }

        private async Task RefrescarInstaladosOnlineAsync(Perfil p, bool soloMarcar)
        {
            if (_listaModsActual.Count == 0) return;

            var ids = await ObtenerProjectIdsInstaladosAsync(Path.Combine(p.RutaCarpeta, "mods"));
            if (!ReferenceEquals(comboPerfilesMods.SelectedItem, p)) return;

            foreach (var m in _listaModsActual.ToList())
            {
                bool instalado = !string.IsNullOrEmpty(m.project_id) && ids.Contains(m.project_id);
                if (instalado || !soloMarcar) m.estaInstalado = instalado;
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

        private async void MostrarToast(string texto, bool enProgreso = false, bool esError = false)
        {
            _ctsToast?.Cancel();
            _ctsToast = new CancellationTokenSource();
            var ct = _ctsToast.Token;

            txtToastMods.Text = texto;
            barraToastMods.Visibility = enProgreso ? Visibility.Visible : Visibility.Collapsed;
            ToastMods.BorderBrush = esError
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0x4a, 0x4a))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4f, 0xac, 0xfe));
            ToastMods.Visibility = Visibility.Visible;

            if (enProgreso) return;

            try
            {
                await Task.Delay(esError ? 7000 : 3500, ct);
                ToastMods.Visibility = Visibility.Collapsed;
            }
            catch (TaskCanceledException) { }
        }

        private async void BtnVerVersiones_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ModInfo mod) return;

                if (comboPerfilesMods.SelectedItem is not Perfil perfilActual)
                {
                    VentanaMensaje.Mostrar(Core.T("Msg_SeleccionaPerfil"));
                    return;
                }

                System.Windows.Controls.Panel.SetZIndex(OverlayVersiones, 999);
                OverlayVersiones.Visibility = Visibility.Visible;
                listaArchivosVersion.ItemsSource = null;
                txtNombreModVersiones.Text = Core.T("Txt_BuscandoVersionesMod", mod.title);

                var versiones = await ModrinthAPI.ObtenerListaVersiones(mod.project_id, perfilActual.Version, perfilActual.TipoLoader);

                if (versiones != null && versiones.Count > 0)
                {
                    txtNombreModVersiones.Text = mod.title;
                    listaArchivosVersion.ItemsSource = versiones;
                }
                else
                {
                    txtNombreModVersiones.Text = Core.T("Txt_SinVersiones");
                }
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorVersionesMod", ex.Message));
                OverlayVersiones.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnInstalarRapido_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ModInfo mod) return;
            if (!mod.PuedeInstalar) return;

            if (comboPerfilesMods.SelectedItem is not Perfil p)
            {
                MostrarToast(Core.T("Msg_SeleccionaPerfil"), false, true);
                return;
            }

            _ = InstalarModEnSegundoPlanoAsync(p, mod, null, mod.title, mod.project_id);
        }

        private void BtnInstalarVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ModVersion version) return;
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            OverlayVersiones.Visibility = Visibility.Collapsed;

            var tarjeta = _listaModsActual.FirstOrDefault(m => m.project_id == version.ProjectId);
            string titulo = tarjeta?.title ?? version.NombreVersion;
            _ = InstalarModEnSegundoPlanoAsync(p, tarjeta, version, titulo, version.ProjectId);
        }

        private async Task InstalarModEnSegundoPlanoAsync(Perfil p, ModInfo? tarjeta, ModVersion? version, string titulo, string projectId)
        {
            string versionMC = p.Version;
            string loader = p.TipoLoader;
            string carpetaMods = Path.Combine(p.RutaCarpeta, "mods");

            if (tarjeta != null) tarjeta.instalando = true;
            _instalacionesActivas++;
            MostrarToast(Core.T("Toast_Instalando", titulo), true);

            try
            {
                await _semInstalaciones.WaitAsync();
                try
                {
                    if (version == null)
                    {
                        var lista = await ModrinthAPI.ObtenerListaVersiones(projectId, versionMC, loader);
                        version = lista.FirstOrDefault(v => v.Tipo == "release") ?? lista.FirstOrDefault();

                        if (version == null)
                        {
                            MostrarToast(Core.T("Toast_SinVersion", titulo, versionMC, loader), false, true);
                            return;
                        }
                    }

                    Directory.CreateDirectory(carpetaMods);
                    var sinVersion = await DescargarModYDependencias(version, versionMC, loader, carpetaMods);

                    if (tarjeta != null && ReferenceEquals(comboPerfilesMods.SelectedItem, p)) tarjeta.estaInstalado = true;

                    if (sinVersion.Count == 0)
                        MostrarToast(Core.T("Toast_Instalado", titulo));
                    else
                        MostrarToast(Core.T("Toast_InstaladoSinDeps", titulo, string.Join(", ", sinVersion)), false, true);

                    if (ReferenceEquals(comboPerfilesMods.SelectedItem, p))
                    {
                        CargarModsLocal(p);
                        _ = RefrescarInstaladosOnlineAsync(p, soloMarcar: true);
                    }
                }
                finally
                {
                    _semInstalaciones.Release();
                }
            }
            catch (Exception ex)
            {
                MostrarToast(Core.T("Toast_ErrorInstalar", titulo, ex.Message), false, true);
            }
            finally
            {
                _instalacionesActivas--;
                if (tarjeta != null) tarjeta.instalando = false;
            }
        }

        private async Task<List<string>> DescargarModYDependencias(ModVersion versionBase, string versionMC, string loader, string carpetaMods)
        {
            var plan = await ModrinthAPI.ResolverDependenciasAsync(versionBase, versionMC, loader);
            var yaInstalados = await ObtenerProjectIdsInstaladosAsync(carpetaMods);

            var pendientes = new List<(ModVersion Mod, string Destino)>();
            foreach (var mod in plan.Descargas)
            {
                bool esBase = ReferenceEquals(mod, versionBase);
                if (!esBase && !string.IsNullOrEmpty(mod.ProjectId) && yaInstalados.Contains(mod.ProjectId)) continue;

                if (!ModpacksApi.EsUrlPermitida(mod.UrlDescarga))
                    throw new InvalidOperationException($"Host de descarga no permitido: {mod.UrlDescarga}");

                string? destino = ModpacksApi.ResolverRutaSegura(carpetaMods, mod.NombreArchivo);
                if (destino == null)
                    throw new InvalidOperationException($"Nombre de archivo inválido: {mod.NombreArchivo}");

                if (File.Exists(destino)) continue;

                string desactivado = destino + ".disabled";
                if (File.Exists(desactivado))
                {
                    File.Move(desactivado, destino);
                    continue;
                }

                pendientes.Add((mod, destino));
            }

            await Parallel.ForEachAsync(pendientes,
                new ParallelOptions { MaxDegreeOfParallelism = 3 },
                async (item, ct) => await AppHttpClient.DescargarArchivoAsync(item.Mod.UrlDescarga, item.Destino, ct: ct));

            return plan.SinVersionCompatible;
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

                _modsLocales = ListarJarsDeCarpeta(path)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new ModLocal(f))
                    .ToList();

                listaModsInstaladosGestor.ItemsSource = _modsLocales;
                txtVacioInstalados.Visibility = _modsLocales.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ActualizarContadorInstalados();
            }
            catch { }
        }

        private void ActualizarContadorInstalados()
        {
            int activos = _modsLocales.Count(m => m.Habilitado);
            txtContadorInstalados.Text = Core.T("Mods_Contador", activos, _modsLocales.Count - activos);
        }

        private void SwitchMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not ModLocal mod) return;

            if (mod.Alternar(out string? error))
                ActualizarContadorInstalados();
            else
                MostrarToast(Core.T("Msg_ErrorAlternarMod", mod.Titulo, error ?? ""), false, true);
        }

        private async void BtnEliminarModLocal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not ModLocal mod) return;

            var respuesta = VentanaMensaje.Mostrar(Core.T("Msg_ConfirmarEliminarMod", mod.Titulo), null, MessageBoxButton.YesNo);
            if (respuesta != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(mod.RutaCompleta);
            }
            catch (Exception ex)
            {
                MostrarToast(Core.T("Msg_ErrorEliminarMod", mod.Titulo, ex.Message), false, true);
                return;
            }

            MostrarToast(Core.T("Msg_ModEliminado", mod.Titulo));

            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                CargarModsLocal(p);
                await RefrescarInstaladosOnlineAsync(p, soloMarcar: false);
            }
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
            var modpackSeleccionado = btn?.Tag as ModpackProject;

            if (_modpackInstalando)
            {
                VentanaMensaje.Mostrar(Core.T("Msg_InstalacionEnCurso"));
                return;
            }

            int token = ++_detalleModpackToken;
            btnInstalarModpack.Tag = null;
            comboVersionesModpack.ItemsSource = null;
            PanelProgresoDescarga.Visibility = System.Windows.Visibility.Collapsed;
            RestablecerBotonModpack();

            if (modpackSeleccionado != null)
            {
                txtDetalleTitulo.Text = modpackSeleccionado.Titulo;
                txtDetalleAutor.Text = Core.T("Txt_PorAutor", modpackSeleccionado.Autor);

                bool webViewListo = await AsegurarWebView2Async(wvDetalleDescripcion);
                if (webViewListo)
                {
                    ConfigurarWebViewDetalle();
                    wvDetalleDescripcion.NavigateToString("<body style='background-color:#151515; color:#AAAAAA; font-family:sans-serif; text-align:center; padding-top:20px;'>Cargando toda la información...</body>");
                }
                else if (!_avisoWebViewMostrado)
                {
                    _avisoWebViewMostrado = true;
                    VentanaMensaje.Mostrar(Core.T("Msg_WebView2Falta"));
                }

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
                if (token != _detalleModpackToken) return;
                var pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
                string htmlCuerpo = Markdig.Markdown.ToHtml(descripcionGigante ?? "", pipeline);

                string htmlCompleto = $@"
<!DOCTYPE html>
<html>
<head>
    <meta http-equiv=""Content-Security-Policy"" content=""default-src 'none'; img-src https: data:; style-src 'unsafe-inline';"">
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

                if (webViewListo) wvDetalleDescripcion.NavigateToString(htmlCompleto);

                var versiones = await ModpacksApi.ObtenerVersionesAsync(modpackSeleccionado.Id);
                if (token != _detalleModpackToken) return;

                btnInstalarModpack.Tag = modpackSeleccionado;
                comboVersionesModpack.ItemsSource = versiones;

                if (versiones.Count > 0) comboVersionesModpack.SelectedIndex = 0;
                else btnInstalarModpack.IsEnabled = false;
            }
        }

        private void RestablecerBotonModpack()
        {
            btnInstalarModpack.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "Txt_Instalar");
            btnInstalarModpack.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27ae60"));
            btnInstalarModpack.IsEnabled = true;
        }

        private void ComboVersionesModpack_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_modpackInstalando) return;
            RestablecerBotonModpack();
            PanelProgresoDescarga.Visibility = System.Windows.Visibility.Collapsed;
        }
        private void ConfigurarWebViewDetalle()
        {
            if (_webViewDetalleConfigurado || wvDetalleDescripcion.CoreWebView2 == null) return;
            _webViewDetalleConfigurado = true;

            var core = wvDetalleDescripcion.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;

            core.NavigationStarting += (s, ev) =>
            {
                if (!ev.IsUserInitiated) return;
                ev.Cancel = true;
                AbrirEnlaceExterno(ev.Uri);
            };
            core.NewWindowRequested += (s, ev) =>
            {
                ev.Handled = true;
                AbrirEnlaceExterno(ev.Uri);
            };
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
            if (versionSeleccionada == null || modpackSeleccionado == null || versionSeleccionada.Archivos.Count == 0) return;
            if (_modpackInstalando) return;

            _modpackInstalando = true;
            btnInstalarModpack.IsEnabled = false;
            comboVersionesModpack.IsEnabled = false;
            btnVolverModpacks.IsEnabled = false;
            btnInstalarModpack.Content = Core.T("Txt_Preparando");
            PanelProgresoDescarga.Visibility = System.Windows.Visibility.Visible;
            txtEstadoDescarga.Text = Core.T("Txt_ExtrayendoConfig");
            barraDescarga.Value = 0;

            bool instalado = false;
            try
            {
                var archivoZip = versionSeleccionada.Archivos.Find(a => a.EsPrimario) ?? versionSeleccionada.Archivos[0];
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string nombreCarpeta = modpackSeleccionado.Titulo;
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                    nombreCarpeta = nombreCarpeta.Replace(c, '_');

                string carpetaInstances = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances");
                string nombreUnico = nombreCarpeta;
                int copia = 1;
                while (System.IO.Directory.Exists(System.IO.Path.Combine(carpetaInstances, nombreUnico))
                       || Core.Perfiles.Any(p => string.Equals(p.Nombre, nombreUnico, StringComparison.OrdinalIgnoreCase)))
                {
                    nombreUnico = $"{nombreCarpeta} ({copia++})";
                }
                string rutaBaseMinecraft = System.IO.Path.Combine(carpetaInstances, nombreUnico);

                var receta = await ModpacksApi.PrepararInstalacionModpackAsync(archivoZip.UrlDescarga, rutaBaseMinecraft);
                if (receta == null)
                {
                    PanelProgresoDescarga.Visibility = System.Windows.Visibility.Collapsed;
                    VentanaMensaje.Mostrar(Core.T("Msg_ErrorModpackLeer"), Core.T("Txt_TituloError"));
                    return;
                }

                int totalArchivos = receta.ArchivosMod?.Count ?? 0;
                barraDescarga.Maximum = Math.Max(1, totalArchivos);
                btnInstalarModpack.Content = Core.T("Txt_Descargando");

                var progreso = new Progress<(int actual, int total, string nombre)>(p =>
                {
                    barraDescarga.Value = p.actual;
                    txtEstadoDescarga.Text = Core.T("Txt_DescargandoProgreso", p.actual, p.total, p.nombre);
                });
                var fallos = await ModpacksApi.DescargarArchivosModpackAsync(receta, rutaBaseMinecraft, progreso);

                var (loaderUsado, versionDelLoader) = ModpacksApi.DetectarLoader(receta.Dependencias);

                string versionMc = "";
                receta.Dependencias?.TryGetValue("minecraft", out versionMc);
                if (string.IsNullOrEmpty(versionMc))
                    versionMc = versionSeleccionada.VersionesMinecraft.FirstOrDefault() ?? "";

                var existente = Core.Perfiles.Find(p => string.Equals(p.RutaCarpeta, rutaBaseMinecraft, StringComparison.OrdinalIgnoreCase));
                if (existente != null)
                {
                    existente.Version = versionMc;
                    existente.TipoLoader = loaderUsado;
                    existente.VersionLoaderExacta = versionDelLoader;
                    existente.IconoPath = modpackSeleccionado.IconoUrl;
                }
                else
                {
                    Core.Perfiles.Add(new Perfil
                    {
                        Nombre = nombreUnico,
                        Version = versionMc,
                        RutaCarpeta = rutaBaseMinecraft,
                        TipoLoader = loaderUsado,
                        VersionLoaderExacta = versionDelLoader,
                        MemoriaRam = 4096,
                        IconoPath = modpackSeleccionado.IconoUrl
                    });
                }

                Core.GuardarPerfiles();
                ActualizarListaPerfiles();
                instalado = true;

                barraDescarga.Value = barraDescarga.Maximum;
                txtEstadoDescarga.Text = Core.T(fallos.Count == 0 ? "Txt_InstalacionCompletada" : "Txt_InstalacionConErrores");
                btnInstalarModpack.Content = Core.T("Txt_Instalado");
                btnInstalarModpack.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D"));

                if (fallos.Count == 0)
                {
                    VentanaMensaje.Mostrar(Core.T("Msg_ModpackOk", modpackSeleccionado.Titulo), Core.T("Txt_TituloExito"));
                }
                else
                {
                    string lista = string.Join("\n", fallos.Take(8)) + (fallos.Count > 8 ? $"\n... y {fallos.Count - 8} más" : "");
                    VentanaMensaje.Mostrar(Core.T("Msg_ModpackIncompleto", modpackSeleccionado.Titulo, fallos.Count, lista), Core.T("Txt_TituloInstalacionIncompleta"));
                }
            }
            catch (Exception ex)
            {
                PanelProgresoDescarga.Visibility = System.Windows.Visibility.Collapsed;
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorModpack", ex.Message), Core.T("Txt_TituloError"));
            }
            finally
            {
                _modpackInstalando = false;
                comboVersionesModpack.IsEnabled = true;
                btnVolverModpacks.IsEnabled = true;
                if (!instalado) RestablecerBotonModpack();
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
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            AplicarSnapping();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNativo { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoNativo
        {
            public int cbSize;
            public RectNativo rcMonitor;
            public RectNativo rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoNativo lpmi);

        private void AplicarSnapping()
        {
            const double distanciaIman = 20.0;

            if (this.WindowState != WindowState.Normal) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MonitorInfoNativo { cbSize = Marshal.SizeOf<MonitorInfoNativo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double izquierda = info.rcWork.Left / dpi.DpiScaleX;
            double arriba = info.rcWork.Top / dpi.DpiScaleY;
            double derecha = info.rcWork.Right / dpi.DpiScaleX;
            double abajo = info.rcWork.Bottom / dpi.DpiScaleY;

            double ancho = this.ActualWidth;
            double alto = this.ActualHeight;

            if (Math.Abs(this.Left - izquierda) < distanciaIman)
                this.Left = izquierda;

            if (Math.Abs(this.Top - arriba) < distanciaIman)
                this.Top = arriba;

            if (Math.Abs((this.Left + ancho) - derecha) < distanciaIman)
                this.Left = derecha - ancho;

            if (Math.Abs((this.Top + alto) - abajo) < distanciaIman)
                this.Top = abajo - alto;
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
            if (comboIdiomas == null || comboIdiomas.Items == null)
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
                if (datos == null) return;

                if (!UpdateService.HayActualizacion(datos.VersionMasReciente, VERSION_ACTUAL)) return;

                var res = VentanaMensaje.Mostrar(Core.T("Msg_NuevaVersion", datos.VersionMasReciente), Core.T("Txt_TituloActualizacion"), MessageBoxButton.YesNo);

                if (res == MessageBoxResult.Yes)
                {
                    PanelCarga.Visibility = Visibility.Visible;
                    barraCarga.IsIndeterminate = true;
                    txtEstadoCarga.Text = Core.T("Txt_PreparandoActualizacion");

                    try
                    {
                        await UpdateService.InstalarDesdeZipAsync(
                            datos.LinkDescarga,
                            datos.Sha256,
                            new Progress<string>(msg => txtEstadoCarga.Text = msg)
                        );
                    }
                    catch
                    {
                        PanelCarga.Visibility = Visibility.Collapsed;
                        throw;
                    }
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
                    VentanaMensaje.Mostrar(ex.Message, Core.T("Txt_TituloActualizacionAbortada"), MessageBoxButton.OK);
                }
                else
                {
                    VentanaMensaje.Mostrar(Core.T("Msg_ErrorGenerico", ex.Message), Core.T("Txt_TituloError"), MessageBoxButton.OK);
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
            if (listaNoticias == null || indice < 0 || indice >= listaNoticias.Count) return;

            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en ActualizarNoticiaConEfecto: " + ex.Message);
            }
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
                bool urlValida = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                if (!urlValida || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uriSegura)) return;

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uriSegura.AbsoluteUri,
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
            if (listaNoticias == null || listaNoticias.Count == 0) return;

            indiceActual--;
            if (indiceActual < 0)
            {
                indiceActual = listaNoticias.Count - 1;
            }
            ActualizarNoticiaConEfecto(indiceActual);
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (listaNoticias == null || listaNoticias.Count == 0) return;

            indiceActual++;
            if (indiceActual >= listaNoticias.Count) indiceActual = 0;
            ActualizarNoticiaConEfecto(indiceActual);
        }
        #endregion
        #region TecniClients
        private List<TecniClientModel>? _clientesCache;
        private DateTime _clientesUltimaCarga = DateTime.MinValue;
        private Dictionary<string, double> _ramClientes = new();
        private string RutaRamClientes => System.IO.Path.Combine(Core.RutaData, "tecniclient_ram.json");

        private void CargarRamClientes()
        {
            try
            {
                if (System.IO.File.Exists(RutaRamClientes))
                    _ramClientes = JsonSerializer.Deserialize<Dictionary<string, double>>(System.IO.File.ReadAllText(RutaRamClientes)) ?? new();
            }
            catch { _ramClientes = new(); }
        }

        private void GuardarRamCliente(TecniClientModel c)
        {
            try
            {
                if (string.IsNullOrEmpty(c.Id)) return;
                _ramClientes[c.Id] = c.SelectedRam;
                System.IO.File.WriteAllText(RutaRamClientes, JsonSerializer.Serialize(_ramClientes));
            }
            catch { }
        }

        private static string LeerVersionInstalada(string id)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string f = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances", id, "version.txt");
                return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f).Trim() : "";
            }
            catch { return ""; }
        }

        private void RefrescarEstadoClientes()
        {
            if (_clientesCache == null) return;
            foreach (var c in _clientesCache)
            {
                if (!c.Ocupado) c.VersionInstalada = LeerVersionInstalada(c.Id);
                c.RefrescarTextos();
            }
        }

        public async Task CargarClientesDesdeInternet()
        {
            if (_clientesCache != null && (DateTime.Now - _clientesUltimaCarga).TotalMinutes < 10)
            {
                RefrescarEstadoClientes();
                return;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = "https://raw.githubusercontent.com/Tecnikero/TecniLauncher-Data/refs/heads/main/tecniclient/tecniclients.json";
                    string json = await client.GetStringAsync(url);

                    var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var nuevos = JsonSerializer.Deserialize<List<TecniClientModel>>(json, opciones) ?? new();

                    CargarRamClientes();
                    for (int i = 0; i < nuevos.Count; i++)
                    {
                        var previo = _clientesCache?.Find(x => x.Id == nuevos[i].Id);
                        if (previo != null && previo.Ocupado)
                        {
                            var n = nuevos[i];
                            previo.Version = n.Version;
                            previo.Description = n.Description;
                            previo.ModpackUrl = n.ModpackUrl;
                            previo.MinecraftVersion = n.MinecraftVersion;
                            previo.Loader = n.Loader;
                            previo.LoaderVersion = n.LoaderVersion;
                            previo.Mods = n.Mods;
                            nuevos[i] = previo;
                        }
                    }

                    foreach (var n in nuevos)
                    {
                        if (_ramClientes.TryGetValue(n.Id ?? "", out double gb)) n.SelectedRam = Math.Clamp(gb, 1, 16);
                        n.RamCambiada = GuardarRamCliente;
                        if (!n.Ocupado) n.VersionInstalada = LeerVersionInstalada(n.Id);
                    }

                    _clientesCache = nuevos;
                    _clientesUltimaCarga = DateTime.Now;
                    ListTecniClients.ItemsSource = _clientesCache;
                }
            }
            catch (Exception ex)
            {
                if (_clientesCache != null) { RefrescarEstadoClientes(); return; }
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorClientes", ex.Message), Core.T("Txt_TituloError"), MessageBoxButton.OK);
            }
        }

        private async void BtnInstalarTecniClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            if (btn.DataContext is not TecniClientModel clienteSeleccionado) return;

            if (_tecniClientOcupado)
            {
                VentanaMensaje.Mostrar(Core.T("Msg_InstalacionEnCurso"));
                return;
            }

            _tecniClientOcupado = true;
            clienteSeleccionado.Ocupado = true;
            clienteSeleccionado.TextoOcupado = Core.T("Txt_Iniciando");
            bool esperandoCierreJuego = false;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string carpetaInstancia = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances", clienteSeleccionado.Id);
            string carpetaMods = System.IO.Path.Combine(carpetaInstancia, "mods");
            string carpetaModsNuevos = carpetaMods + ".new";
            string carpetaModsViejos = carpetaMods + ".old";
            string archivoZipTemp = System.IO.Path.Combine(carpetaInstancia, "modpack_temp.zip");
            string archivoVersion = System.IO.Path.Combine(carpetaInstancia, "version.txt");

            try
            {
                string versionLocal = "";
                if (System.IO.File.Exists(archivoVersion)) versionLocal = System.IO.File.ReadAllText(archivoVersion).Trim();
                System.IO.Directory.CreateDirectory(carpetaInstancia);

                if (versionLocal != clienteSeleccionado.Version)
                {
                    if (!ModpacksApi.EsUrlTecniClientPermitida(clienteSeleccionado.ModpackUrl))
                        throw new InvalidOperationException($"Host de descarga no permitido: {clienteSeleccionado.ModpackUrl}");

                    clienteSeleccionado.TextoOcupado = Core.T("Txt_DescargandoMods");
                    PanelCarga.Visibility = Visibility.Visible;
                    txtEstadoCarga.Text = Core.T("Txt_DescargandoCliente", clienteSeleccionado.Name);
                    barraCarga.IsIndeterminate = true;

                    if (System.IO.Directory.Exists(carpetaModsNuevos)) System.IO.Directory.Delete(carpetaModsNuevos, true);
                    await AppHttpClient.DescargarArchivoAsync(clienteSeleccionado.ModpackUrl, archivoZipTemp, TimeSpan.FromMinutes(15));
                    await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(archivoZipTemp, carpetaModsNuevos, true));
                    System.IO.File.Delete(archivoZipTemp);

                    if (System.IO.Directory.Exists(carpetaModsViejos)) System.IO.Directory.Delete(carpetaModsViejos, true);
                    if (System.IO.Directory.Exists(carpetaMods)) System.IO.Directory.Move(carpetaMods, carpetaModsViejos);
                    try
                    {
                        System.IO.Directory.Move(carpetaModsNuevos, carpetaMods);
                    }
                    catch
                    {
                        if (!System.IO.Directory.Exists(carpetaMods) && System.IO.Directory.Exists(carpetaModsViejos))
                            System.IO.Directory.Move(carpetaModsViejos, carpetaMods);
                        throw;
                    }
                    try { if (System.IO.Directory.Exists(carpetaModsViejos)) System.IO.Directory.Delete(carpetaModsViejos, true); } catch { }

                    System.IO.File.WriteAllText(archivoVersion, clienteSeleccionado.Version);
                    clienteSeleccionado.VersionInstalada = clienteSeleccionado.Version;
                }

                clienteSeleccionado.TextoOcupado = Core.T("Txt_Iniciando");
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

                System.Diagnostics.Process? procesoMinecraft = await LanzarMinecraft(perfilCliente);

                PanelCarga.Visibility = Visibility.Collapsed;

                if (procesoMinecraft != null)
                {
                    esperandoCierreJuego = true;
                    clienteSeleccionado.TextoOcupado = Core.T("Txt_Jugando");

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
                            clienteSeleccionado.TextoOcupado = "";
                            clienteSeleccionado.Ocupado = false;
                            _tecniClientOcupado = false;

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
            }
            catch (Exception ex)
            {
                try { if (System.IO.File.Exists(archivoZipTemp)) System.IO.File.Delete(archivoZipTemp); } catch { }
                try { if (System.IO.Directory.Exists(carpetaModsNuevos)) System.IO.Directory.Delete(carpetaModsNuevos, true); } catch { }
                PanelCarga.Visibility = Visibility.Collapsed;
                VentanaMensaje.Mostrar(Core.T("Msg_ErrorGenerico", ex.Message), Core.T("Txt_TituloError"), MessageBoxButton.OK);
            }
            finally
            {
                PanelCarga.Visibility = Visibility.Collapsed;
                barraCarga.IsIndeterminate = false;
                if (!esperandoCierreJuego)
                {
                    clienteSeleccionado.TextoOcupado = "";
                    clienteSeleccionado.Ocupado = false;
                    clienteSeleccionado.VersionInstalada = LeerVersionInstalada(clienteSeleccionado.Id);
                    _tecniClientOcupado = false;
                }
            }
        }
        #endregion
    }
}