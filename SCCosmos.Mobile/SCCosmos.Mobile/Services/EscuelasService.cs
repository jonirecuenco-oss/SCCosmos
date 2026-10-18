using System.Globalization;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Cálculo de candidatos a la Escuela de Precursores, portado de
/// SCCosmos WPF (EscuelasPage). Lógica pura, sin acceso a BD.
/// </summary>
public static class EscuelasService
{
    public const string PrefijoClaveDescartados =
        "DescartadosEscuelas_";

    public static int CalcularAnioInicioServicio(
        DateTime hoy) =>
        hoy.Month >= 9
            ? hoy.Year
            : hoy.Year - 1;

    public static List<EstudianteEscuelaMobile> Calcular(
        IEnumerable<Persona> personas,
        bool verDescartados,
        ICollection<int> descartados,
        DateTime hoy)
    {
        int anioInicioServicio =
            CalcularAnioInicioServicio(
                hoy);

        DateTime fechaLimiteInicioPrec =
            new(
                anioInicioServicio,
                9,
                1);

        DateTime fechaCorteUltimaEscuela =
            new(
                anioInicioServicio - 4,
                8,
                31);

        var lista =
            new List<EstudianteEscuelaMobile>();

        foreach (Persona p in personas)
        {
            if (string.IsNullOrWhiteSpace(
                    p.Servicio)
                ||
                (!p.Servicio.Contains(
                     "Precursor regular",
                     StringComparison.OrdinalIgnoreCase)
                 &&
                 !p.Servicio.Contains(
                     "Precursor especial",
                     StringComparison.OrdinalIgnoreCase)
                 &&
                 !p.Servicio.Contains(
                     "Misionero",
                     StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    p.FechaInicioPrec)
                ||
                !TryParseFechaFlexible(
                    p.FechaInicioPrec,
                    out DateTime fechaInicioPrec))
            {
                continue;
            }

            if (fechaInicioPrec > fechaLimiteInicioPrec)
            {
                continue;
            }

            bool cumpleFiltroEscuela = true;

            if (!string.IsNullOrWhiteSpace(
                    p.FechaUltimaEscuela)
                &&
                TryParseFechaFlexible(
                    p.FechaUltimaEscuela,
                    out DateTime fechaUltimaEscuela))
            {
                if (fechaUltimaEscuela >
                    fechaCorteUltimaEscuela)
                {
                    cumpleFiltroEscuela = false;
                }
            }

            if (!cumpleFiltroEscuela)
            {
                continue;
            }

            bool esDescartado =
                descartados.Contains(
                    p.Id);

            if (!verDescartados && esDescartado)
            {
                continue;
            }

            if (verDescartados && !esDescartado)
            {
                continue;
            }

            lista.Add(
                new EstudianteEscuelaMobile
                {
                    Id = p.Id,

                    NombreComun =
                        string.IsNullOrWhiteSpace(
                            p.NombreComun)
                            ? p.Nombre
                            : p.NombreComun,

                    Congregacion =
                        string.IsNullOrWhiteSpace(
                            p.CongregacionNombre)
                            ? p.CongregacionCodigo
                            : p.CongregacionNombre,

                    Email =
                        string.IsNullOrWhiteSpace(
                            p.Email)
                            ? p.EmailJW
                            : p.Email,

                    Telefono =
                        p.Telefono,

                    Edad =
                        CalcularEdad(
                            p.FechaNacimiento,
                            hoy),

                    FechaInicio =
                        p.FechaInicioPrec,

                    FechaUltimaEscuela =
                        string.IsNullOrWhiteSpace(
                            p.FechaUltimaEscuela)
                            ? "Nunca"
                            : p.FechaUltimaEscuela,

                    IsDescartado =
                        esDescartado
                });
        }

        return lista
            .OrderBy(c => c.Congregacion)
            .ThenBy(c => c.NombreComun)
            .ToList();
    }

    public static List<int> ParsearDescartados(
        string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new List<int>();
        }

        return texto
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(s =>
                int.TryParse(
                    s.Trim(),
                    out int n)
                    ? n
                    : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();
    }

    public static string SerializarDescartados(
        IEnumerable<int> ids) =>
        string.Join(
            ",",
            ids);

    public static bool TryParseFechaFlexible(
        string? textoFecha,
        out DateTime fechaResultado)
    {
        fechaResultado =
            DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(
                textoFecha))
        {
            return false;
        }

        string[] formatos =
        {
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "d/M/yyyy",
            "yyyy/MM/dd",
            "dd-MM-yyyy",
            "yyyy.MM.dd"
        };

        if (DateTime.TryParseExact(
                textoFecha.Trim(),
                formatos,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out fechaResultado))
        {
            return true;
        }

        return DateTime.TryParse(
            textoFecha,
            out fechaResultado);
    }

    public static string CalcularEdad(
        string? fechaNacimientoTexto,
        DateTime fechaActual)
    {
        if (string.IsNullOrWhiteSpace(
                fechaNacimientoTexto)
            ||
            !TryParseFechaFlexible(
                fechaNacimientoTexto,
                out DateTime fechaNac))
        {
            return "-";
        }

        int edad =
            fechaActual.Year - fechaNac.Year;

        if (fechaNac.Date >
            fechaActual.AddYears(-edad))
        {
            edad--;
        }

        return edad.ToString();
    }
}
