using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Mover a una persona de grupo dentro de una RSC (móvil).
/// </summary>
public partial class RscMoverPage : ContentPage
{
    private readonly string _reunionId;

    private readonly int _personaId;

    private readonly Dictionary<RadioButton, int> _radios =
        new Dictionary<RadioButton, int>();

    private string _nombrePersona = "";

    public RscMoverPage(
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
            var resultado =
                await Task.Run(
                    () =>
                    {
                        var grupos =
                            DatabaseHelper
                                .ObtenerGruposRscMobile(
                                    _reunionId);

                        var asistentes =
                            DatabaseHelper
                                .ObtenerAsistentesRscMobile(
                                    _reunionId);

                        return (grupos, asistentes);
                    });

            var (grupos, asistentes) =
                resultado;

            var persona =
                asistentes.FirstOrDefault(
                    p => p.PersonaId
                         == _personaId);

            if (persona == null)
            {
                await Navigation.PopAsync();

                return;
            }

            _nombrePersona =
                persona.Nombre;

            LblPersona.Text =
                "Mover a "
                + persona.Nombre;

            int numeroActual =
                grupos
                    .FirstOrDefault(
                        g => g.Miembros.Any(
                            m => m.PersonaId
                                 == _personaId))
                    ?.Numero
                ?? 0;

            PanelRadios.Clear();
            _radios.Clear();

            foreach (var grupo in grupos)
            {
                var radio =
                    new RadioButton
                    {
                        Content =
                            "Grupo "
                            + grupo.Numero
                            + " · "
                            + grupo.Miembros.Count
                            + (grupo.Miembros.Count == 1
                                ? " persona"
                                : " personas"),
                        GroupName = "MoverRsc",
                        TextColor = Colors.White,
                        IsChecked =
                            grupo.Numero == numeroActual
                    };

                _radios[radio] =
                    grupo.Numero;

                PanelRadios.Add(
                    radio);
            }

            var sinGrupo =
                new RadioButton
                {
                    Content = "Sin grupo",
                    GroupName = "MoverRsc",
                    TextColor = Colors.White,
                    IsChecked = numeroActual == 0
                };

            _radios[sinGrupo] =
                0;

            PanelRadios.Add(
                sinGrupo);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo cargar el movimiento:\n"
                + ex.Message,
                "Aceptar");

            await Navigation.PopAsync();
        }
    }


    private async void BtnMover_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            int destino = -1;

            foreach (var par in _radios)
            {
                if (par.Key.IsChecked)
                {
                    destino = par.Value;
                    break;
                }
            }

            if (destino < 0)
            {
                await DisplayAlert(
                    "RSC",
                    "Elige un grupo.",
                    "Aceptar");

                return;
            }

            await Task.Run(
                () => DatabaseHelper
                    .MoverPersonaRscMobile(
                        _reunionId,
                        _personaId,
                        destino));

            DatabaseHelper.AnotarActividadMobile(
                "RSC",
                "Movió a "
                + _nombrePersona
                + (destino == 0
                    ? " a sin grupo"
                    : " al Grupo " + destino)
                + " en una RSC");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo mover:\n"
                + ex.Message,
                "Aceptar");
        }
    }
}
