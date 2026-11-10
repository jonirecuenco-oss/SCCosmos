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
    /// avisa (notificación) de los avisos nuevos. Cada 30 minutos, aunque la
    /// app esté cerrada.
    /// </summary>
    public static async Task ActualizarEnSegundoPlanoAsync(CancellationToken ct)
    {
        // Primero los avisos rojos del circuito: así, si uno es también donde
        // vas a estar, se notifica una sola vez y con «Marcar como leída».
        try
        {
            await RevisarAvisosRojosAsync(ct);
        }
        catch
        {
        }

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


    // =========================================================================
    // AVISOS ROJOS EN EL CIRCUITO
    // =========================================================================

    private const string ClaveAvisosRojos =
        "Tiempo_AvisosRojos";

    /// <summary>Tipo de las alertas de la app que son avisos rojos de AEMET.</summary>
    public const string TipoAlertaRojo =
        "TiempoRojo";


    /// <summary>Un aviso rojo de AEMET en congregaciones del circuito.</summary>
    public sealed record AvisoRojo(
        string Identidad,
        string Titulo,
        string Congregaciones,
        DateTimeOffset Inicio,
        DateTimeOffset Fin,
        string Descripcion)
    {
        /// <summary>La del aviso de AEMET (la identidad lleva delante «TiempoRojo|»).</summary>
        public string Clave =>
            Identidad.StartsWith(TipoAlertaRojo + "|", StringComparison.Ordinal)
                ? Identidad[(TipoAlertaRojo.Length + 1)..]
                : Identidad;

        /// <summary>«Mérida - Centro, Mérida - Nueva Ciudad · hoy, 12:00–23:59».</summary>
        public string Texto =>
            $"{Congregaciones} · {new AvisoTiempo { Inicio = Inicio, Fin = Fin }.Periodo}";
    }


    /// <summary>Los avisos rojos de la última revisión que aún no han terminado.</summary>
    public static List<AvisoRojo> AvisosRojosVigentes()
    {
        var lista = new List<AvisoRojo>();

        foreach (string linea in Preferences.Default.Get(ClaveAvisosRojos, "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] c = linea.Split('\t');

            if (c.Length >= 6
                && long.TryParse(c[3], out long inicio)
                && long.TryParse(c[4], out long fin)
                && fin > DateTimeOffset.UtcNow.UtcTicks)
            {
                lista.Add(new AvisoRojo(
                    c[0],
                    c[1],
                    c[2],
                    new DateTimeOffset(inicio, TimeSpan.Zero),
                    new DateTimeOffset(fin, TimeSpan.Zero),
                    c[5]));
            }
        }

        return lista;
    }


    /// <summary>
    /// Mira si hay avisos rojos de AEMET en alguna congregación del circuito
    /// (cada 30 minutos con la revisión del tiempo, y al abrir Circuito). Se
    /// guardan como alertas de la app, que se marcan como leídas, y de los
    /// nuevos se avisa con una notificación. Solo en el móvil del SC.
    /// </summary>
    public static async Task RevisarAvisosRojosAsync(CancellationToken ct)
    {
        if (!PerfilAccesoService.EsHombre)
        {
            return;
        }

        // Cada sitio es el salón de una congregación o un pueblo de su
        // territorio (con su nombre; "" en el salón).
        var nombres = new List<string>();
        var pueblos = new List<string>();
        var sitios = new List<LugarTiempo>();

        foreach (var congregacion in DatabaseHelper.ObtenerCongregacionesParaTiempo())
        {
            if (TiempoAemet.LugarDe(congregacion.Municipio, congregacion.Provincia, congregacion.Latitud, congregacion.Longitud, congregacion.Nombre) is LugarTiempo lugar)
            {
                nombres.Add(congregacion.Nombre);
                pueblos.Add("");
                sitios.Add(lugar);
            }

            foreach (PuebloTerritorio pueblo in DatabaseHelper.ObtenerPueblosTerritorio(congregacion.Codigo))
            {
                nombres.Add(congregacion.Nombre);
                pueblos.Add(pueblo.Nombre);
                sitios.Add(new LugarTiempo(pueblo.Ine, pueblo.Nombre, congregacion.Nombre, pueblo.Latitud, pueblo.Longitud));
            }
        }

        if (sitios.Count == 0)
        {
            return;
        }

        List<(AvisoTiempo Aviso, List<int> Sitios)>? rojos =
            await TiempoAemet.AvisosRojosAsync(sitios, ct);

        // Sin red: se queda lo de la última vez.
        if (rojos is null)
        {
            return;
        }

        static string Limpio(string texto) =>
            texto.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

        // «Mérida - Centro, Navalmoral de la Mata (Talayuela)»: entre
        // paréntesis los pueblos, si el aviso no llega a su salón.
        string Afectados(List<int> indices) =>
            string.Join(
                ", ",
                indices
                    .GroupBy(i => nombres[i])
                    .Select(g => g.Any(i => pueblos[i].Length == 0)
                        ? g.Key
                        : $"{g.Key} ({string.Join(", ", g.Select(i => pueblos[i]).Distinct())})"));

        List<AvisoRojo> lista =
            rojos
                .Select(r => new AvisoRojo(
                    TipoAlertaRojo + "|" + Limpio(r.Aviso.Clave),
                    Limpio(r.Aviso.Titulo),
                    Limpio(Afectados(r.Sitios)),
                    r.Aviso.Inicio,
                    r.Aviso.Fin,
                    Limpio(r.Aviso.Descripcion)))
                .ToList();

        Preferences.Default.Set(
            ClaveAvisosRojos,
            string.Join(
                "\n",
                lista.Select(a => string.Join(
                    "\t",
                    a.Identidad,
                    a.Titulo,
                    a.Congregaciones,
                    a.Inicio.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    a.Fin.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    a.Descripcion))));

        SCCosmos.Mobile.Platforms.Android.TiempoAvisosAndroid.NotificarRojos(lista);
    }
}
