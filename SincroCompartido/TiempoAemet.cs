#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SCCosmos.Sincro;

// =============================================================================
// TIEMPO DE AEMET (común a Windows y Android)
// =============================================================================
//
// Todo sale de los archivos públicos de AEMET, sin clave:
//   - Por horas (3 días): https://www.aemet.es/xml/municipios_h/localidad_h_{ine}.xml
//   - Por tramos (7 días): https://www.aemet.es/xml/municipios/localidad_{ine}.xml
//     (días 1-2: tramos de 6 h con temperatura; 3-4: de 12 h; luego, el día entero)
//   - Avisos (Meteoalerta, CAP): ver UrlAvisos. Cada aviso trae el polígono de
//     su zona, así que se sabe si afecta exactamente al punto.
// El municipio (código del INE) sale de municipios_ine.csv (recurso incrustado).
// Lo descargado se guarda en CarpetaCache: sin red se enseña lo último.
// Dónde se va a estar cada día lo decide cada app (visita o ubicación).

/// <summary>Una hora (o un tramo del día) de la previsión.</summary>
public sealed class HoraTiempo
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>«10h» o, en los tramos, «6–12 h», «Todo el día»...</summary>
    public string Etiqueta { get; init; } = "";

    /// <summary>Hora del día (0-23); -1 en los tramos.</summary>
    public int Hora { get; init; } = -1;

    public DateTime Desde { get; init; }

    public DateTime Hasta { get; init; }

    /// <summary>Código de cielo de AEMET («15», «26n»...).</summary>
    public string Cielo { get; init; } = "";

    public string Descripcion { get; init; } = "";

    public int? Temperatura { get; init; }

    /// <summary>Probabilidad de lluvia: AEMET la da por tramos de 6 horas.</summary>
    public int? ProbLluvia { get; init; }

    /// <summary>Lluvia prevista en esa hora (mm); 0,05 es «inapreciable».</summary>
    public double? Precipitacion { get; init; }

    /// <summary>Avisos que cubren esta hora (o tramo), el de más nivel primero.</summary>
    public List<AvisoTiempo> Avisos { get; } = new();

    public AvisoTiempo? Aviso => Avisos.FirstOrDefault();

    public string Icono => TiempoAemet.Icono(Cielo);

    /// <summary>
    /// En cada hora, los mm de lluvia (el dato horario de AEMET); en los
    /// tramos, su probabilidad. La probabilidad del día va arriba.
    /// </summary>
    public string TextoLluvia =>
        Precipitacion is double mm
            ? (mm >= 0.1 ? mm.ToString("0.#", Es) + " mm" : mm > 0 ? "<0,1 mm" : "")
            : Hora < 0 && ProbLluvia is int p && p > 0 ? $"{p}%" : "";
}


/// <summary>Un aviso de AEMET (Meteoalerta) que afecta al sitio.</summary>
public sealed class AvisoTiempo
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public string Nivel { get; init; } = "";

    /// <summary>Código de Meteoalerta: PR (lluvias), TO (tormentas), VI (vientos)...</summary>
    public string Codigo { get; init; } = "";

    public string Fenomeno { get; init; } = "";

    public string Zona { get; init; } = "";

    public string Descripcion { get; init; } = "";

    public DateTimeOffset Inicio { get; init; }

    public DateTimeOffset Fin { get; init; }

    public DateTimeOffset Enviado { get; init; }

    /// <summary>Para no avisar dos veces del mismo aviso (AEMET lo reenvía al actualizarlo).</summary>
    public string Clave => $"{Zona}|{Fenomeno}|{Nivel}|{Inicio:o}|{Fin:o}";

    public int Orden => Nivel switch { "rojo" => 3, "naranja" => 2, _ => 1 };

    public string Color => Nivel switch { "rojo" => "#FF6B6B", "naranja" => "#FFB74D", _ => "#E5C07B" };

    /// <summary>«amarillo», «naranja» o «rojo»: el icono de aviso de su color.</summary>
    public string NivelIcono =>
        Nivel is "rojo" or "naranja" ? Nivel : "amarillo";

    public string Titulo =>
        $"Aviso {Nivel} por " +
        (Fenomeno.Length > 0 ? char.ToLower(Fenomeno[0], Es) + Fenomeno[1..] : "fenómenos adversos");

    public string Periodo
    {
        get
        {
            DateTime i = Inicio.LocalDateTime, f = Fin.LocalDateTime;

            static string Dia(DateTime d) =>
                d.Date == DateTime.Today ? "hoy"
                : d.Date == DateTime.Today.AddDays(1) ? "mañana"
                : d.ToString("ddd d", Es);

            return i.Date == f.Date
                ? $"{Dia(i)}, {i:HH:mm}–{f:HH:mm}"
                : $"{Dia(i)} {i:HH:mm} – {Dia(f)} {f:HH:mm}";
        }
    }
}


