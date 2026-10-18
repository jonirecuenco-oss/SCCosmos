using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// ASAMBLEAS Y REUNIÓN DE PRECURSORES (Android, solo lectura)
// ============================================================================
//
// Consulta las tablas compartidas ProgramaAsambleas / ...Partes /
// ...Organigrama y ReunionPrecursores / ...Sesiones / ...Partes.
// No se escribe nada: la edición completa permanece en WPF.

public static partial class DatabaseHelper
{
    public static List<AsambleaMobile> ObtenerAsambleasMobile()
    {
        var resultado =
            new List<AsambleaMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ProgramaAsambleas"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Id, Anio, Tipo, Seccion, Titulo,
                   TextoTematico, Lugar, Fecha
            FROM ProgramaAsambleas
            ORDER BY Anio DESC, Seccion;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new AsambleaMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    Anio = LeerTexto(reader, 1),
                    Tipo = LeerTexto(reader, 2),
                    Seccion = LeerTexto(reader, 3),
                    Titulo = LeerTexto(reader, 4),
                    TextoTematico = LeerTexto(reader, 5),
                    Lugar = LeerTexto(reader, 6),
                    Fecha = LeerTexto(reader, 7)
                });
        }

        return resultado;
    }


    /// <summary>
    /// Asambleas con el número de partes / asignaciones, para saber cuáles
    /// están realmente programadas (igual que se ve en Windows).
    /// </summary>
    public static List<AsambleaMobile>
        ObtenerAsambleasConDatosMobile()
    {
        var resultado =
            new List<AsambleaMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ProgramaAsambleas"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                a.Id,
                a.Anio,
                a.Tipo,
                a.Seccion,
                a.Titulo,
                a.TextoTematico,
                a.Lugar,
                a.Fecha,
                (SELECT COUNT(*)
                   FROM ProgramaAsambleasPartes p
                  WHERE p.AsambleaId = a.Id) AS Partes,
                (SELECT COUNT(*)
                   FROM ProgramaAsambleasPartes p
                  WHERE p.AsambleaId = a.Id
                    AND p.AsignadoId > 0) AS ConAsignado,
                (SELECT COUNT(*)
                   FROM ProgramaAsambleasOrganigrama o
                  WHERE o.AsambleaId = a.Id) AS Organigrama
            FROM ProgramaAsambleas a
            ORDER BY a.Anio DESC, a.Tipo, a.Seccion;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new AsambleaMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    Anio = LeerTexto(reader, 1),
                    Tipo = LeerTexto(reader, 2),
                    Seccion = LeerTexto(reader, 3),
                    Titulo = LeerTexto(reader, 4),
                    TextoTematico = LeerTexto(reader, 5),
                    Lugar = LeerTexto(reader, 6),
                    Fecha = LeerTexto(reader, 7),
                    PartesCount = LeerIntAsamblea(reader, 8),
                    PartesConAsignado =
                        LeerIntAsamblea(reader, 9),
                    OrganigramaCount =
                        LeerIntAsamblea(reader, 10)
                });
        }

        return resultado;
    }


    /// <summary>
    /// Participaciones reales de cada hermano en los programas de asambleas
    /// y en las reuniones de precursores. Es la base de la matriz de
    /// oradores de Windows (AC-BR / AC-CO / Reunión precursores).
    /// </summary>
    public static List<ParticipacionOradorMobile>
        ObtenerParticipacionesOradoresMobile()
    {
        var resultado =
            new List<ParticipacionOradorMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        // ---- Asambleas de circuito (AC-BR y AC-CO) ----
        if (ExisteTabla(
                connection,
                "ProgramaAsambleasPartes")
            &&
            ExisteTabla(
                connection,
                "ProgramaAsambleas"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT
                    a.Anio,
                    a.Tipo,
                    pa.AsignadoId,
                    pa.Tipo,
                    pa.Titulo
                FROM ProgramaAsambleasPartes pa
                JOIN ProgramaAsambleas a ON a.Id = pa.AsambleaId
                WHERE pa.AsignadoId > 0
                ORDER BY pa.Id;
                """;

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                string tipoAsamblea =
                    LeerTexto(reader, 1);

                resultado.Add(
                    new ParticipacionOradorMobile
                    {
                        Anio = LeerTexto(reader, 0),
                        Grupo =
                            tipoAsamblea.Contains(
                                "Betel",
                                StringComparison.OrdinalIgnoreCase)
                                ? "AC-BR"
                                : "AC-CO",
                        AsignadoId =
                            LeerIntAsamblea(reader, 2),
                        TipoParte = LeerTexto(reader, 3),
                        Titulo = LeerTexto(reader, 4)
                    });
            }
        }

        // ---- Reunión de precursores (RP) ----
        if (ExisteTabla(
                connection,
                "ReunionPrecursoresPartes")
            &&
            ExisteTabla(
                connection,
                "ReunionPrecursoresSesiones")
            &&
            ExisteTabla(
                connection,
                "ReunionPrecursores"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT
                    r.Anio,
                    rp.AsignadoId,
                    rp.Tipo,
                    rp.Titulo
                FROM ReunionPrecursoresPartes rp
                JOIN ReunionPrecursoresSesiones ps
                  ON ps.Id = rp.SesionId
                JOIN ReunionPrecursores r
                  ON r.Id = ps.ReunionId
                WHERE rp.AsignadoId > 0
                ORDER BY rp.Id;
                """;

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                resultado.Add(
                    new ParticipacionOradorMobile
                    {
                        Anio = LeerTexto(reader, 0),
                        Grupo = "RP",
                        AsignadoId =
                            LeerIntAsamblea(reader, 1),
                        TipoParte = LeerTexto(reader, 2),
                        Titulo = LeerTexto(reader, 3)
                    });
            }
        }

        return resultado;
    }


    public static List<ParteAsambleaMobile>
        ObtenerPartesAsambleaMobile(
            int asambleaId)
    {
        var resultado =
            new List<ParteAsambleaMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ProgramaAsambleasPartes"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                pa.Id,
                pa.AsignadoId,
                pa.Turno,
                pa.Hora,
                pa.Titulo,
                pa.Participantes,
                pa.Tipo,
                COALESCE(p.NombreComun, p.Nombre, '')
            FROM ProgramaAsambleasPartes pa
            LEFT JOIN Personas p ON p.Id = pa.AsignadoId
            WHERE pa.AsambleaId = $id
            ORDER BY pa.Turno, pa.Hora;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            asambleaId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ParteAsambleaMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    AsignadoId = LeerIntAsamblea(reader, 1),
                    Turno = LeerTexto(reader, 2),
                    Hora = LeerTexto(reader, 3),
                    Titulo = LeerTexto(reader, 4),
                    Participantes = LeerTexto(reader, 5),
                    Tipo = LeerTexto(reader, 6),
                    Asignado = LeerTexto(reader, 7)
                });
        }

        return resultado;
    }


    public static List<OrganigramaAsambleaMobile>
        ObtenerOrganigramaAsambleaMobile(
            int asambleaId)
    {
        var resultado =
            new List<OrganigramaAsambleaMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ProgramaAsambleasOrganigrama"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                o.Id,
                o.AsignadoId,
                o.Departamento,
                o.Rol,
                COALESCE(p.NombreComun, p.Nombre, '')
            FROM ProgramaAsambleasOrganigrama o
            LEFT JOIN Personas p ON p.Id = o.AsignadoId
            WHERE o.AsambleaId = $id
            ORDER BY o.Departamento, o.Rol;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            asambleaId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new OrganigramaAsambleaMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    AsignadoId = LeerIntAsamblea(reader, 1),
                    Departamento = LeerTexto(reader, 2),
                    Rol = LeerTexto(reader, 3),
                    Asignado = LeerTexto(reader, 4)
                });
        }

        return resultado;
    }


    public static List<ReunionPrecursoresMobile>
        ObtenerReunionesPrecursoresMobile()
    {
        var resultado =
            new List<ReunionPrecursoresMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ReunionPrecursores"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Id, Anio, Titulo, TextoTematico
            FROM ReunionPrecursores
            ORDER BY Anio DESC;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ReunionPrecursoresMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    Anio = LeerTexto(reader, 1),
                    Titulo = LeerTexto(reader, 2),
                    TextoTematico = LeerTexto(reader, 3)
                });
        }

        return resultado;
    }


    public static List<SesionPrecursoresMobile>
        ObtenerSesionesPrecursoresMobile(
            int reunionId)
    {
        var resultado =
            new List<SesionPrecursoresMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ReunionPrecursoresSesiones"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Id, Numero, Fecha, Hora, Congregaciones, Lugar
            FROM ReunionPrecursoresSesiones
            WHERE ReunionId = $id
            ORDER BY Numero;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            reunionId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new SesionPrecursoresMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    ReunionId = reunionId,
                    Numero = LeerTexto(reader, 1),
                    Fecha = LeerTexto(reader, 2),
                    Hora = LeerTexto(reader, 3),
                    Congregaciones = LeerTexto(reader, 4),
                    Lugar = LeerTexto(reader, 5)
                });
        }

        return resultado;
    }


    public static int GuardarSesionPrecursoresMobile(
        int id,
        int reunionId,
        string numero,
        string fecha,
        string hora,
        string congregaciones,
        string lugar)
    {
        if (reunionId <= 0)
        {
            return 0;
        }

        int numeroInt = 0;
        int.TryParse(
            (numero ?? "").Trim(),
            out numeroInt);

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Parameters.AddWithValue(
            "$reunion",
            reunionId);

        cmd.Parameters.AddWithValue(
            "$numero",
            numeroInt);

        cmd.Parameters.AddWithValue(
            "$fecha",
            fecha ?? "");

        cmd.Parameters.AddWithValue(
            "$hora",
            hora ?? "");

        cmd.Parameters.AddWithValue(
            "$congregaciones",
            congregaciones ?? "");

        cmd.Parameters.AddWithValue(
            "$lugar",
            lugar ?? "");

        if (id > 0)
        {
            cmd.CommandText =
                """
                UPDATE ReunionPrecursoresSesiones
                SET Numero = $numero,
                    Fecha = $fecha,
                    Hora = $hora,
                    Congregaciones = $congregaciones,
                    Lugar = $lugar
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.ExecuteNonQuery();

            return id;
        }

        cmd.CommandText =
            """
            INSERT INTO ReunionPrecursoresSesiones
                (ReunionId, Numero, Fecha, Hora, Congregaciones, Lugar)
            VALUES
                ($reunion, $numero, $fecha, $hora, $congregaciones, $lugar);
            """;

        cmd.ExecuteNonQuery();

        using SqliteCommand idCmd =
            connection.CreateCommand();

        idCmd.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt32(
            idCmd.ExecuteScalar() ?? 0);
    }


    public static List<ParteSesionPrecursoresMobile>
        ObtenerPartesSesionPrecursoresMobile(
            int sesionId)
    {
        var resultado =
            new List<ParteSesionPrecursoresMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ReunionPrecursoresPartes"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                rp.Id,
                rp.AsignadoId,
                rp.Hora,
                rp.Titulo,
                rp.Participantes,
                rp.Tipo,
                COALESCE(p.NombreComun, p.Nombre, '')
            FROM ReunionPrecursoresPartes rp
            LEFT JOIN Personas p ON p.Id = rp.AsignadoId
            WHERE rp.SesionId = $id
            ORDER BY rp.Hora;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            sesionId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new ParteSesionPrecursoresMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    SesionId = sesionId,
                    AsignadoId = LeerIntAsamblea(reader, 1),
                    Hora = LeerTexto(reader, 2),
                    Titulo = LeerTexto(reader, 3),
                    Participantes = LeerTexto(reader, 4),
                    Tipo = LeerTexto(reader, 5),
                    Asignado = LeerTexto(reader, 6)
                });
        }

        return resultado;
    }


    public static int GuardarParteSesionPrecursoresMobile(
        int id,
        int sesionId,
        string hora,
        string titulo,
        int asignadoId,
        string participantes,
        string tipo)
    {
        if (sesionId <= 0)
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Parameters.AddWithValue(
            "$sesion",
            sesionId);

        cmd.Parameters.AddWithValue(
            "$hora",
            hora ?? "");

        cmd.Parameters.AddWithValue(
            "$titulo",
            titulo ?? "");

        cmd.Parameters.AddWithValue(
            "$asignado",
            asignadoId);

        cmd.Parameters.AddWithValue(
            "$participantes",
            participantes ?? "");

        cmd.Parameters.AddWithValue(
            "$tipo",
            tipo ?? "");

        if (id > 0)
        {
            cmd.CommandText =
                """
                UPDATE ReunionPrecursoresPartes
                SET Hora = $hora,
                    Titulo = $titulo,
                    AsignadoId = $asignado,
                    Participantes = $participantes,
                    Tipo = $tipo
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.ExecuteNonQuery();

            return id;
        }

        cmd.CommandText =
            """
            INSERT INTO ReunionPrecursoresPartes
                (SesionId, Hora, Titulo, AsignadoId,
                 Participantes, Tipo, Enviado)
            VALUES
                ($sesion, $hora, $titulo, $asignado,
                 $participantes, $tipo, 0);
            """;

        cmd.ExecuteNonQuery();

        using SqliteCommand idCmd =
            connection.CreateCommand();

        idCmd.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt32(
            idCmd.ExecuteScalar() ?? 0);
    }


    public static void EliminarParteSesionPrecursoresMobile(
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

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM ReunionPrecursoresPartes WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    public static void EliminarSesionPrecursoresMobile(
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

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM ReunionPrecursoresSesiones WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    // ------------------------------------------------------------------
    // EDICIÓN DE PARTES DEL PROGRAMA DE ASAMBLEA
    // ------------------------------------------------------------------

    public static int GuardarParteAsambleaMobile(
        int id,
        int asambleaId,
        string turno,
        string hora,
        string titulo,
        int asignadoId,
        string participantes,
        string tipo)
    {
        if (asambleaId <= 0)
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Parameters.AddWithValue(
            "$asamblea",
            asambleaId);

        cmd.Parameters.AddWithValue(
            "$turno",
            turno ?? "");

        cmd.Parameters.AddWithValue(
            "$hora",
            hora ?? "");

        cmd.Parameters.AddWithValue(
            "$titulo",
            titulo ?? "");

        cmd.Parameters.AddWithValue(
            "$asignado",
            asignadoId);

        cmd.Parameters.AddWithValue(
            "$participantes",
            participantes ?? "");

        cmd.Parameters.AddWithValue(
            "$tipo",
            tipo ?? "");

        if (id > 0)
        {
            cmd.CommandText =
                """
                UPDATE ProgramaAsambleasPartes
                SET Turno = $turno,
                    Hora = $hora,
                    Titulo = $titulo,
                    AsignadoId = $asignado,
                    Participantes = $participantes,
                    Tipo = $tipo
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.ExecuteNonQuery();

            return id;
        }

        cmd.CommandText =
            """
            INSERT INTO ProgramaAsambleasPartes
                (AsambleaId, Turno, Hora, Titulo, AsignadoId,
                 Participantes, Tipo, Enviado)
            VALUES
                ($asamblea, $turno, $hora, $titulo, $asignado,
                 $participantes, $tipo, 0);
            """;

        cmd.ExecuteNonQuery();

        using SqliteCommand idCmd =
            connection.CreateCommand();

        idCmd.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt32(
            idCmd.ExecuteScalar() ?? 0);
    }


    public static void EliminarParteAsambleaMobile(
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

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM ProgramaAsambleasPartes WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    public static int GuardarOrganigramaAsambleaMobile(
        int id,
        int asambleaId,
        string departamento,
        string rol,
        int asignadoId)
    {
        if (asambleaId <= 0)
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Parameters.AddWithValue(
            "$asamblea",
            asambleaId);

        cmd.Parameters.AddWithValue(
            "$departamento",
            departamento ?? "");

        cmd.Parameters.AddWithValue(
            "$rol",
            rol ?? "");

        cmd.Parameters.AddWithValue(
            "$asignado",
            asignadoId);

        if (id > 0)
        {
            cmd.CommandText =
                """
                UPDATE ProgramaAsambleasOrganigrama
                SET Departamento = $departamento,
                    Rol = $rol,
                    AsignadoId = $asignado
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.ExecuteNonQuery();

            return id;
        }

        cmd.CommandText =
            """
            INSERT INTO ProgramaAsambleasOrganigrama
                (AsambleaId, Departamento, AsignadoId, Rol)
            VALUES
                ($asamblea, $departamento, $asignado, $rol);
            """;

        cmd.ExecuteNonQuery();

        using SqliteCommand idCmd =
            connection.CreateCommand();

        idCmd.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt32(
            idCmd.ExecuteScalar() ?? 0);
    }


    public static void EliminarOrganigramaAsambleaMobile(
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

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM ProgramaAsambleasOrganigrama WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    private static int LeerIntAsamblea(
        SqliteDataReader reader,
        int indice)
    {
        return reader.IsDBNull(indice)
            ? 0
            : reader.GetInt32(indice);
    }
}
