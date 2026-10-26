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

        // Horas y cursos bíblicos de cada DÍA. El total del mes se
        // recalcula a partir de estos días (HorasMes se mantiene como
        // resumen para el widget y el resto de pantallas).
        private const string SqlTablaHorasDia = @"CREATE TABLE IF NOT EXISTS HorasDia (
                Usuario TEXT DEFAULT '',
                Fecha TEXT DEFAULT '',
                Horas REAL DEFAULT 0,
                CBs INTEGER DEFAULT 0,
                PRIMARY KEY (Usuario, Fecha)
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
            EjecutarQuery(connection, SqlTablaHorasDia);

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

            MigrarHorasMesADias(connection);
        }


        /// <summary>
        /// Convierte los totales mensuales antiguos (HorasMes) en un día
        /// para no perderlos: se crea una fila del día 1 con ese total
        /// solo si ese mes aún no tiene días guardados.
        /// </summary>
        private static void MigrarHorasMesADias(SqliteConnection connection)
        {
            try
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO HorasDia (Usuario, Fecha, Horas, CBs)
                    SELECT m.Usuario, m.Mes || '-01', m.Horas, m.CBs
                    FROM HorasMes m
                    WHERE COALESCE(m.Horas, 0) <> 0 OR COALESCE(m.CBs, 0) <> 0
                      AND NOT EXISTS (
                          SELECT 1 FROM HorasDia d
                          WHERE d.Usuario = m.Usuario
                            AND substr(d.Fecha, 1, 7) = m.Mes);";

                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }


        /// <summary>Horas y CBs guardados en un día concreto.</summary>
        public static (double Horas, int Cbs) ObtenerPredicacionDia(
            string usuario,
            string fecha)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarHorasMesEnConexion(connection);

                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COALESCE(Horas, 0), COALESCE(CBs, 0) FROM HorasDia WHERE Usuario = $u AND Fecha = $f;";
                    cmd.Parameters.AddWithValue("$u", usuario ?? "");
                    cmd.Parameters.AddWithValue("$f", fecha ?? "");

                    using (var lector = cmd.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            return (
                                lector.IsDBNull(0) ? 0 : lector.GetDouble(0),
                                lector.IsDBNull(1) ? 0 : lector.GetInt32(1));
                        }
                    }
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
        public static void GuardarPredicacionDia(
            string usuario,
            string fecha,
            double horas,
            int cbs)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarHorasMesEnConexion(connection);

                    string dia = (fecha ?? "").Trim();
                    string mes = dia.Length >= 7 ? dia.Substring(0, 7) : "";

                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO HorasDia (Usuario, Fecha, Horas, CBs)
                        VALUES ($u, $f, $h, $c)
                        ON CONFLICT (Usuario, Fecha)
                        DO UPDATE SET Horas = excluded.Horas,
                                      CBs = excluded.CBs;";
                    cmd.Parameters.AddWithValue("$u", usuario ?? "");
                    cmd.Parameters.AddWithValue("$f", dia);
                    cmd.Parameters.AddWithValue("$h", horas);
                    cmd.Parameters.AddWithValue("$c", cbs);
                    cmd.ExecuteNonQuery();

                    if (mes.Length > 0)
                    {
                        RecalcularMesDesdeDias(connection, usuario, mes);
                    }
                }
            }
            catch
            {
            }
        }


        private static void RecalcularMesDesdeDias(
            SqliteConnection connection,
            string usuario,
            string mes)
        {
            var suma = connection.CreateCommand();
            suma.CommandText = @"
                SELECT COALESCE(SUM(Horas), 0), COALESCE(SUM(CBs), 0)
                FROM HorasDia
                WHERE Usuario = $u AND substr(Fecha, 1, 7) = $m;";
            suma.Parameters.AddWithValue("$u", usuario ?? "");
            suma.Parameters.AddWithValue("$m", mes ?? "");

            double horas = 0;
            int cbs = 0;

            using (var lector = suma.ExecuteReader())
            {
                if (lector.Read())
                {
                    horas = lector.IsDBNull(0) ? 0 : lector.GetDouble(0);
                    cbs = lector.IsDBNull(1) ? 0 : lector.GetInt32(1);
                }
            }

            var guardar = connection.CreateCommand();
            guardar.CommandText = @"
                INSERT INTO HorasMes (Usuario, Mes, Horas, CBs)
                VALUES ($u, $m, $h, $c)
                ON CONFLICT (Usuario, Mes)
                DO UPDATE SET Horas = excluded.Horas,
                              CBs = excluded.CBs;";
            guardar.Parameters.AddWithValue("$u", usuario ?? "");
            guardar.Parameters.AddWithValue("$m", mes ?? "");
            guardar.Parameters.AddWithValue("$h", horas);
            guardar.Parameters.AddWithValue("$c", cbs);
            guardar.ExecuteNonQuery();
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