/// <summary>La previsión de un día en un sitio.</summary>
public sealed class TiempoDia
{
    public DateTime Fecha { get; init; }

    public string Lugar { get; init; } = "";

    /// <summary>«Visita a …», «Tu ubicación», «Ubicación del móvil».</summary>
    public string Motivo { get; init; } = "";

    /// <summary>AEMET aún no publica nada para ese día (más de 7 días).</summary>
    public bool SinPrevision { get; init; }

    public bool PorHoras { get; init; }

    public List<HoraTiempo> Horas { get; init; } = new();

    public int? Maxima { get; init; }

    public int? Minima { get; init; }

    /// <summary>Probabilidad de lluvia en todo el día.</summary>
    public int? ProbLluviaDia { get; init; }

    public List<AvisoTiempo> Avisos { get; init; } = new();

    public DateTime Actualizado { get; init; }
}


/// <summary>Resumen de un día: cielo, máxima, mínima, lluvia y su aviso de más nivel.</summary>
public sealed class ResumenDia
{
    private static readonly string[] NombresDia = { "Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };

    public DateTime Fecha { get; init; }

    public string Cielo { get; init; } = "";

    public string Descripcion { get; init; } = "";

    public int? Maxima { get; init; }

    public int? Minima { get; init; }

    public int? ProbLluvia { get; init; }

    /// <summary>El aviso de más nivel de ese día (si hay).</summary>
    public AvisoTiempo? Aviso { get; set; }

    public string Icono => TiempoAemet.Icono(Cielo);

    /// <summary>«Hoy» o «Mié 11».</summary>
    public string Etiqueta =>
        Fecha == DateTime.Today ? "Hoy" : $"{NombresDia[(int)Fecha.DayOfWeek]} {Fecha.Day}";

    public string TextoLluvia =>
        ProbLluvia is int p && p > 0 ? $"{p}%" : "";
}


/// <summary>Los días que publica AEMET en un sitio (hasta 7).</summary>
public sealed class SemanaTiempo
{
    public List<ResumenDia> Dias { get; init; } = new();

    public DateTime Actualizado { get; init; }
}

/// <summary>Sitio de la previsión: municipio del INE y coordenadas.</summary>
public sealed record LugarTiempo(
    string Ine,
    string Nombre,
    string Motivo,
    double Latitud,
    double Longitud);


public static class TiempoAemet
{
    private const string UrlHoras =
        "https://www.aemet.es/xml/municipios_h/localidad_h_{0}.xml";

    private const string UrlDias =
        "https://www.aemet.es/xml/municipios/localidad_{0}.xml";

    private const string UrlAvisos =
        "https://www.aemet.es/documentos_d/eltiempo/prediccion/avisos/rss/CAP_AFAE_wah_RSS.xml";

    private static readonly XNamespace Cap =
        "urn:oasis:names:tc:emergency:cap:1.2";

    private static readonly HttpClient Http =
        CrearHttp();

    /// <summary>Dónde se guarda lo descargado (lo pone cada app al arrancar).</summary>
    public static string CarpetaCache { get; set; } =
        Path.GetTempPath();


    private static HttpClient CrearHttp()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("SCCosmos/2.1");

        return http;
    }


    // =========================================================================
    // MUNICIPIOS DEL INE
    // =========================================================================

    private static Dictionary<string, string>? _codigos;

    private static Dictionary<string, string>? _nombres;

    private static readonly object CandadoMunicipios = new();


    private static void CargarMunicipios()
    {
        lock (CandadoMunicipios)
        {
            if (_codigos is not null)
            {
                return;
            }

            var codigos = new Dictionary<string, string>();
            var nombres = new Dictionary<string, string>();

            using Stream archivo =
                typeof(TiempoAemet).Assembly.GetManifestResourceStream("municipios_ine.csv")
                ?? throw new FileNotFoundException("municipios_ine.csv");

            using var lector =
                new StreamReader(archivo, Encoding.UTF8);

            string? linea;

            while ((linea = lector.ReadLine()) is not null)
            {
                string[] c = linea.Split(';');

                if (linea.StartsWith('#') || c.Length < 3)
                {
                    continue;
                }

                nombres[c[0]] = c[1];

                foreach (string nombre in Variantes(c[1]))
                {
                    foreach (string provincia in c[2].Split('/'))
                    {
                        codigos.TryAdd(
                            Normalizar(nombre) + "|" + Normalizar(provincia),
                            c[0]);
                    }
                }
            }

            _nombres = nombres;
            _codigos = codigos;
        }
    }


