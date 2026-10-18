using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// IMPORTACIÓN DEL PROGRAMA DE LA VISITA (Android)
// ============================================================================
//
// Inserta actividades en la tabla compartida ProgramaVisitaV2 dentro de
// una transacción, igual que WPF (ImportarProgramaVisita). Escribe en la
// columna Direccion (la que lee WPF).

public static partial class DatabaseHelper
{
    public static int ImportarProgramaVisitaMobile(
        string congreCodigo,
        string mesReferencia,
        IList<ActividadProgramaMobile> actividades,
        bool reemplazarExistente)
    {
        if (string.IsNullOrWhiteSpace(
                congreCodigo)
            ||
            string.IsNullOrWhiteSpace(
                mesReferencia))
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarColumnasProgramaVisitaUbicacionMobile(
            connection);

        int insertadas = 0;

        using var transaction =
            connection.BeginTransaction();

        try
        {
            if (reemplazarExistente)
            {
                using SqliteCommand borrar =
                    connection.CreateCommand();

                borrar.Transaction =
                    transaction;

                borrar.CommandText =
                    """
                    DELETE FROM ProgramaVisitaV2
                    WHERE CongreCodigo = $codigo
                      AND MesReferencia = $mes;
                    """;

                borrar.Parameters.AddWithValue(
                    "$codigo",
                    congreCodigo);

                borrar.Parameters.AddWithValue(
                    "$mes",
                    mesReferencia);

                borrar.ExecuteNonQuery();
            }

            var existentes =
                new List<ActividadProgramaMobile>();

            if (!reemplazarExistente)
            {
                existentes =
                    LeerActividadesProgramaMes(
                        connection,
                        transaction,
                        congreCodigo,
                        mesReferencia);
            }

            foreach (ActividadProgramaMobile actividad in
                     actividades
                     ?? (IList<ActividadProgramaMobile>)
                        Array.Empty<ActividadProgramaMobile>())
            {
                if (actividad is null)
                {
                    continue;
                }

                if (!reemplazarExistente
                    &&
                    existentes.Any(e =>
                        SonIguales(e, actividad)))
                {
                    continue;
                }

                using SqliteCommand cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaction;

                cmd.CommandText =
                    """
                    INSERT INTO ProgramaVisitaV2
                    (
                        CongreCodigo, MesReferencia, DiaSemana, Turno,
                        Responsable, TipoActividad, Hora, NotasLibro,
                        CompaneroId, Direccion, Latitud, Longitud
                    )
                    VALUES
                    (
                        $codigo, $mes, $dia, 'General',
                        $responsable, $tipo, $hora, $notas,
                        $comp, $direccion, $latitud, $longitud
                    );
                    """;

                cmd.Parameters.AddWithValue(
                    "$codigo",
                    congreCodigo);

                cmd.Parameters.AddWithValue(
                    "$mes",
                    mesReferencia);

                cmd.Parameters.AddWithValue(
                    "$dia",
                    actividad.DiaSemana ?? "");

                cmd.Parameters.AddWithValue(
                    "$responsable",
                    actividad.Responsable ?? "SC");

                cmd.Parameters.AddWithValue(
                    "$tipo",
                    actividad.TipoActividad ?? "");

                cmd.Parameters.AddWithValue(
                    "$hora",
                    actividad.Hora ?? "");

                cmd.Parameters.AddWithValue(
                    "$notas",
                    actividad.Notas ?? "");

                cmd.Parameters.AddWithValue(
                    "$comp",
                    actividad.CompaneroId);

                cmd.Parameters.AddWithValue(
                    "$direccion",
                    actividad.Direccion ?? "");

                cmd.Parameters.AddWithValue(
                    "$latitud",
                    actividad.Latitud);

                cmd.Parameters.AddWithValue(
                    "$longitud",
                    actividad.Longitud);

                cmd.ExecuteNonQuery();

                insertadas++;
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return insertadas;
    }


    private static List<ActividadProgramaMobile>
        LeerActividadesProgramaMes(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string codigo,
            string mes)
    {
        var resultado =
            new List<ActividadProgramaMobile>();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            transaction;

        cmd.CommandText =
            """
            SELECT
                DiaSemana,
                Turno,
                Responsable,
                TipoActividad,
                Hora,
                NotasLibro,
                CompaneroId,
                COALESCE(NULLIF(Direccion, ''), Ubicacion, '')
            FROM ProgramaVisitaV2
            WHERE CongreCodigo = $codigo
              AND MesReferencia = $mes;
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        cmd.Parameters.AddWithValue(
            "$mes",
            mes);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ActividadProgramaMobile
                {
                    DiaSemana =
                        reader.IsDBNull(0) ? "" : reader.GetString(0),

                    Turno =
                        reader.IsDBNull(1) ? "" : reader.GetString(1),

                    Responsable =
                        reader.IsDBNull(2) ? "" : reader.GetString(2),

                    TipoActividad =
                        reader.IsDBNull(3) ? "" : reader.GetString(3),

                    Hora =
                        reader.IsDBNull(4) ? "" : reader.GetString(4),

                    Notas =
                        reader.IsDBNull(5) ? "" : reader.GetString(5),

                    CompaneroId =
                        reader.IsDBNull(6) ? 0 : reader.GetInt32(6),

                    Direccion =
                        reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
        }

        return resultado;
    }


    private static bool SonIguales(
        ActividadProgramaMobile a,
        ActividadProgramaMobile b)
    {
        if (a is null
            ||
            b is null)
        {
            return false;
        }

        return
            string.Equals(
                (a.DiaSemana ?? "").Trim(),
                (b.DiaSemana ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase)
            &&
            string.Equals(
                (a.Responsable ?? "").Trim(),
                (b.Responsable ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase)
            &&
            string.Equals(
                (a.TipoActividad ?? "").Trim(),
                (b.TipoActividad ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase)
            &&
            string.Equals(
                (a.Hora ?? "").Trim(),
                (b.Hora ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase)
            &&
            a.CompaneroId == b.CompaneroId
            &&
            string.Equals(
                (a.Direccion ?? "").Trim(),
                (b.Direccion ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
    }
}
