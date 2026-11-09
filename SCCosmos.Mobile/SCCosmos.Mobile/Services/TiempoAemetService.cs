using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;

// =============================================================================
// TIEMPO DE AEMET EN ANDROID
// =============================================================================
//
// Lo de AEMET (descargas, avisos, municipios) es común con Windows:
// SincroCompartido/TiempoAemet.cs. Aquí se decide dónde se va a estar cada día
// —el salón de la congregación que se visita (de martes a domingo) o, si no,
// la última ubicación del móvil— y se avisa con notificaciones.

public static class TiempoAemetService
{
    private const string ClaveUbicacion =
        "Tiempo_Ubicacion";

    private const string ClavePermisoPedido =
        "Tiempo_PermisoUbicacionPedido";

    /// <summary>
    /// Dónde está el móvil del SC (municipio y coordenadas aproximadas), en
    /// Configuracion: se sincroniza y Windows enseña en Inicio el tiempo de ahí.
    /// </summary>
    public const string ClaveUbicacionCompartida =
        "TiempoUbicacionMovil";


    /// <summary>Por qué no se pudo saber dónde está el móvil (para el aviso de Inicio).</summary>
    public static string ProblemaUbicacion { get; private set; } = "";


    static TiempoAemetService()
    {
        TiempoAemet.CarpetaCache = FileSystem.CacheDirectory;
    }


    // =========================================================================
    // DÓNDE SE VA A ESTAR
    // =========================================================================

    /// <summary>
    /// El salón de la congregación que se visita ese día (de martes a
    /// domingo) o, si no hay visita, la última ubicación del móvil.
    /// </summary>
    public static LugarTiempo? LugarParaDia(DateTime dia) =>
        LugarVisita(dia) ?? UbicacionGuardada();


    public static bool HayVisita(DateTime dia) =>
        LugarVisita(dia) is not null;


