using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;


public partial class JsonImportNewPersonPage
    : ContentPage
{
    private readonly ResultadoImportacionJsonMobile
        _resultado;

    private readonly JsonImportItemMobile
        _item;

    private readonly Action
        _alVincular;


    public JsonImportNewPersonPage(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item,
        Action? alVincular = null)
    {
        InitializeComponent();

        _resultado =
            resultado;

        _item =
            item;

        _alVincular =
            alVincular
            ?? (() => { });

        if (item.PersonaJson is null)
        {
            throw new ArgumentException(
                "El elemento no contiene datos S-21.");
        }

        PersonaJsonMobile json =
            item.PersonaJson;

        LblNombreJson.Text =
            json.Nombre;

        LblDatosJson.Text =
            string.Join(
                " · ",
                new[]
                {
                    json.Grupo > 0
                        ? $"Grupo {json.Grupo}"
                        : "Sin grupo",

                    json.FechaNacimiento is { Length: > 0 }
                        ? "Nac. " + json.FechaNacimiento
                        : "",

                    json.FechaBautismo is { Length: > 0 }
                        ? "Baut. " + json.FechaBautismo
                        : "",

                    json.Nombramiento ?? "",
                    json.Servicio ?? ""
                }
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)));

        CargarCandidatos();
    }


    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Si volvemos desde una vinculación cancelada, refrescamos por si
        // alguna categoría cambió en otra navegación.
        CargarCandidatos();
    }


    private void CargarCandidatos()
    {
        ListaCandidatos.ItemsSource =
            JsonImportAnalysisService
                .ObtenerCandidatosVinculo(
                    _resultado,
                    _item);
    }


    private async void BtnVincular_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton ||
            boton.CommandParameter is not
                CandidatoVinculoJsonMobile candidato)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Vincular persona",
                "¿Revisar " +
                _item.PersonaJson!.Nombre +
                " como la misma persona que " +
                candidato.NombreVisual +
                "?",
                "Revisar vinculación",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        await Navigation.PushAsync(
            new JsonImportConflictPage(
                _resultado,
                _item,
                candidato.Persona,
                modoVinculacion: true,
                alFinalizarVinculacion:
                    _alVincular));
    }


    private async void BtnCrearNueva_Clicked(
        object sender,
        EventArgs e)
    {
        if (_item.PersonaJson is null)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Persona nueva",
                "¿Confirmas que " +
                _item.PersonaJson.Nombre +
                " no corresponde a ninguna ficha existente?\n\n" +
                "Se añadirá al pulsar Finalizar. Como no tiene cuenta JW " +
                "vinculada, su nombre común será inicialmente el mismo " +
                "nombre del S-21.",
                "Es nueva",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        Persona nueva =
            JsonImportAnalysisService
                .CrearPersonaDesdeJson(
                    _item.PersonaJson,
                    _resultado.CongregacionCodigo);

        JsonImportAnalysisService
            .PrepararPersonaNueva(
                _resultado,
                _item,
                nueva);

        await Navigation.PopAsync();
    }


    private async void BtnOmitir_Clicked(
        object sender,
        EventArgs e)
    {
        bool confirmar =
            await DisplayAlert(
                "Omitir registro",
                "¿Omitir este registro S-21?\n\n" +
                "No se creará ni vinculará la persona y tampoco se " +
                "importará su actividad.",
                "Omitir",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        JsonImportAnalysisService
            .OmitirNuevo(
                _resultado,
                _item);

        await Navigation.PopAsync();
    }


    private async void BtnVolver_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
