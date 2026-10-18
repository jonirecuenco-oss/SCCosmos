using Microsoft.Maui.Controls.Shapes;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Talleres de la escuela: asignado, acompañante y hora de ensayo de
/// cada punto, agrupados por taller (T1..T4), con PDF.
/// </summary>
public partial class EscuelaTalleresPage : ContentPage, IActualizableMobile
{
    private readonly string _escuelaId;

    private List<ItemPersonaEscuelaMobile> _personas;

    public EscuelaTalleresPage(
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

                        List<EscuelaTallerPuntoMobile> puntos =
                            DatabaseHelper
                                .ObtenerTalleresEscuelaMobile(
                                    _escuelaId);

                        return (escuela, puntos);
                    });

            if (resultado.escuela is null)
            {
                await Navigation.PopAsync();

                return;
            }

            LblSubtitulo.Text =
                resultado.escuela.SemanaTexto
                + " · asigna a cada punto su publicador, acompañante y hora de ensayo";

            ConstruirPanel(
                resultado.puntos);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Talleres",
                "No se pudieron cargar los talleres:\n" + ex.Message,
                "Aceptar");
        }
    }


    private void ConstruirPanel(
        List<EscuelaTallerPuntoMobile> puntos)
    {
        try
        {
            PanelTalleres.Children.Clear();

            foreach (var grupo in
                     puntos.GroupBy(p => p.Taller).OrderBy(g => g.Key))
            {
                PanelTalleres.Children.Add(
                    new Label
                    {
                        Text = $"TALLER {grupo.Key} (T{grupo.Key})",
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#4BA3E3"),
                        Margin = new Thickness(0, 10, 0, 0)
                    });

                foreach (EscuelaTallerPuntoMobile punto in grupo)
                {
                    PanelTalleres.Children.Add(
                        CrearTarjetaPunto(
                            punto));
                }
            }
        }
        catch
        {
        }
    }


    private Border CrearTarjetaPunto(
        EscuelaTallerPuntoMobile punto)
    {
        var contenido =
            new VerticalStackLayout
            {
                Spacing = 8
            };

        contenido.Children.Add(
            new Label
            {
                Text = punto.Titulo,
                FontSize = 13,
                TextColor = Colors.White,
                LineBreakMode = LineBreakMode.WordWrap
            });

        var botones =
            new Grid
            {
                ColumnSpacing = 8
            };

        botones.ColumnDefinitions.Add(
            new ColumnDefinition(
                new GridLength(1, GridUnitType.Star)));

        botones.ColumnDefinitions.Add(
            new ColumnDefinition(
                new GridLength(1, GridUnitType.Star)));

        var btnAsignado =
            new Button
            {
                Text = "Asignado: " + punto.AsignadoTexto,
                FontSize = 11.5,
                Padding = new Thickness(8, 6),
                BackgroundColor = Color.FromArgb("#1E242C"),
                TextColor = Color.FromArgb("#B9DEFF"),
                CornerRadius = 8
            };

        btnAsignado.Clicked +=
            async (s, e) => await ElegirPersonaAsync(
                punto,
                "Asignado");

        Grid.SetColumn(
            btnAsignado,
            0);

        botones.Children.Add(
            btnAsignado);

        var btnAcompanante =
            new Button
            {
                Text = "Acompañante: " + punto.AcompananteTexto,
                FontSize = 11.5,
                Padding = new Thickness(8, 6),
                BackgroundColor = Color.FromArgb("#1E242C"),
                TextColor = Color.FromArgb("#B9DEFF"),
                CornerRadius = 8
            };

        btnAcompanante.Clicked +=
            async (s, e) => await ElegirPersonaAsync(
                punto,
                "Acompañante");

        Grid.SetColumn(
            btnAcompanante,
            1);

        botones.Children.Add(
            btnAcompanante);

        contenido.Children.Add(
            botones);

        var filaEnsayo =
            new HorizontalStackLayout
            {
                Spacing = 8
            };

        filaEnsayo.Children.Add(
            new Label
            {
                Text = "Ensayo:",
                FontSize = 12.5,
                TextColor = Color.FromArgb("#AEB4BC"),
                VerticalOptions = LayoutOptions.Center
            });

        var entrada =
            new Entry
            {
                Text = punto.HoraEnsayo,
                Placeholder = "17:00",
                FontSize = 13,
                TextColor = Colors.White,
                WidthRequest = 90
            };

        entrada.Unfocused +=
            async (s, e) => await GuardarEnsayoAsync(
                punto,
                entrada.Text);

        filaEnsayo.Children.Add(
            entrada);

        contenido.Children.Add(
            filaEnsayo);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#161A20"),
            StrokeThickness = 0,
            Padding = new Thickness(12),
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(10)
            },
            Content = contenido
        };
    }


    private async Task AsegurarPersonasAsync()
    {
        if (_personas != null)
        {
            return;
        }

        var lista =
            await Task.Run(
                () => DatabaseHelper.ObtenerPersonas());

        _personas =
            lista
                .Select(
                    p =>
                    {
                        string nombre =
                            string.IsNullOrWhiteSpace(
                                p.NombreComun)
                                ? p.Nombre
                                : p.NombreComun;

                        string congregacion =
                            string.IsNullOrWhiteSpace(
                                p.CongregacionNombre)
                                ? p.CongregacionCodigo
                                : p.CongregacionNombre;

                        return new ItemPersonaEscuelaMobile
                        {
                            Persona = p,
                            NombreSolo = nombre,
                            Visual = string.IsNullOrWhiteSpace(
                                congregacion)
                                ? nombre
                                : $"{nombre} ({congregacion})"
                        };
                    })
                .OrderBy(
                    i => i.Visual)
                .ToList();
    }


    private async Task ElegirPersonaAsync(
        EscuelaTallerPuntoMobile punto,
        string campo)
    {
        try
        {
            await AsegurarPersonasAsync();

            var opciones =
                new List<ItemPersonaEscuelaMobile>
                {
                    new()
                    {
                        Persona = null,
                        Visual = "(nadie)"
                    }
                };

            opciones.AddRange(
                _personas);

            var pagina =
                new EscuelaInstructorPickerPage(
                    campo == "Asignado"
                        ? $"Asignado del punto {punto.Numero}"
                        : $"Acompañante del punto {punto.Numero}",
                    opciones);

            await Navigation.PushAsync(
                pagina);

            var elegido =
                await pagina.EsperarEleccionAsync();

            if (elegido is null)
            {
                return;
            }

            string nombre =
                elegido.Persona is null
                    ? ""
                    : elegido.NombreSolo;

            int personaId =
                elegido.Persona?.Id ?? 0;

            string descripcion;

            if (campo == "Asignado")
            {
                punto.AsignadoId =
                    personaId;

                punto.AsignadoNombre =
                    nombre;

                descripcion =
                    personaId == 0
                        ? $"Dejó sin asignar el punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {SemanaTexto()}"
                        : $"Asignó a {nombre} al punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {SemanaTexto()}";
            }
            else
            {
                punto.AcompananteId =
                    personaId;

                punto.AcompananteNombre =
                    nombre;

                descripcion =
                    personaId == 0
                        ? $"Quitó el acompañante del punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {SemanaTexto()}"
                        : $"Puso a {nombre} como acompañante del punto {punto.Numero} (taller {punto.TallerTexto}) de la escuela de la {SemanaTexto()}";
            }

            await Task.Run(
                () => DatabaseHelper.GuardarTallerPuntoMobile(
                    punto,
                    descripcion));

            await CargarAsync();
        }
        catch
        {
        }
    }


    private string SemanaTexto() =>
        DatabaseHelper
            .ObtenerEscuelaClaseMobile(
                _escuelaId)
            ?.SemanaTexto
            .ToLower()
        ?? "semana sin fecha";


    private async Task GuardarEnsayoAsync(
        EscuelaTallerPuntoMobile punto,
        string texto)
    {
        try
        {
            string nuevo =
                (texto ?? "").Trim();

            if (string.Equals(
                    punto.HoraEnsayo,
                    nuevo,
                    StringComparison.Ordinal))
            {
                return;
            }

            punto.HoraEnsayo =
                nuevo;

            await Task.Run(
                () => DatabaseHelper.GuardarTallerPuntoMobile(
                    punto,
                    $"Cambió la hora de ensayo del punto {punto.Numero} (taller {punto.TallerTexto}) a {(nuevo.Length == 0 ? "sin hora" : nuevo)} en la escuela de la {SemanaTexto()}"));
        }
        catch
        {
        }
    }


    private async void BtnPdfTalleres_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var escuela =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerEscuelaClaseMobile(
                            _escuelaId));

            if (escuela is null)
            {
                return;
            }

            var puntos =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerTalleresEscuelaMobile(
                            _escuelaId));

            string ruta =
                System.IO.Path.Combine(
                    FileSystem.CacheDirectory,
                    "Talleres_"
                    + escuela.SemanaInicio
                    + ".pdf");

            await Task.Run(
                () => ProgramaEscuelaPdfService.GenerarTalleres(
                    escuela,
                    puntos,
                    ruta));

            DatabaseHelper.AnotarActividadMobile(
                "Escuelas",
                "Generó el PDF de los talleres de la escuela de la "
                + escuela.SemanaTexto.ToLower());

            await Launcher.Default.OpenAsync(
                new OpenFileRequest(
                    "Talleres de la escuela",
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
}
