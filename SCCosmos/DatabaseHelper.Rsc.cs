using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    // =========================================================
    // RSC · PERSISTENCIA (WINDOWS)
    //
    // Cuatro tablas con Id GUID de texto (como las escuelas):
    // así varios dispositivos pueden crear y editar RSC a la
    // vez sin que los identificadores choquen al sincronizar.
    //
    //   RSC_Reuniones   (la reunión: congregación, fecha, turno)
    //   RSC_Asistentes  (quién asiste, sale, acepta grupo, arreglo)
    //   RSC_Grupos      (los grupos formados)
    //   RSC_Miembros    (qué persona va en cada grupo)
    //
    // Cada asistente y cada miembro guarda también el NOMBRE
    // (por si la ficha se elimina después) y el SC y su esposa
    // van con PersonaId negativo, porque no son publicadores
    // locales: no tienen ficha en Personas.
    //
    // El territorio de una persona se lee de TerritoriosVisitaV3
    // (con el mapeo S-13 → S-21) para poder repartirlo entre los
    // grupos.
    // =========================================================

    public static partial class DatabaseHelper
    {
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


        private static void AsegurarTablasRscInterno(
            SqliteConnection connection)
        {
            EjecutarQuery(connection, SqlTablaRscReuniones);
            EjecutarQuery(connection, SqlTablaRscAsistentes);
            EjecutarQuery(connection, SqlTablaRscGrupos);
            EjecutarQuery(connection, SqlTablaRscMiembros);

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


        public static void AsegurarTablasRsc()
        {
            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);
            }
            catch
            {
            }
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


        private static RscReunion LeerReunionRsc(SqliteDataReader lector)
        {
            return new RscReunion
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


        public static List<RscReunion> ObtenerTodasReunionesRsc(
            int limite = 300)
        {
            var lista = new List<RscReunion>();

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                var cmd = connection.CreateCommand();

                cmd.CommandText =
                    SqlRscReunionSelect +
                    " ORDER BY r.Fecha DESC, r.Turno ASC LIMIT $limite;";

                cmd.Parameters.AddWithValue("$limite", limite);

                using var lector = cmd.ExecuteReader();

                while (lector.Read())
                {
                    lista.Add(LeerReunionRsc(lector));
                }
            }
            catch
            {
            }

            return lista;
        }


        public static List<RscReunion> ObtenerReunionesRscPorFecha(
            string fecha)
        {
            var lista = new List<RscReunion>();

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                var cmd = connection.CreateCommand();

                cmd.CommandText =
                    SqlRscReunionSelect +
                    " WHERE r.Fecha = $fecha ORDER BY r.Turno ASC;";

                cmd.Parameters.AddWithValue(
                    "$fecha",
                    fecha ?? "");

                using var lector = cmd.ExecuteReader();

                while (lector.Read())
                {
                    lista.Add(LeerReunionRsc(lector));
                }
            }
            catch
            {
            }

            return lista;
        }


        public static RscReunion ObtenerReunionRsc(
            string congreCodigo,
            string fecha,
            string turno)
        {
            RscReunion reunion = null;

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                var cmd = connection.CreateCommand();

                cmd.CommandText =
                    SqlRscReunionSelect +
                    @" WHERE r.CongregacionCodigo = $codigo
                          AND r.Fecha = $fecha
                          AND r.Turno = $turno
                        ORDER BY r.Id DESC
                        LIMIT 1;";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo ?? "");
                cmd.Parameters.AddWithValue("$fecha", fecha ?? "");
                cmd.Parameters.AddWithValue("$turno", turno ?? "");

                using var lector = cmd.ExecuteReader();

                if (lector.Read())
                {
                    reunion = LeerReunionRsc(lector);
                }
            }
            catch
            {
            }

            return reunion;
        }


        /// <summary>Guarda (alta o edición) y devuelve el Id GUID.</summary>
        public static string GuardarReunionRsc(RscReunion reunion)
        {
            if (reunion == null)
            {
                return "";
            }

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                if (string.IsNullOrWhiteSpace(reunion.Id))
                {
                    reunion.Id = Guid.NewGuid().ToString("N");

                    reunion.FechaCreacion =
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    var insertar = connection.CreateCommand();

                    insertar.CommandText = @"
                        INSERT INTO RSC_Reuniones
                            (Id, CongregacionCodigo, Fecha, Turno,
                             Notas, FechaCreacion)
                        VALUES
                            ($id, $codigo, $fecha, $turno,
                             $notas, $creacion);";

                    insertar.Parameters.AddWithValue("$id", reunion.Id);
                    insertar.Parameters.AddWithValue(
                        "$codigo", reunion.CongregacionCodigo ?? "");
                    insertar.Parameters.AddWithValue(
                        "$fecha", reunion.Fecha ?? "");
                    insertar.Parameters.AddWithValue(
                        "$turno", reunion.Turno ?? "");
                    insertar.Parameters.AddWithValue(
                        "$notas", reunion.Notas ?? "");
                    insertar.Parameters.AddWithValue(
                        "$creacion", reunion.FechaCreacion);

                    insertar.ExecuteNonQuery();
                }
                else
                {
                    var actualizar = connection.CreateCommand();

                    actualizar.CommandText = @"
                        UPDATE RSC_Reuniones
                        SET CongregacionCodigo = $codigo,
                            Fecha = $fecha,
                            Turno = $turno,
                            Notas = $notas
                        WHERE Id = $id;";

                    actualizar.Parameters.AddWithValue(
                        "$codigo", reunion.CongregacionCodigo ?? "");
                    actualizar.Parameters.AddWithValue(
                        "$fecha", reunion.Fecha ?? "");
                    actualizar.Parameters.AddWithValue(
                        "$turno", reunion.Turno ?? "");
                    actualizar.Parameters.AddWithValue(
                        "$notas", reunion.Notas ?? "");
                    actualizar.Parameters.AddWithValue("$id", reunion.Id);

                    actualizar.ExecuteNonQuery();
                }
            }
            catch
            {
            }

            return reunion.Id;
        }


        public static void EliminarReunionRsc(string reunionId)
        {
            if (string.IsNullOrWhiteSpace(reunionId))
            {
                return;
            }

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                using var transaccion = connection.BeginTransaction();

                foreach (string sql in new[]
                         {
                             "DELETE FROM RSC_Miembros WHERE RscReunionId = $id;",
                             "DELETE FROM RSC_Grupos WHERE RscReunionId = $id;",
                             "DELETE FROM RSC_Asistentes WHERE RscReunionId = $id;",
                             "DELETE FROM RSC_Reuniones WHERE Id = $id;"
                         })
                {
                    var cmd = connection.CreateCommand();

                    cmd.Transaction = transaccion;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("$id", reunionId);

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

        public static List<RscPersona> ObtenerAsistentesRsc(
            string reunionId)
        {
            var lista = new List<RscPersona>();

            if (string.IsNullOrWhiteSpace(reunionId))
            {
                return lista;
            }

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                var cmd = connection.CreateCommand();

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

                cmd.Parameters.AddWithValue("$id", reunionId);

                using var lector = cmd.ExecuteReader();

                while (lector.Read())
                {
                    lista.Add(new RscPersona
                    {
                        PersonaId = lector.GetInt32(0),
                        Nombre = lector.IsDBNull(1)
                            ? ""
                            : lector.GetString(1),
                        GrupoPredicacion = lector.IsDBNull(2)
                            ? 0
                            : lector.GetInt32(2),
                        Asiste = !lector.IsDBNull(3) &&
                                 lector.GetInt32(3) == 1,
                        SaleAPredicar = !lector.IsDBNull(4) &&
                                        lector.GetInt32(4) == 1,
                        AceptaGrupo = lector.IsDBNull(5) ||
                                      lector.GetInt32(5) == 1,
                        ArregloConPersonaIds = ParsearIdsRsc(
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


        private static void GuardarAsistentesRsc(
            SqliteConnection connection,
            string reunionId,
            IEnumerable<RscPersona> personas)
        {
            var borrar = connection.CreateCommand();

            borrar.CommandText =
                "DELETE FROM RSC_Asistentes WHERE RscReunionId = $id;";

            borrar.Parameters.AddWithValue("$id", reunionId);

            borrar.ExecuteNonQuery();

            foreach (var persona in
                     personas ?? Enumerable.Empty<RscPersona>())
            {
                if (persona == null)
                {
                    continue;
                }

                var cmd = connection.CreateCommand();

                cmd.CommandText = @"
                    INSERT INTO RSC_Asistentes
                        (Id, RscReunionId, PersonaId, Nombre,
                         Asiste, SaleAPredicar, AceptaGrupo,
                         ArregloConPersonaIds, Sexo, Notas)
                    VALUES
                        ($id, $reunion, $persona, $nombre,
                         $asiste, $sale, $acepta,
                         $arreglos, $sexo, $notas);";

                cmd.Parameters.AddWithValue(
                    "$id",
                    Guid.NewGuid().ToString("N"));

                cmd.Parameters.AddWithValue("$reunion", reunionId);
                cmd.Parameters.AddWithValue("$persona", persona.PersonaId);

                cmd.Parameters.AddWithValue(
                    "$nombre",
                    persona.Nombre ?? "");

                cmd.Parameters.AddWithValue(
                    "$asiste",
                    persona.Asiste ? 1 : 0);

                cmd.Parameters.AddWithValue(
                    "$sale",
                    persona.SaleAPredicar ? 1 : 0);

                cmd.Parameters.AddWithValue(
                    "$acepta",
                    persona.AceptaGrupo ? 1 : 0);

                cmd.Parameters.AddWithValue(
                    "$arreglos",
                    SerializarIdsRsc(persona.ArregloConPersonaIds));

                cmd.Parameters.AddWithValue(
                    "$sexo",
                    persona.Sexo ?? "");

                cmd.Parameters.AddWithValue(
                    "$notas",
                    persona.Notas ?? "");

                cmd.ExecuteNonQuery();
            }
        }


        // =====================================================
        // GRUPOS
        // =====================================================

        public static List<RscGrupo> ObtenerGruposRsc(
            string reunionId)
        {
            var grupos = new List<RscGrupo>();

            if (string.IsNullOrWhiteSpace(reunionId))
            {
                return grupos;
            }

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                var cmd = connection.CreateCommand();

                cmd.CommandText = @"
                    SELECT Id, Numero, Notas, COALESCE(Territorio, '')
                    FROM RSC_Grupos
                    WHERE RscReunionId = $id
                    ORDER BY Orden ASC, Numero ASC;";

                cmd.Parameters.AddWithValue("$id", reunionId);

                using (var lector = cmd.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        grupos.Add(new RscGrupo
                        {
                            Id = lector.IsDBNull(0)
                                ? ""
                                : lector.GetString(0),
                            RscReunionId = reunionId,
                            Numero = lector.IsDBNull(1)
                                ? 0
                                : lector.GetInt32(1),
                            Notas = lector.IsDBNull(2)
                                ? ""
                                : lector.GetString(2),
                            Territorio = lector.IsDBNull(3)
                                ? ""
                                : lector.GetString(3)
                        });
                    }
                }

                // Miembros de cada grupo.
                var miembros = connection.CreateCommand();

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

                miembros.Parameters.AddWithValue("$id", reunionId);

                using (var lector = miembros.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        string grupoId = lector.IsDBNull(0)
                            ? ""
                            : lector.GetString(0);

                        var grupo = grupos
                            .FirstOrDefault(g => g.Id == grupoId);

                        if (grupo == null)
                        {
                            continue;
                        }

                        grupo.Miembros.Add(new RscPersona
                        {
                            PersonaId = lector.GetInt32(1),
                            Nombre = lector.IsDBNull(2)
                                ? ""
                                : lector.GetString(2),
                            GrupoPredicacion = lector.IsDBNull(3)
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

            RscMotor.Renumerar(grupos);

            return grupos;
        }


        private static void GuardarGruposRsc(
            SqliteConnection connection,
            string reunionId,
            IEnumerable<RscGrupo> grupos)
        {
            foreach (string sql in new[]
                     {
                         "DELETE FROM RSC_Miembros WHERE RscReunionId = $id;",
                         "DELETE FROM RSC_Grupos WHERE RscReunionId = $id;"
                     })
            {
                var borrar = connection.CreateCommand();

                borrar.CommandText = sql;
                borrar.Parameters.AddWithValue("$id", reunionId);

                borrar.ExecuteNonQuery();
            }

            int ordenGrupo = 0;

            foreach (var grupo in
                     grupos ?? Enumerable.Empty<RscGrupo>())
            {
                if (grupo == null || grupo.Miembros.Count == 0)
                {
                    continue;
                }

                string grupoId = Guid.NewGuid().ToString("N");

                var insertarGrupo = connection.CreateCommand();

                insertarGrupo.CommandText = @"
                    INSERT INTO RSC_Grupos
                        (Id, RscReunionId, Numero, Notas,
                         Territorio, Orden)
                    VALUES
                        ($id, $reunion, $numero, $notas,
                         $territorio, $orden);";

                insertarGrupo.Parameters.AddWithValue("$id", grupoId);
                insertarGrupo.Parameters.AddWithValue("$reunion", reunionId);

                insertarGrupo.Parameters.AddWithValue(
                    "$numero",
                    grupo.Numero);

                insertarGrupo.Parameters.AddWithValue(
                    "$notas",
                    grupo.Notas ?? "");

                insertarGrupo.Parameters.AddWithValue(
                    "$territorio",
                    grupo.Territorio ?? "");

                insertarGrupo.Parameters.AddWithValue(
                    "$orden",
                    ordenGrupo);

                insertarGrupo.ExecuteNonQuery();

                ordenGrupo++;

                int ordenMiembro = 0;

                foreach (var miembro in grupo.Miembros)
                {
                    if (miembro == null)
                    {
                        continue;
                    }

                    var insertarMiembro = connection.CreateCommand();

                    insertarMiembro.CommandText = @"
                        INSERT INTO RSC_Miembros
                            (Id, RscGrupoId, RscReunionId, PersonaId,
                             Nombre, Orden)
                        VALUES
                            ($id, $grupo, $reunion, $persona,
                             $nombre, $orden);";

                    insertarMiembro.Parameters.AddWithValue(
                        "$id",
                        Guid.NewGuid().ToString("N"));

                    insertarMiembro.Parameters.AddWithValue(
                        "$grupo",
                        grupoId);

                    insertarMiembro.Parameters.AddWithValue(
                        "$reunion",
                        reunionId);

                    insertarMiembro.Parameters.AddWithValue(
                        "$persona",
                        miembro.PersonaId);

                    insertarMiembro.Parameters.AddWithValue(
                        "$nombre",
                        miembro.Nombre ?? "");

                    insertarMiembro.Parameters.AddWithValue(
                        "$orden",
                        ordenMiembro);

                    insertarMiembro.ExecuteNonQuery();

                    ordenMiembro++;
                }
            }
        }


        /// <summary>
        /// Guarda la RSC completa (reunión + asistentes + grupos)
        /// y devuelve el Id GUID.
        /// </summary>
        public static string GuardarRscCompleta(
            RscReunion reunion,
            IEnumerable<RscPersona> personas,
            IEnumerable<RscGrupo> grupos)
        {
            if (reunion == null)
            {
                return "";
            }

            string reunionId = GuardarReunionRsc(reunion);

            if (string.IsNullOrWhiteSpace(reunionId))
            {
                return "";
            }

            try
            {
                using var connection =
                    new SqliteConnection(ConnectionString);

                connection.Open();

                AsegurarTablasRscInterno(connection);

                using var transaccion = connection.BeginTransaction();

                GuardarAsistentesRsc(
                    connection,
                    reunionId,
                    personas);

                GuardarGruposRsc(
                    connection,
                    reunionId,
                    grupos);

                transaccion.Commit();
            }
            catch
            {
            }

            return reunionId;
        }


        /// <summary>
        /// Grupos ya guardados, formateados para el buscador por día.
        /// </summary>
        public static List<RscGrupoVista> ObtenerGruposVistaRsc(
            string reunionId)
        {
            var vistas = new List<RscGrupoVista>();

            foreach (var grupo in ObtenerGruposRsc(reunionId))
            {
                var vista = new RscGrupoVista
                {
                    Titulo = grupo.Titulo,
                    Subtitulo = grupo.TieneTerritorio
                        ? "Territorio: " + grupo.Territorio
                        : grupo.TerritoriosMiembros,
                    Notas = grupo.Notas,
                    Miembros = grupo.Miembros
                        .Select(m => m.Nombre)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList()
                };

                vistas.Add(vista);
            }

            return vistas;
        }


        // =====================================================
        // TERRITORIOS DE LAS PERSONAS (solo lectura)
        //
        // El territorio vive en TerritoriosVisitaV3 y viene del
        // S-13: se reconoce al publicador por nombre vinculado o
        // por el mapeo S-13 → S-21.
        // =====================================================

        public static Dictionary<int, string> ObtenerTerritoriosPorPersona(
            string congreCodigo)
        {
            var resultado = new Dictionary<int, string>();

            try
            {
                var personas = ObtenerPersonasPorCongregacion(congreCodigo);

                if (personas.Count == 0)
                {
                    return resultado;
                }

                var territorios = ObtenerTerritoriosPorCongregacion(congreCodigo);

                // Mapeo S-13 → S-21 (nombre del territorio → publicador).
                var mapeo = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

                try
                {
                    using var conexion =
                        new SqliteConnection(ConnectionString);

                    conexion.Open();

                    var cmd = conexion.CreateCommand();

                    cmd.CommandText = @"
                        SELECT NombreS13, NombreS21
                        FROM MapeoS13S21
                        WHERE CongregacionCodigo = $codigo;";

                    cmd.Parameters.AddWithValue("$codigo", congreCodigo);

                    using var lector = cmd.ExecuteReader();

                    while (lector.Read())
                    {
                        string s13 = lector.IsDBNull(0)
                            ? ""
                            : lector.GetString(0);

                        string s21 = lector.IsDBNull(1)
                            ? ""
                            : lector.GetString(1);

                        if (!string.IsNullOrWhiteSpace(s13))
                        {
                            mapeo[Normalizar(s13)] = s21;
                        }
                    }
                }
                catch
                {
                }

                var porNombre = new Dictionary<string, Persona>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var persona in personas)
                {
                    foreach (string nombre in new[]
                             {
                                 persona.Nombre,
                                 persona.NombreComun,
                                 persona.NombreJWHub
                             })
                    {
                        string clave = Normalizar(nombre);

                        if (clave.Length > 0 &&
                            !porNombre.ContainsKey(clave))
                        {
                            porNombre[clave] = persona;
                        }
                    }
                }

                foreach (var territorio in territorios)
                {
                    if (territorio == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            territorio.FechaEliminacion))
                    {
                        continue;
                    }

                    var candidatos = new List<string>
                    {
                        territorio.NombreVinculado,
                        territorio.AsignadoA
                    };

                    // Si el nombre venía del S-13, probar su equivalente.
                    string asignado = Normalizar(territorio.AsignadoA);

                    if (asignado.Length > 0 &&
                        mapeo.TryGetValue(asignado, out string vinculado) &&
                        !string.IsNullOrWhiteSpace(vinculado))
                    {
                        candidatos.Insert(0, vinculado);
                    }

                    foreach (string candidato in candidatos)
                    {
                        string clave = Normalizar(candidato);

                        if (clave.Length == 0)
                        {
                            continue;
                        }

                        if (porNombre.TryGetValue(clave, out Persona persona))
                        {
                            string nombreTerritorio =
                                string.IsNullOrWhiteSpace(territorio.Nombre)
                                    ? ""
                                    : territorio.Nombre.Trim();

                            if (nombreTerritorio.Length > 0 &&
                                !resultado.ContainsKey(persona.Id))
                            {
                                resultado[persona.Id] = nombreTerritorio;
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
        //
        // En ProgramaVisitaV2 se apunta qué harán el SC y su
        // esposa cada día de la visita (predicación, comidas…)
        // y con quién van (CompaneroId). Si ese día ya tienen
        // un arreglo de predicación, aquí se detecta para poder
        // avisarlo al preparar la RSC.
        // =====================================================

        public static List<RscArregloDetectado> ObtenerArreglosDelDia(
            string congreCodigo,
            DateTime fecha,
            string turnoRsc)
        {
            var lista = new List<RscArregloDetectado>();

            try
            {
                string nombreSc =
                    Normalizar(ObtenerConfiguracion("NombreSC", ""));

                string nombreEsposa =
                    Normalizar(ObtenerConfiguracion("NombreEsposaSC", ""));

                string dia = NombreDiaEspanol(fecha.DayOfWeek);
                string mes = fecha.ToString("yyyy-MM");

                using var conexion =
                    new SqliteConnection(ConnectionString);

                conexion.Open();

                AsegurarTablasRscInterno(conexion);

                var cmd = conexion.CreateCommand();

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

                cmd.Parameters.AddWithValue("$mes", mes);
                cmd.Parameters.AddWithValue("$dia", dia);

                // El turno se compara en C#: UPPER() de SQLite no
                // conoce la ñ ("MAÑANA" no cuadraría con "Mañana").
                string turnoNormalizado =
                    Normalizar(turnoRsc);

                var vistos = new HashSet<string>();

                using var lector = cmd.ExecuteReader();

                while (lector.Read())
                {
                    string responsable = lector.IsDBNull(0)
                        ? ""
                        : lector.GetString(0);

                    int companero = lector.IsDBNull(1)
                        ? 0
                        : lector.GetInt32(1);

                    string turno = lector.IsDBNull(2)
                        ? ""
                        : lector.GetString(2);

                    string hora = lector.IsDBNull(3)
                        ? ""
                        : lector.GetString(3);

                    string tipo = lector.IsDBNull(4)
                        ? ""
                        : lector.GetString(4);

                    string nombreCompanero = lector.IsDBNull(5)
                        ? ""
                        : lector.GetString(5);

                    if (companero <= 0)
                    {
                        continue;
                    }

                    string turnoFila = Normalizar(turno);

                    bool turnoOk =
                        turnoFila.Length == 0
                        || turnoFila == "general"
                        || turnoNormalizado.Length == 0
                        || turnoFila == turnoNormalizado;

                    if (!turnoOk)
                    {
                        continue;
                    }

                    string normalizado = Normalizar(responsable);

                    bool ambos =
                        normalizado.Contains("ambos")
                        || normalizado.Contains("juntos");

                    bool esSc =
                        ambos
                        || normalizado == "sc"
                        || normalizado.Contains("circuito")
                        || normalizado.Contains("superintendente")
                        || PareceNombre(normalizado, nombreSc);

                    bool esEsposa =
                        ambos
                        || normalizado.Contains("esposa")
                        || PareceNombre(normalizado, nombreEsposa);

                    if (!esSc && !esEsposa)
                    {
                        continue;
                    }

                    string clave =
                        $"{(esSc ? "S" : "")}{(esEsposa ? "E" : "")}|{companero}";

                    if (vistos.Contains(clave))
                    {
                        continue;
                    }

                    vistos.Add(clave);

                    if (esSc)
                    {
                        lista.Add(new RscArregloDetectado
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
                        lista.Add(new RscArregloDetectado
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


        /// <summary>Ids separados por comas: "536,511".</summary>
        private static List<int> ParsearIdsRsc(
            string texto)
        {
            var ids = new List<int>();

            if (string.IsNullOrWhiteSpace(texto))
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


        private static string SerializarIdsRsc(
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


        private static bool PareceNombre(
            string corto,
            string completo)
        {
            if (string.IsNullOrWhiteSpace(corto)
                || string.IsNullOrWhiteSpace(completo)
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

            // Prefijo común de 3 o más letras: Joni → Jonatán,
            // Rebe → Rebeca.
            int comun = 0;

            while (comun < corto.Length
                   && comun < primerNombre.Length
                   && corto[comun] == primerNombre[comun])
            {
                comun++;
            }

            return comun >= 3;
        }


        private static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return "";
            }

            string descompuesto = texto
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD);

            var constructor = new StringBuilder();

            foreach (char caracter in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter) !=
                    UnicodeCategory.NonSpacingMark)
                {
                    constructor.Append(caracter);
                }
            }

            return constructor
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace("  ", " ")
                .Trim();
        }


        private static string NombreDiaEspanol(DayOfWeek dia)
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
}
