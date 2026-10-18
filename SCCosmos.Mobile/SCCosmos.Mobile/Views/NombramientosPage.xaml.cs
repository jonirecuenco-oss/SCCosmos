using SCCosmos.Mobile.Data;

#if ANDROID
using SCCosmos.Mobile.Platforms.Android;
#endif

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Lista de recomendaciones de nombramientos (tabla compartida con WPF).
/// Es de solo lectura: la aprobación y cancelación reales dependen de la
/// automatización de JW Hub del escritorio, por lo que no se ofrecen aquí.
/// </summary>
public partial class NombramientosPage : ContentPage, IActualizableMobile
{
    private bool _cargado;

    public NombramientosPage()
    {
        InitializeComponent();
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
            List<RecomendacionNombramientoMobile>
                items =
                    await Task.Run(
                        () =>
                            DatabaseHelper
                                .ObtenerRecomendacionesNombramientosMobile());

            Lista.ItemsSource =
                items;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Nombramientos",
                "No se pudieron cargar las recomendaciones:\n" +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAprobar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                RecomendacionNombramientoMobile rec)
        {
            await ProcesarAsync(
                rec,
                aprobar: true);
        }
    }

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                RecomendacionNombramientoMobile rec)
        {
            await ProcesarAsync(
                rec,
                aprobar: false);
        }
    }

    private async Task ProcesarAsync(
        RecomendacionNombramientoMobile rec,
        bool aprobar)
    {
#if ANDROID
        if (string.IsNullOrWhiteSpace(
                rec.UrlDetalle))
        {
            await DisplayAlert(
                "Nombramientos",
                "Esta recomendación no incluye la URL de detalle de " +
                "JW Hub. Vuelve a sintonizar para actualizarla.",
                "Aceptar");

            return;
        }

        bool confirmar =
            await DisplayAlert(
                aprobar
                    ? "Aprobar nombramiento"
                    : "Cancelar nombramiento",
                aprobar
                    ? $"Se aprobará en JW Hub la recomendación de " +
                      $"{rec.Hermano}. Esta acción tiene efectos " +
                      "reales."
                    : $"Se cancelará en JW Hub la recomendación de " +
                      $"{rec.Hermano}. Esta acción tiene efectos " +
                      "reales.",
                aprobar ? "Aprobar" : "Cancelar",
                "Volver");

        if (!confirmar)
        {
            return;
        }

        try
        {
            LblTitulo.Text =
                "Procesando en JW Hub...";

            JwHubAccionesAndroid.ResultadoAccionHub
                resultado =
                    await JwHubAccionesAndroid
                        .ProcesarRecomendacionAsync(
                            rec.UrlDetalle,
                            aprobar,
                            DateTime.Today);

            if (resultado.Correcto)
            {
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .EliminarRecomendacionNombramientoMobile(
                                rec.Id));
            }

            await DisplayAlert(
                "JW Hub",
                resultado.Mensaje,
                "Aceptar");

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Nombramientos",
                "No se pudo procesar:\n" + ex.Message,
                "Aceptar");
        }
        finally
        {
            LblTitulo.Text =
                "RECOMENDACIONES DE NOMBRAMIENTOS";
        }
#else
        await DisplayAlert(
            "Nombramientos",
            "Aprobar o cancelar en JW Hub solo está disponible en " +
            "Android.",
            "Aceptar");
#endif
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
