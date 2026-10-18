using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Navegación y edición de una carpeta del Archivo del circuito:
/// subcarpetas anidadas (CarpetaVFS.ParentId) y archivos. Mismos JSON
/// que WPF (VFS).
/// </summary>
public partial class CarpetaVfsPage : ContentPage
{
    private readonly string _seccionId;

    private readonly string _parentId;

    private VfsService.VfsContenido _vfs =
        new();

    public CarpetaVfsPage(
        string seccionId,
        string parentId,
        string titulo)
    {
        InitializeComponent();

        _seccionId =
            seccionId;

        _parentId =
            parentId ?? "";

        Title =
            string.IsNullOrWhiteSpace(titulo)
                ? "Carpeta"
                : titulo;

        LblRuta.Text =
            string.IsNullOrWhiteSpace(titulo)
                ? "Raíz de la sección"
                : titulo;
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
            _vfs =
                await VfsService.CargarAsync();

            List<CarpetaVfsMobile> subcarpetas =
                _vfs.Carpetas
                    .Where(c =>
                        string.Equals(
                            c.SeccionId,
                            _seccionId,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        string.Equals(
                            c.ParentId ?? "",
                            _parentId,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Nombre)
                    .ToList();

            List<ArchivoVfsMobile> archivos =
                _vfs.Archivos
                    .Where(a =>
                        string.Equals(
                            a.SeccionId,
                            _seccionId,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        string.Equals(
                            a.CarpetaId ?? "",
                            _parentId,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => a.NombreOriginal)
                    .ToList();

            BindableLayout.SetItemsSource(
                ListaCarpetas,
                subcarpetas);

            LblSinCarpetas.IsVisible =
                subcarpetas.Count == 0;

            BindableLayout.SetItemsSource(
                ListaArchivos,
                archivos);

            LblSinArchivos.IsVisible =
                archivos.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo",
                "No se pudo cargar la carpeta: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnNuevaCarpeta_Clicked(
        object sender,
        EventArgs e)
    {
        string nombre =
            await DisplayPromptAsync(
                "Nueva carpeta",
                "Nombre:",
                "Crear",
                "Cancelar");

        if (string.IsNullOrWhiteSpace(
                nombre))
        {
            return;
        }

        _vfs.Carpetas.Add(
            new CarpetaVfsMobile
            {
                SeccionId = _seccionId,
                ParentId = _parentId,
                Nombre = nombre.Trim()
            });

        await VfsService.GuardarCarpetasAsync(
            _vfs.Carpetas);

        await CargarAsync();
    }

    private async void BtnAbrirCarpeta_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                CarpetaVfsMobile carpeta)
        {
            await Navigation.PushAsync(
                new CarpetaVfsPage(
                    _seccionId,
                    carpeta.Id,
                    carpeta.Nombre));
        }
    }

    private async void BtnRenombrarCarpeta_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CarpetaVfsMobile carpeta)
        {
            return;
        }

        string nuevo =
            await DisplayPromptAsync(
                "Renombrar carpeta",
                "Nuevo nombre:",
                "Guardar",
                "Cancelar",
                initialValue: carpeta.Nombre);

        if (string.IsNullOrWhiteSpace(
                nuevo))
        {
            return;
        }

        carpeta.Nombre =
            nuevo.Trim();

        await VfsService.GuardarCarpetasAsync(
            _vfs.Carpetas);

        await CargarAsync();
    }

    private async void BtnEliminarCarpeta_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                CarpetaVfsMobile carpeta)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar carpeta",
                $"¿Eliminar «{carpeta.Nombre}» y todo su contenido?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        // Reunir la carpeta y todos sus descendientes.
        var ids =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                carpeta.Id
            };

        bool crecer = true;

        while (crecer)
        {
            crecer = false;

            foreach (CarpetaVfsMobile c in _vfs.Carpetas)
            {
                if (ids.Contains(c.ParentId ?? "")
                    &&
                    !ids.Contains(c.Id))
                {
                    ids.Add(c.Id);
                    crecer = true;
                }
            }
        }

        // Borrar archivos físicos y de registro.
        foreach (ArchivoVfsMobile a in _vfs.Archivos
                     .Where(a => ids.Contains(a.CarpetaId ?? ""))
                     .ToList())
        {
            VfsService.EliminarFisico(
                a.NombreFisico);

            _vfs.Archivos.Remove(a);
        }

        _vfs.Carpetas.RemoveAll(
            c => ids.Contains(c.Id));

        await VfsService.GuardarCarpetasAsync(
            _vfs.Carpetas);

        await VfsService.GuardarArchivosAsync(
            _vfs.Archivos);

        await CargarAsync();
    }

    private async void BtnSubir_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            var archivo =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Selecciona el archivo"
                    });

            if (archivo is null)
            {
                return;
            }

            using Stream stream =
                await archivo.OpenReadAsync();

            using var memoria =
                new MemoryStream();

            await stream.CopyToAsync(
                memoria);

            string extension =
                Path.GetExtension(
                    archivo.FileName);

            string nombreFisico =
                Guid.NewGuid().ToString("N") +
                extension;

            bool ok =
                await VfsService.GuardarFisicoAsync(
                    nombreFisico,
                    memoria.ToArray());

            if (!ok)
            {
                throw new IOException(
                    "No se pudo guardar el archivo en la carpeta " +
                    "sincronizada.");
            }

            _vfs.Archivos.Add(
                new ArchivoVfsMobile
                {
                    SeccionId = _seccionId,
                    CarpetaId = _parentId,
                    NombreOriginal = archivo.FileName,
                    NombreFisico = nombreFisico
                });

            await VfsService.GuardarArchivosAsync(
                _vfs.Archivos);

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo",
                "No se pudo subir: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnAbrirArchivo_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ArchivoVfsMobile archivo)
        {
            return;
        }

        try
        {
            byte[] contenido =
                await VfsService.LeerFisicoAsync(
                    archivo.NombreFisico);

            if (contenido.Length == 0)
            {
                await DisplayAlert(
                    "Archivo",
                    "No se pudo abrir el archivo.",
                    "Aceptar");

                return;
            }

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    archivo.NombreOriginal);

            await File.WriteAllBytesAsync(
                ruta,
                contenido);

            await Launcher.Default.OpenAsync(
                new OpenFileRequest(
                    archivo.NombreOriginal,
                    new ReadOnlyFile(ruta)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo",
                "No se pudo abrir: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnRenombrarArchivo_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ArchivoVfsMobile archivo)
        {
            return;
        }

        string nuevo =
            await DisplayPromptAsync(
                "Renombrar archivo",
                "Nuevo nombre visible:",
                "Guardar",
                "Cancelar",
                initialValue: archivo.NombreOriginal);

        if (string.IsNullOrWhiteSpace(
                nuevo))
        {
            return;
        }

        archivo.NombreOriginal =
            nuevo.Trim();

        await VfsService.GuardarArchivosAsync(
            _vfs.Archivos);

        await CargarAsync();
    }

    private async void BtnEliminarArchivo_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                ArchivoVfsMobile archivo)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar archivo",
                $"¿Eliminar «{archivo.NombreOriginal}»?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        VfsService.EliminarFisico(
            archivo.NombreFisico);

        _vfs.Archivos.RemoveAll(a =>
            string.Equals(
                a.Id,
                archivo.Id,
                StringComparison.OrdinalIgnoreCase));

        await VfsService.GuardarArchivosAsync(
            _vfs.Archivos);

        await CargarAsync();
    }
}
