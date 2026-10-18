using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Views;

public partial class ActividadPage : ContentPage
{
    private bool _cargando;

    public ActividadPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        CargarCategorias();
        Cargar();
    }

    private void CargarCategorias()
    {
        try
        {
            _cargando =
                true;

            PickerCategoria.ItemsSource =
                DatabaseHelper
                    .ObtenerCategoriasActividadMobile();

            PickerCategoria.SelectedIndex =
                0;

            _cargando =
                false;
        }
        catch
        {
        }
    }

    private void Cargar()
    {
        try
        {
            var entradas =
                DatabaseHelper.ObtenerActividadMobile(
                    PickerCategoria.SelectedItem?
                        .ToString()
                    ?? "Todas",
                    600);

            ListaActividad.ItemsSource =
                entradas;

            LblResumen.Text =
                entradas.Count == 0
                    ? "Sin actividad registrada todavía."
                    : $"{entradas.Count} acciones (más recientes primero)";
        }
        catch
        {
            LblResumen.Text =
                "No se pudo cargar la actividad.";
        }
    }

    private void PickerCategoria_SelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        Cargar();
    }

    private void BtnActualizar_Clicked(
        object? sender,
        EventArgs e)
    {
        CargarCategorias();
        Cargar();
    }
}
