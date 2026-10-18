using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;

public static partial class DatabaseHelper
{
    private const string SqlTablaTareas = @"
        CREATE TABLE IF NOT EXISTS Tareas (
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
            CreadaPor TEXT DEFAULT '',
            FechaCreacion TEXT DEFAULT '',
            UltimaModificacion TEXT DEFAULT ''
        );";


    public static void AsegurarTablaTareasMobile()
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarTablaTareasEnConexion(
            connection);
    }


    /// <summary>
    /// Crea la tabla si falta dentro de una conexión abierta. Se ejecuta
    /// antes de cada lectura o escritura porque la sincronización con el PC
    /// puede sustituir la base de datos por una copia antigua sin la tabla.
    /// </summary>
    private static void AsegurarTablaTareasEnConexion(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlTablaTareas;

        cmd.ExecuteNonQuery();
    }


    public static List<Tarea> ObtenerTareasMobile()
    {
        var lista =
            new List<Tarea>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarTablaTareasEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            SELECT
                Id,
                Titulo,
                Descripcion,
                FechaInicio,
                FechaFin,
                TodoElDia,
                Hora,
                AvisoMinutos,
                Repeticion,
                Compartida,
                Completada,
                CreadaPor,
                FechaCreacion,
                UltimaModificacion
            FROM Tareas
            ORDER BY FechaInicio ASC, Id ASC;";

        using var reader =
            cmd.ExecuteReader();

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
                FechaCreacion = reader.IsDBNull(12) ? "" : reader.GetString(12),
                UltimaModificacion = reader.IsDBNull(13) ? "" : reader.GetString(13)
            });
        }

        return lista;
    }


    public static int GuardarTareaMobile(
        Tarea tarea)
    {
        AnotarActividadMobile(
            "Tareas",
            (tarea.Id > 0 ? "Editó la tarea «" : "Creó la tarea «") +
            (tarea.Titulo ?? "") + "»");

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        AsegurarTablaTareasEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        tarea.UltimaModificacion =
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (string.IsNullOrWhiteSpace(tarea.FechaCreacion))
        {
            tarea.FechaCreacion =
                tarea.UltimaModificacion;
        }

        if (tarea.Id <= 0)
        {
            cmd.CommandText = @"
                INSERT INTO Tareas
                    (Titulo, Descripcion, FechaInicio, FechaFin, TodoElDia,
                     Hora, AvisoMinutos, Repeticion, Compartida, Completada,
                     CreadaPor, FechaCreacion, UltimaModificacion)
                VALUES
                    ($titulo, $descripcion, $inicio, $fin, $todoElDia,
                     $hora, $aviso, $repeticion, $compartida, $completada,
                     $creadaPor, $fechaCreacion, $ultimaModificacion);
                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE Tareas SET
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
                    CreadaPor = $creadaPor,
                    FechaCreacion = $fechaCreacion,
                    UltimaModificacion = $ultimaModificacion
                WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$id",
                tarea.Id);
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


    public static void MarcarTareaCompletadaMobile(
        int id,
        bool completada)
    {
        AnotarActividadMobile(
            "Tareas",
            (completada ? "Completó la tarea «" : "Reabrió la tarea «") +
            NombreTareaPorIdMobile(id) + "»");

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            UPDATE Tareas
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


    public static void EliminarTareaMobile(
        int id)
    {
        AnotarActividadMobile(
            "Tareas",
            "Eliminó la tarea «" + NombreTareaPorIdMobile(id) + "»");

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM Tareas WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }
}
