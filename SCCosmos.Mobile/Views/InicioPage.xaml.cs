using System.Collections.ObjectModel;
using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class InicioPage : ContentPage, IActualizableMobile
{
    public ObservableCollection<ActividadHoy>
        ProgramaHoy
    { get; } = new();

    private SemanaCalendario? _semanaActual;

    private SemanaCalendario? _proximaActividad;

    public InicioPage()
    {
        InitializeComponent();

        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarPaginaAsync();

        // Mantiene al día las notificaciones del sistema y el contador
        // del icono de la app.
        _ = AlertasNotificacionService
            .ActualizarAsync();
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarPaginaAsync();
        }
        catch
        {
        }
    }


    private async Task CargarPaginaAsync()
    {
        LblError.IsVisible = false;

        try
        {
            if (!DatabaseHelper.ExisteBaseLocal)
            {
                MostrarSinBaseDatos();
                return;
            }

            // La esposa del SC no ve la campana de alertas ni recibe
            // notificaciones.
            PanelCampana.IsVisible =
                PerfilAccesoService.AccesoCompleto;

            // La campana se pinta según las alertas pendientes: gris si
            // no hay ninguna, dorada con el número si las hay.
            try
            {
                int pendientes =
                    (await Task.Run(
                        () => AlertasMobileService.Calcular()))
                    .Count;

                ActualizarCampana(
                    pendientes);
            }
            catch
            {
            }

            // Si el dispositivo lo usa la esposa del SC, el saludo es con su
            // nombre (Nombre de la esposa); si no está puesto, se usa el
            // nombre del SC.
            bool esEsposa =
                PerfilAccesoService.Configurado
                && !PerfilAccesoService.EsHombre;

            string nombre =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerConfiguracion(
                        esEsposa
                            ? "NombreEsposaSC"
                            : "NombreSC",
                        ""));

            if (string.IsNullOrWhiteSpace(nombre)
                && esEsposa)
            {
                nombre =
                    await Task.Run(() =>
                        DatabaseHelper.ObtenerConfiguracion(
                            "NombreSC",
                            ""));
            }

            TxtSaludo.Text =
                string.IsNullOrWhiteSpace(nombre)
                    ? "¡Hola!"
                    : $"¡Hola, {nombre}!";

            await Task.WhenAll(
                CargarSemanaActualAsync(),
                CargarEstadisticasAsync());
        }
        catch (Exception ex)
        {
            LblError.Text =
                $"No se pudo cargar Inicio: {ex.Message}";

            LblError.IsVisible = true;
        }
    }

    /// <summary>
    /// Pinta la campana: gris si no hay alertas, dorada con el número
    /// de alertas pendientes si las hay.
    /// </summary>
    private void ActualizarCampana(
        int pendientes)
    {
        try
        {
            bool hay =
                pendientes > 0;

            LblCampana.Text =
                hay ? "🔔" : "🔕";

            LblCampana.Opacity =
                hay ? 1.0 : 0.45;

            BadgeCampana.IsVisible =
                hay;

            LblCampanaNumero.Text =
                pendientes > 99
                    ? "99+"
                    : pendientes.ToString();
        }
        catch
        {
        }
    }


    private async Task CargarSemanaActualAsync()
    {
        DateTime hoy =
            DateTime.Today;

        int diferencia =
            hoy.DayOfWeek -
            DayOfWeek.Tuesday;

        if (diferencia < 0)
            diferencia += 7;

        DateTime martesDeEstaSemana =
            hoy.AddDays(-diferencia).Date;

        string fechaIso =
            martesDeEstaSemana.ToString(
                "yyyy-MM-dd");

        await CargarProximaActividadAsync(
            martesDeEstaSemana);

        SemanaCalendario? semana =
            await Task.Run(() =>
                DatabaseHelper.ObtenerSemana(
                    fechaIso));

        _semanaActual = semana;

        ProgramaHoy.Clear();

        PanelProgramaDia.IsVisible = false;
        PanelVisita.IsVisible = false;
        PanelOtraActividad.IsVisible = false;
        TxtSinDatos.IsVisible = false;

        if (semana == null)
        {
            LblTipoSemana.Text =
                "SEMANA ACTUAL";

            LblActividad.Text =
                "Sin actividad";

            LblFechas.Text =
                FormatearRango(
                    martesDeEstaSemana);

            TxtSinDatos.IsVisible = true;

            return;
        }

        LblFechas.Text =
            semana.RangoFechas;

        LblActividad.Text =
            string.IsNullOrWhiteSpace(
                semana.Actividad)
                ? "Sin actividad"
                : semana.Actividad;

        if (EsVisita(semana))
        {
            LblTipoSemana.Text =
                "VISITA A CONGREGACIÓN";

            LblIconoSemana.Text =
                "●";

            PanelVisita.IsVisible = true;

            LblCongregacion.Text =
                semana.Congregacion;

            if (!string.IsNullOrWhiteSpace(
                    semana.Acompanante))
            {
                LblAcompanante.Text =
                    $"Acompañante: {semana.AcompananteVisual}";

                LblAcompanante.IsVisible =
                    true;
            }
            else
            {
                LblAcompanante.IsVisible =
                    false;
            }

            string codigo =
                await Task.Run(() =>
                    DatabaseHelper
                        .ObtenerCodigoCongregacion(
                            semana.Congregacion));

            if (!string.IsNullOrWhiteSpace(codigo))
            {
                string mes =
                    martesDeEstaSemana.ToString(
                        "yyyy-MM");

                await CargarProgramaDelDiaAsync(
                    codigo,
                    mes,
                    hoy.DayOfWeek);
            }
        }
        else
        {
            LblTipoSemana.Text =
                "ACTIVIDAD DE LA SEMANA";

            LblIconoSemana.Text =
                "◆";

            PanelOtraActividad.IsVisible =
                true;

            string detalle =
                semana.CongregacionVisual;

            if (string.IsNullOrWhiteSpace(detalle))
            {
                detalle =
                    semana.Tipo;
            }

            LblOtraActividad.Text =
                detalle;
        }
    }

    /// <summary>
    /// Comprueba si la semana es una visita, como hace Windows: se admite
    /// cualquier variante de mayúsculas/minúsculas o espacios y también el
    /// tipo «Visita SC».
    /// </summary>
    private static bool EsVisita(
        SemanaCalendario semana)
    {
        string actividad =
            (semana.Actividad ?? "").Trim();

        string tipo =
            (semana.Tipo ?? "").Trim();

        return actividad.Equals(
                   "Visita",
                   StringComparison.OrdinalIgnoreCase)
               ||
               tipo.StartsWith(
                   "Visita",
                   StringComparison.OrdinalIgnoreCase);
    }


    private int _columnasEstadisticas = 2;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
        {
            return;
        }

        // Tablet o fold abierto: el resumen reparte las tarjetas en más
        // columnas para aprovechar el ancho.
        int columnas =
            width >= 900 ? 4 : width >= 600 ? 3 : 2;

        if (columnas != _columnasEstadisticas)
        {
            _columnasEstadisticas = columnas;
            AdaptarEstadisticas(columnas);
        }
    }

    private void AdaptarEstadisticas(int columnas)
    {
        if (GridEstadisticas == null)
        {
            return;
        }

        GridEstadisticas.ColumnDefinitions.Clear();

        for (int i = 0; i < columnas; i++)
        {
            GridEstadisticas.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }

        int filas =
            (GridEstadisticas.Children.Count + columnas - 1) / columnas;

        GridEstadisticas.RowDefinitions.Clear();

        for (int i = 0; i < filas; i++)
        {
            GridEstadisticas.RowDefinitions.Add(
                new RowDefinition(new GridLength(1, GridUnitType.Auto)));
        }

        for (int i = 0; i < GridEstadisticas.Children.Count; i++)
        {
            IView hijo =
                GridEstadisticas.Children[i];

            GridEstadisticas.SetRow(hijo, i / columnas);
            GridEstadisticas.SetColumn(hijo, i % columnas);
        }
    }

    private async Task CargarProgramaDelDiaAsync(
        string congreCodigo,
        string mesReferencia,
        DayOfWeek diaHoy)
    {
        string diaDb = diaHoy switch
        {
            DayOfWeek.Wednesday => "Miércoles",
            DayOfWeek.Thursday => "Jueves",
            DayOfWeek.Friday => "Viernes",
            DayOfWeek.Saturday => "Sábado",
            DayOfWeek.Sunday => "Domingo",
            _ => ""
        };

        if (string.IsNullOrEmpty(diaDb))
            return;

        var programa =
            await Task.Run(() =>
            {
                var resultado =
                    new List<ActividadHoy>();

                // Se leen TODAS las actividades del día (SC, Esposa, Ambos,
                // Joni, Rebe...), como en Windows, para que aparezcan tanto
                // las del hombre como las de la mujer.
                List<ActividadPrograma> actividades =
                    DatabaseHelper
                        .ObtenerActividadesProgramaDia(
                            congreCodigo,
                            mesReferencia,
                            diaDb);

                foreach (ActividadPrograma actividad
                         in actividades)
                {
                    string responsableCrudo =
                        (actividad.Responsable ?? "").Trim();

                    bool esLegacy =
                        responsableCrudo.Equals("Comida", StringComparison.OrdinalIgnoreCase)
                        || responsableCrudo.Equals("Cenar", StringComparison.OrdinalIgnoreCase)
                        || responsableCrudo.Equals("Dormir", StringComparison.OrdinalIgnoreCase);

                    string responsable =
                        responsableCrudo.Equals("Ambos", StringComparison.OrdinalIgnoreCase)
                            ? "Ambos"
                            : (responsableCrudo.Equals("Rebe", StringComparison.OrdinalIgnoreCase)
                               || responsableCrudo.Equals("Esposa", StringComparison.OrdinalIgnoreCase))
                                ? "Esposa"
                                : "SC";

                    if (esLegacy)
                    {
                        responsable = "";
                    }

                    string tipo =
                        (actividad.TipoActividad ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(tipo)
                        && esLegacy)
                    {
                        tipo = responsableCrudo;
                    }

                    if (string.IsNullOrWhiteSpace(tipo))
                    {
                        tipo = "Actividad";
                    }

                    string nombreCompanero =
                        "";

                    if (!esLegacy
                        && actividad.CompaneroId > 0)
                    {
                        Persona? persona =
                            DatabaseHelper
                                .ObtenerPersonaPorId(
                                    actividad.CompaneroId);

                        if (persona != null &&
                            !string.IsNullOrWhiteSpace(
                                persona.Nombre))
                        {
                            nombreCompanero =
                                persona.Nombre
                                    .Split(
                                        ' ',
                                        StringSplitOptions
                                            .RemoveEmptyEntries)
                                    .FirstOrDefault()
                                ?? "";
                        }
                    }

                    resultado.Add(
                        new ActividadHoy
                        {
                            Hora =
                                actividad.Hora,

                            TipoActividad =
                                tipo,

                            Responsable =
                                responsable,

                            CompaneroNombre =
                                nombreCompanero,

                            VerCompanero =
                                !esLegacy &&
                                !string.IsNullOrWhiteSpace(
                                    nombreCompanero),

                            Ubicacion =
                                (actividad.Direccion ?? "").Trim()
                        });
                }

                return resultado
                    .OrderBy(x => x.Hora)
                    .ToList();
            });

        foreach (ActividadHoy actividad
                 in programa)
        {
            ProgramaHoy.Add(actividad);
        }

        PanelProgramaDia.IsVisible =
            ProgramaHoy.Count > 0;
    }

    private async Task CargarEstadisticasAsync()
    {
        var stats =
            await Task.Run(() =>
            {
                List<Persona> personasActivas =
                    DatabaseHelper
                        .ObtenerPersonas()
                        .Where(p =>
                            string.IsNullOrWhiteSpace(
                                p.FechaEliminacion))
                        .ToList();

                int precRegulares =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Servicio,
                            "Precursor regular"));

                int precEspeciales =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Servicio,
                            "Precursor especial"));

                int ancianos =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Nombramiento,
                            "Anciano"));

                int siervosM =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Nombramiento,
                            "Siervo ministerial"));

                int solA2 =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Solicitudes,
                            "A-2"));

                int solA19 =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Solicitudes,
                            "A-19"));

                int solA8 =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Solicitudes,
                            "A-8"));

                int solG8 =
                    personasActivas.Count(p =>
                        ContieneTexto(
                            p.Solicitudes,
                            "G-8"));

                int totalPublicadores =
                    personasActivas.Count;

                int hombres =
                    personasActivas.Count(p =>
                        string.Equals(
                            p.Sexo,
                            "Hombre",
                            StringComparison.OrdinalIgnoreCase));

                int mujeres =
                    personasActivas.Count(p =>
                        string.Equals(
                            p.Sexo,
                            "Mujer",
                            StringComparison.OrdinalIgnoreCase));

                int noBautizados =
                    personasActivas.Count(p =>
                        string.IsNullOrWhiteSpace(
                            p.FechaBautismo));

                int betel =
                    personasActivas.Count(p =>
                        ContieneTexto(p.Servicio, "Betel")
                        ||
                        ContieneTexto(p.Privilegios, "Betel"));

                List<int> edades =
                    personasActivas
                        .Select(p =>
                            EscuelasService.CalcularEdad(
                                p.FechaNacimiento,
                                DateTime.Today))
                        .Where(e => e != "-"
                                    &&
                                    int.TryParse(e, out _))
                        .Select(int.Parse)
                        .ToList();

                string mediaEdad =
                    edades.Count > 0
                        ? Math.Round(edades.Average(), 1)
                              .ToString()
                        : "—";

                DatabaseHelper.EstadisticasExtraMobile extra =
                    DatabaseHelper
                        .ObtenerEstadisticasExtraMobile();

                return (
                    precRegulares,
                    precEspeciales,
                    ancianos,
                    siervosM,
                    solA2,
                    solA19,
                    solA8,
                    solG8,
                    totalPublicadores,
                    hombres,
                    mujeres,
                    noBautizados,
                    betel,
                    mediaEdad,
                    extra);
            });

        TxtTotalPrecReg.Text =
            stats.precRegulares.ToString();

        TxtTotalPrecEsp.Text =
            stats.precEspeciales.ToString();

        TxtTotalAncianos.Text =
            stats.ancianos.ToString();

        TxtTotalSM.Text =
            stats.siervosM.ToString();

        TxtTotalA2.Text =
            stats.solA2.ToString();

        TxtTotalA19.Text =
            stats.solA19.ToString();

        TxtTotalA8.Text =
            stats.solA8.ToString();

        TxtTotalG8.Text =
            stats.solG8.ToString();

        TxtTotalPublicadores.Text =
            stats.totalPublicadores.ToString();

        TxtTotalHombres.Text =
            stats.hombres.ToString();

        TxtTotalMujeres.Text =
            stats.mujeres.ToString();

        TxtTotalNoBautizados.Text =
            stats.noBautizados.ToString();

        TxtTotalBetel.Text =
            stats.betel.ToString();

        TxtMediaEdad.Text =
            stats.mediaEdad;

        TxtTotalCursos.Text =
            stats.extra.CursosUltimoMes.ToString();

        TxtMediaPrecAux.Text =
            stats.extra.MediaPrecursoresAuxiliares
                .ToString("0.#");
    }


    private static bool ContieneTexto(
        string? texto,
        string valor)
    {
        return
            !string.IsNullOrWhiteSpace(texto)
            &&
            texto.Contains(
                valor,
                StringComparison.OrdinalIgnoreCase);
    }

    private void MostrarSinBaseDatos()
    {
        TxtSaludo.Text =
            "¡Hola!";

        LblActividad.Text =
            "Base de datos no disponible";

        LblFechas.Text =
            "Ve a Configuración y actualiza desde la carpeta sincronizada.";

        PanelVisita.IsVisible = false;
        PanelOtraActividad.IsVisible = false;
        PanelProgramaDia.IsVisible = false;

        TxtSinDatos.IsVisible = true;

        TxtSinDatos.Text =
            "Todavía no hay una base de datos local.";
    }

    private static string FormatearRango(
        DateTime martes)
    {
        DateTime lunes =
            martes.AddDays(-1);

        DateTime domingo =
            martes.AddDays(5);

        return
            $"Del {lunes:dd-MMM} al {domingo:dd-MMM}"
                .ToLower();
    }

    private async void BtnAlertas_Click(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new AlertasPage());
    }

    private async void Campana_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await Navigation.PushAsync(
            new AlertasPage());
    }

    private async void BtnVerVisitaInicio_Click(
        object sender,
        EventArgs e)
    {
        if (_semanaActual is null)
        {
            return;
        }

        await Navigation.PushAsync(new VisitaDetallePage(
                    _semanaActual));
    }


    private async Task CargarProximaActividadAsync(
        DateTime martesDeEstaSemana)
    {
        try
        {
            _proximaActividad =
                await Task.Run(() =>
                    DatabaseHelper.ObtenerProximaActividad(
                        martesDeEstaSemana));

            if (_proximaActividad is null)
            {
                LblProximaActividad.IsVisible =
                    false;

                return;
            }

            LblProximaActividad.Text =
                "Próxima: " +
                TextoActividadProxima(
                    _proximaActividad);

            LblProximaActividad.IsVisible =
                true;
        }
        catch
        {
            LblProximaActividad.IsVisible =
                false;
        }
    }


    private static string TextoActividadProxima(
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

        return actividad + " (" +
            semana.RangoFechas.ToLower() + ")";
    }


    private async void ProximaActividad_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (_proximaActividad is null)
        {
            return;
        }

        CalendarioPage.SemanaDestacadaPendiente =
            _proximaActividad.FechaMartes
                .ToString("yyyy-MM-dd");

        await Shell.Current.GoToAsync(
            "//Calendario");
    }

    private async void TarjetaEstadistica_Tapped(
    object sender,
    TappedEventArgs e)
    {
        if (e.Parameter is not string parametro)
            return;

        string[] partes =
            parametro.Split(
                '|',
                2,
                StringSplitOptions.None);

        if (partes.Length != 2)
            return;

        string tipo = partes[0];
        string valor = partes[1];

        var filtros =
            new FiltrosPersonas();

        switch (tipo)
        {
            case "Servicio":
                filtros.Servicios.Add(valor);
                break;

            case "Nombramiento":
                filtros.Nombramiento = valor;
                break;

            case "Solicitud":
                filtros.Solicitudes.Add(valor);
                break;

            default:
                return;
        }

        PersonasNavigationState.Establecer(
            filtros);

        await Shell.Current.GoToAsync(
            "//personas");
    }
}