using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// PERSONAS: CAMPOS DE ORIGEN, HISTORIAL, TRASLADOS Y FUSIÓN (Android)
// ============================================================================
//
// Réplica de las operaciones equivalentes de WPF
// (MarcarOrigenPersona, ObtenerPersonasHistoricas, RegistrarTraslado,
// FusionarYBorrarDefinitivo). Usa exactamente el mismo esquema compartido.

public static partial class DatabaseHelper
{
    // ------------------------------------------------------------------
    // CAMPOS EXTRA
    // ------------------------------------------------------------------

    public sealed class CamposExtraPersonaMobile
    {
        public string OrigenRegistro { get; set; } = "DESCONOCIDO";
        public bool AprobadoExhibidores { get; set; }
        public int RequisitoHoras { get; set; }
        public int TrasladoAnteriorId { get; set; }
        public int TrasladoSiguienteId { get; set; }
        public string FechaTraslado { get; set; } = "";
        public string FechaEliminacion { get; set; } = "";
        public int PersonaRaizId { get; set; }
    }


    public static CamposExtraPersonaMobile?
        ObtenerCamposExtraPersonaMobile(
            int personaId)
    {
        if (personaId <= 0)
        {
            return null;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                OrigenRegistro,
                AprobadoExhibidores,
                RequisitoHoras,
                TrasladoAnteriorId,
                TrasladoSiguienteId,
                FechaTraslado,
                FechaEliminacion,
                PersonaRaizId
            FROM Personas
            WHERE Id = $id
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new CamposExtraPersonaMobile
        {
            OrigenRegistro =
                Texto(reader, 0, "DESCONOCIDO"),

            AprobadoExhibidores =
                Entero(reader, 1) == 1,

            RequisitoHoras =
                Entero(reader, 2),

            TrasladoAnteriorId =
                Entero(reader, 3),

            TrasladoSiguienteId =
                Entero(reader, 4),

            FechaTraslado =
                Texto(reader, 5, ""),

            FechaEliminacion =
                Texto(reader, 6, ""),

            PersonaRaizId =
                Entero(reader, 7)
        };
    }


    /// <summary>
    /// Actualiza solo los campos que gestiona el móvil, sin tocar los
    /// enlaces de traslado ni el resto de columnas de WPF.
    /// </summary>
    public static void ActualizarCamposExtraPersonaMobile(
        int personaId,
        bool aprobadoExhibidores,
        int requisitoHoras)
    {
        if (personaId <= 0)
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
            """
            UPDATE Personas
            SET AprobadoExhibidores = $aprobado,
                RequisitoHoras = $requisito
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue(
            "$aprobado",
            aprobadoExhibidores ? 1 : 0);

        cmd.Parameters.AddWithValue(
            "$requisito",
            requisitoHoras);

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.ExecuteNonQuery();
    }


    public static void MarcarOrigenPersonaMobile(
        int personaId,
        string origen)
    {
        if (personaId <= 0
            ||
            string.IsNullOrWhiteSpace(
                origen))
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        string actual =
            "DESCONOCIDO";

        using (SqliteCommand leer =
               connection.CreateCommand())
        {
            leer.CommandText =
                "SELECT OrigenRegistro FROM Personas WHERE Id = $id;";

            leer.Parameters.AddWithValue(
                "$id",
                personaId);

            object? valor =
                leer.ExecuteScalar();

            if (valor is not null
                &&
                valor != DBNull.Value
                &&
                !string.IsNullOrWhiteSpace(
                    valor.ToString()))
            {
                actual =
                    valor.ToString()!;
            }
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "UPDATE Personas SET OrigenRegistro = $origen WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$origen",
            CombinarOrigenesMobile(
                actual,
                origen));

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.ExecuteNonQuery();
    }


    public static string CombinarOrigenesMobile(
        string? origenActual,
        string? nuevoOrigen)
    {
        var origenes =
            new List<string>();

        void Agregar(string? valor)
        {
            if (string.IsNullOrWhiteSpace(
                    valor))
            {
                return;
            }

            foreach (string parte in valor.Split(
                         new[] { '+', ',', ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string limpia =
                    parte.Trim();

                if (limpia.Equals(
                        "DESCONOCIDO",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!origenes.Any(x =>
                        x.Equals(
                            limpia,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    origenes.Add(limpia);
                }
            }
        }

        Agregar(origenActual);
        Agregar(nuevoOrigen);

        return origenes.Count == 0
            ? "DESCONOCIDO"
            : string.Join(
                "+",
                origenes);
    }


    // ------------------------------------------------------------------
    // HISTORIAL
    // ------------------------------------------------------------------

    public sealed class InstantaneaHistoricaMobile
    {
        public int Id { get; set; }
        public string MesHistorico { get; set; } = "";
        public string FechaExacta { get; set; } = "";
        public string CongregacionCodigo { get; set; } = "";
        public string Nombramiento { get; set; } = "";
        public string Servicio { get; set; } = "";
        public string Grupo { get; set; } = "";

        public string Titulo =>
            string.IsNullOrWhiteSpace(MesHistorico)
                ? "(sin mes)"
                : MesHistorico;

        public string Detalle
        {
            get
            {
                var partes =
                    new List<string>();

                if (!string.IsNullOrWhiteSpace(FechaExacta))
                {
                    partes.Add(FechaExacta);
                }

                if (!string.IsNullOrWhiteSpace(Nombramiento))
                {
                    partes.Add(Nombramiento);
                }

                if (!string.IsNullOrWhiteSpace(Servicio))
                {
                    partes.Add(Servicio);
                }

                if (!string.IsNullOrWhiteSpace(CongregacionCodigo))
                {
                    partes.Add(CongregacionCodigo);
                }

                return string.Join(
                    " · ",
                    partes);
            }
        }
    }


    public static List<InstantaneaHistoricaMobile>
        ObtenerHistorialPersonaMobile(
            int personaId)
    {
        var resultado =
            new List<InstantaneaHistoricaMobile>();

        if (personaId <= 0)
        {
            return resultado;
        }

        int raiz =
            personaId;

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using (SqliteCommand raizCmd =
               connection.CreateCommand())
        {
            raizCmd.CommandText =
                "SELECT COALESCE(PersonaRaizId,0) FROM Personas WHERE Id = $id;";

            raizCmd.Parameters.AddWithValue(
                "$id",
                personaId);

            object? valor =
                raizCmd.ExecuteScalar();

            if (valor is not null
                &&
                valor != DBNull.Value)
            {
                int r =
                    Convert.ToInt32(valor);

                if (r > 0)
                {
                    raiz = r;
                }
            }
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                MesHistorico,
                FechaExactaHistorico,
                CongregacionCodigo,
                Nombramiento,
                Servicio,
                Grupo
            FROM Personas
            WHERE PersonaRaizId = $raiz
              AND MesHistorico IS NOT NULL
            ORDER BY MesHistorico DESC;
            """;

        cmd.Parameters.AddWithValue(
            "$raiz",
            raiz);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new InstantaneaHistoricaMobile
                {
                    Id = Entero(reader, 0),
                    MesHistorico = Texto(reader, 1, ""),
                    FechaExacta = Texto(reader, 2, ""),
                    CongregacionCodigo = Texto(reader, 3, ""),
                    Nombramiento = Texto(reader, 4, ""),
                    Servicio = Texto(reader, 5, ""),
                    Grupo =
                        reader.IsDBNull(6)
                            ? ""
                            : reader.GetInt32(6).ToString()
                });
        }

