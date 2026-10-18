namespace SCCosmos.Mobile.Views;

/// <summary>
/// Página contenedora de la matriz de oradores (la vista se comparte con la
/// pestaña "Oradores" de la página de Asambleas).
/// </summary>
public partial class MatrizOradoresPage : ContentPage
{
    public MatrizOradoresPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await Contenido.CargarAsync();
    }
}
