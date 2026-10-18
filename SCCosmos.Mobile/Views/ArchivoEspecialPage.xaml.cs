using System.Text.Json;

using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Secciones especiales del Archivo del circuito: hermanos con
/// potencial (Archivo_HermanosPotencial.json) y problemas pendientes
/// (Archivo_ProblemasPendientes.json). Lee y edita los mismos JSON
/// que WPF.
/// </summary>
public partial class ArchivoEspecialPage : ContentPage
{
    private readonly bool _esPotencial;

    private readonly string _raiz;

    private const string JsonPotencial =
        "Archivo_HermanosPotencial.json";

    private const string JsonProblemas =
        "Archivo_ProblemasPendientes.json";

    private List<HermanoPotencialMobile> _potenciales =
        new();

    private List<ProblemaPendienteMobile> _problemas =
        new();

    private Dictionary<int, string> _nombres =
        new();

    public ArchivoEspecialPage(
        bool esPotencial,
        string raiz)
    {
        InitializeComponent();

        _esPotencial =
            esPotencial;

        _raiz =
            raiz ?? "";

        Title =
            esPotencial
                ? "Hermanos con potencial"
                : "Problemas pendientes";

        LblTitulo.Text =
            Title;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            if (_esPotencial)
            {
                _potenciales =
                    Deserializar<List<HermanoPotencialMobile>>(
                        await SyncFolderService
                            .LeerTextoAsync(
                                _raiz,
                                JsonPotencial));

                _nombres =
                    (await Task.Run(
                        () => DatabaseHelper.ObtenerPersonas()))
                    .ToDictionary(
                        p => p.Id,
                        p => p.NombreMostrarTabla);

                Lista.ItemsSource =
                    _potenciales
                        .Select(p => new ItemEspecialUi
                        {
                            Id = p.Id,
                            Titulo = _nombres.TryGetValue(
                                         p.PersonaId,
                                         out string? n)
                                         ? n
                                         : "(persona #" +
                                           p.PersonaId + ")",
                            Descripcion = p.Descripcion
                        })
                        .ToList();
            }
            else
            {
                _problemas =
                    Deserializar<List<ProblemaPendienteMobile>>(
                        await SyncFolderService
                            .LeerTextoAsync(
                                _raiz,
                                JsonProblemas));

                Lista.ItemsSource =
                    _problemas
                        .Select(p => new ItemEspecialUi
                        {
                            Id = p.Id,
                            Titulo = string.IsNullOrWhiteSpace(
                                         p.Congregacion)
                                         ? "(sin congregación)"
                                         : p.Congregacion,
                            Descripcion = p.Descripcion
                        })
                        .ToList();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                Title,
                "No se pudo cargar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private static T Deserializar<T>(
        string texto)
        where T : new()
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                       texto)
                   ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private async Task GuardarAsync()
    {
        if (_esPotencial)
        {
            await SyncFolderService
                .EscribirTextoAsync(
                    _raiz,
                    JsonPotencial,
                    JsonSerializer.Serialize(
                        _potenciales));
        }
        else
        {
            await SyncFolderService
                .EscribirTextoAsync(
                    _raiz,
                    JsonProblemas,
                    JsonSerializer.Serialize(
                        _problemas));
        }
    }

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            if (_esPotencial)
            {
                List<Persona> personas =
                    await Task.Run(
                        () => DatabaseHelper.ObtenerPersonas());

                if (personas.Count == 0)
                {
                    return;
                }

                var etiquetas =
                    personas
                        .OrderBy(p => p.NombreMostrarTabla)
                        .Select(p => p.NombreMostrarTabla)
                        .ToArray();

                string elegido =
                    await DisplayActionSheet(
                        "Hermano con potencial:",
                        "Cancelar",
                        null,
                        etiquetas);

                int idx =
                    Array.IndexOf(
                        etiquetas,
                        elegido);

                if (idx < 0)
                {
                    return;
                }

                Persona persona =
                    personas
                        .OrderBy(p => p.NombreMostrarTabla)
                        .ElementAt(idx);

                string descripcion =
                    await DisplayPromptAsync(
                        "Descripción",
                        "Detalle (opcional):",
                        "Guardar",
                        "Cancelar");

                _potenciales.Add(
                    new HermanoPotencialMobile
                    {
                        PersonaId = persona.Id,
                        Descripcion = descripcion ?? ""
                    });
            }
            else
            {
                string congregacion =
                    await DisplayPromptAsync(
                        "Problema pendiente",
                        "Congregación:",
                        "Siguiente",
                        "Cancelar");

                if (string.IsNullOrWhiteSpace(
                        congregacion))
                {
                    return;
                }

                string descripcion =
                    await DisplayPromptAsync(
                        "Problema pendiente",
                        "Descripción:",
                        "Guardar",
                        "Cancelar");

                _problemas.Add(
                    new ProblemaPendienteMobile
                    {
                        Congregacion = congregacion.Trim(),
                        Descripcion = descripcion ?? ""
                    });
            }

            await GuardarAsync();

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                Title,
                "No se pudo añadir: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ItemEspecialUi item)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar",
                "¿Eliminar este registro?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        if (_esPotencial)
        {
            _potenciales.RemoveAll(
                p => p.Id == item.Id);
        }
        else
        {
            _problemas.RemoveAll(
                p => p.Id == item.Id);
        }

        await GuardarAsync();

        await CargarAsync();
    }

    private async void BtnPdf_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var filas =
                (Lista.ItemsSource as IEnumerable<ItemEspecialUi>)
                    ?.ToList()
                ?? new List<ItemEspecialUi>();

            byte[] pdf =
                await Task.Run(
                    () =>
                        ArchivoEspecialPdfService.Generar(
                            Title,
                            filas));

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    (_esPotencial
                        ? "HermanosPotencial"
                        : "ProblemasPendientes") +
                    ".pdf");

            await File.WriteAllBytesAsync(
                ruta,
                pdf);

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = Title,
                    File = new ShareFile(ruta)
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                Title,
                "No se pudo generar el PDF: " +
                ex.Message,
                "Aceptar");
        }
    }
}
