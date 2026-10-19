using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

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

                PanelVisita.Visibility = Visibility.Collapsed;
                PanelOtraActividad.Visibility = Visibility.Collapsed;
                PanelProgramaDia.Visibility = Visibility.Collapsed;
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
                VerCompanero = Visibility.Visible,
                VerUbicacion =
                    string.IsNullOrWhiteSpace(ubicacion)
                        ? Visibility.Collapsed
                        : Visibility.Visible
            };
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
            var registros =
                new List<ActividadResumenCircuito>();

            string dbPath =
                DatabaseHelper.RutaBaseDatos;

            using (var connection =
                   new SqliteConnection(
                       $"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();

                using var cmd =
                    connection.CreateCommand();

                cmd.CommandText = @"
                    SELECT
                        a.PersonaId,
                        a.Mes,
                        a.AnioServicio,
                        a.CursosBiblicos,
                        a.PrecursorAuxiliar
                    FROM ActividadPersonas a
                    INNER JOIN Personas p
                        ON p.Id = a.PersonaId
                    WHERE p.MesHistorico IS NULL
                      AND (p.FechaEliminacion IS NULL
                           OR trim(p.FechaEliminacion) = '');";

                using var reader =
                    cmd.ExecuteReader();

                DateTime mesActual =
                    new DateTime(
                        DateTime.Today.Year,
                        DateTime.Today.Month,
                        1);

                while (reader.Read())
                {
                    string mes =
                        reader.IsDBNull(1)
                            ? ""
                            : reader.GetString(1);

                    string anioServicio =
                        reader.IsDBNull(2)
                            ? ""
                            : reader.GetString(2);

                    DateTime? mesCalendario =
                        ObtenerMesCalendarioActividad(
                            mes,
                            anioServicio);

                    if (!mesCalendario.HasValue
                        || mesCalendario.Value > mesActual)
                    {
                        continue;
                    }

                    int cursos = 0;
                    if (!reader.IsDBNull(3))
                    {
                        int.TryParse(
                            reader.GetValue(3)?.ToString(),
                            out cursos);
                    }

                    bool auxiliar =
                        !reader.IsDBNull(4)
                        && Convert.ToInt32(
                            reader.GetValue(4)) == 1;

                    registros.Add(
                        new ActividadResumenCircuito
                        {
                            PersonaId =
                                reader.GetInt32(0),
                            MesCalendario =
                                mesCalendario.Value,
                            CursosBiblicos =
                                Math.Max(0, cursos),
                            PrecursorAuxiliar =
                                auxiliar
                        });
                }
            }

            var mesesConDatos =
                registros
                    .Select(r => r.MesCalendario)
                    .Distinct()
                    .OrderByDescending(m => m)
                    .ToList();

            if (mesesConDatos.Count == 0)
            {
                return;
            }

            DateTime ultimoMes =
                mesesConDatos[0];

            res.CursosBiblicos =
                registros
                    .Where(r =>
                        r.MesCalendario == ultimoMes)
                    .Sum(r =>
                        r.CursosBiblicos);

            CultureInfo es =
                CultureInfo.GetCultureInfo("es-ES");

            res.MesActividadReferencia =
                ultimoMes
                    .ToString("MMM yyyy", es)
                    .TrimEnd('.');

            var ultimosMeses =
                mesesConDatos
                    .Take(6)
                    .ToList();

            res.MesesMediaAuxiliares =
                ultimosMeses.Count;

            if (ultimosMeses.Count > 0)
            {
                var auxiliaresPorMes =
                    ultimosMeses
                        .Select(m =>
                            registros
                                .Where(r =>
                                    r.MesCalendario == m
                                    && r.PrecursorAuxiliar)
                                .Select(r => r.PersonaId)
                                .Distinct()
                                .Count())
                        .ToList();

                res.MediaAuxiliares =
                    auxiliaresPorMes.Average();
            }
        }

        private static DateTime? ObtenerMesCalendarioActividad(
            string mes,
            string anioServicio)
        {
            if (string.IsNullOrWhiteSpace(mes)
                || !int.TryParse(
                    anioServicio?.Trim(),
                    out int anioServicioNumero))
            {
                return null;
            }

            string normalizado =
                QuitarAcentos(
                    mes.Trim().ToLowerInvariant());

            string[] meses =
            {
                "enero", "febrero", "marzo", "abril",
                "mayo", "junio", "julio", "agosto",
                "septiembre", "octubre", "noviembre", "diciembre"
            };

            int numeroMes =
                Array.IndexOf(
                    meses,
                    normalizado) + 1;

            if (numeroMes <= 0)
            {
                return null;
            }

            // El año de servicio 2026 comprende septiembre-diciembre de 2025
            // y enero-agosto de 2026.
            int anioCalendario =
                numeroMes >= 9
                    ? anioServicioNumero - 1
                    : anioServicioNumero;

            if (anioCalendario < 1900
                || anioCalendario > 2200)
            {
                return null;
            }

            return new DateTime(
                anioCalendario,
                numeroMes,
                1);
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

        private static string QuitarAcentos(
            string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            string normalizado =
                texto.Normalize(
                    System.Text.NormalizationForm.FormD);

            var chars =
                normalizado
                    .Where(c =>
                        CharUnicodeInfo.GetUnicodeCategory(c)
                        != UnicodeCategory.NonSpacingMark)
                    .ToArray();

            return new string(chars)
                .Normalize(
                    System.Text.NormalizationForm.FormC);
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

        private class ActividadResumenCircuito
        {
            public int PersonaId { get; set; }
            public DateTime MesCalendario { get; set; }
            public int CursosBiblicos { get; set; }
            public bool PrecursorAuxiliar { get; set; }
        }

        public class ActividadHoy
        {
            public string Hora { get; set; }
            public string Responsable { get; set; }
            public string TipoActividad { get; set; }
            public string CompaneroNombre { get; set; }
            public string Ubicacion { get; set; }
            public Visibility VerCompanero { get; set; }
            public Visibility VerUbicacion { get; set; }
            public Visibility VerResponsable { get; set; }
        }
    }
}