    /// <summary>
    /// «Rozas de Madrid, Las» vale también como «Las Rozas de Madrid», y
    /// «Alicante/Alacant» con los dos nombres.
    /// </summary>
    private static IEnumerable<string> Variantes(string nombre)
    {
        foreach (string parte in nombre.Split('/'))
        {
            string p = parte.Trim();

            yield return p;

            int coma = p.LastIndexOf(", ", StringComparison.Ordinal);

            if (coma > 0)
            {
                yield return p[(coma + 2)..] + " " + p[..coma];
            }
        }
    }


    /// <summary>Código del INE de un municipio («Mérida», «Badajoz» → «06083»).</summary>
    public static string? BuscarIne(
        string? municipio,
        string? provincia)
    {
        if (string.IsNullOrWhiteSpace(municipio))
        {
            return null;
        }

        CargarMunicipios();

        string m = Normalizar(municipio);

        foreach (string p in (provincia ?? "").Split('/'))
        {
            if (_codigos!.TryGetValue(m + "|" + Normalizar(p), out string? codigo))
            {
                return codigo;
            }
        }

        // Sin provincia (o con otro nombre): solo si el nombre no se repite.
        List<string> candidatos =
            _codigos!
                .Where(k => k.Key.StartsWith(m + "|", StringComparison.Ordinal))
                .Select(k => k.Value)
                .Distinct()
                .Take(2)
                .ToList();

        return candidatos.Count == 1
            ? candidatos[0]
            : null;
    }


    /// <summary>Nombre para mostrar («Rozas de Madrid, Las» → «Las Rozas de Madrid»).</summary>
    public static string NombreMunicipio(
        string ine,
        string otro)
    {
        CargarMunicipios();

        return _nombres!.TryGetValue(ine, out string? nombre)
            ? Variantes(nombre).Last()
            : otro;
    }


    /// <summary>
    /// El sitio de una congregación (municipio, provincia y coordenadas del
    /// salón); null si el municipio no está en la lista del INE.
    /// </summary>
    public static LugarTiempo? LugarDe(
        string? municipio,
        string? provincia,
        double latitud,
        double longitud,
        string motivo)
    {
        string? ine = BuscarIne(municipio, provincia);

        return ine is null
            ? null
            : new LugarTiempo(ine, NombreMunicipio(ine, municipio ?? ""), motivo, latitud, longitud);
    }

