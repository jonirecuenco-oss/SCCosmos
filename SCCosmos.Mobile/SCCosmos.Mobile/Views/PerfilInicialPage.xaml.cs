using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Selección del perfil (superintendente o esposa) en el primer arranque.
/// Solo el superintendente con el código correcto obtiene acceso completo.
/// </summary>
public partial class PerfilInicialPage : ContentPage
{
    public PerfilInicialPage()
    {
        InitializeComponent();
    }

    private void BtnHombre_Clicked(
        object sender,
        EventArgs e)
    {
        PerfilAccesoService.EstablecerRol(
            PerfilAccesoService.PefilHombre);

        BtnHombre.IsVisible = false;
        BtnEsposa.IsVisible = false;

        PanelCodigo.IsVisible = true;

        LblAviso.Text =
            "Introduce el código para acceder a todas las funciones. " +
            "Sin él, solo tendrás Inicio, Calendario, Personas y " +
            "Configuración.";
    }

    private async void BtnEsposa_Clicked(
        object sender,
        EventArgs e)
    {
        PerfilAccesoService.EstablecerRol(
            PerfilAccesoService.PerfilEsposa);

        await FinalizarYVolverAsync();
    }

    private async void BtnContinuar_Clicked(
        object sender,
        EventArgs e)
    {
        bool ok =
            PerfilAccesoService.ValidarCodigo(
                TxtCodigo.Text ?? "");

        if (!ok)
        {
            LblAviso.Text =
                "Código incorrecto. Puedes intentarlo de nuevo o " +
                "continuar con acceso limitado.";

            return;
        }

        await FinalizarYVolverAsync();
    }

    private async void BtnSinCodigo_Clicked(
        object sender,
        EventArgs e)
    {
        PerfilAccesoService.ValidarCodigo(
            "");

        await FinalizarYVolverAsync();
    }

    private async Task FinalizarYVolverAsync()
    {
        try
        {
            (Shell.Current as AppShell)
                ?.AplicarPerfilAcceso();
        }
        catch
        {
        }

        // La selección de perfil es la única que se mantiene como ventana
        // modal (es una barrera de arranque que no debe poder saltarse con
        // el botón Atrás), así que se cierra como modal.
        await Navigation.PopModalAsync();
    }
}
