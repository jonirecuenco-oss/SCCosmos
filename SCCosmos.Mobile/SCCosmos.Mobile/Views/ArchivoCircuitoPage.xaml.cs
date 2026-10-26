using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Archivo del circuito en Android. Muestra, igual que Windows, el árbol de
/// carpetas y archivos de cada sección (mismos JSON: Archivo_SeccionesVFS,
/// Archivo_CarpetasVFS y Archivo_FicherosVFS, y la carpeta ArchivosGuardados).
/// Las carpetas de primer nivel se guardan con ParentId/CarpetaId nulos, tal
/// y como las escribe Windows, para que ambos programas vean lo mismo.
/// </summary>
public partial class ArchivoCircuitoPage : ContentPage
{
    private const string JsonSecciones =
        "Archivo_SeccionesVFS.json";

    private const string JsonCarpetas =
        "Archivo_CarpetasVFS.json";

    private const string JsonArchivos =
        "Archivo_FicherosVFS.json";

    private const string JsonPotencial =
        "Archivo_HermanosPotencial.json";

    private const string JsonProblemas =
        "Archivo_ProblemasPendientes.json";

    private const string CarpetaFisica =
        "ArchivosGuardados";

    private string _raiz = "";

    private List<SeccionVfsMobile> _secciones =
        new();

    private List<CarpetaVfsMobile> _carpetas =
        new();

    private List<ArchivoVfsMobile> _archivos =
        new();

    /// <summary>Carpetas expandidas (por Id) para no cerrar el árbol al recargar.</summary>
    private readonly HashSet<string> _expandidas =
        new();

    private bool _cargando;

