using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Módulo Circuito: listado y edición de congregaciones y secciones.
/// Réplica funcional de CircuitoPage de WPF (sin importación KMZ, que
/// se documenta como pendiente por depender del flujo de escritorio).
/// </summary>
public partial class CircuitoPage : ContentPage, IActualizableMobile
{
    public CircuitoPage()
    {
        InitializeComponent();
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
            List<Congregacion> congregaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCongregacionesCompletasMobile());

            Lista.ItemsSource =
                congregaciones;

            LblTotal.Text =
                $"{congregaciones.Count} congregaciones";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Circuito",
                "No se pudo cargar el circuito: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void Congregacion_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
            Congregacion congregacion)
        {
            return;
        }

        var editor =
            new CongregacionEdicionPage(
                congregacion);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        var editor =
            new CongregacionEdicionPage(
                null);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }

    private async void BtnSecciones_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(new SeccionesCircuitoPage());
    }

    private async void Refrescar_Refreshing(
        object sender,
        EventArgs e)
    {
        try
        {
            await CargarAsync();
        }
        finally
        {
            Refrescar.IsRefreshing =
                false;
        }
    }
}
