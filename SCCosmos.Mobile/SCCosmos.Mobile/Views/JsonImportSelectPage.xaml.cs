using System.Globalization;
using System.Text.Json;

using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Views;


public partial class JsonImportSelectPage
    : ContentPage
{
    private readonly SemanaCalendario
        _semana;

    private readonly string
        _mesReferencia;

    private string
        _congregacionCodigo =
            "";

    private FileResult?
        _archivoS21;

    private FileResult?
        _archivoS13;

    private string?
        _rutaS21Convertida;

    private string?
        _rutaJsonCompartir;


    public JsonImportSelectPage(
        SemanaCalendario semana)
    {
        InitializeComponent();

        _semana =
            semana;

        _mesReferencia =
            semana.FechaMartes.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);

        LblSubtitulo.Text =
            string.Join(
                " · ",
                new[]
                {
                    semana.Congregacion ?? "",
                    semana.RangoFechas
                }
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)));

        PanelAviso.IsVisible =
            semana.TieneDatosImportados;
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!string.IsNullOrWhiteSpace(
                _congregacionCodigo))
        {
            return;
        }

        try
        {
            _congregacionCodigo =
                await Task.Run(
                    ObtenerCodigoCongregacion);

            if (string.IsNullOrWhiteSpace(
                    _congregacionCodigo))
            {
                BtnAnalizar.IsEnabled =
                    false;

                LblEstado.Text =
                    "No se ha podido localizar el código de esta " +
                    "congregación en la base de datos.";
            }
        }
        catch (Exception ex)
        {
            BtnAnalizar.IsEnabled =
                false;

            await DisplayAlert(
                "Importar actividad",
                "No se pudo preparar la importación:\n\n" +
                ex.Message,
                "Aceptar");
        }
    }


    private string ObtenerCodigoCongregacion()
    {
        string nombre =
            _semana.Congregacion
            ?? "";

        return DatabaseHelper
            .ObtenerCongregaciones()
            .FirstOrDefault(c =>
                string.Equals(
                    c.Nombre,
                    nombre,
                    StringComparison.OrdinalIgnoreCase))
            ?.Codigo
            ?? "";
    }


    // ========================================================================
    // SELECTORES DE ARCHIVO
    // ========================================================================

    private async void BtnS21_Clicked(
        object sender,
        EventArgs e)
    {
        FileResult? archivo =
            await SeleccionarJsonAsync(
                "Seleccionar S-21");

        if (archivo is null)
        {
            return;
        }

        _archivoS21 =
            archivo;

        LblS21.Text =
            archivo.FileName;

        LblS21.TextColor =
            Color.FromArgb("#8DE09A");

        LblEstado.Text =
            "S-21 preparado para analizar.";
    }


    private async void BtnS13_Clicked(
        object sender,
        EventArgs e)
    {
        FileResult? archivo =
            await SeleccionarJsonAsync(
                "Seleccionar S-13 / Territorios");

        if (archivo is null)
        {
            return;
        }

        _archivoS13 =
            archivo;

        LblS13.Text =
            archivo.FileName;

        LblS13.TextColor =
            Color.FromArgb("#80C4FF");

        LblEstado.Text =
            "S-13 preparado. Se importará junto con el S-21.";
    }


    private static async Task<FileResult?>
        SeleccionarJsonAsync(
            string titulo)
    {
        try
        {
            var tipos =
                new FilePickerFileType(
                    new Dictionary<
                        DevicePlatform,
                        IEnumerable<string>>
                    {
                        [DevicePlatform.Android] =
                            new[]
                            {
                                "application/json",
                                "text/json",
                                "text/plain",
                                "application/octet-stream"
                            }
                    });

            return await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle =
                        titulo,

                    FileTypes =
                        tipos
                });
        }
        catch
        {
            // Algunos proveedores de archivos no anuncian correctamente el
            // MIME de los .json. En ese caso dejamos que Android muestre
            // cualquier archivo y después validamos el contenido.
            return await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle =
                        titulo
                });
        }
    }


    // ========================================================================
    // CONVERSIÓN DE TARJETAS S-21 (PDF)
    // ========================================================================

    private async void BtnConvertirPdf_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            IEnumerable<FileResult>?
                elegidos =
                    await FilePicker.Default.PickMultipleAsync(
                        new PickOptions
                        {
                            PickerTitle =
                                "Elegir tarjetas S-21 (PDF)"
                        });

            List<FileResult> pdfs =
                elegidos?
                    .Where(a =>
                        a.FileName.EndsWith(
                            ".pdf",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList()
                ?? new List<FileResult>();

            if (pdfs.Count == 0)
            {
                return;
            }

            MostrarCarga(
                true,
                "Convirtiendo tarjetas S-21...");

            BtnConvertirPdf.IsEnabled =
                false;

            int anio =
                ConvertidorS21.AnioCicloActual(
                    DateTime.ParseExact(
                        _mesReferencia + "-01",
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));

            string carpeta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    "s21pdf");

            Directory.CreateDirectory(
                carpeta);

            foreach (string viejo in
                Directory.GetFiles(carpeta))
            {
                try
                {
                    File.Delete(viejo);
                }
                catch
                {
                }
            }

            int indice = 0;

            foreach (FileResult pdf in pdfs)
            {
                indice++;

                string destino =
                    Path.Combine(
                        carpeta,
                        indice.ToString("00") + "_" +
                        pdf.FileName);

                await using Stream origen =
                    await pdf.OpenReadAsync();

                await using FileStream salida =
                    File.Create(destino);

                await origen.CopyToAsync(
                    salida);
            }

            SCCosmos.Sincro.ResultadoConversionS21
                resultado =
                    await Task.Run(
                        () =>
                            ConvertidorS21.ConvertirCarpeta(
                                carpeta,
                                anio));

            if (resultado.Tarjetas.Count == 0)
            {
                LblPdfS21.Text =
                    "No se encontraron tarjetas S-21 en esos PDFs.";

                LblPdfS21.TextColor =
                    Color.FromArgb("#FF9C9C");

                return;
            }

            string rutaJson =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    "datos_s21.json");

            await File.WriteAllTextAsync(
                rutaJson,
                ConvertidorS21.GenerarJson(
                    resultado.Tarjetas));

            _rutaS21Convertida =
                rutaJson;

            _rutaJsonCompartir =
                rutaJson;

            LblPdfS21.Text =
                resultado.Tarjetas.Count + " tarjetas de " +
                pdfs.Count + " PDFs (" +
                resultado.Repetidas + " repetidas). " +
                "El JSON está listo para analizar.";

            LblPdfS21.TextColor =
                Color.FromArgb("#8DE09A");

            BtnCompartirJson.IsVisible =
                true;

            LblEstado.Text =
                "S-21 generado desde PDF. Pulsa Analizar archivos.";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Convertir tarjetas S-21",
                "No se pudieron convertir los PDFs:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            MostrarCarga(
                false,
                "");

            BtnConvertirPdf.IsEnabled =
                true;
        }
    }


    private async void BtnCompartirJson_Clicked(
        object sender,
        EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _rutaJsonCompartir) ||
            !File.Exists(
                _rutaJsonCompartir))
        {
            return;
        }

        try
        {
            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title =
                        "S-21 convertido",

                    File =
                        new ShareFile(
                            _rutaJsonCompartir)
                });
        }
        catch
        {
        }
    }


    // ========================================================================
    // ANALIZAR
    // ========================================================================

    private async void BtnAnalizar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_archivoS21 is null
            && string.IsNullOrWhiteSpace(
                _rutaS21Convertida))
        {
            await DisplayAlert(
                "Archivo faltante",
                "Debes seleccionar el archivo S-21 " +
                "o convertir las tarjetas PDF.",
                "Aceptar");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                _congregacionCodigo))
        {
            await DisplayAlert(
                "Importar actividad",
                "No se ha podido identificar la congregación.",
                "Aceptar");

            return;
        }

        BtnAnalizar.IsEnabled =
            false;

        MostrarCarga(
            true,
            "Leyendo y comparando datos...");

        try
        {
            List<PersonaJsonMobile> personas =
                _archivoS21 is not null
                    ? await LeerS21Async(
                        _archivoS21)
                    : await LeerS21DesdeRutaAsync(
                        _rutaS21Convertida!);

            List<TerritorioJsonMobile>?
                territorios =
                    _archivoS13 is null
                        ? null
                        : await LeerS13Async(
                            _archivoS13);

            if (personas.Count == 0)
            {
                throw new InvalidDataException(
                    "El archivo S-21 no contiene personas.");
            }

            if (personas.Any(p =>
                    string.IsNullOrWhiteSpace(
                        p.Nombre)))
            {
                throw new InvalidDataException(
                    "Hay registros del S-21 sin nombre. " +
                    "Revisa el archivo antes de continuar.");
            }

            ResultadoImportacionJsonMobile resultado =
                await Task.Run(
                    () =>
                        JsonImportAnalysisService.Analizar(
                            _congregacionCodigo,
                            _semana.Congregacion ?? "",
                            _semana.Id,
                            _mesReferencia,
                            personas,
                            territorios));

            var pagina =
                new JsonImportReviewPage(
                    resultado,
                    CerrarImportacionAsync);

            await Navigation.PushAsync(
                pagina);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Importar actividad",
                "No se pudieron analizar los archivos:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            MostrarCarga(
                false,
                "");

            BtnAnalizar.IsEnabled =
                true;
        }
    }


    private static async Task<List<PersonaJsonMobile>>
        LeerS21Async(
            FileResult archivo)
    {
        string contenido =
            await LeerArchivoAsync(
                archivo);

        contenido =
            NormalizarArrayJson(
                contenido);

        JsonSerializerOptions opciones =
            CrearOpcionesJson();

        return JsonSerializer
            .Deserialize<List<PersonaJsonMobile>>(
                contenido,
                opciones)
            ?? new();
    }


    private static async Task<List<PersonaJsonMobile>>
        LeerS21DesdeRutaAsync(
            string ruta)
    {
        string contenido =
            NormalizarArrayJson(
                (await File.ReadAllTextAsync(
                    ruta))
                .Trim());

        return JsonSerializer
            .Deserialize<List<PersonaJsonMobile>>(
                contenido,
                CrearOpcionesJson())
            ?? new();
    }


    private static async Task<List<TerritorioJsonMobile>>
        LeerS13Async(
            FileResult archivo)
    {
        string contenido =
            await LeerArchivoAsync(
                archivo);

        contenido =
            NormalizarArrayJson(
                contenido);

        JsonSerializerOptions opciones =
            CrearOpcionesJson();

        return JsonSerializer
            .Deserialize<List<TerritorioJsonMobile>>(
                contenido,
                opciones)
            ?? new();
    }


    private static JsonSerializerOptions
        CrearOpcionesJson()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive =
                true,

            AllowTrailingCommas =
                true,

            ReadCommentHandling =
                JsonCommentHandling.Skip
        };
    }


    private static async Task<string>
        LeerArchivoAsync(
            FileResult archivo)
    {
        await using Stream stream =
            await archivo.OpenReadAsync();

        using var reader =
            new StreamReader(stream);

        return (await reader.ReadToEndAsync())
            .Trim();
    }


    private static string NormalizarArrayJson(
        string contenido)
    {
        if (string.IsNullOrWhiteSpace(
                contenido))
        {
            throw new InvalidDataException(
                "El archivo JSON está vacío.");
        }

        string limpio =
            contenido.Trim();

        if (limpio.StartsWith(
                "[",
                StringComparison.Ordinal))
        {
            return limpio;
        }

        if (limpio.StartsWith(
                "{",
                StringComparison.Ordinal))
        {
            return "[" +
                   limpio +
                   "]";
        }

        throw new InvalidDataException(
            "El archivo seleccionado no parece contener JSON válido.");
    }


    // ========================================================================
    // CERRAR FLUJO
    // ========================================================================

    private async Task CerrarImportacionAsync()
    {
        await Navigation.PopAsync();
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }


    private void MostrarCarga(
        bool mostrar,
        string texto)
    {
        if (!string.IsNullOrWhiteSpace(
                texto))
        {
            LblCargando.Text =
                texto;
        }

        OverlayCargando.IsVisible =
            mostrar;
    }
}
