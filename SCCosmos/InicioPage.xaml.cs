using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class InicioPage : Page
    {
        public InicioPage()
        {
            InitializeComponent();
            CargarSemanaActual();
        }

        private void CargarSemanaActual()
        {
            try
            {
                // 1. CALCULAMOS QUÉ FECHA FUE EL MARTES DE ESTA SEMANA
                DateTime hoy = DateTime.Today;
                int diferencia = hoy.DayOfWeek - DayOfWeek.Tuesday;

                // Si hoy es lunes, lo asociamos a la semana que empezó el martes anterior
                if (diferencia < 0) diferencia += 7;

                DateTime martesDeEstaSemana = hoy.AddDays(-diferencia).Date;

                // 2. Convertimos el martes al formato exacto de tu base de datos (Ej: "2026-10-27")
                string fechaIso = martesDeEstaSemana.ToString("yyyy-MM-dd");

                // 3. Usamos tu método exacto pasando esa fecha
                var semanaActual = DatabaseHelper.ObtenerSemana(fechaIso);

                if (semanaActual == null)
                {
                    // Si no hay datos guardados en la BD para la semana en curso, mostramos el aviso
                    TxtSinDatos.Visibility = Visibility.Visible;
                    ((UIElement)IconoActividad.Parent).Visibility = Visibility.Collapsed;
                    return;
                }

                // 4. Vinculamos los datos encontrados al diseño de la página
                this.DataContext = semanaActual;

                // 5. LÓGICA VISUAL
                if (semanaActual.Actividad == "Visita")
                {
                    PanelVisita.Visibility = Visibility.Visible;
                    BtnVerVisita.Visibility = Visibility.Visible;
                    IconoActividad.Symbol = Wpf.Ui.Controls.SymbolRegular.Building24;
                }
                else
                {
                    PanelOtraActividad.Visibility = Visibility.Visible;
                    IconoActividad.Symbol = Wpf.Ui.Controls.SymbolRegular.CalendarStar24;
                    IconoActividad.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo cargar la información de la semana actual: {ex.Message}");
            }
        }

        private void BtnVerVisita_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Recuperamos los datos de la semana actual (los habíamos guardado en el DataContext al cargar la página)
                var semanaActual = this.DataContext as SemanaCalendario;

                if (semanaActual != null && !string.IsNullOrEmpty(semanaActual.Congregacion))
                {
                    string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(semanaActual.Congregacion);
                    VisitasPage.CodigoPendiente = congreCodigo;
                    VisitasPage.NombrePendiente = semanaActual.Congregacion;
                    VisitasPage.IdVisitaPendiente = semanaActual.Id;
                    var ventanaPrincipal = Application.Current.MainWindow as MainWindow;
                    if (ventanaPrincipal != null)
                    {
                        ventanaPrincipal.MenuPrincipal.Navigate(typeof(VisitasPage));
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("No hay datos de una congregación válida para esta semana.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al intentar abrir la visita: {ex.Message}");
            }
        }
    }
}