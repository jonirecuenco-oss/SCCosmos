#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SCCosmos.Sincro;

// ============================================================
// PROGRAMA DE LA VISITA DESDE LA WEB
//
// Los ancianos rellenan el programa en una página (servida por
// ServidorProgramaWeb) y lo que envían se guarda en la base. Aquí se
// convierte en los mismos eventos que da el PDF del programa
// (ImportadorProgramaVisita), para guardarlo igual. Los asuntos a
// tratar van aparte: no son eventos con día y hora.
//
// Lo enviado (versión 1):
// {
//   "version": 1,
//   "reuniones": {
//     "entreSemana": { "hora", "lugar", "lat", "lon" },          (martes)
//     "finDeSemana": { "dia", "hora", "lugar", "lat", "lon" },   (sábado o domingo)
//     "ancianos":    { "hora", "lugar", "lat", "lon" },          (viernes)
//     "precursores": { "dia", "hora", "lugar", "lat", "lon" }    (miércoles o sábado)
//   },
//   "comidas":     [ { "dia", "familia", "telefono", "direccion", "lat", "lon" } ],
//   "alojamiento": { "familia", "telefono", "direccion", "lat", "lon" },
//   "predicacion": [ { "franja": "Miércoles mañana", "hora", "lugar", "lat", "lon",
//                      "sc":     [ { "nombre", "faceta", "leccion", "notas" } ],
//                      "esposa": [ ... ] } ],
//   "pastoreo":    [ { "nombre", "dia", "hora", "direccion", "lat", "lon", "descripcion" } ],
//   "asuntos":     [ "..." ]
// }
// Los archivos (S-61, S-21, S-88…) no van aquí: se suben aparte
// (ServidorProgramaWeb, ArchivosProgramaWeb).
// Las comidas van siempre a las 13:30 y el alojamiento a las 22:00, como en
// el PDF; las visitas de pastoreo llevan su acompañante («acompanante»). La hora y el
// lugar de cada salida de predicación se rellenan en Reuniones, pero
// viajan en su franja de "predicacion". En la del medio día libre
// (Configuración) no va nadie con el SC ni con su esposa, y solo hay
// comidas los días que se come con la congregación (Configuración).
// ============================================================

public static class ProgramaWeb
{
    /// <summary>Días de la visita, de martes a domingo.</summary>
    public static readonly string[] Dias =
    {
        "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"
    };

    /// <summary>Las franjas de predicación, las mismas que el PDF.</summary>
    public static readonly string[] Franjas =
    {
        "Miércoles mañana", "Miércoles tarde",
        "Jueves mañana", "Jueves tarde",
        "Viernes mañana", "Viernes tarde",
        "Sábado mañana", "Domingo mañana"
    };

    public static readonly string[] Facetas =
    {
        "Territorio", "Curso bíblico", "Exhibidores"
    };

    public const int MaxPersonasPorFranja = 2;

    public const int MaxVisitasPastoreo = 3;

    public const int MaxAsuntos = 10;

    private const int MaxTexto = 300;


    // ---- Medio día libre del SC y su esposa (en Configuración) ----

    public const string ClaveMedioDiaLibre = "MedioDiaLibre";

    /// <summary>Lo que se puede elegir como medio día libre.</summary>
    public static readonly string[] OpcionesMedioDiaLibre =
    {
        "Miércoles mañana", "Miércoles tarde",
        "Jueves mañana", "Jueves tarde",
        "Viernes mañana", "Viernes tarde"
    };

    /// <summary>
    /// El medio día libre guardado o, sin elegir, el viernes por la mañana
    /// (el que el formulario ya dejaba sin predicación).
    /// </summary>
    public static string MedioDiaLibre(string? guardado) =>
        OpcionesMedioDiaLibre.FirstOrDefault(o => Igual(o, guardado ?? "")) ?? "Viernes mañana";


    // ---- Días que comen con la congregación (en Configuración) ----

    public const string ClaveDiasComida = "DiasComida";

    /// <summary>Los días que puede haber comida con la congregación.</summary>
    public static readonly string[] DiasComidaPosibles =
    {
        "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"
    };

