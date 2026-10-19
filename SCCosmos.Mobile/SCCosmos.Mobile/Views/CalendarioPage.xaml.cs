using System.Collections.ObjectModel;
using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class CalendarioPage : ContentPage, IActualizableMobile
{
    /// <summary>
    /// Semana que debe resaltarse brevemente al abrir el calendario
    /// (la fija el enlace «Próxima» del Inicio). Formato yyyy-MM-dd.
    /// </summary>
    public static string SemanaDestacadaPendiente = "";

    private DateTime _fechaLimiteAnterior;
    private DateTime _fechaLimitePosterior;

    private bool _extendiendo;
    private bool _cargandoCalendario;
    private DateTime? _anclaSemana;

    private readonly ObservableCollection<CalendarioGrupoMes>
        _grupos = new();

    private DateTime? _primeraSemanaVisible;

    private bool _primeraCarga = true;

    public CalendarioPage()
    {
        InitializeComponent();

        InicializarRango();

        // Con el perfil limitado (esposa del SC) en el calendario solo
        // queda el botón de "Ver visita": sin ciclo, sin "Hoy" y sin los
        // cargadores de meses (el rango que se carga ya cubre ±6 meses).
        bool accesoCompleto =
            PerfilAccesoService.AccesoCompleto;

        // La esposa del SC no ve el botón de Ciclo: se quita de la barra
        // superior (el ToolbarItem no tiene IsVisible).
        if (!accesoCompleto
            && ToolbarItems.Contains(
                BtnCiclo))
        {
            ToolbarItems.Remove(
                BtnCiclo);
        }

        BtnHoy.IsVisible =
            accesoCompleto;

        ListaCalendario.ItemsSource =
            _grupos;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_primeraCarga)
        {
            _primeraCarga = false;

            await CargarCalendarioAsync(
                centrarEnActual: true);
        }
        else if (_anclaSemana.HasValue)
        {
            // Se vuelve a la semana que se estaba editando o viendo.
            await CargarCalendarioAsync(
                centrarEnActual: false,
                ancla: _anclaSemana);
        }
        else
        {
            await CargarCalendarioAsync(
                centrarEnActual: true);
        }

        await ResaltarSemanaPendienteAsync();
    }


    /// <summary>
    /// Si el Inicio dejó una semana pendiente, se desplaza hasta ella y
    /// se resalta el borde un momento (y vuelve a su estado normal).
    /// </summary>
    private async Task ResaltarSemanaPendienteAsync()
    {
        string pendiente =
            SemanaDestacadaPendiente;

        SemanaDestacadaPendiente = "";

        if (string.IsNullOrWhiteSpace(pendiente)
            || !DateTime.TryParseExact(
                pendiente,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime fecha))
        {
            return;
        }

        CalendarioItem? item =
            _grupos
                .SelectMany(g => g)
                .FirstOrDefault(i =>
                    i.Semana.FechaMartes.Date == fecha.Date);

        if (item is null)
        {
            return;
        }

        // En listas agrupadas, ScrollTo necesita también el grupo (y sin
        // animación, como el resto de desplazamientos de la página).
        CalendarioGrupoMes? grupo =
            _grupos.FirstOrDefault(
                g => g.Contains(item));

        ListaCalendario.ScrollTo(
            item,
            grupo,
            ScrollToPosition.Center,
            animate: false);

        // Margen para que termine el desplazamiento y se cree la tarjeta.
        Border? tarjeta = null;

        for (int intento = 0;
             intento < 6 && tarjeta is null;
             intento++)
        {
            await Task.Delay(300);

            tarjeta =
                BuscarTarjeta(
                    ListaCalendario,
                    item);
        }

        if (tarjeta is null)
        {
            return;
        }

        Brush bordeOriginal =
            tarjeta.Stroke;

        double grosorOriginal =
            tarjeta.StrokeThickness;

        Color fondoOriginal =
            tarjeta.BackgroundColor;

        tarjeta.Stroke =
            new SolidColorBrush(
                Color.FromArgb("#FFC83C"));

        tarjeta.StrokeThickness = 3;

        tarjeta.BackgroundColor =
            Color.FromArgb("#4DFF9E2C");

        await Task.Delay(1700);

        tarjeta.Stroke = bordeOriginal;
        tarjeta.StrokeThickness = grosorOriginal;
        tarjeta.BackgroundColor = fondoOriginal;
    }


    private static Border? BuscarTarjeta(
        IVisualTreeElement raiz,
        object contexto)
    {
        foreach (IVisualTreeElement hijo
                 in raiz.GetVisualChildren())
        {
            if (hijo is Border borde
                && ReferenceEquals(
                    borde.BindingContext,
                    contexto))
            {
                return borde;
            }

            Border? encontrada =
                BuscarTarjeta(hijo, contexto);

            if (encontrada is not null)
            {
                return encontrada;
            }
        }

        return null;
    }

    // =========================================================
    // RANGO INICIAL: 6 MESES ANTES / 6 MESES DESPUÉS
    // =========================================================

    public void DatosActualizados()
    {
        try
        {
            if (_anclaSemana.HasValue)
            {
                _ = CargarCalendarioAsync(
                    centrarEnActual: false,
                    ancla: _anclaSemana);
            }
            else
            {
                _ = CargarCalendarioAsync(
                    centrarEnActual: true);
            }
        }
        catch
        {
        }
    }


    private void InicializarRango()
    {
        // Como en Windows: se carga de una vez todo el periodo con datos
        // (más un año de margen por cada lado), sin cargas al hacer scroll.
        DateTime martesActual =
            ObtenerMartesActual();

        DateTime inicio =
            martesActual.AddDays(-52 * 7);

        DateTime fin =
            martesActual.AddDays(52 * 7);

        try
        {
            DateTime? minimo =
                DatabaseHelper.ObtenerFechaMinimaSemana();

            DateTime? maximo =
                DatabaseHelper.ObtenerFechaMaximaSemana();

            if (minimo.HasValue
                && minimo.Value.AddDays(-52 * 7) < inicio)
            {
                inicio =
                    minimo.Value.AddDays(-52 * 7);
            }

            if (maximo.HasValue
                && maximo.Value.AddDays(52 * 7) > fin)
            {
                fin =
                    maximo.Value.AddDays(52 * 7);
            }
        }
        catch
        {
            // Sin datos: se usa el rango por defecto alrededor de hoy.
        }

        _fechaLimiteAnterior =
            inicio;

        _fechaLimitePosterior =
            fin;
    }

    // =========================================================
    // CARGAR CALENDARIO
    // =========================================================

    private async Task CargarCalendarioAsync(
        bool centrarEnActual,
        DateTime? ancla = null)
    {
        if (!DatabaseHelper.ExisteBaseLocal)
        {
            _grupos.Clear();

            ListaCalendario.ItemsSource =
                null;

            ListaCalendario.ItemsSource =
                _grupos;

            return;
        }

        _cargandoCalendario =
            true;

        MostrarCarga(
            true,
            "Cargando calendario...");

        try
        {
            List<CalendarioItem> elementos =
                await Task.Run(
                    CargarElementosDesdeBase);

            List<CalendarioGrupoMes> grupos =
                CrearGrupos(
                    elementos);

            _grupos.Clear();

            foreach (CalendarioGrupoMes grupo in grupos)
            {
                _grupos.Add(
                    grupo);
            }

            await Task.Delay(100);

            if (centrarEnActual)
            {
                ScrollASemanaActual();
            }
            else if (ancla.HasValue)
            {
                ScrollAFecha(
                    ancla.Value,
                    ScrollToPosition.Start);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Calendario",
                "No se pudo cargar el calendario:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            _cargandoCalendario =
                false;

            MostrarCarga(
                false);
        }
    }

    private List<CalendarioItem>
        CargarElementosDesdeBase()
    {
        var resultado =
            new List<CalendarioItem>();

        DateTime martesActual =
            ObtenerMartesActual();

        DateTime fecha =
            _fechaLimiteAnterior;

        while (fecha <=
               _fechaLimitePosterior)
        {
            string iso =
                fecha.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            List<SemanaCalendario> semanas =
                DatabaseHelper
                    .ObtenerTodasSemanasPorFecha(
                        iso);

            if (semanas.Count == 0)
            {
                semanas.Add(
                    new SemanaCalendario
                    {
                        FechaMartes =
                            fecha,

                        Actividad =
                            "",

                        Congregacion =
                            "",

                        Acompanante =
                            "",

                        Tipo =
                            "",

                        Alojamiento =
                            ""
                    });
            }

            for (int i = 0;
                 i < semanas.Count;
                 i++)
            {
                resultado.Add(
                    new CalendarioItem(
                        semanas[i],
                        esActividadParalela:
                            i > 0,
                        esActual:
                            fecha.Date ==
                            martesActual.Date));
            }

            fecha =
                fecha.AddDays(7);
        }

        return resultado;
    }

    private static List<CalendarioGrupoMes>
        CrearGrupos(
            IEnumerable<CalendarioItem> elementos)
    {
        CultureInfo cultura =
            CultureInfo.GetCultureInfo(
                "es-ES");

        return elementos
            .GroupBy(
                x =>
                    new
                    {
                        x.Semana.FechaMartes.Year,
                        x.Semana.FechaMartes.Month
                    })
            .OrderBy(
                g => g.Key.Year)
            .ThenBy(
                g => g.Key.Month)
            .Select(
                g =>
                {
                    DateTime fechaMes =
                        new(
                            g.Key.Year,
                            g.Key.Month,
                            1);

                    string titulo =
                        fechaMes
                            .ToString(
                                "MMMM yyyy",
                                cultura)
                            .ToUpper(
                                cultura);

                    return
                        new CalendarioGrupoMes(
                            titulo,
                            g.OrderBy(
                                x =>
                                    x.Semana
                                        .FechaMartes)
                             .ThenBy(
                                x =>
                                    x.Semana.Id));
                })
            .ToList();
    }

    // =========================================================
    // NAVEGACIÓN POR RANGO
    // =========================================================

    /// <summary>
    /// Al acercarse a un extremo del calendario se cargan más meses
    /// automáticamente (sin botones).
    /// </summary>
    private void ListaCalendario_Scrolled(
        object sender,
        ItemsViewScrolledEventArgs e)
    {
        if (_cargandoCalendario
            || _grupos.Count == 0)
        {
            return;
        }

        _primeraSemanaVisible =
            PrimeraSemanaVisible(
                e.FirstVisibleItemIndex);
    }


    /// <summary>
    /// Fecha de la semana que ocupa una posición de la lista aplanada
    /// (cada grupo aporta su cabecera y luego sus semanas).
    /// </summary>
    private DateTime? PrimeraSemanaVisible(
        int indice)
    {
        if (indice < 0)
        {
            return null;
        }

        int posicion =
            0;

        foreach (CalendarioGrupoMes grupo in _grupos)
        {
            if (indice == posicion)
            {
                return grupo.FirstOrDefault()?.Semana?.FechaMartes;
            }

            posicion++;

            foreach (CalendarioItem item in grupo)
            {
                if (indice == posicion)
                {
                    return item.Semana?.FechaMartes;
                }

                posicion++;
            }
        }

        return null;
    }


    /// <summary>
    /// Amplía el rango de meses cargado (tres meses más) sin que la lista
    /// salte de sitio: solo se añaden los grupos nuevos (al final si se
    /// carga hacia el futuro y al principio, dejando la misma fecha arriba,
    /// si se carga hacia el pasado).
    /// </summary>
    private async Task ExtenderRangoAsync(
        bool haciaAtras)
    {
        _extendiendo =
            true;

        try
        {
            DateTime? ancla =
                _primeraSemanaVisible;

            List<CalendarioGrupoMes> anteriores =
                _grupos.ToList();

            if (haciaAtras)
            {
                _fechaLimiteAnterior =
                    _fechaLimiteAnterior
                        .AddDays(-18 * 7);
            }
            else
            {
                _fechaLimitePosterior =
                    _fechaLimitePosterior
                        .AddDays(18 * 7);
            }

            List<CalendarioItem> elementos =
                await Task.Run(
                    CargarElementosDesdeBase);

            List<CalendarioGrupoMes> grupos =
                CrearGrupos(
                    elementos);

            // Los grupos nuevos son los que no estaban antes.
            var titulos =
                new HashSet<string>(
                    anteriores.Select(g => g.Titulo));

            List<CalendarioGrupoMes> nuevos =
                grupos
                    .Where(g => !titulos.Contains(g.Titulo))
                    .ToList();

            if (haciaAtras)
            {
                for (int i = 0;
                     i < nuevos.Count;
                     i++)
                {
                    _grupos.Insert(
                        i,
                        nuevos[i]);
                }

                await Task.Delay(80);

                if (ancla.HasValue)
                {
                    ScrollAFecha(
                        ancla.Value,
                        ScrollToPosition.Start);
                }
            }
            else
            {
                foreach (CalendarioGrupoMes grupo in nuevos)
                {
                    _grupos.Add(
                        grupo);
                }
            }
        }
        finally
        {
            _extendiendo =
                false;
        }
    }

    private async void BtnConfigurarCiclo_Clicked(
        object sender,
        EventArgs e)
    {
        var pagina =
            new CicloVisitasPage();

        await Navigation.PushAsync(pagina);
    }


    private void BtnHoy_Clicked(
        object sender,
        EventArgs e)
    {
        _anclaSemana =
            null;

        ScrollASemanaActual();
    }

    private async void RefrescarCalendario_Refreshing(
        object sender,
        EventArgs e)
    {
        try
        {
            await CargarCalendarioAsync(
                centrarEnActual: false);
        }
        finally
        {
            RefrescarCalendario
                .IsRefreshing =
                    false;
        }
    }

    // =========================================================
    // EDITAR
    // =========================================================

    private async void TarjetaSemana_Tapped(
        object sender,
        TappedEventArgs e)
    {
        // Con el perfil limitado la tarjeta de la semana no abre la
        // edición de la semana.
        if (!PerfilAccesoService.AccesoCompleto)
        {
            return;
        }

        if (e.Parameter is
            CalendarioItem item)
        {
            await EditarSemanaAsync(
                item);
        }
    }

    private async void BtnEditar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                CalendarioItem item)
        {
            await EditarSemanaAsync(
                item);
        }
    }

    private async Task EditarSemanaAsync(
        CalendarioItem item)
    {
        _anclaSemana =
            item.Semana.FechaMartes;

        var pagina =
            new FormularioSemanaPage(
                item.Semana);

        await Navigation.PushAsync(pagina);

        bool guardado =
            await pagina
                .EsperarResultadoAsync();

        if (guardado)
        {
            await CargarCalendarioAsync(
                centrarEnActual: false,
                ancla:
                    item.Semana.FechaMartes);
        }
    }

    // =========================================================
    // VER VISITA
    // =========================================================

    private async void BtnVerVisita_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CalendarioItem item
            ||
            !item.EsVisita)
        {
            return;
        }

        _anclaSemana =
            item.Semana.FechaMartes;

        var pagina =
            new VisitaDetallePage(
                item.Semana);

        await Navigation.PushAsync(pagina);
    }


    // =========================================================
    // ACCIONES DE CORRESPONDENCIA (S-302, documentación, WhatsApp)
    // =========================================================

    private async void BtnAcciones_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CalendarioItem item
            ||
            !item.TieneAcciones)
        {
            return;
        }

        _anclaSemana =
            item.Semana.FechaMartes;

        var pagina =
            new AccionesVisitaPage(
                item.Semana);

        await Navigation.PushAsync(pagina);

        // Al volver, refrescamos para reflejar el estado de los envíos.
        await CargarCalendarioAsync(
            centrarEnActual: false,
            ancla:
                item.Semana.FechaMartes);
    }


    // =========================================================
    // IMPORTAR S-21 / S-13
    // =========================================================

    private async void BtnImportarJson_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CalendarioItem item
            ||
            !item.EsVisita)
        {
            return;
        }

        var pagina =
            new JsonImportSelectPage(
                item.Semana);

        await Navigation.PushAsync(pagina);
    }


    // =========================================================
    // SOLAPAR ACTIVIDAD
    // =========================================================

    private async void BtnSolapar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CalendarioItem item)
        {
            return;
        }

        MostrarCarga(
            true,
            "Creando actividad paralela...");

        SemanaCalendario? nueva =
            null;

        try
        {
            DateTime fecha =
                item.Semana.FechaMartes.Date;

            var semanaNueva =
                new SemanaCalendario
                {
                    FechaMartes =
                        fecha,

                    Actividad =
                        "",

                    Congregacion =
                        "",

                    Acompanante =
                        "",

                    Tipo =
                        "",

                    Alojamiento =
                        ""
                };

            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarSemana(
                            semanaNueva));

            string iso =
                fecha.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            List<SemanaCalendario> recargadas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerTodasSemanasPorFecha(
                                iso));

            nueva =
                recargadas
                    .OrderByDescending(
                        x => x.Id)
                    .FirstOrDefault();

            if (nueva is null
                ||
                nueva.Id <= 0)
            {
                throw new InvalidOperationException(
                    "No se pudo recuperar la nueva actividad.");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Calendario",
                "No se pudo crear la actividad paralela:\n\n" +
                ex.Message,
                "Aceptar");

            return;
        }
        finally
        {
            MostrarCarga(
                false);
        }

        var pagina =
            new FormularioSemanaPage(
                nueva);

        await Navigation.PushAsync(pagina);

        bool guardado =
            await pagina
                .EsperarResultadoAsync();

        if (!guardado)
        {
            try
            {
                int idNueva =
                    nueva.Id;

                await Task.Run(
                    () =>
                        DatabaseHelper
                            .EliminarSemanaCalendario(
                                idNueva));
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Calendario",
                    "La edición se canceló, pero no se pudo " +
                    "eliminar la actividad vacía:\n\n" +
                    ex.Message,
                    "Aceptar");
            }
        }

        await CargarCalendarioAsync(
            centrarEnActual: false,
            ancla:
                item.Semana.FechaMartes);
    }

    // =========================================================
    // ELIMINAR ACTIVIDAD PARALELA
    // =========================================================

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CalendarioItem item
            ||
            !item.EsActividadParalela
            ||
            item.Semana.Id <= 0)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Eliminar actividad",
                "¿Seguro que deseas eliminar esta actividad " +
                "paralela de esta semana?",
                "Eliminar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        try
        {
            int id =
                item.Semana.Id;

            await Task.Run(
                () =>
                    DatabaseHelper
                        .EliminarSemanaCalendario(
                            id));

            await CargarCalendarioAsync(
                centrarEnActual: false,
                ancla:
                    item.Semana.FechaMartes);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Calendario",
                "No se pudo eliminar la actividad:\n\n" +
                ex.Message,
                "Aceptar");
        }
    }

    // =========================================================
    // SCROLL
    // =========================================================

    private void ScrollASemanaActual()
    {
        DateTime martesActual =
            ObtenerMartesActual();

        ScrollAFecha(
            martesActual,
            ScrollToPosition.Center);
    }

    private void ScrollAFecha(
        DateTime fecha,
        ScrollToPosition posicion)
    {
        foreach (
            CalendarioGrupoMes grupo
            in _grupos)
        {
            CalendarioItem? item =
                grupo.FirstOrDefault(
                    x =>
                        x.Semana
                            .FechaMartes
                            .Date
                        ==
                        fecha.Date);

            if (item is not null)
            {
                ListaCalendario.ScrollTo(
                    item,
                    grupo,
                    posicion,
                    animate: false);

                return;
            }
        }
    }

    // =========================================================
    // UTILIDADES
    // =========================================================

    private static DateTime ObtenerMartesActual()
    {
        DateTime hoy =
            DateTime.Today;

        // La semana empieza en LUNES: el martes (la fecha con la que se
        // guardan las semanas) es el día siguiente al lunes de esta semana.
        int diasDesdeLunes =
            ((int)hoy.DayOfWeek
             - (int)DayOfWeek.Monday
             + 7) % 7;

        return hoy
            .AddDays(-diasDesdeLunes)
            .Date
            .AddDays(1);
    }

    private void MostrarCarga(
        bool mostrar,
        string texto =
            "Cargando...")
    {
        LblCargando.Text =
            texto;

        OverlayCargando.IsVisible =
            mostrar;
    }
}


