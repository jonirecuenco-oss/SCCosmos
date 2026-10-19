using Microsoft.Data.Sqlite;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;

// =============================================================
// SINCRONIZACIÓN ENTRE DISPOSITIVOS (MÓVIL)
//
// Arranca el motor de cambios compartido con Windows y guarda la
// configuración en AppData. La primera vez adopta la base publicada
// en la carpeta (sistema antiguo) para partir del mismo estado que
// el PC, y a partir de ahí ya no se usa la carpeta para la base.
// =============================================================

public static class SincronizacionMobile
{
    private const string ClaveMigrado =
        "SCCosmos_MigradoASincro";

    private static SincroConfiguracion? _config;
    private static SincroAlmacen? _almacen;
    private static ServicioSincro? _servicio;


    private static string RutaConfig =>
        Path.Combine(
            FileSystem.AppDataDirectory,
            "sincro.json");


    /// <summary>
    /// El sistema nuevo se ha hecho cargo de la sincronización de la
    /// base: el sistema antiguo de carpeta ya no debe tocar la base.
    /// </summary>
    public static bool NuevaSincroActiva
    {
        get;
        private set;
    }


    public static bool Activa =>
        _config is { Activo: true }
        &&
        _servicio is not null;


    /// <summary>
    /// Cambios llegados de otro dispositivo (para refrescar las
    /// pantallas abiertas).
    /// </summary>
    public static event Action<int>? DatosRecibidos;


    public static ServicioSincro? Servicio =>
        _servicio;


    private static DateTime _ultimoVaciado =
        DateTime.MinValue;


    /// <summary>
    /// Envía ahora lo que quede pendiente (dispositivos vinculados y
    /// carpeta compartida). Se llama al salir a segundo plano o cerrar
    /// la aplicación para no perder los últimos cambios.
    /// </summary>
    public static async Task VaciarPendientesAsync(
        TimeSpan? limite = null)
    {
        ServicioSincro? servicio =
            _servicio;

        if (servicio is null
            || _config is not { Activo: true })
        {
            return;
        }

        try
        {
            await servicio.VaciarPendientesAsync(
                limite);

            _ultimoVaciado =
                DateTime.UtcNow;
        }
        catch
        {
        }
    }


    /// <summary>
    /// Igual que VaciarPendientesAsync, pero evita repetirlo si se acaba
    /// de hacer (por ejemplo, OnSleep y OnStop seguidos).
    /// </summary>
    public static Task VaciarPendientesSiHaceFaltaAsync()
    {
        if ((DateTime.UtcNow - _ultimoVaciado)
            .TotalSeconds < 2)
        {
            return Task.CompletedTask;
        }

        return VaciarPendientesAsync();
    }


    // =========================================================
    // VALORES PARA LA PANTALLA
    // =========================================================

    public static bool ConfiguracionActiva
    {
        get => _config?.Activo ?? true;

        set
        {
            if (_config is not null)
            {
                _config.Activo = value;
            }
        }
    }


    public static string NombreDispositivo
    {
        get => _config?.NombreDispositivo ?? "";

        set
        {
            if (_config is not null)
            {
                _config.NombreDispositivo = value ?? "";
            }
        }
    }


    public static string Clave
    {
        get => _config?.Clave ?? "";

        set
        {
            if (_config is not null)
            {
                _config.Clave = (value ?? "").Trim();
            }
        }
    }


