using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    /// <summary>
    /// Escuelas de precursores: clases (una semana por escuela, de lunes
    /// a sábado), estudiantes de cada clase, asientos y programa de
    /// limpieza. Tablas nuevas que viajan con el motor de cambios
    /// (Id GUID: sin colisiones entre dispositivos).
    /// </summary>
    public static partial class DatabaseHelper
    {
        private const string SqlTablaEscuelasClases =
            @"CREATE TABLE IF NOT EXISTS EscuelasClases (
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

        private const string SqlTablaEscuelasAlumnos =
            @"CREATE TABLE IF NOT EXISTS EscuelasAlumnos (
                Id TEXT PRIMARY KEY,
                EscuelaId TEXT DEFAULT '',
                PersonaId INTEGER DEFAULT 0,
                Nombre TEXT DEFAULT '',
                Asiento INTEGER DEFAULT 0,
                LimpiezaDia TEXT DEFAULT ''
            );";


        /// <summary>
        /// Crea las tablas si faltan. Se llama al inicializar la base y
        /// antes de cada lectura/escritura (auto-reparable).
        /// </summary>
        public static void AsegurarTablasEscuelasClasesInterno(
            SqliteConnection connection)
        {
            EjecutarQuery(
                connection,
                SqlTablaEscuelasClases);

            EjecutarQuery(
                connection,
                SqlTablaEscuelasAlumnos);

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
                var cmd =
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


        // =====================================================
        // CLASES (SEMANAS DE ESCUELA)
        // =====================================================

        private const string SqlEscuelasClasesSelect =
            @"SELECT e.Id, e.SemanaInicio, e.Congregacion,
                     e.InstructorId, e.InstructorNombre,
                     e.SustitutoId, e.SustitutoNombre,
                     e.Asientos, e.Notas, e.FechaCreacion,
                     e.Filas, e.Columnas, e.Pasillos,
                     (SELECT COUNT(*) FROM EscuelasAlumnos a
                       WHERE a.EscuelaId = e.Id) AS Alumnos
                FROM EscuelasClases e";


        public static List<EscuelaClase> ObtenerEscuelasClases()
        {
            var lista =
                new List<EscuelaClase>();

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    SqlEscuelasClasesSelect
                    + " ORDER BY e.SemanaInicio DESC, e.Id DESC;";

                using (var reader =
                       cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            LeerEscuelaClase(
                                reader));
                    }
                }
            }

            return lista;
        }


        public static EscuelaClase ObtenerEscuelaClase(
            string id)
        {
            if (string.IsNullOrWhiteSpace(
                    id))
            {
                return null;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    SqlEscuelasClasesSelect
                    + " WHERE e.Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    id);

                using (var reader =
                       cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return LeerEscuelaClase(
                            reader);
                    }
                }
            }

            return null;
        }


        private static EscuelaClase LeerEscuelaClase(
            SqliteDataReader reader)
        {
            return new EscuelaClase
            {
                Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                SemanaInicio = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Congregacion = reader.IsDBNull(2) ? "" : reader.GetString(2),
                InstructorId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                InstructorNombre = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SustitutoId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                SustitutoNombre = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Asientos = reader.IsDBNull(7) ? 12 : reader.GetInt32(7),
                Notas = reader.IsDBNull(8) ? "" : reader.GetString(8),
                FechaCreacion = reader.IsDBNull(9) ? "" : reader.GetString(9),
                Filas = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                Columnas = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                Pasillos = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Alumnos = reader.IsDBNull(13) ? 0 : reader.GetInt32(13)
            };
        }


        public static string GuardarEscuelaClase(
            EscuelaClase escuela)
        {
            if (escuela == null)
            {
                return "";
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

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"INSERT INTO EscuelasClases
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

            AnotarActividad(
                "Escuelas",
                (esNueva
                    ? "Creó la escuela de la "
                    : "Editó la escuela de la ")
                + escuela.SemanaTexto.ToLower());

            return escuela.Id;
        }


        public static void EliminarEscuelaClase(
            string id)
        {
            EscuelaClase escuela =
                ObtenerEscuelaClase(
                    id);

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"DELETE FROM EscuelasAlumnos
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
                AnotarActividad(
                    "Escuelas",
                    "Eliminó la escuela de la "
                    + escuela.SemanaTexto.ToLower()
                    + (string.IsNullOrWhiteSpace(
                           escuela.Congregacion)
                        ? ""
                        : " (" + escuela.Congregacion + ")"));
            }
        }


        // =====================================================
        // ESTUDIANTES DE UNA CLASE
        // =====================================================

        public static List<EscuelaAlumno> ObtenerAlumnosEscuela(
            string escuelaId)
        {
            var lista =
                new List<EscuelaAlumno>();

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"SELECT Id, EscuelaId, PersonaId, Nombre,
                             Asiento, LimpiezaDia
                        FROM EscuelasAlumnos
                       WHERE EscuelaId = $id
                       ORDER BY Nombre ASC;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    escuelaId ?? "");

                using (var reader =
                       cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            LeerEscuelaAlumno(
                                reader));
                    }
                }
            }

            return lista;
        }


        private static EscuelaAlumno ObtenerAlumnoEscuela(
            string alumnoId)
        {
            if (string.IsNullOrWhiteSpace(
                    alumnoId))
            {
                return null;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"SELECT Id, EscuelaId, PersonaId, Nombre,
                             Asiento, LimpiezaDia
                        FROM EscuelasAlumnos
                       WHERE Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    alumnoId);

                using (var reader =
                       cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return LeerEscuelaAlumno(
                            reader);
                    }
                }
            }

            return null;
        }


        private static EscuelaAlumno LeerEscuelaAlumno(
            SqliteDataReader reader)
        {
            return new EscuelaAlumno
            {
                Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                EscuelaId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                PersonaId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Nombre = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Asiento = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                LimpiezaDia = reader.IsDBNull(5) ? "" : reader.GetString(5)
            };
        }


        public static int AgregarAlumnosEscuela(
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

                AsegurarTablasEscuelasClasesInterno(
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

                    var comprobar =
                        connection.CreateCommand();

                    comprobar.Transaction =
                        transaccion;

                    comprobar.CommandText =
                        @"SELECT COUNT(*) FROM EscuelasAlumnos
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

                    var cmd =
                        connection.CreateCommand();

                    cmd.Transaction =
                        transaccion;

                    cmd.CommandText =
                        @"INSERT INTO EscuelasAlumnos
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
                AnotarActividad(
                    "Escuelas",
                    $"Añadió {agregados} estudiante(s) a la escuela de la "
                    + TextoSemanaDe(
                        escuelaId));
            }

            return agregados;
        }


        public static void QuitarAlumnoEscuela(
            string alumnoId)
        {
            EscuelaAlumno alumno =
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

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    "DELETE FROM EscuelasAlumnos WHERE Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    alumnoId);

                cmd.ExecuteNonQuery();
            }

            AnotarActividad(
                "Escuelas",
                $"Quitó a {alumno.Nombre} de la escuela de la "
                + TextoSemanaDe(
                    alumno.EscuelaId));
        }


        // =====================================================
        // ASIENTOS
        // =====================================================

        public static void AsignarAsientoEscuela(
            string alumnoId,
            int asiento)
        {
            EscuelaAlumno alumno =
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

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                if (asiento > 0)
                {
                    var liberar =
                        connection.CreateCommand();

                    liberar.CommandText =
                        @"UPDATE EscuelasAlumnos
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

                var cmd =
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

            AnotarActividad(
                "Escuelas",
                asiento > 0
                    ? $"Sentó a {alumno.Nombre} en el asiento {asiento} de la escuela de la "
                      + TextoSemanaDe(
                          alumno.EscuelaId)
                    : $"Dejó sin asiento a {alumno.Nombre} en la escuela de la "
                      + TextoSemanaDe(
                          alumno.EscuelaId));
        }


        // =====================================================
        // PROGRAMA DE LIMPIEZA
        // =====================================================

        public static void AsignarLimpiezaEscuela(
            string alumnoId,
            string dia)
        {
            EscuelaAlumno alumno =
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

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
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

            AnotarActividad(
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
                EscuelaClase escuela =
                    ObtenerEscuelaClase(
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


        // =====================================================
        // DISPOSICIÓN DE LA SALA (filas, columnas y pasillos)
        // =====================================================

        /// <summary>
        /// Ajusta la disposición: F filas × C columnas, con pasillos
        /// después de las columnas indicadas ("2,4"). Actualiza el
        /// total de butacas y libera las que ya no existen.
        /// </summary>
        public static void EstablecerDisposicionEscuela(
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
                EscuelaClase.ParsearPasillos(
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

                AsegurarTablasEscuelasClasesInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"UPDATE EscuelasClases
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

                // Las butacas que ya no existen quedan libres.
                var liberar =
                    connection.CreateCommand();

                liberar.CommandText =
                    @"UPDATE EscuelasAlumnos
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

            EscuelaClase escuela =
                ObtenerEscuelaClase(
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

                AnotarActividad(
                    "Escuelas",
                    $"Cambió la disposición de la escuela de la "
                    + escuela.SemanaTexto.ToLower()
                    + $" a {filas} filas × {columnas} columnas"
                    + pasillosTexto);
            }
        }


        // =====================================================
        // INSTRUCTORES POSIBLES
        // =====================================================

        /// <summary>
        /// Puede ser instructor de escuela: precursor regular, precursor
        /// especial o misionero, y además hombre y anciano.
        /// </summary>
        public static bool EsInstructorPosible(
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


        public static List<Persona> ObtenerInstructoresEscuela()
        {
            var lista =
                new List<Persona>();

            try
            {
                foreach (Persona persona in
                         ObtenerPersonas())
                {
                    if (EsInstructorPosible(
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


    /// <summary>
    /// Una escuela (clase) de una semana completa, de lunes a sábado.
    /// </summary>
    public class EscuelaClase
    {
        public string Id { get; set; } = "";
        public string SemanaInicio { get; set; } = "";
        public string Congregacion { get; set; } = "";
        public int InstructorId { get; set; }
        public string InstructorNombre { get; set; } = "";
        public int SustitutoId { get; set; }
        public string SustitutoNombre { get; set; } = "";
        public int Asientos { get; set; } = 12;
        public string Notas { get; set; } = "";
        public string FechaCreacion { get; set; } = "";
        public int Filas { get; set; }
        public int Columnas { get; set; }
        public string Pasillos { get; set; } = "";
        public int Alumnos { get; set; }

        public int FilasEfectivas =>
            Filas > 0
                ? Filas
                : 4;

        public int ColumnasEfectivas =>
            Columnas > 0
                ? Columnas
                : 3;

        public List<int> PasillosLista =>
            ParsearPasillos(
                Pasillos,
                ColumnasEfectivas);

        public string DisposicionTexto =>
            $"{FilasEfectivas} filas × {ColumnasEfectivas} columnas";


        /// <summary>
        /// Convierte "2,4" en la lista ordenada de pasillos válidos
        /// (después de las columnas 1..columnas-1).
        /// </summary>
        public static List<int> ParsearPasillos(
            string pasillos,
            int columnas)
        {
            var lista =
                new List<int>();

            if (string.IsNullOrWhiteSpace(
                    pasillos))
            {
                return lista;
            }

            foreach (string parte in
                     pasillos.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(
                        parte.Trim(),
                        out int n)
                    && n >= 1
                    && n < columnas
                    && !lista.Contains(
                        n))
                {
                    lista.Add(
                        n);
                }
            }

            lista.Sort();

            return lista;
        }

        public string SemanaTexto =>
            FormatearSemana(
                SemanaInicio);

        public string InstructorTexto =>
            string.IsNullOrWhiteSpace(
                InstructorNombre)
                ? "Sin instructor"
                : InstructorNombre;

        public string SustitutoTexto =>
            string.IsNullOrWhiteSpace(
                SustitutoNombre)
                ? "Sin sustituto"
                : SustitutoNombre;

        public string Detalle =>
            (string.IsNullOrWhiteSpace(
                 Congregacion)
                 ? "Sin salón"
                 : Congregacion)
            + " · "
            + InstructorTexto
            + " · "
            + Alumnos
            + (Alumnos == 1
                ? " estudiante"
                : " estudiantes");

        public string NotasTexto =>
            string.IsNullOrWhiteSpace(
                Notas)
                ? "Sin notas"
                : Notas;


        /// <summary>
        /// "Semana del 9 al 14 de noviembre" (lunes a sábado).
        /// </summary>
        public static string FormatearSemana(
            string semanaInicio)
        {
            DateTime inicio;

            if (!DateTime.TryParse(
                    semanaInicio,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out inicio))
            {
                return string.IsNullOrWhiteSpace(
                           semanaInicio)
                       ? "Semana sin fecha"
                       : "Semana del " + semanaInicio;
            }

            DateTime fin =
                inicio.AddDays(5);

            string mesInicio =
                NombreMes(
                    inicio.Month);

            string mesFin =
                NombreMes(
                    fin.Month);

            if (inicio.Year != fin.Year)
            {
                return $"Semana del {inicio.Day} de {mesInicio} de {inicio.Year} al {fin.Day} de {mesFin} de {fin.Year}";
            }

            if (inicio.Month == fin.Month)
            {
                return $"Semana del {inicio.Day} al {fin.Day} de {mesInicio}";
            }

            return $"Semana del {inicio.Day} de {mesInicio} al {fin.Day} de {mesFin}";
        }


        private static string NombreMes(
            int mes) =>
            System.Globalization
                .CultureInfo
                .GetCultureInfo("es-ES")
                .DateTimeFormat
                .GetMonthName(
                    mes);
    }


    /// <summary>
    /// Un estudiante apuntado a una escuela, con su asiento y su día de
    /// limpieza.
    /// </summary>
    public class EscuelaAlumno
    {
        public string Id { get; set; } = "";
        public string EscuelaId { get; set; } = "";
        public int PersonaId { get; set; }
        public string Nombre { get; set; } = "";
        public int Asiento { get; set; }
        public string LimpiezaDia { get; set; } = "";

        public string AsientoTexto =>
            Asiento > 0
                ? "Asiento " + Asiento
                : "Sin asiento";

        public string LimpiezaTexto =>
            string.IsNullOrWhiteSpace(
                LimpiezaDia)
                ? "Sin limpieza"
                : LimpiezaDia;

        public string Detalle =>
            AsientoTexto + " · " + LimpiezaTexto;
    }
}
