#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SCCosmos.Sincro;

/// <summary>
/// Las tareas en el Calendario (móvil y Windows): los días en que cae cada
/// una dentro de un intervalo y el texto de su fila. Una tarea es un día, un
/// rango de días (con fecha fin) o se repite (diaria, semanal, mensual o
/// anual; con fecha fin, hasta ese día), como en los avisos.
/// </summary>
public static class FechasTarea
{
    private static readonly string[] Dias =
    {
        "dom", "lun", "mar", "mié", "jue", "vie", "sáb"
    };


    public static List<DateTime> EnIntervalo(
        string? fechaInicio,
        string? fechaFin,
        string? repeticion,
        DateTime desde,
        DateTime hasta)
    {
        var fechas = new List<DateTime>();

        if (!Leer(fechaInicio, out DateTime inicio))
        {
            return fechas;
        }

        desde = desde.Date;
        hasta = hasta.Date;

        DateTime? fin =
            Leer(fechaFin, out DateTime f) && f >= inicio
                ? f
                : null;

        string rep = (repeticion ?? "").Trim();

        if (rep is not ("Diaria" or "Semanal" or "Mensual" or "Anual"))
        {
            DateTime ultimo = fin ?? inicio;

            for (DateTime d = inicio > desde ? inicio : desde;
                 d <= ultimo && d <= hasta;
                 d = d.AddDays(1))
            {
                fechas.Add(d);
            }

            return fechas;
        }

        DateTime limite =
            fin.HasValue && fin.Value < hasta
                ? fin.Value
                : hasta;

        // Cada repetición se cuenta desde el inicio: una mensual del 31 cae
        // el 28 en febrero y vuelve al 31 en marzo.
        for (int n = 0; n < 20000; n++)
        {
            DateTime d = rep switch
            {
                "Diaria" => inicio.AddDays(n),
                "Semanal" => inicio.AddDays(7 * n),
                "Mensual" => inicio.AddMonths(n),
                _ => inicio.AddYears(n)
            };

            if (d > limite)
            {
                break;
            }

            if (d >= desde)
            {
                fechas.Add(d);
            }
        }

        return fechas;
    }


    /// <summary>
    /// «☐ dom 15 · 17:00 · Llamar a Pedro»; varios días seguidos, «lun 16 –
    /// vie 20». Hecha, con ☑.
    /// </summary>
    public static string Texto(
        bool completada,
        IReadOnlyList<DateTime> dias,
        string? hora,
        string? titulo)
    {
        string cuando =
            dias.Count == 0 ? ""
            : dias.Count == 1 ? Dia(dias[0])
            : Dia(dias[0]) + " – " + Dia(dias[dias.Count - 1]);

        var partes = new List<string>();

        if (cuando.Length > 0)
        {
            partes.Add(cuando);
        }

        if (!string.IsNullOrWhiteSpace(hora))
        {
            partes.Add(hora!.Trim());
        }

        partes.Add(
            string.IsNullOrWhiteSpace(titulo)
                ? "Tarea"
                : titulo!.Trim());

        return (completada ? "☑ " : "☐ ") + string.Join(" · ", partes);
    }


    /// <summary>Las tareas de un día en su casilla del mes: «•», «••» o «•••».</summary>
    public static string Puntos(int cuantas) =>
        cuantas <= 0
            ? ""
            : new string('•', Math.Min(cuantas, 3));


    private static string Dia(DateTime d) =>
        Dias[(int)d.DayOfWeek] + " " + d.Day.ToString(CultureInfo.InvariantCulture);


    private static bool Leer(string? texto, out DateTime fecha)
    {
        string t = (texto ?? "").Trim();

        if (DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
            || DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
        {
            fecha = fecha.Date;

            return true;
        }

        return false;
    }
}
