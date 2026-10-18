using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// TERRITORIOS (Android)
// ============================================================================
//
// Operaciones de edición sobre la tabla compartida TerritoriosVisitaV3,
// espejo de las de WPF (ActualizarVinculacionTerritorio /
// EliminarRegistroTerritorio). No se crea ni se modifica el esquema:
// solo se actualiza NombreVinculado o se borra una fila concreta.

public static partial class DatabaseHelper
{
    public static void ActualizarVinculacionTerritorioMobile(
        int idRegistro,
        string nuevoNombreVinculado)
    {
        if (idRegistro <= 0)
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
            UPDATE TerritoriosVisitaV3
            SET NombreVinculado = $nombre
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            nuevoNombreVinculado ?? "");

        cmd.Parameters.AddWithValue(
            "$id",
            idRegistro);

        cmd.ExecuteNonQuery();
    }


    public static void EliminarRegistroTerritorioMobile(
        int idRegistro)
    {
        if (idRegistro <= 0)
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
            "DELETE FROM TerritoriosVisitaV3 WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            idRegistro);

        cmd.ExecuteNonQuery();
    }
}
