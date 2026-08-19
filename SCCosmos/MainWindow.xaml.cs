using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using Wpf.Ui.Controls;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DatabaseHelper.InicializarDatabase();

            // Forzamos manualmente que el popup "abrace" al botón para evitar fallos de posición
            PopupAlertas.PlacementTarget = BtnAlertas;

            this.Loaded += (s, e) =>
            {
                // DETECCIÓN INTELIGENTE DE PANTALLAS PEQUEÑAS (PORTÁTILES)
                // Si el ancho lógico de la pantalla es menor a 1600px, cerramos el menú por defecto.
                if (SystemParameters.PrimaryScreenWidth < 1600)
                {
                    MenuPrincipal.IsPaneOpen = false;
                }

                this.MenuPrincipal.Navigate(typeof(InicioPage));
            };

            // Se recalcula al volver a la ventana principal
            this.Activated += (s, e) => VerificarAlertas();

            // =========================================================
            // AÑADIMOS EL GANCHO GLOBAL AL CONSTRUCTOR PARA EL SCROLL
            // =========================================================
            this.PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        }

        // =========================================================
        // EL SOLUCIONADOR GLOBAL DE SCROLL DE WPF.UI
        // =========================================================
        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Hemos cambiado el HitTest por OriginalSource. Así respeta los menús flotantes.
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
        }

        private List<AlertaItem> _alertasActuales = new List<AlertaItem>();

        private void BtnAlertas_Click(object sender, RoutedEventArgs e)
        {
            PopupAlertas.IsOpen = true;
        }

        private void BtnAlertas_MouseEnter(object sender, MouseEventArgs e)
        {
            // ¡Se abrirá al instante al posar el cursor sobre la campana!
            PopupAlertas.IsOpen = true;
        }

        private void VerificarAlertas()
        {
            _alertasActuales.Clear();
            DateTime hoy = DateTime.Today;

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

                    cmd.CommandText = "SELECT FechaMartes, Actividad, Congregacion, SeccionAsamblea, S302Enviado, DocEnviada, RecordatorioEnviado FROM Semanas WHERE date(FechaMartes) >= date('now')";

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

                            int diasFaltantes = (fechaMartes - hoy).Days;
                            string nombreCon = string.IsNullOrEmpty(congregacion) ? "una congregación sin asignar" : congregacion;

                            if (actividad == "Visita")
                            {
                                if (!s302 && diasFaltantes <= 90)
                                {
                                    _alertasActuales.Add(new AlertaItem
                                    {
                                        Titulo = "S-302 pendiente",
                                        Descripcion = $"Quedan {diasFaltantes} días para la visita a {nombreCon}.",
                                        ColorBorde = "#FFA500", // Naranja
                                        Dias = diasFaltantes
                                    });
                                }

                                if (!doc && diasFaltantes <= 42)
                                {
                                    _alertasActuales.Add(new AlertaItem
                                    {
                                        Titulo = "Documentación de visita",
                                        Descripcion = $"Quedan {diasFaltantes} días para la visita a {nombreCon}.",
                                        ColorBorde = "#D13438", // Rojo
                                        Dias = diasFaltantes
                                    });
                                }
                            }
                            else if (actividad == "Asamblea de circuito")
                            {
                                if (!rec && diasFaltantes <= 60)
                                {
                                    _alertasActuales.Add(new AlertaItem
                                    {
                                        Titulo = "Recordatorio de asamblea",
                                        Descripcion = $"Quedan {diasFaltantes} días para la asamblea de la Sección {seccion}.",
                                        ColorBorde = "#0078D4", // Azul
                                        Dias = diasFaltantes
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Actualizamos la interfaz del Popup y de la campana
            if (_alertasActuales.Count > 0)
            {
                BadgeAlertas.Visibility = Visibility.Visible;
                BtnAlertas.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB0B0")); // Campana roja

                ListaAlertas.ItemsSource = _alertasActuales.OrderBy(a => a.Dias).ToList();
                ListaAlertas.Visibility = Visibility.Visible;
                TituloPopup.Visibility = Visibility.Visible;
                TxtSinAlertas.Visibility = Visibility.Collapsed;
            }
            else
            {
                BadgeAlertas.Visibility = Visibility.Collapsed;
                BtnAlertas.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")); // Campana normal

                ListaAlertas.ItemsSource = null;
                ListaAlertas.Visibility = Visibility.Collapsed;
                TituloPopup.Visibility = Visibility.Collapsed;
                TxtSinAlertas.Visibility = Visibility.Visible;
            }
        }

        // =========================================================
        // LÓGICA PARA ARRASTRAR Y CAMBIAR EL ANCHO DEL MENÚ
        // =========================================================
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