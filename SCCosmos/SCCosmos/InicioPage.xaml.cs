using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos
{
    public partial class InicioPage : Page, IActualizablePorSincro
    {
        private SemanaCalendario _semanaActual;
        private SemanaCalendario _proximaActividad;
        private DateTime _lunesSemanaActual;
        private DateTime _domingoSemanaActual;
        private DateTime _fechaProgramaSeleccionada;
        private string _codigoCongregacionPrograma = "";
        private string _mesReferenciaPrograma = "";

        public InicioPage()
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();

            InitializeComponent();

            // Si el equipo lo usa la esposa del SC, el saludo es con su
            // nombre; si no está puesto, se usa el del SC.
            bool esEsposa =
                PerfilAcceso.Configurado
                && !PerfilAcceso.EsHombre;

            string nombre =
                DatabaseHelper.ObtenerConfiguracion(
                    esEsposa
                        ? "NombreEsposaSC"
                        : "NombreSC",
                    "");

            if (string.IsNullOrWhiteSpace(nombre)
                && esEsposa)
            {
                nombre =
                    DatabaseHelper.ObtenerConfiguracion(
                        "NombreSC",
                        "");
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                nombre = "Jonatán";
            }

            TxtSaludo.Text = $"¡Hola, {nombre}!";

            CargarSemanaActual();
            _ = CargarEstadisticasAsync();

            Perf.Registrar("Inicio", reloj.ElapsedMilliseconds);
        }

        // =====================================================
        // SEMANA ACTUAL: LUNES A DOMINGO
        // =====================================================
        public void DatosActualizados()
        {
            try
            {
                CargarSemanaActual();

                _ = CargarEstadisticasAsync();
            }
            catch
            {
            }
        }


        private void CargarSemanaActual()
        {
            try
            {
                DateTime hoy = DateTime.Today;

                // DayOfWeek usa Sunday=0. Esta fórmula devuelve cuántos días
                // han pasado desde el lunes de la semana natural actual.
                int diasDesdeLunes =
                    ((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

                _lunesSemanaActual =
                    hoy.AddDays(-diasDesdeLunes).Date;

                _domingoSemanaActual =
                    _lunesSemanaActual.AddDays(6).Date;

                CargarProximaActividad();

                // La tabla Semanas conserva históricamente FechaMartes como
                // fecha identificadora. Para una semana lunes-domingo, el
                // martes asociado es simplemente lunes + 1.
                DateTime martesIdentificador =
                    _lunesSemanaActual.AddDays(1);

                string fechaIso =
                    martesIdentificador.ToString("yyyy-MM-dd");

                _semanaActual =
                    DatabaseHelper.ObtenerSemana(fechaIso);

                CargarHorasMes();

                PanelVisita.Visibility = Visibility.Collapsed;
                PanelOtraActividad.Visibility = Visibility.Collapsed;
                PanelProgramaDia.Visibility = Visibility.Collapsed;
                _ = CargarTiempoAsync(DateTime.Today);
                BtnVerVisita.Visibility = Visibility.Collapsed;
                TxtSinDatos.Visibility = Visibility.Collapsed;
                IconoActividad.Visibility = Visibility.Visible;

                if (_semanaActual == null)
                {
                    TxtSinDatos.Visibility = Visibility.Visible;
                    IconoActividad.Visibility = Visibility.Collapsed;
                    return;
                }

                DataContext = _semanaActual;

                string rango =
                    $"Del {_lunesSemanaActual:dd MMM} al {_domingoSemanaActual:dd MMM yyyy}";

                if (TxtRangoSemanaVisita != null)
                {
                    TxtRangoSemanaVisita.Text = rango;
                }

                if (TxtRangoSemanaOtra != null)
                {
                    TxtRangoSemanaOtra.Text =
                        $"Semana del {_lunesSemanaActual:dd 'de' MMMM}";
                }

                if (string.Equals(
                        _semanaActual.Actividad,
                        "Visita",
                        StringComparison.OrdinalIgnoreCase))
                {
                    PanelVisita.Visibility = Visibility.Visible;
                    BtnVerVisita.Visibility = Visibility.Visible;
                    IconoActividad.Visibility = Visibility.Collapsed;

                    _codigoCongregacionPrograma =
                        DatabaseHelper.ObtenerCodigoCongregacion(
                            _semanaActual.Congregacion);

                    // El programa de la visita se guarda con el mes de la
                    // semana/visita (FechaMartes), incluso cuando el domingo
                    // cae en el mes siguiente.
                    _mesReferenciaPrograma =
                        _semanaActual.FechaMartes.ToString("yyyy-MM");

                    _fechaProgramaSeleccionada = hoy.Date;
                    CargarProgramaFecha(_fechaProgramaSeleccionada);
                }
                else
                {
                    PanelOtraActividad.Visibility = Visibility.Visible;
                    IconoActividad.Visibility = Visibility.Visible;
                    IconoActividad.Symbol =
                        Wpf.Ui.Controls.SymbolRegular.CalendarStar24;
                    IconoActividad.Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)
                            System.Windows.Media.ColorConverter.ConvertFromString(
                                "#AAAAAA"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No se pudo cargar la información de la semana actual: {ex.Message}");
            }
        }

        // =====================================================
        // PROGRAMA DEL DÍA
        // =====================================================
        private void CargarProximaActividad()
        {
            try
            {
                DateTime martesActual =
                    _lunesSemanaActual.AddDays(1);

                _proximaActividad =
                    DatabaseHelper
                        .ObtenerTodasLasSemanas()
                        .Where(s =>
                            s.FechaMartes.Date > martesActual.Date
                            && !string.IsNullOrWhiteSpace(
                                s.Actividad))
                        .OrderBy(s => s.FechaMartes)
                        .ThenBy(s => s.Id)
                        .FirstOrDefault();

                if (_proximaActividad == null)
                {
                    TxtProximaActividad.Visibility =
                        Visibility.Collapsed;

                    return;
                }

                TxtProximaActividad.Text =
                    "Próxima: " +
                    TextoProximaActividad(
                        _proximaActividad);

                TxtProximaActividad.Visibility =
                    Visibility.Visible;
            }
            catch
            {
                TxtProximaActividad.Visibility =
                    Visibility.Collapsed;
            }
        }

        private static string TextoProximaActividad(
            SemanaCalendario semana)
        {
            string actividad =
                semana.Actividad?.Trim() ?? "";

            if (string.Equals(
                    actividad,
                    "Visita",
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(
                    semana.Congregacion))
            {
                actividad =
                    "Visita · " + semana.Congregacion;
            }
            else if (string.Equals(
                    actividad,
                    "Asamblea de circuito",
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(
                    semana.SeccionAsamblea))
            {
                actividad =
                    "Asamblea de circuito · Sección " +
                    semana.SeccionAsamblea;
            }

            return actividad;
        }

        private void TxtProximaActividad_Click(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_proximaActividad == null)
            {
                return;
            }

            CalendarioPage.SemanaDestacadaPendiente =
                _proximaActividad.FechaMartes
                    .ToString("yyyy-MM-dd");

            var ventanaPrincipal =
                Application.Current.MainWindow as MainWindow;

            ventanaPrincipal
                ?.MenuPrincipal
                .Navigate(typeof(CalendarioPage));
        }


        private void CargarProgramaFecha(DateTime fecha)
        {
            if (_semanaActual == null
                || !string.Equals(
                    _semanaActual.Actividad,
                    "Visita",
                    StringComparison.OrdinalIgnoreCase))
            {
                PanelProgramaDia.Visibility = Visibility.Collapsed;
                _ = CargarTiempoAsync(DateTime.Today);
                return;
            }

            _fechaProgramaSeleccionada = fecha.Date;

            if (_fechaProgramaSeleccionada < _lunesSemanaActual)
            {
                _fechaProgramaSeleccionada = _lunesSemanaActual;
            }
            else if (_fechaProgramaSeleccionada > _domingoSemanaActual)
            {
                _fechaProgramaSeleccionada = _domingoSemanaActual;
            }

            BtnProgramaDiaAnterior.IsEnabled =
                _fechaProgramaSeleccionada > _lunesSemanaActual;

            BtnProgramaDiaSiguiente.IsEnabled =
                _fechaProgramaSeleccionada < _domingoSemanaActual;

            TxtFechaProgramaDia.Text =
                FormatearFechaPrograma(
                    _fechaProgramaSeleccionada);

            // El tiempo sigue al día del programa.
            _ = CargarTiempoAsync(_fechaProgramaSeleccionada);

            string diaDb =
                ObtenerNombreDiaPrograma(
                    _fechaProgramaSeleccionada.DayOfWeek);

            var actividadesMes =
                string.IsNullOrWhiteSpace(_codigoCongregacionPrograma)
                    ? new List<DatabaseHelper.ActividadPrograma>()
                    : DatabaseHelper.ObtenerActividadesProgramaMes(
                        _codigoCongregacionPrograma,
                        _mesReferenciaPrograma);

            var programa =
                actividadesMes
                    .Where(a =>
                        string.Equals(
                            a.DiaSemana?.Trim(),
                            diaDb,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => ObtenerOrdenHora(a.Hora))
                    .ThenBy(a => a.Id)
                    .Select(CrearActividadInicio)
                    .ToList();

            ListaProgramaHoy.ItemsSource = programa;
            TxtSinProgramaDia.Visibility =
                programa.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // Durante una visita el panel siempre se muestra, incluso si el
            // día elegido está vacío, para que las flechas sigan disponibles.
            PanelProgramaDia.Visibility = Visibility.Visible;
        }

        private ActividadHoy CrearActividadInicio(
            DatabaseHelper.ActividadPrograma actividad)
        {
            string responsable =
                NormalizarResponsablePrograma(
                    actividad.Responsable);

            // Nuestros nombres de pila en lugar de SC/Esposa.
            if (string.Equals(responsable, "SC", StringComparison.OrdinalIgnoreCase))
            {
                responsable = DatabaseHelper
                    .ObtenerConfiguracion("NombreSC", "")
                    .Trim();
            }
            else if (string.Equals(responsable, "Esposa", StringComparison.OrdinalIgnoreCase))
            {
                responsable = DatabaseHelper
                    .ObtenerConfiguracion("NombreEsposaSC", "")
                    .Trim();
            }
            else if (string.Equals(responsable, "Ambos", StringComparison.OrdinalIgnoreCase))
            {
                responsable = "";
            }

            string tipo =
                actividad.TipoActividad?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(tipo)
                && EsActividadLegacy(actividad.Responsable))
            {
                tipo = actividad.Responsable.Trim();
            }

            if (string.IsNullOrWhiteSpace(tipo))
            {
                tipo = "Actividad";
            }

            // En las actividades "de comida/cena/dormir" el responsable es la
            // propia actividad: no se muestra insignia de persona.
            if (EsActividadLegacy(actividad.Responsable))
            {
                responsable = "";
            }

            Persona acompanante =
                ObtenerPersonaActual(
                    actividad.CompaneroId);

            string nombreAcompanante =
                ObtenerNombrePreferido(
                    acompanante);

            string ubicacion =
                actividad.Direccion?.Trim() ?? "";

            return new ActividadHoy
            {
                Hora =
                    string.IsNullOrWhiteSpace(actividad.Hora)
                        ? "--:--"
                        : actividad.Hora.Trim(),
                Responsable = responsable,
                VerResponsable =
                    string.IsNullOrWhiteSpace(responsable)
                        ? Visibility.Collapsed
                        : Visibility.Visible,
                TipoActividad = tipo,
                CompaneroNombre =
                    string.IsNullOrWhiteSpace(nombreAcompanante)
                        ? "Sin acompañante"
                        : nombreAcompanante,
                Ubicacion =
                    string.IsNullOrWhiteSpace(ubicacion)
                        ? ""
                        : "📍 " + ubicacion,
                VerCompanero =
                    string.IsNullOrWhiteSpace(nombreAcompanante)
                        ? Visibility.Collapsed
                        : Visibility.Visible,
                VerUbicacion =
                    string.IsNullOrWhiteSpace(ubicacion)
                        ? Visibility.Collapsed
                        : Visibility.Visible,
                Direccion = ubicacion,
                Latitud = actividad.Latitud,
                Longitud = actividad.Longitud,
                VerMapa =
                    (actividad.Latitud != 0
                     || actividad.Longitud != 0
                     || !string.IsNullOrWhiteSpace(ubicacion))
                        ? Visibility.Visible
                        : Visibility.Collapsed
            };
        }

        // =========================================================
        // HORAS PREDICADAS DEL MES
        // =========================================================

        private void CargarHorasMes()
        {
            try
            {
                string usuario =
                    PerfilAcceso.AccesoCompleto
                        ? "SC"
                        : "Esposa";

                string mes =
                    _mesPredicacion.ToString("yyyy-MM");

                double horas =
                    DatabaseHelper.ObtenerHorasMes(
                        usuario,
                        mes);

                int cbs =
                    DatabaseHelper.ObtenerCbsMes(
                        usuario,
                        mes);

                TxtHorasMes.Text =
                    horas.ToString("0.#")
                    + " h  ·  "
                    + cbs.ToString()
                    + " CBs";

                DateTime mesActual =
                    new(DateTime.Today.Year, DateTime.Today.Month, 1);

                int diferencia =
                    (mesActual.Year - _mesPredicacion.Year) * 12
                    + mesActual.Month - _mesPredicacion.Month;

                TxtHorasMesEtiqueta.Text =
                    _mesPredicacion.ToString(
                        "MMMM yyyy",
                        System.Globalization.CultureInfo
                            .GetCultureInfo("es-ES"))
                    + diferencia switch
                    {
                        0 => " · este mes",
                        1 => " · el mes pasado",
                        _ => $" · hace {diferencia} meses"
                    };

                // Los meses futuros no tienen nada que ver: en el mes actual la
                // flecha de avanzar se apaga.
                BtnMesSiguiente.IsEnabled =
                    diferencia > 0;
            }
            catch
            {
            }
        }


        // Mes que enseña la tarjeta de predicación (con las flechas se ven otros).
        private DateTime _mesPredicacion =
            new(DateTime.Today.Year, DateTime.Today.Month, 1);


        private void BtnMesPredicacion_Click(
            object sender,
            RoutedEventArgs e)
        {
            int paso =
                (sender as FrameworkElement)?.Tag is string texto
                && int.TryParse(texto, out int valor)
                    ? valor
                    : 0;

            DateTime nuevo =
                _mesPredicacion.AddMonths(paso);

            if (paso == 0
                || nuevo > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
            {
                return;
            }

            _mesPredicacion = nuevo;
            CargarHorasMes();
        }


        private void BtnSumarHoras_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                // Se abre en el mes que se está viendo: hoy si es el actual; si
                // no, su último día (en la ventana se puede cambiar de día).
                DateTime dia =
                    _mesPredicacion == new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                        ? DateTime.Today
                        : _mesPredicacion.AddMonths(1).AddDays(-1);

                var ventana =
                    new VentanaPredicacion(dia)
                    {
                        Owner =
                            Window.GetWindow(this)
                    };

                if (ventana.ShowDialog() != true)
                {
                    return;
                }

                double horas =
                    ventana.Horas;

                int cbs =
                    ventana.Cbs;

                string usuario =
                    PerfilAcceso.AccesoCompleto
                        ? "SC"
                        : "Esposa";

                DatabaseHelper.GuardarPredicacionDia(
                    usuario,
                    ventana.Fecha,
                    horas,
                    cbs);

                DateTime diaGuardado =
                    DateTime.TryParseExact(
                        ventana.Fecha,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime fechaParseada)
                        ? fechaParseada
                        : DateTime.Today;

                DatabaseHelper.AnotarActividad(
                    "Predicación",
                    $"Guardó el {diaGuardado:dd/MM/yyyy}: "
                    + (horas > 0 ? $"{horas:0.#} h" : "0 h")
                    + (cbs > 0 ? $" y {cbs} CB" + (cbs == 1 ? "" : "s") : "")
                    + $" ({usuario})");

                CargarHorasMes();
            }
            catch
            {
            }
        }


        /// <summary>Abre la actividad del día en Google Maps (coordenadas o dirección).</summary>
        private void BtnMapaActividadInicio_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var actividad =
                    (sender as System.Windows.FrameworkElement)?.Tag
                    as ActividadHoy;

                if (actividad == null)
                {
                    return;
                }

                string consulta =
                    "";

                if (actividad.Latitud != 0
                    || actividad.Longitud != 0)
                {
                    consulta =
                        actividad.Latitud.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        + ","
                        + actividad.Longitud.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (!string.IsNullOrWhiteSpace(
                             actividad.Direccion))
                {
                    consulta =
                        actividad.Direccion.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(
                             _codigoCongregacionPrograma))
                {
                    try
                    {
                        consulta =
                            DatabaseHelper
                                .ObtenerCongregaciones()
                                .FirstOrDefault(c => string.Equals(
                                    c.Codigo,
                                    _codigoCongregacionPrograma,
                                    StringComparison.OrdinalIgnoreCase))
                                ?.DireccionSR
                                ?.Trim()
                            ?? "";
                    }
                    catch
                    {
                        consulta = "";
                    }
                }

                if (string.IsNullOrWhiteSpace(consulta))
                {
                    return;
                }

                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        "https://www.google.com/maps/search/?api=1&query="
                        + Uri.EscapeDataString(consulta))
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }


        private void BtnProgramaDiaAnterior_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_fechaProgramaSeleccionada > _lunesSemanaActual)
            {
                CargarProgramaFecha(
                    _fechaProgramaSeleccionada.AddDays(-1));
            }
        }

        private void BtnProgramaDiaSiguiente_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_fechaProgramaSeleccionada < _domingoSemanaActual)
            {
                CargarProgramaFecha(
                    _fechaProgramaSeleccionada.AddDays(1));
            }
        }

        private static string ObtenerNombreDiaPrograma(
            DayOfWeek dia)
        {
            switch (dia)
            {
                case DayOfWeek.Monday: return "Lunes";
                case DayOfWeek.Tuesday: return "Martes";
                case DayOfWeek.Wednesday: return "Miércoles";
                case DayOfWeek.Thursday: return "Jueves";
                case DayOfWeek.Friday: return "Viernes";
                case DayOfWeek.Saturday: return "Sábado";
                case DayOfWeek.Sunday: return "Domingo";
                default: return "";
            }
        }

        private static string FormatearFechaPrograma(
            DateTime fecha)
        {
            CultureInfo es =
                CultureInfo.GetCultureInfo("es-ES");

            string texto =
                fecha.ToString(
                    "dddd, d 'de' MMMM 'de' yyyy",
                    es);

            return char.ToUpper(texto[0]) + texto.Substring(1);
        }

        private static int ObtenerOrdenHora(string hora)
        {
            if (string.IsNullOrWhiteSpace(hora))
            {
                return int.MaxValue;
            }

            string[] formatos =
            {
                @"h\:mm",
                @"hh\:mm",
                @"h\.mm",
                @"hh\.mm"
            };

            if (TimeSpan.TryParseExact(
                    hora.Trim(),
                    formatos,
                    CultureInfo.InvariantCulture,
                    out TimeSpan ts))
            {
                return (int)ts.TotalMinutes;
            }

            if (DateTime.TryParse(
                    hora,
                    CultureInfo.GetCultureInfo("es-ES"),
                    DateTimeStyles.NoCurrentDateDefault,
                    out DateTime dt))
            {
                return dt.Hour * 60 + dt.Minute;
            }

            return int.MaxValue - 1;
        }

        private static string NormalizarResponsablePrograma(
            string responsable)
        {
            string valor =
                responsable?.Trim() ?? "";

            if (string.Equals(
                    valor,
                    "Ambos",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Ambos";
            }

            if (string.Equals(
                    valor,
                    "Rebe",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    valor,
                    "Esposa",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Esposa";
            }

            return "SC";
        }

        private static bool EsActividadLegacy(string responsable)
        {
            string valor =
                responsable?.Trim() ?? "";

            return
                string.Equals(valor, "Comida", StringComparison.OrdinalIgnoreCase)
                || string.Equals(valor, "Cenar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(valor, "Dormir", StringComparison.OrdinalIgnoreCase);
        }

        private static Persona ObtenerPersonaActual(int personaId)
        {
            if (personaId <= 0)
            {
                return null;
            }

            Persona persona =
                DatabaseHelper.ObtenerPersonaPorId(personaId);

            if (persona != null
                && persona.PersonaRaizId > 0)
            {
                Persona raiz =
                    DatabaseHelper.ObtenerPersonaPorId(
                        persona.PersonaRaizId);

                if (raiz != null)
                {
                    persona = raiz;
                }
            }

            return persona;
        }

        private static string ObtenerNombrePreferido(Persona persona)
        {
            if (persona == null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(persona.NombreComun))
            {
                return persona.NombreComun.Trim();
            }

            if (!string.IsNullOrWhiteSpace(persona.NombreJWHub))
            {
                return persona.NombreJWHub.Trim();
            }

            return persona.Nombre?.Trim() ?? "";
        }

        private void BtnVerVisita_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var semanaActual =
                    DataContext as SemanaCalendario;

                if (semanaActual != null
                    && !string.IsNullOrEmpty(
                        semanaActual.Congregacion))
                {
                    string congreCodigo =
                        DatabaseHelper.ObtenerCodigoCongregacion(
                            semanaActual.Congregacion);

                    VisitasPage.CodigoPendiente = congreCodigo;
                    VisitasPage.NombrePendiente =
                        semanaActual.Congregacion;
                    VisitasPage.MesPendiente =
                        semanaActual.FechaMartes.ToString("yyyy-MM");

                    var ventanaPrincipal =
                        Application.Current.MainWindow as MainWindow;

                    ventanaPrincipal
                        ?.MenuPrincipal
                        .Navigate(typeof(VisitasPage));
                }
                else
                {
                    MessageBox.Show(
                        "No hay datos de una congregación válida para esta semana.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error al intentar abrir la visita: {ex.Message}");
            }
        }

        // =====================================================
        // ESTADÍSTICAS DEL CIRCUITO: SOLO FICHAS ACTUALES
        // =====================================================
        private async Task CargarEstadisticasAsync()
        {
            var stats =
                await Task.Run(
                    ObtenerContadoresDesdeBD);

            TxtTotalPrecReg.Text =
                stats.PrecRegulares.ToString();
            TxtTotalPrecEsp.Text =
                stats.PrecEspeciales.ToString();
            TxtTotalAncianos.Text =
                stats.Ancianos.ToString();
            TxtTotalSM.Text =
                stats.SiervosM.ToString();
            TxtTotalA2.Text =
                stats.SolA2.ToString();
            TxtTotalA19.Text =
                stats.SolA19.ToString();
            TxtTotalA8.Text =
                stats.SolA8.ToString();
            TxtTotalG8.Text =
                stats.SolG8.ToString();

            TxtTotalPersonas.Text =
                stats.Personas.ToString();
            TxtTotalHombres.Text =
                stats.Hombres.ToString();
            TxtTotalMujeres.Text =
                stats.Mujeres.ToString();
            TxtNoBautizados.Text =
                stats.NoBautizados.ToString();
            TxtTotalCBs.Text =
                stats.CursosBiblicos.ToString();
            TxtMediaEdad.Text =
                stats.MediaEdad.ToString(
                    "0.0",
                    CultureInfo.GetCultureInfo("es-ES"));
            TxtMediaAuxiliares.Text =
                stats.MediaAuxiliares.ToString(
                    "0.0",
                    CultureInfo.GetCultureInfo("es-ES"));
            TxtTotalBetel.Text =
                stats.Betel.ToString();

            TxtMesCBs.Text =
                string.IsNullOrWhiteSpace(stats.MesActividadReferencia)
                    ? "sin actividad disponible"
                    : stats.MesActividadReferencia;

            TxtPeriodoAuxiliares.Text =
                stats.MesesMediaAuxiliares > 0
                    ? $"media de {stats.MesesMediaAuxiliares} mes{(stats.MesesMediaAuxiliares == 1 ? "" : "es")}" 
                    : "sin actividad disponible";

            if (CardCBs != null)
            {
                CardCBs.ToolTip =
                    string.IsNullOrWhiteSpace(stats.MesActividadReferencia)
                        ? "No hay actividad mensual disponible."
                        : $"Cursos bíblicos del último mes con actividad: {stats.MesActividadReferencia}.";
            }

            if (CardAuxiliaresMedia != null)
            {
                CardAuxiliaresMedia.ToolTip =
                    stats.MesesMediaAuxiliares > 0
                        ? $"Media mensual de precursores auxiliares calculada sobre los últimos {stats.MesesMediaAuxiliares} meses con actividad disponible."
                        : "No hay actividad mensual disponible para calcular la media.";
            }
        }

        private EstadisticasCircuito ObtenerContadoresDesdeBD()
        {
            var res =
                new EstadisticasCircuito();

            try
            {
                // ObtenerPersonas() es la misma fuente que usa PersonasPage y
                // devuelve solo las filas actuales (MesHistorico IS NULL).
                List<Persona> personasActuales =
                    DatabaseHelper.ObtenerPersonas()
                        .Where(p =>
                            string.IsNullOrWhiteSpace(
                                p.FechaEliminacion))
                        .ToList();

                // Todas las tarjetas del resumen se basan únicamente en
                // fichas raíz actuales y activas. ObtenerPersonas() ya excluye
                // MesHistorico; aquí excluimos además las bajas (soft delete).
                res.Personas =
                    personasActuales.Count;

                res.Hombres =
                    personasActuales.Count(p =>
                        string.Equals(
                            p.Sexo?.Trim(),
                            "Hombre",
                            StringComparison.OrdinalIgnoreCase));

                res.Mujeres =
                    personasActuales.Count(p =>
                        string.Equals(
                            p.Sexo?.Trim(),
                            "Mujer",
                            StringComparison.OrdinalIgnoreCase));

                res.NoBautizados =
                    personasActuales.Count(p =>
                        string.IsNullOrWhiteSpace(
                            p.FechaBautismo));

                var edades =
                    personasActuales
                        .Select(p => CalcularEdadActual(p.FechaNacimiento))
                        .Where(e => e.HasValue)
                        .Select(e => (double)e.Value)
                        .ToList();

                res.MediaEdad =
                    edades.Count > 0
                        ? edades.Average()
                        : 0;

                res.Betel =
                    personasActuales.Count(p =>
                        Contiene(p.Privilegios, "Asesor de Betel")
                        || Contiene(p.Privilegios, "Voluntario a distancia"));

                res.PrecRegulares =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Servicio,
                            "Precursor regular"));

                res.PrecEspeciales =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Servicio,
                            "Precursor especial"));

                res.Ancianos =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Nombramiento,
                            "Anciano"));

                res.SiervosM =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Nombramiento,
                            "Siervo ministerial"));

                res.SolA2 =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Solicitudes,
                            "A-2"));

                res.SolA19 =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Solicitudes,
                            "A-19"));

                res.SolA8 =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Solicitudes,
                            "A-8"));

                res.SolG8 =
                    personasActuales.Count(p =>
                        Contiene(
                            p.Solicitudes,
                            "G-8"));

                CargarResumenActividadActual(res);
            }
            catch
            {
                // Si la BD aún no está inicializada, se muestran ceros.
            }

            return res;
        }

        private static void CargarResumenActividadActual(
            EstadisticasCircuito res)
        {
            // Misma regla que Android: SincroCompartido/ResumenActividadInicio.cs
            // (solo meses con informes de verdad, no las filas preparadas).
            using var connection =
                new SqliteConnection(
                    $"Data Source={DatabaseHelper.RutaBaseDatos};Pooling=True");

            connection.Open();

            ResumenActividadInicio.Resultado resumen =
                ResumenActividadInicio.Calcular(
                    connection,
                    DateTime.Today);

            if (resumen.MesReferencia is not DateTime ultimoMes)
            {
                return;
            }

            res.CursosBiblicos =
                resumen.CursosBiblicos;

            res.MesActividadReferencia =
                ultimoMes
                    .ToString("MMM yyyy", CultureInfo.GetCultureInfo("es-ES"))
                    .TrimEnd('.');

            res.MesesMediaAuxiliares =
                resumen.MesesMediaAuxiliares;

            res.MediaAuxiliares =
                resumen.MediaAuxiliares;
        }

        private static int? CalcularEdadActual(
            string fechaNacimiento)
        {
            if (!TryParseFechaPersona(
                    fechaNacimiento,
                    out DateTime nacimiento))
            {
                return null;
            }

            if (nacimiento.Year < 1900
                || nacimiento.Date > DateTime.Today)
            {
                return null;
            }

            int edad =
                DateTime.Today.Year
                - nacimiento.Year;

            if (nacimiento.Date
                > DateTime.Today.AddYears(-edad))
            {
                edad--;
            }

            return edad >= 0 && edad <= 120
                ? edad
                : null;
        }

        private static bool TryParseFechaPersona(
            string texto,
            out DateTime fecha)
        {
            fecha = default;

            if (string.IsNullOrWhiteSpace(texto))
            {
                return false;
            }

            string[] formatos =
            {
                "dd/MM/yyyy",
                "d/M/yyyy",
                "yyyy-MM-dd",
                "dd-MM-yyyy",
                "d-M-yyyy"
            };

            if (DateTime.TryParseExact(
                    texto.Trim(),
                    formatos,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fecha))
            {
                return true;
            }

            return DateTime.TryParse(
                texto.Trim(),
                CultureInfo.GetCultureInfo("es-ES"),
                DateTimeStyles.None,
                out fecha);
        }

        private static bool Contiene(
            string texto,
            string valor)
        {
            return
                !string.IsNullOrWhiteSpace(texto)
                && texto.IndexOf(
                    valor,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void NavegarAPersonas()
        {
            var ventanaPrincipal =
                Application.Current.MainWindow as MainWindow;

            ventanaPrincipal
                ?.MenuPrincipal
                .Navigate(typeof(PersonasPage));
        }

        private void CardPrecReg_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteServicio = "Precursor regular"; NavegarAPersonas(); }
        private void CardPrecEsp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteServicio = "Precursor especial"; NavegarAPersonas(); }
        private void CardAncianos_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteNombramiento = "Anciano"; NavegarAPersonas(); }
        private void CardSiervos_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteNombramiento = "Siervo ministerial"; NavegarAPersonas(); }
        private void CardA2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-2"; NavegarAPersonas(); }
        private void CardA19_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-19"; NavegarAPersonas(); }
        private void CardA8_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "A-8"; NavegarAPersonas(); }
        private void CardG8_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { PersonasPage.FiltroPendienteSolicitud = "G-8"; NavegarAPersonas(); }

        private class EstadisticasCircuito
        {
            public int Personas { get; set; }
            public int Hombres { get; set; }
            public int Mujeres { get; set; }
            public int NoBautizados { get; set; }
            public int CursosBiblicos { get; set; }
            public double MediaEdad { get; set; }
            public double MediaAuxiliares { get; set; }
            public int MesesMediaAuxiliares { get; set; }
            public string MesActividadReferencia { get; set; } = "";
            public int Betel { get; set; }

            public int PrecRegulares { get; set; }
            public int PrecEspeciales { get; set; }
            public int Ancianos { get; set; }
            public int SiervosM { get; set; }
            public int SolA2 { get; set; }
            public int SolA19 { get; set; }
            public int SolA8 { get; set; }
            public int SolG8 { get; set; }
        }

        // =========================================================
        // TIEMPO (AEMET): el del día del programa (o de hoy), donde se va a
        // estar: el salón de la visita o, si no, donde está el móvil.
        // =========================================================

        private System.Threading.CancellationTokenSource _cargaTiempo;

        private async Task CargarTiempoAsync(DateTime dia)
        {
            _cargaTiempo?.Cancel();

            var cancelacion =
                _cargaTiempo = new System.Threading.CancellationTokenSource();

            try
            {
                LugarTiempo lugar =
                    await Task.Run(() => TiempoPc.LugarParaDia(dia));

                TiempoDia tiempo =
                    lugar == null
                        ? null
                        : await Task.Run(() => TiempoAemet.ObtenerAsync(lugar, dia, false, cancelacion.Token));

                if (cancelacion.IsCancellationRequested)
                {
                    return;
                }

                PintarTiempo(tiempo);
            }
            catch
            {
            }
        }

        private void PintarTiempo(TiempoDia tiempo)
        {
            // Sin sitio (ni visita ni ubicación del móvil): no hay tarjeta.
            if (tiempo == null)
            {
                TarjetaTiempo.Visibility = Visibility.Collapsed;
                return;
            }

            var es = CultureInfo.GetCultureInfo("es-ES");

            TarjetaTiempo.Visibility = Visibility.Visible;
            TxtTituloTiempo.Text = tiempo.Lugar.ToUpper(es);
            TxtMaxMinTiempo.Text =
                (tiempo.Maxima is int maxima && tiempo.Minima is int minima ? $"↑ {maxima}°   ↓ {minima}°" : "")
                + (tiempo.ProbLluviaDia is int lluvia && lluvia > 0 ? $"   ·   lluvia {lluvia}%" : "");

            if (tiempo.SinPrevision)
            {
                ListaHorasTiempo.ItemsSource = null;
                TxtPieTiempo.Text = "AEMET todavía no tiene previsión para este día.";
                TxtPieTiempo.Visibility = Visibility.Visible;
                return;
            }

            TxtPieTiempo.Visibility = Visibility.Collapsed;

            bool esHoy = tiempo.Fecha == DateTime.Today;
            int horaActual = DateTime.Now.Hour;

            // Todas las horas del día (desde las 6, o antes si ya es de madrugada).
            List<HoraTiempo> visibles =
                tiempo.Horas
                    .Where(h => !tiempo.PorHoras || h.Hora >= (esHoy ? Math.Min(6, horaActual) : 6))
                    .ToList();

            ListaHorasTiempo.ItemsSource =
                visibles.Select(h => new CeldaTiempo(h, esHoy && h.Hora == horaActual)).ToList();

            // Hoy se ve desde la hora actual (las anteriores, desplazando a la izquierda).
            int indice = esHoy ? Math.Max(0, visibles.FindIndex(h => h.Hora == horaActual)) : 0;

            Dispatcher.BeginInvoke(
                new Action(() => ScrollHorasTiempo.ScrollToHorizontalOffset(indice * 74)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // La rueda mueve la página, no la fila de horas (esa va con su barra).
        private void ScrollTiempo_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not FrameworkElement elemento || elemento.Parent is not UIElement padre)
            {
                return;
            }

            e.Handled = true;

            padre.RaiseEvent(
                new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                });
        }

        internal static System.Windows.Media.Brush Pincel(string color)
        {
            var pincel =
                new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

            pincel.Freeze();

            return pincel;
        }

        // Al pulsar el icono de un aviso, qué aviso es (sin depender del color).
        private void AvisoTiempo_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            MostrarAvisos(sender);
        }

        /// <summary>Explica los avisos de la hora del icono pulsado (Inicio y Circuito).</summary>
        internal static void MostrarAvisos(object sender)
        {
            if (sender is FrameworkElement icono && icono.DataContext is CeldaTiempo celda && celda.TextoAvisos.Length > 0)
            {
                MessageBox.Show(Window.GetWindow(icono), celda.TextoAvisos, "Avisos de AEMET", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>El icono de Fluent (y su color) de un cielo de AEMET, como en Android.</summary>
        internal static (Wpf.Ui.Controls.SymbolRegular Simbolo, System.Windows.Media.Brush Color) SimboloTiempo(string icono) =>
            icono switch
            {
                "tiempo_sol" => (Wpf.Ui.Controls.SymbolRegular.WeatherSunny24, Pincel("#F5C451")),
                "tiempo_luna" => (Wpf.Ui.Controls.SymbolRegular.WeatherMoon24, Pincel("#C9D1D9")),
                "tiempo_sol_nubes" => (Wpf.Ui.Controls.SymbolRegular.WeatherPartlyCloudyDay24, Pincel("#E3C46A")),
                "tiempo_luna_nubes" => (Wpf.Ui.Controls.SymbolRegular.WeatherPartlyCloudyNight24, Pincel("#C9D1D9")),
                "tiempo_lluvia" => (Wpf.Ui.Controls.SymbolRegular.WeatherRain24, Pincel("#4BA3E3")),
                "tiempo_llovizna" => (Wpf.Ui.Controls.SymbolRegular.WeatherDrizzle24, Pincel("#7FB8E6")),
                "tiempo_tormenta" => (Wpf.Ui.Controls.SymbolRegular.WeatherThunderstorm24, Pincel("#B48EAD")),
                "tiempo_nieve" => (Wpf.Ui.Controls.SymbolRegular.WeatherSnow24, Pincel("#E6E8EB")),
                "tiempo_niebla" => (Wpf.Ui.Controls.SymbolRegular.WeatherFog24, Pincel("#8A929C")),
                _ => (Wpf.Ui.Controls.SymbolRegular.WeatherCloudy24, Pincel("#A7AEB8"))
            };

        /// <summary>Una hora (o tramo) del tiempo, con los iconos de Fluent como en Android.</summary>
        public class CeldaTiempo
        {
            public CeldaTiempo(HoraTiempo hora, bool destacada)
            {
                Etiqueta = hora.Etiqueta;
                Temperatura = hora.Temperatura is int t ? $"{t}°" : "";
                Lluvia = hora.TextoLluvia;
                Descripcion = hora.Descripcion + (hora.Aviso == null ? "" : "\n" + hora.Aviso.Titulo + " · " + hora.Aviso.Periodo);
                Fondo = destacada ? Pincel("#1FFFFFFF") : System.Windows.Media.Brushes.Transparent;
                Ancho = hora.Hora < 0 ? 92 : 70;
                PuntosAviso = hora.Aviso?.Puntos ?? "";
                TextoAvisos = hora.TextoAvisos;
                VerAviso = hora.Aviso == null ? Visibility.Collapsed : Visibility.Visible;
                ColorAviso = Pincel(hora.Aviso?.Color ?? "#E5C07B");

                (Simbolo, ColorIcono) = SimboloTiempo(hora.Icono);
            }

            public string Etiqueta { get; }
            public Wpf.Ui.Controls.SymbolRegular Simbolo { get; }
            public System.Windows.Media.Brush ColorIcono { get; }
            public string Temperatura { get; }
            public string Lluvia { get; }
            public double Ancho { get; }
            public string PuntosAviso { get; }
            public string TextoAvisos { get; }
            public Visibility VerAviso { get; }
            public System.Windows.Media.Brush ColorAviso { get; }
            public System.Windows.Media.Brush Fondo { get; }
            public string Descripcion { get; }
        }

        public class ActividadHoy
        {
            public string Hora { get; set; }
            public string Responsable { get; set; }
            public string TipoActividad { get; set; }
            public string CompaneroNombre { get; set; }
            public string Ubicacion { get; set; }
            public string Direccion { get; set; }
            public double Latitud { get; set; }
            public double Longitud { get; set; }
            public Visibility VerCompanero { get; set; }
            public Visibility VerUbicacion { get; set; }
            public Visibility VerResponsable { get; set; }
            public Visibility VerMapa { get; set; }
        }
    }
}
