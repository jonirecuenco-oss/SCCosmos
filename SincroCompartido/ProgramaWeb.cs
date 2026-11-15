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
//     "entreSemana":        { "hora", "lugar", "lat", "lon" },          (martes)
//     "finDeSemana":        { "dia", "hora", "lugar", "lat", "lon" },
//     "ancianos":           { "dia", "hora", "lugar", "lat", "lon" },
//     "predicacionViernes": { "hora", "lugar", "lat", "lon" }
//   },
//   "comidas":     [ { "dia", "familia", "telefono", "hora", "direccion", "lat", "lon" } ],
//   "alojamiento": { "familia", "telefono", "direccion", "lat", "lon" },
//   "predicacion": [ { "franja": "Miércoles mañana", "hora", "lugar", "lat", "lon",
//                      "sc":     [ { "nombre", "faceta", "leccion", "notas" } ],
//                      "esposa": [ ... ] } ],
//   "pastoreo":    [ { "nombre", "dia", "hora", "direccion", "lat", "lon", "descripcion" } ],
//   "asuntos":     [ "..." ]
// }
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


    // ---- Claves en Configuracion (por congregación y mes de la visita) ----

    public static string ClaveCodigo(string congregacion, string mes) =>
        $"ProgramaWeb_Codigo_{congregacion}_{mes}";

    public static string ClaveRecibido(string congregacion, string mes) =>
        $"ProgramaWeb_Recibido_{congregacion}_{mes}";

    public static string ClaveAplicado(string congregacion, string mes) =>
        $"ProgramaWeb_Aplicado_{congregacion}_{mes}";

    public const string PrefijoClaveCodigo = "ProgramaWeb_Codigo_";


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
    /// lista de asuntos a tratar. Lo que no sea válido se ignora.
    /// </summary>
    public static (List<EventoProgramaVisita> Eventos, List<string> Asuntos) AEventos(
        string json)
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

        // ---- Reuniones (las cuatro del PDF) ----
        if (raiz["reuniones"] is JsonObject reuniones)
        {
            Reunion(reuniones["entreSemana"], "Martes", "Reunión entre semana", "");
            Reunion(reuniones["finDeSemana"], null, "Reunión fin de semana", "");
            Reunion(reuniones["ancianos"], null, "Reunión de A/SM", "");
            Reunion(reuniones["predicacionViernes"], "Viernes", "Predicación", "Reunión para la predicación");
        }

        void Reunion(JsonNode? nodo, string? diaFijo, string tipo, string notas)
        {
            if (nodo is not JsonObject r)
            {
                return;
            }

            string dia = diaFijo ?? Dia(Texto(r, "dia"));
            string hora = Hora(Texto(r, "hora"));

            if (dia.Length == 0 || hora.Length == 0)
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
                Notas = notas,
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

            if (dia.Length == 0 || familia.Length == 0)
            {
                continue;
            }

            string hora = Hora(Texto(comida, "hora"));

            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = dia,
                Turno = "General",
                Hora = hora.Length > 0 ? hora : "13:30",
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
                    Hora = "",
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
        foreach (JsonObject salida in Lista(raiz["predicacion"]).Take(Franjas.Length))
        {
            string franja = Franjas.FirstOrDefault(f => Igual(f, Texto(salida, "franja"))) ?? "";

            if (franja.Length == 0)
            {
                continue;
            }

            string dia = franja.Split(' ')[0];
            string turno = franja.EndsWith("mañana", StringComparison.Ordinal) ? "Mañana" : "Tarde";
            string hora = Hora(Texto(salida, "hora"));
            string lugar = Texto(salida, "lugar");
            double lat = Numero(salida, "lat");
            double lon = Numero(salida, "lon");

            string planSc = Personas(salida["sc"]);
            string planEsposa = Personas(salida["esposa"]);

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

            if (planEsposa.Length > 0)
            {
                Salida("Esposa", planEsposa);
            }

            if (planSc.Length > 0)
            {
                Salida("SC", planSc);
            }

            // Con hora y sin nadie todavía: la salida queda, como en el PDF.
            if (planEsposa.Length == 0 && planSc.Length == 0 && hora.Length > 0)
            {
                Salida("", "");
            }
        }

        // ---- Visitas de pastoreo ----
        foreach (JsonObject visita in Lista(raiz["pastoreo"]).Take(MaxVisitasPastoreo))
        {
            string nombre = Texto(visita, "nombre");
            string dia = Dia(Texto(visita, "dia"));

            if (nombre.Length == 0 || dia.Length == 0)
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
                Notas = Juntar(nombre, Texto(visita, "descripcion")),
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


    private static string Dia(string texto) =>
        Dias.FirstOrDefault(d => Igual(d, texto)) ?? "";


    /// <summary>«9:30», «09.30» o «0930» → «09:30»; lo demás, vacío.</summary>
    private static string Hora(string texto)
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
