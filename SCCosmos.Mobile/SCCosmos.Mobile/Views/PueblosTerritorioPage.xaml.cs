using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Los pueblos del territorio de una congregación (además del del salón):
/// en Circuito se ve su tiempo y la revisión de avisos rojos los mira. Se
/// guardan al momento en Configuracion, que se sincroniza con Windows.
/// </summary>
public partial class PueblosTerritorioPage : ContentPage
{
    private readonly Congregacion _congregacion;

    private readonly string _ineSalon;

    private List<PuebloTerritorio> _pueblos = new();

    private List<(string Ine, string Nombre, int Personas)> _sugerencias = new();

    private bool _cargado;

    private bool _ocupado;


    public PueblosTerritorioPage(Congregacion congregacion)
    {
        InitializeComponent();

        _congregacion = congregacion;

        _ineSalon =
            TiempoAemet.BuscarIne(congregacion.Municipio, congregacion.Provincia) ?? "";

        LblCongregacion.Text =
            congregacion.Nombre.ToUpperInvariant();

        LblSalon.Text =
            _ineSalon.Length > 0
                ? $"El del salón: {TiempoAemet.NombreMunicipio(_ineSalon, congregacion.Municipio)}."
                : "El municipio del salón no está en la lista del INE.";
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_cargado)
        {
            return;
        }

        _cargado = true;

        string codigo = _congregacion.Codigo;

        _pueblos =
            await Task.Run(() => DatabaseHelper.ObtenerPueblosTerritorio(codigo));

        _sugerencias =
            await Task.Run(() => PueblosTerritorio.Sugerencias(DatabaseHelper.ObtenerDireccionesCongregacion(codigo), _ineSalon));

        Pintar();
    }


    private void Pintar()
    {
        ListaPueblos.Clear();

        if (_pueblos.Count == 0)
        {
            ListaPueblos.Add(Nota("Todavía no hay ninguno."));
        }

        foreach (PuebloTerritorio pueblo in _pueblos.ToList())
        {
            ListaPueblos.Add(
                Fila(
                    pueblo.Nombre,
                    pueblo.Latitud == 0 && pueblo.Longitud == 0 ? "Sin su sitio en el mapa: no avisa de sus alertas rojas" : "",
                    "Quitar",
                    quitar: true,
                    () => _ = QuitarAsync(pueblo)));
        }

        var pendientes =
            _sugerencias
                .Where(s => _pueblos.All(p => p.Ine != s.Ine))
                .ToList();

        TarjetaSugerencias.IsVisible =
            pendientes.Count > 0;

        ListaSugerencias.Clear();

        foreach (var sugerencia in pendientes)
        {
            ListaSugerencias.Add(
                Fila(
                    sugerencia.Nombre,
                    sugerencia.Personas == 1 ? "1 publicador" : $"{sugerencia.Personas} publicadores",
                    "Añadir",
                    quitar: false,
                    () => _ = AnadirAsync(sugerencia.Ine)));
        }

        PintarResultados();
    }


    private void TxtBuscar_TextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        PintarResultados();


    private void PintarResultados()
    {
        ListaResultados.Clear();

        string provincia =
            _ineSalon.Length >= 2 ? _ineSalon[..2] : "";

        foreach (var municipio in TiempoAemet.BuscarMunicipios(TxtBuscar.Text ?? "", provincia, 8))
        {
            bool esta =
                municipio.Ine == _ineSalon
                || _pueblos.Any(p => p.Ine == municipio.Ine);

            ListaResultados.Add(
                Fila(
                    municipio.Nombre,
                    municipio.Provincia + (municipio.Ine == _ineSalon ? " · el del salón" : esta ? " · ya está" : ""),
                    esta ? "" : "Añadir",
                    quitar: false,
                    () => _ = AnadirAsync(municipio.Ine)));
        }
    }


    private async Task AnadirAsync(string ine)
    {
        if (_ocupado
            || ine == _ineSalon
            || _pueblos.Any(p => p.Ine == ine))
        {
            return;
        }

        _ocupado = true;

        string nombre =
            TiempoAemet.NombreMunicipio(ine, ine);

        LblEstado.Text = $"Buscando {nombre} en el mapa…";
        LblEstado.IsVisible = true;

        try
        {
            // Su sitio en el mapa (OpenStreetMap): con él se sabe su zona de aviso.
            var coordenadas =
                await PueblosTerritorio.CoordenadasAsync(ine, CancellationToken.None);

            _pueblos.Add(
                new PuebloTerritorio(
                    ine,
                    coordenadas?.Latitud ?? 0,
                    coordenadas?.Longitud ?? 0));

            string codigo = _congregacion.Codigo;
            List<PuebloTerritorio> pueblos = _pueblos.ToList();

            await Task.Run(() => DatabaseHelper.GuardarPueblosTerritorio(codigo, pueblos));

            if (coordenadas is null)
            {
                await DisplayAlert(
                    "Pueblos del territorio",
                    $"No se ha encontrado {nombre} en el mapa (¿sin conexión?). Se verá su tiempo, pero no avisará de sus alertas rojas: quítalo y vuelve a añadirlo con conexión.",
                    "Aceptar");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Pueblos del territorio",
                "No se pudo guardar: " + ex.Message,
                "Aceptar");
        }
        finally
        {
            _ocupado = false;
            LblEstado.IsVisible = false;
        }

        Pintar();
    }


    private async Task QuitarAsync(PuebloTerritorio pueblo)
    {
        if (_ocupado)
        {
            return;
        }

        _pueblos.RemoveAll(p => p.Ine == pueblo.Ine);

        string codigo = _congregacion.Codigo;
        List<PuebloTerritorio> pueblos = _pueblos.ToList();

        try
        {
            await Task.Run(() => DatabaseHelper.GuardarPueblosTerritorio(codigo, pueblos));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Pueblos del territorio",
                "No se pudo guardar: " + ex.Message,
                "Aceptar");
        }

        Pintar();
    }


    private static Label Nota(string texto) =>
        new()
        {
            Text = texto,
            FontSize = 13,
            TextColor = Color.FromArgb("#808893")
        };


    // Una fila: el pueblo, una línea debajo y su botón (sin texto, sin botón).
    private static View Fila(
        string titulo,
        string detalle,
        string boton,
        bool quitar,
        Action alPulsar)
    {
        var textos =
            new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center
            };

        textos.Add(new Label
        {
            Text = titulo,
            FontSize = 14.5,
            TextColor = Color.FromArgb("#F2F4F7")
        });

        if (detalle.Length > 0)
        {
            textos.Add(new Label
            {
                Text = detalle,
                FontSize = 11.5,
                TextColor = Color.FromArgb("#808893")
            });
        }

        var fila =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };

        fila.Add(textos, 0);

        if (boton.Length > 0)
        {
            var accion =
                new Button
                {
                    Text = boton,
                    FontSize = 12,
                    HeightRequest = 34,
                    Padding = new Thickness(12, 2),
                    CornerRadius = 8,
                    FontAutoScalingEnabled = false,
                    BackgroundColor = Color.FromArgb(quitar ? "#3A2023" : "#1B3A52"),
                    TextColor = Color.FromArgb(quitar ? "#FFB7BC" : "#F2F4F7"),
                    VerticalOptions = LayoutOptions.Center
                };

            accion.Clicked +=
                (_, _) => alPulsar();

            fila.Add(accion, 1);
        }

        return fila;
    }
}
