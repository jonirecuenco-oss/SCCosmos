using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Tiempo en coche hasta un destino (coordenadas o dirección) desde la
/// ubicación actual del dispositivo. Usa la API de Google Maps cuando hay
/// una clave configurada (tiempos con tráfico en tiempo real); si no,
/// usa servicios abiertos y gratuitos (Nominatim + OSRM) para que
/// funcione sin configurar nada.
/// </summary>
public static class TiempoViajeService
{
    private const string ClaveGoogleMaps = "GoogleMapsApiKey";
    private const string ClaveLatitud = "TiempoViajeLat";
    private const string ClaveLongitud = "TiempoViajeLng";
    private const string ClaveUbicacionFecha = "TiempoViajeFecha";

    private const int MinutosCacheTiempos = 15;
    private const int MinutosMaximosUbicacion = 360;

    private static readonly HttpClient Cliente = CrearCliente();

    private static readonly Dictionary<string, (DateTime Fecha, int Minutos)> CacheTiempos = new();
    private static readonly Dictionary<string, (double Lat, double Lng)> CacheGeocodificacion = new();

    // Destinos que acaban de fallar (geocodificación o ruta): no se
    // reintentan hasta pasado un rato, para no bloquear a los demás.
    private static readonly Dictionary<string, DateTime> CacheFallos = new();

    private const int MinutosEsperaTrasFallo = 30;

    private static bool _calculando;


    private static HttpClient CrearCliente()
    {
        var cliente = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        try
        {
            cliente.DefaultRequestHeaders.UserAgent.ParseAdd("SCCosmos/1.0 (app personal)");
        }
        catch
        {
        }

        return cliente;
    }


    // =========================================================
    // CLAVE DE GOOGLE MAPS (opcional)
    // =========================================================

    public static string ObtenerClaveGoogle() =>
        (Preferences.Get(ClaveGoogleMaps, "") ?? "").Trim();

    public static void GuardarClaveGoogle(string clave) =>
        Preferences.Set(ClaveGoogleMaps, (clave ?? "").Trim());


    // =========================================================
    // UBICACIÓN
    // =========================================================

    /// <summary>
    /// Ubicación guardada (la actualiza la app en primer plano). Devuelve
    /// null si no hay o es demasiado antigua.
    /// </summary>
    public static (double Lat, double Lng)? ObtenerUbicacionGuardada()
    {
        double lat = Preferences.Get(ClaveLatitud, 0d);
        double lng = Preferences.Get(ClaveLongitud, 0d);

        if (lat == 0 && lng == 0)
        {
            return null;
        }

        DateTime fecha = Preferences.Get(ClaveUbicacionFecha, DateTime.MinValue);

        if ((DateTime.Now - fecha).TotalMinutes > MinutosMaximosUbicacion)
        {
            return null;
        }

        return (lat, lng);
    }


    /// <summary>
    /// Refresca la ubicación guardada (llamar en primer plano). Pide el
    /// permiso la primera vez; si se deniega, no hace nada.
    /// </summary>
    public static async Task ActualizarUbicacionAsync()
    {
        try
        {
            PermissionStatus permiso =
                await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (permiso != PermissionStatus.Granted)
            {
                permiso =
                    await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (permiso != PermissionStatus.Granted)
            {
                return;
            }

            var actual = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(
                    GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(8)));

            if (actual is not null)
            {
                GuardarUbicacion(actual.Latitude, actual.Longitude);
                return;
            }

            var ultima = await Geolocation.Default.GetLastKnownLocationAsync();

            if (ultima is not null)
            {
                GuardarUbicacion(ultima.Latitude, ultima.Longitude);
            }
        }
        catch
        {
        }
    }


    private static void GuardarUbicacion(double lat, double lng)
    {
        Preferences.Set(ClaveLatitud, lat);
        Preferences.Set(ClaveLongitud, lng);
        Preferences.Set(ClaveUbicacionFecha, DateTime.Now);
    }


    // =========================================================
    // TIEMPO EN COCHE
    // =========================================================

    /// <summary>
    /// Texto listo para mostrar («🚗 18 min») si está en caché y sigue
    /// vigente; null si hay que calcularlo o no hay destino.
    /// </summary>
    public static string? ObtenerTiempoTextoCacheado(
        double lat,
        double lng,
        string direccion)
    {
        string? clave = CrearClaveDestino(lat, lng, direccion);

        if (clave is null)
        {
            return null;
        }

        if (CacheTiempos.TryGetValue(clave, out var entrada)
            && (DateTime.Now - entrada.Fecha).TotalMinutes <= MinutosCacheTiempos)
        {
            return "🚗 " + FormatearMinutos(entrada.Minutos);
        }

        return null;
    }


