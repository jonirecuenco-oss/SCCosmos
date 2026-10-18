using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Reuniones de precursores con sus sesiones (solo lectura).
/// </summary>
public partial class PrecursoresPage : ContentPage, IActualizableMobile
{
    public PrecursoresPage()
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
            List<ReunionPrecursoresMobile> reuniones =
                await Task.Run(
                    () =>
                    {
                        List<ReunionPrecursoresMobile> lista =
                            DatabaseHelper
                                .ObtenerReunionesPrecursoresMobile();

                        foreach (ReunionPrecursoresMobile reunion
                                 in lista)
                        {
                            reunion.Sesiones =
                                DatabaseHelper
                                    .ObtenerSesionesPrecursoresMobile(
                                        reunion.Id);
                        }

                        return lista;
                    });

            Lista.ItemsSource =
                reuniones;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Precursores",
                "No se pudieron cargar las reuniones: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAnadirSesion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                ReunionPrecursoresMobile reunion)
        {
            await AbrirEditorSesionAsync(
                reunion.Id,
                null);
        }
    }

    private async void BtnEditarSesion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                SesionPrecursoresMobile sesion)
        {
            await AbrirEditorSesionAsync(
                sesion.ReunionId,
                sesion);
        }
    }

    private async void BtnPartesSesion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                SesionPrecursoresMobile sesion)
        {
            await Navigation.PushAsync(
                new SesionPrecursoresDetallePage(
                    sesion));
        }
    }

    private async Task AbrirEditorSesionAsync(
        int reunionId,
        SesionPrecursoresMobile? sesion)
    {
        var editor =
            new SesionPrecursoresEdicionPage(
                reunionId,
                sesion);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
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
