using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    public static class DatabaseHelper
    {
        public static string RutaDirectorioDatos
        {
            get
            {
                string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos", "rutadb.txt");
                if (System.IO.File.Exists(configPath)) return System.IO.File.ReadAllText(configPath);
                return @"C:\Users\Usuario\Documents\2 - ESP-BA-01\8 - Otros\SCCosmos";
            }
            set
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rutadb.txt"), value);
            }
        }

        private static string ConnectionString
        {
            get
            {
                if (!System.IO.Directory.Exists(RutaDirectorioDatos)) System.IO.Directory.CreateDirectory(RutaDirectorioDatos);
                string rutaDb = System.IO.Path.Combine(RutaDirectorioDatos, "sccosmos.db");
                return $"Data Source={rutaDb}";
            }
        }

        public static void InicializarDatabase()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                ActualizarTablaNotasVisita();

                var cmd1 = connection.CreateCommand();
                cmd1.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Congregaciones (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, Codigo TEXT, Nombre TEXT NOT NULL, Email TEXT,
                        SeccionId INTEGER DEFAULT 0, DireccionSR TEXT, HoraEntreSemana TEXT, DiaFinSemana TEXT, HoraFinSemana TEXT
                    );";
                cmd1.ExecuteNonQuery();

                var cmd3 = connection.CreateCommand();
                cmd3.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Personas (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT NOT NULL, FechaNacimiento TEXT, Sexo TEXT,
                        FechaBautismo TEXT, Esperanza TEXT, Nombramiento TEXT, Servicio TEXT, CongregacionCodigo TEXT,
                        Grupo INTEGER DEFAULT 0, Telefono TEXT, Email TEXT, FechaInicioPrec TEXT, FechaUltimaEscuela TEXT,
                        NombreJWHub TEXT, Foto TEXT, UNIQUE(Nombre, CongregacionCodigo)
                    );";
                cmd3.ExecuteNonQuery();

                var cmd4 = connection.CreateCommand();
                cmd4.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ActividadPersonas (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, PersonaId INTEGER, Mes TEXT, AnioServicio TEXT,
                        Participacion INTEGER, CursosBiblicos INTEGER, PrecursorAuxiliar INTEGER, Horas INTEGER, Notas TEXT,
                        UNIQUE(PersonaId, Mes, AnioServicio), FOREIGN KEY(PersonaId) REFERENCES Personas(Id) ON DELETE CASCADE
                    );";
                cmd4.ExecuteNonQuery();

                var cmdTerritoriosV3 = connection.CreateCommand();
                cmdTerritoriosV3.CommandText = @"
                CREATE TABLE IF NOT EXISTS TerritoriosVisitaV3 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, Nombre TEXT, UltimaFechaCompletado TEXT,
                    AsignadoA TEXT, FechaAsignado TEXT, FechaCompletado TEXT, NombreVinculado TEXT, FechaCreacion TEXT
                );";
                cmdTerritoriosV3.ExecuteNonQuery();

                var cmdMapeo = connection.CreateCommand();
                cmdMapeo.CommandText = "CREATE TABLE IF NOT EXISTS MapeoS13S21 (Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, NombreS13 TEXT, NombreS21 TEXT);";
                cmdMapeo.ExecuteNonQuery();

                var cmdPrograma = connection.CreateCommand();
                cmdPrograma.CommandText = @"
                CREATE TABLE IF NOT EXISTS ProgramaVisitaV2 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, CongreCodigo TEXT NOT NULL, MesReferencia TEXT NOT NULL, DiaSemana TEXT NOT NULL,
                    Turno TEXT NOT NULL, Responsable TEXT NOT NULL, TipoActividad TEXT, Hora TEXT, NotasLibro TEXT, CompaneroId INTEGER
                );";
                cmdPrograma.ExecuteNonQuery();

                var cmdConta = connection.CreateCommand();
                cmdConta.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ContabilidadMes (
                        CongreCodigo TEXT, MesReferencia TEXT, Gastos REAL, Mantenimiento REAL, ObraMundial REAL, ObraResolucion REAL, Saldo REAL,
                        PRIMARY KEY(CongreCodigo, MesReferencia)
                    );";
                cmdConta.ExecuteNonQuery();

                var cmdDoc = connection.CreateCommand();
                cmdDoc.CommandText = "CREATE TABLE IF NOT EXISTS Documentos (Id INTEGER PRIMARY KEY AUTOINCREMENT, Categoria TEXT, NombreArchivo TEXT, RutaLocal TEXT);";
                cmdDoc.ExecuteNonQuery();

                var cmdConf = connection.CreateCommand();
                cmdConf.CommandText = "CREATE TABLE IF NOT EXISTS Configuracion (Clave TEXT PRIMARY KEY, Valor TEXT);";
                cmdConf.ExecuteNonQuery();

                var cmdSecciones = connection.CreateCommand();
                cmdSecciones.CommandText = "CREATE TABLE IF NOT EXISTS SeccionesCircuito (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT, LugarAsamblea TEXT);";
                cmdSecciones.ExecuteNonQuery();

                var cmdSemanas = connection.CreateCommand();
                cmdSemanas.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Semanas (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, FechaMartes DATETIME, Actividad TEXT, Congregacion TEXT, Tipo TEXT, Acompanante TEXT, Alojamiento TEXT,
                        S302Enviado INTEGER DEFAULT 0, DocEnviada INTEGER DEFAULT 0, SeccionAsamblea TEXT, FechaAsamblea TEXT, NotificacionEnviada INTEGER DEFAULT 0, RecordatorioEnviado INTEGER DEFAULT 0,
                        DatosImportados INTEGER DEFAULT 0
                    );";
                cmdSemanas.ExecuteNonQuery();

                // Parches automáticos para actualizar la BD sin romperla
                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE Semanas ADD COLUMN FechaAsamblea TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE Semanas ADD COLUMN DatosImportados INTEGER DEFAULT 0;"; cmdAlt.ExecuteNonQuery(); } catch { }
            }
        }

        public static string ObtenerCodigoCongregacion(string nombreCongregacion)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Codigo FROM Congregaciones WHERE Nombre = $nombre;";
                command.Parameters.AddWithValue("$nombre", nombreCongregacion);
                var result = command.ExecuteScalar();
                return result?.ToString() ?? string.Empty;
            }
        }

        public static void GuardarActividadImportada(string congreCodigo, List<PersonaJson> listaPersonas)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var p in listaPersonas)
                    {
                        long personaId = 0;
                        var cmdCheck = connection.CreateCommand();
                        cmdCheck.Transaction = transaction;
                        cmdCheck.CommandText = "SELECT Id FROM Personas WHERE Nombre = $nombre AND CongregacionCodigo = $congreCodigo;";
                        cmdCheck.Parameters.AddWithValue("$nombre", p.Nombre);
                        cmdCheck.Parameters.AddWithValue("$congreCodigo", congreCodigo);
                        var idObj = cmdCheck.ExecuteScalar();

                        if (idObj != null)
                        {
                            personaId = (long)idObj;
                            var cmdUpdate = connection.CreateCommand();
                            cmdUpdate.Transaction = transaction;
                            cmdUpdate.CommandText = @"
                                UPDATE Personas SET 
                                    FechaNacimiento = $fechaNacimiento, Sexo = $sexo, FechaBautismo = $fechaBautismo, 
                                    Esperanza = $esperanza, Nombramiento = $nombramiento, Servicio = $servicio, Grupo = $grupo 
                                WHERE Id = $id;";
                            cmdUpdate.Parameters.AddWithValue("$grupo", p.Grupo);
                            cmdUpdate.Parameters.AddWithValue("$id", personaId);
                            cmdUpdate.Parameters.AddWithValue("$fechaNacimiento", p.FechaNacimiento ?? "");
                            cmdUpdate.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                            cmdUpdate.Parameters.AddWithValue("$fechaBautismo", p.FechaBautismo ?? "");
                            cmdUpdate.Parameters.AddWithValue("$esperanza", p.Esperanza ?? "");
                            cmdUpdate.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                            cmdUpdate.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                            cmdUpdate.ExecuteNonQuery();
                        }
                        else
                        {
                            var cmdInsert = connection.CreateCommand();
                            cmdInsert.Transaction = transaction;
                            cmdInsert.CommandText = @"
                                INSERT INTO Personas (Nombre, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Nombramiento, Servicio, CongregacionCodigo, Grupo)
                                VALUES ($nombre, $fechaNacimiento, $sexo, $fechaBautismo, $esperanza, $nombramiento, $servicio, $congreCodigo, $grupo);
                                SELECT last_insert_rowid();";
                            cmdInsert.Parameters.AddWithValue("$grupo", p.Grupo);
                            cmdInsert.Parameters.AddWithValue("$nombre", p.Nombre);
                            cmdInsert.Parameters.AddWithValue("$fechaNacimiento", p.FechaNacimiento ?? "");
                            cmdInsert.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                            cmdInsert.Parameters.AddWithValue("$fechaBautismo", p.FechaBautismo ?? "");
                            cmdInsert.Parameters.AddWithValue("$esperanza", p.Esperanza ?? "");
                            cmdInsert.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                            cmdInsert.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                            cmdInsert.Parameters.AddWithValue("$congreCodigo", congreCodigo);
                            personaId = (long)cmdInsert.ExecuteScalar();
                        }

                        if (p.Informe != null)
                        {
                            foreach (var inf in p.Informe)
                            {
                                var cmdInf = connection.CreateCommand();
                                cmdInf.Transaction = transaction;
                                cmdInf.CommandText = @"
                                    INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Participacion, CursosBiblicos, PrecursorAuxiliar, Horas, Notas)
                                    VALUES ($personaId, $mes, $anio, $part, $cursos, $prec, $horas, $notas)
                                    ON CONFLICT(PersonaId, Mes, AnioServicio) DO UPDATE SET
                                        Participacion = $part, CursosBiblicos = $cursos, PrecursorAuxiliar = $prec, Horas = $horas, Notas = $notas;";
                                cmdInf.Parameters.AddWithValue("$personaId", personaId);
                                cmdInf.Parameters.AddWithValue("$mes", inf.Mes ?? "");
                                cmdInf.Parameters.AddWithValue("$anio", p.AnioServicio ?? "");
                                cmdInf.Parameters.AddWithValue("$part", inf.Participacion ? 1 : 0);
                                int cursosEnteros = 0; if (!string.IsNullOrWhiteSpace(inf.CursosBiblicos?.ToString())) int.TryParse(inf.CursosBiblicos.ToString(), out cursosEnteros);
                                cmdInf.Parameters.AddWithValue("$cursos", cursosEnteros);
                                cmdInf.Parameters.AddWithValue("$prec", inf.PrecursorAuxiliar ? 1 : 0);
                                int horasEnteras = 0; if (!string.IsNullOrWhiteSpace(inf.Horas?.ToString())) int.TryParse(inf.Horas.ToString(), out horasEnteras);
                                cmdInf.Parameters.AddWithValue("$horas", horasEnteras);
                                cmdInf.Parameters.AddWithValue("$notas", inf.Notas ?? "");
                                cmdInf.ExecuteNonQuery();
                            }
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public static void EliminarCongregacion(string codigo)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Congregaciones WHERE Codigo = $codigo;";
                command.Parameters.AddWithValue("$codigo", codigo);
                command.ExecuteNonQuery();
            }
        }

        public static List<SemanaCalendario> ObtenerTodasSemanasPorFecha(string fechaIso)
        {
            var list = new List<SemanaCalendario>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM Semanas WHERE date(FechaMartes) = date(@FechaIso) OR FechaMartes LIKE @FechaIso || '%' ORDER BY Id ASC";
                cmd.Parameters.AddWithValue("@FechaIso", fechaIso);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new SemanaCalendario
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            FechaMartes = reader.GetDateTime(reader.GetOrdinal("FechaMartes")),
                            Actividad = reader.IsDBNull(reader.GetOrdinal("Actividad")) ? "" : reader.GetString(reader.GetOrdinal("Actividad")),
                            Congregacion = reader.IsDBNull(reader.GetOrdinal("Congregacion")) ? "" : reader.GetString(reader.GetOrdinal("Congregacion")),
                            Tipo = reader.IsDBNull(reader.GetOrdinal("Tipo")) ? "" : reader.GetString(reader.GetOrdinal("Tipo")),
                            Acompanante = reader.IsDBNull(reader.GetOrdinal("Acompanante")) ? "" : reader.GetString(reader.GetOrdinal("Acompanante")),
                            Alojamiento = reader.IsDBNull(reader.GetOrdinal("Alojamiento")) ? "" : reader.GetString(reader.GetOrdinal("Alojamiento")),

                            S302Enviado = !reader.IsDBNull(reader.GetOrdinal("S302Enviado")) && reader.GetInt32(reader.GetOrdinal("S302Enviado")) == 1,
                            DocEnviada = !reader.IsDBNull(reader.GetOrdinal("DocEnviada")) && reader.GetInt32(reader.GetOrdinal("DocEnviada")) == 1,

                            SeccionAsamblea = reader.IsDBNull(reader.GetOrdinal("SeccionAsamblea")) ? "" : reader.GetString(reader.GetOrdinal("SeccionAsamblea")),
                            FechaAsamblea = reader.IsDBNull(reader.GetOrdinal("FechaAsamblea")) ? "" : reader.GetString(reader.GetOrdinal("FechaAsamblea")),
                            NotificacionEnviada = !reader.IsDBNull(reader.GetOrdinal("NotificacionEnviada")) && reader.GetInt32(reader.GetOrdinal("NotificacionEnviada")) == 1,
                            RecordatorioEnviado = !reader.IsDBNull(reader.GetOrdinal("RecordatorioEnviado")) && reader.GetInt32(reader.GetOrdinal("RecordatorioEnviado")) == 1,

                            TieneDatosImportados = !reader.IsDBNull(reader.GetOrdinal("DatosImportados")) && reader.GetInt32(reader.GetOrdinal("DatosImportados")) == 1,

                            EsClonada = Visibility.Collapsed
                        });
                    }
                }
            }
            return list;
        }

        public static void EliminarSemanaCalendario(int id)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Semanas WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public static SemanaCalendario ObtenerSemana(string fechaIso)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM Semanas WHERE date(FechaMartes) = date(@FechaIso) OR FechaMartes LIKE @FechaIso || '%' LIMIT 1";
                cmd.Parameters.AddWithValue("@FechaIso", fechaIso);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new SemanaCalendario
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            FechaMartes = reader.GetDateTime(reader.GetOrdinal("FechaMartes")),
                            Actividad = reader.IsDBNull(reader.GetOrdinal("Actividad")) ? "" : reader.GetString(reader.GetOrdinal("Actividad")),
                            Congregacion = reader.IsDBNull(reader.GetOrdinal("Congregacion")) ? "" : reader.GetString(reader.GetOrdinal("Congregacion")),
                            Tipo = reader.IsDBNull(reader.GetOrdinal("Tipo")) ? "" : reader.GetString(reader.GetOrdinal("Tipo")),
                            Acompanante = reader.IsDBNull(reader.GetOrdinal("Acompanante")) ? "" : reader.GetString(reader.GetOrdinal("Acompanante")),
                            Alojamiento = reader.IsDBNull(reader.GetOrdinal("Alojamiento")) ? "" : reader.GetString(reader.GetOrdinal("Alojamiento")),

                            S302Enviado = !reader.IsDBNull(reader.GetOrdinal("S302Enviado")) && reader.GetInt32(reader.GetOrdinal("S302Enviado")) == 1,
                            DocEnviada = !reader.IsDBNull(reader.GetOrdinal("DocEnviada")) && reader.GetInt32(reader.GetOrdinal("DocEnviada")) == 1,

                            SeccionAsamblea = reader.IsDBNull(reader.GetOrdinal("SeccionAsamblea")) ? "" : reader.GetString(reader.GetOrdinal("SeccionAsamblea")),
                            FechaAsamblea = reader.IsDBNull(reader.GetOrdinal("FechaAsamblea")) ? "" : reader.GetString(reader.GetOrdinal("FechaAsamblea")),
                            NotificacionEnviada = !reader.IsDBNull(reader.GetOrdinal("NotificacionEnviada")) && reader.GetInt32(reader.GetOrdinal("NotificacionEnviada")) == 1,
                            RecordatorioEnviado = !reader.IsDBNull(reader.GetOrdinal("RecordatorioEnviado")) && reader.GetInt32(reader.GetOrdinal("RecordatorioEnviado")) == 1,

                            TieneDatosImportados = !reader.IsDBNull(reader.GetOrdinal("DatosImportados")) && reader.GetInt32(reader.GetOrdinal("DatosImportados")) == 1
                        };
                    }
                }
            }
            return null;
        }

        public static void GuardarSemana(SemanaCalendario semana)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                if (semana.Id > 0)
                {
                    var cmdUpdate = connection.CreateCommand();
                    cmdUpdate.CommandText = @"
                        UPDATE Semanas SET 
                            Actividad = @Actividad, Congregacion = @Congregacion, Tipo = @Tipo, 
                            Acompanante = @Acompanante, Alojamiento = @Alojamiento, 
                            S302Enviado = @S302Enviado, DocEnviada = @DocEnviada,
                            SeccionAsamblea = @SeccionAsamblea, FechaAsamblea = @FechaAsamblea, NotificacionEnviada = @NotificacionEnviada, RecordatorioEnviado = @RecordatorioEnviado,
                            DatosImportados = @DatosImportados
                        WHERE Id = @Id";

                    cmdUpdate.Parameters.AddWithValue("@Id", semana.Id);
                    cmdUpdate.Parameters.AddWithValue("@Actividad", semana.Actividad ?? "");
                    cmdUpdate.Parameters.AddWithValue("@Congregacion", semana.Congregacion ?? "");
                    cmdUpdate.Parameters.AddWithValue("@Tipo", semana.Tipo ?? "");
                    cmdUpdate.Parameters.AddWithValue("@Acompanante", semana.Acompanante ?? "");
                    cmdUpdate.Parameters.AddWithValue("@Alojamiento", semana.Alojamiento ?? "");
                    cmdUpdate.Parameters.AddWithValue("@S302Enviado", semana.S302Enviado ? 1 : 0);
                    cmdUpdate.Parameters.AddWithValue("@DocEnviada", semana.DocEnviada ? 1 : 0);
                    cmdUpdate.Parameters.AddWithValue("@SeccionAsamblea", semana.SeccionAsamblea ?? "");
                    cmdUpdate.Parameters.AddWithValue("@FechaAsamblea", semana.FechaAsamblea ?? "");
                    cmdUpdate.Parameters.AddWithValue("@NotificacionEnviada", semana.NotificacionEnviada ? 1 : 0);
                    cmdUpdate.Parameters.AddWithValue("@RecordatorioEnviado", semana.RecordatorioEnviado ? 1 : 0);
                    cmdUpdate.Parameters.AddWithValue("@DatosImportados", semana.TieneDatosImportados ? 1 : 0);

                    cmdUpdate.ExecuteNonQuery();
                }
                else
                {
                    var cmdInsert = connection.CreateCommand();
                    cmdInsert.CommandText = @"
                        INSERT INTO Semanas 
                        (FechaMartes, Actividad, Congregacion, Tipo, Acompanante, Alojamiento, S302Enviado, DocEnviada, SeccionAsamblea, FechaAsamblea, NotificacionEnviada, RecordatorioEnviado, DatosImportados)
                        VALUES 
                        (@FechaMartes, @Actividad, @Congregacion, @Tipo, @Acompanante, @Alojamiento, @S302Enviado, @DocEnviada, @SeccionAsamblea, @FechaAsamblea, @NotificacionEnviada, @RecordatorioEnviado, @DatosImportados)";

                    cmdInsert.Parameters.AddWithValue("@FechaMartes", semana.FechaMartes);
                    cmdInsert.Parameters.AddWithValue("@Actividad", semana.Actividad ?? "");
                    cmdInsert.Parameters.AddWithValue("@Congregacion", semana.Congregacion ?? "");
                    cmdInsert.Parameters.AddWithValue("@Tipo", semana.Tipo ?? "");
                    cmdInsert.Parameters.AddWithValue("@Acompanante", semana.Acompanante ?? "");
                    cmdInsert.Parameters.AddWithValue("@Alojamiento", semana.Alojamiento ?? "");
                    cmdInsert.Parameters.AddWithValue("@S302Enviado", semana.S302Enviado ? 1 : 0);
                    cmdInsert.Parameters.AddWithValue("@DocEnviada", semana.DocEnviada ? 1 : 0);
                    cmdInsert.Parameters.AddWithValue("@SeccionAsamblea", semana.SeccionAsamblea ?? "");
                    cmdInsert.Parameters.AddWithValue("@FechaAsamblea", semana.FechaAsamblea ?? "");
                    cmdInsert.Parameters.AddWithValue("@NotificacionEnviada", semana.NotificacionEnviada ? 1 : 0);
                    cmdInsert.Parameters.AddWithValue("@RecordatorioEnviado", semana.RecordatorioEnviado ? 1 : 0);
                    cmdInsert.Parameters.AddWithValue("@DatosImportados", semana.TieneDatosImportados ? 1 : 0);

                    cmdInsert.ExecuteNonQuery();
                }
            }
        }

        public static bool TienePersonas(string congreCodigo)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Personas WHERE CongregacionCodigo = $codigo;";
                command.Parameters.AddWithValue("$codigo", congreCodigo);
                long count = (long)command.ExecuteScalar();
                return count > 0;
            }
        }

        public static List<Persona> ObtenerPersonas()
        {
            var lista = new List<Persona>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = @"
            SELECT p.Id, p.Nombre, p.FechaNacimiento, p.Sexo, p.FechaBautismo,
                   p.Esperanza, p.Nombramiento, p.Servicio, p.CongregacionCodigo, 
                   c.Nombre, p.Grupo, p.NombreJWHub, p.FechaCreacion, 
                   p.FechaEliminacion, p.Solicitudes, p.Privilegios, p.Foto 
            FROM Personas p
            LEFT JOIN Congregaciones c ON p.CongregacionCodigo = c.Codigo
            ORDER BY p.Nombre ASC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Persona
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            FechaNacimiento = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Sexo = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            FechaBautismo = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Esperanza = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Nombramiento = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            Servicio = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            CongregacionCodigo = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            CongregacionNombre = reader.IsDBNull(9) ? "Sin asignar" : reader.GetString(9),
                            Grupo = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                            NombreJWHub = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            FechaCreacion = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            FechaEliminacion = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            Solicitudes = reader.IsDBNull(14) ? "" : reader.GetString(14),
                            Privilegios = reader.IsDBNull(15) ? "" : reader.GetString(15),
                            Foto = reader.IsDBNull(16) ? "" : reader.GetString(16)
                        });
                    }
                }
            }
            return lista;
        }

        public static List<Persona> ObtenerPersonasPorCongregacion(string congreCodigo)
        {
            var lista = new List<Persona>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Nombre, Nombramiento, Servicio, Grupo, FechaNacimiento, Sexo, FechaBautismo, NombreJWHub, FechaCreacion, FechaEliminacion FROM Personas WHERE CongregacionCodigo = $codigo ORDER BY Nombre ASC;";
                command.Parameters.AddWithValue("$codigo", congreCodigo);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Persona
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Nombramiento = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Servicio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Grupo = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            FechaNacimiento = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Sexo = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            FechaBautismo = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            NombreJWHub = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            FechaCreacion = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            FechaEliminacion = reader.IsDBNull(10) ? "" : reader.GetString(10)
                        });
                    }
                }
            }
            return lista;
        }

        public static void ActualizarPersona(Persona p)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Personas SET 
                        Nombre = $nombre, FechaNacimiento = $fechaNacimiento, Sexo = $sexo, 
                        FechaBautismo = $fechaBautismo, Esperanza = $esperanza, 
                        Nombramiento = $nombramiento, Servicio = $servicio, Grupo = $grupo,
                        NombreJWHub = $nombreJW
                    WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", p.Id);
                command.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                command.Parameters.AddWithValue("$fechaNacimiento", p.FechaNacimiento ?? "");
                command.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                command.Parameters.AddWithValue("$fechaBautismo", p.FechaBautismo ?? "");
                command.Parameters.AddWithValue("$esperanza", p.Esperanza ?? "");
                command.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                command.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                command.Parameters.AddWithValue("$grupo", p.Grupo);
                command.Parameters.AddWithValue("$nombreJW", p.NombreJWHub ?? "");
                command.ExecuteNonQuery();
            }
        }

        public static void EliminarPersona(int personaId)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmdActividad = connection.CreateCommand();
                    cmdActividad.Transaction = transaction;
                    cmdActividad.CommandText = "DELETE FROM ActividadPersonas WHERE PersonaId = $id;";
                    cmdActividad.Parameters.AddWithValue("$id", personaId);
                    cmdActividad.ExecuteNonQuery();

                    var cmdPersona = connection.CreateCommand();
                    cmdPersona.Transaction = transaction;
                    cmdPersona.CommandText = "DELETE FROM Personas WHERE Id = $id;";
                    cmdPersona.Parameters.AddWithValue("$id", personaId);
                    cmdPersona.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }

        public static List<ActividadMensual> ObtenerActividadPersona(int personaId)
        {
            var lista = new List<ActividadMensual>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Mes, AnioServicio, Participacion, CursosBiblicos, PrecursorAuxiliar, Horas, Notas FROM ActividadPersonas WHERE PersonaId = $id ORDER BY AnioServicio DESC;";
                command.Parameters.AddWithValue("$id", personaId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ActividadMensual
                        {
                            Mes = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            AnioServicio = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Participacion = reader.GetInt32(2) == 1,
                            CursosBiblicos = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            PrecursorAuxiliar = reader.GetInt32(4) == 1,
                            Horas = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Notas = reader.IsDBNull(6) ? "" : reader.GetString(6)
                        });
                    }
                }
            }
            return lista;
        }

        public static void GuardarTerritoriosImportados(string congreCodigo, List<TerritorioJSON> territorios)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var dictCreacion = new Dictionary<string, string>();
                    var cmdSelect = connection.CreateCommand();
                    cmdSelect.Transaction = transaction;
                    cmdSelect.CommandText = "SELECT Nombre, FechaCreacion FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $codigo AND FechaCreacion IS NOT NULL;";
                    cmdSelect.Parameters.AddWithValue("$codigo", congreCodigo);
                    using (var reader = cmdSelect.ExecuteReader())
                    {
                        while (reader.Read()) dictCreacion[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    }

                    var cmdDelete = connection.CreateCommand();
                    cmdDelete.Transaction = transaction;
                    cmdDelete.CommandText = "DELETE FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $codigo;";
                    cmdDelete.Parameters.AddWithValue("$codigo", congreCodigo);
                    cmdDelete.ExecuteNonQuery();

                    var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO TerritoriosVisitaV3 
                        (CongregacionCodigo, Nombre, UltimaFechaCompletado, AsignadoA, FechaAsignado, FechaCompletado, NombreVinculado, FechaCreacion) 
                        VALUES ($codigo, $nombre, $ultima, $asignado, $fechaAsig, $fechaComp, '', $creacion);";

                    foreach (var t in territorios)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("$codigo", congreCodigo);
                        command.Parameters.AddWithValue("$nombre", t.Nombre ?? "");
                        command.Parameters.AddWithValue("$ultima", t.UltimaFechaCompletado ?? "");
                        command.Parameters.AddWithValue("$asignado", t.AsignadoA ?? "");
                        command.Parameters.AddWithValue("$fechaAsig", t.FechaAsignado ?? "");
                        command.Parameters.AddWithValue("$fechaComp", t.FechaCompletado ?? "");

                        string fechaCrea = dictCreacion.ContainsKey(t.Nombre ?? "") ? dictCreacion[t.Nombre] : DateTime.Today.ToString("dd/MM/yyyy");
                        if (string.IsNullOrWhiteSpace(fechaCrea)) fechaCrea = DateTime.Today.ToString("dd/MM/yyyy");
                        command.Parameters.AddWithValue("$creacion", fechaCrea);

                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public static List<TerritorioJSON> ObtenerTerritoriosPorCongregacion(string congreCodigo)
        {
            var lista = new List<TerritorioJSON>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Nombre, UltimaFechaCompletado, AsignadoA, FechaAsignado, FechaCompletado, NombreVinculado, FechaCreacion, FechaEliminacion FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $codigo ORDER BY Id ASC;";
                command.Parameters.AddWithValue("$codigo", congreCodigo);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new TerritorioJSON
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            UltimaFechaCompletado = reader.GetString(2),
                            AsignadoA = reader.GetString(3),
                            FechaAsignado = reader.GetString(4),
                            FechaCompletado = reader.GetString(5),
                            NombreVinculado = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            FechaCreacion = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            FechaEliminacion = reader.IsDBNull(8) ? "" : reader.GetString(8)
                        });
                    }
                }
            }
            return lista;
        }

        public static void ActualizarVinculacionTerritorio(int idRegistro, string nuevoNombreVinculado)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE TerritoriosVisitaV3 SET NombreVinculado = $nombre WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$nombre", nuevoNombreVinculado ?? "");
                cmd.Parameters.AddWithValue("$id", idRegistro);
                cmd.ExecuteNonQuery();
            }
        }

        public static void GuardarMapeoNombre(string congreCodigo, string nombreS13, string nombreS21)
        {
            if (string.IsNullOrWhiteSpace(nombreS13)) return;
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO MapeoS13S21 (CongregacionCodigo, NombreS13, NombreS21) 
                    VALUES ($codigo, $s13, $s21) 
                    ON CONFLICT(CongregacionCodigo, NombreS13) 
                    DO UPDATE SET NombreS21 = $s21;";
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$s13", nombreS13);
                cmd.Parameters.AddWithValue("$s21", nombreS21 ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static Dictionary<string, string> ObtenerMapeos(string congreCodigo)
        {
            var dict = new Dictionary<string, string>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT NombreS13, NombreS21 FROM MapeoS13S21 WHERE CongregacionCodigo = $codigo;";
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) dict[reader.GetString(0)] = reader.GetString(1);
                }
            }
            return dict;
        }

        public static int CalcularSimilitud(string origen, string destino)
        {
            if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino)) return 0;

            string origenLimpio = QuitarAcentos(origen.ToLower());
            string destinoLimpio = QuitarAcentos(destino.ToLower());

            string nombrePrincipal = "";
            int indiceDe = origenLimpio.IndexOf(" de ");
            if (indiceDe > 0)
            {
                nombrePrincipal = origenLimpio.Substring(0, indiceDe).Trim();
                var trozos = nombrePrincipal.Split(' ');
                nombrePrincipal = trozos[trozos.Length - 1];
            }

            var palabrasIgnoradas = new HashSet<string> { "de", "del", "la", "las", "los", "y" };
            char[] separadores = new[] { ' ', ',', '.', '(', ')', '-', '_' };

            var palabrasOrigen = origenLimpio.Split(separadores, StringSplitOptions.RemoveEmptyEntries)
                                             .Where(p => !palabrasIgnoradas.Contains(p))
                                             .ToArray();

            var palabrasDestino = destinoLimpio.Split(separadores, StringSplitOptions.RemoveEmptyEntries)
                                               .Where(p => !palabrasIgnoradas.Contains(p))
                                               .ToArray();

            if (palabrasOrigen.Length == 0 || palabrasDestino.Length == 0) return 0;

            int puntos = 0;
            int maxPuntos = 0;

            foreach (var pOrig in palabrasOrigen)
            {
                int pesoPalabra = pOrig.Length;
                if (pOrig == nombrePrincipal) pesoPalabra *= 10;
                maxPuntos += pesoPalabra;

                int mejorCoincidencia = 0;
                foreach (var pDest in palabrasDestino)
                {
                    if (pOrig == pDest)
                    {
                        mejorCoincidencia = pesoPalabra;
                        break;
                    }
                    if (pOrig.Length > 3 && pDest.Length > 3)
                    {
                        if (DistanciaPalabra(pOrig, pDest) <= 1)
                        {
                            mejorCoincidencia = Math.Max(mejorCoincidencia, (int)(pesoPalabra * 0.8));
                        }
                    }
                }
                puntos += mejorCoincidencia;
            }

            return (int)((double)puntos / maxPuntos * 100);
        }

        public static void BorrarMemoriaMapeos()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM MapeoS13S21;";
                cmd.ExecuteNonQuery();
            }
        }

        private static int DistanciaPalabra(string a, string b)
        {
            int[,] matriz = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; matriz[i, 0] = i++) { }
            for (int j = 0; j <= b.Length; matriz[0, j] = j++) { }
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int coste = (b[j - 1] == a[i - 1]) ? 0 : 1;
                    matriz[i, j] = Math.Min(Math.Min(matriz[i - 1, j] + 1, matriz[i, j - 1] + 1), matriz[i - 1, j - 1] + coste);
                }
            }
            return matriz[a.Length, b.Length];
        }

        private static string QuitarAcentos(string texto)
        {
            var textoNormalizado = texto.Normalize(System.Text.NormalizationForm.FormD);
            var constructor = new System.Text.StringBuilder();

            foreach (var c in textoNormalizado)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    constructor.Append(c);
            }
            return constructor.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        public static Persona ObtenerPersonaPorId(int personaId)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Nombre, Nombramiento, Servicio, Grupo, FechaNacimiento, Sexo, FechaBautismo, NombreJWHub FROM Personas WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", personaId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Persona
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Nombramiento = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Servicio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Grupo = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            FechaNacimiento = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Sexo = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            FechaBautismo = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            NombreJWHub = reader.IsDBNull(8) ? "" : reader.GetString(8)
                        };
                    }
                }
            }
            return null;
        }

        public static void FusionarPersonas(int idSobreviviente, int idABorrar, string nombreFinal, int grupoFinal, string congreCodigo, string nombreViejoBorrar, string nombreViejoSobreviviente)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmdMover = connection.CreateCommand();
                    cmdMover.Transaction = transaction;
                    cmdMover.CommandText = "UPDATE OR IGNORE ActividadPersonas SET PersonaId = $idPrin WHERE PersonaId = $idSec;";
                    cmdMover.Parameters.AddWithValue("$idPrin", idSobreviviente);
                    cmdMover.Parameters.AddWithValue("$idSec", idABorrar);
                    cmdMover.ExecuteNonQuery();

                    var cmdUpdate = connection.CreateCommand();
                    cmdUpdate.Transaction = transaction;
                    cmdUpdate.CommandText = "UPDATE Personas SET Nombre = $nombre, Grupo = $grupo WHERE Id = $idPrin;";
                    cmdUpdate.Parameters.AddWithValue("$nombre", nombreFinal);
                    cmdUpdate.Parameters.AddWithValue("$grupo", grupoFinal);
                    cmdUpdate.Parameters.AddWithValue("$idPrin", idSobreviviente);
                    cmdUpdate.ExecuteNonQuery();

                    var cmdBorrar = connection.CreateCommand();
                    cmdBorrar.Transaction = transaction;
                    cmdBorrar.CommandText = "DELETE FROM Personas WHERE Id = $idSec;";
                    cmdBorrar.Parameters.AddWithValue("$idSec", idABorrar);
                    cmdBorrar.ExecuteNonQuery();

                    var cmdTerr = connection.CreateCommand();
                    cmdTerr.Transaction = transaction;
                    cmdTerr.CommandText = "UPDATE TerritoriosVisitaV3 SET NombreVinculado = $nombreFinal WHERE CongregacionCodigo = $codigo AND (NombreVinculado = $viejoBorrar OR NombreVinculado = $viejoSobreviviente);";
                    cmdTerr.Parameters.AddWithValue("$nombreFinal", nombreFinal);
                    cmdTerr.Parameters.AddWithValue("$codigo", congreCodigo);
                    cmdTerr.Parameters.AddWithValue("$viejoBorrar", nombreViejoBorrar ?? "");
                    cmdTerr.Parameters.AddWithValue("$viejoSobreviviente", nombreViejoSobreviviente ?? "");
                    cmdTerr.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }

        public static void ActualizarNombreEnTerritorios(string congreCodigo, string nombreAntiguo, string nombreNuevo)
        {
            if (string.IsNullOrWhiteSpace(nombreAntiguo) || nombreAntiguo == nombreNuevo) return;
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE TerritoriosVisitaV3 SET NombreVinculado = $nuevo WHERE CongregacionCodigo = $codigo AND NombreVinculado = $antiguo;";
                cmd.Parameters.AddWithValue("$nuevo", nombreNuevo);
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$antiguo", nombreAntiguo);
                cmd.ExecuteNonQuery();
            }
        }

        public static void InsertarPersonaImportada(Persona p, string codigoCongregacionExtraido)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Personas (Nombre, Servicio, Nombramiento, Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, CongregacionCodigo, Grupo, NombreJWHub)
                    VALUES ($nombre, $servicio, $nombramiento, $tlf, $email, $inicio, $escuela, $codigo, 0, $nombreJW);";

                cmd.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                cmd.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                cmd.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$tlf", p.Telefono ?? "");
                cmd.Parameters.AddWithValue("$email", p.Email ?? "");
                cmd.Parameters.AddWithValue("$inicio", p.FechaInicioPrec ?? "");
                cmd.Parameters.AddWithValue("$escuela", p.FechaUltimaEscuela ?? "");
                cmd.Parameters.AddWithValue("$codigo", codigoCongregacionExtraido ?? "");
                cmd.Parameters.AddWithValue("$nombreJW", p.NombreJWHub ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarDatosPersonaImportada(int personaId, PersonaExtraida prec)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();

                cmd.CommandText = @"
                    UPDATE Personas SET 
                        Servicio = $servicio, 
                        Nombramiento = $nombramiento, 
                        NombreJWHub = $nombreJW,
                        Solicitudes = $solicitudes,
                        Privilegios = $privilegios,
                        Foto = CASE WHEN $foto != '' THEN $foto ELSE Foto END,
                        CongregacionCodigo = CASE WHEN $codigo != '' THEN $codigo ELSE CongregacionCodigo END";

                bool esPrecursor = !string.IsNullOrEmpty(prec.Servicio) && prec.Servicio.ToLower().Contains("precursor");
                bool tieneSolicitud = !string.IsNullOrEmpty(prec.Solicitudes);

                if (esPrecursor || tieneSolicitud)
                {
                    cmd.CommandText += ", Telefono = $tlf, Email = $email";
                }
                if (esPrecursor)
                {
                    cmd.CommandText += ", FechaInicioPrec = $inicio, FechaUltimaEscuela = $escuela";
                }

                cmd.CommandText += " WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$servicio", prec.Servicio ?? "");
                cmd.Parameters.AddWithValue("$nombramiento", prec.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$nombreJW", prec.NombreCompleto ?? "");
                cmd.Parameters.AddWithValue("$solicitudes", prec.Solicitudes ?? "");
                cmd.Parameters.AddWithValue("$privilegios", prec.Privilegios ?? "");
                cmd.Parameters.AddWithValue("$foto", prec.Foto ?? "");
                cmd.Parameters.AddWithValue("$codigo", prec.Congregacion ?? "");
                cmd.Parameters.AddWithValue("$id", personaId);

                if (esPrecursor || tieneSolicitud)
                {
                    cmd.Parameters.AddWithValue("$tlf", prec.Telefono ?? "");
                    cmd.Parameters.AddWithValue("$email", prec.Email ?? "");
                }
                if (esPrecursor)
                {
                    cmd.Parameters.AddWithValue("$inicio", prec.FechaInicio ?? "");
                    cmd.Parameters.AddWithValue("$escuela", prec.FechaUltimaEscuela ?? "");
                }

                cmd.ExecuteNonQuery();
            }
        }

        public static void GuardarActividadMensualEditada(int personaId, string mes, string anioServicio, string horas, bool participacion, string cursos, bool precAux, string notas)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ActividadPersonas ADD COLUMN Cursos TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ActividadPersonas ADD COLUMN PrecursorAuxiliar INTEGER DEFAULT 0;"; cmdAlt.ExecuteNonQuery(); } catch { }
                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ActividadPersonas ADD COLUMN Notas TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE ActividadPersonas 
                    SET Horas = $horas, Participacion = $participacion, 
                        Cursos = $cursos, PrecursorAuxiliar = $precAux, Notas = $notas
                    WHERE PersonaId = $id AND Mes = $mes AND AnioServicio = $anio;
                    
                    INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Horas, Participacion, Cursos, PrecursorAuxiliar, Notas)
                    SELECT $id, $mes, $anio, $horas, $participacion, $cursos, $precAux, $notas
                    WHERE (SELECT changes() = 0);
                ";

                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.Parameters.AddWithValue("$mes", mes.ToLower());
                cmd.Parameters.AddWithValue("$anio", anioServicio);
                cmd.Parameters.AddWithValue("$horas", horas ?? "");
                cmd.Parameters.AddWithValue("$participacion", participacion);
                cmd.Parameters.AddWithValue("$cursos", cursos ?? "");
                cmd.Parameters.AddWithValue("$precAux", precAux ? 1 : 0);
                cmd.Parameters.AddWithValue("$notas", notas ?? "");

                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarRequisitoSilencioso(int personaId, int requisito)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE Personas ADD COLUMN RequisitoHoras INTEGER DEFAULT 0;"; cmdAlt.ExecuteNonQuery(); } catch { }

                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Personas SET RequisitoHoras = $req WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$req", requisito);
                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.ExecuteNonQuery();
            }
        }

        public static void GuardarActividadPrograma(string congreCodigo, string mesReferenciaStr, string dia, string turno, string responsable, string tipoActividad, string hora, string notasLibro, int companeroId)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ProgramaVisitaV2 
                    (CongreCodigo, MesReferencia, DiaSemana, Turno, Responsable, TipoActividad, Hora, NotasLibro, CompaneroId) 
                    VALUES ($codigo, $mes, $dia, $turno, $responsable, $tipo, $hora, $notas, $comp);";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferenciaStr);
                cmd.Parameters.AddWithValue("$dia", dia);
                cmd.Parameters.AddWithValue("$turno", turno);
                cmd.Parameters.AddWithValue("$responsable", responsable);
                cmd.Parameters.AddWithValue("$tipo", tipoActividad ?? "");
                cmd.Parameters.AddWithValue("$hora", hora ?? "");
                cmd.Parameters.AddWithValue("$notas", notasLibro ?? "");
                cmd.Parameters.AddWithValue("$comp", companeroId);

                cmd.ExecuteNonQuery();
            }
        }

        public static List<ActividadPrograma> ObtenerActividadesPrograma(string congreCodigo, string mesReferenciaStr, string dia, string turno, string responsable)
        {
            var lista = new List<ActividadPrograma>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT TipoActividad, Hora, NotasLibro, CompaneroId FROM ProgramaVisitaV2 WHERE CongreCodigo = $codigo AND MesReferencia = $mes AND DiaSemana = $dia AND Turno = $turno AND Responsable = $responsable ORDER BY Hora ASC;";
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferenciaStr);
                cmd.Parameters.AddWithValue("$dia", dia);
                cmd.Parameters.AddWithValue("$turno", turno);
                cmd.Parameters.AddWithValue("$responsable", responsable);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ActividadPrograma
                        {
                            TipoActividad = reader.GetString(0),
                            Hora = reader.GetString(1),
                            NotasLibro = reader.GetString(2),
                            CompaneroId = reader.GetInt32(3)
                        });
                    }
                }
            }
            return lista;
        }

        public class ActividadPrograma
        {
            public string TipoActividad { get; set; }
            public string Hora { get; set; }
            public string NotasLibro { get; set; }
            public int CompaneroId { get; set; }
        }

        public static bool HayVisitaRegistrada(string congreCodigo, string congreNombre, string mesReferenciaStr)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd1 = connection.CreateCommand();
                cmd1.CommandText = "SELECT COUNT(*) FROM Semanas WHERE (Congregacion = $codigo OR Congregacion = $nombre) AND Actividad = 'Visita' AND FechaMartes LIKE $mes;";
                cmd1.Parameters.AddWithValue("$codigo", congreCodigo ?? "");
                cmd1.Parameters.AddWithValue("$nombre", congreNombre ?? "");
                cmd1.Parameters.AddWithValue("$mes", mesReferenciaStr + "%");
                if ((long)cmd1.ExecuteScalar() > 0) return true;

                var cmd2 = connection.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(*) FROM ProgramaVisitaV2 WHERE CongreCodigo = $codigo AND MesReferencia = $mes;";
                cmd2.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd2.Parameters.AddWithValue("$mes", mesReferenciaStr);
                if ((long)cmd2.ExecuteScalar() > 0) return true;

                return false;
            }
        }

        public static string ObtenerMesSiguienteVisita(string congreCodigo, string congreNombre, string mesReferenciaStr)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                string fechaMinima = mesReferenciaStr + "-01";

                var cmd1 = connection.CreateCommand();
                cmd1.CommandText = "SELECT FechaMartes FROM Semanas WHERE (Congregacion = $codigo OR Congregacion = $nombre) AND Actividad = 'Visita' AND date(FechaMartes) >= date($mesMin) ORDER BY FechaMartes ASC LIMIT 1;";
                cmd1.Parameters.AddWithValue("$codigo", congreCodigo ?? "");
                cmd1.Parameters.AddWithValue("$nombre", congreNombre ?? "");
                cmd1.Parameters.AddWithValue("$mesMin", fechaMinima);

                var result1 = cmd1.ExecuteScalar();
                if (result1 != null)
                {
                    string fecha = result1.ToString();
                    if (fecha.Length >= 7) return fecha.Substring(0, 7);
                }

                var cmd2 = connection.CreateCommand();
                cmd2.CommandText = "SELECT MesReferencia FROM ProgramaVisitaV2 WHERE CongreCodigo = $codigo AND MesReferencia >= $mesRef ORDER BY MesReferencia ASC LIMIT 1;";
                cmd2.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd2.Parameters.AddWithValue("$mesRef", mesReferenciaStr);

                var result2 = cmd2.ExecuteScalar();
                if (result2 != null)
                {
                    return result2.ToString();
                }

                return null;
            }
        }

        public static int ObtenerIdSemanaVisita(string congreNombre, string mesReferenciaStr)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Semanas WHERE Congregacion = @Nombre AND Actividad = 'Visita' AND FechaMartes LIKE @Mes || '%' LIMIT 1";
                cmd.Parameters.AddWithValue("@Nombre", congreNombre);
                cmd.Parameters.AddWithValue("@Mes", mesReferenciaStr);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) return Convert.ToInt32(res);
                return 0;
            }
        }

        public static void MarcarPersonaEliminada(int idPersona)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Personas SET FechaEliminacion = $fecha WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$fecha", DateTime.Today.ToString("dd/MM/yyyy"));
                cmd.Parameters.AddWithValue("$id", idPersona);
                cmd.ExecuteNonQuery();
            }
        }

        public static void MarcarTerritorioEliminado(string congreCodigo, string nombre)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE TerritoriosVisitaV3 SET FechaEliminacion = $fecha WHERE CongregacionCodigo = $codigo AND Nombre = $nombre;";
                cmd.Parameters.AddWithValue("$fecha", DateTime.Today.ToString("dd/MM/yyyy"));
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$nombre", nombre);
                cmd.ExecuteNonQuery();
            }
        }

        public static void RenombrarTerritorioLocal(string congreCodigo, string nombreViejo, string nombreNuevo)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE TerritoriosVisitaV3 SET Nombre = $nuevo WHERE CongregacionCodigo = $codigo AND Nombre = $viejo;";
                cmd.Parameters.AddWithValue("$nuevo", nombreNuevo);
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$viejo", nombreViejo);
                cmd.ExecuteNonQuery();
            }
        }

        public class ContabilidadMes
        {
            public double Gastos { get; set; }
            public double Mantenimiento { get; set; }
            public double ObraMundial { get; set; }
            public double ObraResolucion { get; set; }
            public double Saldo { get; set; }
        }

        public static ContabilidadMes ObtenerContabilidad(string congreCodigo, string mesReferencia)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Gastos, Mantenimiento, ObraMundial, ObraResolucion, Saldo FROM ContabilidadMes WHERE CongreCodigo = $codigo AND MesReferencia = $mes;";
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferencia);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new ContabilidadMes
                        {
                            Gastos = reader.GetDouble(0),
                            Mantenimiento = reader.GetDouble(1),
                            ObraMundial = reader.GetDouble(2),
                            ObraResolucion = reader.GetDouble(3),
                            Saldo = reader.GetDouble(4)
                        };
                    }
                }
            }
            return new ContabilidadMes();
        }

        public static void GuardarContabilidad(string congreCodigo, string mesReferencia, ContabilidadMes datos)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ContabilidadMes (CongreCodigo, MesReferencia, Gastos, Mantenimiento, ObraMundial, ObraResolucion, Saldo)
                    VALUES ($codigo, $mes, $g, $m, $om, $or, $s)
                    ON CONFLICT(CongreCodigo, MesReferencia) DO UPDATE SET 
                    Gastos = $g, Mantenimiento = $m, ObraMundial = $om, ObraResolucion = $or, Saldo = $s;";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferencia);
                cmd.Parameters.AddWithValue("$g", datos.Gastos);
                cmd.Parameters.AddWithValue("$m", datos.Mantenimiento);
                cmd.Parameters.AddWithValue("$om", datos.ObraMundial);
                cmd.Parameters.AddWithValue("$or", datos.ObraResolucion);
                cmd.Parameters.AddWithValue("$s", datos.Saldo);
                cmd.ExecuteNonQuery();
            }
        }

        public static int InsertarPersonaNueva(Persona p)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"INSERT INTO Personas (Nombre, CongregacionCodigo, Grupo, Nombramiento, Servicio, Solicitudes, Privilegios, Foto, FechaNacimiento, Sexo, FechaBautismo, NombreJWHub, FechaCreacion)
                                    VALUES ($nombre, $codigo, $grupo, $nomb, $serv, $sol, $priv, $foto, $fn, $sexo, $fb, $jw, $fc);
                                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                cmd.Parameters.AddWithValue("$codigo", p.CongregacionCodigo);
                cmd.Parameters.AddWithValue("$grupo", p.Grupo);
                cmd.Parameters.AddWithValue("$nomb", p.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$serv", p.Servicio ?? "");
                cmd.Parameters.AddWithValue("$sol", p.Solicitudes ?? "");
                cmd.Parameters.AddWithValue("$fn", p.FechaNacimiento ?? "");
                cmd.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                cmd.Parameters.AddWithValue("$fb", p.FechaBautismo ?? "");
                cmd.Parameters.AddWithValue("$jw", p.NombreJWHub ?? "");
                cmd.Parameters.AddWithValue("$fc", p.FechaCreacion ?? "");
                cmd.Parameters.AddWithValue("$priv", p.Privilegios ?? "");
                cmd.Parameters.AddWithValue("$foto", p.Foto ?? "");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public static void InsertarDocumento(string categoria, string nombreArchivo, string rutaLocal)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO Documentos (Categoria, NombreArchivo, RutaLocal) VALUES ($cat, $nom, $ruta)";
                cmd.Parameters.AddWithValue("$cat", categoria);
                cmd.Parameters.AddWithValue("$nom", nombreArchivo);
                cmd.Parameters.AddWithValue("$ruta", rutaLocal);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<ArchivoDocumento> ObtenerDocumentos(string categoria)
        {
            var lista = new List<ArchivoDocumento>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, NombreArchivo, RutaLocal FROM Documentos WHERE Categoria = $cat";
                cmd.Parameters.AddWithValue("$cat", categoria);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ArchivoDocumento
                        {
                            Id = reader.GetInt32(0),
                            NombreArchivo = reader.GetString(1),
                            RutaLocal = reader.GetString(2),
                            Categoria = categoria
                        });
                    }
                }
            }
            return lista;
        }

        public static void EliminarDocumento(int id)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Documentos WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public static string ObtenerConfiguracion(string clave, string valorPorDefecto)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Valor FROM Configuracion WHERE Clave = $clave";
                cmd.Parameters.AddWithValue("$clave", clave);
                var res = cmd.ExecuteScalar();
                return res != null ? res.ToString() : valorPorDefecto;
            }
        }

        public static void GuardarConfiguracion(string clave, string valor)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO Configuracion (Clave, Valor) VALUES ($clave, $valor)";
                cmd.Parameters.AddWithValue("$clave", clave);
                cmd.Parameters.AddWithValue("$valor", valor);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<SeccionCircuito> ObtenerSecciones()
        {
            var lista = new List<SeccionCircuito>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM SeccionesCircuito ORDER BY Nombre";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new SeccionCircuito
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Nombre = reader["Nombre"].ToString(),
                            LugarAsamblea = reader["LugarAsamblea"].ToString()
                        });
                    }
                }
            }
            return lista;
        }

        public static void GuardarSeccion(SeccionCircuito seccion)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                if (seccion.Id == 0)
                    cmd.CommandText = "INSERT INTO SeccionesCircuito (Nombre, LugarAsamblea) VALUES (@Nombre, @LugarAsamblea)";
                else
                {
                    cmd.CommandText = "UPDATE SeccionesCircuito SET Nombre = @Nombre, LugarAsamblea = @LugarAsamblea WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", seccion.Id);
                }
                cmd.Parameters.AddWithValue("@Nombre", seccion.Nombre ?? "");
                cmd.Parameters.AddWithValue("@LugarAsamblea", seccion.LugarAsamblea ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void EliminarSeccion(int id)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM SeccionesCircuito WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<Congregacion> ObtenerCongregaciones()
        {
            var lista = new List<Congregacion>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT c.*, s.Nombre as NombreSeccion 
                    FROM Congregaciones c
                    LEFT JOIN SeccionesCircuito s ON c.SeccionId = s.Id
                    ORDER BY c.Nombre";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Congregacion
                        {
                            Codigo = reader["Codigo"].ToString(),
                            Nombre = reader["Nombre"].ToString(),
                            Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? "" : reader["Email"].ToString(),
                            SeccionId = reader.IsDBNull(reader.GetOrdinal("SeccionId")) ? 0 : Convert.ToInt32(reader["SeccionId"]),
                            NombreSeccion = reader.IsDBNull(reader.GetOrdinal("NombreSeccion")) ? "Sin asignar" : reader["NombreSeccion"].ToString(),
                            DireccionSR = reader.IsDBNull(reader.GetOrdinal("DireccionSR")) ? "" : reader["DireccionSR"].ToString(),
                            HoraEntreSemana = reader.IsDBNull(reader.GetOrdinal("HoraEntreSemana")) ? "" : reader["HoraEntreSemana"].ToString(),
                            DiaFinSemana = reader.IsDBNull(reader.GetOrdinal("DiaFinSemana")) ? "" : reader["DiaFinSemana"].ToString(),
                            HoraFinSemana = reader.IsDBNull(reader.GetOrdinal("HoraFinSemana")) ? "" : reader["HoraFinSemana"].ToString()
                        });
                    }
                }
            }
            return lista;
        }

        public static void InsertarCongregacion(Congregacion cong)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Congregaciones (Codigo, Nombre, Email, SeccionId, DireccionSR, HoraEntreSemana, DiaFinSemana, HoraFinSemana)
                    VALUES (@Codigo, @Nombre, @Email, @SeccionId, @DireccionSR, @HoraEntreSemana, @DiaFinSemana, @HoraFinSemana)";

                cmd.Parameters.AddWithValue("@Codigo", cong.Codigo ?? "");
                cmd.Parameters.AddWithValue("@Nombre", cong.Nombre ?? "");
                cmd.Parameters.AddWithValue("@Email", cong.Email ?? "");
                cmd.Parameters.AddWithValue("@SeccionId", cong.SeccionId);
                cmd.Parameters.AddWithValue("@DireccionSR", cong.DireccionSR ?? "");
                cmd.Parameters.AddWithValue("@HoraEntreSemana", cong.HoraEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@DiaFinSemana", cong.DiaFinSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraFinSemana", cong.HoraFinSemana ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarCongregacion(Congregacion cong)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Congregaciones SET 
                        Nombre = @Nombre, Email = @Email, SeccionId = @SeccionId, 
                        DireccionSR = @DireccionSR, HoraEntreSemana = @HoraEntreSemana, 
                        DiaFinSemana = @DiaFinSemana, HoraFinSemana = @HoraFinSemana 
                    WHERE Codigo = @Codigo";

                cmd.Parameters.AddWithValue("@Codigo", cong.Codigo ?? "");
                cmd.Parameters.AddWithValue("@Nombre", cong.Nombre ?? "");
                cmd.Parameters.AddWithValue("@Email", cong.Email ?? "");
                cmd.Parameters.AddWithValue("@SeccionId", cong.SeccionId);
                cmd.Parameters.AddWithValue("@DireccionSR", cong.DireccionSR ?? "");
                cmd.Parameters.AddWithValue("@HoraEntreSemana", cong.HoraEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@DiaFinSemana", cong.DiaFinSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraFinSemana", cong.HoraFinSemana ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarTablaNotasVisita()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS NotasVisita (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, SemanaId INTEGER, Cabecera TEXT, Pregunta TEXT, Respuesta TEXT, EsAlerta INTEGER DEFAULT 0
                    );";
                cmd.ExecuteNonQuery();

                try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE NotasVisita ADD COLUMN EsAlerta INTEGER DEFAULT 0;"; cmdAlt.ExecuteNonQuery(); } catch { }
            }
        }

        private static List<NotaVisita> GenerarPlantillaNotas(int semanaId)
        {
            return new List<NotaVisita>
            {
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Están ordenados y al día?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Se están aplicando las medidas de retención de documentos? (sfl-a)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Se indica en las tarjetas de publicadores inactivos (bautizados) la fecha en la que empezaron a serlo?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Tienen los nombres de publicadores la estructura correcta?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Están todos los datos de contacto de los publicadores (incluidos inactivos)?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN CON CCA", Pregunta = "Apunta aquí los deberes para el CCA", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN CON CCA", Pregunta = "¿Quién hace la oración final?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN DE ANCIANOS", Pregunta = "Información sobre cada una de las recomendaciones. ¿Son unánimes?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN DE ANCIANOS", Pregunta = "Si hay recomendaciones de baja, ¿qué requisitos bíblicos no cumple el hermano?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN DE ANCIANOS", Pregunta = "Comentarios sobre los puntos a tratar", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIÓN DE ANCIANOS", Pregunta = "¿Hay algún otro problema grave que requiera atención?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "PASTOREO", Pregunta = "Comentarios sobre las visitas de pastoreo y llamadas programadas para la semana", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PASTOREO", Pregunta = "¿Cómo se está llevando a cabo el pastoreo en general?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PASTOREO", Pregunta = "¿A qué publicadores podríamos llamar durante la semana?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PASTOREO", Pregunta = "Si existe un grupo, ¿están recibiendo pastoreo sus miembros? ¿Muestran los A vivo interés en el progreso del grupo?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PASTOREO", Pregunta = "¿Se ha hecho la visita anual a aquellos que cumplen un año bautizados?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay algún PR que esté pasando por dificultades o necesite ayuda (horario, eficacia, desánimo, invisible, etc.)?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay algún PR que resulta obvio que no puede continuar? Comenta el asunto con el CSC", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay algún PR de salud delicada o que podría encajar en tal perfil?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay algún precursor especial o misionero? ¿Necesita ayuda/consejo? ¿Está apuntado para predicar?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay graduados de la EER? ¿Cómo están? ¿Cómo se les usa? ¿Se les podría usar más plenamente?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PRECURSOR", Pregunta = "¿Hay alguien a quien podría animarse a servir como PR o PAux?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay publicadores que se hayan mudado a la congregación? Pregunta por ellos", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay publicadores en prisión que no estén recibiendo ayuda espiritual? ¿Se ha informado a la cong de contacto?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay algún hermano con dificultades para obtener las cosas básicas? ¿Sirvió en el pasado como SETC?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Cómo se puede ayudar a los publicadores irregulares?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "Los publicadores inactivos, ¿permiten que se les contacte? ¿Hay posibilidad de ayudarles?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay algún sacado/desasociado reciente? ¿O algún comité/investigación en curso? ¿Por qué motivo bíblico?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay hermanos que no sean ejemplares? ¿Hay hermanos con restricciones? ¿Se les van alzando paulatinamente?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay algún noviazgo en vigor o matrimonio reciente? Si hay algún caso de libertad bíblica, informar a la sucursal", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay algún extranjero indocumentado? ¿Ha dado pasos en firme para obtener la residencia legal?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "PUBLICADORES", Pregunta = "¿Hay algún hermano/a que solo puede salir el fin de semana y que se beneficiaría especialmente de salir juntos?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "¿Se trata este punto en las reuniones trimestrales?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "¿Están los jóvenes bien integrados y progresando? ¿Cómo se les usa? ¿Alguno que preocupe?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "Pregunta por algunos varones, quizás con posibilidad de asumir más responsabilidades", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "Los varones, ¿presentan asignaciones aparte de lecturas y discursos? ¿Presentan discursos los pub no bautizados?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "¿Qué trabajos hacen los SM? (Dptos, partes en VyMC, pastoreo, discursos públicos, etc.)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "¿Se ha capacitado al siervo de publicaciones para cumplir con su asignación?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CAPACITACIÓN", Pregunta = "¿Quién se encarga del mantenimiento? ¿Se capacita a padres e hijos en labores de mantenimiento y limpieza?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIONES", Pregunta = "¿Qué tal la asistencia presencial a las reuniones?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIONES", Pregunta = "¿Cómo se muestra hospitalidad a los oradores? ¿Se les ofrece dinero y comidas?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIONES", Pregunta = "Si existe un grupo, ¿hay hermanos con posibilidad de apoyarlo? ¿Se organizan salidas con este? ¿Y durante la semana?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REUNIONES", Pregunta = "Si existe un grupo, ¿celebra las reuniones en el mismo horario o diferente de la congregación anfitriona?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Qué tal la asistencia presencial al ministerio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Quién es el SS? ¿Cómo hace las visitas a los grupos?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Cómo se lleva a cabo la predicación como grupo?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Apoyan la predicación semanalmente los varones nombrados? ¿Y sus familias?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿En qué aspectos del ministerio están obteniendo los publicadores buenos resultados?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Cómo se organizan las reuniones para la predicación? ¿Se habilitará zoom durante la visita? ¿Quién lo organiza?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Hay un calendario para la PPOC? ¿Cómo funciona?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Habrá suficiente territorio para cada día de la visita?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Se motiva a los publicadores a servir como precursores auxiliares en meses clave?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Hay PR con asignaciones teocráticas adicionales? (Betel, RTO, LDC, CEH, etc.)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Se ha abarcado todo el territorio en el último año de servicio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Hay territorios más de 2 años sin trabajar?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Hay suficientes territorios en manos de los hermanos?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Se aprovechan bien las campañas?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Están equilibrados los grupos de predicación?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "¿Hay existencias de las publicaciones que mejor encajan en el territorio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "MINISTERIO", Pregunta = "Pregunta a un miembro del CSC sobre las tendencias que haya detectado, aspectos que merezcan elogio o consejo", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Han recibido los hermanos capacitación para la predicación pública basada en el S-148?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "El programa de PPOC, ¿es eficaz? ¿Podría hacerse algún ajuste (horario, lugar, etc.)? ¿Está el equipo en buen estado?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Existen residencias de ancianos o universidades en las que se pueda dar testimonio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "Si hay personas que han pedido que no se les visite, ¿está la anotación en la tarjeta? ¿Se les contacta cada 2-3 años?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Están progresando los CB?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "Los CB, ¿son mayoritariamente familiares de hermanos, o más bien del territorio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Hay personas que dependan de otro idioma diferente de la congregación?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Saben los publicadores usar bien el kit de enseñanza?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "En general, cumplen los PR con el requisito de horas?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OBSERVACIONES DE LA PREDICACIÓN", Pregunta = "¿Se ha formado algún campamento de refugiados en la zona? En tal caso habrá que avisar a SRV", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se están indicando correctamente los fondos enviados a la sucursal?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "Las auditorías, ¿se llevan a cabo regularmente?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "Los fondos de la congregación, ¿superan el máximo establecido?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se está enviando la cantidad acordada a la cuenta de mantenimiento?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se han detectado irregularidades en las auditorías?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se ha conciliado correctamente el último estado de cuentas siguiendo las Instrucciones de contabilidad (S-27)?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Pone el CCA sus iniciales en las facturas?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se hacen los ingresos semanalmente o hay una resolución aprobada para hacerlos con menos regularidad?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CONTABILIDAD", Pregunta = "¿Se han detectado otros puntos a mejorar?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Se ciñe la organización de las reuniones a las pautas de audio y video?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Hay un programa de limpieza establecido y colgado en el tablero de anuncios?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Está el cartel de aviso transmisión colgado?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Se hace uso de los programas grabados en JW Stream? ¿Con qué frecuencia se presentan discursos por zoom?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Cómo es la asistencia en referencia al número de publicadores?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Ha habido algún incidente, quasi accidente, incendio, robo o allanamiento? ¿Se ha enviado el TO-5?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Se presentan las necesidades relacionadas con la seguridad cada año?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Cómo funciona el plan de emergencia para proteger a los publicadores con necesidades especiales y los registros?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Entienden los A cómo proceder en caso de desastre? ¿Convendría recordárselo?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Están equilibradas las responsabilidades? ¿Cumplen bien los ancianos con ellas?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Cómo es la relación entre los varones nombrados?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Han animado a todos los publicadores a rellenar su DPA? ¿Conocen el procedimiento para contactar con el CEH?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Qué tal la calidad de la enseñanza? Puedes valorar a quienes han tenido participación. Si algún anciano califica para tener una asignación en la AC, también se puede indicar", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Saben los ancianos redactar cartas claras y concisas? ¿Convendría capacitarles en este aspecto?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Conviene recordarles la importancia de ser juzgar con justicia, discernimiento y sabiduría?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Ponen normas innecesarias?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "En las RA, ¿toman nota solo de las decisiones? ¿O hacen informes demasiado detallados? (sfl 1:8)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Dedican tiempo a predicar con los PR en diversas facetas? Comenta en la RA la actividad y circunstancias de los PR", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Están al tanto de las pautas sobre información pública? (sfl cap. 30)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "CUERPO DE ANCIANOS", Pregunta = "¿Tienen los ancianos la costumbre de elogiar de forma específica y sincera?", Respuesta = "" },

                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "Hasta donde se sabe, ¿tienen los hermanos buenas rutinas de estudio?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Comentan los hermanos ejemplarmente en las reuniones?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Celebran las familias la NAF regularmente? ¿Invitan a otros hermanos?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Hay algún hermano/a con habilidades en un oficio que todavía no haya rellenado el A-2?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Podría animarse a alguien a rellenar la solicitud A-19?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Hermanos con potencial para servir como SM, A, PR, CEH, Betel, LDC, etc?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Es la congregación hospitalaria? ¿Hacen actividades sanas juntos? ¿Llevan los varones nombrados la delantera?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Necesita ayuda la congregación de algún tipo? ¿Convendría enviar un S-310?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Hay que enviar algún S-326? Si merece consideración especial, indícalo en una carta a SRV - Ministros en el Campo", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Hay alguna solicitud G-8 para atender? ¿Hay alguien excepcional a quien se pueda invitar a rellenarla?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Sigue reuniendo la entidad las condiciones expuestas en sfl apéndice B?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "¿Convendría dividir, disolver, proponer un grupo/pregrupo o hacer algún otro cambio ahora o a corto plazo?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "OTRAS ANOTACIONES", Pregunta = "Anotaciones para próximas visitas:", Respuesta = "" }
            };
        }

        public static List<NotaVisita> ObtenerNotasVisita(int semanaId)
        {
            var lista = new List<NotaVisita>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, SemanaId, Cabecera, Pregunta, Respuesta, EsAlerta FROM NotasVisita WHERE SemanaId = @SemanaId ORDER BY Id ASC";
                cmd.Parameters.AddWithValue("@SemanaId", semanaId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new NotaVisita
                        {
                            Id = reader.GetInt32(0),
                            SemanaId = reader.GetInt32(1),
                            Cabecera = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Pregunta = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Respuesta = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            EsAlerta = !reader.IsDBNull(5) && reader.GetInt32(5) == 1
                        });
                    }
                }
            }

            if (lista.Count == 0)
            {
                lista = GenerarPlantillaNotas(semanaId);
                GuardarNotasVisita(semanaId, lista);

                lista.Clear();
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, SemanaId, Cabecera, Pregunta, Respuesta, EsAlerta FROM NotasVisita WHERE SemanaId = @SemanaId ORDER BY Id ASC";
                    cmd.Parameters.AddWithValue("@SemanaId", semanaId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new NotaVisita
                            {
                                Id = reader.GetInt32(0),
                                SemanaId = reader.GetInt32(1),
                                Cabecera = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Pregunta = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Respuesta = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                EsAlerta = !reader.IsDBNull(5) && reader.GetInt32(5) == 1
                            });
                        }
                    }
                }
            }

            return lista;
        }

        public static void GuardarNotasVisita(int semanaId, List<NotaVisita> notas)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmdInsert = connection.CreateCommand();
                    cmdInsert.Transaction = transaction;
                    cmdInsert.CommandText = @"
                        INSERT INTO NotasVisita (SemanaId, Cabecera, Pregunta, Respuesta, EsAlerta)
                        VALUES (@SemanaId, @Cabecera, @Pregunta, @Respuesta, @EsAlerta)";

                    foreach (var nota in notas)
                    {
                        cmdInsert.Parameters.Clear();
                        cmdInsert.Parameters.AddWithValue("@SemanaId", semanaId);
                        cmdInsert.Parameters.AddWithValue("@Cabecera", nota.Cabecera ?? "");
                        cmdInsert.Parameters.AddWithValue("@Pregunta", nota.Pregunta ?? "");
                        cmdInsert.Parameters.AddWithValue("@Respuesta", nota.Respuesta ?? "");
                        cmdInsert.Parameters.AddWithValue("@EsAlerta", nota.EsAlerta ? 1 : 0);
                        cmdInsert.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public static void ActualizarNotaVisitaUnica(NotaVisita nota)
        {
            if (nota.Id == 0) return;
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE NotasVisita SET Respuesta = @Respuesta, EsAlerta = @EsAlerta WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Respuesta", nota.Respuesta ?? "");
                cmd.Parameters.AddWithValue("@EsAlerta", nota.EsAlerta ? 1 : 0);
                cmd.Parameters.AddWithValue("@Id", nota.Id);
                cmd.ExecuteNonQuery();
            }
        }

        // =========================================================
        // GESTIÓN DE ASAMBLEAS Y EVENTOS (PRECURSORES Y ORGANIGRAMA)
        // =========================================================

        public static void AsegurarTablasAsambleas()
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ProgramaAsambleas (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Anio TEXT,
                            Tipo TEXT,
                            Seccion TEXT,
                            Titulo TEXT,
                            TextoTematico TEXT,
                            Lugar TEXT,
                            Fecha TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ProgramaAsambleasPartes (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            AsambleaId INTEGER,
                            Turno TEXT,
                            Hora TEXT,
                            Titulo TEXT,
                            AsignadoId INTEGER,
                            Participantes TEXT,
                            RutaArchivo TEXT,
                            Tipo TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ProgramaAsambleasOrganigrama (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            AsambleaId INTEGER,
                            Departamento TEXT,
                            AsignadoId INTEGER,
                            Rol TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ReunionPrecursores (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Anio TEXT,
                            Titulo TEXT,
                            TextoTematico TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ReunionPrecursoresSesiones (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ReunionId INTEGER,
                            Numero INTEGER,
                            Fecha TEXT,
                            Hora TEXT,
                            Congregaciones TEXT,
                            Lugar TEXT
                        );
                        CREATE TABLE IF NOT EXISTS ReunionPrecursoresPartes (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SesionId INTEGER,
                            Hora TEXT,
                            Titulo TEXT,
                            AsignadoId INTEGER,
                            Participantes TEXT,
                            RutaArchivo TEXT,
                            Tipo TEXT
                        );
                    ";
                    cmd.ExecuteNonQuery();

                    try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ProgramaAsambleasPartes ADD COLUMN Tipo TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                    try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ReunionPrecursoresPartes ADD COLUMN Tipo TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                    try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ProgramaAsambleasOrganigrama ADD COLUMN Rol TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                    try { var cmdAlt = connection.CreateCommand(); cmdAlt.CommandText = "ALTER TABLE ReunionPrecursoresSesiones ADD COLUMN Lugar TEXT;"; cmdAlt.ExecuteNonQuery(); } catch { }
                }
            }
            catch { }
        }

        public static List<string> ObtenerListaCongregaciones()
        {
            var congs = new List<string>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    try { var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT c.Nombre FROM Congregaciones c ORDER BY c.Nombre"; using (var reader = cmd.ExecuteReader()) { while (reader.Read()) { congs.Add(reader.GetString(0)); } } } catch { }
                    if (congs.Count == 0) { try { var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT DISTINCT Congregacion FROM Semanas WHERE Congregacion IS NOT NULL AND Congregacion != '' ORDER BY Congregacion"; using (var reader = cmd.ExecuteReader()) { while (reader.Read()) { congs.Add(reader.GetString(0)); } } } catch { } }
                    if (congs.Count == 0) { try { var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT DISTINCT Congregacion FROM Personas WHERE Congregacion IS NOT NULL AND Congregacion != '' ORDER BY Congregacion"; using (var reader = cmd.ExecuteReader()) { while (reader.Read()) { congs.Add(reader.GetString(0)); } } } catch { } }
                }
            }
            catch { }
            return congs.Distinct().ToList();
        }

        // =========================================================
        // CLASES DE APOYO (Datos del Hermano y Organigrama)
        // =========================================================

        public class InfoHermanoPDF
        {
            public string NombreFormateado { get; set; } = "";
            public string Congregacion { get; set; } = "";
            public string Telefono { get; set; } = "";
            public string Email { get; set; } = "";
            public string AnioNacimiento { get; set; } = "";
        }

        public class AsignacionOrganigramaBD
        {
            public string Departamento { get; set; }
            public int AsignadoId { get; set; }
            public string Rol { get; set; }
        }

        public static InfoHermanoPDF ObtenerInfoHermanoParaOrganigrama(int id)
        {
            var info = new InfoHermanoPDF();
            if (id <= 0) return info;

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT * FROM Personas WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                if (reader.IsDBNull(i)) continue;
                                string col = reader.GetName(i).ToLower();
                                string val = reader.GetValue(i).ToString();

                                if (col == "nombre" || col == "nombrejwhub")
                                {
                                    if (string.IsNullOrEmpty(info.NombreFormateado))
                                    {
                                        if (val.Contains(",")) { var parts = val.Split(','); info.NombreFormateado = $"{parts[1].Trim()} {parts[0].Trim()}"; }
                                        else info.NombreFormateado = val;
                                    }
                                }
                                else if (col == "congregacion") info.Congregacion = val;
                                else if (col == "telefono" || col == "movil" || col == "teléfono" || col == "móvil") { if (string.IsNullOrEmpty(info.Telefono)) info.Telefono = val; }
                                else if (col == "email" || col == "correo") { if (string.IsNullOrEmpty(info.Email)) info.Email = val; }
                                else if (col == "añonacimiento" || col == "anionacimiento" || col == "año" || col == "anio") { if (string.IsNullOrEmpty(info.AnioNacimiento)) info.AnioNacimiento = val; }
                                else if (col == "fechanacimiento")
                                {
                                    if (string.IsNullOrEmpty(info.AnioNacimiento) && val.Length >= 4)
                                    {
                                        if (val.Contains("-")) info.AnioNacimiento = val.Split('-')[0];
                                        else if (val.Contains("/")) info.AnioNacimiento = val.Split('/').Last();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return info;
        }

        // --- ASAMBLEAS Y ORGANIGRAMA ---
        public static long ObtenerIdYDatosAsamblea(string anio, string tipo, string seccion, out string titulo, out string textoTematico, out string lugar, out DateTime? fecha)
        {
            titulo = ""; textoTematico = ""; lugar = ""; fecha = null;
            long asambleaId = 0;

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Titulo, TextoTematico, Lugar, Fecha FROM ProgramaAsambleas WHERE Anio = @anio AND Tipo = @tipo AND Seccion = @seccion";
                    cmd.Parameters.AddWithValue("@anio", anio); cmd.Parameters.AddWithValue("@tipo", tipo); cmd.Parameters.AddWithValue("@seccion", seccion);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            asambleaId = reader.GetInt64(0);
                            titulo = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            textoTematico = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            lugar = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            if (!reader.IsDBNull(4)) if (DateTime.TryParse(reader.GetString(4), out DateTime parsedDate)) fecha = parsedDate;
                        }
                    }
                }
            }
            catch { }
            return asambleaId;
        }

        public static List<PartePrograma> ObtenerPartesAsamblea(long asambleaId)
        {
            var partes = new List<PartePrograma>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Turno, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo FROM ProgramaAsambleasPartes WHERE AsambleaId = @id ORDER BY Id ASC";
                    cmd.Parameters.AddWithValue("@id", asambleaId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            partes.Add(new PartePrograma
                            {
                                Turno = reader.GetString(0),
                                Hora = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Titulo = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                AsignadoId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                Participantes = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                RutaArchivo = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                Tipo = reader.IsDBNull(6) ? "" : reader.GetString(6)
                            });
                        }
                    }
                }
            }
            catch { }
            return partes;
        }

        public static List<AsignacionOrganigramaBD> ObtenerOrganigrama(long asambleaId)
        {
            var asignaciones = new List<AsignacionOrganigramaBD>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Departamento, AsignadoId, Rol FROM ProgramaAsambleasOrganigrama WHERE AsambleaId = @id ORDER BY Id ASC";
                    cmd.Parameters.AddWithValue("@id", asambleaId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            asignaciones.Add(new AsignacionOrganigramaBD
                            {
                                Departamento = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                AsignadoId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                Rol = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch { }
            return asignaciones;
        }

        public static void GuardarProgramaAsamblea(string anio, string tipo, string seccion, string titulo, string texto, string lugar, DateTime? fecha, System.Collections.Generic.IEnumerable<PartePrograma> partes, System.Collections.Generic.IEnumerable<AsignacionOrganigramaBD> organigrama)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id FROM ProgramaAsambleas WHERE Anio = @anio AND Tipo = @tipo AND Seccion = @seccion";
                cmd.Parameters.AddWithValue("@anio", anio); cmd.Parameters.AddWithValue("@tipo", tipo); cmd.Parameters.AddWithValue("@seccion", seccion);
                var result = cmd.ExecuteScalar();
                long asambleaId = 0;
                string fechaStr = fecha.HasValue ? fecha.Value.ToString("yyyy-MM-dd") : null;

                if (result != null)
                {
                    asambleaId = (long)result;
                    var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = "UPDATE ProgramaAsambleas SET Titulo = @titulo, TextoTematico = @texto, Lugar = @lugar, Fecha = @fecha WHERE Id = @id";
                    updateCmd.Parameters.AddWithValue("@titulo", titulo ?? ""); updateCmd.Parameters.AddWithValue("@texto", texto ?? "");
                    updateCmd.Parameters.AddWithValue("@lugar", lugar ?? ""); updateCmd.Parameters.AddWithValue("@fecha", fechaStr ?? (object)DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@id", asambleaId); updateCmd.ExecuteNonQuery();
                }
                else
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO ProgramaAsambleas (Anio, Tipo, Seccion, Titulo, TextoTematico, Lugar, Fecha) VALUES (@anio, @tipo, @seccion, @titulo, @texto, @lugar, @fecha); SELECT last_insert_rowid();";
                    insertCmd.Parameters.AddWithValue("@anio", anio); insertCmd.Parameters.AddWithValue("@tipo", tipo); insertCmd.Parameters.AddWithValue("@seccion", seccion);
                    insertCmd.Parameters.AddWithValue("@titulo", titulo ?? ""); insertCmd.Parameters.AddWithValue("@texto", texto ?? "");
                    insertCmd.Parameters.AddWithValue("@lugar", lugar ?? ""); insertCmd.Parameters.AddWithValue("@fecha", fechaStr ?? (object)DBNull.Value);
                    asambleaId = (long)insertCmd.ExecuteScalar();
                }

                if (partes != null)
                {
                    var deleteCmd = connection.CreateCommand(); deleteCmd.CommandText = "DELETE FROM ProgramaAsambleasPartes WHERE AsambleaId = @id";
                    deleteCmd.Parameters.AddWithValue("@id", asambleaId); deleteCmd.ExecuteNonQuery();

                    foreach (var parte in partes)
                    {
                        var insertParte = connection.CreateCommand();
                        insertParte.CommandText = "INSERT INTO ProgramaAsambleasPartes (AsambleaId, Turno, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo) VALUES (@id, @turno, @hora, @titulo, @asignado, @part, @ruta, @tipoParte);";
                        insertParte.Parameters.AddWithValue("@id", asambleaId); insertParte.Parameters.AddWithValue("@turno", parte.Turno);
                        insertParte.Parameters.AddWithValue("@hora", parte.Hora ?? ""); insertParte.Parameters.AddWithValue("@titulo", parte.Titulo ?? "");
                        insertParte.Parameters.AddWithValue("@asignado", parte.AsignadoId); insertParte.Parameters.AddWithValue("@part", parte.Participantes ?? "");
                        insertParte.Parameters.AddWithValue("@ruta", parte.RutaArchivo ?? ""); insertParte.Parameters.AddWithValue("@tipoParte", parte.Tipo ?? "");
                        insertParte.ExecuteNonQuery();
                    }
                }

                if (organigrama != null)
                {
                    var delOrgCmd = connection.CreateCommand(); delOrgCmd.CommandText = "DELETE FROM ProgramaAsambleasOrganigrama WHERE AsambleaId = @id";
                    delOrgCmd.Parameters.AddWithValue("@id", asambleaId); delOrgCmd.ExecuteNonQuery();

                    foreach (var asignacion in organigrama)
                    {
                        var insOrg = connection.CreateCommand();
                        insOrg.CommandText = "INSERT INTO ProgramaAsambleasOrganigrama (AsambleaId, Departamento, AsignadoId, Rol) VALUES (@id, @dep, @asignado, @rol);";
                        insOrg.Parameters.AddWithValue("@id", asambleaId); insOrg.Parameters.AddWithValue("@dep", asignacion.Departamento ?? "");
                        insOrg.Parameters.AddWithValue("@asignado", asignacion.AsignadoId); insOrg.Parameters.AddWithValue("@rol", asignacion.Rol ?? "");
                        insOrg.ExecuteNonQuery();
                    }
                }

                try
                {
                    if (fecha.HasValue)
                    {
                        int diff = fecha.Value.DayOfWeek - DayOfWeek.Monday; if (diff < 0) diff += 7;
                        DateTime lunes = fecha.Value.AddDays(-diff).Date; DateTime martes = lunes.AddDays(1);
                        string fechaMartesStr = martes.ToString("yyyy-MM-dd");

                        var cmdClean = connection.CreateCommand(); cmdClean.CommandText = "UPDATE Semanas SET Actividad = NULL, SeccionAsamblea = NULL WHERE Actividad = 'Asamblea de circuito' AND SeccionAsamblea = @seccion AND date(FechaMartes) != date(@fechaMartes)";
                        cmdClean.Parameters.AddWithValue("@seccion", seccion); cmdClean.Parameters.AddWithValue("@fechaMartes", fechaMartesStr); cmdClean.ExecuteNonQuery();

                        var cmdCheck = connection.CreateCommand(); cmdCheck.CommandText = "SELECT FechaMartes FROM Semanas WHERE date(FechaMartes) = date(@fechaMartes)"; cmdCheck.Parameters.AddWithValue("@fechaMartes", fechaMartesStr);
                        if (cmdCheck.ExecuteScalar() != null)
                        {
                            var cmdUpdSemana = connection.CreateCommand(); cmdUpdSemana.CommandText = "UPDATE Semanas SET Actividad = 'Asamblea de circuito', SeccionAsamblea = @seccion WHERE date(FechaMartes) = date(@fechaMartes)";
                            cmdUpdSemana.Parameters.AddWithValue("@seccion", seccion); cmdUpdSemana.Parameters.AddWithValue("@fechaMartes", fechaMartesStr); cmdUpdSemana.ExecuteNonQuery();
                        }
                        else
                        {
                            var cmdInsSemana = connection.CreateCommand(); cmdInsSemana.CommandText = "INSERT INTO Semanas (FechaMartes, Actividad, SeccionAsamblea) VALUES (@fechaMartes, 'Asamblea de circuito', @seccion)";
                            cmdInsSemana.Parameters.AddWithValue("@fechaMartes", fechaMartesStr); cmdInsSemana.Parameters.AddWithValue("@seccion", seccion); cmdInsSemana.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        var cmdClean = connection.CreateCommand(); cmdClean.CommandText = "UPDATE Semanas SET Actividad = NULL, SeccionAsamblea = NULL WHERE Actividad = 'Asamblea de circuito' AND SeccionAsamblea = @seccion";
                        cmdClean.Parameters.AddWithValue("@seccion", seccion); cmdClean.ExecuteNonQuery();
                    }
                }
                catch { }
            }
        }

        // --- PRECURSORES ---
        public static long ObtenerDatosReunionPrecursores(string anio, out string titulo, out string textoTematico)
        {
            titulo = ""; textoTematico = "";
            long reunionId = 0;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Titulo, TextoTematico FROM ReunionPrecursores WHERE Anio = @anio";
                    cmd.Parameters.AddWithValue("@anio", anio);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            reunionId = reader.GetInt64(0);
                            titulo = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            textoTematico = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        }
                    }
                }
            }
            catch { }
            return reunionId;
        }

        public static List<SesionPrecursores> ObtenerSesionesPrecursores(long reunionId)
        {
            var sesiones = new List<SesionPrecursores>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Numero, Fecha, Hora, Congregaciones, Lugar FROM ReunionPrecursoresSesiones WHERE ReunionId = @id ORDER BY Numero ASC, Id ASC";
                    cmd.Parameters.AddWithValue("@id", reunionId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime? fechaParseada = null;
                            if (!reader.IsDBNull(2) && DateTime.TryParse(reader.GetString(2), out DateTime result)) fechaParseada = result;

                            long sesionId = reader.GetInt64(0);
                            var sesion = new SesionPrecursores
                            {
                                IdBD = (int)sesionId,
                                Numero = reader.IsDBNull(1) ? 1 : reader.GetInt32(1),
                                Fecha = fechaParseada,
                                Hora = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Congregaciones = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Lugar = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : ""
                            };

                            var cmdPartes = connection.CreateCommand();
                            cmdPartes.CommandText = "SELECT Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo FROM ReunionPrecursoresPartes WHERE SesionId = @sesionId ORDER BY Id ASC";
                            cmdPartes.Parameters.AddWithValue("@sesionId", sesionId);
                            using (var readerPartes = cmdPartes.ExecuteReader())
                            {
                                while (readerPartes.Read())
                                {
                                    sesion.Partes.Add(new PartePrograma
                                    {
                                        Turno = "Precursores",
                                        Hora = readerPartes.IsDBNull(0) ? "" : readerPartes.GetString(0),
                                        Titulo = readerPartes.IsDBNull(1) ? "" : readerPartes.GetString(1),
                                        AsignadoId = readerPartes.IsDBNull(2) ? 0 : readerPartes.GetInt32(2),
                                        Participantes = readerPartes.IsDBNull(3) ? "" : readerPartes.GetString(3),
                                        RutaArchivo = readerPartes.IsDBNull(4) ? "" : readerPartes.GetString(4),
                                        Tipo = readerPartes.IsDBNull(5) ? "" : readerPartes.GetString(5)
                                    });
                                }
                            }
                            sesiones.Add(sesion);
                        }
                    }
                }
            }
            catch { }
            return sesiones;
        }

        public static void GuardarReunionPrecursores(string anio, string titulo, string texto, List<SesionPrecursores> sesiones)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id FROM ReunionPrecursores WHERE Anio = @anio";
                cmd.Parameters.AddWithValue("@anio", anio);
                var result = cmd.ExecuteScalar();
                long reunionId = 0;

                if (result != null)
                {
                    reunionId = (long)result;
                    var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = "UPDATE ReunionPrecursores SET Titulo = @titulo, TextoTematico = @texto WHERE Id = @id";
                    updateCmd.Parameters.AddWithValue("@titulo", titulo ?? ""); updateCmd.Parameters.AddWithValue("@texto", texto ?? "");
                    updateCmd.Parameters.AddWithValue("@id", reunionId); updateCmd.ExecuteNonQuery();
                }
                else
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT INTO ReunionPrecursores (Anio, Titulo, TextoTematico) VALUES (@anio, @titulo, @texto); SELECT last_insert_rowid();";
                    insertCmd.Parameters.AddWithValue("@anio", anio); insertCmd.Parameters.AddWithValue("@titulo", titulo ?? "");
                    insertCmd.Parameters.AddWithValue("@texto", texto ?? ""); reunionId = (long)insertCmd.ExecuteScalar();
                }

                var delPartes = connection.CreateCommand(); delPartes.CommandText = "DELETE FROM ReunionPrecursoresPartes WHERE SesionId IN (SELECT Id FROM ReunionPrecursoresSesiones WHERE ReunionId = @id)";
                delPartes.Parameters.AddWithValue("@id", reunionId); delPartes.ExecuteNonQuery();

                var delSesiones = connection.CreateCommand(); delSesiones.CommandText = "DELETE FROM ReunionPrecursoresSesiones WHERE ReunionId = @id";
                delSesiones.Parameters.AddWithValue("@id", reunionId); delSesiones.ExecuteNonQuery();

                foreach (var sesion in sesiones)
                {
                    var insSesion = connection.CreateCommand();
                    insSesion.CommandText = "INSERT INTO ReunionPrecursoresSesiones (ReunionId, Numero, Fecha, Hora, Congregaciones, Lugar) VALUES (@id, @num, @fecha, @hora, @congre, @lugar); SELECT last_insert_rowid();";
                    insSesion.Parameters.AddWithValue("@id", reunionId); insSesion.Parameters.AddWithValue("@num", sesion.Numero);
                    insSesion.Parameters.AddWithValue("@fecha", sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
                    insSesion.Parameters.AddWithValue("@hora", sesion.Hora ?? ""); insSesion.Parameters.AddWithValue("@congre", sesion.Congregaciones ?? "");
                    insSesion.Parameters.AddWithValue("@lugar", sesion.Lugar ?? "");
                    long newSesionId = (long)insSesion.ExecuteScalar();

                    foreach (var parte in sesion.Partes)
                    {
                        var insParte = connection.CreateCommand();
                        insParte.CommandText = "INSERT INTO ReunionPrecursoresPartes (SesionId, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo) VALUES (@sesionId, @hora, @titulo, @asignado, @part, @ruta, @tipoParte);";
                        insParte.Parameters.AddWithValue("@sesionId", newSesionId); insParte.Parameters.AddWithValue("@hora", parte.Hora ?? "");
                        insParte.Parameters.AddWithValue("@titulo", parte.Titulo ?? ""); insParte.Parameters.AddWithValue("@asignado", parte.AsignadoId);
                        insParte.Parameters.AddWithValue("@part", parte.Participantes ?? ""); insParte.Parameters.AddWithValue("@ruta", parte.RutaArchivo ?? "");
                        insParte.Parameters.AddWithValue("@tipoParte", parte.Tipo ?? "");
                        insParte.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    // =========================================================
    // MODELOS DE DATOS GENERALES
    // =========================================================

    public class SeccionCircuito
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string LugarAsamblea { get; set; }
    }

    public class CongregacionCircuito
    {
        public string Nombre { get; set; }
        public int SeccionId { get; set; }
        public string NombreSeccion { get; set; }
        public string DireccionSR { get; set; }
        public string HoraEntreSemana { get; set; }
        public string DiaFinSemana { get; set; }
        public string HoraFinSemana { get; set; }
    }

    public class NotaVisita : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public int Id { get; set; }
        public int SemanaId { get; set; }
        public string Cabecera { get; set; }
        public string Pregunta { get; set; }

        private string _respuesta;
        public string Respuesta
        {
            get => _respuesta;
            set { _respuesta = value; OnPropertyChanged("Respuesta"); }
        }

        private bool _esAlerta;
        public bool EsAlerta
        {
            get => _esAlerta;
            set
            {
                _esAlerta = value;
                OnPropertyChanged("EsAlerta");
                OnPropertyChanged("FondoRespuesta");
                OnPropertyChanged("ColorTextoRespuesta");
            }
        }

        public Brush FondoRespuesta => EsAlerta ? (Brush)new BrushConverter().ConvertFrom("#FFCDD2") : (Brush)new BrushConverter().ConvertFrom("#1E1E1E");
        public Brush ColorTextoRespuesta => EsAlerta ? Brushes.Black : Brushes.White;
    }

}