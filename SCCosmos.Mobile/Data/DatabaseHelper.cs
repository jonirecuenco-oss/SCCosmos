using Microsoft.Data.Sqlite;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;

public record ResumenBaseDatos(
    int Personas,
    int Semanas,
    string NombreSC);

public static class DatabaseHelper
{
    private static string ConnectionString
    {
        get
        {
            var builder =
                new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePaths.RutaLocal
                };

            return builder.ToString();
        }
    }

    public static bool ExisteBaseLocal =>
        File.Exists(DatabasePaths.RutaLocal);

    public static ResumenBaseDatos ObtenerResumenPrueba()
    {
        if (!ExisteBaseLocal)
        {
            throw new FileNotFoundException(
                "Todavía no existe una base de datos local.");
        }

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        int personas =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas;");

        int semanas =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Semanas;");

        string nombreSC =
            ObtenerConfiguracion(
                connection,
                "NombreSC",
                "(sin configurar)");

        return new ResumenBaseDatos(
            personas,
            semanas,
            nombreSC);
    }

    public static string ObtenerConfiguracion(
        string clave,
        string valorPorDefecto = "")
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        return ObtenerConfiguracion(
            connection,
            clave,
            valorPorDefecto);
    }

    public static void GuardarConfiguracion(
        string clave,
        string valor)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO Configuracion
            (Clave, Valor)
            VALUES ($clave, $valor);";

        cmd.Parameters.AddWithValue(
            "$clave",
            clave);

        cmd.Parameters.AddWithValue(
            "$valor",
            valor ?? "");

        cmd.ExecuteNonQuery();
    }

    private static string ObtenerConfiguracion(
        SqliteConnection connection,
        string clave,
        string valorPorDefecto)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
            SELECT Valor
            FROM Configuracion
            WHERE Clave = $clave;";

        cmd.Parameters.AddWithValue(
            "$clave",
            clave);

        object? resultado =
            cmd.ExecuteScalar();

        return resultado?.ToString()
               ?? valorPorDefecto;
    }

    private static int EjecutarCount(
        SqliteConnection connection,
        string sql)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = sql;

        return Convert.ToInt32(
            cmd.ExecuteScalar());
    }

    public static SemanaCalendario? ObtenerSemana(string fechaIso)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
        SELECT *
        FROM Semanas
        WHERE date(FechaMartes) = date($fechaIso)
           OR FechaMartes LIKE $fechaIso || '%'
        LIMIT 1;";

        cmd.Parameters.AddWithValue(
            "$fechaIso",
            fechaIso);

        using var reader =
            cmd.ExecuteReader();

        if (!reader.Read())
            return null;

        return new SemanaCalendario
        {
            Id = Convert.ToInt32(reader["Id"]),

            FechaMartes =
                reader.GetDateTime(
                    reader.GetOrdinal("FechaMartes")),

            Actividad =
                ObtenerString(reader, "Actividad"),

            Congregacion =
                ObtenerString(reader, "Congregacion"),

            Tipo =
                ObtenerString(reader, "Tipo"),

            Acompanante =
                ObtenerString(reader, "Acompanante"),

            Alojamiento =
                ObtenerString(reader, "Alojamiento"),

            S302Enviado =
                ObtenerBool(reader, "S302Enviado"),

            DocEnviada =
                ObtenerBool(reader, "DocEnviada"),

            SeccionAsamblea =
                ObtenerString(reader, "SeccionAsamblea"),

            FechaAsamblea =
                ObtenerString(reader, "FechaAsamblea"),

            NotificacionEnviada =
                ObtenerBool(reader, "NotificacionEnviada"),

            RecordatorioEnviado =
                ObtenerBool(reader, "RecordatorioEnviado"),

            TieneDatosImportados =
                ObtenerBool(reader, "DatosImportados"),

            TipoAsamblea =
                ObtenerString(reader, "TipoAsamblea"),

            RepBetelNombre =
                ObtenerString(reader, "RepBetelNombre"),

            RepBetelTelefono =
                ObtenerString(reader, "RepBetelTelefono"),

            RepBetelEmail =
                ObtenerString(reader, "RepBetelEmail"),

            RepBetelMensajeEnviado =
                ObtenerBoolOpcional(
                    reader,
                    "RepBetelMensajeEnviado"),

            EsClonada = false
        };
    }

    public static string ObtenerCodigoCongregacion(
        string nombreCongregacion)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
        SELECT Codigo
        FROM Congregaciones
        WHERE Nombre = $nombre;";

        command.Parameters.AddWithValue(
            "$nombre",
            nombreCongregacion);

        object? result =
            command.ExecuteScalar();

        return result?.ToString()
               ?? string.Empty;
    }

    public static List<ActividadPrograma>
        ObtenerActividadesPrograma(
            string congreCodigo,
            string mesReferenciaStr,
            string dia,
            string turno,
            string responsable)
    {
        var lista =
            new List<ActividadPrograma>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
        SELECT
            TipoActividad,
            Hora,
            NotasLibro,
            CompaneroId
        FROM ProgramaVisitaV2
        WHERE CongreCodigo = $codigo
          AND MesReferencia = $mes
          AND DiaSemana = $dia
          AND Turno = $turno
          AND Responsable = $responsable
        ORDER BY Hora ASC;";

        cmd.Parameters.AddWithValue(
            "$codigo",
            congreCodigo);

        cmd.Parameters.AddWithValue(
            "$mes",
            mesReferenciaStr);

        cmd.Parameters.AddWithValue(
            "$dia",
            dia);

        cmd.Parameters.AddWithValue(
            "$turno",
            turno);

        cmd.Parameters.AddWithValue(
            "$responsable",
            responsable);

        using var reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            lista.Add(
                new ActividadPrograma
                {
                    TipoActividad =
                        reader.IsDBNull(0)
                            ? ""
                            : reader.GetString(0),

                    Hora =
                        reader.IsDBNull(1)
                            ? ""
                            : reader.GetString(1),

                    NotasLibro =
                        reader.IsDBNull(2)
                            ? ""
                            : reader.GetString(2),

                    CompaneroId =
                        reader.IsDBNull(3)
                            ? 0
                            : reader.GetInt32(3)
                });
        }

        return lista;
    }

    public static Persona? ObtenerPersonaPorId(
        int personaId)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
        SELECT
            Id,
            Nombre,
            Nombramiento,
            Servicio,
            Grupo,
            FechaNacimiento,
            Sexo,
            FechaBautismo,
            Esperanza,
            NombreJWHub,
            FechaCreacion,
            FechaEliminacion,
            NombreComun,
            Solicitudes,
            Privilegios,
            Foto,
            Email,
            Telefono,
            FechaInicioPrec,
            FechaUltimaEscuela,
            Direccion,
            Latitud,
            Longitud,
            ConyugeId,
            EmailJW,
            CongregacionCodigo,
            PersonaRaizId,
            MesHistorico,
            FechaExactaHistorico,
            Notas
        FROM Personas
        WHERE Id = $id;";

        command.Parameters.AddWithValue(
            "$id",
            personaId);

        using var reader =
            command.ExecuteReader();

        if (!reader.Read())
            return null;

        return new Persona
        {
            Id = reader.GetInt32(0),

            Nombre =
                reader.IsDBNull(1)
                    ? ""
                    : reader.GetString(1),

            Nombramiento =
                reader.IsDBNull(2) ? "" : reader.GetString(2),

            Servicio =
                reader.IsDBNull(3) ? "" : reader.GetString(3),

            Grupo =
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),

            FechaNacimiento =
                reader.IsDBNull(5) ? "" : reader.GetString(5),

            Sexo =
                reader.IsDBNull(6) ? "" : reader.GetString(6),

            FechaBautismo =
                reader.IsDBNull(7) ? "" : reader.GetString(7),

            Esperanza =
                reader.IsDBNull(8) ? "" : reader.GetString(8),

            NombreJWHub =
                reader.IsDBNull(9) ? "" : reader.GetString(9),

            FechaCreacion =
                reader.IsDBNull(10) ? "" : reader.GetString(10),

            FechaEliminacion =
                reader.IsDBNull(11) ? "" : reader.GetString(11),

            NombreComun =
                reader.IsDBNull(12) ? "" : reader.GetString(12),

            Solicitudes =
                reader.IsDBNull(13) ? "" : reader.GetString(13),

            Privilegios =
                reader.IsDBNull(14) ? "" : reader.GetString(14),

            Foto =
                reader.IsDBNull(15) ? "" : reader.GetString(15),

            Email =
                reader.IsDBNull(16) ? "" : reader.GetString(16),

            Telefono =
                reader.IsDBNull(17) ? "" : reader.GetString(17),

            FechaInicioPrec =
                reader.IsDBNull(18) ? "" : reader.GetString(18),

            FechaUltimaEscuela =
                reader.IsDBNull(19) ? "" : reader.GetString(19),

            Direccion =
                reader.IsDBNull(20) ? "" : reader.GetString(20),

            Latitud =
                reader.IsDBNull(21) ? 0 : reader.GetDouble(21),

            Longitud =
                reader.IsDBNull(22) ? 0 : reader.GetDouble(22),

            ConyugeId =
                reader.IsDBNull(23) ? 0 : reader.GetInt32(23),

            EmailJW =
                reader.IsDBNull(24) ? "" : reader.GetString(24),

            CongregacionCodigo =
                reader.IsDBNull(25) ? "" : reader.GetString(25),

            PersonaRaizId =
                reader.IsDBNull(26) ? 0 : reader.GetInt32(26),

            MesHistorico =
                reader.IsDBNull(27) ? null : reader.GetString(27),

            FechaExactaHistorico =
                reader.IsDBNull(28) ? "" : reader.GetString(28),

            Notas =
                reader.IsDBNull(29) ? "" : reader.GetString(29)
        };
    }

    public static EstadisticasCircuito
        ObtenerEstadisticasCircuito()
    {
        var resultado =
            new EstadisticasCircuito();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        resultado.PrecRegulares =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Servicio LIKE '%Precursor regular%';");

        resultado.PrecEspeciales =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Servicio LIKE '%Precursor especial%';");

        resultado.Ancianos =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Nombramiento LIKE '%Anciano%';");

        resultado.SiervosM =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Nombramiento LIKE '%Siervo ministerial%';");

        resultado.SolA2 =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-2%';");

        resultado.SolA19 =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-19%';");

        resultado.SolA8 =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%A-8%';");

        resultado.SolG8 =
            EjecutarCount(
                connection,
                "SELECT COUNT(*) FROM Personas WHERE Solicitudes LIKE '%G-8%';");

        return resultado;
    }

    private static string ObtenerString(
        SqliteDataReader reader,
        string columna)
    {
        int ordinal =
            reader.GetOrdinal(columna);

        return reader.IsDBNull(ordinal)
            ? ""
            : reader.GetString(ordinal);
    }

    private static bool ObtenerBool(
        SqliteDataReader reader,
        string columna)
    {
        int ordinal =
            reader.GetOrdinal(columna);

        return !reader.IsDBNull(ordinal)
               && Convert.ToInt32(
                   reader.GetValue(ordinal)) == 1;
    }

    private static bool ObtenerBoolOpcional(
        SqliteDataReader reader,
        string columna)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(
                    reader.GetName(i),
                    columna,
                    StringComparison.OrdinalIgnoreCase))
            {
                return !reader.IsDBNull(i)
                       && Convert.ToInt32(
                           reader.GetValue(i)) == 1;
            }
        }

        return false;
    }

    public static List<Persona> ObtenerPersonas()
    {
        var lista = new List<Persona>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
        SELECT
            p.Id,
            p.Nombre,
            p.FechaNacimiento,
            p.Sexo,
            p.FechaBautismo,
            p.Esperanza,
            p.Nombramiento,
            p.Servicio,
            p.CongregacionCodigo,
            c.Nombre,
            p.Grupo,
            p.NombreJWHub,
            p.FechaCreacion,
            p.FechaEliminacion,
            p.Solicitudes,
            p.Privilegios,
            p.Foto,
            p.NombreComun,
            p.Telefono,
            p.Email,
            p.FechaInicioPrec,
            p.FechaUltimaEscuela,
            p.Direccion,
            p.Latitud,
            p.Longitud,
            p.ConyugeId,
            p.EmailJW,
            p.PersonaRaizId,
            p.MesHistorico,
            p.FechaExactaHistorico,
            p.Notas
        FROM Personas p
        LEFT JOIN Congregaciones c
            ON p.CongregacionCodigo = c.Codigo
        WHERE p.MesHistorico IS NULL
        ORDER BY p.Nombre ASC;";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            lista.Add(new Persona
            {
                Id = reader.GetInt32(0),

                Nombre =
                    reader.IsDBNull(1) ? "" : reader.GetString(1),

                FechaNacimiento =
                    reader.IsDBNull(2) ? "" : reader.GetString(2),

                Sexo =
                    reader.IsDBNull(3) ? "" : reader.GetString(3),

                FechaBautismo =
                    reader.IsDBNull(4) ? "" : reader.GetString(4),

                Esperanza =
                    reader.IsDBNull(5) ? "" : reader.GetString(5),

                Nombramiento =
                    reader.IsDBNull(6) ? "" : reader.GetString(6),

                Servicio =
                    reader.IsDBNull(7) ? "" : reader.GetString(7),

                CongregacionCodigo =
                    reader.IsDBNull(8) ? "" : reader.GetString(8),

                CongregacionNombre =
                    reader.IsDBNull(9)
                        ? "Sin asignar"
                        : reader.GetString(9),

                Grupo =
                    reader.IsDBNull(10) ? 0 : reader.GetInt32(10),

                NombreJWHub =
                    reader.IsDBNull(11) ? "" : reader.GetString(11),

                FechaCreacion =
                    reader.IsDBNull(12) ? "" : reader.GetString(12),

                FechaEliminacion =
                    reader.IsDBNull(13) ? "" : reader.GetString(13),

                Solicitudes =
                    reader.IsDBNull(14) ? "" : reader.GetString(14),

                Privilegios =
                    reader.IsDBNull(15) ? "" : reader.GetString(15),

                Foto =
                    reader.IsDBNull(16) ? "" : reader.GetString(16),

                NombreComun =
                    reader.IsDBNull(17) ? "" : reader.GetString(17),

                Telefono =
                    reader.IsDBNull(18) ? "" : reader.GetString(18),

                Email =
                    reader.IsDBNull(19) ? "" : reader.GetString(19),

                FechaInicioPrec =
                    reader.IsDBNull(20) ? "" : reader.GetString(20),

                FechaUltimaEscuela =
                    reader.IsDBNull(21) ? "" : reader.GetString(21),

                Direccion =
                    reader.IsDBNull(22) ? "" : reader.GetString(22),

                Latitud =
                    reader.IsDBNull(23) ? 0 : reader.GetDouble(23),

                Longitud =
                    reader.IsDBNull(24) ? 0 : reader.GetDouble(24),

                ConyugeId =
                    reader.IsDBNull(25) ? 0 : reader.GetInt32(25),

                EmailJW =
                    reader.IsDBNull(26) ? "" : reader.GetString(26),

                PersonaRaizId =
                    reader.IsDBNull(27) ? 0 : reader.GetInt32(27),

                MesHistorico =
                    reader.IsDBNull(28) ? null : reader.GetString(28),

                FechaExactaHistorico =
                    reader.IsDBNull(29) ? "" : reader.GetString(29),

                Notas =
                    reader.IsDBNull(30) ? "" : reader.GetString(30)
            });
        }

        return lista;
    }

    public static List<Congregacion> ObtenerCongregaciones()
    {
        var lista = new List<Congregacion>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var cmd =
            connection.CreateCommand();

        cmd.CommandText = @"
        SELECT Codigo, Nombre
        FROM Congregaciones
        ORDER BY Nombre;";

        using var reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            lista.Add(new Congregacion
            {
                Codigo =
                    reader.IsDBNull(0)
                        ? ""
                        : reader.GetString(0),

                Nombre =
                    reader.IsDBNull(1)
                        ? ""
                        : reader.GetString(1)
            });
        }

        return lista;
    }

    public static List<Persona>
    ObtenerPersonasPorCongregacion(
        string congreCodigo)
    {
        return ObtenerPersonas()
            .Where(p =>
                string.Equals(
                    p.CongregacionCodigo,
                    congreCodigo,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static void ActualizarPersona(
    Persona p)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        // Averiguamos si ha cambiado la dirección.
        string direccionAnterior = "";

        using (var comprobar =
               connection.CreateCommand())
        {
            comprobar.CommandText = @"
            SELECT Direccion
            FROM Personas
            WHERE Id = $id;";

            comprobar.Parameters.AddWithValue(
                "$id",
                p.Id);

            object? resultado =
                comprobar.ExecuteScalar();

            if (resultado != null &&
                resultado != DBNull.Value)
            {
                direccionAnterior =
                    resultado.ToString() ?? "";
            }
        }

        bool direccionCambiada =
            !string.Equals(
                (p.Direccion ?? "").Trim(),
                direccionAnterior.Trim(),
                StringComparison.OrdinalIgnoreCase);

        if (direccionCambiada)
        {
            p.Latitud = 0;
            p.Longitud = 0;
        }

        using (var command =
               connection.CreateCommand())
        {
            command.CommandText = @"
            UPDATE Personas SET
                Nombre = $nombre,
                CongregacionCodigo = $congregacion,
                FechaNacimiento = $fechaNacimiento,
                Sexo = $sexo,
                FechaBautismo = $fechaBautismo,
                Esperanza = $esperanza,
                Nombramiento = $nombramiento,
                Servicio = $servicio,
                Grupo = $grupo,
                NombreJWHub = $nombreJW,
                NombreComun = $nombreComun,
                Telefono = $telefono,
                Email = $email,
                Solicitudes = $solicitudes,
                Privilegios = $privilegios,
                FechaInicioPrec = $inicioPrec,
                FechaUltimaEscuela = $escuela,
                Foto = $foto,
                Direccion = $direccion,
                Latitud = $latitud,
                Longitud = $longitud,
                ConyugeId = $conyuge,
                EmailJW = $emailJW,
                Notas = $notas
            WHERE Id = $id;";

            command.Parameters.AddWithValue(
                "$id", p.Id);

            command.Parameters.AddWithValue(
                "$nombre", p.Nombre ?? "");

            command.Parameters.AddWithValue(
                "$congregacion",
                p.CongregacionCodigo ?? "");

            command.Parameters.AddWithValue(
                "$fechaNacimiento",
                p.FechaNacimiento ?? "");

            command.Parameters.AddWithValue(
                "$sexo", p.Sexo ?? "");

            command.Parameters.AddWithValue(
                "$fechaBautismo",
                p.FechaBautismo ?? "");

            command.Parameters.AddWithValue(
                "$esperanza",
                p.Esperanza ?? "");

            command.Parameters.AddWithValue(
                "$nombramiento",
                p.Nombramiento ?? "");

            command.Parameters.AddWithValue(
                "$servicio",
                p.Servicio ?? "");

            command.Parameters.AddWithValue(
                "$grupo", p.Grupo);

            command.Parameters.AddWithValue(
                "$nombreJW",
                p.NombreJWHub ?? "");

            command.Parameters.AddWithValue(
                "$nombreComun",
                p.NombreComun ?? "");

            command.Parameters.AddWithValue(
                "$telefono",
                p.Telefono ?? "");

            command.Parameters.AddWithValue(
                "$email",
                p.Email ?? "");

            command.Parameters.AddWithValue(
                "$solicitudes",
                p.Solicitudes ?? "");

            command.Parameters.AddWithValue(
                "$privilegios",
                p.Privilegios ?? "");

            command.Parameters.AddWithValue(
                "$inicioPrec",
                p.FechaInicioPrec ?? "");

            command.Parameters.AddWithValue(
                "$escuela",
                p.FechaUltimaEscuela ?? "");

            command.Parameters.AddWithValue(
                "$foto",
                p.Foto ?? "");

            command.Parameters.AddWithValue(
                "$direccion",
                p.Direccion ?? "");

            command.Parameters.AddWithValue(
                "$latitud",
                p.Latitud);

            command.Parameters.AddWithValue(
                "$longitud",
                p.Longitud);

            command.Parameters.AddWithValue(
                "$conyuge",
                p.ConyugeId);

            command.Parameters.AddWithValue(
                "$emailJW",
                p.EmailJW ?? "");

            command.Parameters.AddWithValue(
                "$notas",
                p.Notas ?? "");

            command.ExecuteNonQuery();
        }

        // Conservamos la misma regla de SCCosmos Windows:
        // si cambia la dirección, invalidamos también
        // las coordenadas de las versiones relacionadas.
        if (direccionCambiada)
        {
            using var clones =
                connection.CreateCommand();

            clones.CommandText = @"
            UPDATE Personas
            SET Direccion = $direccion,
                Latitud = 0,
                Longitud = 0
            WHERE PersonaRaizId = $id
               OR Id = (
                    SELECT PersonaRaizId
                    FROM Personas
                    WHERE Id = $id
                      AND PersonaRaizId > 0
               );";

            clones.Parameters.AddWithValue(
                "$direccion",
                p.Direccion ?? "");

            clones.Parameters.AddWithValue(
                "$id",
                p.Id);

            clones.ExecuteNonQuery();
        }
    }

    public static int InsertarPersonaNueva(
    Persona p)
    {
        using var connection =
            new Microsoft.Data.Sqlite.SqliteConnection(
                ConnectionString);


        connection.Open();


        using var cmd =
            connection.CreateCommand();


        cmd.CommandText =
            """
        INSERT INTO Personas
        (
            Nombre,
            CongregacionCodigo,
            Grupo,
            Nombramiento,
            Servicio,
            Solicitudes,
            Privilegios,
            Foto,
            FechaNacimiento,
            Sexo,
            FechaBautismo,
            Esperanza,
            Telefono,
            Email,
            FechaInicioPrec,
            FechaUltimaEscuela,
            NombreJWHub,
            FechaCreacion,
            NombreComun,
            Direccion,
            Latitud,
            Longitud,
            ConyugeId,
            EmailJW,
            Notas
        )
        VALUES
        (
            $nombre,
            $codigo,
            $grupo,
            $nomb,
            $serv,
            $sol,
            $priv,
            $foto,
            $fn,
            $sexo,
            $fb,
            $esperanza,
            $tlf,
            $email,
            $inicio,
            $escuela,
            $jw,
            $fc,
            $nombreComun,
            $dir,
            $lat,
            $lon,
            $conyuge,
            $emailjw,
            $notas
        );

        SELECT last_insert_rowid();
        """;


        cmd.Parameters.AddWithValue(
            "$nombre",
            p.Nombre ?? "");


        cmd.Parameters.AddWithValue(
            "$codigo",
            p.CongregacionCodigo ?? "");


        cmd.Parameters.AddWithValue(
            "$grupo",
            p.Grupo);


        cmd.Parameters.AddWithValue(
            "$nomb",
            p.Nombramiento ?? "");


        cmd.Parameters.AddWithValue(
            "$serv",
            p.Servicio ?? "");


        cmd.Parameters.AddWithValue(
            "$sol",
            p.Solicitudes ?? "");


        cmd.Parameters.AddWithValue(
            "$priv",
            p.Privilegios ?? "");


        cmd.Parameters.AddWithValue(
            "$foto",
            p.Foto ?? "");


        cmd.Parameters.AddWithValue(
            "$fn",
            p.FechaNacimiento ?? "");


        cmd.Parameters.AddWithValue(
            "$sexo",
            p.Sexo ?? "");


        cmd.Parameters.AddWithValue(
            "$fb",
            p.FechaBautismo ?? "");


        cmd.Parameters.AddWithValue(
            "$esperanza",
            p.Esperanza ?? "");


        cmd.Parameters.AddWithValue(
            "$tlf",
            p.Telefono ?? "");


        cmd.Parameters.AddWithValue(
            "$email",
            p.Email ?? "");


        cmd.Parameters.AddWithValue(
            "$inicio",
            p.FechaInicioPrec ?? "");


        cmd.Parameters.AddWithValue(
            "$escuela",
            p.FechaUltimaEscuela ?? "");


        cmd.Parameters.AddWithValue(
            "$jw",
            p.NombreJWHub ?? "");


        cmd.Parameters.AddWithValue(
            "$fc",
            p.FechaCreacion ?? "");


        cmd.Parameters.AddWithValue(
            "$nombreComun",
            p.NombreComun ?? "");


        cmd.Parameters.AddWithValue(
            "$dir",
            p.Direccion ?? "");


        cmd.Parameters.AddWithValue(
            "$lat",
            p.Latitud);


        cmd.Parameters.AddWithValue(
            "$lon",
            p.Longitud);


        cmd.Parameters.AddWithValue(
            "$conyuge",
            p.ConyugeId);


        cmd.Parameters.AddWithValue(
            "$emailjw",
            p.EmailJW ?? "");


        cmd.Parameters.AddWithValue(
            "$notas",
            p.Notas ?? "");


        return Convert.ToInt32(
            cmd.ExecuteScalar());
    }

    public static void MarcarPersonaEliminada(
    int idPersona)
    {
        using var connection =
            new Microsoft.Data.Sqlite.SqliteConnection(
                ConnectionString);


        connection.Open();


        using var cmd =
            connection.CreateCommand();


        cmd.CommandText =
            """
        UPDATE Personas
        SET FechaEliminacion = $fecha
        WHERE Id = $id
           OR PersonaRaizId = $id;
        """;


        cmd.Parameters.AddWithValue(
            "$fecha",
            DateTime.Today.ToString(
                "dd/MM/yyyy"));


        cmd.Parameters.AddWithValue(
            "$id",
            idPersona);


        cmd.ExecuteNonQuery();
    }
}