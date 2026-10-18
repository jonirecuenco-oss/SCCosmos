using System.Security.Cryptography;

using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;


// ============================================================================
// SINCRONIZACIÓN AUTOMÁTICA DE LA BASE DE DATOS
// ============================================================================
//
// Vigila la base de datos local: cuando detecta que ha cambiado (por
// cualquier escritura hecha desde el móvil) la exporta a la carpeta
// sincronizada usando DatabaseSyncService, que conserva la protección
// contra sobrescribir cambios hechos desde el PC.

public static class AutoSyncService
{
    private const string ClaveActivo =
        "SCCosmos_AutoSyncActivo";

    private const string ClaveHashLocal =
        "SCCosmos_UltimoHashLocal";

    private const string ClaveMarcaLocal =
        "SCCosmos_UltimaMarcaLocal";

    private static bool _iniciado;
    private static bool _exportando;
    private static bool _registroAsegurado;
    private static CancellationTokenSource? _cancelacion;

    public static event Action<string>? EstadoCambiado;

    public static string UltimoEstado { get; private set; } = "";

    public static bool Activo
    {
        get => Preferences.Default.Get(
            ClaveActivo,
            true);
        set => Preferences.Default.Set(
            ClaveActivo,
            value);
    }

    public static void Iniciar()
    {
        if (_iniciado)
        {
            return;
        }

        _iniciado =
            true;

        _cancelacion =
            new CancellationTokenSource();

        // El registro de cambios (para poder fusionar sin perder nada) se
        // instala en cuanto exista la base local.
        AsegurarRegistroDeCambios();

        _ = BucleAsync(
            _cancelacion.Token);
    }


    private static void AsegurarRegistroDeCambios()
    {
        if (_registroAsegurado
            || !File.Exists(
                DatabasePaths.RutaLocal))
        {
            return;
        }

        try
        {
            FusionBaseDatosService
                .AsegurarRegistro();

            _registroAsegurado =
                true;
        }
        catch
        {
            // Se reintenta en la siguiente vuelta.
        }
    }

    public static void Detener()
    {
        _cancelacion?.Cancel();
        _cancelacion = null;
        _iniciado = false;
    }

