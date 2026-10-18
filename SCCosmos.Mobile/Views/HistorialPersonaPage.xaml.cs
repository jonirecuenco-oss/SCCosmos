using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Historial (instantáneas mensuales) de una persona. Solo lectura.
/// </summary>
public partial class HistorialPersonaPage : ContentPage
{
    private readonly int _personaId;

    private readonly string _nombre;

    public HistorialPersonaPage(
        int personaId,
        string nombre)
    {
        InitializeComponent();

        _personaId =
            personaId;

        _nombre =
            nombre;

        LblPersona.Text =
            nombre;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            List<DatabaseHelper.InstantaneaHistoricaMobile>
                items =
                    await Task.Run(
                        () =>
                            DatabaseHelper
                                .ObtenerHistorialPersonaMobile(
                                    _personaId));

            Lista.ItemsSource =
                items;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Historial",
                "No se pudo cargar el historial: " +
                ex.Message,
                "Aceptar");
        }
    }
}
