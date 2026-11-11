#nullable enable

using System;
using System.Globalization;

namespace SCCosmos.Sincro;

/// <summary>
/// El requisito de horas del mes (50 h; 40 h la esposa si tiene 40 años o
/// más) y si se va con atraso: lo hecho frente a lo que toca hasta hoy, que
/// es el requisito repartido por los días del mes ya terminados (las horas
/// de hoy suman, pero hoy aún no pide su parte).
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
    /// Este mes: «Vas al día · faltan 30 h», «Vas 3,5 h por detrás · faltan
    /// 35 h» o «Requisito cumplido (50 h)». Meses pasados: «Cumplido (50 h)»
    /// o «Faltaron 5 h para las 50». Atraso: si va por detrás o faltaron horas.
    /// </summary>
    public static (string Texto, bool Atraso) Estado(
        double horas,
        int requisito,
        DateTime mes,
        DateTime hoy)
    {
        DateTime inicio = new(mes.Year, mes.Month, 1);
        DateTime mesActual = new(hoy.Year, hoy.Month, 1);

        static string H(double h) =>
            h.ToString("0.#", Es) + " h";

        if (inicio > mesActual)
        {
            return ("", false);
        }

        if (horas >= requisito - 0.05)
        {
            return (inicio < mesActual ? $"Cumplido ({requisito} h)" : $"Requisito cumplido ({requisito} h)", false);
        }

        if (inicio < mesActual)
        {
            return ($"Faltaron {H(requisito - horas)} para las {requisito}", true);
        }

        double debido =
            requisito * (hoy.Day - 1) / (double)DateTime.DaysInMonth(hoy.Year, hoy.Month);

        double atraso = debido - horas;

        string faltan = $"faltan {H(requisito - horas)}";

        return atraso >= 0.05
            ? ($"Vas {H(atraso)} por detrás · {faltan}", true)
            : ($"Vas al día · {faltan}", false);
    }
}
