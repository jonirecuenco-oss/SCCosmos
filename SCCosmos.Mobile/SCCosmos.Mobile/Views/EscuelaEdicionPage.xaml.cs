using Microsoft.Maui.Controls.Shapes;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta y edición de una clase (escuela de una semana, de lunes a
/// sábado): instructor, sustituto, salón del reino y notas.
/// </summary>
public partial class EscuelaEdicionPage : ContentPage
{
    private readonly EscuelaClaseMobile? _editando;

    private List<ItemPersonaEscuelaMobile> _instructores =
        new();

    private ItemPersonaEscuelaMobile? _instructorElegido;

    private ItemPersonaEscuelaMobile? _sustitutoElegido;

    public EscuelaEdicionPage(
        EscuelaClaseMobile? escuela)
    {
        InitializeComponent();

        _editando =
            escuela;

        LblTituloForm.Text =
            escuela is null
                ? "NUEVA ESCUELA"
                : "EDITAR ESCUELA";

        _ = CargarAsync();
    }


    private static DateTime LunesDe(
        DateTime fecha) =>
        fecha.Date.AddDays(
            -(((int)fecha.DayOfWeek + 6) % 7));


    private void ActualizarEtiquetaSemana()
    {
        DateTime lunes =
            LunesDe(
                FechaSemana.Date
                ?? DateTime.Today);

        LblSemana.Text =
            EscuelaClaseMobile.FormatearSemana(
                lunes.ToString("yyyy-MM-dd"))
            + " (lunes a sábado)";

        ActualizarTira(
            lunes);
    }


    /// <summary>
    /// Pinta la semana completa (lunes a sábado) resaltada en azul.
    /// </summary>
    private void ActualizarTira(
        DateTime lunes)
    {
        try
        {
            TiraDias.Children.Clear();

            string[] iniciales =
            {
                "LUN",
                "MAR",
                "MIÉ",
                "JUE",
                "VIE",
                "SÁB"
            };

            for (int i = 0; i < 6; i++)
            {
                DateTime dia =
                    lunes.AddDays(i);

                var contenido =
                    new VerticalStackLayout
                    {
                        Spacing = 0
                    };

                contenido.Children.Add(
                    new Label
                    {
                        Text = iniciales[i],
                        FontSize = 8.5,
                        TextColor = Color.FromArgb("#CFE3F5"),
                        HorizontalTextAlignment = TextAlignment.Center
                    });

                contenido.Children.Add(
                    new Label
                    {
                        Text = dia.Day.ToString(),
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    });

                TiraDias.Children.Add(
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#2F6FA8"),
                        StrokeThickness = 0,
                        Padding = new Thickness(8, 4),
                        StrokeShape = new RoundRectangle
                        {
                            CornerRadius = new CornerRadius(8)
                        },
                        Content = contenido
                    });
            }
        }
        catch
        {
        }
    }


    private void BtnSemanaAnterior_Clicked(
        object sender,
        EventArgs e)
    {
        FechaSemana.Date =
            (FechaSemana.Date
             ?? DateTime.Today).AddDays(-7);
    }


    private void BtnSemanaSiguiente_Clicked(
        object sender,
        EventArgs e)
    {
        FechaSemana.Date =
            (FechaSemana.Date
             ?? DateTime.Today).AddDays(7);
    }


    private void FechaSemana_DateSelected(
        object sender,
        DateChangedEventArgs e)
    {
        ActualizarEtiquetaSemana();
    }


    private async Task CargarAsync()
    {
        try
        {
            var resultado =
                await Task.Run(
                    () =>
                    {
                        List<Persona> instructores =
                            DatabaseHelper
                                .ObtenerInstructoresEscuelaMobile();

                        List<Congregacion> congregaciones =
                            DatabaseHelper
                                .ObtenerCongregaciones();

                        return (instructores, congregaciones);
                    });

            _instructores =
                resultado.instructores
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
                                Persona =
                                    p,
                                NombreSolo =
                                    nombre,
                                Visual =
                                    string.IsNullOrWhiteSpace(
                                        congregacion)
                                        ? nombre
                                        : $"{nombre} ({congregacion})"
                            };
                        })
                    .ToList();

            CmbCongregacion.ItemsSource =
                resultado.congregaciones
                    .OrderBy(
                        c => c.Nombre)
                    .Select(
                        c => c.Nombre)
                    .ToList();

            // Valores de la escuela que se edita (o iniciales).
            DateTime fecha =
                DateTime.Today;

            if (_editando is not null
                && DateTime.TryParse(
                    _editando.SemanaInicio,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime parseada))
            {
                fecha =
                    parseada;
            }

            FechaSemana.Date =
                fecha;

            if (_editando is not null)
            {
                _instructorElegido =
                    _instructores.FirstOrDefault(
                        i => i.Persona.Id == _editando.InstructorId);

                if (_editando.SustitutoId > 0)
                {
                    _sustitutoElegido =
                        _instructores.FirstOrDefault(
                            i => i.Persona.Id == _editando.SustitutoId);
                }

                ActualizarEtiquetasEleccion();

                try
                {
                    int indice =
                        ((List<string>)CmbCongregacion.ItemsSource)
                            .IndexOf(
                                _editando.Congregacion);

                    if (indice >= 0)
                    {
                        CmbCongregacion.SelectedIndex =
                            indice;
                    }
                }
                catch
                {
                }

                TxtNotas.Text =
                    _editando.Notas;
            }

            ActualizarEtiquetaSemana();
        }
        catch
        {
        }
    }


    /// <summary>
    /// Muestra la elección actual bajo cada botón.
    /// </summary>
    private void ActualizarEtiquetasEleccion()
    {
        LblInstructorElegido.Text =
            string.IsNullOrWhiteSpace(
                _instructorElegido?.Visual)
                ? "Sin instructor"
                : _instructorElegido!.Visual;

        LblSustitutoElegido.Text =
            _sustitutoElegido?.Persona is null
                ? "(sin sustituto)"
                : _sustitutoElegido.Visual;
    }


    private async void BtnElegirInstructor_Clicked(
        object sender,
        EventArgs e)
    {
        var pagina =
            new EscuelaInstructorPickerPage(
                "Elegir instructor",
                _instructores);

        await Navigation.PushAsync(
            pagina);

        var elegido =
            await pagina.EsperarEleccionAsync();

        if (elegido is not null)
        {
            _instructorElegido =
                elegido;

            ActualizarEtiquetasEleccion();
        }
    }


    private async void BtnElegirSustituto_Clicked(
        object sender,
        EventArgs e)
    {
        var opciones =
            new List<ItemPersonaEscuelaMobile>
            {
                new()
                {
                    Persona = null,
                    Visual = "(sin sustituto)"
                }
            };

        opciones.AddRange(
            _instructores);

        var pagina =
            new EscuelaInstructorPickerPage(
                "Elegir instructor sustituto",
                opciones);

        await Navigation.PushAsync(
            pagina);

        var elegido =
            await pagina.EsperarEleccionAsync();

        if (elegido is not null)
        {
            _sustitutoElegido =
                elegido;

            ActualizarEtiquetasEleccion();
        }
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }


    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            if (_instructorElegido?.Persona is null)
            {
                await DisplayAlert(
                    "Falta el instructor",
                    "Elige el instructor de la escuela.",
                    "Aceptar");

                return;
            }

            ItemPersonaEscuelaMobile instructor =
                _instructorElegido;

            ItemPersonaEscuelaMobile? sustituto =
                _sustitutoElegido;

            string congregacion =
                (TxtOtraCongregacion.Text ?? "").Trim();

            if (congregacion.Length == 0)
            {
                congregacion =
                    CmbCongregacion.SelectedItem as string ?? "";
            }

            // Las butacas se ajustan desde la vista de asientos.
            int asientos =
                _editando != null && _editando.Asientos > 0
                    ? _editando.Asientos
                    : 12;

            var escuela =
                new EscuelaClaseMobile
                {
                    Id =
                        _editando?.Id ?? "",
                    FechaCreacion =
                        _editando?.FechaCreacion ?? "",
                    SemanaInicio =
                        LunesDe(
                            FechaSemana.Date
                            ?? DateTime.Today)
                            .ToString("yyyy-MM-dd"),
                    Congregacion =
                        congregacion,
                    InstructorId =
                        instructor.Persona.Id,
                    InstructorNombre =
                        string.IsNullOrWhiteSpace(
                            instructor.NombreSolo)
                            ? instructor.Visual
                            : instructor.NombreSolo,
                    SustitutoId =
                        sustituto?.Persona?.Id ?? 0,
                    SustitutoNombre =
                        sustituto?.Persona is null
                            ? ""
                            : (string.IsNullOrWhiteSpace(
                                   sustituto.NombreSolo)
                                   ? sustituto.Visual
                                   : sustituto.NombreSolo),
                    Asientos =
                        asientos,
                    Notas =
                        (TxtNotas.Text ?? "").Trim()
                };

            await Task.Run(
                () => DatabaseHelper
                    .GuardarEscuelaClaseMobile(
                        escuela));

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Guardar escuela",
                "No se pudo guardar:\n" + ex.Message,
                "Aceptar");
        }
    }
}


/// <summary>
/// Instructor del selector (persona + texto visible).
/// </summary>
public sealed class ItemPersonaEscuelaMobile
{
    public Persona? Persona { get; set; }

    public string NombreSolo { get; set; } = "";

    public string Visual { get; set; } = "";
}
