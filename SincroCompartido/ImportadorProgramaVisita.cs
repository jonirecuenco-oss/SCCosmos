using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using iText.Forms;
using iText.Kernel.Pdf;

namespace SCCosmos.Sincro;

// ============================================================
// IMPORTAR EL PROGRAMA DE LA VISITA (PDF)
//
// Lee el formulario «Programa para la visita del SC» (el PDF
// rellenable) y devuelve los eventos del programa: reuniones,
// salidas de predicación por día y turno, comidas/hospitalidad y
// los planes de Rebeca y Jonatán. Los nombres de personas quedan
// como notas (el vínculo con las tarjetas se hace a mano).
// ============================================================

public sealed class EventoProgramaVisita
{
    public string DiaSemana { get; set; } = "";

    /// <summary>Mañana, Tarde o General.</summary>
    public string Turno { get; set; } = "";

    public string Hora { get; set; } = "";

    public string TipoActividad { get; set; } = "";

    /// <summary>SC, Esposa, Ambos o vacío.</summary>
    public string Responsable { get; set; } = "";

    public string Notas { get; set; } = "";
}


public static class ImportadorProgramaVisita
{
    private static readonly string[] Dias =
    {
        "Lunes",
        "Martes",
        "Miércoles",
        "Jueves",
        "Viernes",
        "Sábado",
        "Domingo"
    };

    // Las columnas de los planes de predicación (mañana y tarde).
    private static readonly string[] Franjas =
    {
        "Miércoles mañana",
        "Miércoles tarde",
        "Jueves mañana",
        "Jueves tarde",
        "Viernes mañana",
        "Viernes tarde",
        "Sábado mañana",
        "Domingo mañana"
    };


