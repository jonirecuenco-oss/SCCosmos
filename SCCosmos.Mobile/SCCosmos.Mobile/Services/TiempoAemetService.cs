using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;

// =============================================================================
// TIEMPO DE AEMET
// =============================================================================
//
// Previsión del día en el sitio donde se va a estar: el salón de la
// congregación que se visita esa semana (de martes a domingo) o, si no hay
// visita, la última ubicación del móvil. Todo sale de los archivos públicos de
// AEMET, sin clave:
//   - Por horas (3 días): https://www.aemet.es/xml/municipios_h/localidad_h_{ine}.xml
//   - Por tramos (7 días): https://www.aemet.es/xml/municipios/localidad_{ine}.xml
//   - Avisos (Meteoalerta, CAP): ver UrlAvisos. Cada aviso trae el polígono de
//     su zona, así que se sabe si afecta exactamente a ese punto.
// El municipio (código del INE) sale de Resources/Raw/municipios_ine.csv.
// Se guarda lo descargado en la caché: el widget y la revisión en segundo
// plano leen de ahí, y sin red se enseña lo último que hubo.

/// <summary>Una hora (o un tramo del día) de la previsión.</summary>
public sealed class HoraTiempo
{
    /// <summary>«10h» o, en los días sin previsión por horas, «Mañana», «Tarde»...</summary>
    public string Etiqueta { get; init; } = "";

    /// <summary>Hora del día (0-23); -1 en los tramos.</summary>
    public int Hora { get; init; } = -1;

    /// <summary>Código de cielo de AEMET («15», «26n»...).</summary>
    public string Cielo { get; init; } = "";

    public string Descripcion { get; init; } = "";

    public int? Temperatura { get; init; }

    /// <summary>Probabilidad de lluvia: AEMET la da por tramos de 6 horas.</summary>
    public int? ProbLluvia { get; init; }

    /// <summary>Lluvia prevista en esa hora (mm); 0,05 es «inapreciable».</summary>
    public double? Precipitacion { get; init; }

    public string Icono => TiempoAemetService.Icono(Cielo);

    /// <summary>
    /// En cada hora, los mm de lluvia (el dato horario de AEMET); en los
    /// tramos, su probabilidad. La probabilidad del día va arriba.
    /// </summary>
    public string TextoLluvia =>
        Precipitacion is double mm
            ? (mm >= 0.1 ? mm.ToString("0.#", CultureInfo.GetCultureInfo("es-ES")) + " mm" : mm > 0 ? "<0,1 mm" : "")
            : Hora < 0 && ProbLluvia is int p && p > 0 ? $"{p}%" : "";
}


/// <summary>Un aviso de AEMET (Meteoalerta) que afecta al sitio.</summary>
public sealed class AvisoTiempo
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public string Nivel { get; init; } = "";

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

    /// <summary>«Visita a …» o «Tu ubicación».</summary>
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


/// <summary>Sitio de la previsión: municipio del INE y coordenadas.</summary>
public sealed record LugarTiempo(
    string Ine,
    string Nombre,
    string Motivo,
    double Latitud,
    double Longitud);


public static class TiempoAemetService
{
    private const string UrlHoras =
        "https://www.aemet.es/xml/municipios_h/localidad_h_{0}.xml";

    private const string UrlDias =
        "https://www.aemet.es/xml/municipios/localidad_{0}.xml";

    private const string UrlAvisos =
        "https://www.aemet.es/documentos_d/eltiempo/prediccion/avisos/rss/CAP_AFAE_wah_RSS.xml";

    private const string ClaveUbicacion =
        "Tiempo_Ubicacion";

    private const string ClavePermisoPedido =
        "Tiempo_PermisoUbicacionPedido";

    private static readonly XNamespace Cap =
        "urn:oasis:names:tc:emergency:cap:1.2";

    private static readonly HttpClient Http =
        CrearHttp();


    private static HttpClient CrearHttp()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("SCCosmos/2.1 (Android)");

        return http;
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
            BuscarIne(visita.Value.Municipio, visita.Value.Provincia);

        return ine is null
            ? null
            : new LugarTiempo(
                ine,
                NombreMunicipio(ine, visita.Value.Municipio),
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
                return false;
            }

