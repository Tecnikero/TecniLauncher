using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.ProcessBuilder;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Net.WebRequestMethods;
using static TecniLauncher.ModpacksApi;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Path = System.IO.Path;

namespace TecniLauncher
{
    #region Modelos de Datos
    public class Noticia
    {
        public string Titulo { get; set; }
        public string Imagen { get; set; }
        public string Cuerpo { get; set; }
        public bool MostrarBoton { get; set; }
        public string BotonTexto { get; set; }
        public string BotonUrl { get; set; }
        public string BotonColor { get; set; } = "#5865F2";
    }

    public class DatosUpdate
    {
        public string VersionMasReciente { get; set; }
        public bool EsCritica { get; set; }
        public string LinkDescarga { get; set; }
        public string Novedades { get; set; }
        private bool modoOnline = false;
    }
    #endregion

    public partial class MainWindow : Window
    {
        #region Variables Privadas y Estado
        private bool esPremium = false;
        private Perfil perfilAEditar = null;
        private bool modoOnline = false;
        private List<Noticia> listaNoticias = new List<Noticia>();
        private int indiceActual = 0;
        private const string VERSION_ACTUAL = "1.3.2";
        private CancellationTokenSource ctsActualizacion;
        private bool estaCargando = false;
        private DispatcherTimer _timerNoticias;


        private static readonly HttpClient _httpClient = new HttpClient();
        #endregion
        public MainWindow()
        {
            InitializeComponent();
            InicializarLauncher();
        }

        private void InicializarLauncher()
        {
            Core.Inicializar();
            Core.CargarConfiguracion();
            Core.CambiarIdioma(Core.IdiomaActual);
            SeleccionarIdiomaEnCombo();

            ComprobarActualizaciones();
            CargarEventosSegunLinks();
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
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CargarVersionesVanilla();
            ActualizarListaPerfiles();
            Core.CargarConfiguracion();
            ConfigurarRamAutomatica();

            txtUsuario.Text = "Verificando sesión...";
            bool exito = await Core.IntentarAutoLogin();

            if (exito)
            {
                esPremium = true;
                txtUsuario.Text = Core.SesionUsuario.Username;
                txtOfflineName.Text = Core.SesionUsuario.Username;
                txtOfflineName.IsEnabled = false;

                btnMicrosoft.Content = "Cerrar Sesión";
                btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));