    private static string Normalizar(string texto)
    {
        var resultado = new StringBuilder(texto.Length);

        foreach (char c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            resultado.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return Regex.Replace(resultado.ToString(), @"\s+", " ").Trim();
    }


    // =========================================================================
    // PREVISIÓN
    // =========================================================================

    /// <summary>La previsión de ese día en ese sitio, con sus avisos.</summary>
    public static async Task<TiempoDia> ObtenerAsync(
        LugarTiempo lugar,
        DateTime dia,
        bool soloCache,
        CancellationToken ct)
    {
        string archivoHoras = $"aemet_h_{lugar.Ine}.xml";

        XDocument? horas =
            await DescargarAsync(string.Format(UrlHoras, lugar.Ine), archivoHoras, TimeSpan.FromMinutes(60), latin: true, soloCache, ct);

        XDocument? dias =
            await DescargarAsync(string.Format(UrlDias, lugar.Ine), $"aemet_d_{lugar.Ine}.xml", TimeSpan.FromHours(3), latin: true, soloCache, ct);

        DateTime fecha = dia.Date;
        string clave = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        XElement? diaHoras = DiaDe(horas, clave);
        XElement? diaTramos = DiaDe(dias, clave);

        if (diaHoras is null && diaTramos is null)
        {
            return new TiempoDia
            {
                Fecha = fecha,
                Lugar = lugar.Nombre,
                Motivo = lugar.Motivo,
                SinPrevision = horas is not null || dias is not null
            };
        }

        List<HoraTiempo> lista =
            diaHoras is not null
                ? HorasDe(diaHoras, fecha)
                : TramosDe(diaTramos!, fecha);

        int? maxima = Entero(diaTramos?.Element("temperatura")?.Element("maxima")?.Value);
        int? minima = Entero(diaTramos?.Element("temperatura")?.Element("minima")?.Value);

        if (maxima is null && lista.Any(h => h.Temperatura is not null))
        {
            maxima = lista.Max(h => h.Temperatura);
            minima = lista.Min(h => h.Temperatura);
        }

        List<AvisoTiempo> avisos =
            await AvisosEntreAsync(lugar, fecha, fecha.AddDays(1), soloCache, ct);

        // Cada hora (o tramo) con los avisos que la cubren, el de más nivel primero.
        foreach (HoraTiempo hora in lista)
        {
            hora.Avisos.AddRange(
                avisos
                    .Where(a => a.Inicio.LocalDateTime < hora.Hasta && a.Fin.LocalDateTime > hora.Desde)
                    .OrderByDescending(a => a.Orden));
        }

        string rutaHoras = RutaCache(archivoHoras);

        return new TiempoDia
        {
            Fecha = fecha,
            Lugar = lugar.Nombre,
            Motivo = lugar.Motivo,
            PorHoras = diaHoras is not null,
            Horas = lista,
            Maxima = maxima,
            Minima = minima,
            ProbLluviaDia = ProbLluviaDe(diaTramos),
            Avisos = avisos,
            Actualizado = File.Exists(rutaHoras) ? File.GetLastWriteTime(rutaHoras) : default
        };
    }


    /// <summary>
    /// Los días que publica AEMET en ese sitio (hasta 7), cada uno con su
    /// aviso de más nivel.
    /// </summary>
    public static async Task<SemanaTiempo> ObtenerSemanaAsync(
        LugarTiempo lugar,
        bool soloCache,
        CancellationToken ct)
    {
        string archivo = $"aemet_d_{lugar.Ine}.xml";

        XDocument? documento =
            await DescargarAsync(string.Format(UrlDias, lugar.Ine), archivo, TimeSpan.FromHours(3), latin: true, soloCache, ct);

        var dias = new List<ResumenDia>();

        foreach (XElement dia in documento?.Root?.Element("prediccion")?.Elements("dia") ?? Enumerable.Empty<XElement>())
        {
            if (!DateTime.TryParseExact((string?)dia.Attribute("fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
                || fecha < DateTime.Today)
            {
                continue;
            }

            // El cielo de todo el día o, hoy, el de lo que queda de día.
            List<XElement> cielos =
                dia.Elements("estado_cielo")
                    .Where(e => e.Value.Trim().Length > 0)
                    .ToList();

            XElement? cielo =
                new[] { "00-24", "12-24", "12-18", "18-24" }
                    .Select(p => cielos.FirstOrDefault(e => Periodo(e) == p))
                    .FirstOrDefault(e => e is not null)
                ?? cielos.FirstOrDefault();

            dias.Add(new ResumenDia
            {
                Fecha = fecha,
                Cielo = cielo?.Value.Trim() ?? "",
                Descripcion = (string?)cielo?.Attribute("descripcion") ?? "",
                Maxima = Entero(dia.Element("temperatura")?.Element("maxima")?.Value),
                Minima = Entero(dia.Element("temperatura")?.Element("minima")?.Value),
                ProbLluvia = ProbLluviaDe(dia)
            });
        }

        List<AvisoTiempo> avisos =
            dias.Count == 0
                ? new List<AvisoTiempo>()
                : await AvisosEntreAsync(lugar, dias[0].Fecha, dias[^1].Fecha.AddDays(1), soloCache, ct);

        foreach (ResumenDia resumen in dias)
        {
            resumen.Aviso =
                avisos
                    .Where(a => a.Inicio.LocalDateTime < resumen.Fecha.AddDays(1) && a.Fin.LocalDateTime > resumen.Fecha)
                    .OrderByDescending(a => a.Orden)
                    .FirstOrDefault();
        }

        string ruta = RutaCache(archivo);

        return new SemanaTiempo
        {
            Dias = dias,
            Actualizado = File.Exists(ruta) ? File.GetLastWriteTime(ruta) : default
        };
    }


    // Probabilidad de lluvia del día: la de 00-24 o, si ya no está (hoy), la mayor de lo que queda.
    private static int? ProbLluviaDe(XElement? dia) =>
        Entero(dia?.Elements("prob_precipitacion").FirstOrDefault(e => Periodo(e) == "00-24")?.Value)
        ?? dia?.Elements("prob_precipitacion").Select(e => Entero(e.Value)).Max();

    private static XElement? DiaDe(
        XDocument? documento,
        string fecha) =>
        documento?.Root
            ?.Element("prediccion")
            ?.Elements("dia")
            .FirstOrDefault(d => (string?)d.Attribute("fecha") == fecha);


    // AEMET deja sin periodo el valor de todo el día en los últimos días.
    private static string Periodo(XElement e)
    {
        string p = ((string?)e.Attribute("periodo") ?? "").Trim();

        return p.Length == 0 ? "00-24" : p;
    }


    /// <summary>Hora a hora: cielo, temperatura, lluvia (mm) y probabilidad (de su tramo).</summary>
    private static List<HoraTiempo> HorasDe(
        XElement dia,
        DateTime fecha)
    {
        var cielo = new Dictionary<int, (string Codigo, string Descripcion)>();

        foreach (XElement e in dia.Elements("estado_cielo"))
        {
            if (int.TryParse((string?)e.Attribute("periodo"), out int h) && h < 24 && e.Value.Trim().Length > 0)
            {
                cielo[h] = (e.Value.Trim(), (string?)e.Attribute("descripcion") ?? "");
            }
        }

        var precipitacion = new Dictionary<int, double>();

        foreach (XElement e in dia.Elements("precipitacion"))
        {
            string v = e.Value.Trim();

            if (int.TryParse((string?)e.Attribute("periodo"), out int h) && h < 24)
            {
                if (v.Equals("Ip", StringComparison.OrdinalIgnoreCase))
                {
                    precipitacion[h] = 0.05;
                }
                else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double mm))
                {
                    precipitacion[h] = mm;
                }
            }
        }

        var temperatura = new Dictionary<int, int>();

        foreach (XElement e in dia.Elements("temperatura"))
        {
            if (int.TryParse((string?)e.Attribute("periodo"), out int h) && h < 24 && Entero(e.Value) is int t)
            {
                temperatura[h] = t;
            }
        }

        // La probabilidad de lluvia va por tramos: «0107» es de 01 a 07; «1901», de 19 a 01.
        var tramos = new List<(int Desde, int Hasta, int Valor)>();

        foreach (XElement e in dia.Elements("prob_precipitacion"))
        {
            string p = (string?)e.Attribute("periodo") ?? "";

            if (p.Length == 4
                && int.TryParse(p[..2], out int desde)
                && int.TryParse(p[2..], out int hasta)
                && Entero(e.Value) is int valor)
            {
                tramos.Add((desde, hasta <= desde ? hasta + 24 : hasta, valor));
            }
        }

        return cielo.Keys
            .Union(temperatura.Keys)
            .OrderBy(h => h)
            .Select(h => new HoraTiempo
            {
                Hora = h,
                Desde = fecha.AddHours(h),
                Hasta = fecha.AddHours(h + 1),
                Etiqueta = $"{h:00}h",
                Cielo = cielo.TryGetValue(h, out var c) ? c.Codigo : "",
                Descripcion = cielo.TryGetValue(h, out var d) ? d.Descripcion : "",
                Temperatura = temperatura.TryGetValue(h, out int t) ? t : null,
                Precipitacion = precipitacion.TryGetValue(h, out double mm) ? mm : null,
                ProbLluvia = tramos
                    .Where(x => (h >= x.Desde && h < x.Hasta) || (h + 24 >= x.Desde && h + 24 < x.Hasta))
                    .Select(x => (int?)x.Valor)
                    .FirstOrDefault()
            })
            .ToList();
    }


    /// <summary>
    /// Días sin previsión por horas: tramos de 6 h (con su temperatura), de
    /// 12 h o el día entero, según lo que publique AEMET para ese día.
    /// </summary>
    private static List<HoraTiempo> TramosDe(
        XElement dia,
        DateTime fecha)
    {
        Dictionary<string, XElement> cielo =
            dia.Elements("estado_cielo")
                .Where(e => e.Value.Trim().Length > 0)
                .GroupBy(Periodo)
                .ToDictionary(g => g.Key, g => g.First());

        Dictionary<string, string> lluvia =
            dia.Elements("prob_precipitacion")
                .GroupBy(Periodo)
                .ToDictionary(g => g.Key, g => g.First().Value);

        // Temperatura a las 6, 12, 18 y 24 (solo los primeros días).
        Dictionary<int, int> datos =
            (dia.Element("temperatura")?.Elements("dato") ?? Enumerable.Empty<XElement>())
                .Select(e => (Hora: (int?)e.Attribute("hora"), Valor: Entero(e.Value)))
                .Where(x => x.Hora is not null && x.Valor is not null)
                .GroupBy(x => x.Hora!.Value)
                .ToDictionary(g => g.Key, g => g.First().Valor!.Value);

        string[][] juegos =
        {
            new[] { "00-06", "06-12", "12-18", "18-24" },
            new[] { "00-12", "12-24" },
            new[] { "00-24" }
        };

        string[] periodos =
            juegos.FirstOrDefault(j => j.All(cielo.ContainsKey)) ?? Array.Empty<string>();

        return periodos
            .Select(p =>
            {
                int desde = int.Parse(p[..2], CultureInfo.InvariantCulture);
                int hasta = int.Parse(p[3..], CultureInfo.InvariantCulture);

                return new HoraTiempo
                {
                    Etiqueta = p == "00-24" ? "Todo el día" : $"{desde}–{hasta} h",
                    Desde = fecha.AddHours(desde),
                    Hasta = fecha.AddHours(hasta),
                    Cielo = cielo[p].Value.Trim(),
                    Descripcion = (string?)cielo[p].Attribute("descripcion") ?? "",
                    Temperatura = hasta - desde == 6 && datos.TryGetValue(hasta, out int t) ? t : null,
                    ProbLluvia = Entero(lluvia.GetValueOrDefault(p))
                };
            })
            .ToList();
    }


    /// <summary>Icono (tiempo_*) de un código de cielo de AEMET.</summary>
    public static string Icono(string codigo)
    {
        bool noche = codigo.EndsWith('n');

        _ = int.TryParse(codigo.TrimEnd('n'), out int c);

        return c switch
        {
            11 => noche ? "tiempo_luna" : "tiempo_sol",
            12 or 13 or 17 => noche ? "tiempo_luna_nubes" : "tiempo_sol_nubes",
            >= 23 and <= 26 => "tiempo_lluvia",
            >= 43 and <= 46 => "tiempo_llovizna",
            (>= 33 and <= 36) or (>= 71 and <= 74) => "tiempo_nieve",
            (>= 51 and <= 54) or (>= 61 and <= 64) => "tiempo_tormenta",
            81 or 82 or 83 => "tiempo_niebla",
            _ => "tiempo_nubes"
        };
    }


    // =========================================================================
    // AVISOS (METEOALERTA)
    // =========================================================================

    private static async Task<List<AvisoTiempo>> AvisosEntreAsync(
        LugarTiempo lugar,
        DateTime inicio,
        DateTime fin,
        bool soloCache,
        CancellationToken ct)
    {
        var lista = new List<AvisoTiempo>();

        if (lugar.Latitud == 0 && lugar.Longitud == 0)
        {
            return lista;
        }

        XDocument? rss =
            await DescargarAsync(UrlAvisos, "aemet_avisos.xml", TimeSpan.FromMinutes(30), latin: false, soloCache, ct);

        if (rss is null)
        {
            return lista;
        }

        // Los archivos de cada zona se llaman …AFAZ{área}{provincia}{zona}…: solo
        // se miran los de la provincia del sitio y, de esos, los que lo contienen.
        string provincia = lugar.Ine[..2];

        DateTimeOffset desde = new(inicio);
        DateTimeOffset hasta = new(fin);

        IEnumerable<string> enlaces =
            rss.Descendants("item")
                .Select(i => ((string?)i.Element("link") ?? "").Trim())
                .Where(l => Regex.IsMatch(l, $@"AFAZ\d\d{provincia}\d\d"))
                .Distinct();

        foreach (string enlace in enlaces)
        {
            XDocument? cap =
                await DescargarAsync(enlace, "aemet_cap_" + Path.GetFileName(new Uri(enlace).AbsolutePath), TimeSpan.FromDays(7), latin: false, soloCache, ct);

            AvisoTiempo? aviso =
                cap is null ? null : LeerAviso(cap, lugar);

            if (aviso is not null
                && aviso.Inicio < hasta
                && aviso.Fin > desde
                && aviso.Fin > DateTimeOffset.Now)
            {
                lista.Add(aviso);
            }
        }

        // Si AEMET actualizó un aviso, vale el último que envió.
        return lista
            .GroupBy(a => a.Clave)
            .Select(g => g.OrderByDescending(a => a.Enviado).First())
            .OrderByDescending(a => a.Orden)
            .ThenBy(a => a.Inicio)
            .ToList();
    }


    /// <summary>
    /// Los avisos rojos vigentes (o por venir) que afectan a alguno de esos
    /// sitios —las congregaciones del circuito—, cada uno con los sitios a los
    /// que afecta. Solo se abren los avisos rojos de sus provincias. Null si
    /// no se pudo saber (sin red y sin nada guardado).
    /// </summary>
    public static async Task<List<(AvisoTiempo Aviso, List<int> Sitios)>?> AvisosRojosAsync(
        IReadOnlyList<LugarTiempo> sitios,
        CancellationToken ct)
    {
        XDocument? rss =
            await DescargarAsync(UrlAvisos, "aemet_avisos.xml", TimeSpan.FromMinutes(10), latin: false, soloCache: false, ct);

        if (rss is null)
        {
            return null;
        }

        HashSet<string> provincias =
            sitios.Select(s => s.Ine[..2]).ToHashSet();

        // El título ya dice el nivel («Aviso. Nivel rojo. Lluvias. …»).
        List<string> enlaces =
            rss.Descendants("item")
                .Where(i => Regex.IsMatch((string?)i.Element("title") ?? "", @"nivel\s+rojo", RegexOptions.IgnoreCase))
                .Select(i => ((string?)i.Element("link") ?? "").Trim())
                .Where(l => Regex.Match(l, @"AFAZ\d\d(\d\d)\d\d") is { Success: true } m && provincias.Contains(m.Groups[1].Value))
                .Distinct()
                .ToList();

        var lista = new List<(AvisoTiempo Aviso, List<int> Sitios)>();

        foreach (string enlace in enlaces)
        {
            XDocument? cap =
                await DescargarAsync(enlace, "aemet_cap_" + Path.GetFileName(new Uri(enlace).AbsolutePath), TimeSpan.FromDays(7), latin: false, soloCache: false, ct);

            if (cap is null
                || LeerCap(cap) is not { } leido
                || leido.Aviso.Nivel != "rojo"
                || leido.Aviso.Fin <= DateTimeOffset.Now)
            {
                continue;
            }

            List<int> afectados =
                Enumerable.Range(0, sitios.Count)
                    .Where(i => (sitios[i].Latitud != 0 || sitios[i].Longitud != 0)
                                && leido.Poligonos.Any(p => Contiene(p, sitios[i].Latitud, sitios[i].Longitud)))
                    .ToList();

            if (afectados.Count > 0)
            {
                lista.Add((leido.Aviso, afectados));
            }
        }

        // Si AEMET actualizó un aviso, vale el último que envió.
        return lista
            .GroupBy(a => a.Aviso.Clave)
            .Select(g => g.OrderByDescending(a => a.Aviso.Enviado).First())
            .OrderBy(a => a.Aviso.Inicio)
            .ToList();
    }


    private static AvisoTiempo? LeerAviso(
        XDocument cap,
        LugarTiempo lugar) =>
        LeerCap(cap) is { } leido
        && leido.Poligonos.Any(p => Contiene(p, lugar.Latitud, lugar.Longitud))
            ? leido.Aviso
            : null;


    /// <summary>Un aviso (CAP) de AEMET y los polígonos de su zona; null si no avisa de nada.</summary>
    private static (AvisoTiempo Aviso, List<string> Poligonos)? LeerCap(XDocument cap)
    {
        XElement? alerta = cap.Root;

        if (alerta is null
            || (string?)alerta.Element(Cap + "msgType") == "Cancel")
        {
            return null;
        }

        XElement? info =
            alerta.Elements(Cap + "info")
                .FirstOrDefault(i => ((string?)i.Element(Cap + "language") ?? "").StartsWith("es", StringComparison.OrdinalIgnoreCase))
            ?? alerta.Element(Cap + "info");

        if (info is null)
        {
            return null;
        }

        string nivel =
            (info.Elements(Cap + "parameter")
                .FirstOrDefault(p => (string?)p.Element(Cap + "valueName") == "AEMET-Meteoalerta nivel")
                ?.Element(Cap + "value")?.Value ?? "")
            .Trim()
            .ToLowerInvariant();

        if (nivel is "" or "verde")
        {
            return null;
        }

        List<string> poligonos =
            info.Elements(Cap + "area")
                .SelectMany(a => a.Elements(Cap + "polygon"))
                .Select(p => p.Value)
                .ToList();

        string[] fenomeno =
            (info.Element(Cap + "eventCode")?.Element(Cap + "value")?.Value ?? "").Split(';');

        var aviso = new AvisoTiempo
        {
            Nivel = nivel,
            Codigo = fenomeno.ElementAtOrDefault(0)?.Trim() ?? "",
            Fenomeno = fenomeno.ElementAtOrDefault(1)?.Trim() ?? "",
            Zona = (string?)info.Element(Cap + "area")?.Element(Cap + "areaDesc") ?? "",
            Descripcion = ((string?)info.Element(Cap + "description") ?? "").Trim(),
            Inicio = Fecha(info.Element(Cap + "onset")) ?? Fecha(info.Element(Cap + "effective")) ?? DateTimeOffset.MinValue,
            Fin = Fecha(info.Element(Cap + "expires")) ?? DateTimeOffset.MaxValue,
            Enviado = Fecha(alerta.Element(Cap + "sent")) ?? DateTimeOffset.MinValue
        };

        return (aviso, poligonos);
    }


    /// <summary>¿El punto está dentro del polígono «lat,lon lat,lon …» del aviso?</summary>
    private static bool Contiene(
        string poligono,
        double latitud,
        double longitud)
    {
        var puntos = new List<(double Lat, double Lon)>();

        foreach (string par in poligono.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] c = par.Split(',');

            if (c.Length == 2
                && double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                && double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                puntos.Add((lat, lon));
            }
        }

        bool dentro = false;

        for (int i = 0, j = puntos.Count - 1; i < puntos.Count; j = i++)
        {
            if ((puntos[i].Lat > latitud) != (puntos[j].Lat > latitud)
                && longitud < (puntos[j].Lon - puntos[i].Lon) * (latitud - puntos[i].Lat) / (puntos[j].Lat - puntos[i].Lat) + puntos[i].Lon)
            {
                dentro = !dentro;
            }
        }

        return dentro;
    }


