using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;

// =============================================================
// PREDICACIÓN DEL MES (HORAS Y CURSOS BÍBLICOS, POR USUARIO)
//
// Tabla sincronizada: se guarda el total de horas y de cursos
// bíblicos de cada mes para cada usuario (SC o Esposa); el botón
// + suma a los totales.
// =============================================================

public static partial class DatabaseHelper
{
    private const string SqlTablaHorasMes = @"
        CREATE TABLE IF NOT EXISTS HorasMes (
            Usuario TEXT DEFAULT '',
            Mes TEXT DEFAULT '',
            Horas REAL DEFAULT 0,
            CBs INTEGER DEFAULT 0,
            PRIMARY KEY (Usuario, Mes)
        );";

    // Horas y CBs de cada día; el total del mes se recalcula con ellos.
    private const string SqlTablaHorasDia = @"
        CREATE TABLE IF NOT EXISTS HorasDia (
            Usuario TEXT DEFAULT '',
            Fecha TEXT DEFAULT '',
            Horas REAL DEFAULT 0,
            CBs INTEGER DEFAULT 0,
            PRIMARY KEY (Usuario, Fecha)
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
        using (var cmd =
               connection.CreateCommand())
        {
            cmd.CommandText =
                SqlTablaHorasMes;

            cmd.ExecuteNonQuery();
        }

        using (var cmdDia =
               connection.CreateCommand())
        {
            cmdDia.CommandText =
                SqlTablaHorasDia;

            cmdDia.ExecuteNonQuery();
        }

        // Los totales mensuales antiguos pasan a ser el día 1.
        try
        {
            using var migrar =
                connection.CreateCommand();

            migrar.CommandText =
                """
                INSERT INTO HorasDia (Usuario, Fecha, Horas, CBs)
                SELECT m.Usuario, m.Mes || '-01', m.Horas, m.CBs
                FROM HorasMes m
                WHERE (COALESCE(m.Horas, 0) <> 0 OR COALESCE(m.CBs, 0) <> 0)
                  AND NOT EXISTS (
                      SELECT 1 FROM HorasDia d
                      WHERE d.Usuario = m.Usuario
                        AND substr(d.Fecha, 1, 7) = m.Mes);
                """;

            migrar.ExecuteNonQuery();
        }
        catch
        {
        }

        // Migración: bases creadas antes de los CBs.
        try
        {
            bool tieneCbs =
                false;

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA table_info(HorasMes);";

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    if (!lector.IsDBNull(1)
                        && string.Equals(
                            lector.GetString(1),
                            "CBs",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        tieneCbs =
                            true;
                    }
                }
            }

            if (!tieneCbs)
            {
                using var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    "ALTER TABLE HorasMes ADD COLUMN CBs INTEGER DEFAULT 0;";

                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
        }
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


    public static int ObtenerCbsMesMobile(
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
                "SELECT COALESCE(CBs, 0) FROM HorasMes WHERE Usuario = $u AND Mes = $m;";

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
                : Convert.ToInt32(valor);
        }
        catch
        {
            return 0;
        }
    }


    public static void SumarPredicacionMesMobile(
        string usuario,
        string mes,
        double horas,
        int cbs)
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
                INSERT INTO HorasMes (Usuario, Mes, Horas, CBs)
                VALUES ($u, $m, $h, $c)
                ON CONFLICT (Usuario, Mes)
                DO UPDATE SET Horas = Horas + excluded.Horas,
                              CBs = CBs + excluded.CBs;";

            cmd.Parameters.AddWithValue(
                "$u",
                usuario ?? "");

            cmd.Parameters.AddWithValue(
                "$m",
                mes ?? "");

            cmd.Parameters.AddWithValue(
                "$h",
                horas);

            cmd.Parameters.AddWithValue(
                "$c",
                cbs);

            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }


    public static void SumarHorasMesMobile(
        string usuario,
        string mes,
        double horas)
    {
        SumarPredicacionMesMobile(
            usuario,
            mes,
            horas,
            0);
    }


    /// <summary>Horas y CBs guardados en un día concreto.</summary>
    public static (double Horas, int Cbs) ObtenerPredicacionDiaMobile(
        string usuario,
        string fecha)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            AsegurarHorasMesEnConexion(connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(Horas, 0), COALESCE(CBs, 0) " +
                "FROM HorasDia WHERE Usuario = $u AND Fecha = $f;";

            cmd.Parameters.AddWithValue("$u", usuario ?? "");
            cmd.Parameters.AddWithValue("$f", fecha ?? "");

            using var lector =
                cmd.ExecuteReader();

            if (lector.Read())
            {
                return (
                    lector.IsDBNull(0) ? 0 : lector.GetDouble(0),
                    lector.IsDBNull(1) ? 0 : lector.GetInt32(1));
            }
        }
        catch
        {
        }

        return (0, 0);
    }


    /// <summary>
    /// Guarda las horas y CBs de un día y recalcula el total del mes.
    /// </summary>
    public static void GuardarPredicacionDiaMobile(
        string usuario,
        string fecha,
        double horas,
        int cbs)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            AsegurarHorasMesEnConexion(connection);

            string dia =
                (fecha ?? "").Trim();

            string mes =
                dia.Length >= 7 ? dia.Substring(0, 7) : "";

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.CommandText =
                    """
                    INSERT INTO HorasDia (Usuario, Fecha, Horas, CBs)
                    VALUES ($u, $f, $h, $c)
                    ON CONFLICT (Usuario, Fecha)
                    DO UPDATE SET Horas = excluded.Horas,
                                  CBs = excluded.CBs;
                    """;

                cmd.Parameters.AddWithValue("$u", usuario ?? "");
                cmd.Parameters.AddWithValue("$f", dia);
                cmd.Parameters.AddWithValue("$h", horas);
                cmd.Parameters.AddWithValue("$c", cbs);

                cmd.ExecuteNonQuery();
            }

            if (mes.Length == 0)
            {
                return;
            }

            double totalHoras = 0;
            int totalCbs = 0;

            using (var suma =
                   connection.CreateCommand())
            {
                suma.CommandText =
                    """
                    SELECT COALESCE(SUM(Horas), 0), COALESCE(SUM(CBs), 0)
                    FROM HorasDia
                    WHERE Usuario = $u AND substr(Fecha, 1, 7) = $m;
                    """;

                suma.Parameters.AddWithValue("$u", usuario ?? "");
                suma.Parameters.AddWithValue("$m", mes);

                using var lector =
                    suma.ExecuteReader();

                if (lector.Read())
                {
                    totalHoras = lector.IsDBNull(0) ? 0 : lector.GetDouble(0);
                    totalCbs = lector.IsDBNull(1) ? 0 : lector.GetInt32(1);
                }
            }

            using (var guardar =
                   connection.CreateCommand())
            {
                guardar.CommandText =
                    """
                    INSERT INTO HorasMes (Usuario, Mes, Horas, CBs)
                    VALUES ($u, $m, $h, $c)
                    ON CONFLICT (Usuario, Mes)
                    DO UPDATE SET Horas = excluded.Horas,
                                  CBs = excluded.CBs;
                    """;

                guardar.Parameters.AddWithValue("$u", usuario ?? "");
                guardar.Parameters.AddWithValue("$m", mes);
                guardar.Parameters.AddWithValue("$h", totalHoras);
                guardar.Parameters.AddWithValue("$c", totalCbs);

                guardar.ExecuteNonQuery();
            }
        }
        catch
        {
        }
    }
}
