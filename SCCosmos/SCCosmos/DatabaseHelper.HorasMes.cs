using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    // =========================================================
    // PREDICACIÓN DEL MES (HORAS Y CURSOS BÍBLICOS, POR USUARIO)
    //
    // Tabla sincronizada: total de horas y de cursos bíblicos de
    // cada mes por usuario (SC o Esposa); el botón + suma.
    // =========================================================

    public static partial class DatabaseHelper
    {
        private const string SqlTablaHorasMes = @"CREATE TABLE IF NOT EXISTS HorasMes (
                Usuario TEXT DEFAULT '',
                Mes TEXT DEFAULT '',
                Horas REAL DEFAULT 0,
                CBs INTEGER DEFAULT 0,
                PRIMARY KEY (Usuario, Mes)
            );";


        public static void AsegurarHorasMes()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                AsegurarHorasMesEnConexion(connection);
            }
        }


        private static void AsegurarHorasMesEnConexion(
            SqliteConnection connection)
        {
            EjecutarQuery(connection, SqlTablaHorasMes);

            // Migración: bases creadas antes de los CBs.
            try
            {
                bool tieneCbs =
                    false;

                var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(HorasMes);";

                using (var lector = cmd.ExecuteReader())
                {
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
                    var alter = connection.CreateCommand();
                    alter.CommandText = "ALTER TABLE HorasMes ADD COLUMN CBs INTEGER DEFAULT 0;";
                    alter.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }


        public static double ObtenerHorasMes(
            string usuario,
            string mes)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarHorasMesEnConexion(connection);

                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COALESCE(Horas, 0) FROM HorasMes WHERE Usuario = $u AND Mes = $m;";
                    cmd.Parameters.AddWithValue("$u", usuario ?? "");
                    cmd.Parameters.AddWithValue("$m", mes ?? "");

                    object valor = cmd.ExecuteScalar();

                    return valor == null || valor == DBNull.Value
                        ? 0
                        : Convert.ToDouble(valor);
                }
            }
            catch
            {
                return 0;
            }
        }


        public static int ObtenerCbsMes(
            string usuario,
            string mes)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarHorasMesEnConexion(connection);

                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COALESCE(CBs, 0) FROM HorasMes WHERE Usuario = $u AND Mes = $m;";
                    cmd.Parameters.AddWithValue("$u", usuario ?? "");
                    cmd.Parameters.AddWithValue("$m", mes ?? "");

                    object valor = cmd.ExecuteScalar();

                    return valor == null || valor == DBNull.Value
                        ? 0
                        : Convert.ToInt32(valor);
                }
            }
            catch
            {
                return 0;
            }
        }


        public static void SumarPredicacionMes(
            string usuario,
            string mes,
            double horas,
            int cbs)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarHorasMesEnConexion(connection);

                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO HorasMes (Usuario, Mes, Horas, CBs)
                        VALUES ($u, $m, $h, $c)
                        ON CONFLICT (Usuario, Mes)
                        DO UPDATE SET Horas = Horas + excluded.Horas,
                                      CBs = CBs + excluded.CBs;";

                    cmd.Parameters.AddWithValue("$u", usuario ?? "");
                    cmd.Parameters.AddWithValue("$m", mes ?? "");
                    cmd.Parameters.AddWithValue("$h", horas);
                    cmd.Parameters.AddWithValue("$c", cbs);

                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }


        public static void SumarHorasMes(
            string usuario,
            string mes,
            double horas)
        {
            SumarPredicacionMes(
                usuario,
                mes,
                horas,
                0);
        }
    }
}
