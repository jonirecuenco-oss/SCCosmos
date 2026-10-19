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

    // Programa del día en Inicio: código y mes de la visita en curso, día
    // elegido y el lunes de esa semana (para no salirse de ella).
    private string _codigoVisitaActual = "";
    private string _mesVisitaActual = "";
    private DateTime _diaPrograma;
    private DateTime _lunesVisitaActual;

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

        // Comprobación de versión nueva (una vez por sesión).
        _ = ComprobarActualizacionAsync();
    }


    private static bool _actualizacionComprobada;

    /// <summary>
    /// Comprueba si hay una versión nueva. Si la actualización automática
    /// está activada se descarga sola y se abre el instalador; si no, se
    /// pregunta antes.
    /// </summary>
    private async Task ComprobarActualizacionAsync()
    {
        if (_actualizacionComprobada)
        {
            return;
        }

        _actualizacionComprobada = true;

        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(10));

            var resultado =
                await ActualizacionMobileService
                    .ComprobarAsync();

            if (!ActualizacionMobileService.HayNueva(
                    resultado))
            {
                return;
            }

            var version =
                resultado.Version;

            if (!ActualizacionMobileService.AutoActiva)
            {
                string notas =
                    (version.Notas ?? "").Trim();

                if (notas.Length > 400)
                {
                    notas =
                        notas.Substring(0, 400) + "…";
                }

                bool actualizar =
                    await DisplayAlert(
                        "Actualización disponible",
                        "Hay una versión nueva de SCCosmos (v"
                        + version.Numero + ").\n\n"
                        + (notas.Length == 0
                            ? ""
                            : notas + "\n\n")
                        + "¿Descargarla e instalarla ahora?",
                        "Actualizar",
                        "Más tarde");

                if (!actualizar)
                {
                    return;
                }
            }

            bool ok =
                await ActualizacionMobileService
                    .DescargarEInstalarAsync(
                        version);

            if (!ok)
            {
                await DisplayAlert(
                    "Actualización",
                    "No se pudo descargar la actualización. "
                    + "Puedes intentarlo de nuevo desde "
                    + "Configuración.",
                    "Aceptar");
            }
        }
        catch
        {
        }
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

        // La semana empieza en LUNES: se toma el lunes de esta semana y el
        // martes (la fecha con la que se guardan las semanas) es el
        // siguiente día. Así en lunes ya aparece la semana que empieza.
        int diasDesdeLunes =
            ((int)hoy.DayOfWeek
             - (int)DayOfWeek.Monday
             + 7) % 7;

        DateTime lunesDeEstaSemana =
            hoy.AddDays(-diasDesdeLunes).Date;

        DateTime martesDeEstaSemana =
            lunesDeEstaSemana.AddDays(1);

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

        CargarHorasMes();

        ProgramaHoy.Clear();

        _codigoVisitaActual = "";
        _mesVisitaActual = "";

        PanelProgramaDia.IsVisible = false;
        PanelVisita.IsVisible = false;
        PanelOtraActividad.IsVisible = false;
        TxtSinDatos.IsVisible = false;
        LblTipoSemana.IsVisible = true;
        LblTipoSemana.TextColor = Color.FromArgb("#858C96");
        TxtSinProgramaDia.IsVisible = false;
        MarcoIconoSemana.IsVisible = true;
        BtnVerVisitaSemana.IsVisible = false;

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
            "📅 " + semana.RangoFechas;

        LblActividad.Text =
            string.IsNullOrWhiteSpace(
                semana.Actividad)
                ? "Sin actividad"
                : semana.Actividad;

        if (EsVisita(semana))
        {
            // Tarjeta como la de Windows: antetítulo azul, nombre de la
            // congregación como título (ojo a la derecha) y fechas.
            LblTipoSemana.Text =
                "VISITA A CONGREGACIÓN";

            LblTipoSemana.TextColor =
                Color.FromArgb("#4BA3E3");

            MarcoIconoSemana.IsVisible =
                false;

            BtnVerVisitaSemana.IsVisible =
                true;

            PanelVisita.IsVisible = true;

            LblActividad.Text =
                string.IsNullOrWhiteSpace(
                    semana.Congregacion)
                    ? "Visita"
                    : semana.Congregacion;

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

                // Para poder mover el programa de día en día.
                _codigoVisitaActual =
                    codigo;

                _mesVisitaActual =
                    mes;

                _lunesVisitaActual =
                    martesDeEstaSemana.AddDays(-1);

                _diaPrograma =
                    hoy;

                ActualizarEtiquetaDiaPrograma();

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
    /// <summary>Minutos de la hora («9:30» → 570); sin hora, al final.</summary>
    private static int OrdenHora(
        string hora)
    {
        string valor =
            (hora ?? "").Trim();

        if (valor.Length == 0)
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
                valor,
                formatos,
                System.Globalization.CultureInfo.InvariantCulture,
                out TimeSpan ts))
        {
            return (int)ts.TotalMinutes;
        }

        if (DateTime.TryParse(
                valor,
                System.Globalization.CultureInfo.GetCultureInfo("es-ES"),
                System.Globalization.DateTimeStyles.NoCurrentDateDefault,
                out DateTime dt))
        {
            return dt.Hour * 60 + dt.Minute;
        }

        return int.MaxValue - 1;
    }


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

    private async void BtnDiaAnterior_Clicked(
        object sender,
        EventArgs e)
    {
        await CambiarDiaProgramaAsync(-1);
    }


    private async void BtnDiaSiguiente_Clicked(
        object sender,
        EventArgs e)
    {
        await CambiarDiaProgramaAsync(1);
    }


    private async Task CambiarDiaProgramaAsync(
        int dias)
    {
        if (string.IsNullOrWhiteSpace(
                _codigoVisitaActual))
        {
            return;
        }

        DateTime nuevo =
            _diaPrograma.AddDays(dias);

        // Dentro de la semana de la visita (lunes a domingo).
        if (_lunesVisitaActual != default
            && (nuevo < _lunesVisitaActual
                || nuevo > _lunesVisitaActual.AddDays(6)))
        {
            return;
        }

        _diaPrograma =
            nuevo;

        ActualizarEtiquetaDiaPrograma();

        await CargarProgramaDelDiaAsync(
            _codigoVisitaActual,
            _mesVisitaActual,
            nuevo.DayOfWeek);
    }


    private void ActualizarEtiquetaDiaPrograma()
    {
        try
        {
            string texto;

            if (_diaPrograma.Date == DateTime.Today)
            {
                texto = "HOY";
            }
            else if (_diaPrograma.Date == DateTime.Today.AddDays(1))
            {
                texto = "MAÑANA";
            }
            else if (_diaPrograma.Date == DateTime.Today.AddDays(-1))
            {
                texto = "AYER";
            }
            else
            {
                texto =
                    _diaPrograma.ToString(
                        "dddd d MMM",
                        System.Globalization.CultureInfo
                            .GetCultureInfo("es-ES"))
                        .ToUpperInvariant();
            }

            LblDiaPrograma.Text =
                texto;
        }
        catch
        {
        }
    }


    // =========================================================
    // HORAS PREDICADAS DEL MES
    // =========================================================

    private void CargarHorasMes()
    {
        try
        {
            string usuario =
                PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            string mes =
                DateTime.Today.ToString("yyyy-MM");

            double horas =
                DatabaseHelper.ObtenerHorasMesMobile(
                    usuario,
                    mes);

            LblHorasMes.Text =
                horas.ToString("0.#");

            LblHorasMesEtiqueta.Text =
                DateTime.Today.ToString(
                    "MMMM yyyy",
                    System.Globalization.CultureInfo
                        .GetCultureInfo("es-ES"));
        }
        catch
        {
        }
    }


    private async void BtnSumarHoras_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            string? entrada =
                await DisplayPromptAsync(
                    "Horas predicadas",
                    "¿Cuántas horas quieres añadir al mes?",
                    "Añadir",
                    "Cancelar",
                    "Ej.: 2,5",
                    6,
                    Keyboard.Numeric);

            if (string.IsNullOrWhiteSpace(entrada))
            {
                return;
            }

            double valor = 0;

            if (!double.TryParse(
                    entrada.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out valor)
                || valor <= 0
                || valor > 99)
            {
                await DisplayAlert(
                    "Horas",
                    "Escribe un número de horas válido (por ejemplo 2,5).",
                    "Aceptar");

                return;
            }

            string usuario =
                PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            string mes =
                DateTime.Today.ToString("yyyy-MM");

            await Task.Run(
                () => DatabaseHelper.SumarHorasMesMobile(
                    usuario,
                    mes,
                    valor));

            DatabaseHelper.AnotarActividadMobile(
                "Horas",
                $"Añadió {valor:0.#} h de predicación ({usuario})");

            CargarHorasMes();

            SCCosmos.Mobile.Platforms.Android
                .SccosmosWidgetProvider
                .ActualizarTodos();
        }
        catch
        {
        }
    }


    private async void BtnMapaActividadInicio_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            if (sender is not Button boton
                || boton.CommandParameter is not ActividadHoy actividad)
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
                         _codigoVisitaActual))
            {
                Congregacion? congregacion =
                    DatabaseHelper
                        .ObtenerCongregacionPorCodigoMobile(
                            _codigoVisitaActual);

                consulta =
                    (congregacion?.DireccionSR ?? "")
                        .Trim();
            }

            if (string.IsNullOrWhiteSpace(consulta))
            {
                return;
            }

            await Launcher.Default.OpenAsync(
                new Uri(
                    "https://www.google.com/maps/search/?api=1&query="
                    + Uri.EscapeDataString(consulta)));
        }
        catch
        {
        }
    }


    private async Task CargarProgramaDelDiaAsync(
        string congreCodigo,
        string mesReferencia,
        DayOfWeek diaHoy)
    {
        string diaDb = diaHoy switch
        {
            DayOfWeek.Monday => "Lunes",
            DayOfWeek.Tuesday => "Martes",
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
                            ? ""
                            : (responsableCrudo.Equals("Rebe", StringComparison.OrdinalIgnoreCase)
                               || responsableCrudo.Equals("Esposa", StringComparison.OrdinalIgnoreCase))
                                ? DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "").Trim()
                                : DatabaseHelper.ObtenerConfiguracion("NombreSC", "").Trim();

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
                                (actividad.Direccion ?? "").Trim(),

                            Direccion =
                                (actividad.Direccion ?? "").Trim(),

                            Latitud =
                                actividad.Latitud,

                            Longitud =
                                actividad.Longitud,

                            Notas =
                                (actividad.NotasLibro ?? "").Trim()
                        });
                }

                return resultado
                    .OrderBy(x => OrdenHora(x.Hora))
                    .ToList();
            });

        ProgramaHoy.Clear();

        foreach (ActividadHoy actividad
                 in programa)
        {
            ProgramaHoy.Add(actividad);
        }

        // Durante una visita el panel se mantiene visible aunque el día
        // esté vacío (p. ej. un lunes), para poder pasar de día.
        bool esDiaDeVisita =
            !string.IsNullOrWhiteSpace(
                _codigoVisitaActual);

        PanelProgramaDia.IsVisible =
            ProgramaHoy.Count > 0
            || esDiaDeVisita;

        TxtSinProgramaDia.IsVisible =
            ProgramaHoy.Count == 0
            && esDiaDeVisita;

        MarcoListaProgramaDia.IsVisible =
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

        return actividad;
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