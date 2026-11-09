using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;

public record ResultadoSincronizacion(
    bool Correcto,
    string Mensaje,
    bool Conflicto = false);

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

            // La base local acaba de ser sustituida: hay que descartar las
            // conexiones que SQLite tenía en su pool, porque apuntan al
            // archivo anterior (ya borrado). Si no, las siguientes
            // escrituras podían fallar con "attempt to write a readonly
            // database".
            SqliteConnection.ClearAllPools();

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

    private static void BorrarSiExiste(
        string ruta)
    {
        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }
    }
}