    public ArchivoCircuitoPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarAsync();
        Services.Perf.Registrar(
            "Archivo",
            reloj.ElapsedMilliseconds);
    }

    // ==================================================================
    // CARGA
    // ==================================================================

    private async Task CargarAsync()
    {
        if (_cargando)
        {
            return;
        }

        _cargando = true;

        try
        {
            if (!SyncFolderService.EstaConfigurada)
            {
                LblCarpeta.Text =
                    "No hay carpeta sincronizada configurada.";

                Lista.ItemsSource =
                    new List<SeccionVfsUi>();

                return;
            }

            _raiz =
                DatabaseHelper
                    .ObtenerConfiguracion(
                        "RutaArchivoCircuito",
                        "")
                    .Trim();

            // Los tres archivos se leen a la vez: antes se esperaba uno
            // tras otro y la lectura de la carpeta era lo más lento.
            Task<List<SeccionVfsMobile>> tareaSecciones =
                LeerJsonAsync<SeccionVfsMobile>(
                    JsonSecciones);

            Task<List<CarpetaVfsMobile>> tareaCarpetas =
                LeerJsonAsync<CarpetaVfsMobile>(
                    JsonCarpetas);

            Task<List<ArchivoVfsMobile>> tareaArchivos =
                LeerJsonAsync<ArchivoVfsMobile>(
                    JsonArchivos);

            await Task.WhenAll(
                tareaSecciones,
                tareaCarpetas,
                tareaArchivos);

            _secciones =
                tareaSecciones.Result;

            _carpetas =
                tareaCarpetas.Result;

            _archivos =
                tareaArchivos.Result;

            // Igual que WPF: si no hay secciones, crear las de por defecto.
            if (_secciones.Count == 0)
            {
                InicializarSeccionesPorDefecto();

                await GuardarSeccionesJsonAsync();
                await GuardarCarpetasJsonAsync();
                await GuardarArchivosJsonAsync();
            }

            ConstruirLista();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo del circuito",
                "No se pudo leer el archivo: " +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            _cargando = false;
        }
    }

    private async Task<List<T>> LeerJsonAsync<T>(
        string nombre)
    {
        string texto =
            await SyncFolderService
                .LeerTextoAsync(
                    _raiz,
                    nombre);

        if (string.IsNullOrWhiteSpace(texto))
        {
            return new List<T>();
        }

        try
        {
            return JsonSerializer
                       .Deserialize<List<T>>(texto)
                   ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private void InicializarSeccionesPorDefecto()
    {
        string[] nombres =
        {
            "1 - 2 - S-303 y S-2",
            "3 - Archivos judiciales",
            "4 - 5 - Oradores asambleas",
            "6 - Asuntos complejos",
            "7 - Instructores ESP",
            "8 - Organización asambleas",
            "9 - SC que comparten salón",
            "10 - SCS",
            "11 - Correspondencia EER (SKE)",
            "12 - Hermanos con potencial",
            "13 - Histórico congregaciones visitadas",
            "14 - Problemas pendientes",
            "15 - CEH",
            "16 - Último informe S-318",
            "17 - Formularios CO-14"
        };

        string[] visitas =
        {
            "Entre visitas",
            "Visita 1",
            "Visita 2",
            "Visita 3",
            "Visita 4",
            "Visita 5",
            "Visita 6"
        };

        List<string> congregaciones =
            new();

        try
        {
            congregaciones =
                DatabaseHelper
                    .ObtenerCongregacionesCompletasMobile()
                    .Select(c => c.Nombre)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
        }
        catch
        {
        }

        foreach (string nombre in nombres)
        {
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(
                    nombre,
                    @"^\d+");

            int idEspecial =
                match.Success
                    ? int.Parse(match.Value)
                    : 0;

            string nombreLimpio =
                System.Text.RegularExpressions.Regex
                    .Replace(nombre, @"^[\d\s\-]+", "")
                    .ToUpper();

            var seccion =
                new SeccionVfsMobile
                {
                    NombreLimpio = nombreLimpio,
                    IdEspecial = idEspecial
                };

            _secciones.Add(seccion);

            if (idEspecial == 1)
            {
                foreach (string cong in congregaciones)
                {
                    var carpetaCongregacion =
                        new CarpetaVfsMobile
                        {
                            SeccionId = seccion.Id,
                            ParentId = null,
                            Nombre = cong
                        };

                    _carpetas.Add(carpetaCongregacion);

                    foreach (string visita in visitas)
                    {
                        _carpetas.Add(
                            new CarpetaVfsMobile
                            {
                                SeccionId = seccion.Id,
                                ParentId = carpetaCongregacion.Id,
                                Nombre = visita
                            });
                    }
                }
            }
        }
    }

    // ==================================================================
    // CONSTRUCCIÓN DE LA LISTA Y DEL ÁRBOL
    // ==================================================================

    private void ConstruirLista()
    {
        // Igual que Windows: se respeta el orden del JSON de secciones.
        List<SeccionVfsUi> lista =
            _secciones
                .Select(CrearSeccionUi)
                .ToList();

        Lista.ItemsSource =
            lista;

        string carpeta =
            string.IsNullOrWhiteSpace(_raiz)
                ? SyncFolderService.NombreCarpeta
                : $"{SyncFolderService.NombreCarpeta}/{_raiz}";

        LblCarpeta.Text =
            $"{carpeta} · {_secciones.Count} secciones · " +
            $"{_carpetas.Count} carpetas · {_archivos.Count} archivos";
    }

    private SeccionVfsUi CrearSeccionUi(
        SeccionVfsMobile seccion)
    {
        List<CarpetaVfsMobile> carpetas =
            _carpetas
                .Where(c => string.Equals(
                    c.SeccionId,
                    seccion.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        List<ArchivoVfsMobile> archivos =
            _archivos
                .Where(a => string.Equals(
                    a.SeccionId,
                    seccion.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        var ui =
            new SeccionVfsUi
            {
                Original = seccion,
                Nombre = seccion.NombreLimpio,
                EsTablaEspecial =
                    seccion.IdEspecial is 12 or 14,
                Resumen =
                    $"{carpetas.Count} carpetas · " +
                    $"{archivos.Count} archivos"
            };

        if (seccion.IdEspecial == 10)
        {
            ui.EsScs = true;
            ui.Scs = CrearListaScs();
        }

        ui.Nodos =
            Aplanar(
                seccion,
                null,
                0,
                carpetas,
                archivos,
                ui);

        return ui;
    }

    private static string Normalizar(
        string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? ""
            : id.Trim();
    }

    private List<NodoVfsUi> Aplanar(
        SeccionVfsMobile seccion,
        string? parentId,
        int nivel,
        List<CarpetaVfsMobile> carpetas,
        List<ArchivoVfsMobile> archivos,
        SeccionVfsUi seccionUi)
    {
        var nodos =
            new List<NodoVfsUi>();

        string padre =
            Normalizar(parentId);

        foreach (
            CarpetaVfsMobile carpeta
            in carpetas
                .Where(c => Normalizar(c.ParentId) == padre)
                .OrderBy(c => c.Nombre))
        {
            bool expandida =
                _expandidas.Contains(carpeta.Id);

            nodos.Add(
                new NodoVfsUi
                {
                    EsCarpeta = true,
                    Expandida = expandida,
                    Id = carpeta.Id,
                    Nombre = carpeta.Nombre,
                    Icono = expandida ? "📂" : "📁",
                    Color = Color.FromArgb("#B9DEFF"),
                    TamanoTexto = 13,
                    Grosor = FontAttributes.Bold,
                    Sangria = new Thickness(nivel * 14, 0, 0, 0),
                    Fondo = Color.FromArgb("#1B2129"),
                    Borde = Color.FromArgb("#2E3742"),
                    Accion1 = "＋",
                    Accion2 = "⬆️",
                    Accion3 = "✏️",
                    Accion4 = "🗑️",
                    Carpeta = carpeta,
                    Seccion = seccionUi
                });

            if (expandida)
            {
                nodos.AddRange(
                    Aplanar(
                        seccion,
                        carpeta.Id,
                        nivel + 1,
                        carpetas,
                        archivos,
                        seccionUi));
            }
        }

        foreach (
            ArchivoVfsMobile archivo
            in archivos
                .Where(a => Normalizar(a.CarpetaId) == padre)
                .OrderBy(a => a.NombreOriginal))
        {
            nodos.Add(
                new NodoVfsUi
                {
                    EsArchivo = true,
                    Id = archivo.Id,
                    Nombre = archivo.NombreOriginal,
                    Icono = "📄",
                    Color = Color.FromArgb("#E4E8ED"),
                    TamanoTexto = 12.5,
                    Grosor = FontAttributes.None,
                    Sangria = new Thickness(nivel * 14, 0, 0, 0),
                    Fondo = Color.FromArgb("#12151A"),
                    Borde = Color.FromArgb("#262D36"),
                    Accion1 = "↗",
                    Accion2 = "✏️",
                    Accion3 = "🗑️",
                    Archivo = archivo,
                    Seccion = seccionUi
                });
        }

        return nodos;
    }

    private static List<LineaScsUi> CrearListaScs()
    {
        var lista =
            new List<LineaScsUi>();

        try
        {
            foreach (
                Persona persona
                in DatabaseHelper
                    .ObtenerPersonas()
                    .OrderBy(p => p.NombreMostrarTabla))
            {
                string privilegios =
                    (persona.Privilegios ?? "").ToLowerInvariant();

                string nombramiento =
                    (persona.Nombramiento ?? "").ToLowerInvariant();

                bool esScs =
                    privilegios.Contains("scs")
                    || privilegios.Contains("sustituto")
                    || nombramiento.Contains("scs")
                    || nombramiento.Contains("sustituto");

                if (!esScs)
                {
                    continue;
                }

                string contacto =
                    !string.IsNullOrWhiteSpace(persona.Telefono)
                        ? persona.Telefono
                        : persona.EmailJW;

                lista.Add(
                    new LineaScsUi
                    {
                        Linea =
                            $"• {persona.NombreMostrarTabla}" +
                            (string.IsNullOrWhiteSpace(
                                 persona.CongregacionNombre)
                                ? ""
                                : $" — {persona.CongregacionNombre}") +
                            (string.IsNullOrWhiteSpace(contacto)
                                ? ""
                                : $" — {contacto}")
                    });
            }

            if (lista.Count == 0)
            {
                lista.Add(
                    new LineaScsUi
                    {
                        Linea = "• No hay hermanos marcados como SCS."
                    });
            }
        }
        catch
        {
        }

        return lista;
    }

    private void RefrescarSeccion(
        SeccionVfsUi seccion)
    {
        List<CarpetaVfsMobile> carpetas =
            _carpetas
                .Where(c => string.Equals(
                    c.SeccionId,
                    seccion.Original.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        List<ArchivoVfsMobile> archivos =
            _archivos
                .Where(a => string.Equals(
                    a.SeccionId,
                    seccion.Original.Id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        seccion.Resumen =
            $"{carpetas.Count} carpetas · {archivos.Count} archivos";

        seccion.Nodos =
            Aplanar(
                seccion.Original,
                null,
                0,
                carpetas,
                archivos,
                seccion);
    }

    // ==================================================================
    // GUARDADO
    // ==================================================================

    private Task GuardarSeccionesJsonAsync()
    {
        return SyncFolderService
            .EscribirTextoAsync(
                _raiz,
                JsonSecciones,
                JsonSerializer.Serialize(_secciones));
    }

    private Task GuardarCarpetasJsonAsync()
    {
        return SyncFolderService
            .EscribirTextoAsync(
                _raiz,
                JsonCarpetas,
                JsonSerializer.Serialize(_carpetas));
    }

    private Task GuardarArchivosJsonAsync()
    {
        return SyncFolderService
            .EscribirTextoAsync(
                _raiz,
                JsonArchivos,
                JsonSerializer.Serialize(_archivos));
    }

    // ==================================================================
    // ACCIONES DE SECCIÓN
    // ==================================================================

    private async void BtnActualizar_Clicked(
        object sender,
        EventArgs e)
    {
        await CargarAsync();
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

    private async void BtnNuevaSeccion_Clicked(
        object sender,
        EventArgs e)
    {
        string nombre =
            await DisplayPromptAsync(
                "Nueva sección",
                "Nombre de la sección:",
                "Crear",
                "Cancelar");

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return;
        }

        _secciones.Add(
            new SeccionVfsMobile
            {
                NombreLimpio = nombre.Trim().ToUpper(),
                IdEspecial = 0
            });

        await GuardarSeccionesJsonAsync();

        ConstruirLista();
    }

    private async void BtnRenombrarSeccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        string nuevo =
            await DisplayPromptAsync(
                "Renombrar sección",
                "Nuevo nombre:",
                "Guardar",
                "Cancelar",
                initialValue: seccion.Original.NombreLimpio);

        if (string.IsNullOrWhiteSpace(nuevo))
        {
            return;
        }

        seccion.Original.NombreLimpio =
            nuevo.Trim().ToUpper();

        await GuardarSeccionesJsonAsync();

        ConstruirLista();
    }

    private async void BtnEliminarSeccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar sección",
                $"¿Eliminar «{seccion.Original.NombreLimpio}» " +
                "y TODO su contenido?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        EliminarSeccionRecursivo(
            seccion.Original.Id);

        _secciones.Remove(
            seccion.Original);

        await GuardarSeccionesJsonAsync();
        await GuardarCarpetasJsonAsync();
        await GuardarArchivosJsonAsync();

        ConstruirLista();
    }

    private void EliminarSeccionRecursivo(
        string seccionId)
    {
        foreach (
            CarpetaVfsMobile carpeta
            in _carpetas
                .Where(c => string.Equals(
                    c.SeccionId,
                    seccionId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList())
        {
            EliminarCarpetaRecursivo(
                carpeta.Id);
        }

        foreach (
            ArchivoVfsMobile archivo
            in _archivos
                .Where(a => string.Equals(
                    a.SeccionId,
                    seccionId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList())
        {
            EliminarArchivoFisicoYRegistro(
                archivo);
        }
    }

    private void EliminarCarpetaRecursivo(
        string carpetaId)
    {
        foreach (
            CarpetaVfsMobile subcarpeta
            in _carpetas
                .Where(c => Normalizar(c.ParentId) == carpetaId)
                .ToList())
        {
            EliminarCarpetaRecursivo(
                subcarpeta.Id);
        }

        foreach (
            ArchivoVfsMobile archivo
            in _archivos
                .Where(a => Normalizar(a.CarpetaId) == carpetaId)
                .ToList())
        {
            EliminarArchivoFisicoYRegistro(
                archivo);
        }

        _carpetas.RemoveAll(
            c => c.Id == carpetaId);

        _expandidas.Remove(
            carpetaId);
    }

    private void EliminarArchivoFisicoYRegistro(
        ArchivoVfsMobile archivo)
    {
        try
        {
            VfsService.EliminarFisico(
                archivo.NombreFisico);
        }
        catch
        {
        }

        _archivos.Remove(
            archivo);
    }

    private async void BtnNuevaCarpetaSeccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        await CrearCarpetaAsync(
            seccion,
            null);
    }

    private async void BtnSubirSeccion_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        await SubirArchivoAsync(
            seccion,
            null);
    }

    // ==================================================================
    // ACCIONES DEL ÁRBOL (carpetas y archivos)
    // ==================================================================

    private void Seccion_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not SeccionVfsUi seccion)
        {
            return;
        }

        seccion.Abierta =
            !seccion.Abierta;
    }


    private void Nodo_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not NodoVfsUi nodo
            ||
            !nodo.EsCarpeta
            ||
            nodo.Seccion is null)
        {
            return;
        }

        if (!_expandidas.Remove(
                nodo.Id))
        {
            _expandidas.Add(
                nodo.Id);
        }

        RefrescarSeccion(
            nodo.Seccion);
    }

    private async void Accion1_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not NodoVfsUi nodo)
        {
            return;
        }

        if (nodo.EsCarpeta)
        {
            // Nueva subcarpeta
            await CrearCarpetaAsync(
                nodo.Seccion!,
                nodo.Carpeta!);
        }
        else
        {
            // Abrir archivo
            await AbrirArchivoAsync(
                nodo.Archivo!);
        }
    }

    private async void Accion2_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not NodoVfsUi nodo)
        {
            return;
        }

        if (nodo.EsCarpeta)
        {
            // Subir archivo dentro de esta carpeta
            await SubirArchivoAsync(
                nodo.Seccion!,
                nodo.Carpeta!);
        }
        else
        {
            // Renombrar archivo
            await RenombrarArchivoAsync(
                nodo.Archivo!,
                nodo.Seccion!);
        }
    }

    private async void Accion3_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not NodoVfsUi nodo)
        {
            return;
        }

        if (nodo.EsCarpeta)
        {
            // Renombrar carpeta
            string nuevo =
                await DisplayPromptAsync(
                    "Renombrar carpeta",
                    "Nuevo nombre:",
                    "Guardar",
                    "Cancelar",
                    initialValue: nodo.Carpeta!.Nombre);

            if (string.IsNullOrWhiteSpace(nuevo))
            {
                return;
            }

            nodo.Carpeta.Nombre =
                nuevo.Trim();

            await GuardarCarpetasJsonAsync();

            RefrescarSeccion(
                nodo.Seccion!);
        }
        else
        {
            // Eliminar archivo
            await EliminarArchivoAsync(
                nodo.Archivo!,
                nodo.Seccion!);
        }
    }

    private async void Accion4_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not NodoVfsUi nodo
            ||
            !nodo.EsCarpeta)
        {
            return;
        }

        bool eliminar =
            await DisplayAlert(
                "Eliminar carpeta",
                $"¿Eliminar «{nodo.Carpeta!.Nombre}» y todo su " +
                "contenido?",
                "Eliminar",
                "Cancelar");

        if (!eliminar)
        {
            return;
        }

        EliminarCarpetaRecursivo(
            nodo.Carpeta.Id);

        await GuardarCarpetasJsonAsync();
        await GuardarArchivosJsonAsync();

        RefrescarSeccion(
            nodo.Seccion!);
    }

    // ==================================================================
    // OPERACIONES CON CARPETAS Y ARCHIVOS
    // ==================================================================

    private async Task CrearCarpetaAsync(
        SeccionVfsUi seccion,
        CarpetaVfsMobile? padre)
    {
        string nombre =
            await DisplayPromptAsync(
                "Nueva carpeta",
                padre is null
                    ? "Nombre de la carpeta (primer nivel):"
                    : $"Nombre de la subcarpeta en «{padre.Nombre}»:",
                "Crear",
                "Cancelar");

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return;
        }

        _carpetas.Add(
            new CarpetaVfsMobile
            {
                SeccionId = seccion.Original.Id,
                ParentId = padre?.Id,
                Nombre = nombre.Trim()
            });

        if (padre is not null)
        {
            _expandidas.Add(
                padre.Id);
        }

        await GuardarCarpetasJsonAsync();

        RefrescarSeccion(
            seccion);
    }

    private async Task SubirArchivoAsync(
        SeccionVfsUi seccion,
        CarpetaVfsMobile? destino)
    {
        try
        {
            FileResult? archivo =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            destino is null
                                ? "Selecciona el archivo para la sección"
                                : $"Selecciona el archivo para «{destino.Nombre}»"
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

            // Igual que Windows: GUID con guiones + extensión.
            string nombreFisico =
                Guid.NewGuid().ToString() +
                extension;

            bool guardado =
                await VfsService.GuardarFisicoAsync(
                    nombreFisico,
                    memoria.ToArray());

            if (!guardado)
            {
                throw new IOException(
                    "Android no pudo guardar el archivo en la " +
                    "carpeta sincronizada.");
            }

            _archivos.Add(
                new ArchivoVfsMobile
                {
                    SeccionId = seccion.Original.Id,
                    CarpetaId = destino?.Id,
                    NombreOriginal = archivo.FileName,
                    NombreFisico = nombreFisico
                });

            if (destino is not null)
            {
                _expandidas.Add(
                    destino.Id);
            }

            await GuardarArchivosJsonAsync();

            RefrescarSeccion(
                seccion);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo del circuito",
                "No se pudo subir el archivo: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async Task RenombrarArchivoAsync(
        ArchivoVfsMobile archivo,
        SeccionVfsUi seccion)
    {
        string nuevo =
            await DisplayPromptAsync(
                "Renombrar archivo",
                "Nuevo nombre visible (mantén la extensión):",
                "Guardar",
                "Cancelar",
                initialValue: archivo.NombreOriginal);

        if (string.IsNullOrWhiteSpace(nuevo))
        {
            return;
        }

        archivo.NombreOriginal =
            nuevo.Trim();

        await GuardarArchivosJsonAsync();

        RefrescarSeccion(
            seccion);
    }

    private async Task EliminarArchivoAsync(
        ArchivoVfsMobile archivo,
        SeccionVfsUi seccion)
    {
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

        EliminarArchivoFisicoYRegistro(
            archivo);

        await GuardarArchivosJsonAsync();

        RefrescarSeccion(
            seccion);
    }

    private async Task AbrirArchivoAsync(
        ArchivoVfsMobile archivo)
    {
        try
        {
            byte[] contenido =
                await VfsService.LeerFisicoAsync(
                    archivo.NombreFisico);

            if (contenido.Length == 0)
            {
                await DisplayAlert(
                    "Archivo",
                    "No se pudo abrir el archivo (puede que solo " +
                    "esté en el ordenador todavía).",
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
                    new ReadOnlyFile(
                        ruta)));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo",
                "No se pudo abrir el archivo: " +
                ex.Message,
                "Aceptar");
        }
    }

    // ==================================================================
    // SECCIONES ESPECIALES (12 y 14): tabla y PDF
    // ==================================================================

    private async void BtnTablaEspecial_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        await Navigation.PushAsync(
            new ArchivoEspecialPage(
                seccion.Original.IdEspecial == 12,
                _raiz));

        await CargarAsync();
    }

    private async void BtnPdfEspecial_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                SeccionVfsUi seccion)
        {
            return;
        }

        try
        {
            bool esPotencial =
                seccion.Original.IdEspecial == 12;

            List<ItemEspecialUi> filas =
                await CrearFilasEspecialesAsync(
                    esPotencial);

            byte[] pdf =
                ArchivoEspecialPdfService.Generar(
                    seccion.Original.NombreLimpio,
                    filas);

            string nombreOriginal =
                $"Reporte_{seccion.Original.NombreLimpio}_" +
                $"{DateTime.Now:dd-MM-yyyy}.pdf";

            string nombreFisico =
                Guid.NewGuid().ToString() +
                ".pdf";

            bool guardado =
                await VfsService.GuardarFisicoAsync(
                    nombreFisico,
                    pdf);

            if (!guardado)
            {
                throw new IOException(
                    "No se pudo guardar el PDF en la carpeta " +
                    "sincronizada.");
            }

            _archivos.Add(
                new ArchivoVfsMobile
                {
                    SeccionId = seccion.Original.Id,
                    CarpetaId = null,
                    NombreOriginal = nombreOriginal,
                    NombreFisico = nombreFisico
                });

            await GuardarArchivosJsonAsync();

            RefrescarSeccion(
                seccion);

            await DisplayAlert(
                "Archivo del circuito",
                $"PDF generado y añadido a la sección:\n" +
                $"{nombreOriginal}",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Archivo del circuito",
                "No se pudo generar el PDF: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async Task<List<ItemEspecialUi>>
        CrearFilasEspecialesAsync(
            bool esPotencial)
    {
        var filas =
            new List<ItemEspecialUi>();

        if (esPotencial)
        {
            List<HermanoPotencialMobile> potenciales =
                await LeerJsonAsync<HermanoPotencialMobile>(
                    JsonPotencial);

            Dictionary<int, string> nombres =
                DatabaseHelper
                    .ObtenerPersonas()
                    .GroupBy(p => p.Id)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().NombreMostrarTabla);

            foreach (
                HermanoPotencialMobile h in potenciales)
            {
                filas.Add(
                    new ItemEspecialUi
                    {
                        Id = h.Id,
                        Titulo =
                            nombres.TryGetValue(
                                h.PersonaId,
                                out string? nombre)
                                ? nombre
                                : "Desconocido",
                        Descripcion = h.Descripcion
                    });
            }
        }
        else
        {
            List<ProblemaPendienteMobile> problemas =
                await LeerJsonAsync<ProblemaPendienteMobile>(
                    JsonProblemas);

            foreach (
                ProblemaPendienteMobile p in problemas)
            {
                filas.Add(
                    new ItemEspecialUi
                    {
                        Id = p.Id,
                        Titulo = p.Congregacion,
                        Descripcion = p.Descripcion
                    });
            }
        }

        return filas;
    }
}