// =============================================================
// GRUPO DE MES
// =============================================================

public sealed class CalendarioGrupoMes
    : List<CalendarioItem>
{
    public string Titulo
    { get; }

    public CalendarioGrupoMes(
        string titulo,
        IEnumerable<CalendarioItem> items)
        : base(items)
    {
        Titulo =
            titulo;
    }
}


// =============================================================
// ELEMENTO VISUAL DE SEMANA
// =============================================================

public sealed class CalendarioItem
{
    public SemanaCalendario Semana
    { get; }

    public bool EsActividadParalela
    { get; }

    public bool EsActual
    { get; }

    public bool EsVisita =>
        string.Equals(
            Semana.Actividad,
            "Visita",
            StringComparison.OrdinalIgnoreCase)
        &&
        !string.IsNullOrWhiteSpace(
            Semana.Congregacion);

    public bool EsAsamblea =>
        string.Equals(
            Semana.Actividad,
            "Asamblea de circuito",
            StringComparison.OrdinalIgnoreCase);

    public bool TieneAcciones =>
        EsVisita || EsAsamblea;

    /// <summary>
    /// Con el perfil limitado (esposa del SC) en la semana solo queda el
    /// botón de "Ver visita": nada de editar, acciones, JSON ni
    /// añadir/eliminar actividades.
    /// </summary>
    public bool MostrarAcciones =>
        PerfilAccesoService.AccesoCompleto;

