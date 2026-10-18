using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;

/// <summary>
/// Escuelas de precursores (móvil): clases (una semana por escuela, de
/// lunes a sábado), estudiantes de cada clase, asientos y programa de
/// limpieza. Tablas nuevas que viajan con el motor de cambios
/// (Id GUID: sin colisiones entre dispositivos).
/// </summary>
public static partial class DatabaseHelper
{
    private const string SqlTablaEscuelasClases = @"
        CREATE TABLE IF NOT EXISTS EscuelasClases (
            Id TEXT PRIMARY KEY,
            SemanaInicio TEXT DEFAULT '',
            Congregacion TEXT DEFAULT '',
            InstructorId INTEGER DEFAULT 0,
            InstructorNombre TEXT DEFAULT '',
            SustitutoId INTEGER DEFAULT 0,
            SustitutoNombre TEXT DEFAULT '',
            Asientos INTEGER DEFAULT 12,
            Notas TEXT DEFAULT '',
            FechaCreacion TEXT DEFAULT '',
            Filas INTEGER DEFAULT 0,
            Columnas INTEGER DEFAULT 0,
            Pasillos TEXT DEFAULT ''
        );";

    private const string SqlTablaEscuelasAlumnos = @"
        CREATE TABLE IF NOT EXISTS EscuelasAlumnos (
            Id TEXT PRIMARY KEY,
            EscuelaId TEXT DEFAULT '',
            PersonaId INTEGER DEFAULT 0,
            Nombre TEXT DEFAULT '',
            Asiento INTEGER DEFAULT 0,
            LimpiezaDia TEXT DEFAULT ''
        );";