    /// <summary>
    /// Lee el PDF del programa y devuelve los eventos (ordenados por día
    /// y hora). Los campos vacíos o con «-» no generan evento (salvo las
    /// salidas de predicación con hora, que siempre se crean).
    /// </summary>
    public static List<EventoProgramaVisita> LeerPdf(
        string ruta)
    {
        var eventos =
            new List<EventoProgramaVisita>();

        using var lector =
            new PdfReader(
                ruta);

        using var pdf =
            new PdfDocument(
                lector);

        var formulario =
            PdfAcroForm.GetAcroForm(pdf, false);

        if (formulario == null)
        {
            return eventos;
        }

        Dictionary<string, string> campos =
            formulario.GetAllFormFields()
                .ToDictionary(
                    p => p.Key,
                    p => p.Value.GetValueAsString() ?? "",
                    StringComparer.OrdinalIgnoreCase);

        string Valor(string inicio)
        {
            var par =
                campos.FirstOrDefault(
                    c => c.Key.StartsWith(
                        inicio,
                        StringComparison.OrdinalIgnoreCase));

            return (par.Key ?? "") == ""
                ? ""
                : (par.Value ?? "").Trim();
        }

        string Limpio(string texto)
        {
            return (texto ?? "")
                .Trim()
                .Trim('"', ' ');
        }

        bool EsVacio(string texto)
        {
            string limpio =
                Limpio(texto);

            return limpio.Length == 0
                || limpio == "-"
                || limpio == "–"
                || limpio.Equals(
                    "no programar",
                    StringComparison.OrdinalIgnoreCase);
        }

        // Día normalizado (por si escriben «sabado» o «Miercoles»).
        string NormalizarDia(string texto)
        {
            string buscado =
                Limpio(texto)
                    .ToLowerInvariant()
                    .Replace("á", "a")
                    .Replace("é", "e")
                    .Replace("í", "i")
                    .Replace("ó", "o")
                    .Replace("ú", "u");

            foreach (string dia in Dias)
            {
                string normal =
                    dia.ToLowerInvariant()
                        .Replace("á", "a")
                        .Replace("é", "e")
                        .Replace("í", "i")
                        .Replace("ó", "o")
                        .Replace("ú", "u");

                if (buscado.StartsWith(normal))
                {
                    return dia;
                }
            }

            return Limpio(texto);
        }

        // ---- Reuniones ----
        string horaMartes =
            Limpio(Valor("HoraMartes"));

        if (!EsVacio(horaMartes))
        {
            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = "Martes",
                Turno = "General",
                Hora = horaMartes,
                TipoActividad = "Reunión entre semana",
                Responsable = "Ambos"
            });
        }

        string diaFinde =
            NormalizarDia(
                Valor("MartesEntre semana"));

        string horaFinde =
            Limpio(
                Valor("HoraEntre semana"));

        if (!EsVacio(diaFinde) && !EsVacio(horaFinde))
        {
            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = diaFinde,
                Turno = "General",
                Hora = horaFinde,
                TipoActividad = "Reunión fin de semana",
                Responsable = "Ambos"
            });
        }

        string diaAsm =
            NormalizarDia(
                campos
                    .Where(c => c.Key.EndsWith("_2"))
                    .Select(c => c.Value)
                    .FirstOrDefault()
                ?? "");

        string horaAsm =
            Limpio(
                campos
                    .Where(c => c.Key.StartsWith(
                                    "HoraEntre semana",
                                    StringComparison.OrdinalIgnoreCase)
                                && c.Key.EndsWith("_2"))
                    .Select(c => c.Value)
                    .FirstOrDefault()
                ?? "");

        if (!EsVacio(diaAsm) && !EsVacio(horaAsm))
        {
            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = diaAsm,
                Turno = "General",
                Hora = horaAsm,
                TipoActividad = "Reunión de A/SM",
                Responsable = "Ambos"
            });
        }

        string horaViernes =
            Limpio(Valor("HoraViernes"));

        string notaViernes =
            Limpio(Valor("Salón del ReinoViernes"));

        if (!EsVacio(horaViernes))
        {
            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = "Viernes",
                Turno = "General",
                Hora = horaViernes,
                TipoActividad = "Predicación",
                Responsable = "Ambos",
                Notas = EsVacio(notaViernes)
                    ? ""
                    : notaViernes
            });
        }

        // ---- Salidas de predicación (mañana y tarde) ----
        // Los planes van: 8 primeros de Rebeca (esposa) y 8 siguientes
        // de Jonatán (SC).
        var planes =
            campos
                .Where(c => c.Key.StartsWith(
                    "Acompañantes",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => Sufijo(c.Key))
                .Select(c => c.Value)
                .ToList();

        for (int i = 0;
             i < Franjas.Length;
             i++)
        {
            string franja =
                Franjas[i];

            string hora =
                Limpio(Valor("Hora" + franja));

            string lugar =
                Limpio(Valor("Salón del Reino" + franja));

            if (EsVacio(hora))
            {
                continue;
            }

            string dia =
                franja.Split(' ')[0];

            string turno =
                franja.EndsWith("mañana")
                    ? "Mañana"
                    : "Tarde";

            string planEsposa =
                i < planes.Count
                    ? planes[i]
                    : "";

            string planSc =
                i + Franjas.Length < planes.Count
                    ? planes[i + Franjas.Length]
                    : "";

            bool algo =
                false;

            if (!EsVacio(planEsposa))
            {
                eventos.Add(new EventoProgramaVisita
                {
                    DiaSemana = dia,
                    Turno = turno,
                    Hora = hora,
                    TipoActividad = "Predicación",
                    Responsable = "Esposa",
                    Notas = Juntar(planEsposa, lugar)
                });

                algo = true;
            }

            if (!EsVacio(planSc))
            {
                eventos.Add(new EventoProgramaVisita
                {
                    DiaSemana = dia,
                    Turno = turno,
                    Hora = hora,
                    TipoActividad = "Predicación",
                    Responsable = "SC",
                    Notas = Juntar(planSc, lugar)
                });

                algo = true;
            }

            if (!algo)
            {
                eventos.Add(new EventoProgramaVisita
                {
                    DiaSemana = dia,
                    Turno = turno,
                    Hora = hora,
                    TipoActividad = "Predicación",
                    Responsable = "",
                    Notas = lugar
                });
            }
        }

        // ---- Hospitalidad (comida de cada día) ----
        string[] diasComida =
        {
            "Miércoles",
            "Jueves",
            "Viernes",
            "Sábado",
            "Domingo"
        };

        for (int i = 1;
             i <= diasComida.Length;
             i++)
        {
            string sufijo =
                i == 1
                    ? ""
                    : "_" + i;

            string familia =
                Limpio(
                    campos
                        .Where(c => c.Key.StartsWith(
                                        "Publicador o familia",
                                        StringComparison.OrdinalIgnoreCase)
                                    && c.Key.EndsWith(sufijo))
                        .Select(c => c.Value)
                        .FirstOrDefault()
                    ?? "");

            if (EsVacio(familia))
            {
                continue;
            }

            string telefono =
                Limpio(
                    campos
                        .Where(c => c.Key.StartsWith(
                                        "Teléfono",
                                        StringComparison.OrdinalIgnoreCase)
                                    && c.Key.EndsWith(sufijo))
                        .Select(c => c.Value)
                        .FirstOrDefault()
                    ?? "");

            string direccion =
                Limpio(
                    campos
                        .Where(c => c.Key.StartsWith(
                                        "Dirección",
                                        StringComparison.OrdinalIgnoreCase)
                                    && c.Key.EndsWith(sufijo))
                        .Select(c => c.Value)
                        .FirstOrDefault()
                    ?? "");

            eventos.Add(new EventoProgramaVisita
            {
                DiaSemana = diasComida[i - 1],
                Turno = "General",
                Hora = "",
                TipoActividad = "Comida",
                Responsable = "Ambos",
                Notas = Juntar(
                    Juntar(familia, telefono),
                    direccion)
            });
        }

        // Orden: por día y hora.
        return eventos
            .OrderBy(e => IndiceDia(e.DiaSemana))
            .ThenBy(e => e.Hora)
            .ToList();
    }


    private static string Juntar(
        string a,
        string b)
    {
        string uno =
            (a ?? "").Trim().Trim('"', ' ');

        string dos =
            (b ?? "").Trim().Trim('"', ' ');

        if (uno.Length == 0)
        {
            return dos;
        }

        if (dos.Length == 0)
        {
            return uno;
        }

        return uno + " · " + dos;
    }


    private static int Sufijo(
        string clave)
    {
        int guion =
            clave.LastIndexOf('_');

        if (guion >= 0
            && int.TryParse(
                clave.Substring(guion + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int numero))
        {
            return numero;
        }

        return 1;
    }


    private static int IndiceDia(
        string dia)
    {
        int indice =
            Array.FindIndex(
                Dias,
                d => string.Equals(
                    d,
                    dia,
                    StringComparison.OrdinalIgnoreCase));

        return indice < 0
            ? 99
            : indice;
    }
}
