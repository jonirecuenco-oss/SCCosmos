using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Data;

// =========================================================
// RSC · PERSISTENCIA (MÓVIL)
//
// Las mismas cuatro tablas que en Windows (Id GUID de texto):
// RSC_Reuniones, RSC_Asistentes, RSC_Grupos, RSC_Miembros.
// Viajan con el motor de cambios igual que las escuelas.
//
// Cada asistente y cada miembro guarda también el nombre, y
// el SC y su esposa van con PersonaId negativo (no tienen
// ficha en Personas). El territorio de una persona se lee de
// TerritoriosVisitaV3.
// =========================================================

public static partial class DatabaseHelper
{
    // =====================================================
    // TABLAS
    // =====================================================

    private const string SqlTablaRscReuniones = @"
        CREATE TABLE IF NOT EXISTS RSC_Reuniones (
            Id TEXT PRIMARY KEY,
            CongregacionCodigo TEXT DEFAULT '',
            Fecha TEXT DEFAULT '',
            Turno TEXT DEFAULT '',
            Notas TEXT DEFAULT '',
            FechaCreacion TEXT DEFAULT ''
        );";

    private const string SqlTablaRscAsistentes = @"
        CREATE TABLE IF NOT EXISTS RSC_Asistentes (
            Id TEXT PRIMARY KEY,
            RscReunionId TEXT DEFAULT '',
            PersonaId INTEGER DEFAULT 0,
            Nombre TEXT DEFAULT '',
            Asiste INTEGER DEFAULT 0,
            SaleAPredicar INTEGER DEFAULT 0,
            AceptaGrupo INTEGER DEFAULT 1,
            ArregloConPersonaId INTEGER DEFAULT 0,
            ArregloConPersonaIds TEXT DEFAULT '',
            Sexo TEXT DEFAULT '',
            Notas TEXT DEFAULT ''
        );";

    private const string SqlTablaRscGrupos = @"
        CREATE TABLE IF NOT EXISTS RSC_Grupos (
            Id TEXT PRIMARY KEY,
            RscReunionId TEXT DEFAULT '',
            Numero INTEGER DEFAULT 0,
            Notas TEXT DEFAULT '',
            Territorio TEXT DEFAULT '',
            Orden INTEGER DEFAULT 0
        );";

    private const string SqlTablaRscMiembros = @"
        CREATE TABLE IF NOT EXISTS RSC_Miembros (
            Id TEXT PRIMARY KEY,
            RscGrupoId TEXT DEFAULT '',
            RscReunionId TEXT DEFAULT '',
            PersonaId INTEGER DEFAULT 0,
            Nombre TEXT DEFAULT '',
            Orden INTEGER DEFAULT 0
        );";


