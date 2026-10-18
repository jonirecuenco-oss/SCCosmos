using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta/edición/borrado de una parte de sesión de precursores
/// (tabla compartida ReunionPrecursoresPartes).
/// </summary>
public partial class ParteSesionPrecursoresEdicionPage : ContentPage
{
    private readonly int _sesionId;

    private readonly ParteSesionPrecursoresMobile? _original;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    public ParteSesionPrecursoresEdicionPage(
        int sesionId,
        ParteSesionPrecursoresMobile? parte)
    {
        InitializeComponent();

        _sesionId =
            sesionId;

        _original =
            parte;

        _ = CargarAsync();
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async Task CargarAsync()
    {
        try
        {
            List<Persona> personas =
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerPersonas());

            var candidatos =
                new List<Persona>
                {
                    new()
                    {
                        Id = 0,
                        Nombre = "(Sin asignar)",
                        NombreComun = "(Sin asignar)"
                    }
                };

            candidatos.AddRange(
                personas);

            PickerAsignado.ItemsSource =
                candidatos;

            if (_original is not null)
            {
                TxtHora.Text =
                    _original.Hora;

                TxtTitulo.Text =
                    _original.Titulo;

                TxtParticipantes.Text =
                    _original.Participantes;

                TxtTipo.Text =
                    _original.Tipo;

                PickerAsignado.SelectedItem =
                    candidatos.FirstOrDefault(
                        p => p.Id == _original.AsignadoId)
                    ?? candidatos[0];

                BtnEliminar.IsVisible =
                    true;
            }
            else
            {
                PickerAsignado.SelectedIndex =
                    0;
            }
        }
        catch
        {
            // Se permite guardar aunque fallen las personas.
        }
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        string titulo =
            TxtTitulo.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                titulo))
        {
            await DisplayAlert(
                "Datos incompletos",
                "El título de la parte es obligatorio.",
                "Aceptar");

            return;
        }

        int asignadoId =
            (PickerAsignado.SelectedItem
             as Persona)?.Id
            ?? 0;

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarParteSesionPrecursoresMobile(
                            _original?.Id ?? 0,
                            _sesionId,
                            TxtHora.Text?.Trim() ?? "",
                            titulo,
                            asignadoId,
                            TxtParticipantes.Text?.Trim() ?? "",
                            TxtTipo.Text?.Trim() ?? ""));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Parte",
                "No se pudo guardar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_original is null)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar parte",
                $"¿Eliminar «{_original.Titulo}»?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .EliminarParteSesionPrecursoresMobile(
                            _original.Id));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Parte",
                "No se pudo eliminar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        _resultado.TrySetResult(
            false);

        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _resultado.TrySetResult(
            false);

        return base.OnBackButtonPressed();
    }
}