// ======================================================================
// MODELOS DE INTERFAZ
// ======================================================================

public sealed class SeccionVfsUi :
    INotifyPropertyChanged
{
    public SeccionVfsMobile Original { get; set; } = new();

    public string Nombre { get; set; } = "";

    public string Resumen { get; set; } = "";

    public bool EsScs { get; set; }

    public bool EsTablaEspecial { get; set; }

    private bool _abierta = true;

    /// <summary>
    /// Cada sección se puede plegar y desplegar, también la primera que
    /// aparece abierta al entrar en la página.
    /// </summary>
    public bool Abierta
    {
        get => _abierta;
        set
        {
            if (_abierta == value)
            {
                return;
            }

            _abierta =
                value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(Abierta)));

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(IconoFlecha)));

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(MostrarResumen)));

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(MostrarScs)));

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(MostrarTablaEspecial)));
        }
    }

    public string IconoFlecha =>
        Abierta
            ? "▾"
            : "▸";

    public bool MostrarResumen =>
        Abierta;

    public bool MostrarScs =>
        EsScs
        && Abierta;

    public bool MostrarTablaEspecial =>
        EsTablaEspecial
        && Abierta;

    public List<LineaScsUi> Scs { get; set; } = new();

    private List<NodoVfsUi> _nodos =
        new();

    public List<NodoVfsUi> Nodos
    {
        get => _nodos;
        set
        {
            _nodos = value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(Nodos)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}


public sealed class LineaScsUi
{
    public string Linea { get; set; } = "";
}


public sealed class NodoVfsUi
{
    public bool EsCarpeta { get; set; }

    public bool EsArchivo { get; set; }

    public bool Expandida { get; set; }

    public string Id { get; set; } = "";

    public string Nombre { get; set; } = "";

    public string Icono { get; set; } = "";

    public Color Color { get; set; } = Colors.White;

    public double TamanoTexto { get; set; } = 13;

    public FontAttributes Grosor { get; set; } =
        FontAttributes.None;

    public Thickness Sangria { get; set; }

    public Color Fondo { get; set; } = Colors.Transparent;

    public Color Borde { get; set; } = Colors.Transparent;

    public string Accion1 { get; set; } = "";

    public string Accion2 { get; set; } = "";

    public string Accion3 { get; set; } = "";

    public string Accion4 { get; set; } = "";

    public bool TieneAccion1 =>
        !string.IsNullOrEmpty(Accion1);

    public bool TieneAccion2 =>
        !string.IsNullOrEmpty(Accion2);

    public bool TieneAccion3 =>
        !string.IsNullOrEmpty(Accion3);

    public bool TieneAccion4 =>
        !string.IsNullOrEmpty(Accion4);

    public CarpetaVfsMobile? Carpeta { get; set; }

    public ArchivoVfsMobile? Archivo { get; set; }

    public SeccionVfsUi? Seccion { get; set; }
}
