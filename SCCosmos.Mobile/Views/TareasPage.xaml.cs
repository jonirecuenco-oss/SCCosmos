using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

public partial class TareasPage : ContentPage, IActualizableMobile
{
    private List<Tarea> _tareas =
        new();


    public TareasPage()
    {
        InitializeComponent();
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

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
            List<Tarea> tareas =
                await Task.Run(
                    () => DatabaseHelper.ObtenerTareasMobile());

            _tareas =
                tareas
                    .OrderBy(t => t.Completada)
                    .ThenBy(t => t.FechaInicio)
                    .ThenBy(t => t.Id)
                    .ToList();

            Lista.ItemsSource =
                null;
            Lista.ItemsSource =
                _tareas;

            int pendientes =
                _tareas.Count(t => !t.Completada);

            LblResumen.Text =
                _tareas.Count == 0
                    ? "Programa tareas, eventos y avisos"
                    : pendientes == 1
                        ? "1 tarea pendiente"
                        : $"{pendientes} tareas pendientes";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Tareas",
                "No se pudieron cargar las tareas: " +
                ex.Message,
                "Aceptar");
        }
    }


    private async void BtnNueva_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new TareaEdicionPage(null));
    }


    private async void Tarjeta_Tapped(
        object sender,
        TappedEventArgs e)
    {
        Tarea? tarea =
            e.Parameter as Tarea;

        if (tarea == null
            && sender is BindableObject elemento)
        {
            tarea =
                elemento.BindingContext as Tarea;
        }

        if (tarea == null)
        {
            return;
        }

        await Navigation.PushAsync(
            new TareaEdicionPage(tarea));
    }


    private async void Completada_CheckedChanged(
        object sender,
        CheckedChangedEventArgs e)
    {
        if (sender is not BindableObject elemento
            || elemento.BindingContext is not Tarea tarea)
        {
            return;
        }

        if (tarea.Completada == e.Value)
        {
            return;
        }

        tarea.Completada =
            e.Value;

        await Task.Run(
            () => DatabaseHelper.MarcarTareaCompletadaMobile(
                tarea.Id,
                e.Value));

        await CargarAsync();
    }


    private async void Refrescar_Refreshing(
        object sender,
        EventArgs e)
    {
        Refrescar.IsRefreshing =
            false;

        await CargarAsync();
    }
}
