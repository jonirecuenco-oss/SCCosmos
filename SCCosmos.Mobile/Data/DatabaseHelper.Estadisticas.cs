using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// ESTADÍSTICAS AGREGADAS DE LA ACTIVIDAD (Android)
// ============================================================================
//
// Cursos bíblicos del último mes con datos y media de precursores
// auxiliares de los últimos meses. Solo lectura sobre ActividadPersonas.

public static partial class DatabaseHelper
{
    public sealed class EstadisticasExtraMobile
    {
        public int CursosUltimoMes { get; set; }
        public double MediaPrecursoresAuxiliares { get; set; }
    }


    public static EstadisticasExtraMobile
        ObtenerEstadisticasExtraMobile()
    {
        var resultado =
            new EstadisticasExtraMobile();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ActividadPersonas"))
        {
            return resultado;
        }

        const string ordenMes =
            "CASE lower(Mes) " +
            "WHEN 'agosto' THEN 12 " +
            "WHEN 'julio' THEN 11 " +
            "WHEN 'junio' THEN 10 " +
            "WHEN 'mayo' THEN 9 " +
            "WHEN 'abril' THEN 8 " +
            "WHEN 'marzo' THEN 7 " +
            "WHEN 'febrero' THEN 6 " +
            "WHEN 'enero' THEN 5 " +
            "WHEN 'diciembre' THEN 4 " +
            "WHEN 'noviembre' THEN 3 " +
            "WHEN 'octubre' THEN 2 " +
            "WHEN 'septiembre' THEN 1 " +
            "ELSE 0 END";

        // Cursos bíblicos del último mes con datos.
        using (SqliteCommand cmd =
               connection.CreateCommand())
        {
            cmd.CommandText =
                $"""
                SELECT COALESCE(SUM(CursosBiblicos), 0)
                FROM ActividadPersonas
                WHERE AnioServicio = (
                    SELECT AnioServicio
                    FROM ActividadPersonas
                    ORDER BY CAST(AnioServicio AS INTEGER) DESC
                    LIMIT 1)
                  AND lower(Mes) = (
                    SELECT lower(Mes)
                    FROM ActividadPersonas
                    WHERE AnioServicio = (
                        SELECT AnioServicio
                        FROM ActividadPersonas
                        ORDER BY CAST(AnioServicio AS INTEGER) DESC
                        LIMIT 1)
                    ORDER BY {ordenMes} DESC
                    LIMIT 1);
                """;

            resultado.CursosUltimoMes =
                Convert.ToInt32(
                    cmd.ExecuteScalar() ?? 0);
        }

        // Media de precursores auxiliares (últimos 6 meses con datos).
        using (SqliteCommand cmd =
               connection.CreateCommand())
        {
            cmd.CommandText =
                $"""
                SELECT COALESCE(AVG(cnt), 0)
                FROM (
                    SELECT SUM(
                               CASE WHEN PrecursorAuxiliar = 1
                                    THEN 1 ELSE 0 END) AS cnt
                    FROM ActividadPersonas
                    GROUP BY AnioServicio, lower(Mes)
                    ORDER BY CAST(AnioServicio AS INTEGER) DESC,
                             {ordenMes} DESC
                    LIMIT 6
                );
                """;

            object? valor =
                cmd.ExecuteScalar();

            if (valor is not null
                &&
                valor != DBNull.Value)
            {
                resultado.MediaPrecursoresAuxiliares =
                    Convert.ToDouble(valor);
            }
        }

        return resultado;
    }
}
