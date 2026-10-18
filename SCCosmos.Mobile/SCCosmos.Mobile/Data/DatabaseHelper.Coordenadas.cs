using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// COORDENADAS DE PERSONAS (Android)
// ============================================================================
//
// Actualiza únicamente Latitud/Longitud de una persona, igual que
// ActualizarCoordenadas en WPF. No toca ninguna otra columna.

public static partial class DatabaseHelper
{
    public static void ActualizarCoordenadasPersonaMobile(
        int personaId,
        double latitud,
        double longitud)
    {
        if (personaId <= 0)
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
            UPDATE Personas
            SET Latitud = $lat,
                Longitud = $lon
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue(
            "$lat",
            latitud);

        cmd.Parameters.AddWithValue(
            "$lon",
            longitud);

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.ExecuteNonQuery();
    }
}