            Location? posicion =
                await Geolocation.GetLastKnownLocationAsync()
                ?? await Geolocation.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(10)));

            if (posicion is null)
            {
                return false;
            }

            Placemark? sitio =
                (await Geocoding.GetPlacemarksAsync(posicion.Latitude, posicion.Longitude))
                ?.FirstOrDefault();

            string? ine =
                await Task.Run(() =>
                    BuscarIne(sitio?.Locality, sitio?.SubAdminArea)
                    ?? BuscarIne(sitio?.SubLocality, sitio?.SubAdminArea));

            if (ine is null)
            {
                return false;
            }

            Preferences.Default.Set(
                ClaveUbicacion,
                string.Join(
                    "|",
                    ine,
                    NombreMunicipio(ine, sitio?.Locality ?? ""),
                    posicion.Latitude.ToString(CultureInfo.InvariantCulture),
                    posicion.Longitude.ToString(CultureInfo.InvariantCulture)));

            return true;
        }
        catch
        {
            return false;
        }
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
                FileSystem.OpenAppPackageFileAsync("municipios_ine.csv").GetAwaiter().GetResult();

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


    private static string NombreMunicipio(
        string ine,
        string otro)
    {
        CargarMunicipios();

        return _nombres!.TryGetValue(ine, out string? nombre)
            ? Variantes(nombre).Last()
            : otro;
    }


    private static string Normalizar(string texto)
    {
        var resultado = new StringBuilder(texto.Length);

        foreach (char c in texto.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);

            if (categoria == UnicodeCategory.NonSpacingMark)
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

    public static Task<TiempoDia?> ObtenerAsync(
        DateTime dia,
        CancellationToken ct) =>
        ObtenerInternoAsync(dia, soloCache: false, ct);


    /// <summary>Sin red: lo último descargado (para el widget).</summary>
    public static TiempoDia? ObtenerDeCache(DateTime dia)
    {
        try
        {
            return ObtenerInternoAsync(dia, soloCache: true, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return null;
        }
    }


    private static async Task<TiempoDia?> ObtenerInternoAsync(
        DateTime dia,
        bool soloCache,
        CancellationToken ct)
    {
        LugarTiempo? lugar =
            LugarParaDia(dia);

        if (lugar is null)
        {
            return null;
        }

        string archivoHoras = $"aemet_h_{lugar.Ine}.xml";

        XDocument? horas =
            await DescargarAsync(string.Format(UrlHoras, lugar.Ine), archivoHoras, TimeSpan.FromMinutes(60), latin: true, soloCache, ct);

        XDocument? dias =
            await DescargarAsync(string.Format(UrlDias, lugar.Ine), $"aemet_d_{lugar.Ine}.xml", TimeSpan.FromHours(3), latin: true, soloCache, ct);

        string fecha =
            dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        XElement? diaHoras = DiaDe(horas, fecha);
        XElement? diaTramos = DiaDe(dias, fecha);

        if (diaHoras is null && diaTramos is null)
        {
            return new TiempoDia
            {
                Fecha = dia.Date,
                Lugar = lugar.Nombre,
                Motivo = lugar.Motivo,
                SinPrevision = horas is not null || dias is not null
            };
        }

        List<HoraTiempo> lista =
            diaHoras is not null
                ? HorasDe(diaHoras)
                : TramosDe(diaTramos!);

        int? maxima = Entero(diaTramos?.Element("temperatura")?.Element("maxima")?.Value);
        int? minima = Entero(diaTramos?.Element("temperatura")?.Element("minima")?.Value);

        if (maxima is null && lista.Any(h => h.Temperatura is not null))
        {
            maxima = lista.Max(h => h.Temperatura);
            minima = lista.Min(h => h.Temperatura);
        }

        string rutaHoras = RutaCache(archivoHoras);

        return new TiempoDia
        {
            Fecha = dia.Date,
            Lugar = lugar.Nombre,
            Motivo = lugar.Motivo,
            PorHoras = diaHoras is not null,
            Horas = lista,
            Maxima = maxima,
            Minima = minima,
            ProbLluviaDia =
                Entero(diaTramos?.Elements("prob_precipitacion").FirstOrDefault(e => (string?)e.Attribute("periodo") == "00-24")?.Value)
                ?? diaTramos?.Elements("prob_precipitacion").Select(e => Entero(e.Value)).Max(),
            Avisos = await AvisosParaDiaAsync(lugar, dia.Date, soloCache, ct),
            Actualizado = File.Exists(rutaHoras) ? File.GetLastWriteTime(rutaHoras) : default
        };
    }


    private static XElement? DiaDe(
        XDocument? documento,
        string fecha) =>
        documento?.Root
            ?.Element("prediccion")
            ?.Elements("dia")
            .FirstOrDefault(d => (string?)d.Attribute("fecha") == fecha);


    /// <summary>Hora a hora: cielo, temperatura y probabilidad de lluvia (de su tramo).</summary>
    private static List<HoraTiempo> HorasDe(XElement dia)
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


    /// <summary>Días sin previsión por horas: madrugada, mañana, tarde y noche (o medio día).</summary>
    private static List<HoraTiempo> TramosDe(XElement dia)
    {
        Dictionary<string, XElement> cielo =
            dia.Elements("estado_cielo")
                .Where(e => e.Value.Trim().Length > 0)
                .GroupBy(e => (string?)e.Attribute("periodo") ?? "")
                .ToDictionary(g => g.Key, g => g.First());

        Dictionary<string, string> lluvia =
            dia.Elements("prob_precipitacion")
                .GroupBy(e => (string?)e.Attribute("periodo") ?? "")
                .ToDictionary(g => g.Key, g => g.First().Value);

        string[][] juegos =
        {
            new[] { "00-06", "06-12", "12-18", "18-24" },
            new[] { "00-12", "12-24" },
            new[] { "00-24" }
        };

        string[] periodos =
            juegos.FirstOrDefault(j => j.All(cielo.ContainsKey)) ?? Array.Empty<string>();

        return periodos
            .Select(p => new HoraTiempo
            {
                Etiqueta = p switch
                {
                    "00-06" => "Madrug.",
                    "06-12" or "00-12" => "Mañana",
                    "12-18" or "12-24" => "Tarde",
                    "18-24" => "Noche",
                    _ => "Día"
                },
                Cielo = cielo[p].Value.Trim(),
                Descripcion = (string?)cielo[p].Attribute("descripcion") ?? "",
                ProbLluvia = Entero(lluvia.GetValueOrDefault(p))
            })
            .ToList();
    }


    /// <summary>Icono (Resources/Images/tiempo_*.svg) de un código de cielo de AEMET.</summary>
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

    private static async Task<List<AvisoTiempo>> AvisosParaDiaAsync(
        LugarTiempo lugar,
        DateTime dia,
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

        DateTimeOffset desde = new(dia);
        DateTimeOffset hasta = desde.AddDays(1);

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


    private static AvisoTiempo? LeerAviso(
        XDocument cap,
        LugarTiempo lugar)
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

        bool dentro =
            info.Elements(Cap + "area")
                .SelectMany(a => a.Elements(Cap + "polygon"))
                .Any(p => Contiene(p.Value, lugar.Latitud, lugar.Longitud));

        if (!dentro)
        {
            return null;
        }

        string fenomeno =
            (info.Element(Cap + "eventCode")?.Element(Cap + "value")?.Value ?? "")
                .Split(';')
                .ElementAtOrDefault(1)
                ?.Trim() ?? "";

        return new AvisoTiempo
        {
            Nivel = nivel,
            Fenomeno = fenomeno,
            Zona = (string?)info.Element(Cap + "area")?.Element(Cap + "areaDesc") ?? "",
            Descripcion = ((string?)info.Element(Cap + "description") ?? "").Trim(),
            Inicio = Fecha(info.Element(Cap + "onset")) ?? Fecha(info.Element(Cap + "effective")) ?? DateTimeOffset.MinValue,
            Fin = Fecha(info.Element(Cap + "expires")) ?? DateTimeOffset.MaxValue,
            Enviado = Fecha(alerta.Element(Cap + "sent")) ?? DateTimeOffset.MinValue
        };
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


    // =========================================================================
    // EN SEGUNDO PLANO (cada 2 horas, también con la app cerrada)
    // =========================================================================

    /// <summary>
    /// Descarga la previsión de hoy, mañana y pasado en el sitio de cada día y
    /// avisa (notificación) de los avisos nuevos.
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

            TiempoDia? tiempo = await ObtenerInternoAsync(dia, soloCache: false, ct);

            if (tiempo is not null)
            {
                avisos.AddRange(tiempo.Avisos.Select(a => (a, lugar)));
            }
        }

        BorrarAvisosViejos();

#if ANDROID
        SCCosmos.Mobile.Platforms.Android.TiempoAvisosAndroid.Notificar(avisos);
#endif
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
            byte[] datos = await Http.GetByteArrayAsync(url, ct);

            // Solo se guarda si es XML de verdad.
            XDocument documento = Leer(datos, latin);

            await File.WriteAllBytesAsync(ruta, datos, ct);

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


    private static void BorrarAvisosViejos()
    {
        try
        {
            foreach (string ruta in Directory.GetFiles(FileSystem.CacheDirectory, "aemet_cap_*"))
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


    private static string RutaCache(string archivo) =>
        Path.Combine(FileSystem.CacheDirectory, archivo);


    private static int? Entero(string? texto) =>
        int.TryParse(texto?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor)
            ? valor
            : null;


    private static DateTimeOffset? Fecha(XElement? elemento) =>
        DateTimeOffset.TryParse(elemento?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset valor)
            ? valor
            : null;
}
