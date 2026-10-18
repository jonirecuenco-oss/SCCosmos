using System.Text.Json;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;


// ============================================================================
// ACCESO AL VFS DEL ARCHIVO (carpetas y archivos) EN LA CARPETA SINCRONIZADA
// ============================================================================
//
// Lee y guarda los tres JSON que usa WPF y gestiona el contenido físico
// de ArchivosGuardados. Permite carpetas anidadas (CarpetaVFS.ParentId).

public static class VfsService
{
    public const string JsonSecciones = "Archivo_SeccionesVFS.json";
    public const string JsonCarpetas = "Archivo_CarpetasVFS.json";
    public const string JsonArchivos = "Archivo_FicherosVFS.json";
    public const string CarpetaFisica = "ArchivosGuardados";

    public sealed class VfsContenido
    {
        public List<SeccionVfsMobile> Secciones { get; } = new();
        public List<CarpetaVfsMobile> Carpetas { get; } = new();
        public List<ArchivoVfsMobile> Archivos { get; } = new();
    }

    public static string ObtenerRaiz()
    {
        try
        {
            return Data.DatabaseHelper
                .ObtenerConfiguracion("RutaArchivoCircuito", "")
                .Trim();
        }
        catch
        {
            return "";
        }
    }


    public static async Task<VfsContenido> CargarAsync()
    {
        string raiz = ObtenerRaiz();

        var contenido = new VfsContenido();

        contenido.Secciones.AddRange(
            Deserializar<List<SeccionVfsMobile>>(
                await SyncFolderService.LeerTextoAsync(
                    raiz, JsonSecciones)));

        contenido.Carpetas.AddRange(
            Deserializar<List<CarpetaVfsMobile>>(
                await SyncFolderService.LeerTextoAsync(
                    raiz, JsonCarpetas)));

        contenido.Archivos.AddRange(
            Deserializar<List<ArchivoVfsMobile>>(
                await SyncFolderService.LeerTextoAsync(
                    raiz, JsonArchivos)));

        return contenido;
    }


    public static async Task GuardarCarpetasAsync(
        IEnumerable<CarpetaVfsMobile> carpetas)
    {
        await SyncFolderService.EscribirTextoAsync(
            ObtenerRaiz(),
            JsonCarpetas,
            JsonSerializer.Serialize(carpetas.ToList()));
    }


    public static async Task GuardarArchivosAsync(
        IEnumerable<ArchivoVfsMobile> archivos)
    {
        await SyncFolderService.EscribirTextoAsync(
            ObtenerRaiz(),
            JsonArchivos,
            JsonSerializer.Serialize(archivos.ToList()));
    }


    public static string SubcarpetaFisica()
    {
        string raiz = ObtenerRaiz();

        return string.IsNullOrWhiteSpace(raiz)
            ? CarpetaFisica
            : raiz.TrimEnd('/') + "/" + CarpetaFisica;
    }


    public static async Task<bool> GuardarFisicoAsync(
        string nombreFisico,
        byte[] contenido)
    {
        SyncFolderService.AsegurarCarpetaRaiz(CarpetaFisica);

        return await SyncFolderService.EscribirBytesAsync(
            SubcarpetaFisica(),
            nombreFisico,
            contenido);
    }


    public static bool EliminarFisico(string nombreFisico)
    {
        return SyncFolderService.EliminarArchivo(
            SubcarpetaFisica(),
            nombreFisico);
    }


    public static async Task<byte[]> LeerFisicoAsync(
        string nombreFisico)
    {
        return await SyncFolderService.LeerArchivoAsync(
            SubcarpetaFisica(),
            nombreFisico);
    }


    private static T Deserializar<T>(string texto)
        where T : new()
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(texto)
                   ?? new T();
        }
        catch
        {
            return new T();
        }
    }
}
