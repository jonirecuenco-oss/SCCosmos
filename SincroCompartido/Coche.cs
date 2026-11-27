#nullable enable

using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace SCCosmos.Sincro;

/// <summary>
/// El coche, por el OBDLink: la última lectura del combustible y de los km,
/// en Configuración (se sincroniza: la ven los dos móviles y el PC). El coche
/// no da el cuentakilómetros: los km son los que se ponen a mano una vez más
/// lo que avanza su contador de km desde que se borraron las averías (01 31),
/// que cuenta todos los viajes, vaya o no un móvil en el coche.
/// </summary>
public static class Coche
{
    /// <summary>La última lectura (JSON).</summary>
    public const string ClaveLectura = "Coche_Lectura";

    /// <summary>Los km del cuentakilómetros que corresponden a un valor del contador (JSON).</summary>
    public const string ClaveBase = "Coche_Base";

    /// <summary>Los km puestos a mano (JSON): valen desde la siguiente lectura.</summary>
    public const string ClaveKmPuestos = "Coche_KmPuestos";

    /// <summary>El contador del coche (01 31) se para aquí.</summary>
    public const int TopeContador = 65535;

    /// <summary>Quién la tomó: «SC» o «Esposa» (el móvil de cada uno).</summary>
    public sealed record Lectura(DateTime Cuando, string Quien, int? Combustible, int? Contador, long? Km);

    public sealed record Base(long Km, int Contador, DateTime Cuando);

    public sealed record KmPuestos(long Km, DateTime Cuando);

    /// <summary>El combustible (%) de la respuesta a 01 2F.</summary>
    public static int? CombustibleDe(string? recibido) =>
        PruebaCoche.DatosDe("012F", recibido) is { Length: >= 1 } d ? (int)Math.Round(d[0] * 100 / 255.0) : null;

    /// <summary>El contador (km desde que se borraron las averías) de la respuesta a 01 31.</summary>
    public static int? ContadorDe(string? recibido) =>
        PruebaCoche.DatosDe("0131", recibido) is { Length: >= 2 } d ? d[0] * 256 + d[1] : null;

    /// <summary>
    /// Una lectura nueva, con los km calculados y la base al día. Los km se cuentan
    /// desde lo último que se sabe: la base o, si es más reciente (o este móvil aún
    /// no la tiene: la lectura llegó de otro móvil), la lectura anterior, que lleva
    /// sus km y su contador. Los km puestos a mano después de eso valen desde esta
    /// lectura; si el contador baja (en el taller se borraron las averías y vuelve
    /// a 0), se sigue desde los km que se tenían. Lo que no se haya podido leer
    /// queda como estaba.
    /// </summary>
    public static (Lectura Lectura, Base? Base) Aplicar(
        DateTime cuando,
        string quien,
        int? combustible,
        int? contador,
        Lectura? anterior,
        Base? baseActual,
        KmPuestos? puestos)
    {
        Base? calibracion = baseActual;

        Base? referencia =
            anterior is { Km: long kmAnterior, Contador: int contadorAnterior }
            && (calibracion is null || anterior.Cuando > calibracion.Cuando)
                ? new Base(kmAnterior, contadorAnterior, anterior.Cuando)
                : calibracion;

        if (contador is int c)
        {
            if (puestos is not null && (referencia is null || puestos.Cuando > referencia.Cuando))
            {
                calibracion = referencia = new Base(puestos.Km, c, cuando);
            }
            else if (referencia is not null && c < referencia.Contador)
            {
                calibracion = referencia = new Base(referencia.Km, 0, cuando);
            }
        }

        int? contadorFinal = contador ?? anterior?.Contador;

        long? km = referencia is not null && contadorFinal is int cuenta
            ? referencia.Km + (cuenta - referencia.Contador)
            : anterior?.Km;

        return (new Lectura(cuando, quien, combustible ?? anterior?.Combustible, contadorFinal, km), calibracion);
    }

    /// <summary>
    /// La última lectura, para enseñarla. Si llegó sin km (de un móvil que aún no
    /// tenía desde dónde contarlos), se cuentan aquí con la base de este.
    /// </summary>
    public static Lectura? Ultima(Func<string, string> leerConfiguracion)
    {
        Lectura? lectura = LecturaDe(leerConfiguracion(ClaveLectura));

        if (lectura is { Km: null, Contador: int c }
            && BaseDe(leerConfiguracion(ClaveBase)) is { } calibracion
            && c >= calibracion.Contador)
        {
            return lectura with { Km = calibracion.Km + (c - calibracion.Contador) };
        }

        return lectura;
    }

