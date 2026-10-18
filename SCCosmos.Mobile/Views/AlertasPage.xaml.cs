using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Panel de alertas pendientes. Se calcula a partir del calendario y
/// del estado de sincronización, sin acceso a la red.
/// </summary>
public partial class AlertasPage : ContentPage, IActualizableMobile
{
    private bool _cargado;

    private List<AlertaMobile> _alertas =
        new();

    public AlertasPage()
    {
        InitializeComponent();
    }

    private async void BtnLeidas_Clicked(
        object sender,
        EventArgs e)
    {
        AlertasMobileService.MarcarTodasLeidas(
            _alertas);

        await CargarAsync();

        // Sin alertas pendientes: se retiran las notificaciones del sistema.
        _ = AlertasNotificacionService
            .ActualizarAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_cargado)
        {
            return;
        }

        _cargado = true;

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
            List<AlertaMobile> alertas =
                await Task.Run(
                    () =>
                        AlertasMobileService.Calcular());

            _alertas =
                alertas;

            Lista.ItemsSource =
                alertas;

            LblResumen.Text =
                alertas.Count == 0
                    ? "Sin alertas."
                    : $"{alertas.Count} alertas pendientes.";

            // El contador del icono y las notificaciones del sistema se
            // mantienen al día con lo que se acaba de calcular.
            _ = AlertasNotificacionService
                .ActualizarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Alertas",
                "No se pudieron calcular las alertas: " +
                ex.Message,
                "Aceptar");
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