    public bool MostrarBotonEditar =>
        MostrarAcciones;

    public bool MostrarBotonAcciones =>
        MostrarAcciones
        && TieneAcciones;

    public bool MostrarBotonImportarJson =>
        MostrarAcciones
        && EsVisita;

    public bool MostrarBotonSolapar =>
        MostrarAcciones;

    public bool MostrarBotonEliminar =>
        MostrarAcciones
        && EsActividadParalela;

    /// <summary>
    /// El botón de "Ver visita" solo aparece si la semana es una visita y
    /// hay datos introducidos (igual que en Windows).
    /// </summary>
    public bool MostrarVerVisita =>
        EsVisita
        && Semana.TieneDatosImportados;

    public string TextoImportarJson =>
        Semana.TieneDatosImportados
            ? "Actualizar JSON"
            : "Importar JSON";

    public string EtiquetaActual =>
        EsActual
            ? "SEMANA ACTUAL"
            : "";

    public string MarcadorParalela =>
        EsActividadParalela
            ? "PARALELA"
            : "";

    public string RangoFechas =>
        Semana.RangoFechas;

    public string ActividadVisual
    {
        get
        {
            string actividad =
                string.IsNullOrWhiteSpace(
                    Semana.Actividad)
                    ? "Sin actividad"
                    : Semana.Actividad;

            // En las visitas, el nombre de la congregación va al lado.
            if (actividad == "Visita"
                && !string.IsNullOrWhiteSpace(
                    Semana.Congregacion))
            {
                return actividad + " · " + Semana.Congregacion;
            }

            return actividad;
        }
    }

