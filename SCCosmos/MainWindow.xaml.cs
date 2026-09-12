using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using System.Net.NetworkInformation;
using Wpf.Ui.Controls;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    public partial class MainWindow : FluentWindow
    {
        private DispatcherTimer _timerTareasFondo;

        public MainWindow()
        {
            InitializeComponent();
            DatabaseHelper.InicializarDatabase();

            PopupAlertas.PlacementTarget = BtnAlertas;

            this.Loaded += (s, e) =>
            {
                if (SystemParameters.PrimaryScreenWidth < 1600)
                {
                    MenuPrincipal.IsPaneOpen = false;
                }

                this.MenuPrincipal.Navigate(typeof(InicioPage));

                // =========================================================
                // INICIAR RELOJ PARA TAREAS AUTOMÁTICAS
                // =========================================================
                _timerTareasFondo = new DispatcherTimer();
                _timerTareasFondo.Interval = TimeSpan.FromHours(1);
                _timerTareasFondo.Tick += (sender, args) =>
                {
                    RevisarDescargasMartes();
                    RevisarSincronizacionLunes();
                };
                _timerTareasFondo.Start();

                NetworkChange.NetworkAvailabilityChanged += (senderNet, argsNet) =>
                {
                    if (argsNet.IsAvailable)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            RevisarDescargasMartes();
                            RevisarSincronizacionLunes();
                        });
                    }
                };

                RevisarDescargasMartes();
                RevisarSincronizacionLunes();
            };

            this.Activated += (s, e) => VerificarAlertas();
            this.PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        }

        // =========================================================
        // UTILIDAD: PING PARA ASEGURAR CONEXIÓN REAL
        // =========================================================
        private async Task<bool> HayConexionReal()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        // =========================================================
        // TAREAS AUTOMÁTICAS EN SEGUNDO PLANO
        // =========================================================

        private async void RevisarDescargasMartes()
        {
            if (DateTime.Today.DayOfWeek == DayOfWeek.Tuesday)
            {
                if (!await HayConexionReal()) return;

                string rutaAppData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                string rutaLocalSettings = System.IO.Path.Combine(rutaAppData, "local_settings.json");
                bool autoDescargarEnEstePC = false;

                if (System.IO.File.Exists(rutaLocalSettings))
                {
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(rutaLocalSettings)))
                        {
                            if (doc.RootElement.TryGetProperty("AutoDescargarMartes", out JsonElement prop))
                                autoDescargarEnEstePC = prop.GetBoolean();
                        }
                    }
                    catch { }
                }

                if (!autoDescargarEnEstePC) return;

                string ultimaDescarga = DatabaseHelper.ObtenerConfiguracion("UltimaDescargaMartes", "");
                string hoyString = DateTime.Today.ToString("yyyy-MM-dd");

                if (ultimaDescarga != hoyString)
                {
                    DatabaseHelper.GuardarConfiguracion("UltimaDescargaMartes", hoyString);

                    string rutaDestinoArchivos = ObtenerRutaDescargas();
                    OpcionesDescarga misOpciones = new OpcionesDescarga
                    {
                        Betel_AdoracionMatutina = true,
                        Teocraticos_DiscursosPublicos = true,
                        Teocraticos_AsambleaCircuitoSucursal = true,
                        Teocraticos_AsambleaCircuitoSuperintendente = true,
                        Teocraticos_AsambleaRegional = true,
                        JwOrg_AdoracionMatutina = true,
                        JwOrg_DiscursosBroadcasting = true,
                        JwOrg_GraduacionGalaad = true,
                        JwOrg_ReunionAnual = true
                    };

                    await Task.Run(() =>
                    {
                        string perfil = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
                        int nuevosArchivos = DescargadorStreamJW.DescargarUltimoDiscurso(perfil, rutaDestinoArchivos, misOpciones);

                        if (nuevosArchivos > 0)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                DatabaseHelper.GuardarConfiguracion("AlertaNuevasDescargas", nuevosArchivos.ToString());
                                VerificarAlertas();
                            });
                        }
                    });
                }
            }
        }

        private string ObtenerRutaDescargas()
        {
            string ruta = "";
            try
            {
                string rutaAppData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                string rutaArchivo = System.IO.Path.Combine(rutaAppData, "local_settings.json");

                if (File.Exists(rutaArchivo))
                {
                    string json = File.ReadAllText(rutaArchivo);
                    var localSettings = JsonSerializer.Deserialize<ConfiguracionLocalSettings>(json);

                    if (localSettings != null && !string.IsNullOrWhiteSpace(localSettings.RutaDescargas))
                    {
                        ruta = localSettings.RutaDescargas;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(ruta) || !Directory.Exists(ruta))
            {
                ruta = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DescargasStream");
                if (!Directory.Exists(ruta)) Directory.CreateDirectory(ruta);
            }

            return ruta;
        }

        private async void RevisarSincronizacionLunes()
        {
            if (DateTime.Today.DayOfWeek == DayOfWeek.Monday)
            {
                if (!await HayConexionReal()) return;

                string rutaAppData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                string rutaLocalSettings = System.IO.Path.Combine(rutaAppData, "local_settings.json");
                bool autoSincronizarEnEstePC = false;

                if (System.IO.File.Exists(rutaLocalSettings))
                {
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(rutaLocalSettings)))
                        {
                            if (doc.RootElement.TryGetProperty("AutoSincronizarLunes", out JsonElement prop))
                                autoSincronizarEnEstePC = prop.GetBoolean();
                        }
                    }
                    catch { }
                }

                if (!autoSincronizarEnEstePC) return;

                string ultimaSincronizacion = DatabaseHelper.ObtenerConfiguracion("UltimaSincronizacionLunes", "");
                string hoyString = DateTime.Today.ToString("yyyy-MM-dd");

                if (ultimaSincronizacion != hoyString)
                {
                    DatabaseHelper.GuardarConfiguracion("UltimaSincronizacionLunes", hoyString);

                    await Task.Run(() =>
                    {
                        string perfil = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                        var personasExtraidas = SincronizadorJWHub.SincronizarGlobal(perfil, modoSilencioso: true);

                        if (personasExtraidas != null && personasExtraidas.Count > 0)
                        {
                            var listaCongresBD = DatabaseHelper.ObtenerCongregaciones();
                            var personasParaBD = personasExtraidas.Select(pe =>
                            {
                                string congreHub = pe.Congregacion ?? "";
                                string codigoDetectado = "";
                                string nombreLimpiado = congreHub;

                                int start = congreHub.LastIndexOf('(');
                                int end = congreHub.LastIndexOf(')');
                                if (start >= 0 && end > start)
                                {
                                    codigoDetectado = congreHub.Substring(start + 1, end - start - 1).Trim();
                                    nombreLimpiado = congreHub.Substring(0, start).Trim();
                                }

                                var congreMatch = listaCongresBD.FirstOrDefault(c =>
                                    (!string.IsNullOrEmpty(codigoDetectado) && c.Codigo == codigoDetectado) ||
                                    (c.Nombre != null && c.Nombre.Equals(nombreLimpiado, StringComparison.OrdinalIgnoreCase)) ||
                                    (c.Nombre != null && congreHub.Contains(c.Nombre)));

                                if (congreMatch != null)
                                {
                                    codigoDetectado = congreMatch.Codigo;
                                    nombreLimpiado = congreMatch.Nombre;
                                }

                                return new Persona
                                {
                                    Nombre = pe.NombreCompleto,
                                    NombreJWHub = pe.NombreCompleto,
                                    NombreComun = pe.NombreComun,
                                    Sexo = "",
                                    Esperanza = pe.Esperanza,
                                    FechaNacimiento = pe.FechaNacimiento,
                                    FechaBautismo = pe.FechaBautismo,
                                    FechaInicioPrec = pe.FechaInicioPrec,
                                    FechaUltimaEscuela = pe.FechaUltimaEscuela,
                                    Solicitudes = pe.Solicitudes,
                                    Nombramiento = pe.Nombramiento,
                                    Servicio = pe.Servicio,
                                    Privilegios = pe.Privilegios,
                                    CongregacionNombre = nombreLimpiado,
                                    CongregacionCodigo = codigoDetectado,
                                    Telefono = pe.Telefono,
                                    Email = pe.Email,
                                    EmailJW = pe.EmailJW,
                                    Direccion = pe.Direccion,
                                    Foto = pe.Foto
                                };
                            }).ToList();

                            if (HayCambiosEnSincronizacion(personasParaBD))
                            {
                                string tempFile = System.IO.Path.Combine(rutaAppData, "HubSync_Pendiente.json");
                                System.IO.File.WriteAllText(tempFile, JsonSerializer.Serialize(personasParaBD));

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    VerificarAlertas();
                                });
                            }
                        }
                    });
                }
            }
        }

        private bool HayCambiosEnSincronizacion(List<Persona> extraidas)
        {
            var db = DatabaseHelper.ObtenerPersonas().Where(p => string.IsNullOrEmpty(p.FechaEliminacion)).ToList();

            var nombresExtraidos = extraidas.Select(e => e.NombreJWHub).ToHashSet();
            if (db.Any(p => !string.IsNullOrEmpty(p.NombreJWHub) && !nombresExtraidos.Contains(p.NombreJWHub))) return true;

            foreach (var ext in extraidas)
            {
                var match = db.FirstOrDefault(p => p.NombreJWHub == ext.NombreJWHub || p.Nombre == ext.Nombre);

                if (match == null) return true;

                if (match.CongregacionCodigo != ext.CongregacionCodigo) return true;
                if (match.Nombramiento != ext.Nombramiento) return true;
                if (match.Servicio != ext.Servicio) return true;
                if (match.Privilegios != ext.Privilegios) return true;
            }

            return false;
        }

        // =========================================================
        // EL SOLUCIONADOR GLOBAL DE SCROLL DE WPF.UI
        // =========================================================
        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            DependencyObject obj = e.OriginalSource as DependencyObject;
            ScrollViewer scrollViewerDestino = null;

            while (obj != null)
            {
                if (obj is System.Windows.Controls.ComboBox cb && cb.IsDropDownOpen) return;
                if (obj is System.Windows.Controls.TextBox txt && txt.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && txt.ExtentHeight > txt.ViewportHeight) return;

                if (obj is ScrollViewer sv)
                {
                    if (sv.ExtentHeight > sv.ViewportHeight)
                    {
                        scrollViewerDestino = sv;
                        break;
                    }
                }

                if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
                    obj = VisualTreeHelper.GetParent(obj);
                else
                    obj = LogicalTreeHelper.GetParent(obj);
            }

            if (scrollViewerDestino != null)
            {
                scrollViewerDestino.ScrollToVerticalOffset(scrollViewerDestino.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        // =========================================================
        // LÓGICA DE ALERTAS INTELIGENTES
        // =========================================================

        public class AlertaItem
        {
            public string Titulo { get; set; }
            public string Descripcion { get; set; }
            public string ColorBorde { get; set; }
            public int Dias { get; set; }
            public string TipoAlerta { get; set; }
        }

        private List<AlertaItem> _alertasActuales = new List<AlertaItem>();
        private int _cantidadAlertasVistas = 0;

        private void BtnAlertas_Click(object sender, RoutedEventArgs e)
        {
            PopupAlertas.IsOpen = true;
            ApagarCampana();
        }

        private void BtnAlertas_MouseEnter(object sender, MouseEventArgs e)
        {
            PopupAlertas.IsOpen = true;
            ApagarCampana();
        }

        private void ApagarCampana()
        {
            BtnAlertas.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
            _cantidadAlertasVistas = _alertasActuales.Count;

            if (DatabaseHelper.ObtenerConfiguracion("AlertaNuevasDescargas", "0") != "0")
            {
                DatabaseHelper.GuardarConfiguracion("AlertaNuevasDescargas", "0");
                VerificarAlertas(soloSilencioso: true);
            }
        }

        // =========================================================
        // ACCIONES AL HACER CLIC EN UNA NOTIFICACIÓN DE LA CAMPANA
        // =========================================================
        private void AlertaItem_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string tipoAlerta = border.Tag?.ToString() ?? "";

            if (tipoAlerta == "SincronizacionJWHub")
            {
                string rutaAppData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                string tempFile = System.IO.Path.Combine(rutaAppData, "HubSync_Pendiente.json");

                if (System.IO.File.Exists(tempFile))
                {
                    try
                    {
                        string json = System.IO.File.ReadAllText(tempFile);
                        var personasPendientes = JsonSerializer.Deserialize<List<Persona>>(json);

                        PopupAlertas.IsOpen = false;

                        var ventana = new SyncHubWindow(personasPendientes);
                        ventana.Owner = this;
                        ventana.ShowDialog();
                        System.IO.File.Delete(tempFile);
                        VerificarAlertas();
                    }
                    catch { }
                }
            }
            else if (tipoAlerta == "S303Pendiente")
            {
                PopupAlertas.IsOpen = false;
                MenuPrincipal.Navigate(typeof(VisitasPage));
            }
            // NUEVA ACCIÓN: Navegar a la Ruta si pinchas en la alerta del Rep. Betel
            else if (tipoAlerta == "RepBetel")
            {
                PopupAlertas.IsOpen = false;
                MenuPrincipal.Navigate(typeof(CalendarioPage));
            }
        }

        private void VerificarAlertas(bool soloSilencioso = false)
        {
            _alertasActuales.Clear();
            DateTime hoy = DateTime.Today;

            string rutaAppData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");

            // 1. Alerta por sincronización de JW Hub (Lunes)
            if (System.IO.File.Exists(System.IO.Path.Combine(rutaAppData, "HubSync_Pendiente.json")))
            {
                _alertasActuales.Add(new AlertaItem
                {
                    Titulo = "Revisión de JW Hub",
                    Descripcion = "Haz clic aquí para revisar las altas, bajas y modificaciones detectadas por la sincronización automática.",
                    ColorBorde = "#9C27B0", // Morado
                    Dias = -2,
                    TipoAlerta = "SincronizacionJWHub"
                });
            }

            // 2. Alerta por Descargas (Martes)
            string nuevasDescargas = DatabaseHelper.ObtenerConfiguracion("AlertaNuevasDescargas", "0");
            if (nuevasDescargas != "0")
            {
                _alertasActuales.Add(new AlertaItem
                {
                    Titulo = "¡Nuevas descargas listas!",
                    Descripcion = $"El sistema ha descargado {nuevasDescargas} audios nuevos automáticamente.",
                    ColorBorde = "#107C10", // Verde vibrante
                    Dias = -1,
                    TipoAlerta = "DescargaJW"
                });
            }

            string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos", "rutadb.txt");
            string dir = System.IO.File.Exists(configPath) ? System.IO.File.ReadAllText(configPath) : @"C:\Users\Usuario\Documents\2 - ESP-BA-01\8 - Otros\SCCosmos";
            string dbPath = System.IO.Path.Combine(dir, "sccosmos.db");
            string connectionString = $"Data Source={dbPath}";

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();

                    // CORRECCIÓN: Añadidas TipoAsamblea y RepBetelMensajeEnviado a la consulta SQL
                    cmd.CommandText = "SELECT FechaMartes, Actividad, Congregacion, SeccionAsamblea, S302Enviado, DocEnviada, RecordatorioEnviado, Tipo, Id, TipoAsamblea, RepBetelMensajeEnviado FROM Semanas WHERE date(FechaMartes) >= date('now', '-14 days')";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime fechaMartes = reader.GetDateTime(0);
                            string actividad = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            string congregacion = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            string seccion = reader.IsDBNull(3) ? "" : reader.GetString(3);

                            bool s302 = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;
                            bool doc = !reader.IsDBNull(5) && reader.GetInt32(5) == 1;
                            bool rec = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;

                            string tipoActividad = reader.IsDBNull(7) ? "" : reader.GetString(7);
                            string idSemana = reader.GetInt32(8).ToString();

                            // NUEVOS CAMPOS LEÍDOS
                            string tipoAsamblea = reader.IsDBNull(9) ? "" : reader.GetString(9);
                            bool repBetelEnviado = !reader.IsDBNull(10) && reader.GetInt32(10) == 1;

                            int diasFaltantes = (fechaMartes - hoy).Days;
                            string nombreCon = string.IsNullOrEmpty(congregacion) ? "una congregación sin asignar" : congregacion;

                            // Comprobamos si las alertas futuras siguen en pie (solo si es fecha futura)
                            if (diasFaltantes >= 0)
                            {
                                if (actividad == "Visita")
                                {
                                    if (!s302 && diasFaltantes <= 90)
                                    {
                                        _alertasActuales.Add(new AlertaItem
                                        {
                                            Titulo = "S-302 pendiente",
                                            Descripcion = $"Quedan {diasFaltantes} días para la visita a {nombreCon}.",
                                            ColorBorde = "#FFA500", // Naranja
                                            Dias = diasFaltantes,
                                            TipoAlerta = "S302"
                                        });
                                    }

                                    if (!doc && diasFaltantes <= 42)
                                    {
                                        _alertasActuales.Add(new AlertaItem
                                        {
                                            Titulo = "Documentación de visita",
                                            Descripcion = $"Quedan {diasFaltantes} días para la visita a {nombreCon}.",
                                            ColorBorde = "#D13438", // Rojo
                                            Dias = diasFaltantes,
                                            TipoAlerta = "Documentacion"
                                        });
                                    }
                                }
                                else if (actividad == "Asamblea de circuito")
                                {
                                    // Alerta genérica del recordatorio a la sección
                                    if (!rec && diasFaltantes <= 60)
                                    {
                                        _alertasActuales.Add(new AlertaItem
                                        {
                                            Titulo = "Recordatorio de asamblea",
                                            Descripcion = $"Quedan {diasFaltantes} días para la asamblea de la Sección {seccion}.",
                                            ColorBorde = "#0078D4", // Azul
                                            Dias = diasFaltantes,
                                            TipoAlerta = "Recordatorio"
                                        });
                                    }

                                    // NUEVA ALERTA: Mensaje al representante de Betel
                                    // 30 días = aproximadamente 1 mes
                                    if (tipoAsamblea == "AC-BR" && !repBetelEnviado && diasFaltantes <= 30)
                                    {
                                        _alertasActuales.Add(new AlertaItem
                                        {
                                            Titulo = "Mensaje a Rep. de Betel",
                                            Descripcion = $"Quedan {diasFaltantes} días para la asamblea AC-BR (Sec. {seccion}). Aún no has enviado el email al representante.",
                                            ColorBorde = "#8A2BE2", // Púrpura elegante
                                            Dias = diasFaltantes,
                                            TipoAlerta = "RepBetel"
                                        });
                                    }
                                }
                            }

                            // =========================================================
                            // ALERTA S-303 PENDIENTE DE ENVÍO
                            // =========================================================
                            if (actividad == "Visita" && !string.IsNullOrEmpty(congregacion))
                            {
                                bool esSCS = tipoActividad.Contains("SCS", StringComparison.OrdinalIgnoreCase) || tipoActividad.Equals("Visita de SCS", StringComparison.OrdinalIgnoreCase);

                                if (!esSCS)
                                {
                                    DateTime domingoVisita = fechaMartes.AddDays(5);
                                    DateTime limiteEnvio = domingoVisita.AddHours(17);

                                    if (DateTime.Now >= limiteEnvio && (DateTime.Now - limiteEnvio).TotalDays <= 14)
                                    {
                                        string mesRef = fechaMartes.ToString("yyyy-MM");
                                        string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(congregacion);

                                        bool s303Enviado = DatabaseHelper.ObtenerConfiguracion($"S303Enviado_{congreCodigo}_{mesRef}", "0") == "1";

                                        if (!s303Enviado)
                                        {
                                            _alertasActuales.Add(new AlertaItem
                                            {
                                                Titulo = "S-303 Pendiente de envío",
                                                Descripcion = $"La visita a {nombreCon} ha concluido. Recuerda completar y enviar el informe S-303.",
                                                ColorBorde = "#E81123", // Rojo Fuerte
                                                Dias = -100 + diasFaltantes,
                                                TipoAlerta = "S303Pendiente"
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (_alertasActuales.Count > 0)
            {
                if (!soloSilencioso && _alertasActuales.Count > _cantidadAlertasVistas)
                {
                    BtnAlertas.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); // Dorado
                }

                ListaAlertas.ItemsSource = _alertasActuales.OrderBy(a => a.Dias).ToList();
                ListaAlertas.Visibility = Visibility.Visible;
                TituloPopup.Visibility = Visibility.Visible;
                TxtSinAlertas.Visibility = Visibility.Collapsed;
            }
            else
            {
                BtnAlertas.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")); // Campana normal
                _cantidadAlertasVistas = 0;

                ListaAlertas.ItemsSource = null;
                ListaAlertas.Visibility = Visibility.Collapsed;
                TituloPopup.Visibility = Visibility.Collapsed;
                TxtSinAlertas.Visibility = Visibility.Visible;
            }
        }

        private void MenuSplitter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double nuevoAncho = MenuPrincipal.OpenPaneLength + e.HorizontalChange;

            if (nuevoAncho >= 150 && nuevoAncho <= 500)
            {
                MenuPrincipal.OpenPaneLength = nuevoAncho;
            }
        }
    }
}