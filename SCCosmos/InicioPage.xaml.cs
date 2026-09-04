using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SCCosmos
{
    public partial class InicioPage : Page
    {
        public InicioPage()
        {
            InitializeComponent();
            TxtSaludo.Text = $"¡Hola, {DatabaseHelper.ObtenerConfiguracion("NombreSC", "Jonatán")}!";
            CargarSemanaActual();
            _ = CargarEstadisticasAsync();

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
                    IconoActividad.Visibility = Visibility.Collapsed;
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

                    // NUEVO: Cargar el programa del día si existe
                    string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(semanaActual.Congregacion);
                    string mesReferenciaStr = martesDeEstaSemana.ToString("yyyy-MM");
                    CargarProgramaDelDia(congreCodigo, mesReferenciaStr, hoy.DayOfWeek);
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

        private void CargarProgramaDelDia(string congreCodigo, string mesReferenciaStr, DayOfWeek diaHoy)
        {
            // Traducimos el DayOfWeek al formato string que usa la base de datos del Programa
            string diaDbStr = "";
            switch (diaHoy)
            {
                case DayOfWeek.Wednesday: diaDbStr = "Miércoles"; break;
                case DayOfWeek.Thursday: diaDbStr = "Jueves"; break;
                case DayOfWeek.Friday: diaDbStr = "Viernes"; break;
                case DayOfWeek.Saturday: diaDbStr = "Sábado"; break;
                case DayOfWeek.Sunday: diaDbStr = "Domingo"; break;
                default: return; // Lunes y Martes no tienen programa de predicación guardado
            }

            var programaDeHoy = new List<ActividadHoy>();
            string[] turnos = { "1. MAÑANA", "2. MEDIODÍA", "3. TARDE", "4. NOCHE" };

            // Extraemos todo lo programado para este día exacto
            foreach (var turno in turnos)
            {
                string turnoLimpio = turno.Substring(3); // Quita el "1. "

                // Recopilamos a Joni, Rebe y Comidas del mismo turno
                var responsables = new[] { "Joni", "Rebe", "Comida", "Cenar", "Dormir" };

                foreach (var resp in responsables)
                {
                    var actividades = DatabaseHelper.ObtenerActividadesPrograma(congreCodigo, mesReferenciaStr, diaDbStr, turnoLimpio, resp);
                    foreach (var act in actividades)
                    {
                        var comp = DatabaseHelper.ObtenerPersonaPorId(act.CompaneroId);
                        string nombreComp = comp != null ? comp.Nombre.Split(' ')[0] : "Alguien";

                        programaDeHoy.Add(new ActividadHoy
                        {
                            Hora = act.Hora,
                            TipoActividad = act.TipoActividad,
                            CompaneroNombre = nombreComp,
                            VerCompanero = (resp == "Comida" || resp == "Cenar" || resp == "Dormir") ? Visibility.Collapsed : Visibility.Visible
                        });
                    }
                }
            }

            if (programaDeHoy.Count > 0)
            {
                // Ordenamos por hora para que quede un planning secuencial perfecto
                var programaOrdenado = programaDeHoy.OrderBy(a => a.Hora).ToList();
                ListaProgramaHoy.ItemsSource = programaOrdenado;
                PanelProgramaDia.Visibility = Visibility.Visible;
            }
        }

        private void BtnVerVisita_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var semanaActual = this.DataContext as SemanaCalendario;

                if (semanaActual != null && !string.IsNullOrEmpty(semanaActual.Congregacion))
                {
                    string congreCodigo = DatabaseHelper.ObtenerCodigoCongregacion(semanaActual.Congregacion);
                    VisitasPage.CodigoPendiente = congreCodigo;
                    VisitasPage.NombrePendiente = semanaActual.Congregacion;
                    VisitasPage.MesPendiente = semanaActual.FechaMartes.ToString("yyyy-MM");

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

        // ==========================================
        // CÁLCULO DE ESTADÍSTICAS DEL CIRCUITO
        // ==========================================
        private async Task CargarEstadisticasAsync()
        {
            // Usamos Task.Run para que las consultas SQL se hagan en segundo plano sin congelar la UI
            var stats = await Task.Run(() => ObtenerContadoresDesdeBD());

            // Una vez terminadas las consultas, actualizamos la interfaz gráfica
            TxtTotalPrecReg.Text = stats.PrecRegulares.ToString();
            TxtTotalPrecEsp.Text = stats.PrecEspeciales.ToString();
            TxtTotalAncianos.Text = stats.Ancianos.ToString();
            TxtTotalSM.Text = stats.SiervosM.ToString();
            TxtTotalA2.Text = stats.SolA2.ToString();
            TxtTotalA19.Text = stats.SolA19.ToString();
            TxtTotalA8.Text = stats.SolA8.ToString();
            TxtTotalG8.Text = stats.SolG8.ToString();
        }

        private EstadisticasCircuito ObtenerContadoresDesdeBD()
        {
            var res = new EstadisticasCircuito();

            // Nos conectamos a la BD usando la ruta oficial de tu DatabaseHelper
            string dbPath = System.IO.Path.Combine(DatabaseHelper.RutaDirectorioDatos, "sccosmos.db");
            string connectionString = $"Data Source={dbPath}";

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();

                    res.PrecRegulares = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Servicio LIKE '%Precursor regular%';");
                    res.PrecEspeciales = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Servicio LIKE '%Precursor especial%';");

                    res.Ancianos = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Nombramiento LIKE '%Anciano%';");
                    res.SiervosM = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Nombramiento LIKE '%Siervo ministerial%';");

                    res.SolA2 = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-2%';");
                    res.SolA19 = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-19%';");

                    res.SolA8 = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-8%';");
                    res.SolG8 = EjecutarCount(connection, "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%G-8%';");
                }
            }
            catch { } // Si la BD aún no está inicializada o falla, se quedarán en 0.

            return res;
        }

        private int EjecutarCount(SqliteConnection connection, string query)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = query;
                var resultado = cmd.ExecuteScalar();
                return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
            }
        }

        private void NavegarAPersonas()
        {
            var ventanaPrincipal = Application.Current.MainWindow as MainWindow;
            ventanaPrincipal?.MenuPrincipal.Navigate(typeof(PersonasPage));
        }

        private void CardPrecReg_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteServicio = "Precursor regular"; NavegarAPersonas(); }
        private void CardPrecEsp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteServicio = "Precursor especial"; NavegarAPersonas(); }
        private void CardAncianos_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteNombramiento = "Anciano"; NavegarAPersonas(); }
        private void CardSiervos_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteNombramiento = "Siervo ministerial"; NavegarAPersonas(); }
        private void CardA2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-2"; NavegarAPersonas(); }
        private void CardA19_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-19"; NavegarAPersonas(); }
        private void CardA8_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-8"; NavegarAPersonas(); }
        private void CardG8_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "G-8"; NavegarAPersonas(); }

        // ==========================================
        // CLASES DE DATOS
        // ==========================================
        private class EstadisticasCircuito
        {
            public int PrecRegulares { get; set; } = 0;
            public int PrecEspeciales { get; set; } = 0;
            public int Ancianos { get; set; } = 0;
            public int SiervosM { get; set; } = 0;
            public int SolA2 { get; set; } = 0;
            public int SolA19 { get; set; } = 0;
            public int SolA8 { get; set; } = 0; // NUEVO
            public int SolG8 { get; set; } = 0; // NUEVO
        }

        public class ActividadHoy
        {
            public string Hora { get; set; }
            public string TipoActividad { get; set; }
            public string CompaneroNombre { get; set; }
            public Visibility VerCompanero { get; set; }
        }
    }
}