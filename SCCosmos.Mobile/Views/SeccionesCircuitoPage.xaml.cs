using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Gestión de secciones del circuito (tabla compartida
/// SeccionesCircuito), igual que la pestaña de secciones de WPF.
/// </summary>
public partial class SeccionesCircuitoPage : ContentPage
{
    public SeccionesCircuitoPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            List<SeccionCircuitoMobile> secciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerSeccionesCircuitoMobile());

            Lista.ItemsSource =
                secciones;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Secciones",
                "No se pudieron cargar las secciones: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        string nombre =
            TxtNueva.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                nombre))
        {
            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .InsertarSeccionCircuitoMobile(
                            nombre));

            TxtNueva.Text =
                "";

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Secciones",
                "No se pudo añadir la sección: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionCircuitoMobile seccion)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar sección",
                $"¿Eliminar la sección «{seccion.Nombre}»?",
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
                        .EliminarSeccionCircuitoMobile(
                            seccion.Id));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Secciones",
                "No se pudo eliminar la sección: " +
                ex.Message,
                "Aceptar");
        }
    }
}
