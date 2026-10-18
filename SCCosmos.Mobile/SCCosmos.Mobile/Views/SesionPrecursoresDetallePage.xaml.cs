using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Detalle de una sesión de precursores con la edición de sus partes
/// (tabla compartida ReunionPrecursoresPartes).
/// </summary>
public partial class SesionPrecursoresDetallePage : ContentPage
{
    private readonly SesionPrecursoresMobile _sesion;

    public SesionPrecursoresDetallePage(
        SesionPrecursoresMobile sesion)
    {
        InitializeComponent();

        _sesion =
            sesion;

        Title =
            $"Sesión {sesion.Numero}";

        LblSesion.Text =
            $"Sesión {sesion.Numero}";

        LblDetalle.Text =
            sesion.Detalle;
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
            List<ParteSesionPrecursoresMobile> partes =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerPartesSesionPrecursoresMobile(
                                _sesion.Id));

            BindableLayout.SetItemsSource(
                ListaPartes,
                partes);

            LblSinPartes.IsVisible =
                partes.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Sesión",
                "No se pudieron cargar las partes: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAnadirParte_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorParteAsync(
            null);
    }

    private async void BtnEditarParte_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                ParteSesionPrecursoresMobile parte)
        {
            await AbrirEditorParteAsync(
                parte);
        }
    }

    private async void BtnEliminarParte_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ParteSesionPrecursoresMobile parte)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar parte",
                $"¿Eliminar «{parte.Titulo}»?",
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
                            parte.Id));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Sesión",
                "No se pudo eliminar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async Task AbrirEditorParteAsync(
        ParteSesionPrecursoresMobile? parte)
    {
        var editor =
            new ParteSesionPrecursoresEdicionPage(
                _sesion.Id,
                parte);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }
}