    private static LugarTiempo? LugarVisita(DateTime dia)
    {
        // El lunes ya no se está en la congregación (la visita acaba el domingo).
        if (dia.DayOfWeek == DayOfWeek.Monday)
        {
            return null;
        }

        DateTime martes =
            dia.Date.AddDays(-(((int)dia.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7));

        var visita =
            DatabaseHelper.ObtenerVisitaParaTiempo(martes);

        if (visita is null)
        {
            return null;
        }

        string? ine =
            TiempoAemet.BuscarIne(visita.Value.Municipio, visita.Value.Provincia);

        return ine is null
            ? null
            : new LugarTiempo(
                ine,
                TiempoAemet.NombreMunicipio(ine, visita.Value.Municipio),
                "Visita a " + visita.Value.Congregacion,
                visita.Value.Latitud,
                visita.Value.Longitud);
    }


    private static LugarTiempo? UbicacionGuardada()
    {
        string[] partes =
            Preferences.Default.Get(ClaveUbicacion, "").Split('|');

        if (partes.Length < 4
            || !double.TryParse(partes[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
            || !double.TryParse(partes[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud))
        {
            return null;
        }

        return new LugarTiempo(
            partes[0],
            partes[1],
            "Tu ubicación",
            latitud,
            longitud);
    }


    /// <summary>
    /// Guarda dónde está el móvil (municipio y coordenadas). Se llama desde
    /// Inicio, en primer plano: el widget y la revisión en segundo plano usan
    /// lo guardado. El permiso se pide una sola vez.
    /// </summary>
    public static async Task<bool> ActualizarUbicacionAsync(bool pedirPermiso)
    {
        ProblemaUbicacion = "";

        try
        {
            PermissionStatus estado =
                await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (estado != PermissionStatus.Granted
                && pedirPermiso
                && !Preferences.Default.Get(ClavePermisoPedido, false))
            {
                Preferences.Default.Set(ClavePermisoPedido, true);

                estado =
                    await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (estado != PermissionStatus.Granted)
            {
                ProblemaUbicacion = "Activa la ubicación para ver el tiempo donde estás.";
                return false;
            }

            Location? posicion =
                await Geolocation.GetLastKnownLocationAsync()
                ?? await Geolocation.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(10)));

            if (posicion is null)
            {
                ProblemaUbicacion = "No se ha podido saber dónde estás.";
                return false;
            }

            Placemark? sitio =
                (await Geocoding.GetPlacemarksAsync(posicion.Latitude, posicion.Longitude))
                ?.FirstOrDefault();

            string? ine =
                await Task.Run(() =>
                    TiempoAemet.BuscarIne(sitio?.Locality, sitio?.SubAdminArea)
                    ?? TiempoAemet.BuscarIne(sitio?.SubLocality, sitio?.SubAdminArea));

            if (ine is null)
            {
                ProblemaUbicacion = "No se ha encontrado tu municipio (hace falta conexión).";
                return false;
            }

            string nombre = TiempoAemet.NombreMunicipio(ine, sitio?.Locality ?? "");
            string latitud = posicion.Latitude.ToString("0.###", CultureInfo.InvariantCulture);
            string longitud = posicion.Longitude.ToString("0.###", CultureInfo.InvariantCulture);

            Preferences.Default.Set(
                ClaveUbicacion,
                string.Join("|", ine, nombre, latitud, longitud));

            await Task.Run(() => CompartirUbicacion(ine, nombre, latitud, longitud));

            return true;
        }
        catch
        {
            ProblemaUbicacion = "No se ha podido saber dónde estás.";
            return false;
        }
    }


    // Solo el móvil del SC, y solo al cambiar de municipio (no llena la sincronización).
    private static void CompartirUbicacion(
        string ine,
        string nombre,
        string latitud,
        string longitud)
    {
        if (!PerfilAccesoService.EsHombre)
        {
            return;
        }

        try
        {
            string anterior =
                DatabaseHelper.ObtenerConfiguracion(ClaveUbicacionCompartida);

            if (!anterior.StartsWith(ine + "|", StringComparison.Ordinal))
            {
                DatabaseHelper.GuardarConfiguracion(
                    ClaveUbicacionCompartida,
                    string.Join(
                        "|",
                        ine,
                        nombre,
                        latitud,
                        longitud,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
        }
    }


    // =========================================================================
    // PREVISIÓN
    // =========================================================================

    public static async Task<TiempoDia?> ObtenerAsync(
        DateTime dia,
        CancellationToken ct)
    {
        LugarTiempo? lugar = LugarParaDia(dia);

        return lugar is null
            ? null
            : await TiempoAemet.ObtenerAsync(lugar, dia, soloCache: false, ct);
    }


    /// <summary>Sin red: lo último descargado (para el widget).</summary>
    public static TiempoDia? ObtenerDeCache(DateTime dia)
    {
        try
        {
            LugarTiempo? lugar = LugarParaDia(dia);

            return lugar is null
                ? null
                : TiempoAemet.ObtenerAsync(lugar, dia, soloCache: true, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Descarga la previsión de hoy, mañana y pasado en el sitio de cada día y
    /// avisa (notificación) de los avisos nuevos. Cada 2 horas, aunque la app
    /// esté cerrada.
    /// </summary>
    public static async Task ActualizarEnSegundoPlanoAsync(CancellationToken ct)
    {
        var avisos = new List<(AvisoTiempo Aviso, LugarTiempo Lugar)>();

        for (int i = 0; i < 3; i++)
        {
            DateTime dia = DateTime.Today.AddDays(i);

            LugarTiempo? lugar = LugarParaDia(dia);

            if (lugar is null)
            {
                continue;
            }

            TiempoDia tiempo =
                await TiempoAemet.ObtenerAsync(lugar, dia, soloCache: false, ct);

            avisos.AddRange(tiempo.Avisos.Select(a => (a, lugar)));
        }

        TiempoAemet.BorrarAvisosViejos();

        SCCosmos.Mobile.Platforms.Android.TiempoAvisosAndroid.Notificar(avisos);
    }
}