    private static async Task BucleAsync(
        CancellationToken token)
    {
        // Pequeña espera para no competir con el arranque de la app.
        try
        {
            await Task.Delay(
                4000,
                token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        int ciclos = 0;

        while (!token.IsCancellationRequested)
        {
            ciclos++;

            AsegurarRegistroDeCambios();

            // La comprobación de "el PC dejó una base de datos nueva" es más
            // costosa (hay que calcular el hash del archivo de la carpeta),
            // así que se hace cada ~2 minutos; el envío de cambios locales se
            // revisa en cada vuelta (cada 5 s) y solo cuesta una consulta de
            // la fecha del archivo.
            if (ciclos == 1
                || ciclos % 24 == 0)
            {
                try
                {
                    await ImportarSiHayCambiosAsync();
                }
                catch
                {
                    // Silencioso: se reintentará más adelante.
                }
            }

            // Avisos de tareas y eventos: cada ~30 segundos.
            if (ciclos == 1
                || ciclos % 6 == 0)
            {
                try
                {
                    await TareasAvisoService.VerificarAsync();
                }
                catch
                {
                    // Silencioso: se reintentará en el siguiente ciclo.
                }
            }

            try
            {
                await SincronizarSiHayCambiosAsync();
            }
            catch
            {
                // Silencioso: se reintentará en el siguiente ciclo.
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Exporta automáticamente si la BD local cambió desde la última
    /// exportación. La protección de DatabaseSyncService evita pisar
    /// cambios realizados desde el PC.
    /// </summary>
    public static async Task SincronizarSiHayCambiosAsync()
    {
        if (SincronizacionMobile.NuevaSincroActiva)
        {
            // El sistema nuevo de sincronización directa se encarga.
            return;
        }

        if (!Activo
            ||
            !SyncFolderService.EstaConfigurada
            ||
            _exportando
            ||
            !File.Exists(
                DatabasePaths.RutaLocal))
        {
            return;
        }

        // Comprobación barata: si el archivo no se ha tocado desde la última
        // exportación no hay nada que enviar (así no se calcula el hash de
        // una base de 27 MB en cada vuelta del ciclo).
        long marcaActual =
            File.GetLastWriteTimeUtc(
                DatabasePaths.RutaLocal)
                .Ticks;

        long marcaExportada =
            Preferences.Default.Get(
                ClaveMarcaLocal,
                0L);

        if (marcaActual == marcaExportada)
        {
            return;
        }

        string hashLocal =
            CalcularHashLocal();

        string hashUltimoLocal =
            Preferences.Default.Get(
                ClaveHashLocal,
                "");

        if (string.Equals(
                hashLocal,
                hashUltimoLocal,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _exportando =
            true;

        try
        {
            ResultadoSincronizacion resultado =
                await DatabaseSyncService
                    .ExportarAsync();

            if (resultado.Correcto)
            {
                Preferences.Default.Set(
                    ClaveHashLocal,
                    CalcularHashLocal());

                Preferences.Default.Set(
                    ClaveMarcaLocal,
                    File.GetLastWriteTimeUtc(
                        DatabasePaths.RutaLocal)
                        .Ticks);

                Avisar(
                    "Sincronizado a las " +
                    DateTime.Now.ToString("HH:mm:ss"));
            }
            else if (resultado.Conflicto)
            {
                // Otro dispositivo cambió la carpeta mientras aquí también
                // había cambios: se combinan automáticamente.
                ResultadoFusion fusion =
                    await FusionarConCarpetaAsync();

                if (!fusion.Correcto)
                {
                    Preferences.Default.Set(
                        ClaveMarcaLocal,
                        File.GetLastWriteTimeUtc(
                            DatabasePaths.RutaLocal)
                            .Ticks);

                    Avisar(
                        "Cambios locales pendientes de enviar. " +
                        resultado.Mensaje);
                }
            }
            else
            {
                Preferences.Default.Set(
                    ClaveMarcaLocal,
                    File.GetLastWriteTimeUtc(
                        DatabasePaths.RutaLocal)
                        .Ticks);

                Avisar(
                    "Cambios locales pendientes de enviar. " +
                    resultado.Mensaje);
            }
        }
        finally
        {
            _exportando =
                false;
        }
    }

    /// <summary>
    /// Comprueba si la carpeta sincronizada tiene una base de datos más
    /// reciente y, si el móvil no tiene cambios pendientes, la importa.
    /// Se ejecuta al arrancar y también en cada ciclo de la sincronización
    /// automática, para que los cambios hechos en el PC aparezcan solos.
    /// </summary>
    public static async Task ImportarSiHayCambiosAsync()
    {
        if (SincronizacionMobile.NuevaSincroActiva)
        {
            // El sistema nuevo de sincronización directa se encarga.
            return;
        }

        if (!SyncFolderService.EstaConfigurada)
        {
            return;
        }

        // Antes de cualquier importación se combinan las copias de conflicto
        // que Syncthing haya dejado en la carpeta: contienen cambios que la
        // base principal no llegó a recibir.
        try
        {
            await FusionarCopiasConflictoAsync();
        }
        catch
        {
            // Silencioso: se reintentará en el siguiente ciclo.
        }

        try
        {
            string hashRemoto =
                SyncFolderService.CalcularHashBaseDatos();

            string hashImportado =
                Preferences.Default.Get(
                    "SCCosmos_UltimoHashSincronizado",
                    "");

            string hashLocal =
                File.Exists(
                    DatabasePaths.RutaLocal)
                    ? CalcularHashLocal()
                    : "";

            // La base local se reescribe al importar o exportar (copia
            // SQLite), así que el "móvil sin cambios" se comprueba contra el
            // último estado local sincronizado, no contra el hash del
            // archivo de la carpeta.
            string hashLocalSincronizado =
                Preferences.Default.Get(
                    ClaveHashLocal,
                    "");

            bool localSinCambios =
                string.IsNullOrEmpty(hashLocal)
                ||
                (string.IsNullOrEmpty(
                     hashLocalSincronizado)
                 && BaseLocalVacia())
                ||
                string.Equals(
                    hashLocal,
                    hashLocalSincronizado,
                    StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(
                    hashRemoto,
                    hashImportado,
                    StringComparison.OrdinalIgnoreCase)
                &&
                localSinCambios)
            {
                ResultadoSincronizacion resultado =
                    await DatabaseSyncService
                        .ImportarAsync();

                if (resultado.Correcto)
                {
                    TrasImportarCorrecto();

                    Avisar(
                        "Datos actualizados desde la carpeta " +
                        "sincronizada.");
                }
                else
                {
                    Avisar(
                        "No se pudo actualizar desde la carpeta: " +
                        resultado.Mensaje);
                }
            }
            else if (!string.Equals(
                         hashRemoto,
                         hashImportado,
                         StringComparison.OrdinalIgnoreCase)
                     &&
                     FusionBaseDatosService
                         .HayCambiosPendientes())
            {
                // Hay cambios aquí y también en la carpeta: se combinan
                // automáticamente (y se sube el resultado).
                await FusionarConCarpetaAsync();
            }
        }
        catch
        {
            // Sin conexión o carpeta no disponible: se reintenta luego.
        }
    }


    /// <summary>
    /// Combina en la base local las copias de conflicto que Syncthing haya
    /// dejado en la carpeta (sccosmos.sync-conflict-*.db) y las retira de la
    /// carpeta una vez combinadas. Devuelve cuántas se combinaron.
    /// </summary>
    public static async Task<int> FusionarCopiasConflictoAsync()
    {
        int combinadas = 0;

        if (SincronizacionMobile.NuevaSincroActiva)
        {
            // El sistema nuevo de sincronización directa se encarga.
            return combinadas;
        }

        if (!SyncFolderService.EstaConfigurada)
        {
            return combinadas;
        }

        List<string> nombres;

        try
        {
            nombres =
                SyncFolderService.ListarNombres(
                    null);
        }
        catch
        {
            return combinadas;
        }

        foreach (string nombre in nombres)
        {
            if (!EsCopiaDeConflicto(nombre))
            {
                continue;
            }

            string temporal =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_copia_conflicto.db");

            try
            {
                byte[] bytes =
                    await SyncFolderService
                        .LeerArchivoAsync(
                            null,
                            nombre);

                if (bytes.Length == 0)
                {
                    continue;
                }

                // Si la copia es exactamente lo que ya se sincronizó, no
                // aporta nada nuevo: solo se retira de la carpeta.
                string hashCopia =
                    Convert.ToHexString(
                        SHA256.HashData(
                            bytes));

                string hashSincronizado =
                    Preferences.Default.Get(
                        "SCCosmos_UltimoHashSincronizado",
                        "");

                if (!string.Equals(
                        hashCopia,
                        hashSincronizado,
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllBytes(
                        temporal,
                        bytes);

                    if (!ComprobarIntegridadSqlite(
                            temporal))
                    {
                        BorrarFichero(
                            temporal);

                        continue;
                    }

                    ResultadoFusion resultado =
                        FusionBaseDatosService
                            .FusionarCopiaDeConflicto(
                                DatabasePaths.RutaLocal,
                                temporal);

                    BorrarFichero(
                        temporal);

                    if (!resultado.Correcto)
                    {
                        // No se pudo combinar: la copia se queda en la
                        // carpeta y se reintentará más adelante.
                        continue;
                    }

                    combinadas++;

                    Avisar(
                        resultado.Mensaje);
                }

                SyncFolderService.EliminarArchivo(
                    null,
                    nombre);
            }
            catch
            {
                BorrarFichero(
                    temporal);
            }
        }

        return combinadas;
    }


    private static bool EsCopiaDeConflicto(
        string nombre)
    {
        return !string.IsNullOrWhiteSpace(nombre)
               &&
               nombre.StartsWith(
                   "sccosmos.sync-conflict",
                   StringComparison.OrdinalIgnoreCase)
               &&
               nombre.EndsWith(
                   ".db",
                   StringComparison.OrdinalIgnoreCase);
    }


    private static bool ComprobarIntegridadSqlite(
        string ruta)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    $"Data Source={ruta};Mode=ReadOnly");

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "PRAGMA quick_check;";

            string resultado =
                cmd.ExecuteScalar()?.ToString()
                ?? "";

            return resultado.Equals(
                "ok",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Importa la base de datos de la carpeta ahora mismo. La usa la página
    /// de Configuración al elegir la carpeta, para no tener que reiniciar la
    /// app. Devuelve el resultado para poder avisar al usuario.
    /// </summary>
    public static async Task<ResultadoSincronizacion> ImportarAhoraAsync()
    {
        if (SincronizacionMobile.NuevaSincroActiva)
        {
            return new ResultadoSincronizacion(
                false,
                "La base ya se sincroniza sola entre dispositivos. " +
                "La carpeta se usa para el Archivo del circuito y los " +
                "archivos, ya no para la base de datos.");
        }

        if (!SyncFolderService.EstaConfigurada)
        {
            return new ResultadoSincronizacion(
                false,
                "No hay carpeta sincronizada configurada.");
        }

        ResultadoSincronizacion resultado =
            await DatabaseSyncService.ImportarAsync();

        if (resultado.Correcto)
        {
            TrasImportarCorrecto();

            Avisar(
                "Datos actualizados desde la carpeta sincronizada.");
        }

        return resultado;
    }


    /// <summary>
    /// Resolución automática del conflicto: descarga la base de la carpeta
    /// (con los cambios del otro dispositivo), aplica encima los cambios
    /// hechos aquí y sube el resultado. Así no hay que hacer nada a mano.
    /// </summary>
    public static async Task<ResultadoFusion> FusionarConCarpetaAsync()
    {
        if (!SyncFolderService.EstaConfigurada)
        {
            return new ResultadoFusion(
                false,
                "No hay carpeta sincronizada configurada.",
                0,
                0,
                "");
        }

        try
        {
            string suya =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_carpeta.db");

            string resultado =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "sccosmos_fusion.db");

            BorrarFichero(suya);
            BorrarFichero(resultado);

            // 1. Descargar la base que ha dejado el otro dispositivo.
            await SyncFolderService
                .CopiarBaseDatosALocalAsync(
                    suya);

            // 2. Aplicar encima los cambios hechos aquí.
            ResultadoFusion fusion =
                FusionBaseDatosService.Fusionar(
                    DatabasePaths.RutaLocal,
                    suya,
                    resultado);

            BorrarFichero(suya);

            if (!fusion.Correcto)
            {
                return fusion;
            }

            // 3. La base local pasa a ser el resultado de la fusión.
            string hashCarpeta =
                SyncFolderService.CalcularHashBaseDatos();

            BorrarFichero(
                DatabasePaths.RutaLocal);

            File.Move(
                resultado,
                DatabasePaths.RutaLocal);

            SqliteConnection.ClearAllPools();

            Preferences.Default.Set(
                "SCCosmos_UltimoHashSincronizado",
                hashCarpeta);

            TrasImportarCorrecto();

            // 4. Subir el resultado.
            ResultadoSincronizacion exportacion =
                await DatabaseSyncService
                    .ExportarAsync();

            if (!exportacion.Correcto)
            {
                return new ResultadoFusion(
                    false,
                    "Se combinaron los cambios, pero no se pudo subir: " +
                    exportacion.Mensaje,
                    fusion.Aplicados,
                    fusion.Renumerados,
                    fusion.CopiaSeguridad);
            }

            Avisar(
                "Cambios combinados con la carpeta: " +
                fusion.Mensaje);

            return fusion;
        }
        catch (Exception ex)
        {
            return new ResultadoFusion(
                false,
                "No se pudieron combinar los cambios: " + ex.Message,
                0,
                0,
                "");
        }
    }


    private static void BorrarFichero(
        string ruta)
    {
        try
        {
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
        catch
        {
        }
    }


    /// <summary>
    /// ¿Hay cambios hechos en el móvil que todavía no se han enviado a la
    /// carpeta?
    /// </summary>
    public static bool HayCambiosLocalesSinSincronizar()
    {
        try
        {
            if (!File.Exists(
                    DatabasePaths.RutaLocal))
            {
                return false;
            }

            string hashLocal =
                CalcularHashLocal();

            string hashUltimoLocal =
                Preferences.Default.Get(
                    ClaveHashLocal,
                    "");

            if (string.IsNullOrEmpty(
                    hashUltimoLocal))
            {
                // Nunca se ha enviado nada a la carpeta: solo hay algo que
                // proteger si la base local ya tiene personas.
                return DatabaseHelper
                    .ObtenerPersonas()
                    .Count > 0;
            }

            return !string.Equals(
                hashLocal,
                hashUltimoLocal,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Después de sustituir la base de datos local hay que dejar los
    /// estados al día.
    /// </summary>
    private static void TrasImportarCorrecto()
    {
        Preferences.Default.Set(
            ClaveHashLocal,
            CalcularHashLocal());

        // La base local acaba de cambiar (se ha sustituido el archivo): su
        // marca de tiempo hay que refrescarla para no reenviarla al ciclo de
        // exportación.
        Preferences.Default.Set(
            ClaveMarcaLocal,
            File.GetLastWriteTimeUtc(
                DatabasePaths.RutaLocal)
                .Ticks);

        // La base importada no trae el registro de cambios: se vuelve a
        // instalar (con el registro vacío, que es lo correcto después de
        // sincronizar).
        FusionBaseDatosService.AsegurarRegistro();

#if ANDROID
        // La base de datos local acaba de ser sustituida: la revisión de
        // JW Hub guardada puede contener Id de personas de la base
        // anterior y deja de ser válida.
        SCCosmos.Mobile.Platforms.Android
            .JwHubSyncAnalysisService
            .LimpiarUltimoAnalisis();
#endif
    }

    public static void MarcarEstadoExportadoActual()
    {
        try
        {
            if (File.Exists(
                    DatabasePaths.RutaLocal))
            {
                Preferences.Default.Set(
                    ClaveHashLocal,
                    CalcularHashLocal());

                Preferences.Default.Set(
                    ClaveMarcaLocal,
                    File.GetLastWriteTimeUtc(
                        DatabasePaths.RutaLocal)
                        .Ticks);
            }
        }
        catch
        {
        }
    }

    private static string CalcularHashLocal()
    {
        using var stream =
            File.OpenRead(
                DatabasePaths.RutaLocal);

        using var sha =
            SHA256.Create();

        return Convert.ToHexString(
            sha.ComputeHash(stream));
    }


    /// <summary>
    /// ¿La base local todavía no tiene personas? (Instalación recién hecha.)
    /// </summary>
    private static bool BaseLocalVacia()    {
        try
        {
            if (!File.Exists(
                    DatabasePaths.RutaLocal))
            {
                return true;
            }

            using var conexion =
                new SqliteConnection(
                    $"Data Source={DatabasePaths.RutaLocal}");

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT COUNT(*) FROM Personas;";

            return Convert.ToInt32(
                cmd.ExecuteScalar()
                ?? 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Avisar(
        string mensaje)
    {
        UltimoEstado =
            mensaje;

        EstadoCambiado?.Invoke(
            mensaje);
    }
}
