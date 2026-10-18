using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Arreglo de una persona dentro de una RSC (móvil): con quiénes va
/// (una o varias) y si acepta ir en un grupo más grande.
/// </summary>
public partial class RscArregloPage : ContentPage
{
    private readonly string _reunionId;

    private readonly int _personaId;

    private readonly Dictionary<CheckBox, int> _checks =
        new Dictionary<CheckBox, int>();

    private readonly Dictionary<int, string> _nombres =
        new Dictionary<int, string>();

    private RscPersonaMobile? _persona;

    public RscArregloPage(
        string reunionId,
        int personaId)
    {
        InitializeComponent();

        _reunionId =
            reunionId ?? "";

        _personaId =
            personaId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            var asistentes =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerAsistentesRscMobile(
                            _reunionId));

            _persona =
                asistentes.FirstOrDefault(
                    p => p.PersonaId
                         == _personaId);

            if (_persona == null)
            {
                await Navigation.PopAsync();

                return;
            }

            LblPersona.Text =
                "Arreglo de "
                + _persona.Nombre;

            PanelCon.Clear();
            _checks.Clear();
            _nombres.Clear();

            foreach (var otra in asistentes.Where(
                         p => p.PersonaId
                              != _personaId))
            {
                _nombres[otra.PersonaId] =
                    otra.Nombre;

                var check =
                    new CheckBox
                    {
                        Color = Color.FromArgb(
                            "#4BA3E3"),
                        IsChecked =
                            _persona.ArregloConPersonaIds
                                .Contains(
                                    otra.PersonaId),
                        VerticalOptions =
                            LayoutOptions.Center
                    };

                _checks[check] =
                    otra.PersonaId;

                var fila =
                    new HorizontalStackLayout
                    {
                        Spacing = 10
                    };

                fila.Add(
                    check);

                fila.Add(
                    new Label
                    {
                        Text = otra.Nombre,
                        FontSize = 13,
                        TextColor = Colors.White,
                        VerticalTextAlignment =
                            TextAlignment.Center
                    });

                var borde =
                    new Border
                    {
                        Style = Estilo("TarjetaPlana")
                    };

                borde.Content =
                    fila;

                PanelCon.Add(
                    borde);
            }

            TogAcepta.IsToggled =
                _persona.AceptaGrupo;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo cargar el arreglo:\n"
                + ex.Message,
                "Aceptar");

            await Navigation.PopAsync();
        }
    }


    private static Style Estilo(
        string clave)
    {
        return (Style)Application
            .Current!
            .Resources[clave];
    }


    private async void BtnGuardar_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            var elegidos =
                new List<int>();

            foreach (var par in _checks)
            {
                if (par.Key.IsChecked == true)
                {
                    elegidos.Add(
                        par.Value);
                }
            }

            bool acepta =
                TogAcepta.IsToggled;

            await Task.Run(
                () => DatabaseHelper
                    .EstablecerArregloRscMobile(
                        _reunionId,
                        _personaId,
                        elegidos,
                        acepta));

            if (_persona != null)
            {
                string descripcion;

                if (elegidos.Count == 0)
                {
                    descripcion =
                        acepta
                            ? "dejó sin arreglo a "
                              + _persona.Nombre
                            : "marcó a "
                              + _persona.Nombre
                              + " para predicar solo/a (sin grupo)";
                }
                else
                {
                    var nombres = elegidos.Select(id =>
                        _nombres.TryGetValue(id, out string? n)
                            ? n
                            : "otro hermano");

                    descripcion =
                        "puso a "
                        + _persona.Nombre
                        + " para predicar con "
                        + string.Join(", ", nombres)
                        + (acepta
                            ? ""
                            : " (sin grupo más grande)");
                }

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "En una RSC, "
                    + descripcion);
            }

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo guardar:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnQuitar_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await Task.Run(
                () => DatabaseHelper
                    .EstablecerArregloRscMobile(
                        _reunionId,
                        _personaId,
                        new List<int>(),
                        true));

            if (_persona != null)
            {
                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Quitó el arreglo de "
                    + _persona.Nombre
                    + " en una RSC");
            }

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo quitar el arreglo:\n"
                + ex.Message,
                "Aceptar");
        }
    }
}
