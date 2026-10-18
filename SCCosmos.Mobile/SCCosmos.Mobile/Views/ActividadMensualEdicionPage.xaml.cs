using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Editor de una fila de actividad mensual (tabla compartida
/// ActividadPersonas). Funciona tanto para nuevos registros como para
/// editar/eliminar uno existente.
/// </summary>
public partial class ActividadMensualEdicionPage : ContentPage
{
    private static readonly string[] Meses =
    {
        "septiembre",
        "octubre",
        "noviembre",
        "diciembre",
        "enero",
        "febrero",
        "marzo",
        "abril",
        "mayo",
        "junio",
        "julio",
        "agosto"
    };

    private readonly int _personaId;

    private readonly ActividadMensualMobile? _original;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    public ActividadMensualEdicionPage(
        int personaId,
        ActividadMensualMobile? actividad)
    {
        InitializeComponent();

        _personaId =
            personaId;

        _original =
            actividad;

        PickerMes.ItemsSource =
            Meses.ToList();

        if (actividad is not null)
        {
            Title =
                "Editar actividad";

            PickerMes.SelectedItem =
                Meses.FirstOrDefault(m =>
                    string.Equals(
                        m,
                        actividad.Mes,
                        StringComparison.OrdinalIgnoreCase))
                ?? Meses[0];

            PickerMes.IsEnabled =
                false;

            TxtAnio.Text =
                actividad.AnioServicio;

            TxtHoras.Text =
                actividad.Horas;

            TxtCursos.Text =
                actividad.CursosBiblicos;

            TxtNotas.Text =
                actividad.Notas;

            SwParticipacion.IsToggled =
                actividad.Participacion;

            SwPrecAux.IsToggled =
                actividad.PrecursorAuxiliar;

            BtnEliminar.IsVisible =
                true;
        }
        else
        {
            Title =
                "Nuevo mes";

            PickerMes.SelectedIndex =
                0;

            TxtAnio.Text =
                CalcularAnioServicioActual();

            SwParticipacion.IsToggled =
                true;
        }
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private static string CalcularAnioServicioActual()
    {
        DateTime hoy =
            DateTime.Today;

        int anio =
            hoy.Month >= 9
                ? hoy.Year
                : hoy.Year - 1;

        return anio.ToString();
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        string mes =
            PickerMes.SelectedItem?.ToString()
            ?? "";

        string anio =
            TxtAnio.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(mes)
            ||
            string.IsNullOrWhiteSpace(anio))
        {
            await DisplayAlert(
                "Datos incompletos",
                "Indica el mes y el año de servicio.",
                "Aceptar");

            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarActividadMensualEditadaMobile(
                            _personaId,
                            mes,
                            anio,
                            TxtHoras.Text?.Trim() ?? "",
                            SwParticipacion.IsToggled,
                            TxtCursos.Text?.Trim() ?? "",
                            SwPrecAux.IsToggled,
                            TxtNotas.Text?.Trim() ?? ""));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Error al guardar",
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
                "Eliminar registro",
                $"¿Eliminar la actividad de " +
                $"{_original.Mes} {_original.AnioServicio}?",
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
                        .EliminarActividadMensualMobile(
                            _personaId,
                            _original.Mes,
                            _original.AnioServicio));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Error al eliminar",
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
