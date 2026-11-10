using System.Net;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// «Nuevo mensaje» al circuito (como en Windows): un correo para todas las
/// congregaciones, por secciones o para las que se marquen. Se prepara en
/// Gmail con CorreoService y se envía desde allí.
/// </summary>
public partial class MensajeCircuitoPage : ContentPage
{
    private readonly List<(CheckBox Casilla, string Seccion, string Email)> _congregaciones =
        new();

    private readonly Dictionary<string, CheckBox> _secciones =
        new();

    private readonly List<string> _adjuntos =
        new();

    // Al marcar en bloque (sección, todas) no se repasa casilla a casilla.
    private bool _marcando;


    public MensajeCircuitoPage()
    {
        InitializeComponent();
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_congregaciones.Count == 0)
        {
            await CargarAsync();
        }
    }


    private async Task CargarAsync()
    {
        List<Congregacion> lista;

        try
        {
            lista =
                await Task.Run(DatabaseHelper.ObtenerCongregacionesCompletasMobile);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Nuevo mensaje",
                "No se pudieron cargar las congregaciones: " + ex.Message,
                "Aceptar");

            return;
        }

        // Por secciones (las que no tienen, al final), como en Windows.
        var grupos =
            lista
                .GroupBy(c => (c.NombreSeccion ?? "").Trim())
                .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var grupo in grupos)
        {
            string seccion = grupo.Key;

            var casillaSeccion =
                NuevaCasilla();

            casillaSeccion.CheckedChanged +=
                (_, e) => MarcarSeccion(seccion, e.Value);

            _secciones[seccion] = casillaSeccion;

            ListaDestinatarios.Add(
                Fila(
                    casillaSeccion,
                    (seccion.Length == 0 ? "Sin sección" : "Sección " + seccion) + $" · {grupo.Count()}",
                    "",
                    esSeccion: true));

            foreach (Congregacion congregacion in grupo.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase))
            {
                string email = (congregacion.Email ?? "").Trim();

                var casilla =
                    NuevaCasilla();

                casilla.CheckedChanged +=
                    (_, _) => AlCambiarCongregacion(seccion);

                _congregaciones.Add((casilla, seccion, email));

                ListaDestinatarios.Add(
                    Fila(
                        casilla,
                        congregacion.Nombre,
                        email.Length > 0 ? email : "sin correo",
                        esSeccion: false));
            }
        }

        Resumir();
    }


    private static CheckBox NuevaCasilla() =>
        new()
        {
            Color = Color.FromArgb("#4BA3E3"),
            VerticalOptions = LayoutOptions.Center
        };


    // Una fila con su casilla; tocar el texto también la marca.
    private static View Fila(
        CheckBox casilla,
        string titulo,
        string detalle,
        bool esSeccion)
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
            FontSize = esSeccion ? 14 : 14.5,
            FontAttributes = esSeccion ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Color.FromArgb(esSeccion ? "#4BA3E3" : "#F2F4F7")
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

        var toque =
            new TapGestureRecognizer();

        toque.Tapped +=
            (_, _) => casilla.IsChecked = !casilla.IsChecked;

        textos.GestureRecognizers.Add(toque);

        var fila =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 4,
                Padding = new Thickness(esSeccion ? 0 : 18, esSeccion ? 8 : 0, 0, 0)
            };

        fila.Add(casilla, 0);
        fila.Add(textos, 1);

        return fila;
    }


    private void MarcarSeccion(
        string seccion,
        bool marcada)
    {
        if (_marcando)
        {
            return;
        }

        _marcando = true;

        foreach (var congregacion in _congregaciones.Where(c => c.Seccion == seccion))
        {
            congregacion.Casilla.IsChecked = marcada;
        }

        _marcando = false;

        Resumir();
    }


    // La casilla de la sección queda marcada si lo están todas las suyas.
    private void AlCambiarCongregacion(string seccion)
    {
        if (_marcando)
        {
            return;
        }

        _marcando = true;

        _secciones[seccion].IsChecked =
            _congregaciones
                .Where(c => c.Seccion == seccion)
                .All(c => c.Casilla.IsChecked);

        _marcando = false;

        Resumir();
    }


    private void MarcarTodas(bool marcada)
    {
        _marcando = true;

        foreach (var congregacion in _congregaciones)
        {
            congregacion.Casilla.IsChecked = marcada;
        }

        foreach (CheckBox seccion in _secciones.Values)
        {
            seccion.IsChecked = marcada;
        }

        _marcando = false;

        Resumir();
    }


    private void BtnTodas_Clicked(object sender, EventArgs e) =>
        MarcarTodas(true);


    private void BtnNinguna_Clicked(object sender, EventArgs e) =>
        MarcarTodas(false);


    private void Resumir()
    {
        var marcadas =
            _congregaciones
                .Where(c => c.Casilla.IsChecked)
                .ToList();

        int sinCorreo =
            marcadas.Count(c => c.Email.Length == 0);

        LblResumen.Text =
            marcadas.Count == 0
                ? "Marca las congregaciones (o una sección entera)."
                : $"{marcadas.Count} {(marcadas.Count == 1 ? "congregación" : "congregaciones")}"
                  + (sinCorreo > 0 ? $" · {sinCorreo} sin correo (no recibirán el mensaje)" : "");
    }


    // =========================================================================
    // ADJUNTOS
    // =========================================================================

    private async void BtnAdjuntar_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var archivos =
                await FilePicker.Default.PickMultipleAsync();

            if (archivos is null)
            {
                return;
            }

            foreach (var archivo in archivos)
            {
                if (archivo is not null
                    && !_adjuntos.Contains(archivo.FullPath))
                {
                    _adjuntos.Add(archivo.FullPath);
                }
            }

            PintarAdjuntos();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Adjuntos",
                "No se pudo añadir el archivo: " + ex.Message,
                "Aceptar");
        }
    }


    private void PintarAdjuntos()
    {
        ListaAdjuntos.Clear();

        foreach (string ruta in _adjuntos.ToList())
        {
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

            fila.Add(
                new Label
                {
                    Text = Path.GetFileName(ruta),
                    FontSize = 13,
                    TextColor = Color.FromArgb("#F2F4F7"),
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.MiddleTruncation
                },
                0);

            var quitar =
                new Button
                {
                    Text = "Quitar",
                    FontSize = 12,
                    HeightRequest = 32,
                    Padding = new Thickness(10, 2),
                    CornerRadius = 8,
                    BackgroundColor = Color.FromArgb("#3A2023"),
                    TextColor = Color.FromArgb("#FFB7BC")
                };

            quitar.Clicked +=
                (_, _) =>
                {
                    _adjuntos.Remove(ruta);
                    PintarAdjuntos();
                };

            fila.Add(quitar, 1);

            ListaAdjuntos.Add(fila);
        }
    }


    // =========================================================================
    // PREPARAR EL CORREO
    // =========================================================================

    private async void BtnPreparar_Clicked(
        object sender,
        EventArgs e)
    {
        List<string> correos =
            _congregaciones
                .Where(c => c.Casilla.IsChecked && c.Email.Length > 0)
                .Select(c => c.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (correos.Count == 0)
        {
            await DisplayAlert(
                "Nuevo mensaje",
                "Marca al menos una congregación con correo.",
                "Aceptar");

            return;
        }

        if (string.IsNullOrWhiteSpace(TxtAsunto.Text)
            && !await DisplayAlert(
                "Nuevo mensaje",
                "El correo no tiene asunto. ¿Prepararlo igualmente?",
                "Sí",
                "No"))
        {
            return;
        }

        // El texto, como HTML (con sus saltos de línea).
        string cuerpo =
            WebUtility.HtmlEncode(TxtCuerpo.Text ?? "")
                .Replace("\r\n", "\n")
                .Replace("\n", "<br>");

        // Como en Windows: todas las congregaciones en «Para».
        await Services.CorreoService.ComponerAsync(
            (TxtAsunto.Text ?? "").Trim(),
            cuerpo,
            correos,
            _adjuntos);
    }
}