    public static string DireccionesTexto
    {
        get => _config is null
            ? ""
            : string.Join(
                Environment.NewLine,
                _config.Direcciones);

        set
        {
            if (_config is null)
            {
                return;
            }

            _config.Direcciones =
                (value ?? "")
                    .Split(
                        new[] { '\r', '\n', ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
        }
    }


    public static string EstadoTexto =>
        _servicio?.UltimoEstado ?? "";


    public static string DispositivosTexto
    {
        get
        {
            if (_servicio is null)
            {
                return "—";
            }

            var dispositivos =
                _servicio.ObtenerDispositivos();

            if (dispositivos.Count == 0)
            {
                return "Ninguno detectado todavía.";
            }

            return string.Join(
                Environment.NewLine,
                dispositivos.Select(d =>
                    (d.Conectado ? "🟢 " : "⚪ ")
                    + d.Nombre
                    + (string.IsNullOrWhiteSpace(d.Direccion)
                        ? ""
                        : "  (" + d.Direccion + ")")));
        }
    }


    // =========================================================
    // ARRANCAR
    // =========================================================

    public static void Iniciar()
    {
        if (_config is not null)
        {
            return;
        }

        NuevaSincroActiva =
            true;

        _ = TareaInicioAsync();
    }


    private static async Task TareaInicioAsync()
    {
        try
        {
            string nombrePredeterminado =
                "Móvil " +
                (DeviceInfo.Current.Model ?? "");

            _config =
                SincroConfiguracion.Cargar(
                    RutaConfig,
                    nombrePredeterminado);

            _config.Guardar(
                RutaConfig);

            // ---- Migración única desde el sistema de carpeta ----

            if (!Preferences.Default.Get(
                    ClaveMigrado,
                    false))
            {
                await MigrarDesdeCarpetaAsync();
            }

            // ---- Motor ----

            _almacen =
                new SincroAlmacen(
                    DatabasePaths.RutaLocal,
                    _config.IdDispositivo);

            _almacen.Asegurar();

            _servicio =
                CrearServicio();

            if (_config.Activo)
            {
                _servicio.Iniciar();
            }
        }
        catch
        {
            // La aplicación sigue funcionando sin sincronización.
        }
    }


    /// <summary>
    /// Una sola vez: importa la última base publicada en la carpeta
    /// compartida (el sistema antiguo) y deja el registro de cambios
    /// nuevo limpio y con el Id de este dispositivo.
    /// </summary>
    private static async Task MigrarDesdeCarpetaAsync()
    {
        try
        {
            if (SyncFolderService.EstaConfigurada
                &&
                SyncFolderService.ExisteBaseDatos())
            {
                await DatabaseSyncService
                    .ImportarAsync();
            }
        }
        catch
        {
        }

        try
        {
            using (var conexion =
                   new SqliteConnection(
                       $"Data Source={DatabasePaths.RutaLocal};Pooling=False"))
            {
                conexion.Open();

                foreach (string sql in
                         new[]
                         {
                             "DELETE FROM SCCosmos_Cambios;",
                             "UPDATE SCCosmos_SincroContador SET Valor = 0;",
                             "DELETE FROM SCCosmos_SincroPares;",
                             "DELETE FROM SCCosmos_Silencio;"
                         })
                {
                    try
                    {
                        using var cmd =
                            conexion.CreateCommand();

                        cmd.CommandText =
                            sql;

                        cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        // Los disparadores deben llevar el Id de ESTE dispositivo
        // (la base importada traía los del PC).
        try
        {
            new SincroAlmacen(
                    DatabasePaths.RutaLocal,
                    _config!.IdDispositivo)
                .Asegurar();
        }
        catch
        {
        }

        Preferences.Default.Set(
            ClaveMigrado,
            true);

        Avisar(
            "Base adoptada desde la carpeta. " +
            "A partir de ahora la sincronización es directa " +
            "entre dispositivos.");
    }


    // =========================================================
    // APLICAR / DETENER
    // =========================================================

    public static void Aplicar()
    {
        try
        {
            _config?.Guardar(
                RutaConfig);

            _servicio?.Detener();

            _servicio =
                null;

            if (_almacen is null
                &&
                _config is not null)
            {
                _almacen =
                    new SincroAlmacen(
                        DatabasePaths.RutaLocal,
                        _config.IdDispositivo);

                _almacen.Asegurar();
            }

            if (_config is not null
                &&
                _almacen is not null)
            {
                _servicio =
                    CrearServicio();

                if (_config.Activo)
                {
                    _servicio.Iniciar();
                }
            }
        }
        catch
        {
        }
    }


    // =========================================================
    // DISPOSITIVOS (LISTA CON CASILLA)
    // =========================================================

    /// <summary>
    /// Dispositivos conocidos, para pintarlos en Configuración con su
    /// casilla de sincronización.
    /// </summary>
    public static List<DispositivoSyncMobile> ObtenerDispositivosVista()
    {
        var lista =
            new List<DispositivoSyncMobile>();

        try
        {
            ServicioSincro? servicio =
                _servicio;

            if (servicio is null)
            {
                return lista;
            }

            foreach (InfoDispositivoSincro dispositivo in
                     servicio.ObtenerDispositivos())
            {
                lista.Add(
                    new DispositivoSyncMobile
                    {
                        Id =
                            dispositivo.Id,
                        Nombre =
                            dispositivo.Nombre,
                        Detalle =
                            (dispositivo.Conectado
                                ? "Conectado"
                                : "Visible")
                            + (string.IsNullOrWhiteSpace(
                                    dispositivo.Direccion)
                                ? ""
                                : " · " + dispositivo.Direccion),
                        Habilitado =
                            _config?.EstaHabilitado(
                                dispositivo.Id)
                            ?? true
                    });
            }
        }
        catch
        {
        }

        return lista;
    }


    /// <summary>
    /// Marca o desmarca un dispositivo: si se desmarca, no se
    /// sincroniza con él y se corta la conexión.
    /// </summary>
    public static void EstablecerHabilitado(
        string id,
        string nombre,
        bool habilitado)
    {
        try
        {
            if (_config is null
                || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            if (_config.EstaHabilitado(id)
                == habilitado)
            {
                return;
            }

            _config.Deshabilitados.RemoveAll(
                x => string.Equals(
                    x,
                    id,
                    StringComparison.OrdinalIgnoreCase));

            if (!habilitado)
            {
                _config.Deshabilitados.Add(
                    id);

                _servicio?.Desconectar(
                    id);
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                try
                {
                    nombre =
                        _servicio
                            ?.ObtenerDispositivos()
                            .FirstOrDefault(
                                d => string.Equals(
                                    d.Id,
                                    id,
                                    StringComparison.OrdinalIgnoreCase))
                            ?.Nombre
                        ?? "";
                }
                catch
                {
                }
            }

            string etiqueta =
                string.IsNullOrWhiteSpace(nombre)
                    ? id
                    : nombre;

            DatabaseHelper.AnotarActividadMobile(
                "Configuración",
                habilitado
                    ? $"Volvió a marcar la sincronización con «{etiqueta}»"
                    : $"Desmarcó la sincronización con «{etiqueta}»");

            _config.Guardar(
                RutaConfig);
        }
        catch
        {
        }
    }


    // =========================================================
    // CREAR EL SERVICIO (CON CANAL POR CARPETA)
    // =========================================================

    private static ServicioSincro CrearServicio()
    {
        ICanalArchivosSincro? canalArchivos =
            null;

        try
        {
            if (SyncFolderService.EstaConfigurada)
            {
                canalArchivos =
                    new CanalArchivosMobile();
            }
        }
        catch
        {
        }

        var servicio =
            new ServicioSincro(
                _config!,
                _almacen!,
                canalArchivos);

        try
        {
            servicio.DatosRecibidos +=
                n => DatosRecibidos?.Invoke(n);
        }
        catch
        {
        }

        return servicio;
    }


    public static void Detener()
    {
        try
        {
            _servicio?.Detener();
        }
        catch
        {
        }

        _servicio =
            null;
    }


    // =========================================================
    // AVISOS
    // =========================================================

    public static event Action<string>? EstadoCambiado;


    private static void Avisar(
        string mensaje)
    {
        try
        {
            EstadoCambiado?.Invoke(
                mensaje);
        }
        catch
        {
        }
    }
}


/// <summary>
/// Fila de la lista de dispositivos de configuración.
/// </summary>
public sealed class DispositivoSyncMobile
{
    public string Id
    {
        get;
        set;
    } = "";

    public string Nombre
    {
        get;
        set;
    } = "";

    public string Detalle
    {
        get;
        set;
    } = "";

    public bool Habilitado
    {
        get;
        set;
    } = true;
}
