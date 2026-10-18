using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Asambleas con las MISMAS cuatro secciones que Windows:
///   · Programa            (año + tipo + secciones que tienen programa)
///   · Organigrama         (año + tipo + secciones que tienen organigrama)
///   · Oradores            (matriz: participantes de los últimos años)
///   · Reunión precursores (año + sesiones y sus partes)
/// </summary>
public partial class AsambleasPage : ContentPage, IActualizableMobile
{
    private List<AsambleaMobile> _asambleas =
        new();

    private List<ReunionPrecursoresMobile> _reuniones =
        new();

    private List<string> _anios =
        new();

    private bool _cargando;

    private string _tabActiva =
        "programa";

    // Próxima asamblea programada en el calendario (se selecciona al abrir
    // la página y se resalta su tarjeta).
    private string _proximaSeccion = "";
    private string _proximaTipo = "";
    private string _proximaAnio = "";
    private bool _proximaAplicada;

    private static readonly string[] TiposAsamblea =
    {
        "AC (con sup. de circuito)",
        "AC (con rep. de Betel)"
    };

    public AsambleasPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarAsync();
        }
        catch
        {
        }
    }


    private async Task CargarAsync()
    {
        if (_cargando)
        {
            return;
        }

        _cargando = true;

        try
        {
            List<AsambleaMobile> asambleas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerAsambleasConDatosMobile());

            List<ReunionPrecursoresMobile> reuniones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerReunionesPrecursoresMobile());

            _asambleas =
                asambleas;

            _reuniones =
                reuniones;

            if (!_proximaAplicada)
            {
                _proximaAplicada = true;

                try
                {
                    var proxima =
                        await Task.Run(
                            () =>
                                DatabaseHelper
                                    .ObtenerProximaAsambleaCalendarioMobile());

                    _proximaSeccion = proxima.Seccion ?? "";
                    _proximaTipo = proxima.Tipo ?? "";
                    _proximaAnio = proxima.Anio ?? "";
                }
                catch
                {
                }
            }

            _anios =
                CrearListaAnios();

            // ---- Rellenamos los selectores conservando la selección ----
            string anioPrograma =
                PickerAnioPrograma.SelectedItem as string
                ?? AnioProximaOPorDefecto();

            string tipoPrograma =
                PickerTipoPrograma.SelectedItem as string
                ?? TipoProximaOPorDefecto();

            PickerAnioPrograma.ItemsSource =
                _anios.ToList();

            PickerAnioPrograma.SelectedIndex =
                Math.Max(
                    0,
                    _anios.IndexOf(anioPrograma));

            PickerTipoPrograma.ItemsSource =
                TiposAsamblea.ToList();

            PickerTipoPrograma.SelectedIndex =
                Math.Max(
                    0,
                    Array.IndexOf(
                        TiposAsamblea,
                        tipoPrograma));

            PickerAnioOrganigrama.ItemsSource =
                _anios.ToList();

            PickerAnioOrganigrama.SelectedIndex =
                Math.Max(
                    0,
                    _anios.IndexOf(
                        PickerAnioOrganigrama.SelectedItem
                            as string
                        ?? AnioProximaOPorDefecto()));

            PickerTipoOrganigrama.ItemsSource =
                TiposAsamblea.ToList();

            PickerTipoOrganigrama.SelectedIndex =
                Math.Max(
                    0,
                    Array.IndexOf(
                        TiposAsamblea,
                        PickerTipoOrganigrama.SelectedItem
                            as string
                        ?? TipoProximaOPorDefecto()));

            PickerAnioPrecursores.ItemsSource =
                _anios.ToList();

            PickerAnioPrecursores.SelectedIndex =
                Math.Max(
                    0,
                    _anios.IndexOf(
                        PickerAnioPrecursores.SelectedItem
                            as string
                        ?? AnioPorDefecto()));

            PintarPrograma();
            PintarOrganigrama();

            await PintarPrecursoresAsync();

            await SeleccionarTab(
                _tabActiva,
                forzar: true);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Asambleas",
                "No se pudieron cargar las asambleas: " +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            _cargando = false;
        }
    }

    private List<string> CrearListaAnios()
    {
        var anios =
            new SortedSet<int>();

        int actual =
            DateTime.Now.Year;

        for (int i = -3;
             i <= 1;
             i++)
        {
            anios.Add(
                actual + i);
        }

        foreach (
            AsambleaMobile a in _asambleas)
        {
            if (int.TryParse(
                    a.Anio,
                    out int anio))
            {
                anios.Add(anio);
            }
        }

        foreach (
            ReunionPrecursoresMobile r in _reuniones)
        {
            if (int.TryParse(
                    r.Anio,
                    out int anio))
            {
                anios.Add(anio);
            }
        }

        // El año de servicio de la próxima asamblea del calendario, por si
        // aún no hay ninguna asamblea guardada de ese año.
        if (int.TryParse(
                _proximaAnio,
                out int anioProxima))
        {
            anios.Add(
                anioProxima);
        }

        return anios
            .OrderByDescending(a => a)
            .Select(a => a.ToString())
            .ToList();
    }

    private string AnioPorDefecto()
    {
        string actual =
            DateTime.Now.Year.ToString();

        return _anios.Contains(actual)
            ? actual
            : _anios.FirstOrDefault() ?? "";
    }

    private string AnioProximaOPorDefecto()
    {
        return !string.IsNullOrWhiteSpace(
                _proximaAnio)
            ? _proximaAnio
            : AnioPorDefecto();
    }

    private string TipoProximaOPorDefecto()
    {
        return !string.IsNullOrWhiteSpace(
                _proximaTipo)
            ? _proximaTipo
            : TiposAsamblea[0];
    }

    // ------------------------------------------------------------------
    // PESTAÑAS
    // ------------------------------------------------------------------

    private async void Tab_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                string tab)
        {
            return;
        }

        await SeleccionarTab(tab);
    }

    private async Task SeleccionarTab(
        string tab,
        bool forzar = false)
    {
        if (!forzar
            &&
            tab == _tabActiva)
        {
            return;
        }

        _tabActiva =
            tab;

        PanelPrograma.IsVisible =
            tab == "programa";

        PanelOrganigrama.IsVisible =
            tab == "organigrama";

        PanelOradores.IsVisible =
            tab == "oradores";

        PanelPrecursores.IsVisible =
            tab == "precursores";

        PintarBoton(
            TabPrograma,
            tab == "programa");

        PintarBoton(
            TabOrganigrama,
            tab == "organigrama");

        PintarBoton(
            TabOradores,
            tab == "oradores");

        PintarBoton(
            TabPrecursores,
            tab == "precursores");

        if (tab == "oradores")
        {
            await PanelOradores.CargarAsync();
        }
    }

    private static void PintarBoton(
        Button boton,
        bool activa)
    {
        boton.BackgroundColor =
            Color.FromArgb(
                activa
                    ? "#2478B5"
                    : "#242830");

        boton.TextColor =
            Colors.White;
    }

    // ------------------------------------------------------------------
    // PROGRAMA Y ORGANIGRAMA
    // ------------------------------------------------------------------

    private void FiltroPrograma_Changed(
        object sender,
        EventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        PintarPrograma();
    }

    private void FiltroOrganigrama_Changed(
        object sender,
        EventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        PintarOrganigrama();
    }

    private void PintarPrograma()
    {
        string anio =
            PickerAnioPrograma.SelectedItem as string
            ?? "";

        string tipo =
            PickerTipoPrograma.SelectedItem as string
            ?? "";

        List<AsambleaMobile> lista =
            _asambleas
                .Where(a =>
                    a.Anio == anio
                    && a.Tipo == tipo
                    && a.TienePrograma)
                .OrderBy(a => a.Seccion)
                .ToList();

        MarcarProxima(
            lista);

        ListaPrograma.ItemsSource =
            lista;

        LblPrograma.Text =
            $"{lista.Count} secciones con programa · " +
            $"{anio} · {tipo}";

        DesplazarHastaProxima(
            ListaPrograma,
            lista);
    }

    private void PintarOrganigrama()
    {
        string anio =
            PickerAnioOrganigrama.SelectedItem as string
            ?? "";

        string tipo =
            PickerTipoOrganigrama.SelectedItem as string
            ?? "";

        List<AsambleaMobile> lista =
            _asambleas
                .Where(a =>
                    a.Anio == anio
                    && a.Tipo == tipo
                    && a.TieneOrganigrama)
                .OrderBy(a => a.Seccion)
                .ToList();

        MarcarProxima(
            lista);

        ListaOrganigrama.ItemsSource =
            lista;

        LblOrganigrama.Text =
            $"{lista.Count} secciones con organigrama · " +
            $"{anio} · {tipo}";

        DesplazarHastaProxima(
            ListaOrganigrama,
            lista);
    }


    /// <summary>
    /// Marca la asamblea que coincide con la próxima del calendario para
    /// que su tarjeta se resalte.
    /// </summary>
    private void MarcarProxima(
        List<AsambleaMobile> lista)
    {
        bool hayProxima =
            !string.IsNullOrWhiteSpace(
                _proximaSeccion);

        foreach (AsambleaMobile asamblea in lista)
        {
            asamblea.EsProxima =
                hayProxima
                && string.Equals(
                    (asamblea.Anio ?? "").Trim(),
                    _proximaAnio,
                    StringComparison.Ordinal)
                && string.Equals(
                    (asamblea.Tipo ?? "").Trim(),
                    _proximaTipo,
                    StringComparison.Ordinal)
                && string.Equals(
                    (asamblea.Seccion ?? "").Trim(),
                    _proximaSeccion,
                    StringComparison.OrdinalIgnoreCase);
        }
    }


    private static void DesplazarHastaProxima(
        CollectionView lista,
        List<AsambleaMobile> elementos)
    {
        AsambleaMobile? proxima =
            elementos.FirstOrDefault(
                a => a.EsProxima);

        if (proxima == null)
        {
            return;
        }

        try
        {
            lista.Dispatcher.Dispatch(
                async () =>
                {
                    try
                    {
                        await Task.Delay(
                            250);

                        lista.ScrollTo(
                            proxima,
                            position: ScrollToPosition.Center,
                            animate: false);
                    }
                    catch
                    {
                    }
                });
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------
    // REUNIÓN DE PRECURSORES
    // ------------------------------------------------------------------

    private async void FiltroPrecursores_Changed(
        object sender,
        EventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        await PintarPrecursoresAsync();
    }

    private async Task PintarPrecursoresAsync()
    {
        string anio =
            PickerAnioPrecursores.SelectedItem as string
            ?? "";

        ReunionPrecursoresMobile? reunion =
            _reuniones.FirstOrDefault(
                r => r.Anio == anio);

        var lista =
            new List<SesionPrecursoresUi>();

        if (reunion is not null)
        {
            List<SesionPrecursoresMobile> sesiones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerSesionesPrecursoresMobile(
                                reunion.Id));

            foreach (
                SesionPrecursoresMobile sesion in sesiones)
            {
                List<ParteSesionPrecursoresMobile> partes =
                    await Task.Run(
                        () =>
                            DatabaseHelper
                                .ObtenerPartesSesionPrecursoresMobile(
                                    sesion.Id));

                lista.Add(
                    new SesionPrecursoresUi
                    {
                        TituloSesion =
                            "Sesión " + sesion.Numero,
                        DetalleSesion =
                            sesion.Detalle,
                        Partes =
                            partes
                                .Select(p =>
                                    new ParteLineaUi
                                    {
                                        Linea =
                                            CrearLineaParte(p)
                                    })
                                .ToList()
                    });
            }
        }

        ListaPrecursores.ItemsSource =
            lista;

        LblPrecursores.Text =
            reunion is null
                ? $"Sin reunión de precursores en {anio}"
                : (string.IsNullOrWhiteSpace(reunion.Titulo)
                    ? $"Reunión de precursores {anio} · {lista.Count} sesiones"
                    : $"{reunion.Titulo} · {lista.Count} sesiones");
    }

    private static string CrearLineaParte(
        ParteSesionPrecursoresMobile parte)
    {
        var datos =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(parte.Hora))
        {
            datos.Add(parte.Hora);
        }

        if (!string.IsNullOrWhiteSpace(parte.Tipo))
        {
            datos.Add(parte.Tipo);
        }

        if (!string.IsNullOrWhiteSpace(parte.Titulo))
        {
            datos.Add(parte.Titulo);
        }

        if (!string.IsNullOrWhiteSpace(parte.Asignado))
        {
            datos.Add(parte.Asignado);
        }

        return "• " + string.Join(
            " · ",
            datos);
    }

    private async void BtnEditorPrecursores_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new PrecursoresPage());
    }

    // ------------------------------------------------------------------
    // NAVEGACIÓN Y REFRESCO
    // ------------------------------------------------------------------

    private async void Asamblea_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
            AsambleaMobile asamblea)
        {
            return;
        }

        await Navigation.PushAsync(
            new AsambleaDetallePage(
                asamblea));
    }

    private async void Refrescar_Refreshing(
        object sender,
        EventArgs e)
    {
        try
        {
            await CargarAsync();
        }
        finally
        {
            RefrescarPrograma.IsRefreshing =
                false;
        }
    }
}


public sealed class SesionPrecursoresUi
{
    public string TituloSesion { get; set; } = "";

    public string DetalleSesion { get; set; } = "";

    public List<ParteLineaUi> Partes { get; set; } = new();
}


public sealed class ParteLineaUi
{
    public string Linea { get; set; } = "";
}