    /// <summary>Borra de la caché los avisos de hace más de una semana.</summary>
    public static void BorrarAvisosViejos()
    {
        try
        {
            foreach (string ruta in Directory.GetFiles(CarpetaCache, "aemet_cap_*"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(ruta) > TimeSpan.FromDays(7))
                {
                    File.Delete(ruta);
                }
            }
        }
        catch
        {
        }
    }


    // =========================================================================
    // DESCARGAS Y CACHÉ
    // =========================================================================

    private static async Task<XDocument?> DescargarAsync(
        string url,
        string archivo,
        TimeSpan vigencia,
        bool latin,
        bool soloCache,
        CancellationToken ct)
    {
        string ruta = RutaCache(archivo);
        bool hay = File.Exists(ruta);

        if (soloCache
            || (hay && DateTime.UtcNow - File.GetLastWriteTimeUtc(ruta) < vigencia))
        {
            return hay ? LeerSeguro(File.ReadAllBytes(ruta), latin) : null;
        }

        try
        {
            // Con la fecha de lo guardado, AEMET contesta «sin cambios» (304) sin volver a mandarlo.
            string rutaFecha = ruta + ".fecha";

            using var peticion =
                new HttpRequestMessage(HttpMethod.Get, url);

            if (hay
                && File.Exists(rutaFecha)
                && DateTimeOffset.TryParse(File.ReadAllText(rutaFecha), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset modificado))
            {
                peticion.Headers.IfModifiedSince = modificado;
            }

            using HttpResponseMessage respuesta =
                await Http.SendAsync(peticion, ct);

            if (respuesta.StatusCode == HttpStatusCode.NotModified && hay)
            {
                File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow);

                return LeerSeguro(File.ReadAllBytes(ruta), latin);
            }

            respuesta.EnsureSuccessStatusCode();

            byte[] datos = await respuesta.Content.ReadAsByteArrayAsync(ct);

            // Solo se guarda si es XML de verdad, y de golpe (puede haber otra descarga a la vez).
            XDocument documento = Leer(datos, latin);

            Directory.CreateDirectory(CarpetaCache);

            string temporal = $"{ruta}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporal, datos, ct);
            File.Move(temporal, ruta, overwrite: true);

            if (respuesta.Content.Headers.LastModified is DateTimeOffset fecha)
            {
                File.WriteAllText(rutaFecha, fecha.ToString("o", CultureInfo.InvariantCulture));
            }

            return documento;
        }
        catch
        {
            // Sin red: lo último que se descargó, aunque sea viejo.
            return hay ? LeerSeguro(File.ReadAllBytes(ruta), latin) : null;
        }
    }


    // Los de AEMET por municipio van en ISO-8859-15 (para el español, igual que Latin-1).
    private static XDocument Leer(byte[] datos, bool latin) =>
        XDocument.Parse(
            (latin ? Encoding.Latin1 : Encoding.UTF8)
                .GetString(datos)
                .TrimStart('﻿'));


    private static XDocument? LeerSeguro(byte[] datos, bool latin)
    {
        try
        {
            return Leer(datos, latin);
        }
        catch
        {
            return null;
        }
    }


    private static string RutaCache(string archivo) =>
        Path.Combine(CarpetaCache, archivo);


    private static int? Entero(string? texto) =>
        int.TryParse(texto?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor)
            ? valor
            : null;


    private static DateTimeOffset? Fecha(XElement? elemento) =>
        DateTimeOffset.TryParse(elemento?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset valor)
            ? valor
            : null;
}