    /// <summary>
    /// Para Inicio: «64 % de combustible · 187.512 km» y, debajo, cuándo y quién
    /// («Leído hoy a las 16:02 (Jonatán)»).
    /// </summary>
    public static (string Datos, string Cuando) Texto(Lectura lectura, DateTime ahora, string nombreSc, string nombreEsposa)
    {
        string combustible = lectura.Combustible is int c ? $"{c} % de combustible" : "Combustible sin leer";

        string km = lectura.Km is long k
            ? Km(k)
            : "faltan los km del cuentakilómetros";

        if (lectura.Contador >= TopeContador)
        {
            km += " (el contador del coche ha llegado a su tope: vuelve a poner los km)";
        }

        string dia = lectura.Cuando.Date == ahora.Date ? "hoy"
            : lectura.Cuando.Date == ahora.Date.AddDays(-1) ? "ayer"
            : "el " + lectura.Cuando.ToString("dd/MM", CultureInfo.InvariantCulture);

        string quien = (lectura.Quien == "Esposa" ? nombreEsposa : nombreSc).Trim();

        return ($"{combustible} · {km}",
            $"Leído {dia} a las {lectura.Cuando.ToString("HH:mm", CultureInfo.InvariantCulture)}" + (quien.Length > 0 ? $" ({quien})" : ""));
    }

    /// <summary>
    /// Para el widget, en una línea: «64 % · 187.512 km» y, aparte, cuándo
    /// («hoy, 16:02», «ayer, 18:30» o «22/11, 9:15»).
    /// </summary>
    public static (string Datos, string Cuando) TextoWidget(Lectura lectura, DateTime ahora)
    {
        string combustible = lectura.Combustible is int c ? $"{c} %" : "–";
        string km = lectura.Km is long k ? Km(k) : "faltan los km";

        string dia = lectura.Cuando.Date == ahora.Date ? "hoy"
            : lectura.Cuando.Date == ahora.Date.AddDays(-1) ? "ayer"
            : lectura.Cuando.ToString("dd/MM", CultureInfo.InvariantCulture);

        return ($"{combustible} · {km}", $"{dia}, {lectura.Cuando.ToString("H:mm", CultureInfo.InvariantCulture)}");
    }

    private static readonly NumberFormatInfo Numeros = new() { NumberGroupSeparator = "." };

    /// <summary>«187.512 km», con punto aunque el móvil esté en otro idioma.</summary>
    public static string Km(long km) => km.ToString("N0", Numeros) + " km";

    // ---- En Configuración, como JSON ----

    public static string Json(Lectura l) => new JsonObject
    {
        ["cuando"] = Fecha(l.Cuando),
        ["quien"] = l.Quien,
        ["combustible"] = l.Combustible,
        ["contador"] = l.Contador,
        ["km"] = l.Km
    }.ToJsonString();

    public static string Json(Base b) => new JsonObject
    {
        ["km"] = b.Km,
        ["contador"] = b.Contador,
        ["cuando"] = Fecha(b.Cuando)
    }.ToJsonString();

    public static string Json(KmPuestos p) => new JsonObject
    {
        ["km"] = p.Km,
        ["cuando"] = Fecha(p.Cuando)
    }.ToJsonString();

    public static Lectura? LecturaDe(string? json) =>
        Objeto(json) is { } o && FechaDe(o["cuando"]) is DateTime cuando
            ? new Lectura(cuando, Texto(o["quien"]), Entero(o["combustible"]), Entero(o["contador"]), Largo(o["km"]))
            : null;

    public static Base? BaseDe(string? json) =>
        Objeto(json) is { } o && FechaDe(o["cuando"]) is DateTime cuando && Largo(o["km"]) is long km && Entero(o["contador"]) is int contador
            ? new Base(km, contador, cuando)
            : null;

    public static KmPuestos? KmPuestosDe(string? json) =>
        Objeto(json) is { } o && FechaDe(o["cuando"]) is DateTime cuando && Largo(o["km"]) is long km
            ? new KmPuestos(km, cuando)
            : null;

    private static string Fecha(DateTime fecha) => fecha.ToString("s", CultureInfo.InvariantCulture);

    private static JsonObject? Objeto(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTime? FechaDe(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue(out string? s)
        && DateTime.TryParseExact(s, "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
            ? fecha
            : null;

    private static string Texto(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    private static int? Entero(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue(out int i) ? i : null;

    private static long? Largo(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue(out long l) ? l : null;
}
