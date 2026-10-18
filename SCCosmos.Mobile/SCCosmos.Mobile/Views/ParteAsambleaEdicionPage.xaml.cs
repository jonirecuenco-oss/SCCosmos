using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta/edición/borrado de una parte del programa de asamblea
/// (tabla compartida ProgramaAsambleasPartes).
/// </summary>
public partial class ParteAsambleaEdicionPage : ContentPage
{
    private static readonly string[] Turnos =
    {
        "Mañana",
        "Tarde",
        "Noche"
    };

    private readonly int _asambleaId;

    private readonly ParteAsambleaMobile? _original;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    private readonly List<Persona> _personas =
        new();

    public ParteAsambleaEdicionPage(
        int asambleaId,
        ParteAsambleaMobile? parte)
    {
        InitializeComponent();

        _asambleaId =
            asambleaId;

        _original =
            parte;

        PickerTurno.ItemsSource =
            Turnos.ToList();

        _ = CargarAsync();
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async Task CargarAsync()
    {
        try
        {
            _personas.Clear();

            _personas.AddRange(
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerPersonas()));

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
                _personas);

            PickerAsignado.ItemsSource =
                candidatos;

            if (_original is not null)
            {
                PickerTurno.SelectedItem =
                    Turnos.Contains(
                        _original.Turno)
                        ? _original.Turno
                        : Turnos[0];

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
                PickerTurno.SelectedIndex =
                    0;

                PickerAsignado.SelectedIndex =
                    0;
            }
        }
        catch
        {
            // Se puede guardar aunque falle la carga de personas.
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

        string turno =
            PickerTurno.SelectedItem?.ToString()
            ?? "Mañana";

        int asignadoId =
            (PickerAsignado.SelectedItem
             as Persona)?.Id
            ?? 0;

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarParteAsambleaMobile(
                            _original?.Id ?? 0,
                            _asambleaId,
                            turno,
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
                        .EliminarParteAsambleaMobile(
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