    public static async Task<int?> CalcularMinutosCocheAsync(
        double lat,
        double lng,
        string direccion)
    {
        try
        {
            string? clave = CrearClaveDestino(lat, lng, direccion);

            if (clave is null)
            {
                return null;
            }

            if (CacheTiempos.TryGetValue(clave, out var entrada)
                && (DateTime.Now - entrada.Fecha).TotalMinutes <= MinutosCacheTiempos)
            {
                return entrada.Minutos;
            }

            // Un fallo reciente (geocodificación o ruta) no se reintenta
            // todavía: así deja avanzar al resto de destinos.
            if (EnFalloReciente(clave))
            {
                return null;
            }

            var origen = ObtenerUbicacionGuardada();

            if (origen is null)
            {
                return null;
            }

            var destino = await ResolverDestinoAsync(lat, lng, direccion);

            if (destino is null)
            {
                MarcarFallo(clave);
                return null;
            }

            int? minutos = await CalcularRutaAsync(origen.Value, destino.Value);

            if (minutos is not int valor)
            {
                MarcarFallo(clave);
                return null;
            }

            CacheFallos.Remove(clave);
            CacheTiempos[clave] = (DateTime.Now, valor);
            return valor;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// true si el destino falló hace poco (no conviene reintentarlo aún).
    /// </summary>
    public static bool EnFalloReciente(
        double lat,
        double lng,
        string direccion)
    {
        string? clave = CrearClaveDestino(lat, lng, direccion);

        return clave is not null
            && EnFalloReciente(clave);
    }


    private static bool EnFalloReciente(string clave)
    {
        return CacheFallos.TryGetValue(clave, out DateTime fallo)
            && (DateTime.Now - fallo).TotalMinutes <= MinutosEsperaTrasFallo;
    }


    private static void MarcarFallo(string clave)
    {
        CacheFallos[clave] = DateTime.Now;
    }


    /// <summary>
    /// Calcula en segundo plano los destinos pendientes y, si alguno se
    /// consigue, avisa al terminar (el widget se vuelve a pintar). Sin
    /// ubicación guardada no hace nada.
    /// </summary>
    public static void RefrescarEnSegundoPlano(
        IEnumerable<(double Lat, double Lng, string Direccion)> destinos,
        Action alTerminar)
    {
        if (_calculando || ObtenerUbicacionGuardada() is null)
        {
            return;
        }

        List<(double Lat, double Lng, string Direccion)> lista =
            destinos.Take(6).ToList();

        if (lista.Count == 0)
        {
            return;
        }

        _calculando = true;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var destino in lista)
                {
                    await CalcularMinutosCocheAsync(
                        destino.Lat,
                        destino.Lng,
                        destino.Direccion);
                }
            }
            catch
            {
            }
            finally
            {
                _calculando = false;
            }

            // Se repinta después de cada tanda: los éxitos ya se ven y
            // los fallos quedan apartados un rato, así la siguiente
            // pasada intenta destinos nuevos (sin bucles infinitos).
            try
            {
                alTerminar();
            }
            catch
            {
            }
        });
    }


    public static string FormatearMinutos(int minutos)
    {
        if (minutos < 60)
        {
            return minutos + " min";
        }

        int horas = minutos / 60;
        int resto = minutos % 60;

        return resto == 0
            ? horas + " h"
            : $"{horas} h {resto} min";
    }


    private static string? CrearClaveDestino(
        double lat,
        double lng,
        string direccion)
    {
        if (lat != 0 || lng != 0)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:F5},{1:F5}",
                lat,
                lng);
        }

        string texto = (direccion ?? "").Trim();

        if (texto.Length == 0)
        {
            return null;
        }

        return "dir:" + texto.ToLowerInvariant();
    }


    private static async Task<(double Lat, double Lng)?> ResolverDestinoAsync(
        double lat,
        double lng,
        string direccion)
    {
        if (lat != 0 || lng != 0)
        {
            return (lat, lng);
        }

        string direccionTexto = (direccion ?? "").Trim();

        if (direccionTexto.Length == 0)
        {
            return null;
        }

        string claveCache = direccionTexto.ToLowerInvariant();

        if (CacheGeocodificacion.TryGetValue(claveCache, out var guardada))
        {
            return guardada;
        }

        string claveGoogle = ObtenerClaveGoogle();

        foreach (string consulta in CrearConsultasGeocodificacion(direccionTexto))
        {
            (double Lat, double Lng)? resultado = null;

            if (claveGoogle.Length > 0)
            {
                resultado = await GeocodificarConGoogleAsync(claveGoogle, consulta);
            }

            // Si Google no da resultado (o no hay clave), se prueba con
            // el servicio abierto.
            resultado ??= await GeocodificarConNominatimAsync(consulta);

            if (resultado is { } punto)
            {
                CacheGeocodificacion[claveCache] = punto;
                return punto;
            }
        }

        return null;
    }


    /// <summary>
    /// Variantes de la dirección para el geocodificador: tal cual, sin
    /// paréntesis y con el país (mejora los aciertos).
    /// </summary>
    private static List<string> CrearConsultasGeocodificacion(string direccion)
    {
        var consultas = new List<string>();

        void Anadir(string texto)
        {
            texto = (texto ?? "").Trim();

            if (texto.Length > 0
                && !consultas.Any(c => string.Equals(
                    c,
                    texto,
                    StringComparison.OrdinalIgnoreCase)))
            {
                consultas.Add(texto);
            }
        }

        Anadir(direccion);

        string sinParentesis =
            System.Text.RegularExpressions.Regex.Replace(
                direccion,
                @"\([^)]*\)",
                " ");
        sinParentesis =
            System.Text.RegularExpressions.Regex.Replace(
                sinParentesis,
                @"\s+",
                " ").Trim();

        Anadir(sinParentesis);

        if (!direccion.Contains("España", StringComparison.OrdinalIgnoreCase)
            && !direccion.Contains("Spain", StringComparison.OrdinalIgnoreCase))
        {
            Anadir(sinParentesis + ", España");
            Anadir(direccion + ", España");
        }

        return consultas;
    }


    private static async Task<(double Lat, double Lng)?> GeocodificarConGoogleAsync(
        string clave,
        string consulta)
    {
        try
        {
            string url =
                "https://maps.googleapis.com/maps/api/geocode/json"
                + "?address=" + Uri.EscapeDataString(consulta)
                + "&language=es&key=" + Uri.EscapeDataString(clave);

            using JsonDocument json =
                JsonDocument.Parse(
                    await Cliente.GetStringAsync(url));

            JsonElement raiz = json.RootElement;

            if (raiz.TryGetProperty("results", out JsonElement resultados)
                && resultados.GetArrayLength() > 0)
            {
                JsonElement ubicacion = resultados[0]
                    .GetProperty("geometry")
                    .GetProperty("location");

                return (
                    ubicacion.GetProperty("lat").GetDouble(),
                    ubicacion.GetProperty("lng").GetDouble());
            }
        }
        catch
        {
        }

        return null;
    }


    private static async Task<(double Lat, double Lng)?> GeocodificarConNominatimAsync(
        string consulta)
    {
        try
        {
            string url =
                "https://nominatim.openstreetmap.org/search"
                + "?format=json&limit=1&q="
                + Uri.EscapeDataString(consulta);

            using JsonDocument json =
                JsonDocument.Parse(
                    await Cliente.GetStringAsync(url));

            JsonElement raiz = json.RootElement;

            if (raiz.ValueKind == JsonValueKind.Array
                && raiz.GetArrayLength() > 0)
            {
                JsonElement primero = raiz[0];

                return (
                    double.Parse(
                        primero.GetProperty("lat").GetString() ?? "0",
                        System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(
                        primero.GetProperty("lon").GetString() ?? "0",
                        System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch
        {
        }

        return null;
    }


    private static async Task<int?> CalcularRutaAsync(
        (double Lat, double Lng) origen,
        (double Lat, double Lng) destino)
    {
        string clave = ObtenerClaveGoogle();

        if (clave.Length > 0)
        {
            int? conGoogle = await RutaConGoogleAsync(clave, origen, destino);

            if (conGoogle is int valorGoogle)
            {
                return valorGoogle;
            }
        }

        return await RutaConOsrmAsync(origen, destino);
    }


    private static async Task<int?> RutaConGoogleAsync(
        string clave,
        (double Lat, double Lng) origen,
        (double Lat, double Lng) destino)
    {
        try
        {
            string url =
                "https://maps.googleapis.com/maps/api/directions/json"
                + "?origin=" + Numero(origen.Lat) + "," + Numero(origen.Lng)
                + "&destination=" + Numero(destino.Lat) + "," + Numero(destino.Lng)
                + "&mode=driving&language=es&departure_time=now"
                + "&key=" + Uri.EscapeDataString(clave);

            using JsonDocument json =
                JsonDocument.Parse(
                    await Cliente.GetStringAsync(url));

            JsonElement raiz = json.RootElement;

            if (raiz.TryGetProperty("routes", out JsonElement rutas)
                && rutas.GetArrayLength() > 0)
            {
                JsonElement pierna = rutas[0].GetProperty("legs")[0];

                double segundos;

                if (pierna.TryGetProperty(
                        "duration_in_traffic",
                        out JsonElement conTrafico))
                {
                    segundos =
                        conTrafico.GetProperty("value").GetDouble();
                }
                else
                {
                    segundos = pierna
                        .GetProperty("duration")
                        .GetProperty("value")
                        .GetDouble();
                }

                return (int)Math.Ceiling(segundos / 60.0);
            }
        }
        catch
        {
        }

        return null;
    }


    private static async Task<int?> RutaConOsrmAsync(
        (double Lat, double Lng) origen,
        (double Lat, double Lng) destino)
    {
        try
        {
            string url =
                "https://router.project-osrm.org/route/v1/driving/"
                + Numero(origen.Lng) + "," + Numero(origen.Lat) + ";"
                + Numero(destino.Lng) + "," + Numero(destino.Lat)
                + "?overview=false";

            using JsonDocument json =
                JsonDocument.Parse(
                    await Cliente.GetStringAsync(url));

            JsonElement raiz = json.RootElement;

            if (raiz.TryGetProperty("routes", out JsonElement rutas)
                && rutas.GetArrayLength() > 0)
            {
                double segundos =
                    rutas[0].GetProperty("duration").GetDouble();

                return (int)Math.Ceiling(segundos / 60.0);
            }
        }
        catch
        {
        }

        return null;
    }


    private static string Numero(double valor) =>
        valor.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}
