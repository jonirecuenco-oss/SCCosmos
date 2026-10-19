using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;

// =============================================================
// HORAS PREDICADAS DEL MES (POR USUARIO)
//
// Tabla sincronizada: se guarda el total de horas de cada mes
// para cada usuario (SC o Esposa); el botón + suma al total.
// =============================================================

public static partial class DatabaseHelper
{
    private const string SqlTablaHorasMes = @"
        CREATE TABLE IF NOT EXISTS HorasMes (
            Usuario TEXT DEFAULT '',
            Mes TEXT DEFAULT '',
            Horas REAL DEFAULT 0,
            PRIMARY KEY (Usuario, Mes)
        );";


    public static void AsegurarTablaHorasMesMobile()
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarHorasMesEnConexion(
            connection);
    }


    private static void AsegurarHorasMesEnConexion(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlTablaHorasMes;

        cmd.ExecuteNonQuery();
    }


    public static double ObtenerHorasMesMobile(
        string usuario,
        string mes)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            AsegurarHorasMesEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(Horas, 0) FROM HorasMes WHERE Usuario = $u AND Mes = $m;";

            cmd.Parameters.AddWithValue(
                "$u",
                usuario ?? "");

            cmd.Parameters.AddWithValue(
                "$m",
                mes ?? "");

            object? valor =
                cmd.ExecuteScalar();

            return valor == null || valor == DBNull.Value
                ? 0
                : Convert.ToDouble(valor);
        }
        catch
        {
            return 0;
        }
    }


    public static void SumarHorasMesMobile(
        string usuario,
        string mes,
        double horas)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            AsegurarHorasMesEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO HorasMes (Usuario, Mes, Horas)
                VALUES ($u, $m, $h)
                ON CONFLICT (Usuario, Mes)
                DO UPDATE SET Horas = Horas + excluded.Horas;";

            cmd.Parameters.AddWithValue(
                "$u",
                usuario ?? "");

            cmd.Parameters.AddWithValue(
                "$m",
                mes ?? "");

            cmd.Parameters.AddWithValue(
                "$h",
                horas);

            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }
}
