using System.Globalization;

using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// EXTENSIÓN DE DATABASEHELPER PARA CALENDARIO
// ============================================================================
//
// IMPORTANTE:
// El DatabaseHelper principal debe declararse así:
//
//     public static partial class DatabaseHelper
//
// en lugar de:
//
//     public static class DatabaseHelper
//
// No hay que cambiar nada más en el DatabaseHelper principal.
//
// Esta parte reutiliza el ConnectionString PRIVADO del DatabaseHelper principal,
// ya que ambas declaraciones forman la misma clase partial.
// ============================================================================

public static partial class DatabaseHelper
{
    // ========================================================================
    // OBTENER TODAS LAS ACTIVIDADES DE UNA FECHA
    // ========================================================================

    public static List<SemanaCalendario>
        ObtenerTodasSemanasPorFecha(
            string fechaIso)
    {
        var resultado =
            new List<SemanaCalendario>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarEstructuraCalendarioMobile(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                FechaMartes,
                Actividad,
                Congregacion,
                Tipo,
                Acompanante,
                Alojamiento,
                S302Enviado,
                DocEnviada,
                SeccionAsamblea,
                FechaAsamblea,
                NotificacionEnviada,
                RecordatorioEnviado,
                DatosImportados
            FROM Semanas
            WHERE
                date(FechaMartes) = date($fecha)
                OR FechaMartes LIKE $fecha || '%'
            ORDER BY Id ASC;
            """;

        cmd.Parameters.AddWithValue(
            "$fecha",
            fechaIso);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                LeerSemanaCalendarioMobile(
                    reader));
        }

        return resultado;
    }


    // ========================================================================
    // GUARDAR / ACTUALIZAR SEMANA
    // ========================================================================

    public static void GuardarSemana(
        SemanaCalendario semana)
    {
        ArgumentNullException
            .ThrowIfNull(
                semana);

        AnotarActividadMobile(
            "Calendario",
            "Guardó la semana del " +
            semana.FechaMartes.ToString("dd/MM/yyyy"));

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarEstructuraCalendarioMobile(
            connection);


        if (semana.Id > 0)
        {
            using var cmdUpdate =
                connection.CreateCommand();

            cmdUpdate.CommandText =
                """
                UPDATE Semanas
                SET
                    Actividad = $actividad,
                    Congregacion = $congregacion,
                    Tipo = $tipo,
                    Acompanante = $acompanante,
                    Alojamiento = $alojamiento,
                    S302Enviado = $s302,
                    DocEnviada = $doc,
                    SeccionAsamblea = $seccion,
                    FechaAsamblea = $fechaAsamblea,
                    NotificacionEnviada = $notificacion,
                    RecordatorioEnviado = $recordatorio,
                    DatosImportados = $datosImportados
                WHERE Id = $id;
                """;

            cmdUpdate.Parameters.AddWithValue(
                "$id",
                semana.Id);

            AgregarParametrosSemanaMobile(
                cmdUpdate,
                semana);

            cmdUpdate.ExecuteNonQuery();

            return;
        }


        using var cmdInsert =
            connection.CreateCommand();

        cmdInsert.CommandText =
            """
            INSERT INTO Semanas
            (
                FechaMartes,
                Actividad,
                Congregacion,
                Tipo,
                Acompanante,
                Alojamiento,
                S302Enviado,
                DocEnviada,
                SeccionAsamblea,
                FechaAsamblea,
                NotificacionEnviada,
                RecordatorioEnviado,
                DatosImportados
            )
            VALUES
            (
                $fechaMartes,
                $actividad,
                $congregacion,
                $tipo,
                $acompanante,
                $alojamiento,
                $s302,
                $doc,
                $seccion,
                $fechaAsamblea,
                $notificacion,
                $recordatorio,
                $datosImportados
            );

            SELECT last_insert_rowid();
            """;

        cmdInsert.Parameters.AddWithValue(
            "$fechaMartes",
            semana.FechaMartes);

        AgregarParametrosSemanaMobile(
            cmdInsert,
            semana);

        object? nuevoId =
            cmdInsert.ExecuteScalar();

        if (nuevoId is not null
            &&
            nuevoId != DBNull.Value)
        {
            semana.Id =
                Convert.ToInt32(
                    nuevoId);
        }
    }


    // ========================================================================
    // ELIMINAR ACTIVIDAD PARALELA
    // ========================================================================

    public static void
        EliminarSemanaCalendario(
            int id)
    {
        if (id <= 0)
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarEstructuraCalendarioMobile(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            DELETE FROM Semanas
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    // ========================================================================
    // SECCIONES DEL CIRCUITO
    //
    // Para esta primera versión Android solo necesitamos el nombre.
    // Devolvemos List<string> para no obligar a portar todavía el modelo
    // completo SeccionCircuito de Windows.
    // ========================================================================

    public static List<string>
        ObtenerSecciones()
    {
        var resultado =
            new List<string>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarEstructuraCalendarioMobile(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Nombre
            FROM SeccionesCircuito
            WHERE Nombre IS NOT NULL
              AND TRIM(Nombre) <> ''
            ORDER BY Nombre;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string nombre =
                reader.IsDBNull(0)
                    ? ""
                    : reader.GetString(0);

            if (!string.IsNullOrWhiteSpace(
                    nombre))
            {
                resultado.Add(
                    nombre);
            }
        }

        return resultado;
    }


    // ========================================================================
    // CONVERSIÓN SQLITE -> SEMANA
    // ========================================================================

    private static SemanaCalendario
        LeerSemanaCalendarioMobile(
            SqliteDataReader reader)
    {
        return new SemanaCalendario
        {
            Id =
                Convert.ToInt32(
                    reader["Id"]),

            FechaMartes =
                LeerFechaSqliteMobile(
                    reader[
                        "FechaMartes"]),

            Actividad =
                LeerTextoMobile(
                    reader,
                    "Actividad"),

            Congregacion =
                LeerTextoMobile(
                    reader,
                    "Congregacion"),

            Tipo =
                LeerTextoMobile(
                    reader,
                    "Tipo"),

            Acompanante =
                LeerTextoMobile(
                    reader,
                    "Acompanante"),

            Alojamiento =
                LeerTextoMobile(
                    reader,
                    "Alojamiento"),

            S302Enviado =
                LeerBoolMobile(
                    reader,
                    "S302Enviado"),

            DocEnviada =
                LeerBoolMobile(
                    reader,
                    "DocEnviada"),

            SeccionAsamblea =
                LeerTextoMobile(
                    reader,
                    "SeccionAsamblea"),

            FechaAsamblea =
                LeerTextoMobile(
                    reader,
                    "FechaAsamblea"),

            NotificacionEnviada =
                LeerBoolMobile(
                    reader,
                    "NotificacionEnviada"),

            RecordatorioEnviado =
                LeerBoolMobile(
                    reader,
                    "RecordatorioEnviado"),

            TieneDatosImportados =
                LeerBoolMobile(
                    reader,
                    "DatosImportados")
        };
    }


    // ========================================================================
    // PARÁMETROS COMUNES
    // ========================================================================

    private static void
        AgregarParametrosSemanaMobile(
            SqliteCommand cmd,
            SemanaCalendario semana)
    {
        cmd.Parameters.AddWithValue(
            "$actividad",
            semana.Actividad
            ?? "");

        cmd.Parameters.AddWithValue(
            "$congregacion",
            semana.Congregacion
            ?? "");

        cmd.Parameters.AddWithValue(
            "$tipo",
            semana.Tipo
            ?? "");

        cmd.Parameters.AddWithValue(
            "$acompanante",
            semana.Acompanante
            ?? "");

        cmd.Parameters.AddWithValue(
            "$alojamiento",
            semana.Alojamiento
            ?? "");

        cmd.Parameters.AddWithValue(
            "$s302",
            semana.S302Enviado
                ? 1
                : 0);

        cmd.Parameters.AddWithValue(
            "$doc",
            semana.DocEnviada
                ? 1
                : 0);

        cmd.Parameters.AddWithValue(
            "$seccion",
            semana.SeccionAsamblea
            ?? "");

        cmd.Parameters.AddWithValue(
            "$fechaAsamblea",
            semana.FechaAsamblea
            ?? "");

        cmd.Parameters.AddWithValue(
            "$notificacion",
            semana.NotificacionEnviada
                ? 1
                : 0);

        cmd.Parameters.AddWithValue(
            "$recordatorio",
            semana.RecordatorioEnviado
                ? 1
                : 0);

        cmd.Parameters.AddWithValue(
            "$datosImportados",
            semana.TieneDatosImportados
                ? 1
                : 0);
    }


    // ========================================================================
    // ESTRUCTURA NECESARIA
    //
    // Es deliberadamente defensiva:
    // - no borra tablas;
    // - no borra datos;
    // - no modifica columnas existentes;
    // - solo crea lo que falte.
    // ========================================================================

    private static void
        AsegurarEstructuraCalendarioMobile(
            SqliteConnection connection)
    {
        using (
            SqliteCommand cmd =
                connection.CreateCommand())
        {
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Semanas
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FechaMartes DATETIME,
                    Actividad TEXT,
                    Congregacion TEXT,
                    Tipo TEXT,
                    Acompanante TEXT,
                    Alojamiento TEXT,
                    S302Enviado INTEGER DEFAULT 0,
                    DocEnviada INTEGER DEFAULT 0,
                    SeccionAsamblea TEXT,
                    FechaAsamblea TEXT,
                    NotificacionEnviada INTEGER DEFAULT 0,
                    RecordatorioEnviado INTEGER DEFAULT 0,
                    DatosImportados INTEGER DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS SeccionesCircuito
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT,
                    LugarAsamblea TEXT
                );
                """;

            cmd.ExecuteNonQuery();
        }

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "Actividad",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "Congregacion",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "Tipo",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "Acompanante",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "Alojamiento",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "S302Enviado",
            "INTEGER DEFAULT 0");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "DocEnviada",
            "INTEGER DEFAULT 0");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "SeccionAsamblea",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "FechaAsamblea",
            "TEXT");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "NotificacionEnviada",
            "INTEGER DEFAULT 0");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "RecordatorioEnviado",
            "INTEGER DEFAULT 0");

        AsegurarColumnaCalendarioMobile(
            connection,
            "Semanas",
            "DatosImportados",
            "INTEGER DEFAULT 0");
    }


    private static void
        AsegurarColumnaCalendarioMobile(
            SqliteConnection connection,
            string tabla,
            string columna,
            string definicion)
    {
        bool existe =
            false;

        using (
            SqliteCommand cmdCheck =
                connection.CreateCommand())
        {
            cmdCheck.CommandText =
                $"PRAGMA table_info({tabla});";

            using SqliteDataReader reader =
                cmdCheck.ExecuteReader();

            while (reader.Read())
            {
                string nombre =
                    reader.GetString(1);

                if (string.Equals(
                        nombre,
                        columna,
                        StringComparison
                            .OrdinalIgnoreCase))
                {
                    existe =
                        true;

                    break;
                }
            }
        }

        if (existe)
        {
            return;
        }

        using SqliteCommand cmdAlter =
            connection.CreateCommand();

        cmdAlter.CommandText =
            $"ALTER TABLE {tabla} " +
            $"ADD COLUMN {columna} " +
            $"{definicion};";

        cmdAlter.ExecuteNonQuery();
    }


    // ========================================================================
    // LECTURA SEGURA
    // ========================================================================

    private static string
        LeerTextoMobile(
            SqliteDataReader reader,
            string columna)
    {
        int ordinal =
            reader.GetOrdinal(
                columna);

        return reader.IsDBNull(
                ordinal)
            ? ""
            : reader.GetString(
                ordinal);
    }


    private static bool
        LeerBoolMobile(
            SqliteDataReader reader,
            string columna)
    {
        int ordinal =
            reader.GetOrdinal(
                columna);

        if (reader.IsDBNull(
                ordinal))
        {
            return false;
        }

        object valor =
            reader.GetValue(
                ordinal);

        if (valor is long largo)
        {
            return largo != 0;
        }

        if (valor is int entero)
        {
            return entero != 0;
        }

        if (valor is bool booleano)
        {
            return booleano;
        }

        return int.TryParse(
                   valor.ToString(),
                   out int numero)
               &&
               numero != 0;
    }


    private static DateTime
        LeerFechaSqliteMobile(
            object? valor)
    {
        if (valor is DateTime fecha)
        {
            return fecha;
        }

        string texto =
            Convert.ToString(
                valor,
                CultureInfo.InvariantCulture)
            ?? "";

        if (DateTime.TryParse(
                texto,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime resultado))
        {
            return resultado;
        }

        if (DateTime.TryParse(
                texto,
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out resultado))
        {
            return resultado;
        }

        throw new FormatException(
            "No se pudo interpretar FechaMartes: " +
            texto);
    }
}
