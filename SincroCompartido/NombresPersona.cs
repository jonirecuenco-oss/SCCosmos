using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SCCosmos.Sincro;

// =============================================================
// ¿ES LA MISMA PERSONA, AUNQUE ESTÉ ESCRITA DE OTRA FORMA?
//
// El S-21 escribe «Pérez García, Juan» y JW Hub «Juan Perez Garcia»:
// compararlos tal cual crea una ficha duplicada al importar. Son el
// mismo nombre si tienen las mismas palabras, en cualquier orden y sin
// distinguir mayúsculas ni acentos.
//
// JW Hub solo da la edad, así que sus fechas son aproximadas (01/01/año).
// Las exactas tienen que coincidir; con una aproximada basta con que el
// año encaje: así no se confunde a un padre con su hijo.
//
// Solo sirve cuando hay una única ficha así: dos fichas con el mismo
// nombre pueden ser dos personas, y entonces decide el usuario.
// =============================================================

public static class NombresPersona
{
    private const int MargenAnios =
        2;

    private static readonly HashSet<string> Particulas =
        new(StringComparer.Ordinal)
        {
            "de", "del", "la", "las", "los", "y", "da", "do"
        };

    private static readonly string[] FormatosFecha =
    [
        "dd/MM/yyyy",
        "d/M/yyyy",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss"
    ];


    /// <summary>
    /// Las palabras del nombre, normalizadas y ordenadas. Vacía si tiene menos
    /// de dos: un nombre suelto no identifica a nadie.
    /// </summary>
    public static string Clave(
        string? nombre)
    {
        if (string.IsNullOrWhiteSpace(
                nombre))
        {
            return "";
        }

        var limpio =
            new StringBuilder(
                nombre.Length);

        foreach (char letra in
                 nombre.ToLowerInvariant()
                     .Normalize(
                         NormalizationForm.FormD))
        {
            UnicodeCategory categoria =
                CharUnicodeInfo.GetUnicodeCategory(
                    letra);

            if (categoria == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            limpio.Append(
                char.IsLetter(letra)
                    ? letra
                    : ' ');
        }

        List<string> palabras =
            limpio.ToString()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !Particulas.Contains(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

        return palabras.Count < 2
            ? ""
            : string.Join(
                " ",
                palabras);
    }


    public static bool Equivalentes(
        string? a,
        string? b)
    {
        string clave =
            Clave(a);

        return clave.Length > 0
               && clave == Clave(b);
    }


    public static bool FechaAproximada(
        string? fecha) =>
        (fecha ?? "")
            .Trim()
            .StartsWith(
                "01/01/",
                StringComparison.Ordinal);


    /// <summary>
    /// ¿Pueden ser la misma fecha? Sin fecha no se puede descartar.
    /// </summary>
    public static bool FechasCompatibles(
        string? a,
        string? b)
    {
        if (string.IsNullOrWhiteSpace(a)
            || string.IsNullOrWhiteSpace(b))
        {
            return true;
        }

        DateTime? fechaA =
            LeerFecha(a);

        DateTime? fechaB =
            LeerFecha(b);

        if (fechaA.HasValue
            && fechaB.HasValue
            && !FechaAproximada(a)
            && !FechaAproximada(b))
        {
            return fechaA.Value.Date == fechaB.Value.Date;
        }

        int anioA =
            LeerAnio(a);

        int anioB =
            LeerAnio(b);

        return anioA == 0
               || anioB == 0
               || Math.Abs(anioA - anioB) <= MargenAnios;
    }


    /// <summary>
    /// ¿Hay que preguntar al usuario qué fecha se queda? No si son la misma, si
    /// falta una o si la de la ficha es solo una aproximación que encaja con la
    /// de la tarjeta: la exacta la sustituye.
    /// </summary>
    public static bool FechaEnConflicto(
        string? ficha,
        string? tarjeta)
    {
        if (string.IsNullOrWhiteSpace(ficha)
            || string.IsNullOrWhiteSpace(tarjeta))
        {
            return false;
        }

        DateTime? fechaFicha =
            LeerFecha(ficha);

        DateTime? fechaTarjeta =
            LeerFecha(tarjeta);

        if (fechaFicha.HasValue
            && fechaTarjeta.HasValue
            && fechaFicha.Value.Date == fechaTarjeta.Value.Date)
        {
            return false;
        }

        if (string.Equals(
                ficha.Trim(),
                tarjeta.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }

        return !(FechaAproximada(ficha)
                 && FechasCompatibles(ficha, tarjeta));
    }


    /// <summary>
    /// ¿Esa ficha puede ser la de esa tarjeta, por nombre y por fechas?
    /// </summary>
    public static bool PuedeSerLaMisma(
        (string? Nombre, string? Hub, string? Nacimiento, string? Bautismo) ficha,
        string? nombre,
        string? nacimiento,
        string? bautismo) =>
        (Equivalentes(ficha.Nombre, nombre)
         || Equivalentes(ficha.Hub, nombre))
        && FechasCompatibles(ficha.Nacimiento, nacimiento)
        && FechasCompatibles(ficha.Bautismo, bautismo);


    /// <summary>
    /// La única ficha que puede ser la de esa tarjeta; null si no hay ninguna o
    /// hay varias.
    /// </summary>
    public static T? BuscarUnica<T>(
        IEnumerable<T> fichas,
        string? nombre,
        string? nacimiento,
        string? bautismo,
        Func<T, (string? Nombre, string? Hub, string? Nacimiento, string? Bautismo)> datos)
        where T : class
    {
        List<T> coinciden =
            fichas
                .Where(f =>
                    PuedeSerLaMisma(
                        datos(f),
                        nombre,
                        nacimiento,
                        bautismo))
                .Take(2)
                .ToList();

        return coinciden.Count == 1
            ? coinciden[0]
            : null;
    }


    private static DateTime? LeerFecha(
        string fecha)
    {
        return DateTime.TryParseExact(
            fecha.Trim(),
            FormatosFecha,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime leida)
            ? leida
            : null;
    }


    private static int LeerAnio(
        string fecha)
    {
        Match anio =
            Regex.Match(
                fecha,
                @"\b(\d{4})\b",
                RegexOptions.RightToLeft);

        return anio.Success
               && int.TryParse(
                   anio.Groups[1].Value,
                   out int valor)
            ? valor
            : 0;
    }
}