    /// <summary>
    /// Color con el que se pinta el nombre de la actividad (la visita se
    /// queda como estaba).
    /// </summary>
    public Color ColorActividad
    {
        get
        {
            string actividad =
                (Semana.Actividad ?? "")
                    .ToLowerInvariant();

            if (actividad.Contains("asamblea"))
            {
                return Color.FromArgb("#FFB74D");
            }

            if (actividad.Contains("conmemor"))
            {
                return Color.FromArgb("#CE93D8");
            }

            if (actividad.Contains("escuela"))
            {
                return Color.FromArgb("#4DB6AC");
            }

            if (actividad.Contains("precursor"))
            {
                return Color.FromArgb("#81C784");
            }

            if (actividad.Contains("vacaciones"))
            {
                return Color.FromArgb("#FFD54F");
            }

            if (actividad.Contains("libre"))
            {
                return Color.FromArgb("#64B5F6");
            }

            if (actividad.Contains("esp"))
            {
                return Color.FromArgb("#4DD0E1");
            }

            if (actividad.Contains("otro")
                || string.IsNullOrWhiteSpace(actividad))
            {
                return Color.FromArgb("#B0BEC5");
            }

            // Visita (y por defecto): el azul de siempre.
            return Color.FromArgb("#4BA3E3");
        }
    }