    /// <summary>
    /// Los días guardados («Jueves,Sábado»; «Ninguno» si no hay ninguno).
    /// Sin guardar, todos (lo que ya hacía el formulario).
    /// </summary>
    public static string[] DiasComida(string? guardado)
    {
        string texto = (guardado ?? "").Trim();

        if (Igual(texto, "Ninguno"))
        {
            return Array.Empty<string>();
        }

        string[] partes = texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] dias = DiasComidaPosibles.Where(d => partes.Any(p => Igual(p, d))).ToArray();

        return dias.Length > 0 ? dias : DiasComidaPosibles.ToArray();
    }

    /// <summary>Lo que se guarda para esos días, en su orden («Ninguno» si no hay).</summary>
    public static string TextoDiasComida(IEnumerable<string> dias)
    {
        List<string> lista = DiasComidaPosibles.Where(d => dias.Any(x => Igual(x, d))).ToList();

        return lista.Count == 0 ? "Ninguno" : string.Join(",", lista);
    }

    /// <summary>
    /// Los días de comida propios de una congregación (página Circuito). Vacía:
    /// los de Configuración. Se sincroniza como el resto de Configuracion.
    /// </summary>
    public static string ClaveDiasComidaCongregacion(string congregacion) =>
        $"ProgramaWeb_DiasComida_{congregacion}";

    /// <summary>Los días que coméis con esa congregación: los suyos o, sin elegir, los de Configuración.</summary>
    public static string[] DiasComidaDe(string? congregacion, Func<string, string> leerConfiguracion)
    {
        string propios = string.IsNullOrWhiteSpace(congregacion)
            ? ""
            : (leerConfiguracion(ClaveDiasComidaCongregacion(congregacion.Trim())) ?? "").Trim();

        return DiasComida(propios.Length > 0 ? propios : leerConfiguracion(ClaveDiasComida));
    }

    /// <summary>Resumen corto para una tabla: «Todos», «Ninguno» o «Jue, Sáb».</summary>
    public static string ResumenDiasComida(IReadOnlyCollection<string> dias) =>
        dias.Count == 0 ? "Ninguno"
        : dias.Count == DiasComidaPosibles.Length ? "Todos"
        : string.Join(", ", DiasComidaPosibles.Where(d => dias.Any(x => Igual(x, d))).Select(d => d.Substring(0, 3)));


    // ---- El enlace del formulario (lo sirve un PC con SCCosmos abierto y publicado en Internet) ----

    /// <summary>
    /// La dirección para los enlaces que leen todas las versiones. Sin servidor
    /// elegido, la del último PC que creó un enlace (su dirección de Tailscale);
    /// con servidor elegido, la de ese servidor.
    /// </summary>
    public const string ClaveDireccion = "ProgramaWeb_Direccion";

    /// <summary>
    /// El servidor del formulario elegido en Configuración («https://mi-pc.red.ts.net»).
    /// Con él puesto, todos los enlaces van a esa dirección, se creen en el PC o en
    /// el móvil, y ningún equipo la cambia por su cuenta. Se sincroniza.
    /// </summary>
    public const string ClaveServidor = "ProgramaWeb_Servidor";

    /// <summary>
    /// La dirección para los enlaces: la del servidor elegido; sin elegir, la del
    /// último PC que creó un enlace (como hasta ahora). «» si aún no se sabe.
    /// </summary>
    public static string DireccionEnlaces(Func<string, string> leerConfiguracion)
    {
        string servidor = NormalizarDireccion(leerConfiguracion(ClaveServidor));

        return servidor.Length > 0
            ? servidor
            : NormalizarDireccion(leerConfiguracion(ClaveDireccion));
    }

    /// <summary>
    /// «https://mi-pc.red.ts.net»: con «https://» si no lo lleva y sin «/» al final.
    /// «» si no es la dirección de un servidor web (sin rutas, consultas ni usuario).
    /// </summary>
    public static string NormalizarDireccion(string? texto)
    {
        string direccion = (texto ?? "").Trim().TrimEnd('/');

        if (direccion.Length == 0)
        {
            return "";
        }

        if (!direccion.Contains("://", StringComparison.Ordinal))
        {
            direccion = "https://" + direccion;
        }

        return Uri.TryCreate(direccion, UriKind.Absolute, out Uri? uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && uri.Host.Length > 0
               && uri.AbsolutePath == "/"
               && uri.Query.Length == 0
               && uri.Fragment.Length == 0
               && uri.UserInfo.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority)
            : "";
    }

    /// <summary>
    /// Adónde mandar la página pedida con la dirección «host» (cabecera Host):
    /// al servidor elegido, si llegó por otra dirección (un enlace enviado antes
    /// de cambiar de servidor). null: servirla aquí. Nunca redirige desde el
    /// propio servidor elegido («propia», su dirección) ni lo pedido en el
    /// propio equipo, así que no puede dar vueltas.
    /// </summary>
    public static string? DestinoRedireccion(string? host, string? servidor, string? propia)
    {
        string elegido = NormalizarDireccion(servidor);

        if (elegido.Length == 0
            || string.IsNullOrWhiteSpace(host)
            || !Uri.TryCreate("http://" + host.Trim(), UriKind.Absolute, out Uri? llegada)
            || llegada.IsLoopback
            || string.Equals(NormalizarDireccion(propia), elegido, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Equals(llegada.Host.TrimEnd('.'), new Uri(elegido).Host, StringComparison.OrdinalIgnoreCase)
            ? null
            : elegido;
    }

    /// <summary>La variable de las plantillas de correo que se cambia por el enlace.</summary>
    public const string VariableEnlace = "$_ENLACEDOCU";

    public static string Enlace(string direccion, string codigo) =>
        direccion.TrimEnd('/') + "/programa/" + codigo;

    /// <summary>El enlace, para el cuerpo HTML de un correo.</summary>
    public static string EnlaceHtml(string enlace) =>
        $"<a href=\"{enlace}\">{enlace}</a>";


    // ---- Claves en Configuracion (por congregación y mes de la visita) ----

    public static string ClaveCodigo(string congregacion, string mes) =>
        $"ProgramaWeb_Codigo_{congregacion}_{mes}";

    public static string ClaveRecibido(string congregacion, string mes) =>
        $"ProgramaWeb_Recibido_{congregacion}_{mes}";

    public static string ClaveAplicado(string congregacion, string mes) =>
        $"ProgramaWeb_Aplicado_{congregacion}_{mes}";

    /// <summary>Cómo quedó el programa del mes al pasar lo recibido (si cambia, alguien lo ha tocado).</summary>
    public static string ClaveHuella(string congregacion, string mes) =>
        $"ProgramaWeb_Huella_{congregacion}_{mes}";

    public const string PrefijoClaveCodigo = "ProgramaWeb_Codigo_";

    /// <summary>
    /// La última vez que pulsaron «Enviar» y cuántos archivos había subidos
    /// («2026-11-18T10:30:00|5»). Los borradores posteriores no la cambian.
    /// Con ella el móvil avisa de cada envío.
    /// </summary>
    public static string ClaveEnviado(string congregacion, string mes) =>
        $"ProgramaWeb_Enviado_{congregacion}_{mes}";

    /// <summary>
    /// Cuántos archivos han subido los ancianos y en qué PC están
    /// («{"n":5,"en":"https://pc.red.ts.net"}»). Lo anota el PC que sirve el
    /// formulario y se sincroniza: así los demás pueden descargarlos de él.
    /// </summary>
    public static string ClaveArchivos(string congregacion, string mes) =>
        $"ProgramaWeb_Archivos_{congregacion}_{mes}";

    /// <summary>
    /// Los puntos a tratar de la agenda (JSON con la lista) cuando el
    /// superintendente los cambia; sin ella, los que enviaron los ancianos.
    /// </summary>
    public static string ClaveAgendaPuntos(string congregacion, string mes) =>
        $"ProgramaWeb_AgendaPuntos_{congregacion}_{mes}";

    public static string TextoEnviado(DateTime cuando, int archivos) =>
        cuando.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "|" + Math.Max(0, archivos);

    /// <summary>Lo guardado con TextoEnviado (null si no hay o no se entiende).</summary>
    public static (DateTime Cuando, int Archivos)? LeerEnviado(string? valor)
    {
        string[] partes = (valor ?? "").Trim().Split('|');

        if (!DateTime.TryParseExact(partes[0], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime cuando))
        {
            return null;
        }

        int archivos = partes.Length > 1 && int.TryParse(partes[1], out int n) ? Math.Max(0, n) : 0;

        return (cuando, archivos);
    }

    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

    /// <summary>«diciembre de 2026» para «2026-12» (sin depender del idioma del equipo).</summary>
    public static string TextoMes(string mes) =>
        DateTime.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime primero)
            ? $"{Meses[primero.Month - 1]} de {primero.Year}"
            : mes;

    /// <summary>La notificación de un envío del formulario (otraVez: ya lo habían enviado antes).</summary>
    public static (string Titulo, string Texto) AvisoEnviado(string congregacion, string mes, int archivos, bool otraVez) =>
        ($"Programa recibido: {congregacion}",
         (otraVez ? "Los ancianos han vuelto a enviar" : "Los ancianos han enviado")
         + $" el programa de la visita de {TextoMes(mes)}"
         + (archivos switch { 0 => "", 1 => ", con 1 archivo", _ => $", con {archivos} archivos" })
         + ".");


    /// <summary>Código secreto del enlace de una visita (128 bits, sin «+/=»).</summary>
    public static string NuevoCodigo()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(16);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }


    /// <summary>Un código con la forma de los que se reparten (para no buscar basura).</summary>
    public static bool CodigoValido(string? codigo) =>
        codigo is { Length: >= 16 and <= 64 }
        && codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');


    /// <summary>
    /// Lo enviado, convertido en eventos del programa (como el PDF) y la
    /// lista de asuntos a tratar. Lo que no sea válido se ignora. En la
    /// franja del medio día libre no va nadie con el SC ni con su esposa, y
    /// las comidas solo cuentan los días de <paramref name="diasComida"/>
    /// (null: todos). Cada salida a predicar programada va para el SC y, si
    /// está casado, para su esposa, aunque todavía no vaya nadie con ellos.
    /// </summary>
    public static (List<EventoProgramaVisita> Eventos, List<string> Asuntos) AEventos(
        string json,
        string medioDiaLibre = "",
        IReadOnlyCollection<string>? diasComida = null,
        bool casado = true)
    {
        var eventos = new List<EventoProgramaVisita>();
        var asuntos = new List<string>();

        JsonObject? raiz;

        try
        {
            raiz = JsonNode.Parse(json) as JsonObject;
        }
        catch
        {
            raiz = null;
        }

        if (raiz is null)
        {
            return (eventos, asuntos);
        }

        // ---- Reuniones ----
        // La de A/SM, siempre el viernes; la de precursores, el miércoles o
        // el sábado. (La «reunión para la predicación» del viernes ya no se
        // pregunta: si un envío anterior la trae, se ignora.)
        if (raiz["reuniones"] is JsonObject reuniones)
        {
            Reunion(reuniones["entreSemana"], "Martes", "Reunión entre semana");
            Reunion(reuniones["finDeSemana"], null, "Reunión fin de semana", "Sábado", "Domingo");
            Reunion(reuniones["ancianos"], "Viernes", "Reunión de A/SM");
            Reunion(reuniones["precursores"], null, "Reunión de precursores", "Miércoles", "Sábado");
        }

        void Reunion(JsonNode? nodo, string? diaFijo, string tipo, params string[] diasValidos)
        {
            if (nodo is not JsonObject r)
            {
                return;
            }

            string dia = diaFijo ?? Dia(Texto(r, "dia"));
            string hora = Hora(Texto(r, "hora"));

            if (dia.Length == 0
                || hora.Length == 0
                || (diasValidos.Length > 0 && !diasValidos.Contains(dia)))
            {
                return;
            }

            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = dia,
                Turno = "General",
                Hora = hora,
                TipoActividad = tipo,
                Responsable = "Ambos",
                Ubicacion = Texto(r, "lugar"),
                Latitud = Numero(r, "lat"),
                Longitud = Numero(r, "lon")
            });
        }

        // ---- Hospitalidad: la comida de cada día ----
        foreach (JsonObject comida in Lista(raiz["comidas"]).Take(Dias.Length))
        {
            string dia = Dia(Texto(comida, "dia"));
            string familia = Texto(comida, "familia");

            // Solo los días que se come con la congregación.
            if (dia.Length == 0
                || familia.Length == 0
                || (diasComida is not null && !diasComida.Contains(dia)))
            {
                continue;
            }

            // Siempre a las 13:30 (como el PDF), aunque llegue otra hora.
            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = dia,
                Turno = "General",
                Hora = "13:30",
                TipoActividad = "Comida",
                Responsable = "Ambos",
                Notas = Juntar(familia, Texto(comida, "telefono")),
                Ubicacion = Texto(comida, "direccion"),
                Latitud = Numero(comida, "lat"),
                Longitud = Numero(comida, "lon")
            });
        }

        // ---- Alojamiento: un evento cada día, como el PDF ----
        if (raiz["alojamiento"] is JsonObject alojamiento
            && Texto(alojamiento, "familia") is { Length: > 0 } familiaAlojamiento)
        {
            foreach (string dia in Dias)
            {
                eventos.Add(new EventoProgramaVisita
                {
                    DiaSemana = dia,
                    Turno = "General",
                    Hora = "22:00",
                    TipoActividad = "Alojamiento",
                    Responsable = "Ambos",
                    Notas = Juntar(familiaAlojamiento, Texto(alojamiento, "telefono")),
                    Ubicacion = Texto(alojamiento, "direccion"),
                    Latitud = Numero(alojamiento, "lat"),
                    Longitud = Numero(alojamiento, "lon")
                });
            }
        }

        // ---- Predicación: por franja, hasta 2 personas con cada uno ----
        // Cada franja, una vez; una que no valga se salta sin perder las
        // siguientes.
        var franjasVistas = new HashSet<string>();

        foreach (JsonObject salida in Lista(raiz["predicacion"]).Take(2 * Franjas.Length))
        {
            string franja = Franjas.FirstOrDefault(f => Igual(f, Texto(salida, "franja"))) ?? "";

            if (franja.Length == 0 || !franjasVistas.Add(franja))
            {
                continue;
            }

            string dia = franja.Split(' ')[0];
            string turno = franja.EndsWith("mañana", StringComparison.Ordinal) ? "Mañana" : "Tarde";
            string hora = Hora(Texto(salida, "hora"));
            string lugar = Texto(salida, "lugar");
            double lat = Numero(salida, "lat");
            double lon = Numero(salida, "lon");

            // Medio día libre: la salida se puede programar, pero ellos no van.
            bool libre = Igual(franja, medioDiaLibre);

            string planSc = libre ? "" : Personas(salida["sc"]);
            string planEsposa = libre ? "" : Personas(salida["esposa"]);

            void Salida(string responsable, string notas) =>
                eventos.Add(new EventoProgramaVisita
                {
                    DiaSemana = dia,
                    Turno = turno,
                    Hora = hora,
                    TipoActividad = "Predicación",
                    Responsable = responsable,
                    Notas = notas,
                    Ubicacion = lugar,
                    Latitud = lat,
                    Longitud = lon
                });

            // Programada: con hora o con alguien que va.
            if (hora.Length == 0 && planSc.Length == 0 && planEsposa.Length == 0)
            {
                continue;
            }

            // Medio día libre: la salida queda, pero ellos no van.
            if (libre)
            {
                Salida("", "Medio día libre");
                continue;
            }

            // Para los dos, aunque todavía no vaya nadie con ellos. Sin esposa,
            // quien se hubiera puesto con ella va en la del SC.
            if (casado)
            {
                Salida("Esposa", planEsposa);
                Salida("SC", planSc);
            }
            else
            {
                Salida("SC", Juntar(planSc, planEsposa));
            }
        }

        // ---- Visitas de pastoreo ----
        foreach (JsonObject visita in Lista(raiz["pastoreo"]).Take(MaxVisitasPastoreo))
        {
            string nombre = Texto(visita, "nombre");
            string dia = Dia(Texto(visita, "dia"));

            // De miércoles a domingo: el martes no hay visitas de pastoreo.
            if (nombre.Length == 0 || dia.Length == 0 || dia == "Martes")
            {
                continue;
            }

            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = dia,
                Turno = "General",
                Hora = Hora(Texto(visita, "hora")),
                TipoActividad = "Visita de pastoreo",
                Responsable = "SC",
                // Quién visita, con quién va (como los publicadores de una salida) y la descripción.
                Notas = Juntar(
                    Juntar(nombre, Texto(visita, "acompanante") is { Length: > 0 } acompanante ? "Acompañante: " + acompanante : ""),
                    Texto(visita, "descripcion")),
                Ubicacion = Texto(visita, "direccion"),
                Latitud = Numero(visita, "lat"),
                Longitud = Numero(visita, "lon")
            });
        }

        // ---- Asuntos a tratar ----
        if (raiz["asuntos"] is JsonArray listaAsuntos)
        {
            foreach (JsonNode? nodo in listaAsuntos.Take(MaxAsuntos))
            {
                string asunto = Recortar(nodo is JsonValue v && v.TryGetValue(out string? s) ? s : "");

                if (asunto.Length > 0)
                {
                    asuntos.Add(asunto);
                }
            }
        }

        // Orden: por día y hora (sin hora, al final del día).
        List<EventoProgramaVisita> ordenados = eventos
            .OrderBy(e => Array.IndexOf(Dias, e.DiaSemana))
            .ThenBy(e => e.Hora.Length == 0 ? "99:99" : e.Hora.PadLeft(5, '0'))
            .ToList();

        return (ordenados, asuntos);
    }


    /// <summary>
    /// «Juan Pérez (Curso bíblico, lección 12) — lleva el libro; Ana Ruiz
    /// (Territorio)»: las personas de una salida, para las notas.
    /// </summary>
    private static string Personas(JsonNode? nodo)
    {
        var partes = new List<string>();

        foreach (JsonObject persona in Lista(nodo).Take(MaxPersonasPorFranja))
        {
            string nombre = Texto(persona, "nombre");

            if (nombre.Length == 0)
            {
                continue;
            }

            string faceta = Facetas.FirstOrDefault(f => Igual(f, Texto(persona, "faceta"))) ?? "";

            int leccion = (int)Numero(persona, "leccion");

            string detalle =
                faceta == "Curso bíblico" && leccion is >= 1 and <= 60
                    ? $"{faceta}, lección {leccion}"
                    : faceta;

            string texto = detalle.Length > 0 ? $"{nombre} ({detalle})" : nombre;

            string notas = Texto(persona, "notas");

            partes.Add(notas.Length > 0 ? $"{texto} — {notas}" : texto);
        }

        return string.Join("; ", partes);
    }


    private static IEnumerable<JsonObject> Lista(JsonNode? nodo) =>
        nodo is JsonArray lista
            ? lista.OfType<JsonObject>()
            : Enumerable.Empty<JsonObject>();


    private static string Texto(JsonObject objeto, string clave) =>
        Recortar(objeto[clave] is JsonValue v && v.TryGetValue(out string? s) ? s : "");


    private static double Numero(JsonObject objeto, string clave)
    {
        if (objeto[clave] is not JsonValue v)
        {
            return 0;
        }

        if (v.TryGetValue(out double d) && double.IsFinite(d))
        {
            return d;
        }

        return v.TryGetValue(out string? s)
               && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double t)
               && double.IsFinite(t)
            ? t
            : 0;
    }


    private static string Recortar(string? texto)
    {
        string t = (texto ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();

        return t.Length > MaxTexto ? t.Substring(0, MaxTexto) : t;
    }


    /// <summary>«sabado» → «Sábado» (de martes a domingo); lo demás, vacío.</summary>
    public static string Dia(string texto) =>
        Dias.FirstOrDefault(d => Igual(d, texto)) ?? "";


    /// <summary>«9:30», «09.30», «0930» o «19:30h» → «09:30»/«19:30»; lo demás, vacío.</summary>
    public static string Hora(string texto)
    {
        string digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());

        if (digitos.Length is 3 or 4
            && int.TryParse(digitos.Substring(0, digitos.Length - 2), out int h)
            && int.TryParse(digitos.Substring(digitos.Length - 2), out int m)
            && h is >= 0 and <= 23
            && m is >= 0 and <= 59)
        {
            return $"{h:00}:{m:00}";
        }

        return "";
    }


    private static bool Igual(string a, string b) =>
        string.Compare(a, b, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;


    private static string Juntar(string a, string b) =>
        a.Length == 0 ? b
        : b.Length == 0 ? a
        : a + " · " + b;
}