                CargarSkinEnInterfaz(Core.SesionUsuario.UUID);
            }
            else
            {
                txtUsuario.Text = Core.UltimoNombreOffline;
                CargarSkinEnInterfaz(Core.UltimoNombreOffline);
            }
            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Maximized) btnMaximizar.Content = "❐";
                else btnMaximizar.Content = "☐";
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
        private void MoverVentana_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); }
        private void Cerrar_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void Maximizar_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                btnMaximizar.Content = "☐";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                btnMaximizar.Content = "❐";
            }
        }
        private void Minimizar_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void AbrirLogin_Click(object sender, RoutedEventArgs e) => GridLogin.Visibility = Visibility.Visible;
        private void BtnCerrarLogin_Click(object sender, RoutedEventArgs e) => GridLogin.Visibility = Visibility.Collapsed;

        private void MenuJugar_Click(object sender, RoutedEventArgs e)
        {
            if (VistaJugar == null) return;
            VistaJugar.Visibility = Visibility.Visible;
            VistaPerfiles.Visibility = Visibility.Collapsed;
            VistaAjustes.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Collapsed;
            VistaEventos.Visibility = Visibility.Collapsed;
            VistaModpacks.Visibility = Visibility.Collapsed;
            CargarNoticiasGitHub();
        }
        private void MenuPerfiles_Click(object sender, RoutedEventArgs e)
        {
            VistaJugar.Visibility = Visibility.Collapsed;
            VistaPerfiles.Visibility = Visibility.Visible;
            VistaAjustes.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Collapsed;
            VistaEventos.Visibility = Visibility.Collapsed;
            VistaModpacks.Visibility = Visibility.Collapsed;
        }
        private void MenuMods_Click(object sender, RoutedEventArgs e)
        {
            VistaJugar.Visibility = Visibility.Collapsed;
            VistaPerfiles.Visibility = Visibility.Collapsed;
            VistaAjustes.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Visible;
            VistaEventos.Visibility = Visibility.Collapsed;
            VistaModpacks.Visibility = Visibility.Collapsed;

            comboPerfilesMods.ItemsSource = null;
            comboPerfilesMods.ItemsSource = Core.Perfiles;
            if (comboPerfilesMods.Items.Count > 0)
            {
                comboPerfilesMods.SelectedIndex = 0;
                BtnBuscarOnline_Click(null, null);
            }

        }
        private void MenuEventos_Click(object sender, RoutedEventArgs e)
        {
            VistaJugar.Visibility = Visibility.Collapsed;
            VistaPerfiles.Visibility = Visibility.Collapsed;
            VistaAjustes.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Collapsed;
            VistaEventos.Visibility = Visibility.Visible;
            VistaModpacks.Visibility = Visibility.Collapsed;
            CargarEventosSegunLinks();
        }
        private void MenuModpacks_Click(object sender, RoutedEventArgs e)
        {
            VistaEventos.Visibility = Visibility.Collapsed;
            VistaJugar.Visibility = Visibility.Collapsed;
            VistaPerfiles.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Collapsed;
            VistaAjustes.Visibility = Visibility.Collapsed;
            VistaModpacks.Visibility = Visibility.Visible;
        }

        private void MenuAjustes_Click(object sender, RoutedEventArgs e)
        {
            VistaJugar.Visibility = Visibility.Collapsed;
            VistaPerfiles.Visibility = Visibility.Collapsed;
            VistaMods.Visibility = Visibility.Collapsed;
            VistaAjustes.Visibility = Visibility.Visible;
            VistaEventos.Visibility = Visibility.Collapsed;
            VistaModpacks.Visibility = Visibility.Collapsed;
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
            string rutaCacheSesion = Core.RutaSesion;

            if (esPremium)
            {
                try
                {
                    var loginHandler = new JELoginHandlerBuilder().WithAccountManager(rutaCacheSesion).Build();
                    loginHandler.Signout();
                    if (File.Exists(rutaCacheSesion)) File.Delete(rutaCacheSesion);
                }
                catch { }

                esPremium = false;
                Core.SesionUsuario = null;
                btnMicrosoft.Content = "Iniciar con Microsoft";
                btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(0, 93, 166));
                txtOfflineName.IsEnabled = true;
                txtOfflineName.Text = "Jugador";
                txtUsuario.Text = "Sin Sesión";
                CargarSkinEnInterfaz(null);
                VentanaMensaje.Mostrar("Sesión cerrada.");
                return;
            }

            try
            {
                btnMicrosoft.IsEnabled = false;
                btnMicrosoft.Content = "Iniciando...";

                var loginHandler = new JELoginHandlerBuilder().WithAccountManager(rutaCacheSesion).Build();
                var resultado = await loginHandler.AuthenticateInteractively();

                Core.SesionUsuario = resultado;
                esPremium = true;
                CargarSkinEnInterfaz(resultado.Username);

                btnMicrosoft.Content = "Cerrar Sesión";
                btnMicrosoft.Background = new SolidColorBrush(Color.FromRgb(200, 50, 50));
                txtOfflineName.Text = resultado.Username;
                txtOfflineName.IsEnabled = false;
                txtUsuario.Text = resultado.Username;

                this.Activate();
                GridLogin.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Login cancelado: " + ex.Message);
                btnMicrosoft.Content = "Iniciar con Microsoft";
            }
            finally { btnMicrosoft.IsEnabled = true; }
        }

        private void BtnAbrirElyBy_Click(object sender, RoutedEventArgs e)
        {
            PanelLoginPrincipal.Visibility = Visibility.Collapsed;
            PanelLoginElyBy.Visibility = Visibility.Visible;

            txtUsuarioElyBy.Text = "";
            txtPasswordElyBy.Password = "";
            txtEstadoElyBy.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelarElyBy_Click(object sender, RoutedEventArgs e)
        {
            PanelLoginElyBy.Visibility = Visibility.Collapsed;
            PanelLoginPrincipal.Visibility = Visibility.Visible;
        }
        private void BtnEntrarLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtOfflineName.Text)) return;
            Core.UltimoNombreOffline = txtOfflineName.Text;
            Core.GuardarConfiguracion();
            txtUsuario.Text = txtOfflineName.Text;
            CargarSkinEnInterfaz(txtOfflineName.Text);
            GridLogin.Visibility = Visibility.Collapsed;
        }

        private async void BtnLoginElyBy_Click(object sender, RoutedEventArgs e)
        {
            string usuario = txtUsuarioElyBy.Text.Trim();
            string password = txtPasswordElyBy.Password;

            if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(password))
            {
                txtEstadoElyBy.Text = "Por favor, llena todos los campos.";
                txtEstadoElyBy.Visibility = Visibility.Visible;
                return;
            }

            txtEstadoElyBy.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4facfe"));
            txtEstadoElyBy.Text = "Conectando con Ely.by...";
            txtEstadoElyBy.Visibility = Visibility.Visible;
            btnLoginElyBy.IsEnabled = false;

            try
            {
                var sesionElyBy = await IniciarSesionElyByOficial(usuario, password);

                if (sesionElyBy != null)
                {
                    Core.SesionUsuario = sesionElyBy;
                    Core.EsElyBy = true;
                    Core.UltimoNombreOffline = sesionElyBy.Username;

                    this.esPremium = false;

                    GridLogin.Visibility = Visibility.Collapsed;

                    CargarSkinEnInterfaz(sesionElyBy.Username);
                }
                else
                {

                    txtEstadoElyBy.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5656"));
                    txtEstadoElyBy.Text = "Correo o contraseña incorrectos.";
                }
            }
            catch (Exception)
            {
                txtEstadoElyBy.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5656"));
                txtEstadoElyBy.Text = "Error de conexión con los servidores.";
            }
            finally
            {
                btnLoginElyBy.IsEnabled = true;
            }
        }

        private async Task<MSession> IniciarSesionElyByOficial(string usuarioElyBy, string contrasenaElyBy)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher/1.0");

                    var requestData = new
                    {
                        agent = new { name = "Minecraft", version = 1 },
                        username = usuarioElyBy,
                        password = contrasenaElyBy,
                        clientToken = Guid.NewGuid().ToString("N")
                    };

                    string jsonContent = JsonConvert.SerializeObject(requestData);
                    var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://authserver.ely.by/auth/authenticate", content);
                    string jsonRespuesta = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic datosAuth = JsonConvert.DeserializeObject(jsonRespuesta);

                        return new MSession
                        {
                            Username = datosAuth.selectedProfile.name,
                            UUID = datosAuth.selectedProfile.id,
                            AccessToken = datosAuth.accessToken,
                            ClientToken = datosAuth.clientToken,
                            UserType = "mojang"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error de red con Ely.by: " + ex.Message);
            }

            return null;
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
                {
                    skinBitmap = new BitmapImage(new Uri("pack://application:,,,/Resources/steve.png"));
                }

                imgAvatar.Fill = SkinUtils.RecortarParte(skinBitmap, 8, 8, 8, 8);

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error visualizando skin: " + ex.Message);
            }
        }

        private void ZonaSkin_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ely.by/authorization",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void ZonaSkin_Drop(object sender, DragEventArgs e) { }

        private void BtnBorrarSkin_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(txtUsuario.Text))
            {
                VentanaMensaje.Mostrar("Recargando skin desde la nube...");
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

            try
            {
                btnJugar.IsEnabled = false;
                PanelCarga.Visibility = Visibility.Visible;

                System.Diagnostics.Process procesoMinecraft = await LanzarMinecraft(perfil);
                PanelCarga.Visibility = Visibility.Collapsed;

                if (procesoMinecraft != null)
                {
                    DateTime tiempoInicio = DateTime.Now;

                    procesoMinecraft.EnableRaisingEvents = true;
                    await Task.Run(() => procesoMinecraft.WaitForExit());

                    DateTime tiempoFinal = DateTime.Now;
                    long segundosJugados = (long)(tiempoFinal - tiempoInicio).TotalSeconds;

                    perfil.SegundosJugados += segundosJugados;

                    Core.GuardarPerfiles();
                    ActualizarListaPerfiles();
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

                var pathHibrido = new MinecraftPath(perfil.RutaCarpeta);
                string rutaGlobal = Core.RutaGlobal;

                pathHibrido.Assets = System.IO.Path.Combine(rutaGlobal, "assets");
                pathHibrido.Library = System.IO.Path.Combine(rutaGlobal, "libraries");
                pathHibrido.Versions = System.IO.Path.Combine(rutaGlobal, "versions");
                pathHibrido.Runtime = System.IO.Path.Combine(rutaGlobal, "runtime");

                var launcher = new MinecraftLauncher(pathHibrido);

                if (Core.SesionUsuario == null)
                {
                    string nombreFinal = string.IsNullOrEmpty(Core.UltimoNombreOffline) ? "Jugador" : Core.UltimoNombreOffline;
                    Core.SesionUsuario = MSession.CreateOfflineSession(nombreFinal);
                }

                if (Core.EsElyBy)
                {
                    Dispatcher.Invoke(() => txtEstadoCarga.Text = "Obteniendo datos de Ely.by...");

                    string uuidReal = await ObtenerUuidElyBy(Core.SesionUsuario.Username);

                    if (!string.IsNullOrEmpty(uuidReal))
                    {
                        Core.SesionUsuario = new MSession
                        {
                            Username = Core.SesionUsuario.Username,
                            UUID = uuidReal,
                            AccessToken = "token_elyby",
                            UserType = "mojang"
                        };
                    }
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

                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = perfil.MemoriaRam,
                    Session = Core.SesionUsuario,
                    ScreenWidth = Core.JuegoAncho,
                    ScreenHeight = Core.JuegoAlto,
                    FullScreen = Core.PantallaCompleta,
                };
                if (perfil.TipoLoader == "Vanilla" && perfil.ModoRendimientoActivado)
                {
                    string[] banderasAikar = new string[]
                    {
        "-XX:+UseG1GC",
        "-XX:+ParallelRefProcEnabled",
        "-XX:MaxGCPauseMillis=200",
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:+DisableExplicitGC",
        "-XX:G1NewSizePercent=30",
        "-XX:G1MaxNewSizePercent=40",
        "-XX:G1HeapRegionSize=8M",
        "-XX:G1ReservePercent=20",
        "-XX:G1HeapWastePercent=5",
        "-XX:G1MixedGCCountTarget=4",
        "-XX:InitiatingHeapOccupancyPercent=15",
        "-XX:G1MixedGCLiveThresholdPercent=90",
        "-XX:G1RSetUpdatingPauseTimePercent=5",
        "-XX:SurvivorRatio=32",
        "-XX:+PerfDisableSharedMem",
        "-XX:MaxTenuringThreshold=1"
                    };

                    var argumentosExtra = new System.Collections.Generic.List<CmlLib.Core.ProcessBuilder.MArgument>();

                    foreach (string bandera in banderasAikar)
                    {
                        argumentosExtra.Add(new CmlLib.Core.ProcessBuilder.MArgument(bandera));
                    }

                    launchOption.ExtraJvmArguments = argumentosExtra;

                    Dispatcher.Invoke(() => txtEstadoCarga.Text = "Inyectando optimizaciones de memoria Aikar...");
                }

                var process = await launcher.CreateProcessAsync(idVersion, launchOption);

                if (Core.EsElyBy)
                {
                    string rutaJar = await PrepararAuthlibInjector(Core.RutaGlobal);

                    if (!string.IsNullOrEmpty(rutaJar))
                    {
                        process.StartInfo.Arguments = $"-javaagent:\"{rutaJar}\"=https://authserver.ely.by/api/authlib-injector " + process.StartInfo.Arguments;
                    }
                }

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
                string rutaInjector = System.IO.Path.Combine(carpetaBase, "authlib-injector.jar");

                if (!File.Exists(rutaInjector))
                {
                    txtEstadoCarga.Text = "Descargando componentes de Skin...";
                    string url = "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar";

                    using (var client = new HttpClient())
                    {
                        var bytes = await client.GetByteArrayAsync(url);
                        File.WriteAllBytes(rutaInjector, bytes);
                    }
                }
                return rutaInjector;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error descargando authlib: " + ex.Message);
                return null;
            }
        }
        private async Task<string> ObtenerUuidElyBy(string nombreUsuario)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    string url = $"https://authserver.ely.by/api/profiles/minecraft/{nombreUsuario}";
                    var response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();

                        dynamic perfilEly = JsonConvert.DeserializeObject(json);
                        return perfilEly.id;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error obteniendo UUID de Ely.by: " + ex.Message);
            }
            return null;
        }

        private void StartProcess(Process process)
        {
            process.Start();
            if (chkOcultarLauncher.IsChecked == true) this.Hide();

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

            if (listaEventosUI != null && listaEventosUI.ItemsSource is IEnumerable<EventoModelo> eventos)
            {
                foreach (var ev in eventos)
                {
                    ev.MemoriaRam = (int)e.NewValue * 1024; 
                }
            }
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
            var btn = (Button)sender;
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
            var perfil = (Perfil)((Button)sender).Tag;
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
            var perfil = (Perfil)((Button)sender).Tag;
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
            if (PanelOnline == null || PanelInstalados == null) return;
            PanelOnline.Visibility = Visibility.Visible;
            PanelInstalados.Visibility = Visibility.Collapsed;

            listaModsGestor.ItemsSource = null;
            modoOnline = true;

            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                BtnBuscarOnline_Click(null, null);
            }
        }

        private void TabInstaladosBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelOnline == null || PanelInstalados == null) return;
            PanelOnline.Visibility = Visibility.Collapsed;
            PanelInstalados.Visibility = Visibility.Visible;

            modoOnline = false;
            if (comboPerfilesMods.SelectedItem is Perfil p)
                CargarModsLocal(p);
        }

        private async void BtnBuscarOnline_Click(object sender, RoutedEventArgs e)
        {
            if (comboPerfilesMods.SelectedItem is not Perfil p) return;

            modoOnline = true;

            string busqueda = txtBuscadorMods?.Text ?? "";

            var res = await ModrinthAPI.BuscarMods(busqueda, p.TipoLoader, p.Version);

            foreach (var m in res)
            {
                if (m.title.ToLower().Contains("sodium") ||
                    m.title.ToLower().Contains("iris") ||
                    m.title.ToLower().Contains("lithium"))
                {
                    m.esRecomendado = true;
                }
                if (string.IsNullOrEmpty(m.icon_url))
                    m.icon_url = "https://cdn.modrinth.com/assets/icon.png";
            }

            listaModsGestor.ItemsSource = res.OrderByDescending(x => x.esRecomendado).ToList();
        }

        private async void BtnAccionMod_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var mod = (ModInfo)btn.Tag;

            if (!modoOnline)
            {
                try
                {
                    if (File.Exists(mod.project_id))
                    {
                        File.Delete(mod.project_id);
                        VentanaMensaje.Mostrar("Mod eliminado: " + mod.title);
                        if (comboPerfilesMods.SelectedItem is Perfil p) CargarModsLocal(p);
                    }
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Error al borrar: " + ex.Message);
                }
                return;
            }

            if (comboPerfilesMods.SelectedItem is Perfil perfilActual)
            {
                OverlayVersiones.Visibility = Visibility.Visible;
                listaArchivosVersion.ItemsSource = null;
                txtNombreModVersiones.Text = $"Buscando versiones de {mod.title}...";

                var versiones = await ModrinthAPI.ObtenerListaVersiones(mod.project_id, perfilActual.Version, perfilActual.TipoLoader);

                if (versiones.Count > 0)
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

        private async void BtnInstalarVersion_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var version = (ModVersion)btn.Tag;

            if (comboPerfilesMods.SelectedItem is Perfil p)
            {
                try
                {
                    string carpetaMods = Path.Combine(p.RutaCarpeta, "mods");
                    if (!Directory.Exists(carpetaMods)) Directory.CreateDirectory(carpetaMods);

                    string destino = Path.Combine(carpetaMods, version.NombreArchivo);

                    if (File.Exists(destino))
                    {
                        VentanaMensaje.Mostrar("Este mod ya está instalado.");
                        return;
                    }

                    VentanaMensaje.Mostrar($"Descargando {version.NombreArchivo}...");
                    OverlayVersiones.Visibility = Visibility.Collapsed;

                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher");
                        var datos = await client.GetByteArrayAsync(version.UrlDescarga);
                        File.WriteAllBytes(destino, datos);
                    }

                    VentanaMensaje.Mostrar("¡Mod instalado correctamente!");
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Error en la descarga: " + ex.Message);
                    OverlayVersiones.Visibility = Visibility.Visible;
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
        private async void BtnBuscarModpacks_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string busqueda = txtBuscadorModpacks.Text.Trim();
            if (string.IsNullOrEmpty(busqueda)) return;

            txtCargandoModpacks.Visibility = System.Windows.Visibility.Visible;
            listaModpacksUI.ItemsSource = null;
            btnBuscarModpacks.IsEnabled = false;

            var resultados = await ModpacksApi.BuscarModpacksAsync(busqueda, 20);

            listaModpacksUI.ItemsSource = resultados;

            txtCargandoModpacks.Visibility = System.Windows.Visibility.Collapsed;
            btnBuscarModpacks.IsEnabled = true;
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

                mdDetalleDescripcion.Markdown = "Cargando toda la información...";

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

                mdDetalleDescripcion.Markdown = descripcionGigante;

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
                string rutaBaseMinecraft = System.IO.Path.Combine(appData, ".TecniLauncher", "Instances", modpackSeleccionado.Titulo);

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

            if (VistaModpacks.Visibility == Visibility.Visible && listaModpacksUI.Items.Count == 0)
            {

                txtCargandoModpacks.Visibility = Visibility.Visible;


                var recomendados = await ModpacksApi.BuscarModpacksAsync("");

                listaModpacksUI.ItemsSource = recomendados;

                txtCargandoModpacks.Visibility = Visibility.Collapsed;
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
                using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
                {
                    string url = "https://raw.githubusercontent.com/johan12390785/TecniLauncher-Data/refs/heads/main/LauncherUpdate/version.json";

                    string json = await client.GetStringAsync(url);
                    var datos = Newtonsoft.Json.JsonConvert.DeserializeObject<DatosUpdate>(json);

                    if (datos.VersionMasReciente != VERSION_ACTUAL)
                    {
                        string mensaje = $"¡Nueva versión {datos.VersionMasReciente} disponible!\n\n" +
                                         "¿Quieres actualizar ahora automáticamente?";

                        var resultado = VentanaMensaje.Mostrar(mensaje, "ACTUALIZACIÓN DISPONIBLE", MessageBoxButton.YesNo);

                        if (resultado == MessageBoxResult.Yes)
                        {
                            InstalarActualizacion(datos.LinkDescarga);
                        }
                        else
                        {
                            if (datos.EsCritica)
                            {
                                VentanaMensaje.Mostrar("Esta actualización es obligatoria para seguir usando el Launcher. El programa se cerrará.", "ACTUALIZACIÓN REQUERIDA");
                                Application.Current.Shutdown();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error comprobando updates: " + ex.Message);
            }
        }

        private void MostrarAvisoUpdate(DatosUpdate datos)
        {
            string mensaje = $"¡Nueva versión {datos.VersionMasReciente} disponible!\n\n" +
                    $"Novedades: {datos.Novedades}\n\n" +
                    "¿Quieres descargarla ahora?";

            var resultado = MessageBox.Show(mensaje, "Actualización TecniLauncher",
                            MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (resultado == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = datos.LinkDescarga,
                    UseShellExecute = true
                });

                if (datos.EsCritica)
                {
                    Application.Current.Shutdown();
                }
            }
        }
        public async void InstalarActualizacion(string urlDescarga)
        {
            try
            {
                string rutaActual = Process.GetCurrentProcess().MainModule.FileName;
                string directorio = Path.GetDirectoryName(rutaActual);
                string rutaZip = Path.Combine(directorio, "Update.zip");
                string carpetaTemp = Path.Combine(directorio, "Update_Temp");

                txtEstadoCarga.Text = "Descargando paquete de actualización...";
                PanelCarga.Visibility = Visibility.Visible;

                using (var client = new System.Net.Http.HttpClient())
                {
                    var bytes = await client.GetByteArrayAsync(urlDescarga);
                    File.WriteAllBytes(rutaZip, bytes);
                }

                if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
                ZipFile.ExtractToDirectory(rutaZip, carpetaTemp);

                string rutaBat = Path.Combine(directorio, "update_script.bat");
                string nombreEjecutable = Path.GetFileName(rutaActual);

                string comandos = $@"@echo off
cd /d ""{directorio}""
taskkill /f /im ""{nombreEjecutable}"" >nul 2>&1
timeout /t 2 /nobreak >nul
xcopy /y /e ""{carpetaTemp}\*"" ""{directorio}\""
timeout /t 1 /nobreak >nul
start """" ""{nombreEjecutable}""
rd /s /q ""{carpetaTemp}""
del ""{rutaZip}""
del ""%~f0""
";
                File.WriteAllText(rutaBat, comandos);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{rutaBat}\"\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error en actualización ZIP: " + ex.Message);
            }
        }
        #endregion
        #region Noticias
        private async void CargarNoticiasGitHub()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = "https://raw.githubusercontent.com/johan12390785/TecniLauncher-Data/refs/heads/main/Web/noticias.json";

                    string json = await client.GetStringAsync(url);

                    var todas = JsonConvert.DeserializeObject<List<Noticia>>(json);

                    listaNoticias = todas.Take(5).ToList();

                    if (listaNoticias.Count > 0)
                    {
                        GenerarPuntos();
                        MostrarNoticia(0);
                    }
                }
            }
            catch (Exception)
            {
                TxtTitulo.Text = "No se pudo conectar con el servidor de noticias.";
            }
        }
        private BitmapImage CargarImagenOptimizada(string url)
        {
            try
            {
                var imagen = new BitmapImage();
                imagen.BeginInit();
                imagen.UriSource = new Uri(url);
                imagen.DecodePixelWidth = 900;

                imagen.CacheOption = BitmapCacheOption.OnLoad;
                imagen.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                imagen.EndInit();
                imagen.Freeze();

                return imagen;
            }
            catch
            {
                return null;
            }
        }
        private void ActualizarNoticiaConEfecto(int indice)
        {
            var noticia = listaNoticias[indice];
            string urlNueva = noticia.Imagen;

            var bitmapNuevo = new BitmapImage(new Uri(urlNueva));

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
                    ImgNoticiaBrush.BeginAnimation(Brush.OpacityProperty, null);
                    ImgNoticiaBrush.Opacity = 1.0;
                };
                ImgNoticiaBrush.BeginAnimation(Brush.OpacityProperty, fadeOut);

            }
            MostrarNoticia(indice);
            ActualizarPuntos();
        }
        private void DetenerCarrusel(object sender, MouseEventArgs e)
        {
            if (_timerNoticias != null)
            {
                _timerNoticias.Stop();
            }
        }
        private void IniciarCarrusel(object sender, MouseEventArgs e)
        {
            if (_timerNoticias != null)
            {
                _timerNoticias.Start();
            }
        }
        private void MostrarNoticia(int index)
        {
            if (listaNoticias.Count == 0) return;

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
                        BtnNoticiaAccion.Background = (Brush)new BrushConverter().ConvertFrom(dato.BotonColor);
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
                    var nuevaImagen = CargarImagenOptimizada(dato.Imagen);

                    if (nuevaImagen != null)
                    {
                        ImgNoticiaBrush.ImageSource = nuevaImagen;
                        ImgNoticiaFondoBrush.ImageSource = nuevaImagen;

                        ImgNoticiaBrush.BeginAnimation(Brush.OpacityProperty, null);
                        ImgNoticiaBrush.Opacity = 1.0;
                    }
                }
            }
            catch { }

            ActualizarPuntos();
        }
        private void BtnNoticia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
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
                Ellipse punto = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Margin = new Thickness(5),
                    Fill = Brushes.Gray
                };
                PanelPuntos.Children.Add(punto);
            }
        }
        private void ActualizarPuntos()
        {
            if (PanelPuntos == null) return;

            for (int i = 0; i < PanelPuntos.Children.Count; i++)
            {
                if (PanelPuntos.Children[i] is Ellipse punto)
                {
                    if (i == indiceActual)
                    {
                        punto.Fill = Brushes.White;
                        punto.Opacity = 1.0;
                    }
                    else
                    {
                        punto.Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
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
        #region Eventos

        private async void CargarEventosSegunLinks()
        {
            string urlMaestra = "https://raw.githubusercontent.com/johan12390785/TecniLauncher-Data/main/Eventos/index_eventos.json";

            try
            {
                PanelCarga.Visibility = Visibility.Visible;
                txtEstadoCarga.Text = "Conectando con el servidor...";

                var eventos = await EventosManager.CargarTodosLosEventos(urlMaestra);

                if (eventos != null && eventos.Count > 0)
                {
                    foreach (var ev in eventos)
                    {
                        ActualizarEstadoBoton(ev);
                    }
                    listaEventosUI.ItemsSource = eventos;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("La lista de eventos está vacía.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error crítico en CargarEventos: " + ex.Message);
            }
            finally
            {
                PanelCarga.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnAccionEvento_Click(object sender, RoutedEventArgs e)
        {
            var evento = (EventoModelo)((Button)sender).Tag;
            if (evento == null) return;

            if (evento.TextoBoton == "INSTALAR" || evento.TextoBoton == "ACTUALIZAR")
            {
                try
                {
                    evento.TextoBoton = "INSTALANDO...";
                    evento.ColorEstado = Brushes.Gray;
                    PanelCarga.Visibility = Visibility.Visible;

                    var pathEvento = new CmlLib.Core.MinecraftPath(evento.RutaCarpeta);
                    string rutaGlobal = Core.RutaGlobal;
                    pathEvento.Assets = Path.Combine(rutaGlobal, "assets");
                    pathEvento.Library = Path.Combine(rutaGlobal, "libraries");
                    pathEvento.Versions = Path.Combine(rutaGlobal, "versions");
                    pathEvento.Runtime = Path.Combine(rutaGlobal, "runtime");

                    var launcher = new CmlLib.Core.MinecraftLauncher(pathEvento);

                    var pTemp = new Perfil(evento.Nombre, evento.VersionMinecraft, evento.Loader, evento.MemoriaRam);
                    pTemp.VersionLoaderExacta = evento.VersionLoader;
                    pTemp.RutaCarpeta = evento.RutaCarpeta;

                    txtEstadoCarga.Text = $"Instalando {evento.Loader}...";
                    await InstalarModLoader(pTemp, launcher);

                    await InstalarArchivosEvento(evento);

                    if (!Directory.Exists(evento.RutaCarpeta)) Directory.CreateDirectory(evento.RutaCarpeta);
                    File.WriteAllText(Path.Combine(evento.RutaCarpeta, "version_local.txt"), evento.VersionEvento);

                    VentanaMensaje.Mostrar($"¡{evento.Nombre} listo para jugar!");
                    ActualizarEstadoBoton(evento);
                }
                catch (Exception ex)
                {
                    VentanaMensaje.Mostrar("Error: " + ex.Message);
                    ActualizarEstadoBoton(evento);
                }
                finally
                {
                    PanelCarga.Visibility = Visibility.Collapsed;
                }
            }
            else if (evento.TextoBoton == "JUGAR")
            {
                await LanzarMinecraftEvento(evento, (Button)sender);
            }
        }
        private async Task InstalarArchivosEvento(EventoModelo evento)
        {
            if (evento.Archivos == null || evento.Archivos.Count == 0) return;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TecniLauncher-App");

                int total = evento.Archivos.Count;
                int actuales = 0;

                foreach (var archivo in evento.Archivos)
                {
                    actuales++;
                    string rutaDestino = Path.Combine(evento.RutaCarpeta, archivo.RutaRelativa);

                    Application.Current.Dispatcher.Invoke(() => {
                        txtEstadoCarga.Text = $"({actuales}/{total}) Verificando: {archivo.Nombre}...";
                    });

                    bool necesitaDescarga = true;
                    if (File.Exists(rutaDestino) && !archivo.Descomprimir)
                    {
                        string hashLocal = CalcularHashSHA1(rutaDestino);
                        if (hashLocal.Equals(archivo.HashSHA1, StringComparison.OrdinalIgnoreCase))
                        {
                            necesitaDescarga = false;
                        }
                    }

                    if (necesitaDescarga)
                    {
                        Application.Current.Dispatcher.Invoke(() => {
                            txtEstadoCarga.Text = $"({actuales}/{total}) Descargando: {archivo.Nombre}...";
                        });

                        Directory.CreateDirectory(Path.GetDirectoryName(rutaDestino));

                        byte[] datos = await client.GetByteArrayAsync(archivo.UrlDescarga);

                        if (archivo.Descomprimir)
                        {
                            string zipTemp = Path.Combine(evento.RutaCarpeta, "temp_" + Guid.NewGuid() + ".zip");
                            await File.WriteAllBytesAsync(zipTemp, datos);

                            Application.Current.Dispatcher.Invoke(() => txtEstadoCarga.Text = $"Extrayendo: {archivo.Nombre}...");
                            try
                            {
                                string carpetaExtraction = Path.GetDirectoryName(rutaDestino);
                                System.IO.Compression.ZipFile.ExtractToDirectory(zipTemp, carpetaExtraction, true);
                            }
                            catch { }
                            finally { if (File.Exists(zipTemp)) File.Delete(zipTemp); }
                        }
                        else
                        {
                            await File.WriteAllBytesAsync(rutaDestino, datos);
                        }
                    }
                }
            }
        }
        private string CalcularHashSHA1(string filename)
        {
            try
            {
                using (var stream = File.OpenRead(filename))
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    byte[] hash = sha1.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return "error"; }
        }

        private void ActualizarEstadoBoton(EventoModelo ev)
        {
            if (ev == null) return;
            string fileVersion = Path.Combine(ev.RutaCarpeta, "version_local.txt");

            if (!File.Exists(fileVersion))
            {
                ev.TextoBoton = "INSTALAR";
                ev.ColorEstado = (Brush)new BrushConverter().ConvertFrom("#3498db");
            }
            else
            {
                string vLocal = File.ReadAllText(fileVersion).Trim();
                bool hayUpdate = vLocal != ev.VersionEvento;
                ev.TextoBoton = hayUpdate ? "ACTUALIZAR" : "JUGAR";
                ev.ColorEstado = hayUpdate ? Brushes.Orange : Brushes.SeaGreen;
            }
        }

        private async Task LanzarMinecraftEvento(EventoModelo evento, Button btnOrigen)
        {
            try
            {
                PanelCarga.Visibility = Visibility.Visible;
                txtEstadoCarga.Text = "Iniciando juego...";
                btnOrigen.IsEnabled = false;

                var p = new Perfil(evento.Nombre, evento.VersionMinecraft, evento.Loader, evento.MemoriaRam);
                p.RutaCarpeta = evento.RutaCarpeta;
                p.VersionLoaderExacta = evento.VersionLoader;

                await LanzarMinecraft(p);
            }
            catch (Exception ex)
            {
                VentanaMensaje.Mostrar("Error: " + ex.Message);
            }
            finally
            {
                PanelCarga.Visibility = Visibility.Collapsed;
                btnOrigen.IsEnabled = true;
            }
        }

        private void BtnDiscordEvento_Click(object sender, RoutedEventArgs e)
        {
            var evento = (EventoModelo)((Button)sender).Tag;
            if (!string.IsNullOrEmpty(evento.DiscordLink)) AbrirLink(evento.DiscordLink);
            else VentanaMensaje.Mostrar("Sin Discord configurado.");
        }

        private void BtnEliminarEvento_Click(object sender, RoutedEventArgs e)
        {
            var evento = (EventoModelo)((Button)sender).Tag;
            if (Directory.Exists(evento.RutaCarpeta))
            {
                if (VentanaMensaje.Mostrar($"¿Borrar '{evento.Nombre}'?", "Confirmar", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.Delete(evento.RutaCarpeta, true);
                        ActualizarEstadoBoton(evento);
                        listaEventosUI.Items.Refresh();
                    }
                    catch (Exception ex) { VentanaMensaje.Mostrar("Error: " + ex.Message); }
                }
            }
        }
        #endregion
    }
}