        return resultado;
    }


    // ------------------------------------------------------------------
    // TRASLADO
    // ------------------------------------------------------------------

    /// <summary>
    /// Registra el traslado de una persona a otra congregación: crea una
    /// nueva ficha enlazada, marca la anterior como baja y conserva la
    /// cadena de traslados. Réplica simplificada de RegistrarTraslado de
    /// WPF (sin datos nuevos de JW Hub).
    /// </summary>
    public static int RegistrarTrasladoMobile(
        int personaAnteriorId,
        string nuevoCongreCodigo,
        DateTime fechaTraslado)
    {
        AnotarActividadMobile(
            "Personas",
            "Registró el traslado de «" +
            NombrePersonaPorIdMobile(personaAnteriorId) +
            "» a " + nuevoCongreCodigo);

        if (personaAnteriorId <= 0
            ||
            string.IsNullOrWhiteSpace(
                nuevoCongreCodigo))
        {
            return 0;
        }

        Persona? anterior =
            ObtenerPersonaPorId(
                personaAnteriorId);

        if (anterior is null)
        {
            throw new InvalidOperationException(
                "La ficha anterior no existe.");
        }

        if (string.Equals(
                anterior.CongregacionCodigo,
                nuevoCongreCodigo,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Un traslado debe realizarse entre congregaciones " +
                "distintas.");
        }

        string fecha =
            fechaTraslado.ToString(
                "dd/MM/yyyy");

        CamposExtraPersonaMobile? extra =
            ObtenerCamposExtraPersonaMobile(
                personaAnteriorId);

        string origen =
            CombinarOrigenesMobile(
                extra?.OrigenRegistro,
                "Manual");

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using var transaction =
            connection.BeginTransaction();

        try
        {
            int yaTrasladadoA = 0;

            using (SqliteCommand check =
                   connection.CreateCommand())
            {
                check.Transaction =
                    transaction;

                check.CommandText =
                    "SELECT COALESCE(TrasladoSiguienteId, 0) FROM Personas WHERE Id = $id;";

                check.Parameters.AddWithValue(
                    "$id",
                    personaAnteriorId);

                object? valor =
                    check.ExecuteScalar();

                if (valor is null
                    ||
                    valor == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "La ficha anterior ya no existe.");
                }

                yaTrasladadoA =
                    Convert.ToInt32(valor);
            }

            if (yaTrasladadoA > 0)
            {
                throw new InvalidOperationException(
                    "Esta ficha ya tiene registrado un traslado posterior.");
            }

            int nuevoId;

            using (SqliteCommand insert =
                   connection.CreateCommand())
            {
                insert.Transaction =
                    transaction;

                insert.CommandText =
                    """
                    INSERT INTO Personas
                    (
                        Nombre, CongregacionCodigo, Grupo, Nombramiento, Servicio,
                        Solicitudes, Privilegios, Foto, FechaNacimiento, Sexo,
                        FechaBautismo, Esperanza, Telefono, Email, FechaInicioPrec,
                        FechaUltimaEscuela, NombreJWHub, FechaCreacion, FechaEliminacion,
                        NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW,
                        Notas, OrigenRegistro, AprobadoExhibidores, RequisitoHoras,
                        TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
                    )
                    SELECT
                        Nombre, $codigo, Grupo, Nombramiento, Servicio,
                        Solicitudes, Privilegios, Foto, FechaNacimiento, Sexo,
                        FechaBautismo, Esperanza, Telefono, Email, FechaInicioPrec,
                        FechaUltimaEscuela, NombreJWHub, $fecha, '',
                        NombreComun, Direccion, Latitud, Longitud, 0, EmailJW,
                        Notas, $origen, AprobadoExhibidores, RequisitoHoras,
                        $anterior, 0, $fecha
                    FROM Personas
                    WHERE Id = $anterior;
                    """;

                insert.Parameters.AddWithValue(
                    "$codigo",
                    nuevoCongreCodigo);

                insert.Parameters.AddWithValue(
                    "$fecha",
                    fecha);

                insert.Parameters.AddWithValue(
                    "$origen",
                    origen);

                insert.Parameters.AddWithValue(
                    "$anterior",
                    personaAnteriorId);

                insert.ExecuteNonQuery();

                using SqliteCommand idCmd =
                    connection.CreateCommand();

                idCmd.Transaction =
                    transaction;

                idCmd.CommandText =
                    "SELECT last_insert_rowid();";

                nuevoId =
                    Convert.ToInt32(
                        idCmd.ExecuteScalar() ?? 0);
            }

            using (SqliteCommand marcar =
                   connection.CreateCommand())
            {
                marcar.Transaction =
                    transaction;

                marcar.CommandText =
                    """
                    UPDATE Personas
                    SET FechaEliminacion = $fecha,
                        TrasladoSiguienteId = $nuevoId,
                        FechaTraslado = $fecha
                    WHERE Id = $anteriorId;
                    """;

                marcar.Parameters.AddWithValue(
                    "$fecha",
                    fecha);

                marcar.Parameters.AddWithValue(
                    "$nuevoId",
                    nuevoId);

                marcar.Parameters.AddWithValue(
                    "$anteriorId",
                    personaAnteriorId);

                marcar.ExecuteNonQuery();
            }

            transaction.Commit();

            return nuevoId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }


    /// <summary>
    /// Registra el traslado de una persona usando además los datos
    /// recién leídos de JW Hub (réplica de RegistrarTraslado de WPF):
    /// crea la ficha nueva en la congregación de destino con los datos
    /// actuales del Hub y deja la anterior enlazada como baja histórica.
    /// </summary>
    public static int RegistrarTrasladoConHubMobile(
        Persona personaAnterior,
        Persona personaHubNueva,
        DateTime fechaTraslado)
    {
        AnotarActividadMobile(
            "Personas",
            "Registró el traslado de «" +
            MostrarNombreMobile(personaAnterior) + "» (" +
            (personaAnterior?.CongregacionCodigo ?? "") + " → " +
            (personaHubNueva?.CongregacionCodigo ?? "") + ")");

        if (personaAnterior is null)
        {
            throw new ArgumentNullException(
                nameof(personaAnterior));
        }

        if (personaHubNueva is null)
        {
            throw new ArgumentNullException(
                nameof(personaHubNueva));
        }

        if (personaAnterior.Id <= 0)
        {
            throw new InvalidOperationException(
                "La persona anterior no tiene un Id válido.");
        }

        if (string.IsNullOrWhiteSpace(
                personaHubNueva.CongregacionCodigo))
        {
            throw new InvalidOperationException(
                "La nueva congregación no es válida.");
        }

        if (string.Equals(
                personaAnterior.CongregacionCodigo,
                personaHubNueva.CongregacionCodigo,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Un traslado debe realizarse entre congregaciones " +
                "distintas.");
        }

        static string Elegir(
            string? preferido,
            string? alternativo) =>
            !string.IsNullOrWhiteSpace(preferido)
                ? preferido
                : (alternativo ?? "");

        string fecha =
            fechaTraslado.ToString(
                "dd/MM/yyyy");

        // Nombre completo y nombre común consolidados se conservan de
        // la ficha anterior; NombreJWHub sí se toma del Hub.
        string nombre =
            Elegir(
                personaAnterior.Nombre,
                personaHubNueva.Nombre);

        string nombreComun =
            Elegir(
                personaAnterior.NombreComun,
                personaHubNueva.NombreComun);

        string nombreJw =
            !string.IsNullOrWhiteSpace(
                personaHubNueva.NombreJWHub)
                ? personaHubNueva.NombreJWHub
                : (!string.IsNullOrWhiteSpace(
                       personaHubNueva.Nombre)
                      ? personaHubNueva.Nombre
                      : (personaAnterior.NombreJWHub ?? ""));

        string fechaNacimiento =
            Elegir(
                personaAnterior.FechaNacimiento,
                personaHubNueva.FechaNacimiento);

        string fechaBautismo =
            Elegir(
                personaAnterior.FechaBautismo,
                personaHubNueva.FechaBautismo);

        string sexo =
            Elegir(
                personaAnterior.Sexo,
                personaHubNueva.Sexo);

        string esperanza =
            Elegir(
                personaHubNueva.Esperanza,
                personaAnterior.Esperanza);

        // Para campos personales/contacto, Hub aporta el dato actual
        // si existe; si la extracción viene vacía, se conserva el
        // dato anterior.
        string telefono =
            Elegir(
                personaHubNueva.Telefono,
                personaAnterior.Telefono);

        string email =
            Elegir(
                personaHubNueva.Email,
                personaAnterior.Email);

        string emailJw =
            Elegir(
                personaHubNueva.EmailJW,
                personaAnterior.EmailJW);

        string direccion =
            Elegir(
                personaHubNueva.Direccion,
                personaAnterior.Direccion);

        string foto =
            Elegir(
                personaHubNueva.Foto,
                personaAnterior.Foto);

        string inicioPrec =
            Elegir(
                personaHubNueva.FechaInicioPrec,
                personaAnterior.FechaInicioPrec);

        string ultimaEscuela =
            Elegir(
                personaHubNueva.FechaUltimaEscuela,
                personaAnterior.FechaUltimaEscuela);

        double latitud =
            personaHubNueva.Latitud;

        double longitud =
            personaHubNueva.Longitud;

        if ((Math.Abs(latitud) < 0.000001
             && Math.Abs(longitud) < 0.000001)
            &&
            string.Equals(
                (direccion ?? "").Trim(),
                (personaAnterior.Direccion ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            latitud =
                personaAnterior.Latitud;

            longitud =
                personaAnterior.Longitud;
        }

        string origen =
            CombinarOrigenesMobile(
                CombinarOrigenesMobile(
                    personaAnterior.OrigenRegistro,
                    personaHubNueva.OrigenRegistro),
                "JWHub");

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using var transaction =
            connection.BeginTransaction();

        try
        {
            int yaTrasladadoA = 0;

            using (SqliteCommand check =
                   connection.CreateCommand())
            {
                check.Transaction =
                    transaction;

                check.CommandText =
                    "SELECT COALESCE(TrasladoSiguienteId, 0) FROM Personas WHERE Id = $id;";

                check.Parameters.AddWithValue(
                    "$id",
                    personaAnterior.Id);

                object? valor =
                    check.ExecuteScalar();

                if (valor is null
                    || valor == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "La ficha anterior ya no existe.");
                }

                yaTrasladadoA =
                    Convert.ToInt32(valor);
            }

            if (yaTrasladadoA > 0)
            {
                throw new InvalidOperationException(
                    "Esta ficha ya tiene registrado un traslado " +
                    "posterior.");
            }

            // Si el cónyuge también se trasladó antes, enlazamos con
            // su nueva ficha.
            int conyugeNuevoId = 0;

            if (personaAnterior.ConyugeId > 0)
            {
                using SqliteCommand conyuge =
                    connection.CreateCommand();

                conyuge.Transaction =
                    transaction;

                conyuge.CommandText =
                    "SELECT COALESCE(TrasladoSiguienteId, 0) FROM Personas WHERE Id = $id;";

                conyuge.Parameters.AddWithValue(
                    "$id",
                    personaAnterior.ConyugeId);

                object? valorConyuge =
                    conyuge.ExecuteScalar();

                if (valorConyuge is not null
                    && valorConyuge != DBNull.Value)
                {
                    conyugeNuevoId =
                        Convert.ToInt32(valorConyuge);
                }
            }

            int nuevoId;

            using (SqliteCommand insert =
                   connection.CreateCommand())
            {
                insert.Transaction =
                    transaction;

                insert.CommandText =
                    """
                    INSERT INTO Personas
                    (
                        Nombre, CongregacionCodigo, Grupo, Nombramiento, Servicio,
                        Solicitudes, Privilegios, Foto, FechaNacimiento, Sexo,
                        FechaBautismo, Esperanza, Telefono, Email, FechaInicioPrec,
                        FechaUltimaEscuela, NombreJWHub, FechaCreacion, FechaEliminacion,
                        NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW,
                        Notas, OrigenRegistro, AprobadoExhibidores, RequisitoHoras,
                        TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
                    )
                    VALUES
                    (
                        $nombre, $codigo, 0, $nomb, $serv,
                        $sol, $priv, $foto, $fn, $sexo,
                        $fb, $esperanza, $tlf, $email, $inicio,
                        $escuela, $jw, $fc, '',
                        $nombreComun, $dir, $lat, $lon, $conyuge, $emailjw,
                        $notas, $origen, $aprobado, $requisito,
                        $anteriorId, 0, $fechaTraslado
                    );
                    """;

                insert.Parameters.AddWithValue(
                    "$nombre",
                    nombre);

                insert.Parameters.AddWithValue(
                    "$codigo",
                    personaHubNueva.CongregacionCodigo);

                // Nombramiento/servicio/solicitudes/privilegios reflejan
                // el estado actual de Hub: un vacío puede significar que
                // el privilegio terminó.
                insert.Parameters.AddWithValue(
                    "$nomb",
                    personaHubNueva.Nombramiento ?? "");

                insert.Parameters.AddWithValue(
                    "$serv",
                    personaHubNueva.Servicio ?? "");

                insert.Parameters.AddWithValue(
                    "$sol",
                    personaHubNueva.Solicitudes ?? "");

                insert.Parameters.AddWithValue(
                    "$priv",
                    personaHubNueva.Privilegios ?? "");

                insert.Parameters.AddWithValue(
                    "$foto",
                    foto);

                insert.Parameters.AddWithValue(
                    "$fn",
                    fechaNacimiento);

                insert.Parameters.AddWithValue(
                    "$sexo",
                    sexo);

                insert.Parameters.AddWithValue(
                    "$fb",
                    fechaBautismo);

                insert.Parameters.AddWithValue(
                    "$esperanza",
                    esperanza);

                insert.Parameters.AddWithValue(
                    "$tlf",
                    telefono);

                insert.Parameters.AddWithValue(
                    "$email",
                    email);

                insert.Parameters.AddWithValue(
                    "$inicio",
                    inicioPrec);

                insert.Parameters.AddWithValue(
                    "$escuela",
                    ultimaEscuela);

                insert.Parameters.AddWithValue(
                    "$jw",
                    nombreJw);

                insert.Parameters.AddWithValue(
                    "$fc",
                    fecha);

                insert.Parameters.AddWithValue(
                    "$nombreComun",
                    nombreComun);

                insert.Parameters.AddWithValue(
                    "$dir",
                    direccion);

                insert.Parameters.AddWithValue(
                    "$lat",
                    latitud);

                insert.Parameters.AddWithValue(
                    "$lon",
                    longitud);

                insert.Parameters.AddWithValue(
                    "$conyuge",
                    conyugeNuevoId);

                insert.Parameters.AddWithValue(
                    "$emailjw",
                    emailJw);

                insert.Parameters.AddWithValue(
                    "$notas",
                    personaAnterior.Notas ?? "");

                insert.Parameters.AddWithValue(
                    "$origen",
                    origen);

                insert.Parameters.AddWithValue(
                    "$aprobado",
                    personaAnterior.AprobadoExhibidores
                        ? 1
                        : 0);

                insert.Parameters.AddWithValue(
                    "$requisito",
                    personaHubNueva.RequisitoHoras > 0
                        ? personaHubNueva.RequisitoHoras
                        : personaAnterior.RequisitoHoras);

                insert.Parameters.AddWithValue(
                    "$anteriorId",
                    personaAnterior.Id);

                insert.Parameters.AddWithValue(
                    "$fechaTraslado",
                    fecha);

                insert.ExecuteNonQuery();

                using SqliteCommand idCmd =
                    connection.CreateCommand();

                idCmd.Transaction =
                    transaction;

                idCmd.CommandText =
                    "SELECT last_insert_rowid();";

                nuevoId =
                    Convert.ToInt32(
                        idCmd.ExecuteScalar() ?? 0);
            }

            using (SqliteCommand marcar =
                   connection.CreateCommand())
            {
                marcar.Transaction =
                    transaction;

                marcar.CommandText =
                    """
                    UPDATE Personas
                    SET FechaEliminacion = $fecha,
                        TrasladoSiguienteId = $nuevoId,
                        FechaTraslado = $fecha
                    WHERE Id = $anteriorId;
                    """;

                marcar.Parameters.AddWithValue(
                    "$fecha",
                    fecha);

                marcar.Parameters.AddWithValue(
                    "$nuevoId",
                    nuevoId);

                marcar.Parameters.AddWithValue(
                    "$anteriorId",
                    personaAnterior.Id);

                marcar.ExecuteNonQuery();
            }

            // Si el cónyuge también tiene ficha nueva, reconstruimos la
            // relación entre las dos fichas actuales.
            if (conyugeNuevoId > 0)
            {
                using SqliteCommand pareja =
                    connection.CreateCommand();

                pareja.Transaction =
                    transaction;

                pareja.CommandText =
                    "UPDATE Personas SET ConyugeId = $nuevoId WHERE Id = $conyugeNuevoId;";

                pareja.Parameters.AddWithValue(
                    "$nuevoId",
                    nuevoId);

                pareja.Parameters.AddWithValue(
                    "$conyugeNuevoId",
                    conyugeNuevoId);

                pareja.ExecuteNonQuery();
            }

            transaction.Commit();

            return nuevoId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }


    // ------------------------------------------------------------------
    // FUSIÓN
    // ------------------------------------------------------------------

    /// <summary>
    /// Fusiona la ficha origen en la de destino y borra la origen (y sus
    /// instantáneas). Réplica de FusionarYBorrarDefinitivo de WPF.
    /// </summary>
    public static void FusionarYBorrarDefinitivoMobile(
        int destinoId,
        int origenId)
    {
        if (destinoId <= 0
            ||
            origenId <= 0
            ||
            destinoId == origenId)
        {
            return;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using var transaction =
            connection.BeginTransaction();

        try
        {
            // Mover actividad.
            try
            {
                using SqliteCommand act =
                    connection.CreateCommand();

                act.Transaction =
                    transaction;

                act.CommandText =
                    "UPDATE ActividadPersonas SET PersonaId = $destino WHERE PersonaId = $origen;";

                act.Parameters.AddWithValue(
                    "$destino",
                    destinoId);

                act.Parameters.AddWithValue(
                    "$origen",
                    origenId);

                act.ExecuteNonQuery();
            }
            catch
            {
                // La actividad no coincide exactamente; se ignora igual que WPF.
            }

            // Redirigir referencias de cónyuge.
            try
            {
                using SqliteCommand cony =
                    connection.CreateCommand();

                cony.Transaction =
                    transaction;

                cony.CommandText =
                    "UPDATE Personas SET ConyugeId = $destino WHERE ConyugeId = $origen;";

                cony.Parameters.AddWithValue(
                    "$destino",
                    destinoId);

                cony.Parameters.AddWithValue(
                    "$origen",
                    origenId);

                cony.ExecuteNonQuery();
            }
            catch
            {
            }

            // Redirigir enlaces de traslado de otras fichas.
            try
            {
                using SqliteCommand links =
                    connection.CreateCommand();

                links.Transaction =
                    transaction;

                links.CommandText =
                    """
                    UPDATE Personas SET TrasladoSiguienteId = $destino WHERE TrasladoSiguienteId = $origen;
                    UPDATE Personas SET TrasladoAnteriorId = $destino WHERE TrasladoAnteriorId = $origen;
                    """;

                links.Parameters.AddWithValue(
                    "$destino",
                    destinoId);

                links.Parameters.AddWithValue(
                    "$origen",
                    origenId);

                links.ExecuteNonQuery();
            }
            catch
            {
            }

            string origenDestino =
                LeerOrigen(
                    connection,
                    transaction,
                    destinoId);

            string origenOrigen =
                LeerOrigen(
                    connection,
                    transaction,
                    origenId);

            string combinado =
                CombinarOrigenesMobile(
                    origenDestino,
                    origenOrigen);

            int antDest = LeerEntero(
                connection,
                transaction,
                destinoId,
                "TrasladoAnteriorId");

            int sigDest = LeerEntero(
                connection,
                transaction,
                destinoId,
                "TrasladoSiguienteId");

            string fechaDest =
                LeerTextoColumna(
                    connection,
                    transaction,
                    destinoId,
                    "FechaTraslado");

            int antOrig = LeerEntero(
                connection,
                transaction,
                origenId,
                "TrasladoAnteriorId");

            int sigOrig = LeerEntero(
                connection,
                transaction,
                origenId,
                "TrasladoSiguienteId");

            string fechaOrig =
                LeerTextoColumna(
                    connection,
                    transaction,
                    origenId,
                    "FechaTraslado");

            int antFinal =
                antDest > 0 ? antDest : antOrig;

            int sigFinal =
                sigDest > 0 ? sigDest : sigOrig;

            string fechaFinal =
                !string.IsNullOrWhiteSpace(fechaDest)
                    ? fechaDest
                    : fechaOrig;

            // Borrar la ficha origen y sus instantáneas.
            using (SqliteCommand borrar =
                   connection.CreateCommand())
            {
                borrar.Transaction =
                    transaction;

                borrar.CommandText =
                    "DELETE FROM Personas WHERE Id = $origen OR PersonaRaizId = $origen;";

                borrar.Parameters.AddWithValue(
                    "$origen",
                    origenId);

                borrar.ExecuteNonQuery();
            }

            // Consolidar origen y enlaces en la ficha destino.
            using (SqliteCommand actualizar =
                   connection.CreateCommand())
            {
                actualizar.Transaction =
                    transaction;

                actualizar.CommandText =
                    """
                    UPDATE Personas
                    SET OrigenRegistro = $origen,
                        TrasladoAnteriorId = $ant,
                        TrasladoSiguienteId = $sig,
                        FechaTraslado = $fecha
                    WHERE Id = $destino;
                    """;

                actualizar.Parameters.AddWithValue(
                    "$origen",
                    combinado);

                actualizar.Parameters.AddWithValue(
                    "$ant",
                    antFinal);

                actualizar.Parameters.AddWithValue(
                    "$sig",
                    sigFinal);

                actualizar.Parameters.AddWithValue(
                    "$fecha",
                    fechaFinal);

                actualizar.Parameters.AddWithValue(
                    "$destino",
                    destinoId);

                actualizar.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }


    private static string LeerOrigen(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id)
    {
        return LeerTextoColumna(
            connection,
            transaction,
            id,
            "OrigenRegistro");
    }

    private static string LeerTextoColumna(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        string columna)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            transaction;

        cmd.CommandText =
            $"SELECT COALESCE({columna}, '') FROM Personas WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        object? valor =
            cmd.ExecuteScalar();

        return valor is null
               ||
               valor == DBNull.Value
            ? ""
            : valor.ToString() ?? "";
    }

    private static int LeerEntero(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        string columna)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            transaction;

        cmd.CommandText =
            $"SELECT COALESCE({columna}, 0) FROM Personas WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        object? valor =
            cmd.ExecuteScalar();

        return valor is null
               ||
               valor == DBNull.Value
            ? 0
            : Convert.ToInt32(valor);
    }

    private static string Texto(
        SqliteDataReader reader,
        int indice,
        string porDefecto)
    {
        return reader.IsDBNull(indice)
            ? porDefecto
            : reader.GetValue(indice)?.ToString()
              ?? porDefecto;
    }

    private static int Entero(
        SqliteDataReader reader,
        int indice)
    {
        return reader.IsDBNull(indice)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(indice));
    }
}
