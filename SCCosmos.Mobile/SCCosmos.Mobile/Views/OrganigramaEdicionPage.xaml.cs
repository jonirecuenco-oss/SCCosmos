using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta/edición/borrado de una fila del organigrama de asamblea
/// (tabla compartida ProgramaAsambleasOrganigrama).
/// </summary>
public partial class OrganigramaEdicionPage : ContentPage
{
    private readonly int _asambleaId;

    private readonly OrganigramaAsambleaMobile? _original;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    public OrganigramaEdicionPage(
        int asambleaId,
        OrganigramaAsambleaMobile? fila)
    {
        InitializeComponent();

        _asambleaId =
            asambleaId;

        _original =
            fila;

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
                TxtDepartamento.Text =
                    _original.Departamento;

                TxtRol.Text =
                    _original.Rol;

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
        string departamento =
            TxtDepartamento.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                departamento))
        {
            await DisplayAlert(
                "Datos incompletos",
                "El departamento es obligatorio.",
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
                        .GuardarOrganigramaAsambleaMobile(
                            _original?.Id ?? 0,
                            _asambleaId,
                            departamento,
                            TxtRol.Text?.Trim() ?? "",
                            asignadoId));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Organigrama",
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
                "Eliminar asignación",
                $"¿Eliminar «{_original.Departamento} · " +
                $"{_original.Rol}»?",
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
                        .EliminarOrganigramaAsambleaMobile(
                            _original.Id));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Organigrama",
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
