using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// RSC (móvil): lista de reuniones para el Servicio del Campo y
/// buscador por día (mañana y tarde), con los grupos formados.
/// </summary>
public partial class RscPage : ContentPage, IActualizableMobile
{
    private bool _filtrando;

    private bool _cargado;

    public RscPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _cargado =
            true;

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarAsync();
        Services.Perf.Registrar("RSC", reloj.ElapsedMilliseconds);
    }

    public void DatosActualizados()
    {
        try
        {
            if (_cargado)
            {
                _ = CargarAsync();
            }
        }
        catch
        {
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            DateTime fecha =
                DpFiltro.Date
                ?? DateTime.Today;

            bool filtrando =
                _filtrando;

            var resultado =
                await Task.Run(
                    () =>
                    {
                        List<RscReunionMobile> reuniones =
                            filtrando
                                ? DatabaseHelper
                                    .ObtenerReunionesRscPorFechaMobile(
                                        fecha.ToString(
                                            "yyyy-MM-dd"))
                                : DatabaseHelper
                                    .ObtenerTodasReunionesRscMobile();

                        var resumenes =
                            new Dictionary<string, List<string>>();

                        if (filtrando)
                        {
                            foreach (var reunion in reuniones)
                            {
                                resumenes[reunion.Id] =
                                    DatabaseHelper
                                        .ObtenerGruposResumenRscMobile(
                                            reunion.Id);
                            }
                        }

                        return (reuniones, resumenes);
                    });

            var (reuniones, resumenes) =
                resultado;

            LblInfoFiltro.Text =
                filtrando
                    ? "Mostrando: "
                      + DescripcionDia(fecha)
                      + " · mañana y tarde"
                    : (reuniones.Count == 1
                        ? "1 RSC guardada"
                        : reuniones.Count
                          + " RSC guardadas");

            LblVacio.IsVisible =
                reuniones.Count == 0;

            ListaReuniones.Clear();

            foreach (var reunion in reuniones)
            {
                ListaReuniones.Add(
                    ConstruirTarjetaReunion(
                        reunion,
                        filtrando
                            ? resumenes.GetValueOrDefault(
                                  reunion.Id)
                              ?? new List<string>()
                            : null));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo cargar la lista:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private static string DescripcionDia(
        DateTime fecha)
    {
        string texto =
            fecha.ToString(
                "ddd d 'de' MMMM 'de' yyyy",
                new CultureInfo("es-ES"));

        if (texto.Length > 1)
        {
            texto =
                char.ToUpper(
                    texto[0],
                    new CultureInfo("es-ES"))
                + texto.Substring(1);
        }

        return texto;
    }


    private Border ConstruirTarjetaReunion(
        RscReunionMobile reunion,
        List<string>? resumenGrupos)
    {
        var tarjeta =
            new Border
            {
                Style = Estilo("Tarjeta")
            };

        var contenido =
            new VerticalStackLayout
            {
                Spacing = 6
            };

        contenido.Add(
            new Label
            {
                Text = DescripcionDia(
                    DateTime.TryParse(
                        reunion.Fecha,
                        out DateTime fecha)
                            ? fecha
                            : DateTime.Today),
                Style = Estilo("TituloTarjeta")
            });

        string congregacion =
            string.IsNullOrWhiteSpace(
                reunion.CongregacionNombre)
                ? "(sin congregación)"
                : reunion.CongregacionNombre;

        contenido.Add(
            new Label
            {
                Text = reunion.Turno
                       + " · "
                       + congregacion
                       + " · "
                       + reunion.NumeroGrupos
                       + (reunion.NumeroGrupos == 1
                           ? " grupo"
                           : " grupos")
                       + " · "
                       + reunion.NumeroPersonas
                       + " para predicar",
                Style = Estilo("TextoTarjeta")
            });

        if (resumenGrupos != null)
        {
            if (resumenGrupos.Count == 0)
            {
                contenido.Add(
                    new Label
                    {
                        Text = "Sin grupos formados todavía.",
                        Style = Estilo("TextoNota")
                    });
            }
            else
            {
                foreach (string linea in resumenGrupos)
                {
                    contenido.Add(
                        new Label
                        {
                            Text = linea,
                            FontSize = 11.5,
                            TextColor = Color.FromArgb(
                                linea.StartsWith("   ")
                                    ? "#C6CDD6"
                                    : "#8FB8E0"),
                            FontAutoScalingEnabled = false
                        });
                }
            }
        }

        var botones =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star),
                    new ColumnDefinition(
                        GridLength.Star)
                },
                ColumnSpacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };

        var abrir =
            new Button
            {
                Text = "Abrir",
                Style = Estilo("BotonSecundario"),
                HeightRequest = 40,
                FontSize = 12.5
            };

        string idAbrir =
            reunion.Id;

        abrir.Clicked +=
            async (s, e) =>
            {
                await Navigation.PushAsync(
                    new RscEditorPage(
                        idAbrir));
            };

        var eliminar =
            new Button
            {
                Text = "Eliminar",
                Style = Estilo("BotonPeligro"),
                HeightRequest = 40,
                FontSize = 12.5
            };

        var reunionEliminar =
            reunion;

        eliminar.Clicked +=
            async (s, e) =>
            {
                bool confirmar =
                    await DisplayAlert(
                        "Eliminar RSC",
                        "¿Eliminar la RSC de "
                        + reunionEliminar.CongregacionNombre
                        + " ("
                        + reunionEliminar.FechaVisual
                        + " · "
                        + reunionEliminar.Turno
                        + ")?\n\nSe borrarán también sus asistentes y grupos.",
                        "Sí",
                        "No");

                if (!confirmar)
                {
                    return;
                }

                await Task.Run(
                    () => DatabaseHelper
                        .EliminarReunionRscMobile(
                            reunionEliminar.Id));

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Eliminó la RSC de "
                    + reunionEliminar.CongregacionNombre
                    + " ("
                    + reunionEliminar.FechaVisual
                    + " · "
                    + reunionEliminar.Turno
                    + ")");

                await CargarAsync();
            };

        botones.Add(
            abrir,
            0,
            0);

        botones.Add(
            eliminar,
            1,
            0);

        contenido.Add(
            botones);

        tarjeta.Content =
            contenido;

        return tarjeta;
    }


    private static Style Estilo(
        string clave)
    {
        return (Style)Application
            .Current!
            .Resources[clave];
    }


    private async void BtnNueva_Clicked(
        object? sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new RscNuevaPage());
    }


    private void DpFiltro_DateSelected(
        object? sender,
        DateChangedEventArgs e)
    {
        _filtrando =
            true;

        _ = CargarAsync();
    }


    private void BtnTodas_Clicked(
        object? sender,
        EventArgs e)
    {
        _filtrando =
            false;

        _ = CargarAsync();
    }
}
