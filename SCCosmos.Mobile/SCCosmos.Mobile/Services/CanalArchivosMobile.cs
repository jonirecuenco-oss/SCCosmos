using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;

// =============================================================
// CANAL POR CARPETA EN ANDROID
//
// Los archivos de cambios viven en la subcarpeta «Sincro» de la
// carpeta compartida que el usuario ya sincroniza. Cada dispositivo
// escribe solo su propio archivo (nunca hay conflictos) y los
// cambios van cifrados con la clave de sincronización.
// =============================================================

public sealed class CanalArchivosMobile
    : ICanalArchivosSincro
{
    private const string Subcarpeta =
        "Sincro";


    public CanalArchivosMobile()
    {
        // La subcarpeta debe existir (se crea si falta).
        try
        {
            SyncFolderService.AsegurarCarpetaRaiz(
                Subcarpeta);
        }
        catch
        {
        }
    }


    public Task<List<string>> ListarAsync(
        string prefijo)
    {
        var lista =
            new List<string>();

        try
        {
            foreach (string nombre in
                     SyncFolderService.ListarNombres(
                         Subcarpeta))
            {
                if (nombre.StartsWith(
                        prefijo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    lista.Add(
                        nombre);
                }
            }
        }
        catch
        {
        }

        return Task.FromResult(
            lista);
    }


    public async Task<byte[]?> LeerAsync(
        string nombre)
    {
        try
        {
            byte[] bytes =
                await SyncFolderService
                    .LeerArchivoAsync(
                        Subcarpeta,
                        nombre);

            return bytes.Length == 0
                ? null
                : bytes;
        }
        catch
        {
            return null;
        }
    }


    public async Task<bool> EscribirAsync(
        string nombre,
        IReadOnlyList<string> lineas)
    {
        try
        {
            return await SyncFolderService
                .EscribirBytesAsync(
                    Subcarpeta,
                    nombre,
                    System.Text.Encoding.UTF8
                        .GetBytes(
                            string.Join(
                                "\n",
                                lineas)
                            + "\n"));
        }
        catch
        {
            return false;
        }
    }


    public async Task<bool> AgregarAsync(
        string nombre,
        IReadOnlyList<string> lineas)
    {
        try
        {
            byte[] actuales =
                await SyncFolderService
                    .LeerArchivoAsync(
                        Subcarpeta,
                        nombre);

            string texto =
                actuales.Length == 0
                    ? ""
                    : System.Text.Encoding.UTF8
                        .GetString(
                            actuales);

            texto +=
                string.Join(
                    "\n",
                    lineas)
                + "\n";

            return await SyncFolderService
                .EscribirBytesAsync(
                    Subcarpeta,
                    nombre,
                    System.Text.Encoding.UTF8
                        .GetBytes(
                            texto));
        }
        catch
        {
            return false;
        }
    }
}
