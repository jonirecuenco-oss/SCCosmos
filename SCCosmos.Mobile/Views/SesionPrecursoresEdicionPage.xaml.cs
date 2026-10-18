using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta/edición/borrado de una sesión de la reunión de precursores
/// (tabla compartida ReunionPrecursoresSesiones).
/// </summary>
public partial class SesionPrecursoresEdicionPage : ContentPage
{
    private readonly int _reunionId;

    private readonly SesionPrecursoresMobile? _original;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    public SesionPrecursoresEdicionPage(
        int reunionId,
        SesionPrecursoresMobile? sesion)
    {
        InitializeComponent();

        _reunionId =
            reunionId;

        _original =
            sesion;

        if (sesion is not null)
        {
            TxtNumero.Text =
                sesion.Numero;

            TxtFecha.Text =
                sesion.Fecha;

            TxtHora.Text =
                sesion.Hora;

            TxtCongregaciones.Text =
                sesion.Congregaciones;

            TxtLugar.Text =
                sesion.Lugar;

            BtnEliminar.IsVisible =
                true;
        }
        else
        {
            TxtNumero.Text =
                "1";
        }
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        string fecha =
            TxtFecha.Text?.Trim()
            ?? "";

        string hora =
            TxtHora.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                fecha)
            &&
            string.IsNullOrWhiteSpace(
                hora))
        {
            await DisplayAlert(
                "Datos incompletos",
                "Indica al menos la fecha o la hora de la sesión.",
                "Aceptar");

            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarSesionPrecursoresMobile(
                            _original?.Id ?? 0,
                            _reunionId,
                            TxtNumero.Text?.Trim() ?? "",
                            fecha,
                            hora,
                            TxtCongregaciones.Text?.Trim() ?? "",
                            TxtLugar.Text?.Trim() ?? ""));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Sesión",
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
                "Eliminar sesión",
                $"¿Eliminar la sesión {_original.Numero}?",
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
                        .EliminarSesionPrecursoresMobile(
                            _original.Id));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
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
