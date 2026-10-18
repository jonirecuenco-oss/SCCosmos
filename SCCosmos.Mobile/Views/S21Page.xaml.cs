using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Generación de tarjetas S-21-S para una congregación. Requiere que el
/// usuario cargue una plantilla PDF del S-21-S (se guarda en la tabla
/// Documentos, categoría "S-21", compartida con WPF).
/// </summary>
public partial class S21Page : ContentPage
{
    private readonly string _congregacionNombre;
    private readonly string _congregacionCodigo;

    private DocumentoVisitaMobile? _plantilla;

    public S21Page(
        string congregacionNombre,
        string congregacionCodigo)
    {
        InitializeComponent();

        _congregacionNombre =
            congregacionNombre;

        _congregacionCodigo =
            congregacionCodigo;

        LblCongregacion.Text =
            congregacionNombre;

        TxtAnio.Text =
            EscuelasService
                .CalcularAnioInicioServicio(
                    DateTime.Today)
                .ToString();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        CargarPlantilla();
        ActualizarPublicadores();
    }

    private void CargarPlantilla()
    {
        try
        {
            _plantilla =
                DatabaseHelper
                    .ObtenerPlantillaS21VisitaMobile();

            LblPlantilla.Text =
                _plantilla is null
                    ? "Sin plantilla cargada. Carga un PDF S-21-S."
                    : "Plantilla: " + _plantilla.NombreArchivo;
        }
        catch (Exception ex)
        {
            LblPlantilla.Text =
                "No se pudo comprobar la plantilla: " +
                ex.Message;
        }
    }

    private void ActualizarPublicadores()
    {
        try
        {
            List<Persona> personas =
                DatabaseHelper
                    .ObtenerPersonasPorCongregacion(
                        _congregacionCodigo);

            LblPublicadores.Text =
                $"{personas.Count} publicadores en la congregación.";
        }
        catch
        {
            LblPublicadores.Text = "";
        }
    }

    private async void BtnCargarPlantilla_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var resultado =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Selecciona la plantilla S-21-S",

                        FileTypes =
                            new FilePickerFileType(
                                new Dictionary<DevicePlatform, IEnumerable<string>>
                                {
                                    {
                                        DevicePlatform.Android,
                                        new[] { "application/pdf" }
                                    },
                                    {
                                        DevicePlatform.WinUI,
                                        new[] { ".pdf" }
                                    },
                                    {
                                        DevicePlatform.iOS,
                                        new[] { "com.adobe.pdf" }
                                    },
                                    {
                                        DevicePlatform.MacCatalyst,
                                        new[] { "pdf" }
                                    }
                                })
                    });

            if (resultado is null)
            {
                return;
            }

            using Stream stream =
                await resultado.OpenReadAsync();

            using var memoria =
                new MemoryStream();

            await stream.CopyToAsync(
                memoria);

            byte[] contenido =
                memoria.ToArray();

            await Task.Run(
                () =>
                    DatabaseHelper
                        .GuardarPlantillaS21VisitaMobile(
                            resultado.FileName,
                            "",
                            contenido));

            CargarPlantilla();

            await DisplayAlert(
                "Plantilla S-21",
                "Plantilla guardada correctamente.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Plantilla S-21",
                "No se pudo cargar la plantilla: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnGenerar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_plantilla is null
            ||
            _plantilla.Contenido.Length == 0)
        {
            await DisplayAlert(
                "S-21",
                "Primero carga una plantilla S-21-S (PDF).",
                "Aceptar");

            return;
        }

        if (!int.TryParse(
                TxtAnio.Text?.Trim(),
                out int anioServicio))
        {
            await DisplayAlert(
                "S-21",
                "Introduce un año de servicio válido.",
                "Aceptar");

            return;
        }

        try
        {
            List<S21MobileService.TarjetaS21> tarjetas =
                await Task.Run(
                    () =>
                    {
                        List<Persona> personas =
                            DatabaseHelper
                                .ObtenerPersonasPorCongregacion(
                                    _congregacionCodigo);

                        var lista =
                            new List<S21MobileService.TarjetaS21>();

                        foreach (Persona persona in personas)
                        {
                            List<ActividadMensualMobile> actividad =
                                DatabaseHelper
                                    .ObtenerActividadPersonaMobile(
                                        persona.Id)
                                    .Where(a =>
                                        string.Equals(
                                            (a.AnioServicio ?? "").Trim(),
                                            anioServicio.ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                            lista.Add(
                                new S21MobileService.TarjetaS21
                                {
                                    Persona = persona,
                                    Actividad = actividad
                                });
                        }

                        return lista;
                    });

            byte[] pdf =
                await Task.Run(
                    () =>
                        S21MobileService.Generar(
                            _plantilla.Contenido,
                            tarjetas,
                            anioServicio));

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    $"S-21_{_congregacionNombre}_{anioServicio}.pdf");

            File.WriteAllBytes(
                ruta,
                pdf);

            LblEstado.Text =
                $"Generadas {tarjetas.Count} tarjetas para " +
                $"{anioServicio}.";

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title =
                        $"S-21 {_congregacionNombre} {anioServicio}",
                    File =
                        new ShareFile(
                            ruta)
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "S-21",
                "No se pudieron generar las tarjetas: " +
                ex.Message,
                "Aceptar");
        }
    }
}
