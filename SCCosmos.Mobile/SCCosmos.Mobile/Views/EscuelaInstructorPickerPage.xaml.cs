namespace SCCosmos.Mobile.Views;

/// <summary>
/// Selector con campo de búsqueda para elegir instructor (o sustituto):
/// lista filtrable por nombre o congregación; tocar una fila elige.
/// </summary>
public partial class EscuelaInstructorPickerPage : ContentPage
{
    private readonly List<ItemPersonaEscuelaMobile> _todos;

    private readonly TaskCompletionSource<ItemPersonaEscuelaMobile?> _resultado =
        new();

    public EscuelaInstructorPickerPage(
        string titulo,
        IEnumerable<ItemPersonaEscuelaMobile> opciones)
    {
        InitializeComponent();

        LblTitulo.Text =
            titulo.ToUpperInvariant();

        _todos =
            opciones.ToList();

        AplicarFiltro();
    }


    /// <summary>
    /// Espera la elección (null si se cancela o se vuelve atrás).
    /// </summary>
    public Task<ItemPersonaEscuelaMobile?> EsperarEleccionAsync() =>
        _resultado.Task;


    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _resultado.TrySetResult(
            null);
    }


    private void AplicarFiltro()
    {
        string filtro =
            (TxtBuscar.Text ?? "").Trim();

        Lista.ItemsSource =
            string.IsNullOrWhiteSpace(
                filtro)
                ? _todos
                : _todos
                    .Where(
                        i => (i.Visual ?? "")
                            .IndexOf(
                                filtro,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
    }


    private void TxtBuscar_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AplicarFiltro();
    }


    private async void Lista_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault()
            is not ItemPersonaEscuelaMobile elegido)
        {
            return;
        }

        _resultado.TrySetResult(
            elegido);

        if (sender is CollectionView lista)
        {
            lista.SelectedItem =
                null;
        }

        await Navigation.PopAsync();
    }
}
