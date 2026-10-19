using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta/edición de una congregación (tabla compartida Congregaciones).
/// Réplica de FormularioCongregacion de WPF. El código es inmutable
/// una vez creada la congregación.
/// </summary>
public partial class CongregacionEdicionPage : ContentPage
{
    private readonly string? _codigoOriginal;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    private List<SeccionCircuitoMobile> _secciones =
        new();

    private bool _esNueva;

    public CongregacionEdicionPage(
        Congregacion? congregacion)
    {
        InitializeComponent();

        _esNueva =
            congregacion is null;

        _codigoOriginal =
            congregacion?.Codigo;

        _ = CargarSeccionesAsync();

        if (congregacion is not null)
        {
            Rellenar(
                congregacion);
        }
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async Task CargarSeccionesAsync()
    {
        try
        {
            _secciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerSeccionesCircuitoMobile());

            var lista =
                new List<SeccionCircuitoMobile>
                {
                    new()
                    {
                        Id = 0,
                        Nombre = "(Sin sección)"
                    }
                };

            lista.AddRange(
                _secciones);

            PickerSeccion.ItemsSource =
                lista;

            if (_seccionPendiente >= 0)
            {
                SeleccionarSeccion(
                    _seccionPendiente);

                _seccionPendiente =
                    -1;
            }
        }
        catch
        {
            // La congregación puede guardarse sin sección.
        }
    }

    private void Rellenar(
        Congregacion c)
    {
        TxtCodigo.Text =
            c.Codigo;

        TxtCodigo.IsEnabled =
            false;

        TxtNombre.Text =
            c.Nombre;

        TxtEmail.Text =
            c.Email;

        TxtProvincia.Text =
            c.Provincia;

        TxtMunicipio.Text =
            c.Municipio;

        TxtDireccion.Text =
            c.DireccionSR;

        // Campos nuevos: Zoom y email del AV.
        var extras =
            DatabaseHelper
                .ObtenerDatosExtraCongregacionMobile(
                    c.Codigo);

        TxtIdZoom.Text =
            extras.IdZoom;

        TxtClaveZoom.Text =
            extras.ClaveZoom;

        TxtEmailAV.Text =
            extras.EmailAV;

        TxtDiaEntre.Text =
            c.DiaEntreSemana;

        TxtHoraEntre.Text =
            c.HoraEntreSemana;

        TxtDiaFin.Text =
            c.DiaFinSemana;

        TxtHoraFin.Text =
            c.HoraFinSemana;

        TxtCapacidad.Text =
            c.Capacidad > 0
                ? c.Capacidad.ToString()
                : "";

        TxtPoblacion.Text =
            c.Poblacion > 0
                ? c.Poblacion.ToString()
                : "";

        _seccionPendiente =
            c.SeccionId;

        BtnEliminar.IsVisible =
            true;
    }

    private int _seccionPendiente = -1;

    private int ObtenerSeccionSeleccionada()
    {
        if (PickerSeccion.SelectedItem is
            SeccionCircuitoMobile seccion)
        {
            return seccion.Id;
        }

        return 0;
    }

    private void SeleccionarSeccion(
        int seccionId)
    {
        List<SeccionCircuitoMobile>?
            items =
                PickerSeccion.ItemsSource
                    as List<SeccionCircuitoMobile>;

        if (items is null)
        {
            return;
        }

        PickerSeccion.SelectedItem =
            items.FirstOrDefault(
                s => s.Id == seccionId)
            ?? items[0];
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        string codigo =
            TxtCodigo.Text?.Trim()
            ?? "";

        string nombre =
            TxtNombre.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                codigo)
            ||
            string.IsNullOrWhiteSpace(
                nombre))
        {
            await DisplayAlert(
                "Datos incompletos",
                "El código y el nombre son obligatorios.",
                "Aceptar");

            return;
        }

        if (_esNueva)
        {
            bool existe =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ExisteCodigoCongregacionMobile(
                                codigo));

            if (existe)
            {
                await DisplayAlert(
                    "Código duplicado",
                    $"Ya existe una congregación con el código {codigo}.",
                    "Aceptar");

                return;
            }
        }

        int.TryParse(
            TxtCapacidad.Text?.Trim(),
            out int capacidad);

        int.TryParse(
            TxtPoblacion.Text?.Trim(),
            out int poblacion);

        var cong =
            new Congregacion
            {
                Codigo = codigo,
                Nombre = nombre,
                Email = TxtEmail.Text?.Trim() ?? "",
                SeccionId = ObtenerSeccionSeleccionada(),
                Provincia = TxtProvincia.Text?.Trim() ?? "",
                Municipio = TxtMunicipio.Text?.Trim() ?? "",
                DireccionSR = TxtDireccion.Text?.Trim() ?? "",
                IdZoom = TxtIdZoom.Text?.Trim() ?? "",
                ClaveZoom = TxtClaveZoom.Text?.Trim() ?? "",
                EmailAV = TxtEmailAV.Text?.Trim() ?? "",
                DiaEntreSemana = TxtDiaEntre.Text?.Trim() ?? "",
                HoraEntreSemana = TxtHoraEntre.Text?.Trim() ?? "",
                DiaFinSemana = TxtDiaFin.Text?.Trim() ?? "",
                HoraFinSemana = TxtHoraFin.Text?.Trim() ?? "",
                Capacidad = capacidad,
                Poblacion = poblacion
            };

        try
        {
            await Task.Run(
                () =>
                {
                    if (_esNueva)
                    {
                        DatabaseHelper
                            .InsertarCongregacionMobile(
                                cong);
                    }
                    else
                    {
                        DatabaseHelper
                            .ActualizarCongregacionMobile(
                                cong);
                    }

                    // Campos nuevos (Zoom y AV).
                    DatabaseHelper
                        .GuardarDatosExtraCongregacionMobile(
                            cong.Codigo,
                            cong.IdZoom,
                            cong.ClaveZoom,
                            cong.EmailAV);
                });

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Congregación",
                "No se pudo guardar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _codigoOriginal))
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar congregación",
                $"¿Eliminar la congregación " +
                $"{TxtNombre.Text}?\n\nEsta acción borra la fila " +
                "de la base de datos.",
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
                        .EliminarCongregacionMobile(
                            _codigoOriginal));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Congregación",
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
