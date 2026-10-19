using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    // =========================================================
    // ALERTAS LEÍDAS (TABLA SINCRONIZADA)
    //
    // El estado de «alertas leídas» vive en la base de datos para
    // que se sincronice entre dispositivos: si marcas una alerta
    // como leída en un aparato, deja de aparecer (y de avisar) en
    // todos los demás.
    // =========================================================

    public static partial class DatabaseHelper
    {
        private const string SqlTablaAlertasLeidas = @"CREATE TABLE IF NOT EXISTS AlertasLeidas (
                Clave TEXT PRIMARY KEY
            );";


        public static void AsegurarAlertasLeidas()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                EjecutarQuery(connection, SqlTablaAlertasLeidas);
            }
        }


        private static void AsegurarAlertasLeidasEnConexion(
            SqliteConnection connection)
        {
            EjecutarQuery(connection, SqlTablaAlertasLeidas);
        }


        public static List<string> ObtenerAlertasLeidas()
        {
            var claves =
                new List<string>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                AsegurarAlertasLeidasEnConexion(connection);

                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Clave FROM AlertasLeidas;";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            claves.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return claves;
        }


        public static void GuardarAlertasLeidas(
            IEnumerable<string> claves)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    AsegurarAlertasLeidasEnConexion(connection);

                    using (var transaccion = connection.BeginTransaction())
                    {
                        foreach (string clave in claves)
                        {
                            if (string.IsNullOrWhiteSpace(clave))
                            {
                                continue;
                            }

                            var cmd = connection.CreateCommand();
                            cmd.Transaction = transaccion;
                            cmd.CommandText = "INSERT OR IGNORE INTO AlertasLeidas (Clave) VALUES ($clave);";
                            cmd.Parameters.AddWithValue("$clave", clave.Trim());
                            cmd.ExecuteNonQuery();
                        }

                        transaccion.Commit();
                    }
                }
            }
            catch
            {
            }
        }
    }
}