    /// <summary>
    /// Crea las tablas si faltan. Se llama al arrancar la app
    /// (antes de crear los disparadores del motor) y antes de
    /// cada lectura o escritura.
    /// </summary>
    public static void AsegurarTablasRscMobile()
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasRscEnConexion(
            connection);
    }


    private static void AsegurarTablasRscEnConexion(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            SqlTablaRscReuniones
            + SqlTablaRscAsistentes
            + SqlTablaRscGrupos
            + SqlTablaRscMiembros;

        cmd.ExecuteNonQuery();

        // Columnas nuevas en bases ya creadas.
        AgregarColumnaSiFalta(
            connection,
            "RSC_Asistentes",
            "Nombre",
            "TEXT DEFAULT ''");

        AgregarColumnaSiFalta(
            connection,
            "RSC_Asistentes",
            "Sexo",
            "TEXT DEFAULT ''");

        AgregarColumnaSiFalta(
            connection,
            "RSC_Asistentes",
            "ArregloConPersonaIds",
            "TEXT DEFAULT ''");

        AgregarColumnaSiFalta(
            connection,
            "RSC_Miembros",
            "Nombre",
            "TEXT DEFAULT ''");

        AgregarColumnaSiFalta(
            connection,
            "RSC_Grupos",
            "Territorio",
            "TEXT DEFAULT ''");
    }


    // =====================================================
    // REUNIONES
    // =====================================================

    private const string SqlRscReunionSelect = @"
        SELECT r.Id, r.CongregacionCodigo,
               COALESCE((SELECT c.Nombre FROM Congregaciones c
                          WHERE c.Codigo = r.CongregacionCodigo), ''),
               r.Fecha, r.Turno, r.Notas, r.FechaCreacion,
               (SELECT COUNT(*) FROM RSC_Grupos g
                 WHERE g.RscReunionId = r.Id) AS Grupos,
               (SELECT COUNT(*) FROM RSC_Asistentes a
                 WHERE a.RscReunionId = r.Id
                   AND a.Asiste = 1
                   AND a.SaleAPredicar = 1) AS Personas
        FROM RSC_Reuniones r";


    private static RscReunionMobile LeerReunionRsc(
        SqliteDataReader lector)
    {
        return new RscReunionMobile
        {
            Id = lector.IsDBNull(0)
                ? ""
                : lector.GetString(0),
            CongregacionCodigo = lector.IsDBNull(1)
                ? ""
                : lector.GetString(1),
            CongregacionNombre = lector.IsDBNull(2)
                ? ""
                : lector.GetString(2),
            Fecha = lector.IsDBNull(3)
                ? ""
                : lector.GetString(3),
            Turno = lector.IsDBNull(4)
                ? ""
                : lector.GetString(4),
            Notas = lector.IsDBNull(5)
                ? ""
                : lector.GetString(5),
            FechaCreacion = lector.IsDBNull(6)
                ? ""
                : lector.GetString(6),
            NumeroGrupos = lector.IsDBNull(7)
                ? 0
                : lector.GetInt32(7),
            NumeroPersonas = lector.IsDBNull(8)
                ? 0
                : lector.GetInt32(8)
        };
    }


    public static List<RscReunionMobile> ObtenerTodasReunionesRscMobile(
        int limite = 300)
    {
        var lista =
            new List<RscReunionMobile>();

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                SqlRscReunionSelect
                + " ORDER BY r.Fecha DESC, r.Turno ASC LIMIT $limite;";

            cmd.Parameters.AddWithValue(
                "$limite",
                limite);

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                lista.Add(
                    LeerReunionRsc(
                        lector));
            }
        }
        catch
        {
        }

        return lista;
    }


    public static List<RscReunionMobile> ObtenerReunionesRscPorFechaMobile(
        string fecha)
    {
        var lista =
            new List<RscReunionMobile>();

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                SqlRscReunionSelect
                + " WHERE r.Fecha = $fecha ORDER BY r.Turno ASC;";

            cmd.Parameters.AddWithValue(
                "$fecha",
                fecha ?? "");

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                lista.Add(
                    LeerReunionRsc(
                        lector));
            }
        }
        catch
        {
        }

        return lista;
    }


    public static RscReunionMobile? ObtenerReunionRscMobile(
        string congreCodigo,
        string fecha,
        string turno)
    {
        RscReunionMobile? reunion =
            null;

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                SqlRscReunionSelect
                + @" WHERE r.CongregacionCodigo = $codigo
                      AND r.Fecha = $fecha
                      AND r.Turno = $turno
                    ORDER BY r.Id DESC
                    LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$codigo",
                congreCodigo ?? "");

            cmd.Parameters.AddWithValue(
                "$fecha",
                fecha ?? "");

            cmd.Parameters.AddWithValue(
                "$turno",
                turno ?? "");

            using var lector =
                cmd.ExecuteReader();

            if (lector.Read())
            {
                reunion =
                    LeerReunionRsc(
                        lector);
            }
        }
        catch
        {
        }

        return reunion;
    }


    public static RscReunionMobile? ObtenerReunionRscPorIdMobile(
        string reunionId)
    {
        RscReunionMobile? reunion =
            null;

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                SqlRscReunionSelect
                + " WHERE r.Id = $id LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$id",
                reunionId ?? "");

            using var lector =
                cmd.ExecuteReader();

            if (lector.Read())
            {
                reunion =
                    LeerReunionRsc(
                        lector);
            }
        }
        catch
        {
        }

        return reunion;
    }


    /// <summary>Crea una reunión nueva y devuelve su Id GUID.</summary>
    public static string CrearReunionRscMobile(
        string congreCodigo,
        string fecha,
        string turno)
    {
        try
        {
            string id =
                Guid.NewGuid()
                    .ToString("N");

            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText = @"
                INSERT INTO RSC_Reuniones
                    (Id, CongregacionCodigo, Fecha, Turno,
                     Notas, FechaCreacion)
                VALUES
                    ($id, $codigo, $fecha, $turno,
                     '', $creacion);";

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.Parameters.AddWithValue(
                "$codigo",
                congreCodigo ?? "");

            cmd.Parameters.AddWithValue(
                "$fecha",
                fecha ?? "");

            cmd.Parameters.AddWithValue(
                "$turno",
                turno ?? "");

            cmd.Parameters.AddWithValue(
                "$creacion",
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

            cmd.ExecuteNonQuery();

            // El superintendente y su esposa van marcados por defecto en
            // toda RSC nueva.
            var porDefecto =
                new List<RscPersonaMobile>();

            string nombreSc = (
                ObtenerConfiguracion("NombreSC", "")
                + " "
                + ObtenerConfiguracion("ApellidoSC", ""))
                .Trim();

            string nombreEsposa = (
                ObtenerConfiguracion("NombreEsposaSC", "")
                + " "
                + ObtenerConfiguracion("ApellidoEsposaSC", ""))
                .Trim();

            if (!string.IsNullOrWhiteSpace(nombreSc))
            {
                porDefecto.Add(
                    new RscPersonaMobile
                    {
                        PersonaId = RscVirtualesMobile.Superintendente,
                        Nombre = nombreSc,
                        Sexo = "Hombre"
                    });
            }

            if (!string.IsNullOrWhiteSpace(nombreEsposa))
            {
                porDefecto.Add(
                    new RscPersonaMobile
                    {
                        PersonaId = RscVirtualesMobile.Esposa,
                        Nombre = nombreEsposa,
                        Sexo = "Mujer"
                    });
            }

            if (porDefecto.Count > 0)
            {
                AnadirAsistentesRscMobile(
                    id,
                    porDefecto);
            }

            return id;
        }
        catch
        {
            return "";
        }
    }


    public static void EliminarReunionRscMobile(
        string reunionId)
    {
        if (string.IsNullOrWhiteSpace(
                reunionId))
        {
            return;
        }

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            foreach (string sql in new[]
                     {
                         "DELETE FROM RSC_Miembros WHERE RscReunionId = $id;",
                         "DELETE FROM RSC_Grupos WHERE RscReunionId = $id;",
                         "DELETE FROM RSC_Asistentes WHERE RscReunionId = $id;",
                         "DELETE FROM RSC_Reuniones WHERE Id = $id;"
                     })
            {
                using var cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    sql;

                cmd.Parameters.AddWithValue(
                    "$id",
                    reunionId);

                cmd.ExecuteNonQuery();
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }


    // =====================================================
    // ASISTENTES
    // =====================================================

    public static List<RscPersonaMobile> ObtenerAsistentesRscMobile(
        string reunionId)
    {
        var lista =
            new List<RscPersonaMobile>();

        if (string.IsNullOrWhiteSpace(
                reunionId))
        {
            return lista;
        }

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText = @"
                SELECT a.PersonaId,
                       COALESCE(NULLIF(p.NombreComun, ''),
                                NULLIF(p.Nombre, ''),
                                NULLIF(a.Nombre, ''),
                                ''),
                       COALESCE(p.Grupo, 0),
                       a.Asiste,
                       a.SaleAPredicar,
                       a.AceptaGrupo,
                       COALESCE(a.ArregloConPersonaIds, ''),
                       COALESCE(NULLIF(p.Sexo, ''), a.Sexo, ''),
                       a.Notas
                FROM RSC_Asistentes a
                LEFT JOIN Personas p ON p.Id = a.PersonaId
                WHERE a.RscReunionId = $id
                ORDER BY 2 COLLATE NOCASE ASC;";

            cmd.Parameters.AddWithValue(
                "$id",
                reunionId);

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                lista.Add(
                    new RscPersonaMobile
                    {
                        PersonaId = lector.GetInt32(0),
                        Nombre = lector.IsDBNull(1)
                            ? ""
                            : lector.GetString(1),
                        GrupoPredicacion = lector.IsDBNull(2)
                            ? 0
                            : lector.GetInt32(2),
                        Asiste = !lector.IsDBNull(3)
                                 && lector.GetInt32(3) == 1,
                        SaleAPredicar = !lector.IsDBNull(4)
                                        && lector.GetInt32(4) == 1,
                        AceptaGrupo = lector.IsDBNull(5)
                                      || lector.GetInt32(5) == 1,
                        ArregloConPersonaIds = ParsearIdsRscMobile(
                            lector.IsDBNull(6)
                                ? ""
                                : lector.GetString(6)),
                        Sexo = lector.IsDBNull(7)
                            ? ""
                            : lector.GetString(7),
                        Notas = lector.IsDBNull(8)
                            ? ""
                            : lector.GetString(8)
                    });
            }
        }
        catch
        {
        }

        return lista;
    }


    /// <summary>
    /// Añade asistentes (los que falten) y devuelve cuántos entraron.
    /// </summary>
    public static int AnadirAsistentesRscMobile(
        string reunionId,
        IEnumerable<RscPersonaMobile> nuevas)
    {
        int anadidas = 0;

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            foreach (var persona in
                     nuevas ?? Enumerable.Empty<RscPersonaMobile>())
            {
                if (persona == null)
                {
                    continue;
                }

                using (var existe =
                       connection.CreateCommand())
                {
                    existe.Transaction =
                        transaccion;

                    existe.CommandText =
                        "SELECT COUNT(*) FROM RSC_Asistentes WHERE RscReunionId = $r AND PersonaId = $p;";

                    existe.Parameters.AddWithValue(
                        "$r",
                        reunionId);

                    existe.Parameters.AddWithValue(
                        "$p",
                        persona.PersonaId);

                    object? contador =
                        existe.ExecuteScalar();

                    if (contador != null
                        && Convert.ToInt32(
                            contador,
                            CultureInfo.InvariantCulture) > 0)
                    {
                        continue;
                    }
                }

                using var cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaccion;

                cmd.CommandText = @"
                    INSERT INTO RSC_Asistentes
                        (Id, RscReunionId, PersonaId, Nombre,
                         Asiste, SaleAPredicar, AceptaGrupo,
                         ArregloConPersonaIds, Sexo, Notas)
                    VALUES
                        ($id, $reunion, $persona, $nombre,
                         1, 1, 1, '', $sexo, '');";

                cmd.Parameters.AddWithValue(
                    "$id",
                    Guid.NewGuid()
                        .ToString("N"));

                cmd.Parameters.AddWithValue(
                    "$reunion",
                    reunionId);

                cmd.Parameters.AddWithValue(
                    "$persona",
                    persona.PersonaId);

                cmd.Parameters.AddWithValue(
                    "$nombre",
                    persona.Nombre ?? "");

                cmd.Parameters.AddWithValue(
                    "$sexo",
                    persona.Sexo ?? "");

                cmd.ExecuteNonQuery();

                anadidas++;
            }

            transaccion.Commit();
        }
        catch
        {
        }

        return anadidas;
    }


    public static void QuitarAsistenteRscMobile(
        string reunionId,
        int personaId)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    "DELETE FROM RSC_Asistentes WHERE RscReunionId = $r AND PersonaId = $p;";

                cmd.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                cmd.Parameters.AddWithValue(
                    "$p",
                    personaId);

                cmd.ExecuteNonQuery();
            }

            // Si alguien la tenía como pareja, se queda sin arreglo.
            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    "UPDATE RSC_Asistentes SET ArregloConPersonaId = 0, AceptaGrupo = 1 WHERE RscReunionId = $r AND ArregloConPersonaId = $p;";

                cmd.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                cmd.Parameters.AddWithValue(
                    "$p",
                    personaId);

                cmd.ExecuteNonQuery();
            }

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    "DELETE FROM RSC_Miembros WHERE RscReunionId = $r AND PersonaId = $p;";

                cmd.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                cmd.Parameters.AddWithValue(
                    "$p",
                    personaId);

                cmd.ExecuteNonQuery();
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }


    public static void EstablecerPredicaRscMobile(
        string reunionId,
        int personaId,
        bool predica)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "UPDATE RSC_Asistentes SET SaleAPredicar = $valor WHERE RscReunionId = $r AND PersonaId = $p;";

            cmd.Parameters.AddWithValue(
                "$valor",
                predica ? 1 : 0);

            cmd.Parameters.AddWithValue(
                "$r",
                reunionId);

            cmd.Parameters.AddWithValue(
                "$p",
                personaId);

            cmd.ExecuteNonQuery();

            if (!predica)
            {
                // Quien no sale no figura en ningún grupo.
                using var quitar =
                    connection.CreateCommand();

                quitar.CommandText =
                    "DELETE FROM RSC_Miembros WHERE RscReunionId = $r AND PersonaId = $p;";

                quitar.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                quitar.Parameters.AddWithValue(
                    "$p",
                    personaId);

                quitar.ExecuteNonQuery();
            }
        }
        catch
        {
        }
    }


    /// <summary>
    /// Deja el arreglo de una persona: ella y quienes elija (una o
    /// varias) quedan como un conjunto que predica junto (y acepta o
    /// no ir en un grupo más grande). Los vínculos que otras personas
    /// tenían con el conjunto se limpian.
    /// </summary>
    public static void EstablecerArregloRscMobile(
        string reunionId,
        int personaId,
        List<int> conIds,
        bool acepta)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            // Arreglos actuales de todos los asistentes.
            var actuales =
                new Dictionary<int, List<int>>();

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT PersonaId, COALESCE(ArregloConPersonaIds, '') FROM RSC_Asistentes WHERE RscReunionId = $r;";

                cmd.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    actuales[lector.GetInt32(0)] =
                        ParsearIdsRscMobile(
                            lector.IsDBNull(1)
                                ? ""
                                : lector.GetString(1));
                }
            }

            var conjunto = new List<int>
            {
                personaId
            };

            foreach (int id in conIds ?? new List<int>())
            {
                if (id != personaId
                    && !conjunto.Contains(id))
                {
                    conjunto.Add(id);
                }
            }

            using var transaccion =
                connection.BeginTransaction();

            foreach (var par in actuales)
            {
                bool esMiembro =
                    conjunto.Contains(par.Key);

                List<int> nuevos = esMiembro
                    ? conjunto
                        .Where(x => x != par.Key)
                        .ToList()
                    : par.Value
                        .Where(x => !conjunto.Contains(x))
                        .ToList();

                bool iguales =
                    nuevos.Count == par.Value.Count
                    && !nuevos.Except(par.Value).Any();

                if (iguales && !esMiembro)
                {
                    continue;
                }

                using var cmd =
                    connection.CreateCommand();

                cmd.Transaction =
                    transaccion;

                cmd.CommandText = esMiembro
                    ? "UPDATE RSC_Asistentes SET ArregloConPersonaIds = $ids, AceptaGrupo = $acepta WHERE RscReunionId = $r AND PersonaId = $p;"
                    : "UPDATE RSC_Asistentes SET ArregloConPersonaIds = $ids WHERE RscReunionId = $r AND PersonaId = $p;";

                cmd.Parameters.AddWithValue(
                    "$ids",
                    SerializarIdsRscMobile(nuevos));

                cmd.Parameters.AddWithValue(
                    "$acepta",
                    acepta ? 1 : 0);

                cmd.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                cmd.Parameters.AddWithValue(
                    "$p",
                    par.Key);

                cmd.ExecuteNonQuery();
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }


    // =====================================================
    // GRUPOS
    // =====================================================

    public static List<RscGrupoMobile> ObtenerGruposRscMobile(
        string reunionId)
    {
        var grupos =
            new List<RscGrupoMobile>();

        if (string.IsNullOrWhiteSpace(
                reunionId))
        {
            return grupos;
        }

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using (var cmd =
                   connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, Numero, Notas, COALESCE(Territorio, '')
                    FROM RSC_Grupos
                    WHERE RscReunionId = $id
                    ORDER BY Orden ASC, Numero ASC;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    reunionId);

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    grupos.Add(
                        new RscGrupoMobile
                        {
                            Id = lector.IsDBNull(0)
                                ? ""
                                : lector.GetString(0),
                            RscReunionId =
                                reunionId,
                            Numero = lector.IsDBNull(1)
                                ? 0
                                : lector.GetInt32(1),
                            Notas = lector.IsDBNull(2)
                                ? ""
                                : lector.GetString(2),
                            Territorio =
                                lector.IsDBNull(3)
                                    ? ""
                                    : lector.GetString(3)
                        });
                }
            }

            using (var miembros =
                   connection.CreateCommand())
            {
                miembros.CommandText = @"
                    SELECT m.RscGrupoId, m.PersonaId,
                           COALESCE(NULLIF(p.NombreComun, ''),
                                    NULLIF(p.Nombre, ''),
                                    NULLIF(m.Nombre, ''),
                                    ''),
                           COALESCE(p.Grupo, 0)
                    FROM RSC_Miembros m
                    LEFT JOIN Personas p ON p.Id = m.PersonaId
                    WHERE m.RscReunionId = $id
                    ORDER BY m.Orden ASC, m.Id ASC;";

                miembros.Parameters.AddWithValue(
                    "$id",
                    reunionId);

                using var lector =
                    miembros.ExecuteReader();

                while (lector.Read())
                {
                    string grupoId =
                        lector.IsDBNull(0)
                            ? ""
                            : lector.GetString(0);

                    var grupo =
                        grupos.FirstOrDefault(
                            g => g.Id == grupoId);

                    if (grupo == null)
                    {
                        continue;
                    }

                    grupo.Miembros.Add(
                        new RscPersonaMobile
                        {
                            PersonaId =
                                lector.GetInt32(1),
                            Nombre = lector.IsDBNull(2)
                                ? ""
                                : lector.GetString(2),
                            GrupoPredicacion =
                                lector.IsDBNull(3)
                                    ? 0
                                    : lector.GetInt32(3),
                            Asiste = true,
                            SaleAPredicar = true
                        });
                }
            }
        }
        catch
        {
        }

        RscMotor.Renumerar(
            grupos);

        return grupos;
    }


    /// <summary>
    /// Forma los grupos automáticamente (conservando los territorios
    /// escritos, por número de grupo) y devuelve los avisos.
    /// </summary>
    public static List<string> FormarGruposRscMobile(
        string reunionId)
    {
        var avisos =
            new List<string>();

        try
        {
            string congregacion = "";

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasRscEnConexion(
                    connection);

                using var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    "SELECT CongregacionCodigo FROM RSC_Reuniones WHERE Id = $id LIMIT 1;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    reunionId);

                object? valor =
                    cmd.ExecuteScalar();

                congregacion =
                    valor?.ToString()
                    ?? "";
            }

            var asistentes =
                ObtenerAsistentesRscMobile(
                    reunionId);

            var territorios =
                ObtenerTerritoriosPorPersonaMobile(
                    congregacion);

            foreach (var asistente in asistentes)
            {
                if (asistente.PersonaId > 0
                    && territorios.TryGetValue(
                        asistente.PersonaId,
                        out string? territorio))
                {
                    asistente.Territorio =
                        territorio ?? "";
                }
            }

            // Territorios ya escritos, por número.
            var territoriosPrevios =
                ObtenerGruposRscMobile(
                        reunionId)
                    .Where(g => g.TieneTerritorio)
                    .GroupBy(g => g.Numero)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo
                            .First()
                            .Territorio);

            var grupos =
                RscMotor.FormarGrupos(
                    asistentes,
                    out avisos,
                    null);

            foreach (var grupo in grupos)
            {
                grupo.RscReunionId =
                    reunionId;

                if (territoriosPrevios.TryGetValue(
                        grupo.Numero,
                        out string? previo))
                {
                    grupo.Territorio =
                        previo ?? "";
                }
            }

            GuardarGruposRscEnConexion(
                reunionId,
                grupos);
        }
        catch
        {
        }

        return avisos;
    }


    private static void GuardarGruposRscEnConexion(
        string reunionId,
        IEnumerable<RscGrupoMobile> grupos)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarTablasRscEnConexion(
            connection);

        using var transaccion =
            connection.BeginTransaction();

        foreach (string sql in new[]
                 {
                     "DELETE FROM RSC_Miembros WHERE RscReunionId = $id;",
                     "DELETE FROM RSC_Grupos WHERE RscReunionId = $id;"
                 })
        {
            using var borrar =
                connection.CreateCommand();

            borrar.Transaction =
                transaccion;

            borrar.CommandText =
                sql;

            borrar.Parameters.AddWithValue(
                "$id",
                reunionId);

            borrar.ExecuteNonQuery();
        }

        int ordenGrupo = 0;

        foreach (var grupo in
                 grupos ?? Enumerable.Empty<RscGrupoMobile>())
        {
            if (grupo == null
                || grupo.Miembros.Count == 0)
            {
                continue;
            }

            string grupoId =
                Guid.NewGuid()
                    .ToString("N");

            using (var insertar =
                   connection.CreateCommand())
            {
                insertar.Transaction =
                    transaccion;

                insertar.CommandText = @"
                    INSERT INTO RSC_Grupos
                        (Id, RscReunionId, Numero, Notas,
                         Territorio, Orden)
                    VALUES
                        ($id, $reunion, $numero, $notas,
                         $territorio, $orden);";

                insertar.Parameters.AddWithValue(
                    "$id",
                    grupoId);

                insertar.Parameters.AddWithValue(
                    "$reunion",
                    reunionId);

                insertar.Parameters.AddWithValue(
                    "$numero",
                    grupo.Numero);

                insertar.Parameters.AddWithValue(
                    "$notas",
                    grupo.Notas ?? "");

                insertar.Parameters.AddWithValue(
                    "$territorio",
                    grupo.Territorio ?? "");

                insertar.Parameters.AddWithValue(
                    "$orden",
                    ordenGrupo);

                insertar.ExecuteNonQuery();
            }

            ordenGrupo++;

            int ordenMiembro = 0;

            foreach (var miembro in grupo.Miembros)
            {
                if (miembro == null)
                {
                    continue;
                }

                using var insertar =
                    connection.CreateCommand();

                insertar.Transaction =
                    transaccion;

                insertar.CommandText = @"
                    INSERT INTO RSC_Miembros
                        (Id, RscGrupoId, RscReunionId, PersonaId,
                         Nombre, Orden)
                    VALUES
                        ($id, $grupo, $reunion, $persona,
                         $nombre, $orden);";

                insertar.Parameters.AddWithValue(
                    "$id",
                    Guid.NewGuid()
                        .ToString("N"));

                insertar.Parameters.AddWithValue(
                    "$grupo",
                    grupoId);

                insertar.Parameters.AddWithValue(
                    "$reunion",
                    reunionId);

                insertar.Parameters.AddWithValue(
                    "$persona",
                    miembro.PersonaId);

                insertar.Parameters.AddWithValue(
                    "$nombre",
                    miembro.Nombre ?? "");

                insertar.Parameters.AddWithValue(
                    "$orden",
                    ordenMiembro);

                insertar.ExecuteNonQuery();

                ordenMiembro++;
            }
        }

        transaccion.Commit();
    }


    /// <summary>Mueve a una persona a un grupo (0 = sin grupo).</summary>
    public static void MoverPersonaRscMobile(
        string reunionId,
        int personaId,
        int numeroDestino)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            using (var quitar =
                   connection.CreateCommand())
            {
                quitar.Transaction =
                    transaccion;

                quitar.CommandText =
                    "DELETE FROM RSC_Miembros WHERE RscReunionId = $r AND PersonaId = $p;";

                quitar.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                quitar.Parameters.AddWithValue(
                    "$p",
                    personaId);

                quitar.ExecuteNonQuery();
            }

            if (numeroDestino > 0)
            {
                string grupoId = "";
                string nombre = "";

                using (var buscar =
                       connection.CreateCommand())
                {
                    buscar.Transaction =
                        transaccion;

                    buscar.CommandText =
                        "SELECT Id FROM RSC_Grupos WHERE RscReunionId = $r AND Numero = $n LIMIT 1;";

                    buscar.Parameters.AddWithValue(
                        "$r",
                        reunionId);

                    buscar.Parameters.AddWithValue(
                        "$n",
                        numeroDestino);

                    object? valor =
                        buscar.ExecuteScalar();

                    grupoId =
                        valor?.ToString()
                        ?? "";
                }

                if (grupoId.Length == 0)
                {
                    transaccion.Rollback();

                    return;
                }

                using (var buscar =
                       connection.CreateCommand())
                {
                    buscar.Transaction =
                        transaccion;

                    buscar.CommandText = @"
                        SELECT COALESCE(NULLIF(p.NombreComun, ''),
                                        NULLIF(p.Nombre, ''),
                                        NULLIF(a.Nombre, ''),
                                        '')
                        FROM RSC_Asistentes a
                        LEFT JOIN Personas p ON p.Id = a.PersonaId
                        WHERE a.RscReunionId = $r AND a.PersonaId = $p
                        LIMIT 1;";

                    buscar.Parameters.AddWithValue(
                        "$r",
                        reunionId);

                    buscar.Parameters.AddWithValue(
                        "$p",
                        personaId);

                    object? valor =
                        buscar.ExecuteScalar();

                    nombre =
                        valor?.ToString()
                        ?? "";
                }

                using (var insertar =
                       connection.CreateCommand())
                {
                    insertar.Transaction =
                        transaccion;

                    insertar.CommandText = @"
                        INSERT INTO RSC_Miembros
                            (Id, RscGrupoId, RscReunionId, PersonaId,
                             Nombre, Orden)
                        VALUES
                            ($id, $grupo, $reunion, $persona,
                             $nombre,
                             (SELECT COALESCE(MAX(Orden), -1) + 1
                              FROM RSC_Miembros
                              WHERE RscGrupoId = $grupo));";

                    insertar.Parameters.AddWithValue(
                        "$id",
                        Guid.NewGuid()
                            .ToString("N"));

                    insertar.Parameters.AddWithValue(
                        "$grupo",
                        grupoId);

                    insertar.Parameters.AddWithValue(
                        "$reunion",
                        reunionId);

                    insertar.Parameters.AddWithValue(
                        "$persona",
                        personaId);

                    insertar.Parameters.AddWithValue(
                        "$nombre",
                        nombre);

                    insertar.ExecuteNonQuery();
                }
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }


    /// <summary>
    /// Quita un grupo (sus miembros quedan sin grupo) y renumera
    /// los que quedan.
    /// </summary>
    public static void EliminarGrupoRscMobile(
        string reunionId,
        string grupoId)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var transaccion =
                connection.BeginTransaction();

            using (var borrar =
                   connection.CreateCommand())
            {
                borrar.Transaction =
                    transaccion;

                borrar.CommandText =
                    "DELETE FROM RSC_Miembros WHERE RscGrupoId = $g;";

                borrar.Parameters.AddWithValue(
                    "$g",
                    grupoId);

                borrar.ExecuteNonQuery();
            }

            using (var borrar =
                   connection.CreateCommand())
            {
                borrar.Transaction =
                    transaccion;

                borrar.CommandText =
                    "DELETE FROM RSC_Grupos WHERE Id = $g;";

                borrar.Parameters.AddWithValue(
                    "$g",
                    grupoId);

                borrar.ExecuteNonQuery();
            }

            // Renumerar los grupos que quedan.
            var ids =
                new List<string>();

            using (var leer =
                   connection.CreateCommand())
            {
                leer.Transaction =
                    transaccion;

                leer.CommandText =
                    "SELECT Id FROM RSC_Grupos WHERE RscReunionId = $r ORDER BY Orden ASC, Numero ASC;";

                leer.Parameters.AddWithValue(
                    "$r",
                    reunionId);

                using var lector =
                    leer.ExecuteReader();

                while (lector.Read())
                {
                    ids.Add(
                        lector.GetString(0));
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                using var renumerar =
                    connection.CreateCommand();

                renumerar.Transaction =
                    transaccion;

                renumerar.CommandText =
                    "UPDATE RSC_Grupos SET Numero = $n, Orden = $o WHERE Id = $id;";

                renumerar.Parameters.AddWithValue(
                    "$n",
                    i + 1);

                renumerar.Parameters.AddWithValue(
                    "$o",
                    i);

                renumerar.Parameters.AddWithValue(
                    "$id",
                    ids[i]);

                renumerar.ExecuteNonQuery();
            }

            transaccion.Commit();
        }
        catch
        {
        }
    }


    public static void GuardarTerritorioGrupoRscMobile(
        string grupoId,
        string territorio)
    {
        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            AsegurarTablasRscEnConexion(
                connection);

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "UPDATE RSC_Grupos SET Territorio = $t WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$t",
                territorio ?? "");

            cmd.Parameters.AddWithValue(
                "$id",
                grupoId);

            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }


    /// <summary>
    /// Grupos guardados, resumidos en líneas para el buscador por día.
    /// </summary>
    public static List<string> ObtenerGruposResumenRscMobile(
        string reunionId)
    {
        var lineas =
            new List<string>();

        foreach (var grupo in
                 ObtenerGruposRscMobile(
                     reunionId))
        {
            string linea =
                grupo.Titulo;

            if (grupo.TieneTerritorio)
            {
                linea +=
                    " · Territorio: "
                    + grupo.Territorio;
            }

            lineas.Add(
                linea);

            if (grupo.Miembros.Count > 0)
            {
                lineas.Add(
                    "   "
                    + string.Join(
                        ", ",
                        grupo.Miembros
                            .Select(m => m.Nombre)
                            .Where(n => !string.IsNullOrWhiteSpace(n))));
            }

            if (!string.IsNullOrWhiteSpace(
                    grupo.Notas))
            {
                lineas.Add(
                    "   "
                    + grupo.Notas);
            }
        }

        return lineas;
    }


    // =====================================================
    // TERRITORIOS DE LAS PERSONAS (solo lectura)
    // =====================================================

    public static Dictionary<int, string> ObtenerTerritoriosPorPersonaMobile(
        string congreCodigo)
    {
        var resultado =
            new Dictionary<int, string>();

        try
        {
            var personas =
                ObtenerPersonasPorCongregacion(
                    congreCodigo);

            if (personas.Count == 0)
            {
                return resultado;
            }

            // Territorios de la congregación.
            var territorios =
                new List<(string Nombre, string AsignadoA, string Vinculado)>();

            using (var conexion =
                   new SqliteConnection(
                       ConnectionString))
            {
                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText = @"
                    SELECT COALESCE(Nombre, ''),
                           COALESCE(AsignadoA, ''),
                           COALESCE(NombreVinculado, '')
                    FROM TerritoriosVisitaV3
                    WHERE CongregacionCodigo = $codigo
                      AND COALESCE(FechaEliminacion, '') = '';";

                cmd.Parameters.AddWithValue(
                    "$codigo",
                    congreCodigo ?? "");

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    territorios.Add((
                        lector.IsDBNull(0) ? "" : lector.GetString(0),
                        lector.IsDBNull(1) ? "" : lector.GetString(1),
                        lector.IsDBNull(2) ? "" : lector.GetString(2)));
                }
            }

            // Mapeo S-13 → S-21 (si existe la tabla).
            var mapeo =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                using var conexion =
                    new SqliteConnection(
                        ConnectionString);

                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText = @"
                    SELECT NombreS13, NombreS21
                    FROM MapeoS13S21
                    WHERE CongregacionCodigo = $codigo;";

                cmd.Parameters.AddWithValue(
                    "$codigo",
                    congreCodigo ?? "");

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    string s13 = lector.IsDBNull(0)
                        ? ""
                        : lector.GetString(0);

                    string s21 = lector.IsDBNull(1)
                        ? ""
                        : lector.GetString(1);

                    if (!string.IsNullOrWhiteSpace(
                            s13))
                    {
                        mapeo[NormalizarRsc(
                            s13)] = s21;
                    }
                }
            }
            catch
            {
            }

            var porNombre =
                new Dictionary<string, Persona>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var persona in personas)
            {
                foreach (string? nombre in new[]
                         {
                             persona.Nombre,
                             persona.NombreComun,
                             persona.NombreJWHub
                         })
                {
                    string clave =
                        NormalizarRsc(
                            nombre ?? "");

                    if (clave.Length > 0
                        && !porNombre.ContainsKey(
                            clave))
                    {
                        porNombre[clave] =
                            persona;
                    }
                }
            }

            foreach (var territorio in territorios)
            {
                var candidatos =
                    new List<string>
                    {
                        territorio.Vinculado,
                        territorio.AsignadoA
                    };

                string asignado =
                    NormalizarRsc(
                        territorio.AsignadoA);

                if (asignado.Length > 0
                    && mapeo.TryGetValue(
                        asignado,
                        out string? vinculado)
                    && !string.IsNullOrWhiteSpace(
                        vinculado))
                {
                    candidatos.Insert(
                        0,
                        vinculado);
                }

                foreach (string candidato in candidatos)
                {
                    string clave =
                        NormalizarRsc(
                            candidato);

                    if (clave.Length == 0)
                    {
                        continue;
                    }

                    if (porNombre.TryGetValue(
                            clave,
                            out Persona? persona)
                        && persona != null)
                    {
                        string nombreTerritorio =
                            string.IsNullOrWhiteSpace(
                                territorio.Nombre)
                                ? ""
                                : territorio.Nombre.Trim();

                        if (nombreTerritorio.Length > 0
                            && !resultado.ContainsKey(
                                persona.Id))
                        {
                            resultado[persona.Id] =
                                nombreTerritorio;
                        }

                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return resultado;
    }


    // =====================================================
    // ARREGLOS DEL DÍA (desde el programa de la visita)
    // =====================================================

    public static List<RscArregloDetectadoMobile> ObtenerArreglosDelDiaMobile(
        string congreCodigo,
        DateTime fecha,
        string turnoRsc)
    {
        var lista =
            new List<RscArregloDetectadoMobile>();

        try
        {
            string nombreSc =
                NormalizarRsc(
                    ObtenerConfiguracion(
                        "NombreSC",
                        ""));

            string nombreEsposa =
                NormalizarRsc(
                    ObtenerConfiguracion(
                        "NombreEsposaSC",
                        ""));

            string dia =
                NombreDiaEspanol(
                    fecha.DayOfWeek);

            string mes =
                fecha.ToString(
                    "yyyy-MM");

            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            AsegurarTablasRscEnConexion(
                conexion);

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText = @"
                SELECT COALESCE(v.Responsable, ''),
                       COALESCE(v.CompaneroId, 0),
                       COALESCE(v.Turno, ''),
                       COALESCE(v.Hora, ''),
                       COALESCE(v.TipoActividad, ''),
                       COALESCE(NULLIF(p.NombreComun, ''),
                                NULLIF(p.Nombre, ''),
                                '')
                FROM ProgramaVisitaV2 v
                LEFT JOIN Personas p ON p.Id = v.CompaneroId
                WHERE v.CongreCodigo = $codigo
                  AND v.MesReferencia = $mes
                  AND v.DiaSemana = $dia
                  AND COALESCE(v.CompaneroId, 0) > 0
                  AND LOWER(COALESCE(v.TipoActividad, '')) LIKE '%predic%';";

            cmd.Parameters.AddWithValue(
                "$codigo",
                congreCodigo ?? "");

            cmd.Parameters.AddWithValue(
                "$mes",
                mes);

            cmd.Parameters.AddWithValue(
                "$dia",
                dia);

            // El turno se compara en C# (SQLite no conoce la ñ).
            string turnoNormalizado =
                NormalizarRsc(
                    turnoRsc);

            var vistos =
                new HashSet<string>();

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                string responsable =
                    lector.IsDBNull(0)
                        ? ""
                        : lector.GetString(0);

                int companero =
                    lector.IsDBNull(1)
                        ? 0
                        : lector.GetInt32(1);

                string turno =
                    lector.IsDBNull(2)
                        ? ""
                        : lector.GetString(2);

                string hora =
                    lector.IsDBNull(3)
                        ? ""
                        : lector.GetString(3);

                string tipo =
                    lector.IsDBNull(4)
                        ? ""
                        : lector.GetString(4);

                string nombreCompanero =
                    lector.IsDBNull(5)
                        ? ""
                        : lector.GetString(5);

                if (companero <= 0)
                {
                    continue;
                }

                string turnoFila =
                    NormalizarRsc(
                        turno);

                bool turnoOk =
                    turnoFila.Length == 0
                    || turnoFila == "general"
                    || turnoNormalizado.Length == 0
                    || turnoFila == turnoNormalizado;

                if (!turnoOk)
                {
                    continue;
                }

                string normalizado =
                    NormalizarRsc(
                        responsable);

                bool ambos =
                    normalizado.Contains(
                        "ambos")
                    || normalizado.Contains(
                        "juntos");

                bool esSc =
                    ambos
                    || normalizado == "sc"
                    || normalizado.Contains(
                        "circuito")
                    || normalizado.Contains(
                        "superintendente")
                    || PareceNombreRsc(
                        normalizado,
                        nombreSc);

                bool esEsposa =
                    ambos
                    || normalizado.Contains(
                        "esposa")
                    || PareceNombreRsc(
                        normalizado,
                        nombreEsposa);

                if (!esSc && !esEsposa)
                {
                    continue;
                }

                string clave =
                    (esSc ? "S" : "")
                    + (esEsposa ? "E" : "")
                    + "|"
                    + companero;

                if (vistos.Contains(
                        clave))
                {
                    continue;
                }

                vistos.Add(
                    clave);

                if (esSc)
                {
                    lista.Add(
                        new RscArregloDetectadoMobile
                        {
                            ParaSuperintendente = true,
                            Responsable = responsable,
                            CompaneroId = companero,
                            CompaneroNombre = nombreCompanero,
                            Turno = turno,
                            Hora = hora,
                            Tipo = tipo
                        });
                }

                if (esEsposa)
                {
                    lista.Add(
                        new RscArregloDetectadoMobile
                        {
                            ParaEsposa = true,
                            Responsable = responsable,
                            CompaneroId = companero,
                            CompaneroNombre = nombreCompanero,
                            Turno = turno,
                            Hora = hora,
                            Tipo = tipo
                        });
                }
            }
        }
        catch
        {
        }

        return lista;
    }


    // =====================================================
    // AYUDAS
    // =====================================================

    /// <summary>Ids separados por comas: "536,511".</summary>
    private static List<int> ParsearIdsRscMobile(
        string texto)
    {
        var ids =
            new List<int>();

        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return ids;
        }

        foreach (string parte in texto.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(
                    parte.Trim(),
                    out int id)
                && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }


    private static string SerializarIdsRscMobile(
        IEnumerable<int> ids)
    {
        if (ids == null)
        {
            return "";
        }

        return string.Join(
            ",",
            ids.Distinct());
    }


    private static bool PareceNombreRsc(
        string corto,
        string completo)
    {
        if (string.IsNullOrWhiteSpace(
                corto)
            || string.IsNullOrWhiteSpace(
                completo)
            || corto.Length < 3
            || completo.Length < 3)
        {
            return false;
        }

        string primerNombre =
            completo.Split(' ')[0];

        if (corto == primerNombre)
        {
            return true;
        }

        int comun = 0;

        while (comun < corto.Length
               && comun < primerNombre.Length
               && corto[comun] == primerNombre[comun])
        {
            comun++;
        }

        return comun >= 3;
    }


    private static string NormalizarRsc(
        string texto)
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return "";
        }

        string descompuesto = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(
                NormalizationForm.FormD);

        var constructor =
            new StringBuilder();

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo
                    .GetUnicodeCategory(
                        caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                constructor.Append(
                    caracter);
            }
        }

        return constructor
            .ToString()
            .Normalize(
                NormalizationForm.FormC)
            .Replace("  ", " ")
            .Trim();
    }


    private static string NombreDiaEspanol(
        DayOfWeek dia)
    {
        switch (dia)
        {
            case DayOfWeek.Monday: return "Lunes";
            case DayOfWeek.Tuesday: return "Martes";
            case DayOfWeek.Wednesday: return "Miércoles";
            case DayOfWeek.Thursday: return "Jueves";
            case DayOfWeek.Friday: return "Viernes";
            case DayOfWeek.Saturday: return "Sábado";
            default: return "Domingo";
        }
    }
}