    public bool MostrarDetallePrincipal =>
        Semana.Actividad != "Visita"
        && !string.IsNullOrWhiteSpace(
            DetallePrincipal);

    public bool MostrarDetalleSecundario =>
        !string.IsNullOrWhiteSpace(
            DetalleSecundario);

    public string DetallePrincipal
    {
        get
        {
            if (Semana.Actividad ==
                "Visita")
            {
                return string.IsNullOrWhiteSpace(
                    Semana.Congregacion)
                    ? "Congregación sin asignar"
                    : Semana.Congregacion;
            }

            if (Semana.Actividad ==
                "Asamblea de circuito")
            {
                string seccion =
                    string.IsNullOrWhiteSpace(
                        Semana.SeccionAsamblea)
                        ? ""
                        : "Sección " +
                          Semana.SeccionAsamblea;

                string fecha =
                    Semana.FechaAsamblea
                    ?? "";

                return string.Join(
                    " · ",
                    new[]
                    {
                        seccion,
                        fecha
                    }
                    .Where(
                        x =>
                            !string
                                .IsNullOrWhiteSpace(
                                    x)));
            }

            return "";
        }
    }

    public string DetalleSecundario
    {
        get
        {
            if (Semana.Actividad !=
                "Visita")
            {
                return "";
            }

            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(
                    Semana.Tipo))
            {
                partes.Add(
                    Semana.Tipo);
            }

            if (!string.IsNullOrWhiteSpace(
                    Semana.Acompanante))
            {
                partes.Add(
                    Semana.Acompanante);
            }

            if (!string.IsNullOrWhiteSpace(
                    Semana.Alojamiento))
            {
                partes.Add(
                    Semana.Alojamiento);
            }

            return string.Join(
                " · ",
                partes);
        }
    }

    public Microsoft.Maui.Graphics.Color
        Fondo =>
            EsActual
                ? Microsoft.Maui.Graphics.Color
                    .FromArgb("#203D6A")
                : Microsoft.Maui.Graphics.Color
                    .FromArgb("#171A1F");

    public Microsoft.Maui.Graphics.Color
        Borde =>
            EsActual
                ? Microsoft.Maui.Graphics.Color
                    .FromArgb("#4BA3E3")
                : Microsoft.Maui.Graphics.Color
                    .FromArgb("#343942");

    public double GrosorBorde =>
        EsActual
            ? 2
            : 1;

    public CalendarioItem(
        SemanaCalendario semana,
        bool esActividadParalela,
        bool esActual)
    {
        Semana =
            semana;

        EsActividadParalela =
            esActividadParalela;

        EsActual =
            esActual;
    }
}
