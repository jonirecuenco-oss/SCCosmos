using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Módulo Circuito: listado y edición de congregaciones y secciones, y
/// subida del KMZ (coordenadas de los salones), como en Windows.
/// </summary>
public partial class CircuitoPage : ContentPage, IActualizableMobile
{
    public CircuitoPage()
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


    private async void BtnSubirKmz_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            FileResult? archivo =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle = "Seleccionar mapa KMZ o KML"
                    });

            if (archivo is null)
            {
                return;
            }

            string contenido = "";

            if (archivo.FileName.EndsWith(
                    ".kmz",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var flujo =
                    await archivo.OpenReadAsync();

                using var comprimido =
                    new ZipArchive(
                        flujo,
                        ZipArchiveMode.Read);

                var entrada =
                    comprimido.Entries.FirstOrDefault(
                        x => x.FullName.EndsWith(
                            ".kml",
                            StringComparison.OrdinalIgnoreCase));

                if (entrada is not null)
                {
                    using var lector =
                        new StreamReader(
                            entrada.Open());

                    contenido =
                        await lector.ReadToEndAsync();
                }
            }
            else
            {
                using var flujo =
                    await archivo.OpenReadAsync();

                using var lector =
                    new StreamReader(
                        flujo);

                contenido =
                    await lector.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(contenido))
            {
                await DisplayAlert(
                    "KMZ",
                    "No se encontró información en el archivo.",
                    "Aceptar");

                return;
            }

            int actualizadas =
                await Task.Run(
                    () =>
                    {
                        int total = 0;

                        var documento =
                            XDocument.Parse(contenido);

                        XNamespace ns =
                            documento.Root!.GetDefaultNamespace();

                        foreach (var marcador
                                 in documento.Descendants(
                                     ns + "Placemark"))
                        {
                            string nombre =
                                marcador.Element(
                                        ns + "name")?
                                    .Value?
                                    .Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(nombre))
                            {
                                continue;
                            }

                            string punto =
                                marcador.Descendants(
                                        ns + "Point")
                                    .FirstOrDefault()?
                                    .Element(
                                        ns + "coordinates")?
                                    .Value?
                                    .Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(punto))
                            {
                                continue;
                            }

                            string[] partes =
                                punto.Split(',');

                            if (partes.Length < 2)
                            {
                                continue;
                            }

                            if (double.TryParse(
                                    partes[0],
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out double lon)
                                && double.TryParse(
                                    partes[1],
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out double lat))
                            {
                                if (DatabaseHelper
                                    .ActualizarCoordenadasCongregacionMobile(
                                        nombre,
                                        lat,
                                        lon))
                                {
                                    total++;
                                }
                            }
                        }

                        return total;
                    });

            await CargarAsync();

            await DisplayAlert(
                "KMZ",
                actualizadas == 0
                    ? "No se actualizó ninguna congregación (los nombres del mapa no coinciden con los de la lista)."
                    : $"Mapa importado: {actualizadas} congregaciones actualizadas con sus coordenadas.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "KMZ",
                "No se pudo leer el archivo:\n" + ex.Message,
                "Aceptar");
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            List<Congregacion> congregaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCongregacionesCompletasMobile());

            Lista.ItemsSource =
                congregaciones;

            LblTotal.Text =
                $"{congregaciones.Count} congregaciones";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Circuito",
                "No se pudo cargar el circuito: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void Congregacion_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
            Congregacion congregacion)
        {
            return;
        }

        var editor =
            new CongregacionEdicionPage(
                congregacion);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        var editor =
            new CongregacionEdicionPage(
                null);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }

    private async void BtnSecciones_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(new SeccionesCircuitoPage());
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
