using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Edición de un territorio: vincularlo a un publicador o eliminar el
/// registro. Réplica de las operaciones de WPF
/// (ActualizarVinculacionTerritorio / EliminarRegistroTerritorio).
/// </summary>
public partial class TerritorioEdicionPage : ContentPage
{
    private readonly VistaTerritorioMobile _territorio;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    public TerritorioEdicionPage(
        VistaTerritorioMobile territorio,
        string congregacionCodigo)
    {
        InitializeComponent();

        _territorio =
            territorio;

        LblNombre.Text =
            territorio.Nombre;

        LblEstado.Text =
            CrearEstado(territorio);

        TxtVinculado.Text =
            territorio.NombreVinculado;

        _ = CargarPersonasAsync(
            congregacionCodigo);
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async Task CargarPersonasAsync(
        string congregacionCodigo)
    {
        if (string.IsNullOrWhiteSpace(
                congregacionCodigo))
        {
            return;
        }

        try
        {
            List<Persona> personas =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerPersonasPorCongregacion(
                                congregacionCodigo));

            var lista =
                personas
                    .OrderBy(p =>
                        p.NombreMostrarTabla)
                    .ToList();

            lista.Insert(
                0,
                new Persona
                {
                    Id = 0,
                    Nombre = "(Sin vincular)",
                    NombreComun = "(Sin vincular)"
                });

            PickerPersona.ItemsSource =
                lista;
        }
        catch
        {
            // La vinculación por texto sigue disponible aunque falle
            // la carga de publicadores.
        }
    }

    private void PickerPersona_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (PickerPersona.SelectedItem is
            Persona persona)
        {
            TxtVinculado.Text =
                persona.Id == 0
                    ? ""
                    : persona.NombreMostrarTabla;
        }
    }

    private static string CrearEstado(
        VistaTerritorioMobile t)
    {
        if (!string.IsNullOrWhiteSpace(
                t.FechaCompletado))
        {
            return "Completado: " + t.FechaCompletado;
        }

        if (!string.IsNullOrWhiteSpace(
                t.AsignadoA)
            ||
            !string.IsNullOrWhiteSpace(
                t.NombreVinculado))
        {
            string persona =
                !string.IsNullOrWhiteSpace(
                    t.NombreVinculado)
                    ? t.NombreVinculado
                    : t.AsignadoA;

            return "Asignado a " +
                   persona +
                   (string.IsNullOrWhiteSpace(
                        t.FechaAsignado)
                       ? ""
                       : " · " + t.FechaAsignado);
        }

        return "Disponible";
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            string valor =
                TxtVinculado.Text?.Trim()
                ?? "";

            await Task.Run(
                () =>
                    DatabaseHelper
                        .ActualizarVinculacionTerritorioMobile(
                            _territorio.Id,
                            valor));

            _territorio.NombreVinculado =
                valor;

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Territorio",
                "No se pudo guardar la vinculación: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        bool eliminar =
            await DisplayAlert(
                "Eliminar territorio",
                $"¿Eliminar el registro «{_territorio.Nombre}»? " +
                "Esta acción borra la fila de la base de datos.",
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
                        .EliminarRegistroTerritorioMobile(
                            _territorio.Id));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Territorio",
                "No se pudo eliminar el registro: " +
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
