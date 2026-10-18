using System.Globalization;
using System.Text.Json;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

public partial class FormularioSemanaPage
    : ContentPage
{
    private readonly SemanaCalendario
        _semanaActual;

    private readonly TaskCompletionSource<bool>
        _resultado =
            new();

    private bool _inicializando =
        true;

    private bool _cerrando;

    private List<CongregacionDisplayItem>
        _congregaciones =
            new();

    private List<PersonaPickerItem>
        _personasSCS =
            new();

    private static readonly string[]
        Actividades =
        [
            "Visita",
            "Asamblea de circuito",
            "Asamblea regional",
            "Semana de precursor",
            "Semana libre CG",
            "ESP",
            "Vacaciones",
            "Conmemoración",
            "Otro"
        ];

    private static readonly string[]
        TiposVisita =
        [
            "Visita SC",
            "Visita de SCS",
            "Visita (con SCS)",
            "Visita (de pastoreo)"
        ];

    public FormularioSemanaPage(
        SemanaCalendario semana)
    {
        InitializeComponent();

        _semanaActual =
            semana;

        LblRango.Text =
            semana.RangoFechas;

        PickerActividad.ItemsSource =
            Actividades;

        PickerTipo.ItemsSource =
            TiposVisita;

        PickerCongregacion.ItemDisplayBinding =
            new Binding(
                nameof(
                    CongregacionDisplayItem
                        .Display));

        PickerSCS.ItemDisplayBinding =
            new Binding(
                nameof(
                    PersonaPickerItem
                        .Display));

        PickerEsposaSCS.ItemDisplayBinding =
            new Binding(
                nameof(
                    PersonaPickerItem
                        .Display));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_inicializando)
        {
            return;
        }

        await InicializarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (!_cerrando
            &&
            !_resultado.Task.IsCompleted)
        {
            _resultado
                .TrySetResult(
                    false);
        }
    }

    public Task<bool>
        EsperarResultadoAsync()
    {
        return _resultado.Task;
    }

    // =========================================================
    // INICIALIZACIÓN
    // =========================================================

    private async Task InicializarAsync()
    {
        MostrarCarga(
            true,
            "Preparando semana...");

        try
        {
            DatosFormulario datos =
                await Task.Run(
                    PrepararDatos);

            _congregaciones =
                datos.Congregaciones;

            PickerCongregacion.ItemsSource =
                _congregaciones;

            PickerSeccion.ItemsSource =
                datos.Secciones;

            _personasSCS =
                datos.Scs;

            PickerSCS.ItemsSource =
                _personasSCS;

            PrepararDiasAsamblea();

            SeleccionarActividadActual();

            ActualizarPanelActividad();

            SeleccionarTipoActual();

            ActualizarTipoVisita();

            SeleccionarCongregacionActual();

            SeleccionarAlojamientoActual();

            SeleccionarSeccionActual();

            SeleccionarDiaAsambleaActual();

            CargarAcompananteActual();

            _inicializando =
                false;
        }
        catch (Exception ex)
        {
            LblAviso.Text =
                "No se pudieron cargar todos los datos " +
                "del formulario:\n" +
                ex.Message;

            LblAviso.IsVisible =
                true;

            _inicializando =
                false;
        }
        finally
        {
            MostrarCarga(
                false);
        }
    }

    private DatosFormulario PrepararDatos()
    {
        List<SemanaCalendario>
            todasLasVisitas =
                ObtenerTodasLasVisitas();

        string sugerida =
            ObtenerCongregacionSugerida(
                todasLasVisitas);

        var congregaciones =
            new List<CongregacionDisplayItem>();

        foreach (
            var c
            in DatabaseHelper
                .ObtenerCongregaciones())
        {
            string nombre =
                c.GetType()
                    .GetProperty("Nombre")
                    ?.GetValue(c)
                    ?.ToString()
                ?? c.ToString()
                ?? "";

            nombre =
                nombre.Trim();

            if (string.IsNullOrWhiteSpace(
                    nombre))
            {
                continue;
            }

            string display =
                nombre;

            int mesesParaOrdenar =
                9999;

            List<SemanaCalendario>
                anteriores =
                    todasLasVisitas
                        .Where(
                            v =>
                                string.Equals(
                                    v.Congregacion
                                        .Trim(),
                                    nombre,
                                    StringComparison
                                        .Ordinal)
                                &&
                                v.FechaMartes <
                                _semanaActual
                                    .FechaMartes)
                        .ToList();

            List<SemanaCalendario>
                posteriores =
                    todasLasVisitas
                        .Where(
                            v =>
                                string.Equals(
                                    v.Congregacion
                                        .Trim(),
                                    nombre,
                                    StringComparison
                                        .Ordinal)
                                &&
                                v.FechaMartes >
                                _semanaActual
                                    .FechaMartes)
                        .OrderBy(
                            v =>
                                v.FechaMartes)
                        .ToList();

            SemanaCalendario? futuraCercana =
                posteriores
                    .FirstOrDefault(
                        v =>
                            (v.FechaMartes -
                             _semanaActual
                                 .FechaMartes)
                            .TotalDays
                            <
                            150);

            if (futuraCercana is not null)
            {
                int dias =
                    (futuraCercana
                        .FechaMartes
                     -
                     _semanaActual
                        .FechaMartes)
                    .Days;

                if (dias < 30)
                {
                    display =
                        nombre +
                        " (¡Ojo! Programada este mes)";
                }
                else if (dias < 60)
                {
                    display =
                        nombre +
                        " (Programada el mes que viene)";
                }
                else
                {
                    display =
                        nombre +
                        $" (Programada en {dias / 30} meses)";
                }

                mesesParaOrdenar =
                    -1;
            }
            else if (anteriores.Count > 0)
            {
                DateTime ultima =
                    anteriores
                        .OrderByDescending(
                            v =>
                                v.FechaMartes)
                        .First()
                        .FechaMartes;

                mesesParaOrdenar =
                    ((_semanaActual
                        .FechaMartes.Year
                      -
                      ultima.Year)
                     * 12)
                    +
                    _semanaActual
                        .FechaMartes.Month
                    -
                    ultima.Month;

                if (mesesParaOrdenar == 0)
                {
                    display =
                        nombre +
                        " (este mes)";
                }
                else if (mesesParaOrdenar == 1)
                {
                    display =
                        nombre +
                        " (hace 1 mes)";
                }
                else
                {
                    display =
                        nombre +
                        $" (hace {mesesParaOrdenar} meses)";
                }
            }
            else
            {
                display =
                    nombre +
                    " (nunca)";
            }

            bool esSugerida =
                string.Equals(
                    nombre,
                    sugerida,
                    StringComparison.Ordinal);

            if (esSugerida)
            {
                display =
                    "⭐ " +
                    display +
                    " (SUGERIDA)";
            }

            congregaciones.Add(
                new CongregacionDisplayItem
                {
                    Nombre =
                        nombre,

                    Display =
                        display,

                    Meses =
                        mesesParaOrdenar,

                    EsSugerida =
                        esSugerida
                });
        }

        congregaciones =
            congregaciones
                .OrderByDescending(
                    x =>
                        x.EsSugerida)
                .ThenByDescending(
                    x =>
                        x.Meses)
                .ToList();

        List<string> secciones =
            DatabaseHelper
                .ObtenerSecciones()
                .Select(
                    s =>
                        s.GetType()
                            .GetProperty("Nombre")
                            ?.GetValue(s)
                            ?.ToString()
                        ?? s.ToString()
                        ?? "")
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x))
                .ToList();

        List<PersonaPickerItem> scs =
            DatabaseHelper
                .ObtenerPersonas()
                .Where(
                    p =>
                        string.IsNullOrWhiteSpace(
                            p.FechaEliminacion)
                        &&
                        !string.IsNullOrWhiteSpace(
                            p.Privilegios)
                        &&
                        p.Privilegios.Contains(
                            "Superintendente de circuito sustituto",
                            StringComparison
                                .OrdinalIgnoreCase))
                .OrderBy(
                    p =>
                        NombrePersona(
                            p))
                .Select(
                    p =>
                        new PersonaPickerItem(
                            p))
                .ToList();

        return
            new DatosFormulario
            {
                Congregaciones =
                    congregaciones,

                Secciones =
                    secciones,

                Scs =
                    scs
            };
    }

    // =========================================================
    // LÓGICA DEL CICLO DE VISITAS
    // =========================================================

    private List<SemanaCalendario>
        ObtenerTodasLasVisitas()
    {
        var visitas =
            new List<SemanaCalendario>();

        DateTime fecha =
            new(
                2020,
                1,
                1);

        while (fecha.DayOfWeek !=
               DayOfWeek.Tuesday)
        {
            fecha =
                fecha.AddDays(1);
        }

        DateTime limite =
            DateTime.Today
                .AddDays(
                    365 * 2);

        while (fecha <= limite)
        {
            string iso =
                fecha.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            List<SemanaCalendario> semanas =
                DatabaseHelper
                    .ObtenerTodasSemanasPorFecha(
                        iso);

            foreach (
                SemanaCalendario semana
                in semanas)
            {
                if (!string.IsNullOrWhiteSpace(
                        semana.Congregacion)
                    &&
                    !string.IsNullOrWhiteSpace(
                        semana.Tipo)
                    &&
                    semana.Tipo.Contains(
                        "Visita",
                        StringComparison
                            .OrdinalIgnoreCase))
                {
                    visitas.Add(
                        semana);
                }
            }

            fecha =
                fecha.AddDays(7);
        }

        return visitas;
    }

    private string ObtenerCongregacionSugerida(
        List<SemanaCalendario>
            todasLasVisitas)
    {
        string json =
            DatabaseHelper
                .ObtenerConfiguracion(
                    "MatrizCicloVisitas",
                    "");

        if (string.IsNullOrWhiteSpace(
                json))
        {
            return "";
        }

        try
        {
            List<FilaCicloDto>? filas =
                JsonSerializer
                    .Deserialize<
                        List<FilaCicloDto>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });

            if (filas is null
                ||
                filas.Count == 0)
            {
                return "";
            }

            var plana =
                new List<string>();

            for (int col = 1;
                 col <= 6;
                 col++)
            {
                foreach (
                    FilaCicloDto fila
                    in filas.OrderBy(
                        x =>
                            x.Orden))
                {
                    string valor =
                        col switch
                        {
                            1 => fila.Visita1,
                            2 => fila.Visita2,
                            3 => fila.Visita3,
                            4 => fila.Visita4,
                            5 => fila.Visita5,
                            _ => fila.Visita6
                        };

                    if (!string.IsNullOrWhiteSpace(
                            valor))
                    {
                        plana.Add(
                            valor.Trim());
                    }
                }
            }

            if (plana.Count == 0)
            {
                return "";
            }

            int visitasPasadas =
                todasLasVisitas.Count(
                    v =>
                        v.FechaMartes <
                        _semanaActual
                            .FechaMartes
                        &&
                        (v.Tipo ==
                         "Visita SC"
                         ||
                         v.Tipo ==
                         "Visita (con SCS)"));

            int inicio =
                visitasPasadas
                %
                plana.Count;

            for (int i = 0;
                 i < plana.Count;
                 i++)
            {
                int indice =
                    (inicio + i)
                    %
                    plana.Count;

                string candidato =
                    plana[indice];

                bool programadaCerca =
                    todasLasVisitas.Any(
                        v =>
                            string.Equals(
                                v.Congregacion
                                    .Trim(),
                                candidato,
                                StringComparison
                                    .Ordinal)
                            &&
                            v.FechaMartes !=
                            _semanaActual
                                .FechaMartes
                            &&
                            Math.Abs(
                                (v.FechaMartes
                                 -
                                 _semanaActual
                                    .FechaMartes)
                                .TotalDays)
                            <
                            75);

                if (!programadaCerca)
                {
                    return candidato;
                }
            }
        }
        catch
        {
            // Si la matriz no puede deserializarse,
            // mantenemos el formulario operativo
            // sin sugerencia.
        }

        return "";
    }

    // =========================================================
    // ACTIVIDAD
    // =========================================================

    private void PickerActividad_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        ActualizarPanelActividad();
    }

    private void ActualizarPanelActividad()
    {
        string actividad =
            PickerActividad
                .SelectedItem
                ?.ToString()
            ?? "";

        bool esVisita =
            actividad ==
            "Visita";

        bool esAsamblea =
            actividad ==
            "Asamblea de circuito";

        PanelCongregacion.IsVisible =
            esVisita;

        PanelDetallesVisita.IsVisible =
            esVisita;

        PanelAsamblea.IsVisible =
            esAsamblea;
    }

    // =========================================================
    // TIPO DE VISITA
    // =========================================================

    private void PickerTipo_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        ActualizarTipoVisita();
    }

    private void ActualizarTipoVisita()
    {
        string tipo =
            PickerTipo
                .SelectedItem
                ?.ToString()
            ?? "";

        string alojamientoAnterior =
            PickerAlojamiento
                .SelectedItem
                ?.ToString()
            ?? "";

        var alojamientos =
            new List<string>();

        PanelSCS.IsVisible =
            false;

        PanelPastoreo.IsVisible =
            false;

        PickerAlojamiento.IsEnabled =
            true;

        if (tipo ==
            "Visita SC")
        {
            alojamientos.Add(
                "Con alojamiento");

            alojamientos.Add(
                "Sin alojamiento");
        }
        else if (tipo ==
                 "Visita (con SCS)")
        {
            PanelSCS.IsVisible =
                true;

            alojamientos.AddRange(
                new[]
                {
                    "Con alojamiento (ambos)",
                    "Con alojamiento (solo yo)",
                    "Con alojamiento (solo acompañante)",
                    "Sin alojamiento (ambos)"
                });
        }
        else if (tipo ==
                 "Visita de SCS")
        {
            PanelSCS.IsVisible =
                true;

            alojamientos.Add(
                "");

            PickerAlojamiento.IsEnabled =
                false;
        }
        else if (tipo ==
                 "Visita (de pastoreo)")
        {
            PanelPastoreo.IsVisible =
                true;

            alojamientos.AddRange(
                new[]
                {
                    "Con alojamiento (ambos)",
                    "Con alojamiento (solo yo)",
                    "Con alojamiento (solo acompañante)",
                    "Sin alojamiento (ambos)"
                });
        }

        PickerAlojamiento.ItemsSource =
            alojamientos;

        int indiceAnterior =
            alojamientos.FindIndex(
                x =>
                    x ==
                    alojamientoAnterior);

        if (indiceAnterior >= 0)
        {
            PickerAlojamiento.SelectedIndex =
                indiceAnterior;
        }
        else if (!_inicializando
                 &&
                 alojamientos.Count > 0)
        {
            PickerAlojamiento.SelectedIndex =
                0;
        }
    }

    // =========================================================
    // SCS / ESPOSA
    // =========================================================

    private void PickerSCS_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        CargarPosiblesEsposas();
    }

    private void CargarPosiblesEsposas(
        string? seleccionarNombre =
            null)
    {
        if (PickerSCS.SelectedItem
            is not PersonaPickerItem
                scsItem)
        {
            PickerEsposaSCS.ItemsSource =
                null;

            return;
        }

        Persona scs =
            scsItem.Persona;

        List<PersonaPickerItem> posibles =
            DatabaseHelper
                .ObtenerPersonas()
                .Where(
                    p =>
                        string.IsNullOrWhiteSpace(
                            p.FechaEliminacion)
                        &&
                        p.CongregacionCodigo ==
                        scs.CongregacionCodigo
                        &&
                        p.Id !=
                        scs.Id)
                .OrderBy(
                    p =>
                        NombrePersona(
                            p))
                .Select(
                    p =>
                        new PersonaPickerItem(
                            p))
                .ToList();

        PickerEsposaSCS.ItemsSource =
            posibles;

        if (!string.IsNullOrWhiteSpace(
                seleccionarNombre))
        {
            PersonaPickerItem? seleccion =
                posibles.FirstOrDefault(
                    x =>
                        x.Display ==
                        seleccionarNombre);

            if (seleccion is not null)
            {
                PickerEsposaSCS.SelectedItem =
                    seleccion;
            }
        }
    }

    // =========================================================
    // CARGA DEL VALOR ACTUAL
    // =========================================================

    private void SeleccionarActividadActual()
    {
        string actividad =
            _semanaActual.Actividad
            ?? "";

        int indice =
            Array.IndexOf(
                Actividades,
                actividad);

        PickerActividad.SelectedIndex =
            indice;
    }

    private void SeleccionarTipoActual()
    {
        string tipo =
            string.IsNullOrWhiteSpace(
                _semanaActual.Tipo)
                ? "Visita SC"
                : _semanaActual.Tipo;

        int indice =
            Array.IndexOf(
                TiposVisita,
                tipo);

        PickerTipo.SelectedIndex =
            indice >= 0
                ? indice
                : 0;
    }

    private void SeleccionarCongregacionActual()
    {
        if (string.IsNullOrWhiteSpace(
                _semanaActual.Congregacion))
        {
            return;
        }

        CongregacionDisplayItem? item =
            _congregaciones
                .FirstOrDefault(
                    x =>
                        x.Nombre ==
                        _semanaActual
                            .Congregacion);

        if (item is not null)
        {
            PickerCongregacion.SelectedItem =
                item;
        }
    }

    private void SeleccionarAlojamientoActual()
    {
        string alojamiento =
            _semanaActual.Alojamiento
            ?? "";

        if (PickerAlojamiento.ItemsSource
            is IEnumerable<string> lista)
        {
            List<string> valores =
                lista.ToList();

            int indice =
                valores.IndexOf(
                    alojamiento);

            if (indice >= 0)
            {
                PickerAlojamiento.SelectedIndex =
                    indice;
            }
        }
    }

    private void SeleccionarSeccionActual()
    {
        if (PickerSeccion.ItemsSource
            is not IEnumerable<string>
                secciones
            ||
            string.IsNullOrWhiteSpace(
                _semanaActual
                    .SeccionAsamblea))
        {
            return;
        }

        List<string> lista =
            secciones.ToList();

        int indice =
            lista.IndexOf(
                _semanaActual
                    .SeccionAsamblea);

        if (indice >= 0)
        {
            PickerSeccion.SelectedIndex =
                indice;
        }
    }

    private void PrepararDiasAsamblea()
    {
        CultureInfo cultura =
            CultureInfo.GetCultureInfo(
                "es-ES");

        DateTime sabado =
            _semanaActual
                .FechaMartes
                .AddDays(4);

        DateTime domingo =
            _semanaActual
                .FechaMartes
                .AddDays(5);

        string Formatear(
            DateTime fecha)
        {
            string texto =
                fecha.ToString(
                    "dddd d 'de' MMMM",
                    cultura);

            return char
                .ToUpper(
                    texto[0],
                    cultura)
                +
                texto[1..];
        }

        PickerDiaAsamblea.ItemsSource =
            new List<string>
            {
                Formatear(
                    sabado),

                Formatear(
                    domingo)
            };
    }

    private void SeleccionarDiaAsambleaActual()
    {
        if (PickerDiaAsamblea.ItemsSource
            is not IEnumerable<string>
                dias
            ||
            string.IsNullOrWhiteSpace(
                _semanaActual
                    .FechaAsamblea))
        {
            return;
        }

        List<string> lista =
            dias.ToList();

        int indice =
            lista.IndexOf(
                _semanaActual
                    .FechaAsamblea);

        if (indice >= 0)
        {
            PickerDiaAsamblea.SelectedIndex =
                indice;
        }
    }

    private void CargarAcompananteActual()
    {
        if (string.IsNullOrWhiteSpace(
                _semanaActual.Acompanante))
        {
            return;
        }

        string tipo =
            _semanaActual.Tipo
            ?? "";

        string[] partes =
            _semanaActual
                .Acompanante
                .Split(
                    new[]
                    {
                        " y "
                    },
                    StringSplitOptions
                        .None);

        if (tipo ==
            "Visita (con SCS)"
            ||
            tipo ==
            "Visita de SCS")
        {
            string nombreSCS =
                partes[0].Trim();

            PersonaPickerItem? scs =
                _personasSCS
                    .FirstOrDefault(
                        x =>
                            x.Display ==
                            nombreSCS);

            if (scs is not null)
            {
                PickerSCS.SelectedItem =
                    scs;

                string? nombreEsposa =
                    partes.Length > 1
                        ? partes[1]
                            .Trim()
                        : null;

                CargarPosiblesEsposas(
                    nombreEsposa);
            }
        }
        else if (tipo ==
                 "Visita (de pastoreo)")
        {
            string marido =
                partes[0].Trim();

            if (marido.Contains(','))
            {
                string[] partesComa =
                    marido.Split(
                        ',',
                        2);

                TxtApellidoPastoreo.Text =
                    partesComa[0]
                        .Trim();

                TxtHermanoPastoreo.Text =
                    partesComa.Length > 1
                        ? partesComa[1]
                            .Trim()
                        : "";
            }
            else
            {
                TxtHermanoPastoreo.Text =
                    marido;

                TxtApellidoPastoreo.Text =
                    "";
            }

            if (partes.Length > 1)
            {
                TxtEsposaPastoreo.Text =
                    partes[1]
                        .Trim();
            }
        }
    }

    // =========================================================
    // GUARDAR
    // =========================================================

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        MostrarCarga(
            true,
            "Guardando semana...");

        try
        {
            AplicarFormularioASemana();

            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarSemana(
                            _semanaActual));

            _cerrando =
                true;

            _resultado
                .TrySetResult(
                    true);

            await Navigation
                .PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Editar semana",
                "No se pudo guardar:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            if (!_cerrando)
            {
                MostrarCarga(
                    false);
            }
        }
    }

    private void AplicarFormularioASemana()
    {
        string actividad =
            PickerActividad
                .SelectedItem
                ?.ToString()
            ?? "";

        _semanaActual.Actividad =
            actividad;

        if (actividad ==
            "Visita")
        {
            _semanaActual.Congregacion =
                (PickerCongregacion
                    .SelectedItem
                 as CongregacionDisplayItem)
                    ?.Nombre
                ?? "";

            _semanaActual.Tipo =
                PickerTipo
                    .SelectedItem
                    ?.ToString()
                ?? "Visita SC";

            _semanaActual.Alojamiento =
                PickerAlojamiento
                    .SelectedItem
                    ?.ToString()
                ?? "";

            if (_semanaActual.Tipo ==
                "Visita SC")
            {
                _semanaActual.Acompanante =
                    "";
            }
            else if (
                _semanaActual.Tipo ==
                "Visita (con SCS)"
                ||
                _semanaActual.Tipo ==
                "Visita de SCS")
            {
                string nombreSCS =
                    (PickerSCS.SelectedItem
                     as PersonaPickerItem)
                        ?.Display
                    ?? "";

                string nombreEsposa =
                    (PickerEsposaSCS
                        .SelectedItem
                     as PersonaPickerItem)
                        ?.Display
                    ?? "";

                _semanaActual.Acompanante =
                    string.IsNullOrWhiteSpace(
                        nombreEsposa)
                        ? nombreSCS
                        : nombreSCS +
                          " y " +
                          nombreEsposa;
            }
            else if (
                _semanaActual.Tipo ==
                "Visita (de pastoreo)")
            {
                string nombre =
                    TxtHermanoPastoreo
                        .Text
                        ?.Trim()
                    ?? "";

                string apellido =
                    TxtApellidoPastoreo
                        .Text
                        ?.Trim()
                    ?? "";

                string esposa =
                    TxtEsposaPastoreo
                        .Text
                        ?.Trim()
                    ?? "";

                string marido =
                    !string.IsNullOrWhiteSpace(
                        apellido)
                        ? apellido +
                          ", " +
                          nombre
                        : nombre;

                _semanaActual.Acompanante =
                    string.IsNullOrWhiteSpace(
                        esposa)
                        ? marido
                        : marido +
                          " y " +
                          esposa;
            }

            _semanaActual.SeccionAsamblea =
                "";

            _semanaActual.FechaAsamblea =
                "";
        }
        else if (actividad ==
                 "Asamblea de circuito")
        {
            _semanaActual.SeccionAsamblea =
                PickerSeccion
                    .SelectedItem
                    ?.ToString()
                ?? "";

            _semanaActual.FechaAsamblea =
                PickerDiaAsamblea
                    .SelectedItem
                    ?.ToString()
                ?? "";

            LimpiarDatosVisita();
        }
        else
        {
            LimpiarDatosVisita();

            _semanaActual.SeccionAsamblea =
                "";

            _semanaActual.FechaAsamblea =
                "";
        }
    }

    private void LimpiarDatosVisita()
    {
        _semanaActual.Congregacion =
            "";

        _semanaActual.Tipo =
            "";

        _semanaActual.Alojamiento =
            "";

        _semanaActual.Acompanante =
            "";
    }

    // =========================================================
    // CANCELAR
    // =========================================================

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        _cerrando =
            true;

        _resultado
            .TrySetResult(
                false);

        await Navigation
            .PopAsync();
    }

    // =========================================================
    // UTILIDADES
    // =========================================================

    private static string NombrePersona(
        Persona persona)
    {
        if (!string.IsNullOrWhiteSpace(
                persona.NombreJWHub))
        {
            return persona.NombreJWHub;
        }

        return persona.Nombre
               ?? "";
    }

    private void MostrarCarga(
        bool mostrar,
        string texto =
            "Cargando...")
    {
        LblCarga.Text =
            texto;

        OverlayCargando.IsVisible =
            mostrar;
    }


    // =========================================================
    // CLASES AUXILIARES
    // =========================================================

    private sealed class DatosFormulario
    {
        public List<CongregacionDisplayItem>
            Congregaciones
        { get; set; } = new();

        public List<string>
            Secciones
        { get; set; } = new();

        public List<PersonaPickerItem>
            Scs
        { get; set; } = new();
    }

    private sealed class FilaCicloDto
    {
        public int Orden
        { get; set; }

        public string Visita1
        { get; set; } = "";

        public string Visita2
        { get; set; } = "";

        public string Visita3
        { get; set; } = "";

        public string Visita4
        { get; set; } = "";

        public string Visita5
        { get; set; } = "";

        public string Visita6
        { get; set; } = "";
    }
}


public sealed class CongregacionDisplayItem
{
    public string Nombre
    { get; set; } = "";

    public string Display
    { get; set; } = "";

    public int Meses
    { get; set; }

    public bool EsSugerida
    { get; set; }
}


public sealed class PersonaPickerItem
{
    public Persona Persona
    { get; }

    public string Display
    { get; }

    public PersonaPickerItem(
        Persona persona)
    {
        Persona =
            persona;

        Display =
            !string.IsNullOrWhiteSpace(
                persona.NombreJWHub)
                ? persona.NombreJWHub
                : persona.Nombre
                  ?? "";
    }
}
