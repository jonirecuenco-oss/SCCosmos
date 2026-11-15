#nullable enable

using System;
using System.Globalization;

namespace SCCosmos.Sincro;

/// <summary>
/// El requisito de horas del mes (50 h; 40 h la esposa si tiene 40 años o
/// más) y las horas de adelanto o atraso: lo hecho frente a lo que toca
/// hasta hoy, que es el requisito repartido por los días del mes ya
/// terminados (las horas de hoy suman, pero hoy aún no pide su parte).
/// </summary>
public static class RequisitoHoras
{
    /// <summary>En Configuracion (se sincroniza): «yyyy-MM-dd».</summary>
    public const string ClaveNacimientoEsposa = "FechaNacimientoEsposa";

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");


    public static DateTime? LeerNacimiento(string? texto) =>
        DateTime.TryParseExact((texto ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha)
            ? fecha
            : null;


    public static string EscribirNacimiento(DateTime? fecha) =>
        fecha?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";


    /// <summary>50 h; 40 h la esposa si a final de ese mes tiene 40 años o más.</summary>
    public static int Mensual(
        bool esposa,
        DateTime? nacimiento,
        DateTime mes)
    {
        if (!esposa || nacimiento is not DateTime nacida)
        {
            return 50;
        }

        DateTime finMes =
            new DateTime(mes.Year, mes.Month, 1).AddMonths(1).AddDays(-1);

        int edad = finMes.Year - nacida.Year;

        if (nacida.Date > finMes.AddYears(-edad))
        {
            edad--;
        }

        return edad >= 40 ? 40 : 50;
    }


    /// <summary>
    /// Las horas de adelanto o de atraso, con su signo: «+4», «−2,5» o «±0».
    /// Este mes, frente a lo que toca hasta hoy; en los pasados, frente al
    /// requisito. Signo: 1 adelanto, −1 atraso, 0 al día. En meses futuros, "".
    /// </summary>
    public static (string Texto, int Signo) Diferencia(
        double horas,
        int requisito,
        DateTime mes,
        DateTime hoy)
    {
        DateTime inicio = new(mes.Year, mes.Month, 1);
        DateTime mesActual = new(hoy.Year, hoy.Month, 1);

        if (inicio > mesActual)
        {
            return ("", 0);
        }

        double debido =
            inicio < mesActual
                ? requisito
                : requisito * (hoy.Day - 1) / (double)DateTime.DaysInMonth(hoy.Year, hoy.Month);

        double diferencia =
            Math.Round(horas - debido, 1, MidpointRounding.AwayFromZero);

        return diferencia >= 0.1 ? ("+" + diferencia.ToString("0.#", Es), 1)
            : diferencia <= -0.1 ? ("−" + (-diferencia).ToString("0.#", Es), -1)
            : ("±0", 0);
    }
}
