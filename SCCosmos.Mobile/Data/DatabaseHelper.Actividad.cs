using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// ACTIVIDAD MENSUAL DE LAS PERSONAS (Android)
// ============================================================================
//
// Respeta exactamente la tabla compartida ActividadPersonas de WPF:
//   - Unicidad (PersonaId, Mes, AnioServicio).
//   - Mes en minúsculas, AnioServicio como año de servicio (sept.-ago.).
//   - Se mantienen tanto Cursos como CursosBiblicos.
//
// No se elimina ni renombra ningún dato existente.

public static partial class DatabaseHelper
{
    public static List<ActividadMensualMobile>
        ObtenerActividadPersonaMobile(
            int personaId)
    {
        var lista =
            new List<ActividadMensualMobile>();

        if (personaId <= 0)
        {
            return lista;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        // Mismo orden que WPF (año descendente, mes dentro del año).
        cmd.CommandText =
            """
            SELECT
                Mes,
                AnioServicio,
                Participacion,
                COALESCE(NULLIF(Cursos, ''), CAST(CursosBiblicos AS TEXT), ''),
                PrecursorAuxiliar,
                Horas,
                Notas
            FROM ActividadPersonas
            WHERE PersonaId = $id
            ORDER BY CAST(AnioServicio AS INTEGER) DESC,
                     CASE lower(Mes)
                         WHEN 'agosto' THEN 12
                         WHEN 'julio' THEN 11
                         WHEN 'junio' THEN 10
                         WHEN 'mayo' THEN 9
                         WHEN 'abril' THEN 8
                         WHEN 'marzo' THEN 7
                         WHEN 'febrero' THEN 6
                         WHEN 'enero' THEN 5
                         WHEN 'diciembre' THEN 4
                         WHEN 'noviembre' THEN 3
                         WHEN 'octubre' THEN 2
                         WHEN 'septiembre' THEN 1
                         ELSE 0
                     END DESC;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            lista.Add(
                new ActividadMensualMobile
                {
                    Mes =
                        LeerTextoActividad(
                            reader,
                            0),

                    AnioServicio =
                        LeerTextoActividad(
                            reader,
                            1),

                    Participacion =
                        !reader.IsDBNull(2)
                        && reader.GetInt64(2) == 1,

                    CursosBiblicos =
                        LeerTextoActividad(
                            reader,
                            3),

                    PrecursorAuxiliar =
                        !reader.IsDBNull(4)
                        && reader.GetInt64(4) == 1,

                    Horas =
                        LeerTextoActividad(
                            reader,
                            5),

                    Notas =
                        LeerTextoActividad(
                            reader,
                            6)
                });
        }

        return lista;
    }


    /// <summary>
    /// Crea o actualiza una fila mensual. Réplica de
    /// GuardarActividadMensualEditada de WPF.
    /// </summary>
    public static void GuardarActividadMensualEditadaMobile(
        int personaId,
        string mes,
        string anioServicio,
        string horas,
        bool participacion,
        string cursos,
        bool precAux,
        string notas)
    {
        AnotarActividadMobile(
            "Personas",
            "Registró la actividad de «" +
            NombrePersonaPorIdMobile(personaId) +
            "» para " + mes + " de " + anioServicio);

        if (personaId <= 0
            ||
            string.IsNullOrWhiteSpace(
                mes)
            ||
            string.IsNullOrWhiteSpace(
                anioServicio))
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            UPDATE ActividadPersonas
            SET Horas = $horas,
                Participacion = $participacion,
                Cursos = $cursos,
                CursosBiblicos = $cursosNumerico,
                PrecursorAuxiliar = $precAux,
                Notas = $notas
            WHERE PersonaId = $id
              AND lower(Mes) = lower($mes)
              AND AnioServicio = $anio;

            INSERT INTO ActividadPersonas
                (PersonaId, Mes, AnioServicio, Horas, Participacion,
                 Cursos, CursosBiblicos, PrecursorAuxiliar, Notas)
            SELECT $id, $mes, $anio, $horas, $participacion,
                   $cursos, $cursosNumerico, $precAux, $notas
            WHERE (SELECT changes() = 0);
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.Parameters.AddWithValue(
            "$mes",
            mes.Trim().ToLowerInvariant());

        cmd.Parameters.AddWithValue(
            "$anio",
            anioServicio.Trim());

        cmd.Parameters.AddWithValue(
            "$horas",
            horas ?? "");

        cmd.Parameters.AddWithValue(
            "$participacion",
            participacion ? 1 : 0);

        cmd.Parameters.AddWithValue(
            "$cursos",
            cursos ?? "");

        int cursosNumerico = 0;

        int.TryParse(
            cursos ?? "",
            out cursosNumerico);

        cmd.Parameters.AddWithValue(
            "$cursosNumerico",
            cursosNumerico);

        cmd.Parameters.AddWithValue(
            "$precAux",
            precAux ? 1 : 0);

        cmd.Parameters.AddWithValue(
            "$notas",
            notas ?? "");

        cmd.ExecuteNonQuery();
    }


    public static void EliminarActividadMensualMobile(
        int personaId,
        string mes,
        string anioServicio)
    {
        AnotarActividadMobile(
            "Personas",
            "Quitó la actividad de «" +
            NombrePersonaPorIdMobile(personaId) +
            "» de " + mes + " de " + anioServicio);

        if (personaId <= 0
            ||
            string.IsNullOrWhiteSpace(
                mes)
            ||
            string.IsNullOrWhiteSpace(
                anioServicio))
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            DELETE FROM ActividadPersonas
            WHERE PersonaId = $id
              AND lower(Mes) = lower($mes)
              AND AnioServicio = $anio;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.Parameters.AddWithValue(
            "$mes",
            mes.Trim());

        cmd.Parameters.AddWithValue(
            "$anio",
            anioServicio.Trim());

        cmd.ExecuteNonQuery();
    }


    private static string LeerTextoActividad(
        SqliteDataReader reader,
        int indice)
    {
        return reader.IsDBNull(indice)
            ? ""
            : reader.GetValue(indice)
                ?.ToString()
              ?? "";
    }
}
