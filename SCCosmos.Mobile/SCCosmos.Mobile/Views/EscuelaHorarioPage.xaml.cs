using Microsoft.Maui.Controls.Shapes;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Horario de la escuela: las horas de clase son fijas y se asignan
/// las tres oraciones de cada día (inicial, de la mañana y final),
/// con PDF.
/// </summary>
public partial class EscuelaHorarioPage : ContentPage, IActualizableMobile
{
    private readonly string _escuelaId;

    private List<ItemPersonaEscuelaMobile> _personas;

    public EscuelaHorarioPage(
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

                        List<EscuelaHorarioDiaMobile> dias =
                            DatabaseHelper
                                .ObtenerHorarioEscuelaMobile(
                                    _escuelaId);

                        return (escuela, dias);
                    });

            if (resultado.escuela is null)
            {
                await Navigation.PopAsync();

                return;
            }

            LblSubtitulo.Text =
                "Horario de la "
                + resultado.escuela.SemanaTexto.ToLower()
                + " · las horas de clase son fijas; asigna las oraciones";

            ConstruirPanel(
                resultado.dias);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Horario",
                "No se pudo cargar el horario:\n" + ex.Message,
                "Aceptar");
        }
    }


    private void ConstruirPanel(
        List<EscuelaHorarioDiaMobile> dias)
    {
        try
        {
            PanelHorario.Children.Clear();

            foreach (EscuelaHorarioDiaMobile dia in dias)
            {
                var contenido =
                    new VerticalStackLayout
                    {
                        Spacing = 6
                    };

                contenido.Children.Add(
                    new Label
                    {
                        Text = dia.Dia.ToUpperInvariant(),
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#4BA3E3")
                    });

                contenido.Children.Add(
                    FilaCancionYOracion(
                        DatabaseHelper.CancionInicialDe(dia.Dia),
                        "Oración inicial",
                        dia.OracionInicialTexto,
                        dia,
                        "OracionInicial"));

                var lecciones =
                    DatabaseHelper.LeccionesDe(
                        dia.Dia);

                for (int i = 0; i < lecciones.Count; i++)
                {
                    contenido.Children.Add(
                        new Label
                        {
                            Text = $"{lecciones[i].Nombre}  ({lecciones[i].Horario})",
                            FontSize = 12.5,
                            TextColor = Color.FromArgb("#D0D5DD"),
                            Margin = new Thickness(16, 0, 0, 0)
                        });

                    if (i == 1)
                    {
                        contenido.Children.Add(
                            Pausa("Pausa 15 minutos"));
                    }

                    if (i == 2)
                    {
                        contenido.Children.Add(
                            FilaCancionYOracion(
                                "",
                                "Oración de la mañana",
                                dia.OracionIntermediaTexto,
                                dia,
                                "OracionIntermedia"));

                        contenido.Children.Add(
                            Pausa("Pausa 60 minutos"));
                    }

                    if (i == 3)
                    {
                        contenido.Children.Add(
                            Pausa(
                                dia.Dia == "Sábado"
                                    ? "Pausa 10 minutos"
                                    : "Pausa 15 minutos"));
                    }
                }

                contenido.Children.Add(
                    FilaCancionYOracion(
                        DatabaseHelper.CancionFinalDe(dia.Dia),
                        "Oración final",
                        dia.OracionFinalTexto,
                        dia,
                        "OracionFinal"));

                PanelHorario.Children.Add(
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#161A20"),
                        StrokeThickness = 0,
                        Padding = new Thickness(14, 12),
                        StrokeShape = new RoundRectangle
                        {
                            CornerRadius = new CornerRadius(12)
                        },
                        Content = contenido
                    });
            }
        }
        catch
        {
        }
    }


    private static Label Pausa(
        string texto) =>
        new()
        {
            Text = texto,
            FontSize = 11,
            FontAttributes = FontAttributes.Italic,
            TextColor = Color.FromArgb("#9BA3AE"),
            Margin = new Thickness(16, 0, 0, 0)
        };


    private HorizontalStackLayout FilaCancionYOracion(
        string cancion,
        string etiquetaOracion,
        string nombreOracion,
        EscuelaHorarioDiaMobile dia,
        string campo)
    {
        var fila =
            new HorizontalStackLayout
            {
                Spacing = 8
            };

        fila.Children.Add(
            new Label
            {
                Text = cancion ?? "",
                FontSize = 12.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                WidthRequest = 90,
                VerticalOptions = LayoutOptions.Center
            });

        var boton =
            new Button
            {
                Text = $"{etiquetaOracion}: {nombreOracion}",
                FontSize = 11.5,
                Padding = new Thickness(10, 5),
                BackgroundColor = Color.FromArgb("#1E242C"),
                TextColor = Color.FromArgb("#B9DEFF"),
                CornerRadius = 8
            };

        boton.Clicked +=
            async (s, e) => await ElegirOracionAsync(
                dia,
                campo,
                etiquetaOracion);

        fila.Children.Add(
            boton);

        return fila;
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


    private async Task ElegirOracionAsync(
        EscuelaHorarioDiaMobile dia,
        string campo,
        string etiqueta)
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
                    $"{etiqueta} del {dia.Dia}",
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

            string etiquetaLarga =
                campo switch
                {
                    "OracionInicial" => "oración inicial",
                    "OracionIntermedia" => "oración de la mañana",
                    _ => "oración final"
                };

            if (campo == "OracionInicial")
            {
                dia.OracionInicialId = personaId;
                dia.OracionInicialNombre = nombre;
            }
            else if (campo == "OracionIntermedia")
            {
                dia.OracionIntermediaId = personaId;
                dia.OracionIntermediaNombre = nombre;
            }
            else
            {
                dia.OracionFinalId = personaId;
                dia.OracionFinalNombre = nombre;
            }

            string descripcion =
                personaId == 0
                    ? $"Dejó sin asignar la {etiquetaLarga} del {dia.Dia.ToLower()} en la escuela de la {SemanaTexto()}"
                    : $"Asignó a {nombre} para la {etiquetaLarga} del {dia.Dia.ToLower()} en la escuela de la {SemanaTexto()}";

            await Task.Run(
                () => DatabaseHelper.GuardarHorarioDiaMobile(
                    dia,
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


    private async void BtnPdfHorario_Clicked(
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

            var dias =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerHorarioEscuelaMobile(
                            _escuelaId));

            string ruta =
                System.IO.Path.Combine(
                    FileSystem.CacheDirectory,
                    "Horario_"
                    + escuela.SemanaInicio
                    + ".pdf");

            await Task.Run(
                () => ProgramaEscuelaPdfService.GenerarHorario(
                    escuela,
                    dias,
                    ruta));

            DatabaseHelper.AnotarActividadMobile(
                "Escuelas",
                "Generó el PDF del horario de la escuela de la "
                + escuela.SemanaTexto.ToLower());

            await Launcher.Default.OpenAsync(
                new OpenFileRequest(
                    "Horario de la escuela",
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
