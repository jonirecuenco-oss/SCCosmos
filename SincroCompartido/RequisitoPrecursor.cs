using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SCCosmos.Sincro;

// =============================================================
// REQUISITO DE HORAS DE PRECURSORES Y MISIONEROS
//
// La misma regla en Windows y en Android.
//
// Requisito anual: ninguno con salud delicada; si no, el fijado a
// mano en la ficha; si no, el de su servicio (regular 600; especial
// y misionero 1200, o 1080 en mujeres de 40 años o más).
//
// Cada mes cuentan las horas más el crédito (horas en otras
// actividades) hasta un máximo de 55. El tope solo limita lo que
// aporta el crédito: quien predica más de 55 horas no pierde ninguna.
// =============================================================

/// <summary>
/// Lo informado en un mes del año de servicio.
/// </summary>
public readonly record struct HorasDeMes(
    string? Mes,
    string? AnioServicio,
    double Horas,
    double Credito);


public static class RequisitoPrecursor
{
    public const double TopeConCredito =
        55;

    private static readonly string[] NombresMes =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };


    public static double HorasComputables(
        double horas,
        double credito)
    {
        return credito <= 0
            ? horas
            : Math.Max(
                horas,
                Math.Min(
                    horas + credito,
                    TopeConCredito));
    }


    public static bool EsPrecursorRegular(
        string? servicio)
    {
        return (servicio ?? "")
            .Contains(
                "precursor regular",
                StringComparison.OrdinalIgnoreCase);
    }


    public static int RequisitoAnual(
        string? servicio,
        string? sexo,
        DateTime? nacimiento,
        DateTime referencia,
        int requisitoFijado,
        bool saludDelicada)
    {
        string texto =
            (servicio ?? "").Trim();

        bool regular =
            EsPrecursorRegular(
                texto);

        bool especial =
            texto.Contains(
                "especial",
                StringComparison.OrdinalIgnoreCase)
            || texto.Contains(
                "misionero",
                StringComparison.OrdinalIgnoreCase);

        if (saludDelicada
            || (!regular && !especial))
        {
            return 0;
        }

        if (requisitoFijado > 0)
        {
            return requisitoFijado;
        }

        if (regular)
        {
            return 600;
        }

        if (string.Equals(
                (sexo ?? "").Trim(),
                "Mujer",
                StringComparison.OrdinalIgnoreCase)
            && nacimiento.HasValue)
        {
            int edad =
                referencia.Year - nacimiento.Value.Year;

            if (nacimiento.Value.Date > referencia.AddYears(-edad))
            {
                edad--;
            }

            if (edad >= 40)
            {
                return 1080;
            }
        }

        return 1200;
    }


    /// <summary>
    /// Horas de adelanto (positivas) o de atraso (negativas) acumuladas en el
    /// año de servicio hasta ese mes. Null si no hay nada que mostrar: sin
    /// requisito, o un regular que aún no había empezado. Los regulares
    /// cuentan, en horas y en requisito, desde que empezaron.
    /// </summary>
    public static double? DiferenciaHasta(
        IEnumerable<HorasDeMes> actividad,
        DateTime mes,
        int requisitoAnual,
        bool esRegular,
        DateTime? inicioPrecursor)
    {
        if (requisitoAnual <= 0)
        {
            return null;
        }

        int meses =
            MesesConRequisito(
                mes,
                esRegular,
                inicioPrecursor);

        if (meses <= 0)
        {
            return null;
        }

        return Math.Round(
            HorasHasta(
                actividad,
                mes,
                esRegular,
                inicioPrecursor)
            - (requisitoAnual / 12d * meses),
            1);
    }


    /// <summary>
    /// Horas que cuentan en el año de servicio hasta ese mes incluido, con su
    /// crédito. Las de un regular, desde que empezó.
    /// </summary>
    public static double HorasHasta(
        IEnumerable<HorasDeMes> actividad,
        DateTime mes,
        bool esRegular,
        DateTime? inicioPrecursor)
    {
        DateTime mesActual =
            new(mes.Year, mes.Month, 1);

        DateTime desde =
            mesActual.AddMonths(
                1 - MesesConRequisito(
                    mes,
                    esRegular,
                    inicioPrecursor));

        string anio =
            (mes.Month >= 9 ? mes.Year + 1 : mes.Year)
            .ToString(CultureInfo.InvariantCulture);

        int anioInicio =
            mes.Month >= 9
                ? mes.Year
                : mes.Year - 1;

        double acumulado =
            0;

        foreach (HorasDeMes registro in actividad)
        {
            if (!string.Equals(
                    (registro.AnioServicio ?? "").Trim(),
                    anio,
                    StringComparison.Ordinal))
            {
                continue;
            }

            int numero =
                Array.IndexOf(
                    NombresMes,
                    (registro.Mes ?? "").Trim().ToLowerInvariant()) + 1;

            if (numero <= 0)
            {
                continue;
            }

            DateTime mesRegistro =
                new(
                    numero >= 9 ? anioInicio : anioInicio + 1,
                    numero,
                    1);

            if (mesRegistro < desde
                || mesRegistro > mesActual)
            {
                continue;
            }

            acumulado +=
                HorasComputables(
                    Math.Max(0, registro.Horas),
                    registro.Credito);
        }

        return acumulado;
    }


    /// <summary>
    /// Meses del año de servicio en los que ya se le exige el requisito, hasta
    /// ese mes incluido. Los regulares, desde que empezaron; 0 si aún no.
    /// </summary>
    public static int MesesConRequisito(
        DateTime mes,
        bool esRegular,
        DateTime? inicioPrecursor)
    {
        DateTime desde =
            mes.Month >= 9
                ? new DateTime(mes.Year, 9, 1)
                : new DateTime(mes.Year - 1, 9, 1);

        if (esRegular
            && inicioPrecursor.HasValue)
        {
            DateTime inicioMes =
                new(inicioPrecursor.Value.Year, inicioPrecursor.Value.Month, 1);

            if (inicioMes > desde)
            {
                desde =
                    inicioMes;
            }
        }

        return Math.Max(
            0,
            ((mes.Year - desde.Year) * 12)
            + mes.Month
            - desde.Month
            + 1);
    }


    /// <summary>
    /// Adelanto o atraso de un mes de la ficha, con todos los datos de la persona.
    /// </summary>
    public static double? DiferenciaDeRegistro(
        IReadOnlyCollection<HorasDeMes> actividad,
        HorasDeMes registro,
        string? servicio,
        string? sexo,
        DateTime? nacimiento,
        DateTime? inicioPrecursor,
        int requisitoFijado,
        bool saludDelicada)
    {
        DateTime? mes =
            MesDelAnioServicio(
                registro.Mes,
                registro.AnioServicio);

        if (mes is null)
        {
            return null;
        }

        return DiferenciaHasta(
            actividad,
            mes.Value,
            RequisitoAnual(
                servicio,
                sexo,
                nacimiento,
                mes.Value,
                requisitoFijado,
                saludDelicada),
            EsPrecursorRegular(
                servicio),
            inicioPrecursor);
    }


    /// <summary>
    /// «+12,5», «-3» o «0», como se muestra bajo cada mes.
    /// </summary>
    public static string TextoDiferencia(
        double diferencia)
    {
        return diferencia > 0
            ? "+" + diferencia.ToString("0.#", CultureInfo.CurrentCulture)
            : diferencia < 0
                ? diferencia.ToString("0.#", CultureInfo.CurrentCulture)
                : "0";
    }


    /// <summary>
    /// Primer día de un mes del año de servicio; null si no se reconoce.
    /// </summary>
    public static DateTime? MesDelAnioServicio(
        string? mes,
        string? anioServicio)
    {
        int numero =
            Array.IndexOf(
                NombresMes,
                (mes ?? "").Trim().ToLowerInvariant()) + 1;

        if (numero <= 0
            || !int.TryParse(
                (anioServicio ?? "").Trim(),
                out int anio)
            || anio < 2
            || anio > 9000)
        {
            return null;
        }

        return new DateTime(
            numero >= 9 ? anio - 1 : anio,
            numero,
            1);
    }


    /// <summary>
    /// Crédito que se guarda a partir de la casilla: vacía, lo que digan las
    /// notas; con un número (también 0), ese número.
    /// </summary>
    public static double CreditoAGuardar(
        string? casilla,
        string? notas)
    {
        return string.IsNullOrWhiteSpace(
            casilla)
            ? CreditoDeNotas(
                notas)
            : LeerHoras(
                casilla);
    }


    /// <summary>
    /// Lo que muestra la casilla: vacía si no hay crédito, salvo que las notas
    /// digan otra cosa y alguien lo haya dejado expresamente en 0.
    /// </summary>
    public static string TextoCredito(
        double credito,
        string? notas)
    {
        return credito > 0
            ? credito.ToString("0.##", CultureInfo.CurrentCulture)
            : CreditoDeNotas(notas) > 0
                ? "0"
                : "";
    }


    /// <summary>
    /// Convierte el texto de una casilla de horas o de crédito: «», «12», «12,5».
    /// </summary>
    public static double LeerHoras(
        string? texto)
    {
        return double.TryParse(
                   (texto ?? "").Replace(" ", "").Replace(",", "."),
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out double valor)
               && valor > 0
            ? valor
            : 0;
    }


    // =========================================================
    // CRÉDITO EN LAS NOTAS
    //
    // Si las notas lo dicen expresamente («[Crédito: 15]», como las escribe
    // JW Hub), vale ese número aunque luego venga desglosado. Si no, se suman
    // las horas de cada actividad («15h CEH, 10h Betel»), sin contar lo que
    // no son horas («6 días libres», «2 cursos») ni los números sueltos.
    // =========================================================

    private static readonly Regex CreditoExpreso =
        new(
            @"credito\s*[:=]?\s*(\d+(?:[.,]\d+)?)");

    private static readonly Regex Numero =
        new(
            @"(?<![\d/\-])(?<!\d[:.,])(\d{1,3})(?::(\d{2}))?(?:[.,](\d{1,2}))?(?![\d/])");

    private static readonly Regex NoSonHoras =
        new(
            @"^(?:dias?\b|semanas?\b|sem\b|mes(?:es)?\b|anos?\b|vez\b|veces\b|km\b|cursos?\b|personas?\b|publicaciones?\b|estudios?\b|revisitas?\b|ninos?\b|hermanos?\b|ultim)");


    public static double CreditoDeNotas(
        string? notas)
    {
        if (string.IsNullOrWhiteSpace(
                notas))
        {
            return 0;
        }

        string texto =
            SinAcentos(
                notas.ToLowerInvariant());

        Match expreso =
            CreditoExpreso.Match(
                texto);

        if (expreso.Success)
        {
            return LeerHoras(
                expreso.Groups[1].Value);
        }

        MatchCollection numeros =
            Numero.Matches(
                texto);

        double total =
            0;

        for (int i = 0; i < numeros.Count; i++)
        {
            Match numero =
                numeros[i];

            int finTramo =
                i + 1 < numeros.Count
                    ? numeros[i + 1].Index
                    : texto.Length;

            string detras =
                texto.Substring(
                        numero.Index + numero.Length,
                        finTramo - numero.Index - numero.Length)
                    .TrimStart();

            // Un número sin nada detrás no dice de qué son esas horas, y las
            // de predicación ya están en la casilla de horas.
            if (detras.Length == 0
                || !char.IsLetter(detras[0])
                || NoSonHoras.IsMatch(detras)
                || detras.Contains("pred"))
            {
                continue;
            }

            double valor =
                double.Parse(
                    numero.Groups[1].Value,
                    CultureInfo.InvariantCulture);

            if (numero.Groups[2].Success)
            {
                valor +=
                    double.Parse(
                        numero.Groups[2].Value,
                        CultureInfo.InvariantCulture)
                    / 60;
            }
            else if (numero.Groups[3].Success)
            {
                valor +=
                    double.Parse(
                        "0." + numero.Groups[3].Value,
                        CultureInfo.InvariantCulture);
            }

            total +=
                valor;
        }

        return Math.Round(
            total,
            1);
    }


    private static string SinAcentos(
        string texto)
    {
        var resultado =
            new StringBuilder(
                texto.Length);

        foreach (char letra in
                 texto.Normalize(
                     NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra)
                != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(
                    letra);
            }
        }

        return resultado.ToString();
    }
}
