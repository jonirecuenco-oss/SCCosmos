using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;

// =============================================================
// ALERTAS LEÍDAS (TABLA SINCRONIZADA)
//
// El estado de «alertas leídas» vive en la base de datos para
// que se sincronice entre dispositivos: marcar una alerta como
// leída en el móvil la oculta (y deja de avisar) también en el PC.
// =============================================================

public static partial class DatabaseHelper
{
    private const string SqlTablaAlertasLeidas = @"
        CREATE TABLE IF NOT EXISTS AlertasLeidas (
            Clave TEXT PRIMARY KEY
        );";


    public static void AsegurarTablaAlertasLeidasMobile()
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarAlertasLeidasEnConexion(
            connection);
    }


    private static void AsegurarAlertasLeidasEnConexion(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlTablaAlertasLeidas;

        cmd.ExecuteNonQuery();
    }


    public static List<string> ObtenerAlertasLeidasMobile()
    {
        var claves =
            new List<string>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarAlertasLeidasEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "SELECT Clave FROM AlertasLeidas;";

        using var reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                claves.Add(
                    reader.GetString(0));
            }
        }

        return claves;
    }


    public static void GuardarAlertasLeidasMobile(
        IEnumerable<string> claves)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            AsegurarAlertasLeidasEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            foreach (string clave in claves)
            {
                if (string.IsNullOrWhiteSpace(clave))
                {
                    continue;
                }

                using var cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    "INSERT OR IGNORE INTO AlertasLeidas (Clave) VALUES ($clave);";

                cmd.Parameters.AddWithValue(
                    "$clave",
                    clave.Trim());

                cmd.ExecuteNonQuery();
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }
}