    /// <summary>
    /// Crea las tablas si faltan. Se llama al arrancar la app (antes de
    /// crear los disparadores del motor) y antes de cada lectura o
    /// escritura.
    /// </summary>
    public static void AsegurarTablasEscuelasClasesMobile()
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasEscuelasClasesEnConexion(
            connection);
    }


    private static void AsegurarTablasEscuelasClasesEnConexion(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlTablaEscuelasClases
            + SqlTablaEscuelasAlumnos;

        cmd.ExecuteNonQuery();

        // Columnas nuevas (disposición de la sala) en bases ya creadas.
        AgregarColumnaSiFalta(
            connection,
            "EscuelasClases",
            "Filas",
            "INTEGER DEFAULT 0");

        AgregarColumnaSiFalta(
            connection,
            "EscuelasClases",
            "Columnas",
            "INTEGER DEFAULT 0");

        AgregarColumnaSiFalta(
            connection,
            "EscuelasClases",
            "Pasillos",
            "TEXT DEFAULT ''");
    }


    private static void AgregarColumnaSiFalta(
        SqliteConnection connection,
        string tabla,
        string columna,
        string definicion)
    {
        try
        {
            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                $"ALTER TABLE {tabla} ADD COLUMN {columna} {definicion};";

            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Ya existe: nada que hacer.
        }
    }


    // =========================================================
    // CLASES (SEMANAS DE ESCUELA)
    // =========================================================

    private const string SqlEscuelasClasesSelect = @"
        SELECT e.Id, e.SemanaInicio, e.Congregacion,
               e.InstructorId, e.InstructorNombre,
               e.SustitutoId, e.SustitutoNombre,
               e.Asientos, e.Notas, e.FechaCreacion,
               e.Filas, e.Columnas, e.Pasillos,
               (SELECT COUNT(*) FROM EscuelasAlumnos a
                 WHERE a.EscuelaId = e.Id) AS Alumnos
          FROM EscuelasClases e";


    public static List<EscuelaClaseMobile> ObtenerEscuelasClasesMobile()
    {
        var lista =
            new List<EscuelaClaseMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasEscuelasClasesEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlEscuelasClasesSelect
            + " ORDER BY e.SemanaInicio DESC, e.Id DESC;";

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            lista.Add(
                LeerEscuelaClase(
                    lector));
        }

        return lista;
    }


    public static EscuelaClaseMobile ObtenerEscuelaClaseMobile(
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasEscuelasClasesEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlEscuelasClasesSelect
            + " WHERE e.Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        using var lector =
            cmd.ExecuteReader();

        if (lector.Read())
        {
            return LeerEscuelaClase(
                lector);
        }

        return null;
    }


    private static EscuelaClaseMobile LeerEscuelaClase(
        SqliteDataReader lector)
    {
        return new EscuelaClaseMobile
        {
            Id = lector.IsDBNull(0) ? "" : lector.GetString(0),
            SemanaInicio = lector.IsDBNull(1) ? "" : lector.GetString(1),
            Congregacion = lector.IsDBNull(2) ? "" : lector.GetString(2),
            InstructorId = lector.IsDBNull(3) ? 0 : lector.GetInt32(3),
            InstructorNombre = lector.IsDBNull(4) ? "" : lector.GetString(4),
            SustitutoId = lector.IsDBNull(5) ? 0 : lector.GetInt32(5),
            SustitutoNombre = lector.IsDBNull(6) ? "" : lector.GetString(6),
            Asientos = lector.IsDBNull(7) ? 12 : lector.GetInt32(7),
            Notas = lector.IsDBNull(8) ? "" : lector.GetString(8),
            FechaCreacion = lector.IsDBNull(9) ? "" : lector.GetString(9),
            Filas = lector.IsDBNull(10) ? 0 : lector.GetInt32(10),
            Columnas = lector.IsDBNull(11) ? 0 : lector.GetInt32(11),
            Pasillos = lector.IsDBNull(12) ? "" : lector.GetString(12),
            Alumnos = lector.IsDBNull(13) ? 0 : lector.GetInt32(13)
        };
    }


    public static void GuardarEscuelaClaseMobile(
        EscuelaClaseMobile escuela)
    {
        if (escuela == null)
        {
            return;
        }

        bool esNueva =
            string.IsNullOrWhiteSpace(
                escuela.Id);

        if (esNueva)
        {
            escuela.Id =
                Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(
                escuela.FechaCreacion))
        {
            escuela.FechaCreacion =
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss");
        }

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO EscuelasClases
                    (Id, SemanaInicio, Congregacion, InstructorId,
                     InstructorNombre, SustitutoId, SustitutoNombre,
                     Asientos, Notas, FechaCreacion,
                     Filas, Columnas, Pasillos)
                VALUES
                    ($id, $semana, $cong, $instId, $instNom,
                     $susId, $susNom, $asientos, $notas, $creada,
                     $filas, $cols, $pasillos)
                ON CONFLICT(Id) DO UPDATE SET
                    SemanaInicio = excluded.SemanaInicio,
                    Congregacion = excluded.Congregacion,
                    InstructorId = excluded.InstructorId,
                    InstructorNombre = excluded.InstructorNombre,
                    SustitutoId = excluded.SustitutoId,
                    SustitutoNombre = excluded.SustitutoNombre,
                    Asientos = excluded.Asientos,
                    Notas = excluded.Notas,
                    Filas = CASE WHEN excluded.Filas > 0 THEN excluded.Filas ELSE EscuelasClases.Filas END,
                    Columnas = CASE WHEN excluded.Columnas > 0 THEN excluded.Columnas ELSE EscuelasClases.Columnas END,
                    Pasillos = CASE WHEN excluded.Filas > 0 THEN excluded.Pasillos ELSE EscuelasClases.Pasillos END;";

            cmd.Parameters.AddWithValue("$id", escuela.Id);
            cmd.Parameters.AddWithValue("$semana", escuela.SemanaInicio ?? "");
            cmd.Parameters.AddWithValue("$cong", escuela.Congregacion ?? "");
            cmd.Parameters.AddWithValue("$instId", escuela.InstructorId);
            cmd.Parameters.AddWithValue("$instNom", escuela.InstructorNombre ?? "");
            cmd.Parameters.AddWithValue("$susId", escuela.SustitutoId);
            cmd.Parameters.AddWithValue("$susNom", escuela.SustitutoNombre ?? "");
            cmd.Parameters.AddWithValue("$asientos", escuela.Asientos < 1 ? 12 : escuela.Asientos);
            cmd.Parameters.AddWithValue("$notas", escuela.Notas ?? "");
            cmd.Parameters.AddWithValue("$creada", escuela.FechaCreacion);
            cmd.Parameters.AddWithValue("$filas", escuela.Filas);
            cmd.Parameters.AddWithValue("$cols", escuela.Columnas);
            cmd.Parameters.AddWithValue("$pasillos", escuela.Pasillos ?? "");

            cmd.ExecuteNonQuery();
        }

        AnotarActividadMobile(
            "Escuelas",
            (esNueva
                ? "Creó la escuela de la "
                : "Editó la escuela de la ")
            + escuela.SemanaTexto.ToLower());
    }


    public static void EliminarEscuelaClaseMobile(
        string id)
    {
        EscuelaClaseMobile escuela =
            ObtenerEscuelaClaseMobile(
                id);

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            AsegurarTablasEscuelasProgramaEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText = @"
                DELETE FROM EscuelasAlumnos
                 WHERE EscuelaId = $id;

                DELETE FROM EscuelasTalleres
                 WHERE EscuelaId = $id;

                DELETE FROM EscuelasHorario
                 WHERE EscuelaId = $id;

                DELETE FROM EscuelasClases
                 WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$id",
                id ?? "");

            cmd.ExecuteNonQuery();
        }

        if (escuela != null)
        {
            AnotarActividadMobile(
                "Escuelas",
                "Eliminó la escuela de la "
                + escuela.SemanaTexto.ToLower()
                + (string.IsNullOrWhiteSpace(
                       escuela.Congregacion)
                    ? ""
                    : " (" + escuela.Congregacion + ")"));
        }
    }


    // =========================================================
    // ESTUDIANTES DE UNA CLASE
    // =========================================================

    public static List<EscuelaAlumnoMobile> ObtenerAlumnosEscuelaMobile(
        string escuelaId)
    {
        var lista =
            new List<EscuelaAlumnoMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasEscuelasClasesEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            SELECT Id, EscuelaId, PersonaId, Nombre,
                   Asiento, LimpiezaDia
              FROM EscuelasAlumnos
             WHERE EscuelaId = $id
             ORDER BY Nombre ASC;";

        cmd.Parameters.AddWithValue(
            "$id",
            escuelaId ?? "");

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            lista.Add(
                LeerEscuelaAlumno(
                    lector));
        }

        return lista;
    }


    private static EscuelaAlumnoMobile ObtenerAlumnoEscuela(
        string alumnoId)
    {
        if (string.IsNullOrWhiteSpace(
                alumnoId))
        {
            return null;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasEscuelasClasesEnConexion(
            connection);

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            SELECT Id, EscuelaId, PersonaId, Nombre,
                   Asiento, LimpiezaDia
              FROM EscuelasAlumnos
             WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            alumnoId);

        using var lector =
            cmd.ExecuteReader();

        if (lector.Read())
        {
            return LeerEscuelaAlumno(
                lector);
        }

        return null;
    }


    private static EscuelaAlumnoMobile LeerEscuelaAlumno(
        SqliteDataReader lector)
    {
        return new EscuelaAlumnoMobile
        {
            Id = lector.IsDBNull(0) ? "" : lector.GetString(0),
            EscuelaId = lector.IsDBNull(1) ? "" : lector.GetString(1),
            PersonaId = lector.IsDBNull(2) ? 0 : lector.GetInt32(2),
            Nombre = lector.IsDBNull(3) ? "" : lector.GetString(3),
            Asiento = lector.IsDBNull(4) ? 0 : lector.GetInt32(4),
            LimpiezaDia = lector.IsDBNull(5) ? "" : lector.GetString(5)
        };
    }


    public static int AgregarAlumnosEscuelaMobile(
        string escuelaId,
        List<Persona> personas)
    {
        if (string.IsNullOrWhiteSpace(
                escuelaId)
            || personas == null
            || personas.Count == 0)
        {
            return 0;
        }

        int agregados =
            0;

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            foreach (Persona persona in
                     personas)
            {
                if (persona == null)
                {
                    continue;
                }

                using (var comprobar =
                       connection.CreateCommand())
                {
                    comprobar.Transaction =
                        transaccion;

                    comprobar.CommandText = @"
                        SELECT COUNT(*) FROM EscuelasAlumnos
                         WHERE EscuelaId = $e
                           AND PersonaId = $p;";

                    comprobar.Parameters.AddWithValue(
                        "$e",
                        escuelaId);

                    comprobar.Parameters.AddWithValue(
                        "$p",
                        persona.Id);

                    long existe =
                        (long)comprobar.ExecuteScalar();

                    if (existe > 0)
                    {
                        continue;
                    }
                }

                using var cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaccion;

                cmd.CommandText = @"
                    INSERT INTO EscuelasAlumnos
                        (Id, EscuelaId, PersonaId, Nombre,
                         Asiento, LimpiezaDia)
                    VALUES
                        ($id, $e, $p, $n, 0, '');";

                cmd.Parameters.AddWithValue(
                    "$id",
                    Guid.NewGuid().ToString("N"));

                cmd.Parameters.AddWithValue(
                    "$e",
                    escuelaId);

                cmd.Parameters.AddWithValue(
                    "$p",
                    persona.Id);

                cmd.Parameters.AddWithValue(
                    "$n",
                    string.IsNullOrWhiteSpace(
                        persona.NombreComun)
                        ? persona.Nombre
                        : persona.NombreComun);

                cmd.ExecuteNonQuery();

                agregados++;
            }

            transaccion.Commit();
        }

        if (agregados > 0)
        {
            AnotarActividadMobile(
                "Escuelas",
                $"Añadió {agregados} estudiante(s) a la escuela de la "
                + TextoSemanaDe(
                    escuelaId));
        }

        return agregados;
    }


    public static void QuitarAlumnoEscuelaMobile(
        string alumnoId)
    {
        EscuelaAlumnoMobile alumno =
            ObtenerAlumnoEscuela(
                alumnoId);

        if (alumno == null)
        {
            return;
        }

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "DELETE FROM EscuelasAlumnos WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$id",
                alumnoId);

            cmd.ExecuteNonQuery();
        }

        AnotarActividadMobile(
            "Escuelas",
            $"Quitó a {alumno.Nombre} de la escuela de la "
            + TextoSemanaDe(
                alumno.EscuelaId));
    }


    // =========================================================
    // ASIENTOS
    // =========================================================

    public static void AsignarAsientoEscuelaMobile(
        string alumnoId,
        int asiento)
    {
        EscuelaAlumnoMobile alumno =
            ObtenerAlumnoEscuela(
                alumnoId);

        if (alumno == null)
        {
            return;
        }

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            if (asiento > 0)
            {
                using var liberar =
                    connection.CreateCommand();

                liberar.CommandText = @"
                    UPDATE EscuelasAlumnos
                       SET Asiento = 0
                     WHERE EscuelaId = $e
                       AND Asiento = $a
                       AND Id != $id;";

                liberar.Parameters.AddWithValue(
                    "$e",
                    alumno.EscuelaId);

                liberar.Parameters.AddWithValue(
                    "$a",
                    asiento);

                liberar.Parameters.AddWithValue(
                    "$id",
                    alumnoId);

                liberar.ExecuteNonQuery();
            }

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "UPDATE EscuelasAlumnos SET Asiento = $a WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$a",
                asiento < 0 ? 0 : asiento);

            cmd.Parameters.AddWithValue(
                "$id",
                alumnoId);

            cmd.ExecuteNonQuery();
        }

        AnotarActividadMobile(
            "Escuelas",
            asiento > 0
                ? $"Sentó a {alumno.Nombre} en el asiento {asiento} de la escuela de la "
                  + TextoSemanaDe(
                      alumno.EscuelaId)
                : $"Dejó sin asiento a {alumno.Nombre} en la escuela de la "
                  + TextoSemanaDe(
                      alumno.EscuelaId));
    }


    // =========================================================
    // PROGRAMA DE LIMPIEZA
    // =========================================================

    public static void AsignarLimpiezaEscuelaMobile(
        string alumnoId,
        string dia)
    {
        EscuelaAlumnoMobile alumno =
            ObtenerAlumnoEscuela(
                alumnoId);

        if (alumno == null)
        {
            return;
        }

        dia =
            (dia ?? "").Trim();

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "UPDATE EscuelasAlumnos SET LimpiezaDia = $d WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$d",
                dia);

            cmd.Parameters.AddWithValue(
                "$id",
                alumnoId);

            cmd.ExecuteNonQuery();
        }

        AnotarActividadMobile(
            "Escuelas",
            dia.Length > 0
                ? $"Puso a {alumno.Nombre} para limpiar el {dia.ToLower()} en la escuela de la "
                  + TextoSemanaDe(
                      alumno.EscuelaId)
                : $"Quitó a {alumno.Nombre} del programa de limpieza de la escuela de la "
                  + TextoSemanaDe(
                      alumno.EscuelaId));
    }


    private static string TextoSemanaDe(
        string escuelaId)
    {
        try
        {
            EscuelaClaseMobile escuela =
                ObtenerEscuelaClaseMobile(
                    escuelaId);

            if (escuela != null)
            {
                return escuela.SemanaTexto.ToLower();
            }
        }
        catch
        {
        }

        return "semana sin fecha";
    }


    // =========================================================
    // DISPOSICIÓN DE LA SALA (filas, columnas y pasillos)
    // =========================================================

    /// <summary>
    /// Ajusta la disposición: F filas × C columnas, con pasillos
    /// después de las columnas indicadas ("2,4"). Actualiza el total
    /// de butacas y libera las que ya no existen.
    /// </summary>
    public static void EstablecerDisposicionEscuelaMobile(
        string escuelaId,
        int filas,
        int columnas,
        string pasillos)
    {
        if (string.IsNullOrWhiteSpace(
                escuelaId))
        {
            return;
        }

        if (filas < 1)
        {
            filas = 1;
        }

        if (filas > 15)
        {
            filas = 15;
        }

        if (columnas < 1)
        {
            columnas = 1;
        }

        if (columnas > 20)
        {
            columnas = 20;
        }

        List<int> listaPasillos =
            EscuelaClaseMobile.ParsearPasillos(
                pasillos,
                columnas);

        string pasillosLimpios =
            string.Join(
                ",",
                listaPasillos);

        int total =
            filas * columnas;

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            AsegurarTablasEscuelasClasesEnConexion(
                connection);

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE EscuelasClases
                       SET Filas = $f,
                           Columnas = $c,
                           Pasillos = $p,
                           Asientos = $a
                     WHERE Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$f",
                    filas);

                cmd.Parameters.AddWithValue(
                    "$c",
                    columnas);

                cmd.Parameters.AddWithValue(
                    "$p",
                    pasillosLimpios);

                cmd.Parameters.AddWithValue(
                    "$a",
                    total);

                cmd.Parameters.AddWithValue(
                    "$id",
                    escuelaId);

                cmd.ExecuteNonQuery();
            }

            // Las butacas que ya no existen quedan libres.
            using var liberar =
                connection.CreateCommand();

            liberar.CommandText = @"
                UPDATE EscuelasAlumnos
                   SET Asiento = 0
                 WHERE EscuelaId = $e
                   AND Asiento > $a;";

            liberar.Parameters.AddWithValue(
                "$e",
                escuelaId);

            liberar.Parameters.AddWithValue(
                "$a",
                total);

            liberar.ExecuteNonQuery();
        }

        EscuelaClaseMobile escuela =
            ObtenerEscuelaClaseMobile(
                escuelaId);

        if (escuela != null)
        {
            string pasillosTexto =
                listaPasillos.Count == 0
                    ? ""
                    : " (pasillos tras las columnas "
                      + string.Join(
                          " y ",
                          listaPasillos)
                      + ")";

            AnotarActividadMobile(
                "Escuelas",
                $"Cambió la disposición de la escuela de la "
                + escuela.SemanaTexto.ToLower()
                + $" a {filas} filas × {columnas} columnas"
                + pasillosTexto);
        }
    }


    // =========================================================
    // INSTRUCTORES POSIBLES
    // =========================================================

    /// <summary>
    /// Puede ser instructor de escuela: precursor regular, precursor
    /// especial o misionero, y además hombre y anciano.
    /// </summary>
    public static bool EsInstructorPosibleMobile(
        Persona persona)
    {
        if (persona == null)
        {
            return false;
        }

        if (!string.Equals(
                persona.Sexo,
                "Hombre",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string nombramiento =
            persona.Nombramiento ?? "";

        if (nombramiento.IndexOf(
                "anciano",
                StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        string servicio =
            persona.Servicio ?? "";

        return servicio.IndexOf(
                   "Precursor regular",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || servicio.IndexOf(
                   "Precursor especial",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || servicio.IndexOf(
                   "Misionero",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }


    public static List<Persona> ObtenerInstructoresEscuelaMobile()
    {
        var lista =
            new List<Persona>();

        try
        {
            foreach (Persona persona in
                     ObtenerPersonas())
            {
                if (EsInstructorPosibleMobile(
                        persona))
                {
                    lista.Add(
                        persona);
                }
            }
        }
        catch
        {
        }

        return lista
            .OrderBy(
                p => string.IsNullOrWhiteSpace(
                         p.NombreComun)
                     ? p.Nombre
                     : p.NombreComun)
            .ToList();
    }
}
