using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Candidatos a la Escuela de Precursores. Réplica funcional de
/// EscuelasPage de WPF, con la lista de descartados persistida en la
/// misma clave de configuración (DescartadosEscuelas_&lt;año&gt;).
/// </summary>
public partial class EscuelasPage : ContentPage, IActualizableMobile
{
    private bool _viendoDescartados;
    private bool _cargado;

    public EscuelasPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_cargado)
        {
            _cargado = true;

            await CargarAsync();
        }

        // Las clases se refrescan siempre al volver (por si se creó,
        // editó o eliminó alguna al entrar en su detalle).
        await CargarClasesAsync();
    }

    private static string ClaveDescartados()
    {
        int anio =
            EscuelasService
                .CalcularAnioInicioServicio(
                    DateTime.Today);

        return EscuelasService.PrefijoClaveDescartados +
               anio;
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarAsync();
            _ = CargarClasesAsync();
        }
        catch
        {
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            string clave =
                ClaveDescartados();

            var resultado =
                await Task.Run(
                    () =>
                    {
                        List<Persona> personas =
                            DatabaseHelper
                                .ObtenerPersonas();

                        List<int> descartados =
                            EscuelasService
                                .ParsearDescartados(
                                    DatabaseHelper
                                        .ObtenerConfiguracion(
                                            clave,
                                            ""));

                        return EscuelasService
                            .Calcular(
                                personas,
                                _viendoDescartados,
                                descartados,
                                DateTime.Today);
                    });

            Lista.ItemsSource =
                resultado;

            LblTotal.Text =
                $"({resultado.Count} encontrados)";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Escuelas",
                "No se pudieron cargar los candidatos:\n" +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnVerDescartados_Clicked(
        object sender,
        EventArgs e)
    {
        _viendoDescartados =
            !_viendoDescartados;

        BtnVerDescartados.Text =
            _viendoDescartados
                ? "Volver a lista oficial"
                : "Ver descartados";

        await CargarAsync();
    }

    private async void BtnAccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                EstudianteEscuelaMobile est)
        {
            return;
        }

        try
        {
            string clave =
                ClaveDescartados();

            List<int> descartados =
                EscuelasService
                    .ParsearDescartados(
                        DatabaseHelper
                            .ObtenerConfiguracion(
                                clave,
                                ""));

            if (est.IsDescartado)
            {
                descartados.Remove(
                    est.Id);
            }
            else if (!descartados.Contains(
                         est.Id))
            {
                descartados.Add(
                    est.Id);
            }

            DatabaseHelper.GuardarConfiguracion(
                clave,
                EscuelasService
                    .SerializarDescartados(
                        descartados));

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Escuelas",
                "No se pudo actualizar la lista: " +
                ex.Message,
                "Aceptar");
        }
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


    // =========================================================
    // PESTAÑAS (ESTUDIANTES / CLASES)
    // =========================================================

    private bool _viendoClases;


    private void MostrarPestana(
        bool clases)
    {
        _viendoClases =
            clases;

        PanelEstudiantes.IsVisible =
            !clases;

        PanelClases.IsVisible =
            clases;

        try
        {
            BtnTabEstudiantes.Style =
                (Style)Application
                    .Current!
                    .Resources[clases
                        ? "BotonSecundario"
                        : "BotonPrincipal"];

            BtnTabClases.Style =
                (Style)Application
                    .Current!
                    .Resources[clases
                        ? "BotonPrincipal"
                        : "BotonSecundario"];
        }
        catch
        {
        }
    }


    private void BtnTabEstudiantes_Clicked(
        object sender,
        EventArgs e)
    {
        MostrarPestana(
            false);
    }


    private void BtnTabClases_Clicked(
        object sender,
        EventArgs e)
    {
        MostrarPestana(
            true);
    }


    // =========================================================
    // CLASES (SEMANAS DE ESCUELA)
    // =========================================================

    private async Task CargarClasesAsync()
    {
        try
        {
            var clases =
                await Task.Run(
                    () => DatabaseHelper
                        .ObtenerEscuelasClasesMobile());

            ListaClases.ItemsSource =
                clases;

            LblResumenClases.Text =
                clases.Count == 0
                    ? "Sin clases creadas"
                    : clases.Count == 1
                        ? "1 clase creada"
                        : clases.Count + " clases creadas";
        }
        catch
        {
        }
    }


    private async void RefrescarClases_Refreshing(
        object sender,
        EventArgs e)
    {
        try
        {
            await CargarClasesAsync();
        }
        finally
        {
            RefrescarClases.IsRefreshing =
                false;
        }
    }


    private async void BtnNuevaEscuela_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new EscuelaEdicionPage(
                null));
    }


    private async void TarjetaClase_Tapped(
        object sender,
        TappedEventArgs e)
    {
        try
        {
            if (sender is Border borde
                && borde.BindingContext is EscuelaClaseMobile escuela)
            {
                await Navigation.PushAsync(
                    new EscuelaClasePage(
                        escuela.Id));
            }
        }
        catch
        {
        }
    }
}
