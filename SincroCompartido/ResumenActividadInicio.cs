using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

/// <summary>
/// Cifras de actividad de Inicio: cursos bíblicos del último mes y media de
/// precursores auxiliares. Windows y Android usan esta misma regla (antes cada
/// una tenía la suya y daban cifras distintas).
/// </summary>
/// <remarks>
/// ActividadPersonas tiene filas preparadas para los meses que aún no se han
/// informado (todo el año de servicio, con los auxiliares continuos ya
/// marcados). Por eso un mes solo cuenta si no es futuro y tiene informes de
/// verdad: al menos la mitad de los del mes con más informes de los últimos
/// doce. Así no cuentan los meses preparados ni el que acaba de empezar a
/// llegar. Solo se cuentan las fichas actuales (ni históricos ni bajas).
/// </remarks>
public static class ResumenActividadInicio
{
    /// <summary>Meses (con informes) de los que se saca la media de auxiliares.</summary>
    public const int MesesMedia = 6;

    public sealed class Resultado
    {
        /// <summary>Mes de los cursos bíblicos; null si ningún mes tiene informes.</summary>
        public DateTime? MesReferencia { get; init; }

        public int CursosBiblicos { get; init; }

        public double MediaAuxiliares { get; init; }

        public int MesesMediaAuxiliares { get; init; }
    }

    /// <summary>Una fila de ActividadPersonas, ya con su mes del calendario.</summary>
    public readonly record struct Fila(
        int PersonaId,
        DateTime Mes,
        bool Participo,
        int CursosBiblicos,
        bool PrecursorAuxiliar);

    /// <summary>Lee la actividad de las fichas actuales y calcula las cifras.</summary>
    public static Resultado Calcular(
        SqliteConnection conexion,
        DateTime hoy)
    {
        var filas = new List<Fila>();

        using SqliteCommand cmd = conexion.CreateCommand();

        cmd.CommandText = """
            SELECT a.PersonaId, a.Mes, a.AnioServicio,
                   a.Participacion, a.CursosBiblicos, a.PrecursorAuxiliar
            FROM ActividadPersonas a
            INNER JOIN Personas p
                ON p.Id = a.PersonaId
            WHERE p.MesHistorico IS NULL
              AND (p.FechaEliminacion IS NULL
                   OR trim(p.FechaEliminacion) = '');
            """;

        using SqliteDataReader lector = cmd.ExecuteReader();

        while (lector.Read())
        {
            DateTime? mes = MesCalendario(
                Convert.ToString(lector.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToString(lector.GetValue(2), CultureInfo.InvariantCulture));

            if (mes is null)
            {
                continue;
            }

            filas.Add(new Fila(
                Entero(lector.GetValue(0)),
                mes.Value,
                Entero(lector.GetValue(3)) == 1,
                Math.Max(0, Entero(lector.GetValue(4))),
                Entero(lector.GetValue(5)) == 1));
        }

        return Calcular(filas, hoy);
    }

    public static Resultado Calcular(
        IEnumerable<Fila> filas,
        DateTime hoy)
    {
        DateTime mesActual = new(hoy.Year, hoy.Month, 1);

        var meses = filas
            .Where(f => f.Mes <= mesActual)
            .GroupBy(f => f.Mes)
            .Select(g => (
                Mes: g.Key,
                Informes: g.Where(f => f.Participo).Select(f => f.PersonaId).Distinct().Count(),
                Cursos: g.Sum(f => f.CursosBiblicos),
                Auxiliares: g.Where(f => f.PrecursorAuxiliar).Select(f => f.PersonaId).Distinct().Count()))
            .Where(m => m.Informes > 0)
            .OrderByDescending(m => m.Mes)
            .ToList();

        if (meses.Count == 0)
        {
            return new Resultado();
        }

        int maximo = meses.Take(12).Max(m => m.Informes);

        var completos = meses
            .Where(m => m.Informes * 2 >= maximo)
            .ToList();

        var paraMedia = completos
            .Take(MesesMedia)
            .ToList();

        return new Resultado
        {
            MesReferencia = completos[0].Mes,
            CursosBiblicos = completos[0].Cursos,
            MediaAuxiliares = paraMedia.Average(m => m.Auxiliares),
            MesesMediaAuxiliares = paraMedia.Count
        };
    }

    /// <summary>
    /// Mes del calendario de un mes del año de servicio: el año de servicio
    /// 2026 va de septiembre de 2025 a agosto de 2026.
    /// </summary>
    public static DateTime? MesCalendario(
        string? mes,
        string? anioServicio)
    {
        if (string.IsNullOrWhiteSpace(mes)
            || !int.TryParse(anioServicio?.Trim(), out int anio))
        {
            return null;
        }

        int numero = Array.IndexOf(Meses, SinTildes(mes.Trim().ToLowerInvariant())) + 1;

        if (numero <= 0)
        {
            return null;
        }

        int anioCalendario = numero >= 9 ? anio - 1 : anio;

        return anioCalendario is < 1900 or > 2200
            ? null
            : new DateTime(anioCalendario, numero, 1);
    }

    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio",
        "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

    private static string SinTildes(string texto)
    {
        var resultado = new StringBuilder(texto.Length);

        foreach (char c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                resultado.Append(c);
            }
        }

        return resultado.ToString();
    }

    private static int Entero(object valor) =>
        valor switch
        {
            long n => (int)n,
            int n => n,
            double d => (int)d,
            string s when int.TryParse(s.Trim(), out int n) => n,
            _ => 0
        };
}