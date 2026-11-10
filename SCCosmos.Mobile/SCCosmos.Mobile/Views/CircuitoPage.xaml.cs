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

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarAsync();
        Services.Perf.Registrar(
            "Circuito",
            reloj.ElapsedMilliseconds);

        // Al volver de sus pueblos, el tiempo de esa congregación sigue desplegado.
        if (_reabrirTiempo is string codigo)
        {
            _reabrirTiempo = null;

            if ((Lista.ItemsSource as IEnumerable<CongregacionCircuito>)?
                    .FirstOrDefault(c => c.Congregacion.Codigo == codigo) is CongregacionCircuito abierta)
            {
                await abierta.AlternarAsync();
            }
        }

        // Al abrir Circuito también se miran los avisos rojos de sus congregaciones.
        _ = Task.Run(async () =>
        {
            try
            {
                await Services.TiempoAemetService.RevisarAvisosRojosAsync(CancellationToken.None);
            }
            catch
            {
            }
        });
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
                congregaciones
                    .Select(c => new CongregacionCircuito(c))
                    .ToList();

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
            CongregacionCircuito item)
        {
            return;
        }

        Congregacion congregacion =
            item.Congregacion;

        var editor =
            new CongregacionEdicionPage(
                congregacion);

        await Navigation.PushAsync(editor);

        if (await editor.EsperarResultadoAsync())
        {
            await CargarAsync();
        }
    }

    // Despliega (o pliega) el tiempo de AEMET de la congregación, debajo.
    private async void BtnTiempo_Clicked(
        object sender,
        EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CongregacionCircuito item)
        {
            await item.AlternarAsync();
        }
    }

    // Al tocar un pueblo de la fila, su tiempo.
    private async void PuebloTiempo_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is PuebloCelda pueblo)
        {
            await pueblo.Congregacion.ElegirPuebloAsync(pueblo.Indice);
        }
    }


    // La congregación cuyo tiempo se vuelve a desplegar al volver de sus pueblos.
    private string? _reabrirTiempo;


    // Los pueblos de su territorio (además del del salón).
    private async void BtnPueblos_Clicked(
        object sender,
        EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CongregacionCircuito item)
        {
            _reabrirTiempo = item.Congregacion.Codigo;

            await Navigation.PushAsync(
                new PueblosTerritorioPage(item.Congregacion));
        }
    }


    // Al tocar uno de los 7 días, se ven sus horas.
    private async void DiaTiempo_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not DiaCelda dia)
        {
            return;
        }

        await dia.Congregacion.CargarAsync(dia.Fecha);

        // Las horas del día elegido, desde el principio (hoy, desde la hora actual).
        if ((sender as Element)?.Parent?.Parent is ScrollView dias
            && dias.Parent is Layout panel
            && panel.Children.SkipWhile(c => !ReferenceEquals(c, dias)).Skip(1).OfType<ScrollView>().FirstOrDefault() is ScrollView horas)
        {
            await Task.Delay(150);
            await horas.ScrollToAsync(dia.Congregacion.IndiceActual * 56, 0, false);
        }
    }

    // Al tocar una hora con aviso, qué aviso es (sin depender del color).
    private async void AvisoTiempo_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is HoraCelda hora && hora.HayAviso)
        {
            await DisplayAlert("Avisos de AEMET", hora.TextoAvisos, "Aceptar");
        }
    }

    // Con las horas ya puestas, hoy se ven desde la hora actual (como en Inicio).
    private void HorasTiempo_ChildAdded(
        object? sender,
        ElementEventArgs e)
    {
        if (sender is Layout fila
            && fila.Parent is ScrollView scroll
            && fila.BindingContext is CongregacionCircuito item
            && fila.Children.Count == item.Horas.Count)
        {
            fila.Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(120),
                () => _ = scroll.ScrollToAsync(item.IndiceActual * 56, 0, false));
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
