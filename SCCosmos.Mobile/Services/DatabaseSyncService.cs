using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;

public record ResultadoSincronizacion(
    bool Correcto,
    string Mensaje);

public static class DatabaseSyncService
{
    private const string ClaveHash =
        "SCCosmos_UltimoHashSincronizado";

    private const string ClaveUltimaSincronizacion =
        "SCCosmos_UltimaSincronizacion";

    public static string UltimaSincronizacion =>
        Preferences.Default.Get(
            ClaveUltimaSincronizacion,
            "");

    public static async Task<ResultadoSincronizacion>
        ImportarAsync()
    {
        try
        {
            if (!SyncFolderService.EstaConfigurada)
            {
                return new ResultadoSincronizacion(
                    false,
                    "No hay carpeta sincronizada configurada.");
            }

            string temporal =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_importando.db");

            string consolidada =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_consolidada.db");

            BorrarSiExiste(temporal);
            BorrarSiExiste(consolidada);

            // Comprobamos que el archivo no cambie
            // mientras lo estamos importando.
            string hashAntes =
                SyncFolderService.CalcularHashBaseDatos();

            await SyncFolderService
                .CopiarBaseDatosALocalAsync(
                    temporal);

            string hashDespues =
                SyncFolderService.CalcularHashBaseDatos();

            if (!string.Equals(
                    hashAntes,
                    hashDespues,
                    StringComparison.OrdinalIgnoreCase))
            {
                BorrarSiExiste(temporal);

                return new ResultadoSincronizacion(
                    false,
                    "La base de datos cambió mientras se estaba copiando. " +
                    "Espera a que termine la sincronización del PC y vuelve a intentarlo.");
            }

            // Comprobar integridad.
            using (var conexion =
                   new SqliteConnection(
                       $"Data Source={temporal};Mode=ReadOnly"))
            {
                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText =
                    "PRAGMA quick_check;";

                string resultado =
                    cmd.ExecuteScalar()?.ToString()
                    ?? "";

                if (!resultado.Equals(
                        "ok",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"La base no superó quick_check: {resultado}");
                }
            }

            // Crear una copia SQLite limpia.
            using (var origen =
                   new SqliteConnection(
                       $"Data Source={temporal};Mode=ReadOnly"))
            using (var destino =
                   new SqliteConnection(
                       $"Data Source={consolidada}"))
            {
                origen.Open();
                destino.Open();

                origen.BackupDatabase(
                    destino);
            }

            BorrarSiExiste(
                DatabasePaths.RutaLocal);

            File.Move(
                consolidada,
                DatabasePaths.RutaLocal);

            BorrarSiExiste(temporal);

            Preferences.Default.Set(
                ClaveHash,
                hashDespues);

            Preferences.Default.Set(
                ClaveUltimaSincronizacion,
                DateTime.Now.ToString(
                    "dd/MM/yyyy HH:mm:ss"));

            return new ResultadoSincronizacion(
                true,
                "Base de datos importada correctamente.");
        }
        catch (Exception ex)
        {
            return new ResultadoSincronizacion(
                false,
                $"Error al importar la base de datos: {ex.Message}");
        }
    }

    public static async Task<ResultadoSincronizacion>
    ExportarAsync()
    {
        try
        {
            if (!SyncFolderService.EstaConfigurada)
            {
                return new ResultadoSincronizacion(
                    false,
                    "No hay carpeta sincronizada configurada.");
            }

            if (!File.Exists(DatabasePaths.RutaLocal))
            {
                return new ResultadoSincronizacion(
                    false,
                    "Todavía no existe una base de datos local.");
            }

            // =====================================================
            // 1. Comprobar que el PC no ha cambiado la BD
            // =====================================================

            string hashAnterior =
                Preferences.Default.Get(
                    ClaveHash,
                    "");

            if (string.IsNullOrWhiteSpace(hashAnterior))
            {
                return new ResultadoSincronizacion(
                    false,
                    "Primero debes actualizar desde la carpeta sincronizada.");
            }

            string hashActualExterno =
                SyncFolderService.CalcularHashBaseDatos();

            if (!string.Equals(
                    hashAnterior,
                    hashActualExterno,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ResultadoSincronizacion(
                    false,
                    "La base de datos sincronizada ha cambiado desde la última actualización. " +
                    "Puede haber cambios realizados desde el PC. " +
                    "Actualiza primero desde la carpeta para evitar perder información.");
            }

            // =====================================================
            // 2. Crear instantánea SQLite consistente
            // =====================================================

            string temporal =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_exportando.db");

            BorrarSiExiste(temporal);

            using (var origen =
                   new SqliteConnection(
                       $"Data Source={DatabasePaths.RutaLocal};Mode=ReadOnly"))
            using (var destino =
                   new SqliteConnection(
                       $"Data Source={temporal}"))
            {
                origen.Open();
                destino.Open();

                origen.BackupDatabase(destino);
            }

            // =====================================================
            // 3. Comprobar la copia
            // =====================================================

            using (var conexion =
                   new SqliteConnection(
                       $"Data Source={temporal};Mode=ReadOnly"))
            {
                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText =
                    "PRAGMA quick_check;";

                string resultado =
                    cmd.ExecuteScalar()?.ToString()
                    ?? "";

                if (!resultado.Equals(
                        "ok",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"La copia a exportar no superó quick_check: {resultado}");
                }
            }

            // =====================================================
            // 4. Escribir mediante ContentResolver
            // =====================================================

            await SyncFolderService
                .CopiarLocalABaseDatosAsync(
                    temporal);

            BorrarSiExiste(temporal);

            // =====================================================
            // 5. Registrar el nuevo estado sincronizado
            // =====================================================

            string nuevoHash =
                SyncFolderService.CalcularHashBaseDatos();

            Preferences.Default.Set(
                ClaveHash,
                nuevoHash);

            Preferences.Default.Set(
                ClaveUltimaSincronizacion,
                DateTime.Now.ToString(
                    "dd/MM/yyyy HH:mm:ss"));

            return new ResultadoSincronizacion(
                true,
                "Los cambios del móvil se han guardado correctamente en la carpeta sincronizada.");
        }
        catch (Exception ex)
        {
            return new ResultadoSincronizacion(
                false,
                $"Error al exportar la base de datos: {ex.Message}");
        }
    }

    private static void BorrarSiExiste(
        string ruta)
    {
        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }
    }
}