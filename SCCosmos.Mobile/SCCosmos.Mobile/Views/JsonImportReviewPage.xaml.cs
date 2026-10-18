using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;


public partial class JsonImportReviewPage
    : ContentPage
{
    private readonly ResultadoImportacionJsonMobile
        _resultado;

    private readonly Func<Task>
        _alFinalizar;

    private TipoAnalisisJsonMobile
        _categoriaActual =
            TipoAnalisisJsonMobile.Conflicto;


    public JsonImportReviewPage(
        ResultadoImportacionJsonMobile resultado,
        Func<Task> alFinalizar)
    {
        InitializeComponent();

        _resultado =
            resultado;

        _alFinalizar =
            alFinalizar;

        LblSubtitulo.Text =
            string.Join(
                " · ",
                new[]
                {
                    resultado.CongregacionNombre,
                    resultado.MesReferencia,
                    resultado.Territorios is null
                        ? "Solo S-21"
                        : "S-21 + S-13"
                }
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)));
    }


    protected override void OnAppearing()
    {
        base.OnAppearing();

        Refrescar();
    }


    private void Refrescar()
    {
        TxtNumConflictos.Text =
            _resultado.TotalConflictos.ToString();

        TxtNumNuevos.Text =
            _resultado.TotalNuevos.ToString();

        TxtNumBajas.Text =
            _resultado.TotalBajas.ToString();

        TxtNumIguales.Text =
            _resultado.TotalIguales.ToString();

        int total =
            _resultado.TotalConflictos +
            _resultado.TotalNuevos +
            _resultado.TotalBajas +
            _resultado.TotalIguales;

        LblEstado.Text =
            $"{total} elementos";

        MostrarCategoria(
            _categoriaActual);
    }


    private void MostrarCategoria(
        TipoAnalisisJsonMobile categoria)
    {
        _categoriaActual =
            categoria;

        IReadOnlyList<JsonImportItemMobile>
            elementos;

        switch (categoria)
        {
            case TipoAnalisisJsonMobile.Conflicto:
                elementos =
                    _resultado.Conflictos;

                LblTituloLista.Text =
                    $"Conflictos ({elementos.Count})";
                break;

            case TipoAnalisisJsonMobile.Nuevo:
                elementos =
                    _resultado.Nuevos;

                LblTituloLista.Text =
                    $"Nuevos en S-21 ({elementos.Count})";
                break;

            case TipoAnalisisJsonMobile.Baja:
                elementos =
                    _resultado.Bajas;

                LblTituloLista.Text =
                    $"Posibles bajas ({elementos.Count})";
                break;

            default:
                elementos =
                    _resultado.Iguales;

                LblTituloLista.Text =
                    $"Sin cambios / revisados ({elementos.Count})";
                break;
        }

        ListaResultados.ItemsSource =
            null;

        ListaResultados.ItemsSource =
            elementos;
    }


    // ========================================================================
    // CATEGORÍAS
    // ========================================================================

    private void BtnConflictos_Tapped(
        object sender,
        TappedEventArgs e)
    {
        MostrarCategoria(
            TipoAnalisisJsonMobile.Conflicto);
    }


    private void BtnNuevos_Tapped(
        object sender,
        TappedEventArgs e)
    {
        MostrarCategoria(
            TipoAnalisisJsonMobile.Nuevo);
    }


    private void BtnBajas_Tapped(
        object sender,
        TappedEventArgs e)
    {
        MostrarCategoria(
            TipoAnalisisJsonMobile.Baja);
    }


    private void BtnIguales_Tapped(
        object sender,
        TappedEventArgs e)
    {
        MostrarCategoria(
            TipoAnalisisJsonMobile.Igual);
    }


    // ========================================================================
    // ACCIÓN POR ELEMENTO
    // ========================================================================

    private async void BtnAccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton ||
            boton.CommandParameter is not
                JsonImportItemMobile item)
        {
            return;
        }

        switch (item.Tipo)
        {
            case TipoAnalisisJsonMobile.Conflicto:
                await Navigation.PushAsync(
                    new JsonImportConflictPage(
                        _resultado,
                        item));
                break;

            case TipoAnalisisJsonMobile.Nuevo:
                await Navigation.PushAsync(
                    new JsonImportNewPersonPage(
                        _resultado,
                        item,
                        () =>
                        {
                            // Tras vincular una persona, volvemos a la
                            // bandeja de Conflictos, no a Nuevos.
                            _categoriaActual =
                                TipoAnalisisJsonMobile.Conflicto;
                        }));
                break;

            case TipoAnalisisJsonMobile.Baja:
                await ConfirmarBajaAsync(
                    item);
                break;
        }
    }


    private async Task ConfirmarBajaAsync(
        JsonImportItemMobile item)
    {
        if (item.PersonaDB is null)
        {
            return;
        }

        // Un "Nuevo" puede ser precisamente esta misma persona con un
        // nombre S-21 diferente. Evitamos confirmar la baja antes de haber
        // revisado todos los nuevos, para no perderla como candidata.
        if (_resultado.TotalNuevos > 0)
        {
            await DisplayAlert(
                "Revisa primero los nuevos",
                "Todavía hay personas clasificadas como nuevas en el S-21. " +
                "Alguna podría corresponder a esta posible baja." +
                "Revisa primero la categoría Nuevos y vuelve después a " +
                "Posibles bajas.",
                "Aceptar");

            MostrarCategoria(
                TipoAnalisisJsonMobile.Nuevo);

            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Confirmar baja",
                "¿Marcar como baja a " +
                item.NombreVisual +
                "?\n\n" +
                "No se borrará su historial. El cambio se aplicará " +
                "solo cuando finalices la importación.",
                "Confirmar baja",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        JsonImportAnalysisService
            .ConfirmarBaja(
                _resultado,
                item);

        Refrescar();
    }


    // ========================================================================
    // FINALIZAR
    // ========================================================================

    private async void BtnFinalizar_Clicked(
        object sender,
        EventArgs e)
    {
        // Una persona nueva no resuelta no tiene Id al que asociar actividad.
        // Por eso, como en el revisor JW actual, cada nuevo debe resolverse,
        // vincularse o descartarse antes de finalizar.
        if (_resultado.TotalNuevos > 0)
        {
            await DisplayAlert(
                "Nuevos pendientes",
                $"Todavía hay {_resultado.TotalNuevos} persona(s) " +
                "nueva(s) sin revisar.\n\n" +
                "Abre cada una y decide si corresponde a una persona " +
                "existente, si es realmente nueva o si debe omitirse.",
                "Aceptar");

            MostrarCategoria(
                TipoAnalisisJsonMobile.Nuevo);

            return;
        }

        string mensaje =
            "¿Finalizar la importación del S-21?";

        if (_resultado.TotalConflictos > 0)
        {
            mensaje +=
                $"\n\n• {_resultado.TotalConflictos} conflicto(s) " +
                "sin resolver conservarán los valores actuales de la " +
                "ficha, aunque su actividad mensual sí se importará.";
        }

        if (_resultado.TotalBajas > 0)
        {
            mensaje +=
                $"\n\n• {_resultado.TotalBajas} posible(s) baja(s) " +
                "sin confirmar permanecerán activas.";
        }

        if (_resultado.Territorios is not null)
        {
            mensaje +=
                $"\n\n• Se actualizarán {_resultado.Territorios.Count} " +
                "territorio(s) del S-13.";
        }

        bool confirmar =
            await DisplayAlert(
                "Finalizar importación",
                mensaje,
                "Finalizar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        BtnFinalizar.IsEnabled =
            false;

        OverlayCargando.IsVisible =
            true;

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper
                        .FinalizarImportacionJson(
                            _resultado));

            await DisplayAlert(
                "Importación completada",
                "El S-21 se ha importado correctamente" +
                (_resultado.Territorios is null
                    ? "."
                    : " junto con los territorios del S-13."),
                "Aceptar");

            await _alFinalizar();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Importar actividad",
                "No se pudo completar la importación. " +
                "La transacción se ha cancelado y no se han aplicado " +
                "cambios parciales.\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            OverlayCargando.IsVisible =
                false;

            BtnFinalizar.IsEnabled =
                true;
        }
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        bool salir =
            await DisplayAlert(
                "Cancelar importación",
                "¿Cerrar esta importación?\n\n" +
                "Como todavía no has pulsado Finalizar, no se escribirá " +
                "ningún cambio del S-21/S-13 en la base de datos.",
                "Cerrar",
                "Continuar revisando");

        if (!salir)
        {
            return;
        }

        await _alFinalizar();
    }
}
