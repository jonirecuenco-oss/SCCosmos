using Microsoft.Maui.Controls.Shapes;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Disposición de la sala: filas y columnas ajustables, pasillos,
/// mesa del instructor centrada delante de la primera fila, PDF con
/// la disposición y asignación de personas a cada butaca.
/// </summary>
public partial class EscuelaAsientosPage : ContentPage, IActualizableMobile
{
    private readonly string _escuelaId;

    private List<EscuelaAlumnoMobile> _alumnos =
        new();

    private EscuelaClaseMobile? _escuela;

    public EscuelaAsientosPage(
        string escuelaId)
    {
        InitializeComponent();

        _escuelaId =
            escuelaId ?? "";
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
        try
        {
            var resultado =
                await Task.Run(
                    () =>
                    {
                        EscuelaClaseMobile escuela =
                            DatabaseHelper
                                .ObtenerEscuelaClaseMobile(
                                    _escuelaId);

                        List<EscuelaAlumnoMobile> alumnos =
                            DatabaseHelper
                                .ObtenerAlumnosEscuelaMobile(
                                    _escuelaId);

                        return (escuela, alumnos);
                    });

            if (resultado.escuela is null)
            {
                await Navigation.PopAsync();

                return;
            }

            _escuela =
                resultado.escuela;

            _alumnos =
                resultado.alumnos;

            LblSemana.Text =
                _escuela.SemanaTexto;

            LblSinAlumnos.IsVisible =
                _alumnos.Count == 0;

            ConstruirSala();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Asientos",
                "No se pudieron cargar las butacas:\n" + ex.Message,
                "Aceptar");
        }
    }


    /// <summary>
    /// Dibuja la sala: mesa del instructor centrada delante de la
    /// primera fila, filas de butacas y pasillos.
    /// </summary>
    private void ConstruirSala()
    {
        try
        {
            if (_escuela is null)
            {
                return;
            }

            int filas =
                _escuela.FilasEfectivas;

            int columnas =
                _escuela.ColumnasEfectivas;

            List<int> pasillos =
                _escuela.PasillosLista;

            LblNumFilas.Text =
                filas.ToString();

            LblNumColumnas.Text =
                columnas.ToString();

            // Botones de pasillos (entre columna y columna).
            PanelPasillos.Children.Clear();

            for (int k = 1; k < columnas; k++)
            {
                bool activo =
                    pasillos.Contains(k);

                var boton =
                    new Button
                    {
                        Text = $"{k}|{k + 1}",
                        FontSize = 11.5,
                        Padding = new Thickness(10, 2),
                        HeightRequest = 34,
                        CornerRadius = 8,
                        Margin = new Thickness(0, 0, 6, 6),
                        BackgroundColor = activo
                            ? Color.FromArgb("#2F6FA8")
                            : Color.FromArgb("#1A1D22"),
                        TextColor = activo
                            ? Colors.White
                            : Color.FromArgb("#AEB4BC")
                    };

                int columna = k;

                boton.Clicked +=
                    async (s, e) => await AlternarPasilloAsync(columna);

                PanelPasillos.Children.Add(
                    boton);
            }

            PanelFilasAsientos.Children.Clear();

            // Ancho disponible (en unidades de diseño) para repartir.
            double anchoPantalla =
                DeviceDisplay.MainDisplayInfo.Width
                / DeviceDisplay.MainDisplayInfo.Density;

            double anchoDisponible =
                Math.Max(
                    240,
                    anchoPantalla - 40);

            double anchoHuecoPasillo =
                16;

            double anchoButaca =
                (anchoDisponible
                 - ((columnas - 1) * 6)
                 - (pasillos.Count * anchoHuecoPasillo))
                / columnas;

            anchoButaca =
                Math.Clamp(anchoButaca, 46, 104);

            // Mesa del instructor (centrada, delante de la primera fila).
            var mesa =
                new Border
                {
                    BackgroundColor = Color.FromArgb("#242A34"),
                    Stroke = Color.FromArgb("#4BA3E3"),
                    StrokeThickness = 1.5,
                    Padding = new Thickness(12, 8),
                    WidthRequest = Math.Min(240, anchoDisponible * 0.62),
                    Margin = new Thickness(0, 0, 0, 14),
                    HorizontalOptions = LayoutOptions.Center,
                    StrokeShape = new RoundRectangle
                    {
                        CornerRadius = new CornerRadius(10)
                    },
                    Content = new Label
                    {
                        Text = "MESA DEL INSTRUCTOR",
                        FontSize = 11.5,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#C8DDF5"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                };

            PanelFilasAsientos.Children.Add(
                mesa);

            int numero =
                0;

            for (int f = 1; f <= filas; f++)
            {
                var fila =
                    new HorizontalStackLayout
                    {
                        Spacing = 6,
                        HorizontalOptions = LayoutOptions.Center
                    };

                for (int c = 1; c <= columnas; c++)
                {
                    numero++;

                    var ocupante =
                        _alumnos.FirstOrDefault(
                            a => a.Asiento == numero);

                    bool ocupado =
                        ocupante is not null;

                    var contenido =
                        new VerticalStackLayout
                        {
                            Spacing = 1,
                            VerticalOptions = LayoutOptions.Center
                        };

                    contenido.Children.Add(
                        new Label
                        {
                            Text = numero.ToString(),
                            FontSize = 11,
                            TextColor = Color.FromArgb("#8B94A0"),
                            HorizontalTextAlignment = TextAlignment.Center
                        });

                    contenido.Children.Add(
                        new Label
                        {
                            Text = ocupado
                                ? ocupante!.Nombre
                                : "Libre",
                            FontSize = 10.5,
                            TextColor = Color.FromArgb(
                                ocupado
                                    ? "#FFFFFF"
                                    : "#5E6672"),
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.TailTruncation,
                            MaxLines = 2
                        });

                    var butaca =
                        new Border
                        {
                            BackgroundColor = Color.FromArgb(
                                ocupado
                                    ? "#1B2A3E"
                                    : "#141820"),
                            Stroke = Color.FromArgb(
                                ocupado
                                    ? "#4BA3E3"
                                    : "#2A2F38"),
                            StrokeThickness = 1.5,
                            Padding = new Thickness(4, 6),
                            WidthRequest = anchoButaca,
                            HeightRequest = 58,
                            BindingContext = new AsientoMobile
                            {
                                Numero = numero,
                                Ocupante = ocupado
                                    ? ocupante!.Nombre
                                    : "Libre",
                                ColorFondo = ocupado ? "#1B2A3E" : "#141820",
                                ColorBorde = ocupado ? "#4BA3E3" : "#2A2F38"
                            },
                            StrokeShape = new RoundRectangle
                            {
                                CornerRadius = new CornerRadius(9)
                            },
                            Content = contenido
                        };

                    var toque =
                        new TapGestureRecognizer();

                    toque.Tapped +=
                        Butaca_Tapped;

                    butaca.GestureRecognizers.Add(
                        toque);

                    fila.Children.Add(
                        butaca);

                    if (pasillos.Contains(c))
                    {
                        fila.Children.Add(
                            new BoxView
                            {
                                WidthRequest = anchoHuecoPasillo
                            });
                    }
                }

                PanelFilasAsientos.Children.Add(
                    fila);
            }
        }
        catch
        {
        }
    }


    private async void BtnMenosFila_Clicked(
        object sender,
        EventArgs e)
    {
        await AjustarDisposicionAsync(-1, 0);
    }


    private async void BtnMasFila_Clicked(
        object sender,
        EventArgs e)
    {
        await AjustarDisposicionAsync(1, 0);
    }


    private async void BtnMenosColumna_Clicked(
        object sender,
        EventArgs e)
    {
        await AjustarDisposicionAsync(0, -1);
    }


    private async void BtnMasColumna_Clicked(
        object sender,
        EventArgs e)
    {
        await AjustarDisposicionAsync(0, 1);
    }


    private async Task AjustarDisposicionAsync(
        int deltaFilas,
        int deltaColumnas)
    {
        try
        {
            if (_escuela is null)
            {
                return;
            }

            int filas =
                Math.Clamp(
                    _escuela.FilasEfectivas + deltaFilas,
                    1,
                    15);

            int columnas =
                Math.Clamp(
                    _escuela.ColumnasEfectivas + deltaColumnas,
                    1,
                    20);

            if (filas == _escuela.FilasEfectivas
                && columnas == _escuela.ColumnasEfectivas)
            {
                return;
            }

            string id =
                _escuela.Id;

            string pasillos =
                string.Join(
                    ",",
                    _escuela.PasillosLista
                        .Where(p => p < columnas));

            await Task.Run(
                () => DatabaseHelper.EstablecerDisposicionEscuelaMobile(
                    id,
                    filas,
                    columnas,
                    pasillos));

            await CargarAsync();
        }
        catch
        {
        }
    }


    private async Task AlternarPasilloAsync(
        int columna)
    {
        try
        {
            if (_escuela is null)
            {
                return;
            }

            List<int> lista =
                _escuela.PasillosLista;

            if (lista.Contains(columna))
            {
                lista.Remove(columna);
            }
            else
            {
                lista.Add(columna);
            }

            string id =
                _escuela.Id;

            string pasillos =
                string.Join(
                    ",",
                    lista.OrderBy(n => n));

            await Task.Run(
                () => DatabaseHelper.EstablecerDisposicionEscuelaMobile(
                    id,
                    _escuela.FilasEfectivas,
                    _escuela.ColumnasEfectivas,
                    pasillos));

            await CargarAsync();
        }
        catch
        {
        }
    }


    private async void BtnPdfAsientos_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            if (_escuela is null)
            {
                return;
            }

            string id =
                _escuela.Id;

            var resultado =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerEscuelaClaseMobile(
                            id));

            if (resultado is null)
            {
                return;
            }

            var alumnos =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerAlumnosEscuelaMobile(
                            id));

            string ruta =
                System.IO.Path.Combine(
                    FileSystem.CacheDirectory,
                    "Butacas_"
                    + resultado.SemanaInicio
                    + ".pdf");

            await Task.Run(
                () => ButacasPdfService.Generar(
                    resultado,
                    alumnos,
                    ruta));

            DatabaseHelper.AnotarActividadMobile(
                "Escuelas",
                "Generó el PDF de la disposición de butacas de la escuela de la "
                + resultado.SemanaTexto.ToLower());

            await Launcher.Default.OpenAsync(
                new OpenFileRequest(
                    "Disposición de butacas",
                    new ReadOnlyFile(ruta)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "PDF",
                "No se pudo generar el PDF:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async void Butaca_Tapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            if (sender is not Border borde
                || borde.BindingContext is not AsientoMobile asiento)
            {
                return;
            }

            var ocupante =
                _alumnos.FirstOrDefault(
                    a => a.Asiento == asiento.Numero);

            var opciones =
                _alumnos
                    .Select(
                        a => a.Nombre)
                    .ToList();

            opciones.Add(
                "Vaciar butaca");

            string eleccion =
                await DisplayActionSheet(
                    $"Butaca {asiento.Numero}",
                    "Cancelar",
                    null,
                    opciones.ToArray());

            if (string.IsNullOrWhiteSpace(
                    eleccion)
                || eleccion == "Cancelar")
            {
                return;
            }

            if (eleccion == "Vaciar butaca")
            {
                if (ocupante is null)
                {
                    return;
                }

                await Task.Run(
                    () => DatabaseHelper
                        .AsignarAsientoEscuelaMobile(
                            ocupante.Id,
                            0));

                await CargarAsync();

                return;
            }

            var elegido =
                _alumnos.FirstOrDefault(
                    a => a.Nombre == eleccion);

            if (elegido is null)
            {
                return;
            }

            await Task.Run(
                () => DatabaseHelper
                    .AsignarAsientoEscuelaMobile(
                        elegido.Id,
                        asiento.Numero));

            await CargarAsync();
        }
        catch
        {
        }
    }
}


/// <summary>
/// Una butaca de la sala.
/// </summary>
public sealed class AsientoMobile
{
    public int Numero { get; set; }

    public string Ocupante { get; set; } = "";

    public string NumeroTexto =>
        "Butaca " + Numero;

    public string ColorFondo { get; set; } = "#141820";

    public string ColorBorde { get; set; } = "#2A2F38";
}
