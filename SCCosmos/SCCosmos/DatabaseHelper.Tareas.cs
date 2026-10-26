using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    public static partial class DatabaseHelper
    {
        public static void AsegurarTablaTareasInterno(
            SqliteConnection connection)
        {
            EjecutarQuery(connection, SqlTablaTareas);
        }


        private const string SqlTablaTareas = @"CREATE TABLE IF NOT EXISTS Tareas (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Titulo TEXT DEFAULT '',
                Descripcion TEXT DEFAULT '',
                FechaInicio TEXT DEFAULT '',
                FechaFin TEXT DEFAULT '',
                TodoElDia INTEGER DEFAULT 1,
                Hora TEXT DEFAULT '',
                AvisoMinutos INTEGER DEFAULT -1,
                Repeticion TEXT DEFAULT 'Ninguna',
                Compartida INTEGER DEFAULT 0,
                Completada INTEGER DEFAULT 0,
                Categoria TEXT DEFAULT 'Personal',
                CreadaPor TEXT DEFAULT '',
                FechaCreacion TEXT DEFAULT '',
                UltimaModificacion TEXT DEFAULT ''
            );";


        /// <summary>
        /// Crea la tabla si falta dentro de una conexión abierta. Se ejecuta
        /// antes de cada lectura o escritura porque la sincronización con el
        /// móvil puede sustituir la base por una copia sin la tabla.
        /// </summary>
        private static void AsegurarTablaTareasEnConexion(
            SqliteConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = SqlTablaTareas;
            cmd.ExecuteNonQuery();

            // Bases creadas antes de las pestañas de tareas.
            try
            {
                bool tieneCategoria = false;

                using (var info = connection.CreateCommand())
                {
                    info.CommandText = "PRAGMA table_info(Tareas);";

                    using var lector = info.ExecuteReader();

                    while (lector.Read())
                    {
                        if (!lector.IsDBNull(1)
                            && string.Equals(
                                lector.GetString(1),
                                "Categoria",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            tieneCategoria = true;
                        }
                    }
                }

                if (!tieneCategoria)
                {
                    using (var alter = connection.CreateCommand())
                    {
                        alter.CommandText =
                            "ALTER TABLE Tareas ADD COLUMN Categoria TEXT DEFAULT 'Personal';";
                        alter.ExecuteNonQuery();
                    }

                    // Solo la primera vez: las tareas de trámites del
                    // calendario pasan a su pestaña. Después manda lo que
                    // elija el usuario.
                    using var migrar = connection.CreateCommand();
                    migrar.CommandText = @"
                        UPDATE Tareas
                        SET Categoria = 'Calendario'
                        WHERE lower(Titulo) LIKE '%s-303%'
                           OR lower(Titulo) LIKE '%s-302%'
                           OR lower(Titulo) LIKE '%s-21%'
                           OR lower(Titulo) LIKE '%betel%'
                           OR lower(Titulo) LIKE '%documentaci%'
                           OR lower(Titulo) LIKE '%nombramiento%'
                           OR lower(Titulo) LIKE '%territorio%'
                           OR lower(Titulo) LIKE '%informe%'
                           OR lower(Titulo) LIKE '%envio%'
                           OR lower(Titulo) LIKE '%envío%';";
                    migrar.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }


        public static List<Tarea> ObtenerTareas()
        {
            var lista =
                new List<Tarea>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                AsegurarTablaTareasEnConexion(connection);

                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Titulo, Descripcion, FechaInicio, FechaFin, TodoElDia, Hora, AvisoMinutos, Repeticion, Compartida, Completada, CreadaPor, FechaCreacion, UltimaModificacion, COALESCE(Categoria, 'Personal') FROM Tareas ORDER BY FechaInicio ASC, Id ASC;";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Tarea
                        {
                            Id = reader.GetInt32(0),
                            Titulo = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Descripcion = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            FechaInicio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            FechaFin = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            TodoElDia = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                            Hora = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            AvisoMinutos = reader.IsDBNull(7) ? -1 : reader.GetInt32(7),
                            Repeticion = reader.IsDBNull(8) ? "Ninguna" : reader.GetString(8),
                            Compartida = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                            Completada = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
                            CreadaPor = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            Categoria = reader.IsDBNull(14) ? "Personal" : reader.GetString(14),
                            FechaCreacion = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            UltimaModificacion = reader.IsDBNull(13) ? "" : reader.GetString(13)
                        });
                    }
                }
            }

            // Las tareas son de quien las crea: cada usuario ve las suyas y
            // las compartidas (las antiguas sin creador las ven todos).
            string usuario =
                PerfilAcceso.AccesoCompleto
                    ? "SC"
                    : "Esposa";

            return lista
                .Where(t =>
                    t.Compartida
                    || string.IsNullOrWhiteSpace(t.CreadaPor)
                    || string.Equals(
                        t.CreadaPor.Trim(),
                        usuario,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }


        public static int GuardarTarea(Tarea tarea)
        {
            AnotarActividad(
                "Tareas",
                (tarea.Id > 0 ? "Editó la tarea «" : "Creó la tarea «") +
                (tarea.Titulo ?? "") + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                AsegurarTablaTareasEnConexion(connection);

                var cmd = connection.CreateCommand();

                tarea.UltimaModificacion =
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                if (string.IsNullOrWhiteSpace(tarea.FechaCreacion))
                {
                    tarea.FechaCreacion =
                        tarea.UltimaModificacion;
                }

                if (tarea.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO Tareas
                        (Titulo, Descripcion, FechaInicio, FechaFin, TodoElDia,
                         Hora, AvisoMinutos, Repeticion, Compartida, Completada,
                         Categoria, CreadaPor, FechaCreacion, UltimaModificacion)
                    VALUES
                        ($titulo, $descripcion, $inicio, $fin, $todoElDia,
                         $hora, $aviso, $repeticion, $compartida, $completada,
                         $categoria, $creadaPor, $fechaCreacion, $ultimaModificacion);
                    SELECT last_insert_rowid();";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Tareas SET
                        Titulo = $titulo,
                        Descripcion = $descripcion,
                        FechaInicio = $inicio,
                        FechaFin = $fin,
                        TodoElDia = $todoElDia,
                        Hora = $hora,
                        AvisoMinutos = $aviso,
                        Repeticion = $repeticion,
                        Compartida = $compartida,
                        Completada = $completada,
                        Categoria = $categoria,
                        CreadaPor = $creadaPor,
                        FechaCreacion = $fechaCreacion,
                        UltimaModificacion = $ultimaModificacion
                    WHERE Id = $id;";

                    cmd.Parameters.AddWithValue("$id", tarea.Id);
                }

                cmd.Parameters.AddWithValue("$titulo", tarea.Titulo ?? "");
                cmd.Parameters.AddWithValue("$descripcion", tarea.Descripcion ?? "");
                cmd.Parameters.AddWithValue("$inicio", tarea.FechaInicio ?? "");
                cmd.Parameters.AddWithValue("$fin", tarea.FechaFin ?? "");
                cmd.Parameters.AddWithValue("$todoElDia", tarea.TodoElDia ? 1 : 0);
                cmd.Parameters.AddWithValue("$hora", tarea.Hora ?? "");
                cmd.Parameters.AddWithValue("$aviso", tarea.AvisoMinutos);
                cmd.Parameters.AddWithValue("$repeticion", tarea.Repeticion ?? "Ninguna");
                cmd.Parameters.AddWithValue("$compartida", tarea.Compartida ? 1 : 0);
                cmd.Parameters.AddWithValue("$completada", tarea.Completada ? 1 : 0);
                cmd.Parameters.AddWithValue("$categoria",
                    string.IsNullOrWhiteSpace(tarea.Categoria) ? "Personal" : tarea.Categoria);
                cmd.Parameters.AddWithValue("$creadaPor", tarea.CreadaPor ?? "");
                cmd.Parameters.AddWithValue("$fechaCreacion", tarea.FechaCreacion ?? "");
                cmd.Parameters.AddWithValue("$ultimaModificacion", tarea.UltimaModificacion ?? "");

                object resultado =
                    cmd.ExecuteScalar();

                if (tarea.Id > 0)
                {
                    return tarea.Id;
                }

                return resultado == null
                    ? 0
                    : Convert.ToInt32(resultado);
            }
        }


        public static void MarcarTareaCompletada(
            int id,
            bool completada)
        {
            AnotarActividad(
                "Tareas",
                (completada ? "Completó la tarea «" : "Reabrió la tarea «") +
                NombreTareaPorId(id) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"UPDATE Tareas
                    SET Completada = $completada,
                        UltimaModificacion = $modificacion
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$completada", completada ? 1 : 0);
                cmd.Parameters.AddWithValue(
                    "$modificacion",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$id", id);

                cmd.ExecuteNonQuery();
            }
        }


        public static void EliminarTarea(int id)
        {
            AnotarActividad(
                "Tareas",
                "Eliminó la tarea «" + NombreTareaPorId(id) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Tareas WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);

                cmd.ExecuteNonQuery();
            }
        }
    }
}
