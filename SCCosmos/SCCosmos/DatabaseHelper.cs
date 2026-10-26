using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using System.IO;

namespace SCCosmos
{
    public static partial class DatabaseHelper
    {

        public static void BorrarPersonasYActividad()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;

                        cmd.CommandText = "DELETE FROM ActividadPersonas;";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "DELETE FROM TerritoriosVisitaV3;";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "DELETE FROM Personas;";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name IN ('ActividadPersonas', 'TerritoriosVisitaV3', 'Personas');";
                        cmd.ExecuteNonQuery();

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #region 1. CONFIGURACIÓN Y CORE DE BASE DE DATOS

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

        private static bool _migracionBaseDatosHecha;

        /// <summary>
        /// La base de datos vive ahora dentro de la aplicación (datos locales
        /// de este equipo). La sincronización entre dispositivos la hace el
        /// motor de cambios, sin copiar el archivo por ninguna carpeta.
        /// </summary>
        public static string RutaBaseDatos
        {
            get
            {
                string carpeta = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos");

                System.IO.Directory.CreateDirectory(carpeta);

                string ruta = System.IO.Path.Combine(carpeta, "sccosmos.db");

                if (!_migracionBaseDatosHecha)
                {
                    _migracionBaseDatosHecha = true;

                    try
                    {
                        // Primera vez con el sistema nuevo: la base de la
                        // carpeta compartida (si existe) se copia una sola
                        // vez y a partir de ahí todo va por cambios.
                        if (!System.IO.File.Exists(ruta))
                        {
                            string antigua = System.IO.Path.Combine(
                                RutaDirectorioDatos,
                                "sccosmos.db");

                            if (System.IO.File.Exists(antigua))
                            {
                                System.IO.File.Copy(antigua, ruta, true);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return ruta;
            }
        }

        private static string ConnectionString
        {
            get
            {
                // Sin agrupación de conexiones (pooling): así la app no
                // mantiene el archivo abierto entre operación y operación.
                return $"Data Source={RutaBaseDatos};Pooling=True";
            }
        }

        public static void InicializarDatabase()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                // Modo WAL: lecturas y escrituras mucho más rápidas
                // (sobre todo durante ráfagas de sincronización). Es una
                // propiedad persistente del archivo; aquí se asegura en
                // cada arranque. En Windows no hay copias del .db en vivo,
                // así que es seguro.
                try
                {
                    EjecutarQuery(connection, "PRAGMA journal_mode=WAL;");
                }
                catch
                {
                }

                //BorrarPersonasYActividad();
                CrearTablasPrincipales(connection);
                AsegurarTablasAsambleasInterno(connection);
                AsegurarTablaTareasInterno(connection);
                AsegurarTablaRegistroActividad(connection);
                AsegurarTablasEscuelasClasesInterno(connection);
                AsegurarTablasEscuelasProgramaInterno(connection);
                AsegurarTablasRscInterno(connection);
                EjecutarMigracionesYParches(connection);
            }
        }

        private static void CrearTablasPrincipales(SqliteConnection connection)
        {
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS Congregaciones (Id INTEGER PRIMARY KEY AUTOINCREMENT, Codigo TEXT, Nombre TEXT NOT NULL, Email TEXT, SeccionId INTEGER DEFAULT 0, DireccionSR TEXT, DiaEntreSemana TEXT, HoraEntreSemana TEXT, DiaFinSemana TEXT, HoraFinSemana TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, Capacidad INTEGER DEFAULT 0, Poblacion INTEGER DEFAULT 0);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS SeccionesCircuito (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT, LugarAsamblea TEXT);");

            // TABLA PERSONAS
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS Personas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT NOT NULL, FechaNacimiento TEXT, Sexo TEXT, FechaBautismo TEXT, Esperanza TEXT, Nombramiento TEXT, Servicio TEXT, CongregacionCodigo TEXT, Grupo INTEGER DEFAULT 0, Telefono TEXT, Email TEXT, FechaInicioPrec TEXT, FechaUltimaEscuela TEXT, NombreJWHub TEXT, Foto TEXT, FechaCreacion TEXT, FechaEliminacion TEXT, Solicitudes TEXT, Privilegios TEXT, RequisitoHoras INTEGER DEFAULT 0, NombreComun TEXT, Direccion TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, ConyugeId INTEGER DEFAULT 0, EmailJW TEXT, PersonaRaizId INTEGER DEFAULT 0, MesHistorico TEXT, FechaExactaHistorico TEXT, Notas TEXT, OrigenRegistro TEXT DEFAULT 'DESCONOCIDO', AprobadoExhibidores INTEGER DEFAULT 0, TrasladoAnteriorId INTEGER DEFAULT 0, TrasladoSiguienteId INTEGER DEFAULT 0, FechaTraslado TEXT);");
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS ActividadPersonas (Id INTEGER PRIMARY KEY AUTOINCREMENT, PersonaId INTEGER, Mes TEXT, AnioServicio TEXT, Participacion INTEGER, CursosBiblicos INTEGER, PrecursorAuxiliar INTEGER DEFAULT 0, Horas INTEGER, Notas TEXT, Cursos TEXT, UNIQUE(PersonaId, Mes, AnioServicio), FOREIGN KEY(PersonaId) REFERENCES Personas(Id) ON DELETE CASCADE);");

            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS Semanas (Id INTEGER PRIMARY KEY AUTOINCREMENT, FechaMartes DATETIME, Actividad TEXT, Congregacion TEXT, Tipo TEXT, Acompanante TEXT, Alojamiento TEXT, S302Enviado INTEGER DEFAULT 0, DocEnviada INTEGER DEFAULT 0, SeccionAsamblea TEXT, FechaAsamblea TEXT, NotificacionEnviada INTEGER DEFAULT 0, RecordatorioEnviado INTEGER DEFAULT 0, DatosImportados INTEGER DEFAULT 0);");
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS NotasVisita (Id INTEGER PRIMARY KEY AUTOINCREMENT, SemanaId INTEGER, Cabecera TEXT, Pregunta TEXT, Respuesta TEXT, EsAlerta INTEGER DEFAULT 0);");
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS ProgramaVisitaV2 (Id INTEGER PRIMARY KEY AUTOINCREMENT, CongreCodigo TEXT NOT NULL, MesReferencia TEXT NOT NULL, DiaSemana TEXT NOT NULL, Turno TEXT NOT NULL, Responsable TEXT NOT NULL, TipoActividad TEXT, Hora TEXT, NotasLibro TEXT, CompaneroId INTEGER, Direccion TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0);");

            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS TerritoriosVisitaV3 (Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, Nombre TEXT, UltimaFechaCompletado TEXT, AsignadoA TEXT, FechaAsignado TEXT, FechaCompletado TEXT, NombreVinculado TEXT, FechaCreacion TEXT, FechaEliminacion TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS MapeoS13S21 (Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, NombreS13 TEXT, NombreS21 TEXT);");
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS ContabilidadMes (CongreCodigo TEXT, MesReferencia TEXT, Gastos REAL, Mantenimiento REAL, ObraMundial REAL, ObraResolucion REAL, Saldo REAL, SaldoMaximo REAL DEFAULT 0, PRIMARY KEY(CongreCodigo, MesReferencia));");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS Documentos (Id INTEGER PRIMARY KEY AUTOINCREMENT, Categoria TEXT, NombreArchivo TEXT, RutaLocal TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS Configuracion (Clave TEXT PRIMARY KEY, Valor TEXT);");
            EjecutarQuery(connection, @"CREATE TABLE IF NOT EXISTS VariablesCorrespondencia (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Categoria TEXT NOT NULL,
                Etiqueta TEXT NOT NULL,
                Token TEXT NOT NULL,
                ClaveSistema TEXT,
                Orden INTEGER DEFAULT 0
            );");

            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ElementosMapa (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT, Tipo TEXT, Coordenadas TEXT, Descripcion TEXT);");
        }

        private static void AsegurarTablasAsambleasInterno(SqliteConnection connection)
        {
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ProgramaAsambleas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Anio TEXT, Tipo TEXT, Seccion TEXT, Titulo TEXT, TextoTematico TEXT, Lugar TEXT, Fecha TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ProgramaAsambleasPartes (Id INTEGER PRIMARY KEY AUTOINCREMENT, AsambleaId INTEGER, Turno TEXT, Hora TEXT, Titulo TEXT, AsignadoId INTEGER, Participantes TEXT, RutaArchivo TEXT, Tipo TEXT, Enviado INTEGER DEFAULT 0);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ProgramaAsambleasOrganigrama (Id INTEGER PRIMARY KEY AUTOINCREMENT, AsambleaId INTEGER, Departamento TEXT, AsignadoId INTEGER, Rol TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ReunionPrecursores (Id INTEGER PRIMARY KEY AUTOINCREMENT, Anio TEXT, Titulo TEXT, TextoTematico TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ReunionPrecursoresSesiones (Id INTEGER PRIMARY KEY AUTOINCREMENT, ReunionId INTEGER, Numero INTEGER, Fecha TEXT, Hora TEXT, Congregaciones TEXT, Lugar TEXT);");
            EjecutarQuery(connection, "CREATE TABLE IF NOT EXISTS ReunionPrecursoresPartes (Id INTEGER PRIMARY KEY AUTOINCREMENT, SesionId INTEGER, Hora TEXT, Titulo TEXT, AsignadoId INTEGER, Participantes TEXT, RutaArchivo TEXT, Tipo TEXT, Enviado INTEGER DEFAULT 0);");
        }

        public static void AsegurarTablasAsambleas() { InicializarDatabase(); }

        private static void EjecutarQuery(SqliteConnection conn, string query)
        {
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = query; cmd.ExecuteNonQuery(); }
        }

        #endregion

        #region 2. MIGRACIONES Y ACTUALIZACIONES DE ESQUEMA (Saneamiento)

        private static void EjecutarMigracionesYParches(SqliteConnection connection)
        {
            QuitarRestriccionUniquePersonas(connection);
            RepararActividadPersonas(connection);

            AsegurarColumna(connection, "Personas", "FechaCreacion", "TEXT");
            AsegurarColumna(connection, "Personas", "FechaEliminacion", "TEXT");
            AsegurarColumna(connection, "Personas", "Solicitudes", "TEXT");
            AsegurarColumna(connection, "Personas", "Privilegios", "TEXT");
            AsegurarColumna(connection, "Personas", "RequisitoHoras", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Personas", "NombreComun", "TEXT");
            AsegurarColumna(connection, "Personas", "Direccion", "TEXT");
            AsegurarColumna(connection, "Personas", "Latitud", "REAL DEFAULT 0");
            AsegurarColumna(connection, "Personas", "Longitud", "REAL DEFAULT 0");
            AsegurarColumna(connection, "Personas", "ConyugeId", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Personas", "EmailJW", "TEXT");

            AsegurarColumna(connection, "Personas", "PersonaRaizId", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Personas", "MesHistorico", "TEXT");
            AsegurarColumna(connection, "Personas", "FechaExactaHistorico", "TEXT");

            // <-- NUEVO CAMPO NOTAS -->
            AsegurarColumna(connection, "Personas", "Notas", "TEXT");

            // Origen del registro y autorización para predicar con exhibidores.
            AsegurarColumna(connection, "Personas", "OrigenRegistro", "TEXT DEFAULT 'DESCONOCIDO'");
            AsegurarColumna(connection, "Personas", "AprobadoExhibidores", "INTEGER DEFAULT 0");

            // Historial de traslados. Las fichas de congregaciones distintas no se
            // fusionan: quedan enlazadas en cadena para conservar cada etapa.
            AsegurarColumna(connection, "Personas", "TrasladoAnteriorId", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Personas", "TrasladoSiguienteId", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Personas", "FechaTraslado", "TEXT");

            // Las bases de datos anteriores no conocían el origen. No intentamos adivinarlo:
            // se irá completando de forma segura al volver a sincronizar con S-21 o JW Hub.
            try
            {
                EjecutarQuery(connection, "UPDATE Personas SET OrigenRegistro = 'DESCONOCIDO' WHERE OrigenRegistro IS NULL OR trim(OrigenRegistro) = '';");
            }
            catch { }

            AsegurarColumna(connection, "Semanas", "FechaAsamblea", "TEXT");
            AsegurarColumna(connection, "Semanas", "DatosImportados", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Semanas", "TipoAsamblea", "TEXT");

            AsegurarColumna(connection, "ActividadPersonas", "Cursos", "TEXT");
            AsegurarColumna(connection, "ActividadPersonas", "PrecursorAuxiliar", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "ActividadPersonas", "Notas", "TEXT");

            AsegurarColumna(connection, "NotasVisita", "EsAlerta", "INTEGER DEFAULT 0");

            // Programa de la visita: ubicación del evento.
            AsegurarColumna(connection, "ProgramaVisitaV2", "Direccion", "TEXT");
            AsegurarColumna(connection, "ProgramaVisitaV2", "Latitud", "REAL DEFAULT 0");
            AsegurarColumna(connection, "ProgramaVisitaV2", "Longitud", "REAL DEFAULT 0");

            AsegurarColumna(connection, "ProgramaAsambleasPartes", "Tipo", "TEXT");
            AsegurarColumna(connection, "ProgramaAsambleasPartes", "Enviado", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "ProgramaAsambleasOrganigrama", "Rol", "TEXT");

            AsegurarColumna(connection, "ReunionPrecursoresPartes", "Tipo", "TEXT");
            AsegurarColumna(connection, "ReunionPrecursoresPartes", "Enviado", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "ReunionPrecursoresSesiones", "Lugar", "TEXT");

            AsegurarColumna(connection, "Congregaciones", "Latitud", "REAL DEFAULT 0");
            AsegurarColumna(connection, "Congregaciones", "IdZoom", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "ClaveZoom", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "EmailAV", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "Longitud", "REAL DEFAULT 0");
            AsegurarColumna(connection, "Congregaciones", "Capacidad", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Congregaciones", "Poblacion", "INTEGER DEFAULT 0");
            AsegurarColumna(connection, "Congregaciones", "Provincia", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "Municipio", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "DiaEntreSemana", "TEXT");

            AsegurarColumna(connection, "Documentos", "ContenidoArchivo", "BLOB");
            AsegurarColumna(connection, "ContabilidadMes", "SaldoMaximo", "REAL DEFAULT 0");

            try
            {
                EjecutarQuery(connection, "UPDATE Personas SET NombreComun = NombreJWHub WHERE (NombreComun IS NULL OR trim(NombreComun) = '') AND NombreJWHub IS NOT NULL AND trim(NombreJWHub) != '';");
            }
            catch { }

            // --- LIMPIEZA AUTOMÁTICA DE RECOMENDACIONES DUPLICADAS ---
            try
            {
                EjecutarQuery(connection, @"
                    DELETE FROM RecomendacionesNombramientos 
                    WHERE Id NOT IN (
                        SELECT MAX(Id) 
                        FROM RecomendacionesNombramientos 
                        GROUP BY Hermano, Congregacion, Tipo
                    );");
            }
            catch { }
        }

        private static void AsegurarColumna(SqliteConnection connection, string tabla, string columna, string definicion)
        {
            try
            {
                bool existe = false;
                using (var cmdCheck = connection.CreateCommand())
                {
                    cmdCheck.CommandText = $"PRAGMA table_info({tabla});";
                    using (var reader = cmdCheck.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.GetString(1).Equals(columna, StringComparison.OrdinalIgnoreCase))
                            {
                                existe = true; break;
                            }
                        }
                    }
                }
                if (!existe) EjecutarQuery(connection, $"ALTER TABLE {tabla} ADD COLUMN {columna} {definicion};");
            }
            catch { }
        }

        private static void QuitarRestriccionUniquePersonas(SqliteConnection connection)
        {
            try
            {
                using (var cmdCheck = connection.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='Personas';";
                    string sql = cmdCheck.ExecuteScalar()?.ToString() ?? "";

                    if (sql.Contains("UNIQUE(Nombre, CongregacionCodigo)"))
                    {
                        var cmdCols = connection.CreateCommand();
                        cmdCols.CommandText = "PRAGMA table_info(Personas);";
                        List<string> columnas = new List<string>();
                        using (var reader = cmdCols.ExecuteReader()) { while (reader.Read()) columnas.Add(reader.GetString(1)); }
                        string colsCsv = string.Join(", ", columnas);

                        using (var tx = connection.BeginTransaction())
                        {
                            EjecutarQuery(connection, "ALTER TABLE Personas RENAME TO Personas_Old;");
                            EjecutarQuery(connection, @"CREATE TABLE Personas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT NOT NULL, FechaNacimiento TEXT, Sexo TEXT, FechaBautismo TEXT, Esperanza TEXT, Nombramiento TEXT, Servicio TEXT, CongregacionCodigo TEXT, Grupo INTEGER DEFAULT 0, Telefono TEXT, Email TEXT, FechaInicioPrec TEXT, FechaUltimaEscuela TEXT, NombreJWHub TEXT, Foto TEXT, FechaCreacion TEXT, FechaEliminacion TEXT, Solicitudes TEXT, Privilegios TEXT, RequisitoHoras INTEGER DEFAULT 0, NombreComun TEXT, Direccion TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, ConyugeId INTEGER DEFAULT 0, EmailJW TEXT, PersonaRaizId INTEGER DEFAULT 0, MesHistorico TEXT, FechaExactaHistorico TEXT, Notas TEXT, OrigenRegistro TEXT DEFAULT 'DESCONOCIDO', AprobadoExhibidores INTEGER DEFAULT 0, TrasladoAnteriorId INTEGER DEFAULT 0, TrasladoSiguienteId INTEGER DEFAULT 0, FechaTraslado TEXT);");
                            EjecutarQuery(connection, $"INSERT INTO Personas ({colsCsv}) SELECT {colsCsv} FROM Personas_Old;");
                            EjecutarQuery(connection, "DROP TABLE Personas_Old;");
                            tx.Commit();
                        }
                    }
                }
            }
            catch { }
        }

        private static void RepararActividadPersonas(SqliteConnection connection)
        {
            try
            {
                using (var cmdCheck = connection.CreateCommand())
                {
                    cmdCheck.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='ActividadPersonas';";
                    string sql = cmdCheck.ExecuteScalar()?.ToString() ?? "";

                    if (sql.Contains("Personas_Old"))
                    {
                        var cmdCols = connection.CreateCommand();
                        cmdCols.CommandText = "PRAGMA table_info(ActividadPersonas);";
                        List<string> columnas = new List<string>();
                        using (var reader = cmdCols.ExecuteReader()) { while (reader.Read()) columnas.Add(reader.GetString(1)); }
                        string colsCsv = string.Join(", ", columnas);

                        using (var tx = connection.BeginTransaction())
                        {
                            EjecutarQuery(connection, "PRAGMA foreign_keys=OFF;");
                            EjecutarQuery(connection, @"CREATE TABLE ActividadPersonas_New (Id INTEGER PRIMARY KEY AUTOINCREMENT, PersonaId INTEGER, Mes TEXT, AnioServicio TEXT, Participacion INTEGER, CursosBiblicos INTEGER, PrecursorAuxiliar INTEGER DEFAULT 0, Horas INTEGER, Notas TEXT, Cursos TEXT, UNIQUE(PersonaId, Mes, AnioServicio), FOREIGN KEY(PersonaId) REFERENCES Personas(Id) ON DELETE CASCADE);");
                            EjecutarQuery(connection, $"INSERT INTO ActividadPersonas_New ({colsCsv}) SELECT {colsCsv} FROM ActividadPersonas;");
                            EjecutarQuery(connection, "DROP TABLE ActividadPersonas;");
                            EjecutarQuery(connection, "ALTER TABLE ActividadPersonas_New RENAME TO ActividadPersonas;");
                            tx.Commit();
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region 3. GESTIÓN DE PERSONAS

        public static void ActualizarPersona(Persona p)
        {
            AnotarActividad("Personas", "Editó la ficha de «" + MostrarNombre(p) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                string dirAnterior = "";
                var cmdCheck = connection.CreateCommand();
                cmdCheck.CommandText = "SELECT Direccion FROM Personas WHERE Id = $id";
                cmdCheck.Parameters.AddWithValue("$id", p.Id);
                var resDir = cmdCheck.ExecuteScalar();
                if (resDir != null && resDir != DBNull.Value) dirAnterior = resDir.ToString();

                // Editar la ficha actual desde PersonasPage NO debe modificar snapshots
                // históricos. Si cambia la dirección solo reiniciamos las coordenadas
                // de esta ficha concreta; la instantánea de cada visita se conserva.
                if (!string.Equals((p.Direccion ?? "").Trim(), dirAnterior.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    p.Latitud = 0;
                    p.Longitud = 0;
                }

                var command = connection.CreateCommand();
                command.CommandText = @"UPDATE Personas SET 
                Nombre = $nombre, CongregacionCodigo = @CongregacionCodigo, FechaNacimiento = $fechaNacimiento, Sexo = $sexo, FechaBautismo = $fechaBautismo, Esperanza = $esperanza, Nombramiento = $nombramiento, Servicio = $servicio, Grupo = $grupo,
                NombreJWHub = $nombreJW, NombreComun = $nombreComun, Telefono = $tlf, Email = $email, Solicitudes = $sol, Privilegios = $priv, FechaInicioPrec = $inicio, FechaUltimaEscuela = $escuela, Foto = $foto,
                Direccion = $dir, Latitud = $lat, Longitud = $lon, ConyugeId = $conyuge, EmailJW = $emailjw, Notas = $notas,
                FechaEliminacion = $fechaEliminacion,
                OrigenRegistro = CASE
                    WHEN $origenRegistro IS NULL OR trim($origenRegistro) = '' OR upper($origenRegistro) = 'DESCONOCIDO' THEN OrigenRegistro
                    ELSE $origenRegistro END,
                AprobadoExhibidores = $aprobadoExhibidores
            WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", p.Id);
                command.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                command.Parameters.AddWithValue("@CongregacionCodigo", p.CongregacionCodigo ?? "");
                command.Parameters.AddWithValue("$fechaNacimiento", p.FechaNacimiento ?? "");
                command.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                command.Parameters.AddWithValue("$fechaBautismo", p.FechaBautismo ?? "");
                command.Parameters.AddWithValue("$esperanza", p.Esperanza ?? "");
                command.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                command.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                command.Parameters.AddWithValue("$grupo", p.Grupo);
                command.Parameters.AddWithValue("$nombreJW", p.NombreJWHub ?? "");
                command.Parameters.AddWithValue("$nombreComun", p.NombreComun ?? "");
                command.Parameters.AddWithValue("$tlf", p.Telefono ?? "");
                command.Parameters.AddWithValue("$email", p.Email ?? "");
                command.Parameters.AddWithValue("$sol", p.Solicitudes ?? "");
                command.Parameters.AddWithValue("$priv", p.Privilegios ?? "");
                command.Parameters.AddWithValue("$inicio", p.FechaInicioPrec ?? "");
                command.Parameters.AddWithValue("$escuela", p.FechaUltimaEscuela ?? "");
                command.Parameters.AddWithValue("$foto", p.Foto ?? "");
                command.Parameters.AddWithValue("$dir", p.Direccion ?? "");
                command.Parameters.AddWithValue("$lat", p.Latitud);
                command.Parameters.AddWithValue("$lon", p.Longitud);
                command.Parameters.AddWithValue("$conyuge", p.ConyugeId);
                command.Parameters.AddWithValue("$emailjw", p.EmailJW ?? "");
                command.Parameters.AddWithValue("$notas", p.Notas ?? "");
                command.Parameters.AddWithValue("$fechaEliminacion", p.FechaEliminacion ?? "");
                command.Parameters.AddWithValue("$origenRegistro", string.IsNullOrWhiteSpace(p.OrigenRegistro) ? "DESCONOCIDO" : p.OrigenRegistro);
                command.Parameters.AddWithValue("$aprobadoExhibidores", p.AprobadoExhibidores ? 1 : 0);

                command.ExecuteNonQuery();

            }
        }

        public static void ActualizarPersonaDesdeVisitas(Persona p, string mesReferencia)
        {
            if (p == null || p.Id <= 0) return;

            string mes = (mesReferencia ?? "").Trim();
            if (string.IsNullOrWhiteSpace(mes))
            {
                ActualizarPersona(p);
                return;
            }

            // Desde VISITAS se mantiene el comportamiento esperado de la ficha actual
            // y, además, se actualiza la instantánea del mes que se está consultando.
            // PersonasPage sigue llamando a ActualizarPersona(), por lo que nunca toca
            // snapshots históricos.
            int personaRaizId = p.PersonaRaizId > 0 ? p.PersonaRaizId : p.Id;

            // El flujo actual de Visitas abre siempre la ficha raíz. Dejamos esta
            // protección por si en el futuro se pasa directamente una instantánea.
            if (p.Id == personaRaizId)
            {
                ActualizarPersona(p);
            }
            else
            {
                var raiz = ObtenerPersonaPorId(personaRaizId);
                if (raiz != null)
                {
                    raiz.Nombre = p.Nombre;
                    raiz.NombreComun = p.NombreComun;
                    raiz.NombreJWHub = p.NombreJWHub;
                    raiz.FechaNacimiento = p.FechaNacimiento;
                    raiz.Sexo = p.Sexo;
                    raiz.FechaBautismo = p.FechaBautismo;
                    raiz.Esperanza = p.Esperanza;
                    raiz.Nombramiento = p.Nombramiento;
                    raiz.Servicio = p.Servicio;
                    raiz.CongregacionCodigo = p.CongregacionCodigo;
                    raiz.Grupo = p.Grupo;
                    raiz.Telefono = p.Telefono;
                    raiz.Email = p.Email;
                    raiz.EmailJW = p.EmailJW;
                    raiz.FechaInicioPrec = p.FechaInicioPrec;
                    raiz.FechaUltimaEscuela = p.FechaUltimaEscuela;
                    raiz.Solicitudes = p.Solicitudes;
                    raiz.Privilegios = p.Privilegios;
                    raiz.Foto = p.Foto;
                    raiz.Direccion = p.Direccion;
                    raiz.Latitud = p.Latitud;
                    raiz.Longitud = p.Longitud;
                    raiz.ConyugeId = p.ConyugeId;
                    raiz.Notas = p.Notas;
                    raiz.FechaEliminacion = p.FechaEliminacion;
                    raiz.OrigenRegistro = p.OrigenRegistro;
                    raiz.AprobadoExhibidores = p.AprobadoExhibidores;
                    ActualizarPersona(raiz);
                }
            }

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    // Puede haber snapshots antiguos duplicados por importaciones previas.
                    // Para este mes dejamos una única instantánea canónica.
                    var cmdDelete = connection.CreateCommand();
                    cmdDelete.Transaction = transaction;
                    cmdDelete.CommandText = @"
                        DELETE FROM Personas
                        WHERE PersonaRaizId = $raiz
                          AND MesHistorico = $mes;";
                    cmdDelete.Parameters.AddWithValue("$raiz", personaRaizId);
                    cmdDelete.Parameters.AddWithValue("$mes", mes);
                    cmdDelete.ExecuteNonQuery();

                    var cmdInsert = connection.CreateCommand();
                    cmdInsert.Transaction = transaction;
                    cmdInsert.CommandText = @"
                        INSERT INTO Personas
                        (Nombre, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Nombramiento, Servicio, CongregacionCodigo, Grupo,
                         Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, NombreJWHub, Foto, FechaCreacion, FechaEliminacion,
                         Solicitudes, Privilegios, RequisitoHoras, NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW,
                         PersonaRaizId, MesHistorico, FechaExactaHistorico, Notas, OrigenRegistro, AprobadoExhibidores,
                         TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado)
                        SELECT
                         Nombre, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Nombramiento, Servicio, CongregacionCodigo, Grupo,
                         Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, NombreJWHub, Foto, FechaCreacion, FechaEliminacion,
                         Solicitudes, Privilegios, RequisitoHoras, NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW,
                         $raiz, $mes, $fechaExacta, Notas, OrigenRegistro, AprobadoExhibidores,
                         TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
                        FROM Personas
                        WHERE Id = $raiz;";
                    cmdInsert.Parameters.AddWithValue("$raiz", personaRaizId);
                    cmdInsert.Parameters.AddWithValue("$mes", mes);
                    cmdInsert.Parameters.AddWithValue("$fechaExacta", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmdInsert.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }


        public static void ActualizarCoordenadas(int personaId, double lat, double lon)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Personas SET Latitud = @lat, Longitud = @lon WHERE Id = @id";
                cmd.Parameters.AddWithValue("@lat", lat);
                cmd.Parameters.AddWithValue("@lon", lon);
                cmd.Parameters.AddWithValue("@id", personaId);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<Persona> ObtenerPersonasPorCongregacion(string congreCodigo)
        {
            var lista = new List<Persona>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT Id, Nombre, Nombramiento, Servicio, Grupo, FechaNacimiento, Sexo, FechaBautismo, Esperanza,
                    NombreJWHub, FechaCreacion, FechaEliminacion, NombreComun, Solicitudes, Privilegios,
                    Foto, Email, Telefono, FechaInicioPrec, FechaUltimaEscuela, CongregacionCodigo,
                    Direccion, Latitud, Longitud, ConyugeId, EmailJW, PersonaRaizId, MesHistorico, FechaExactaHistorico, Notas, OrigenRegistro, AprobadoExhibidores, TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
             FROM Personas 
             WHERE CongregacionCodigo = $codigo AND MesHistorico IS NULL ORDER BY Nombre ASC;";
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
                            Esperanza = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            NombreJWHub = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            FechaCreacion = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            FechaEliminacion = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            NombreComun = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            Solicitudes = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            Privilegios = reader.IsDBNull(14) ? "" : reader.GetString(14),
                            Foto = reader.IsDBNull(15) ? "" : reader.GetString(15),
                            Email = reader.IsDBNull(16) ? "" : reader.GetString(16),
                            Telefono = reader.IsDBNull(17) ? "" : reader.GetString(17),
                            FechaInicioPrec = reader.IsDBNull(18) ? "" : reader.GetString(18),
                            FechaUltimaEscuela = reader.IsDBNull(19) ? "" : reader.GetString(19),
                            CongregacionCodigo = reader.IsDBNull(20) ? "" : reader.GetString(20),
                            Direccion = reader.IsDBNull(21) ? "" : reader.GetString(21),
                            Latitud = reader.IsDBNull(22) ? 0 : reader.GetDouble(22),
                            Longitud = reader.IsDBNull(23) ? 0 : reader.GetDouble(23),
                            ConyugeId = reader.IsDBNull(24) ? 0 : reader.GetInt32(24),
                            EmailJW = reader.IsDBNull(25) ? "" : reader.GetString(25),
                            PersonaRaizId = reader.IsDBNull(26) ? 0 : reader.GetInt32(26),
                            MesHistorico = reader.IsDBNull(27) ? null : reader.GetString(27),
                            FechaExactaHistorico = reader.IsDBNull(28) ? "" : reader.GetString(28),
                            Notas = reader.IsDBNull(29) ? "" : reader.GetString(29),
                            OrigenRegistro = reader.IsDBNull(30) ? "DESCONOCIDO" : reader.GetString(30),
                            AprobadoExhibidores = !reader.IsDBNull(31) && reader.GetInt32(31) == 1,
                            TrasladoAnteriorId = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
                            TrasladoSiguienteId = reader.IsDBNull(33) ? 0 : reader.GetInt32(33),
                            FechaTraslado = reader.IsDBNull(34) ? "" : reader.GetString(34)
                        });
                    }
                }
            }
            return lista;
        }

        public static List<Persona> ObtenerPersonasHistoricasPorMes(string congreCodigo, string mesReferencia)
        {
            var lista = new List<Persona>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = @"
                    SELECT Id, Nombre, Nombramiento, Servicio, Grupo, FechaNacimiento, Sexo, FechaBautismo, Esperanza,
                           NombreJWHub, FechaCreacion, FechaEliminacion, NombreComun, Solicitudes, Privilegios,
                           Foto, Email, Telefono, FechaInicioPrec, FechaUltimaEscuela, CongregacionCodigo,
                           Direccion, Latitud, Longitud, ConyugeId, EmailJW, PersonaRaizId, MesHistorico, FechaExactaHistorico, Notas, OrigenRegistro, AprobadoExhibidores, TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
                    FROM Personas 
                    WHERE Id IN (
                        SELECT MAX(Id)
                        FROM Personas
                        WHERE CongregacionCodigo = $codigo 
                        AND (
                            MesHistorico = $mes 
                            OR (MesHistorico IS NULL AND Id NOT IN (SELECT PersonaRaizId FROM Personas WHERE MesHistorico = $mes AND PersonaRaizId > 0))
                        )
                        GROUP BY CASE WHEN PersonaRaizId > 0 THEN PersonaRaizId ELSE Id END
                    )
                    ORDER BY Nombre ASC;";
                command.Parameters.AddWithValue("$codigo", congreCodigo);
                command.Parameters.AddWithValue("$mes", mesReferencia);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var p = new Persona
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Nombramiento = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Servicio = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Grupo = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            FechaNacimiento = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Sexo = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            FechaBautismo = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            Esperanza = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            NombreJWHub = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            FechaCreacion = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            FechaEliminacion = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            NombreComun = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            Solicitudes = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            Privilegios = reader.IsDBNull(14) ? "" : reader.GetString(14),
                            Foto = reader.IsDBNull(15) ? "" : reader.GetString(15),
                            Email = reader.IsDBNull(16) ? "" : reader.GetString(16),
                            Telefono = reader.IsDBNull(17) ? "" : reader.GetString(17),
                            FechaInicioPrec = reader.IsDBNull(18) ? "" : reader.GetString(18),
                            FechaUltimaEscuela = reader.IsDBNull(19) ? "" : reader.GetString(19),
                            CongregacionCodigo = reader.IsDBNull(20) ? "" : reader.GetString(20),
                            Direccion = reader.IsDBNull(21) ? "" : reader.GetString(21),
                            Latitud = reader.IsDBNull(22) ? 0 : reader.GetDouble(22),
                            Longitud = reader.IsDBNull(23) ? 0 : reader.GetDouble(23),
                            ConyugeId = reader.IsDBNull(24) ? 0 : reader.GetInt32(24),
                            EmailJW = reader.IsDBNull(25) ? "" : reader.GetString(25),
                            PersonaRaizId = reader.IsDBNull(26) ? 0 : reader.GetInt32(26),
                            MesHistorico = reader.IsDBNull(27) ? null : reader.GetString(27),
                            FechaExactaHistorico = reader.IsDBNull(28) ? "" : reader.GetString(28),
                            Notas = reader.IsDBNull(29) ? "" : reader.GetString(29),
                            OrigenRegistro = reader.IsDBNull(30) ? "DESCONOCIDO" : reader.GetString(30),
                            AprobadoExhibidores = !reader.IsDBNull(31) && reader.GetInt32(31) == 1,
                            TrasladoAnteriorId = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
                            TrasladoSiguienteId = reader.IsDBNull(33) ? 0 : reader.GetInt32(33),
                            FechaTraslado = reader.IsDBNull(34) ? "" : reader.GetString(34)
                        };
                        lista.Add(p);
                    }
                }
            }
            return lista;
        }

        public static Persona ObtenerPersonaPorId(int personaId)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT Id, Nombre, Nombramiento, Servicio, Grupo, FechaNacimiento, Sexo, FechaBautismo, Esperanza,
                    NombreJWHub, FechaCreacion, FechaEliminacion, NombreComun, Solicitudes, Privilegios,
                    Foto, Email, Telefono, FechaInicioPrec, FechaUltimaEscuela,
                   Direccion, Latitud, Longitud, ConyugeId, EmailJW, CongregacionCodigo, PersonaRaizId, MesHistorico, FechaExactaHistorico, Notas, OrigenRegistro, AprobadoExhibidores, TrasladoAnteriorId, TrasladoSiguienteId, FechaTraslado
             FROM Personas WHERE Id = $id;";
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
                            Esperanza = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            NombreJWHub = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            FechaCreacion = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            FechaEliminacion = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            NombreComun = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            Solicitudes = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            Privilegios = reader.IsDBNull(14) ? "" : reader.GetString(14),
                            Foto = reader.IsDBNull(15) ? "" : reader.GetString(15),
                            Email = reader.IsDBNull(16) ? "" : reader.GetString(16),
                            Telefono = reader.IsDBNull(17) ? "" : reader.GetString(17),
                            FechaInicioPrec = reader.IsDBNull(18) ? "" : reader.GetString(18),
                            FechaUltimaEscuela = reader.IsDBNull(19) ? "" : reader.GetString(19),
                            Direccion = reader.IsDBNull(20) ? "" : reader.GetString(20),
                            Latitud = reader.IsDBNull(21) ? 0 : reader.GetDouble(21),
                            Longitud = reader.IsDBNull(22) ? 0 : reader.GetDouble(22),
                            ConyugeId = reader.IsDBNull(23) ? 0 : reader.GetInt32(23),
                            EmailJW = reader.IsDBNull(24) ? "" : reader.GetString(24),
                            CongregacionCodigo = reader.IsDBNull(25) ? "" : reader.GetString(25),
                            PersonaRaizId = reader.IsDBNull(26) ? 0 : reader.GetInt32(26),
                            MesHistorico = reader.IsDBNull(27) ? null : reader.GetString(27),
                            FechaExactaHistorico = reader.IsDBNull(28) ? "" : reader.GetString(28),
                            Notas = reader.IsDBNull(29) ? "" : reader.GetString(29),
                            OrigenRegistro = reader.IsDBNull(30) ? "DESCONOCIDO" : reader.GetString(30),
                            AprobadoExhibidores = !reader.IsDBNull(31) && reader.GetInt32(31) == 1,
                            TrasladoAnteriorId = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
                            TrasladoSiguienteId = reader.IsDBNull(33) ? 0 : reader.GetInt32(33),
                            FechaTraslado = reader.IsDBNull(34) ? "" : reader.GetString(34)
                        };
                    }
                }
            }
            return null;
        }

        public static List<Persona> ObtenerPersonas()
        {
            var lista = new List<Persona>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @" SELECT p.Id, p.Nombre, p.FechaNacimiento, p.Sexo, p.FechaBautismo, p.Esperanza, p.Nombramiento, p.Servicio, p.CongregacionCodigo, c.Nombre, p.Grupo, p.NombreJWHub, p.FechaCreacion, p.FechaEliminacion, p.Solicitudes, p.Privilegios, p.Foto, p.NombreComun, p.Telefono, p.Email, p.FechaInicioPrec, p.FechaUltimaEscuela, p.Direccion, p.Latitud, p.Longitud, p.ConyugeId, p.EmailJW, p.PersonaRaizId, p.MesHistorico, p.FechaExactaHistorico, p.Notas, p.OrigenRegistro, p.AprobadoExhibidores, p.TrasladoAnteriorId, p.TrasladoSiguienteId, p.FechaTraslado
                                         FROM Personas p LEFT JOIN Congregaciones c ON p.CongregacionCodigo = c.Codigo WHERE p.MesHistorico IS NULL ORDER BY p.Nombre ASC;";

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
                            Foto = reader.IsDBNull(16) ? "" : reader.GetString(16),
                            NombreComun = reader.IsDBNull(17) ? "" : reader.GetString(17),
                            Telefono = reader.IsDBNull(18) ? "" : reader.GetString(18),
                            Email = reader.IsDBNull(19) ? "" : reader.GetString(19),
                            FechaInicioPrec = reader.IsDBNull(20) ? "" : reader.GetString(20),
                            FechaUltimaEscuela = reader.IsDBNull(21) ? "" : reader.GetString(21),
                            Direccion = reader.IsDBNull(22) ? "" : reader.GetString(22),
                            Latitud = reader.IsDBNull(23) ? 0 : reader.GetDouble(23),
                            Longitud = reader.IsDBNull(24) ? 0 : reader.GetDouble(24),
                            ConyugeId = reader.IsDBNull(25) ? 0 : reader.GetInt32(25),
                            EmailJW = reader.IsDBNull(26) ? "" : reader.GetString(26),
                            PersonaRaizId = reader.IsDBNull(27) ? 0 : reader.GetInt32(27),
                            MesHistorico = reader.IsDBNull(28) ? null : reader.GetString(28),
                            FechaExactaHistorico = reader.IsDBNull(29) ? "" : reader.GetString(29),
                            Notas = reader.IsDBNull(30) ? "" : reader.GetString(30),
                            OrigenRegistro = reader.IsDBNull(31) ? "DESCONOCIDO" : reader.GetString(31),
                            AprobadoExhibidores = !reader.IsDBNull(32) && reader.GetInt32(32) == 1,
                            TrasladoAnteriorId = reader.IsDBNull(33) ? 0 : reader.GetInt32(33),
                            TrasladoSiguienteId = reader.IsDBNull(34) ? 0 : reader.GetInt32(34),
                            FechaTraslado = reader.IsDBNull(35) ? "" : reader.GetString(35)
                        });
                    }
                }
            }
            return lista;
        }

        public static int InsertarPersonaNueva(Persona p)
        {
            AnotarActividad("Personas", "Añadió la persona «" + MostrarNombre(p) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();

                cmd.CommandText = @"
                    INSERT INTO Personas (Nombre, CongregacionCodigo, Grupo, Nombramiento, Servicio, Solicitudes, Privilegios, Foto, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, NombreJWHub, FechaCreacion, NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW, Notas, OrigenRegistro, AprobadoExhibidores)
                    VALUES ($nombre, $codigo, $grupo, $nomb, $serv, $sol, $priv, $foto, $fn, $sexo, $fb, $esperanza, $tlf, $email, $inicio, $escuela, $jw, $fc, $nombreComun, $dir, $lat, $lon, $conyuge, $emailjw, $notas, $origenRegistro, $aprobadoExhibidores);
                    SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                cmd.Parameters.AddWithValue("$codigo", p.CongregacionCodigo ?? "");
                cmd.Parameters.AddWithValue("$grupo", p.Grupo);
                cmd.Parameters.AddWithValue("$nomb", p.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$serv", p.Servicio ?? "");
                cmd.Parameters.AddWithValue("$sol", p.Solicitudes ?? "");
                cmd.Parameters.AddWithValue("$priv", p.Privilegios ?? "");
                cmd.Parameters.AddWithValue("$foto", p.Foto ?? "");
                cmd.Parameters.AddWithValue("$fn", p.FechaNacimiento ?? "");
                cmd.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                cmd.Parameters.AddWithValue("$fb", p.FechaBautismo ?? "");
                cmd.Parameters.AddWithValue("$esperanza", p.Esperanza ?? "");
                cmd.Parameters.AddWithValue("$tlf", p.Telefono ?? "");
                cmd.Parameters.AddWithValue("$email", p.Email ?? "");
                cmd.Parameters.AddWithValue("$inicio", p.FechaInicioPrec ?? "");
                cmd.Parameters.AddWithValue("$escuela", p.FechaUltimaEscuela ?? "");
                cmd.Parameters.AddWithValue("$jw", p.NombreJWHub ?? "");
                cmd.Parameters.AddWithValue("$fc", p.FechaCreacion ?? "");
                cmd.Parameters.AddWithValue("$nombreComun", p.NombreComun ?? "");
                cmd.Parameters.AddWithValue("$dir", p.Direccion ?? "");
                cmd.Parameters.AddWithValue("$lat", p.Latitud);
                cmd.Parameters.AddWithValue("$lon", p.Longitud);
                cmd.Parameters.AddWithValue("$conyuge", p.ConyugeId);
                cmd.Parameters.AddWithValue("$emailjw", p.EmailJW ?? "");
                cmd.Parameters.AddWithValue("$notas", p.Notas ?? "");
                cmd.Parameters.AddWithValue("$origenRegistro", string.IsNullOrWhiteSpace(p.OrigenRegistro) ? "Manual" : p.OrigenRegistro);
                cmd.Parameters.AddWithValue("$aprobadoExhibidores", p.AprobadoExhibidores ? 1 : 0);

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Registra un traslado entre congregaciones sin fusionar las fichas.
        /// La ficha anterior se marca como baja y la nueva queda enlazada a ella.
        /// La actividad mensual NO se copia: cada congregación conserva su propia etapa.
        /// </summary>
        public static int RegistrarTraslado(Persona personaAnterior, Persona personaHubNueva, DateTime fechaTraslado)
        {
            if (personaAnterior == null) throw new ArgumentNullException(nameof(personaAnterior));
            if (personaHubNueva == null) throw new ArgumentNullException(nameof(personaHubNueva));
            if (personaAnterior.Id <= 0) throw new ArgumentException("La persona anterior no tiene un Id válido.");
            if (string.IsNullOrWhiteSpace(personaHubNueva.CongregacionCodigo)) throw new ArgumentException("La nueva congregación no es válida.");
            if (string.Equals(personaAnterior.CongregacionCodigo, personaHubNueva.CongregacionCodigo, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Un traslado debe realizarse entre congregaciones distintas.");

            string fecha = fechaTraslado.ToString("dd/MM/yyyy");

            AnotarActividad(
                "Personas",
                "Registró el traslado de «" + MostrarNombre(personaAnterior) + "» (" +
                personaAnterior.CongregacionCodigo + " → " +
                personaHubNueva.CongregacionCodigo + ")");

            string Elegir(string preferido, string alternativo)
                => !string.IsNullOrWhiteSpace(preferido) ? preferido : (alternativo ?? "");

            // Nombre completo y nombre común consolidados se conservan de la ficha anterior.
            // NombreJWHub sí se toma de la nueva lectura de Hub para mantener la identidad Hub.
            string nombre = Elegir(personaAnterior.Nombre, personaHubNueva.Nombre);
            string nombreComun = Elegir(personaAnterior.NombreComun, personaHubNueva.NombreComun);
            string nombreJw = !string.IsNullOrWhiteSpace(personaHubNueva.NombreJWHub)
                ? personaHubNueva.NombreJWHub
                : (!string.IsNullOrWhiteSpace(personaHubNueva.Nombre) ? personaHubNueva.Nombre : (personaAnterior.NombreJWHub ?? ""));

            string fechaNacimiento = Elegir(personaAnterior.FechaNacimiento, personaHubNueva.FechaNacimiento);
            string fechaBautismo = Elegir(personaAnterior.FechaBautismo, personaHubNueva.FechaBautismo);
            string sexo = Elegir(personaAnterior.Sexo, personaHubNueva.Sexo);
            string esperanza = Elegir(personaHubNueva.Esperanza, personaAnterior.Esperanza);

            // Para campos personales/contacto, Hub aporta el dato actual si existe;
            // si la extracción viene vacía, conservamos el dato anterior.
            string telefono = Elegir(personaHubNueva.Telefono, personaAnterior.Telefono);
            string email = Elegir(personaHubNueva.Email, personaAnterior.Email);
            string emailJw = Elegir(personaHubNueva.EmailJW, personaAnterior.EmailJW);
            string direccion = Elegir(personaHubNueva.Direccion, personaAnterior.Direccion);
            string foto = Elegir(personaHubNueva.Foto, personaAnterior.Foto);
            string inicioPrec = Elegir(personaHubNueva.FechaInicioPrec, personaAnterior.FechaInicioPrec);
            string ultimaEscuela = Elegir(personaHubNueva.FechaUltimaEscuela, personaAnterior.FechaUltimaEscuela);

            double latitud = personaHubNueva.Latitud;
            double longitud = personaHubNueva.Longitud;
            if ((Math.Abs(latitud) < 0.000001 && Math.Abs(longitud) < 0.000001)
                && string.Equals((direccion ?? "").Trim(), (personaAnterior.Direccion ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                latitud = personaAnterior.Latitud;
                longitud = personaAnterior.Longitud;
            }

            string origen = CombinarOrigenes(personaAnterior.OrigenRegistro, personaHubNueva.OrigenRegistro);
            origen = CombinarOrigenes(origen, "JWHub");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int yaTrasladadoA = 0;
                        using (var cmdCheck = connection.CreateCommand())
                        {
                            cmdCheck.Transaction = transaction;
                            cmdCheck.CommandText = "SELECT COALESCE(TrasladoSiguienteId, 0) FROM Personas WHERE Id = $id;";
                            cmdCheck.Parameters.AddWithValue("$id", personaAnterior.Id);
                            object obj = cmdCheck.ExecuteScalar();
                            if (obj == null || obj == DBNull.Value)
                                throw new InvalidOperationException("La ficha anterior ya no existe.");
                            yaTrasladadoA = Convert.ToInt32(obj);
                        }

                        if (yaTrasladadoA > 0)
                            throw new InvalidOperationException("Esta ficha ya tiene registrado un traslado posterior.");

                        // Si el cónyuge también se trasladó previamente, enlazamos con su nueva ficha.
                        int conyugeNuevoId = 0;
                        if (personaAnterior.ConyugeId > 0)
                        {
                            using (var cmdConyuge = connection.CreateCommand())
                            {
                                cmdConyuge.Transaction = transaction;
                                cmdConyuge.CommandText = "SELECT COALESCE(TrasladoSiguienteId, 0) FROM Personas WHERE Id = $id;";
                                cmdConyuge.Parameters.AddWithValue("$id", personaAnterior.ConyugeId);
                                object obj = cmdConyuge.ExecuteScalar();
                                if (obj != null && obj != DBNull.Value) conyugeNuevoId = Convert.ToInt32(obj);
                            }
                        }

                        int nuevoId;
                        using (var cmdInsert = connection.CreateCommand())
                        {
                            cmdInsert.Transaction = transaction;
                            cmdInsert.CommandText = @"
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
                                SELECT last_insert_rowid();";

                            cmdInsert.Parameters.AddWithValue("$nombre", nombre ?? "");
                            cmdInsert.Parameters.AddWithValue("$codigo", personaHubNueva.CongregacionCodigo ?? "");
                            // Nombramiento/servicio/solicitudes/privilegios reflejan el estado actual
                            // de Hub. Un vacío aquí puede representar que el privilegio terminó.
                            cmdInsert.Parameters.AddWithValue("$nomb", personaHubNueva.Nombramiento ?? "");
                            cmdInsert.Parameters.AddWithValue("$serv", personaHubNueva.Servicio ?? "");
                            cmdInsert.Parameters.AddWithValue("$sol", personaHubNueva.Solicitudes ?? "");
                            cmdInsert.Parameters.AddWithValue("$priv", personaHubNueva.Privilegios ?? "");
                            cmdInsert.Parameters.AddWithValue("$foto", foto ?? "");
                            cmdInsert.Parameters.AddWithValue("$fn", fechaNacimiento ?? "");
                            cmdInsert.Parameters.AddWithValue("$sexo", sexo ?? "");
                            cmdInsert.Parameters.AddWithValue("$fb", fechaBautismo ?? "");
                            cmdInsert.Parameters.AddWithValue("$esperanza", esperanza ?? "");
                            cmdInsert.Parameters.AddWithValue("$tlf", telefono ?? "");
                            cmdInsert.Parameters.AddWithValue("$email", email ?? "");
                            cmdInsert.Parameters.AddWithValue("$inicio", inicioPrec ?? "");
                            cmdInsert.Parameters.AddWithValue("$escuela", ultimaEscuela ?? "");
                            cmdInsert.Parameters.AddWithValue("$jw", nombreJw ?? "");
                            cmdInsert.Parameters.AddWithValue("$fc", fecha);
                            cmdInsert.Parameters.AddWithValue("$nombreComun", nombreComun ?? "");
                            cmdInsert.Parameters.AddWithValue("$dir", direccion ?? "");
                            cmdInsert.Parameters.AddWithValue("$lat", latitud);
                            cmdInsert.Parameters.AddWithValue("$lon", longitud);
                            cmdInsert.Parameters.AddWithValue("$conyuge", conyugeNuevoId);
                            cmdInsert.Parameters.AddWithValue("$emailjw", emailJw ?? "");
                            cmdInsert.Parameters.AddWithValue("$notas", personaAnterior.Notas ?? "");
                            cmdInsert.Parameters.AddWithValue("$origen", origen);
                            cmdInsert.Parameters.AddWithValue("$aprobado", personaAnterior.AprobadoExhibidores ? 1 : 0);
                            int requisito = personaHubNueva.RequisitoHoras > 0 ? personaHubNueva.RequisitoHoras : personaAnterior.RequisitoHoras;
                            cmdInsert.Parameters.AddWithValue("$requisito", requisito);
                            cmdInsert.Parameters.AddWithValue("$anteriorId", personaAnterior.Id);
                            cmdInsert.Parameters.AddWithValue("$fechaTraslado", fecha);

                            nuevoId = Convert.ToInt32(cmdInsert.ExecuteScalar());
                        }

                        using (var cmdAnterior = connection.CreateCommand())
                        {
                            cmdAnterior.Transaction = transaction;
                            cmdAnterior.CommandText = @"
                                UPDATE Personas
                                SET FechaEliminacion = $fecha,
                                    TrasladoSiguienteId = $nuevoId,
                                    FechaTraslado = $fecha
                                WHERE Id = $anteriorId;";
                            cmdAnterior.Parameters.AddWithValue("$fecha", fecha);
                            cmdAnterior.Parameters.AddWithValue("$nuevoId", nuevoId);
                            cmdAnterior.Parameters.AddWithValue("$anteriorId", personaAnterior.Id);
                            cmdAnterior.ExecuteNonQuery();
                        }

                        // Si el cónyuge también tiene una nueva ficha, reconstruimos la relación
                        // entre las dos fichas actuales y no dejamos referencias a las antiguas.
                        if (conyugeNuevoId > 0)
                        {
                            using (var cmdPareja = connection.CreateCommand())
                            {
                                cmdPareja.Transaction = transaction;
                                cmdPareja.CommandText = "UPDATE Personas SET ConyugeId = $nuevoId WHERE Id = $conyugeNuevoId;";
                                cmdPareja.Parameters.AddWithValue("$nuevoId", nuevoId);
                                cmdPareja.Parameters.AddWithValue("$conyugeNuevoId", conyugeNuevoId);
                                cmdPareja.ExecuteNonQuery();
                            }
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
            }
        }

        /// <summary>
        /// Añade un origen al registro sin borrar los que ya tuviera.
        /// Valores habituales: Manual, S21 y JWHub.
        /// </summary>
        public static void MarcarOrigenPersona(int personaId, string origen)
        {
            if (personaId <= 0 || string.IsNullOrWhiteSpace(origen)) return;

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                string actual = "DESCONOCIDO";
                using (var cmdGet = connection.CreateCommand())
                {
                    cmdGet.CommandText = "SELECT OrigenRegistro FROM Personas WHERE Id = $id;";
                    cmdGet.Parameters.AddWithValue("$id", personaId);
                    var obj = cmdGet.ExecuteScalar();
                    if (obj != null && obj != DBNull.Value && !string.IsNullOrWhiteSpace(obj.ToString()))
                        actual = obj.ToString();
                }

                string combinado = CombinarOrigenes(actual, origen);
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "UPDATE Personas SET OrigenRegistro = $origen WHERE Id = $id;";
                    cmd.Parameters.AddWithValue("$origen", combinado);
                    cmd.Parameters.AddWithValue("$id", personaId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static string CombinarOrigenes(string origenActual, string nuevoOrigen)
        {
            var origenes = new List<string>();

            Action<string> agregar = valor =>
            {
                if (string.IsNullOrWhiteSpace(valor)) return;
                foreach (var parte in valor.Split(new[] { '+', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string limpia = parte.Trim();
                    if (limpia.Equals("DESCONOCIDO", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!origenes.Any(x => x.Equals(limpia, StringComparison.OrdinalIgnoreCase))) origenes.Add(limpia);
                }
            };

            agregar(origenActual);
            agregar(nuevoOrigen);

            return origenes.Count == 0 ? "DESCONOCIDO" : string.Join("+", origenes);
        }

        public static void EliminarPersona(int personaId)
        {
            AnotarActividad("Personas", "Eliminó la ficha de «" + NombrePersonaPorId(personaId) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    // Si se elimina físicamente una ficha, limpiamos los enlaces de
                    // traslado para no dejar Ids colgantes en la cadena histórica.
                    var cmdTraslados = connection.CreateCommand();
                    cmdTraslados.Transaction = transaction;
                    cmdTraslados.CommandText = @"
                        UPDATE Personas SET TrasladoAnteriorId = 0 WHERE TrasladoAnteriorId = $id;
                        UPDATE Personas SET TrasladoSiguienteId = 0 WHERE TrasladoSiguienteId = $id;";
                    cmdTraslados.Parameters.AddWithValue("$id", personaId);
                    cmdTraslados.ExecuteNonQuery();

                    var cmdActividad = connection.CreateCommand();
                    cmdActividad.Transaction = transaction;
                    cmdActividad.CommandText = "DELETE FROM ActividadPersonas WHERE PersonaId = $id OR PersonaId IN (SELECT Id FROM Personas WHERE PersonaRaizId = $id);";
                    cmdActividad.Parameters.AddWithValue("$id", personaId);
                    cmdActividad.ExecuteNonQuery();

                    var cmdPersona = connection.CreateCommand();
                    cmdPersona.Transaction = transaction;
                    cmdPersona.CommandText = "DELETE FROM Personas WHERE Id = $id OR PersonaRaizId = $id;";
                    cmdPersona.Parameters.AddWithValue("$id", personaId);
                    cmdPersona.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }

        public static void MarcarPersonaEliminada(int idPersona)
        {
            AnotarActividad("Personas", "Dio de baja a «" + NombrePersonaPorId(idPersona) + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Personas SET FechaEliminacion = $fecha WHERE Id = $id OR PersonaRaizId = $id;";
                cmd.Parameters.AddWithValue("$fecha", DateTime.Today.ToString("dd/MM/yyyy"));
                cmd.Parameters.AddWithValue("$id", idPersona);
                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarRequisitoSilencioso(int personaId, int requisito)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Personas SET RequisitoHoras = $req WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$req", requisito);
                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.ExecuteNonQuery();
            }
        }

        public static bool TienePersonas(string congreCodigo)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Personas WHERE CongregacionCodigo = $codigo AND MesHistorico IS NULL;";
                command.Parameters.AddWithValue("$codigo", congreCodigo);
                return (long)command.ExecuteScalar() > 0;
            }
        }

        public static void GuardarRecomendaciones(List<RecomendacionHub> recomendaciones)
        {
            if (recomendaciones == null)
                recomendaciones = new List<RecomendacionHub>();

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmdCreate = connection.CreateCommand();
                cmdCreate.CommandText = @"
                    CREATE TABLE IF NOT EXISTS RecomendacionesNombramientos (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Hermano TEXT,
                        Tipo TEXT,
                        Congregacion TEXT,
                        Fecha TEXT,
                        FechaDescarga TEXT,
                        UrlDetalle TEXT,
                        RecomendadoComo TEXT
                    )";
                cmdCreate.ExecuteNonQuery();

                try { cmdCreate.CommandText = "ALTER TABLE RecomendacionesNombramientos ADD COLUMN UrlDetalle TEXT"; cmdCreate.ExecuteNonQuery(); } catch { }
                try { cmdCreate.CommandText = "ALTER TABLE RecomendacionesNombramientos ADD COLUMN RecomendadoComo TEXT"; cmdCreate.ExecuteNonQuery(); } catch { }

                // Esta tabla representa la bandeja ACTUAL de recomendaciones de JW Hub.
                // Cuando la página de Hub se ha cargado correctamente, sustituimos el
                // contenido completo en una transacción. Así desaparecen también las
                // recomendaciones que ya no existen en la web, incluso si una
                // congregación se ha quedado con cero recomendaciones.
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var cmdDelete = connection.CreateCommand();
                        cmdDelete.Transaction = transaction;
                        cmdDelete.CommandText = "DELETE FROM RecomendacionesNombramientos;";
                        cmdDelete.ExecuteNonQuery();

                        foreach (var r in recomendaciones)
                        {
                            var cmdInsert = connection.CreateCommand();
                            cmdInsert.Transaction = transaction;
                            cmdInsert.CommandText = @"
                                INSERT INTO RecomendacionesNombramientos
                                    (Hermano, Tipo, Congregacion, Fecha, FechaDescarga, UrlDetalle, RecomendadoComo)
                                VALUES
                                    (@h, @t, @c, @f, @fd, @url, @recComo);";
                            cmdInsert.Parameters.AddWithValue("@h", r.Hermano ?? "");
                            cmdInsert.Parameters.AddWithValue("@t", r.Tipo ?? "");
                            cmdInsert.Parameters.AddWithValue("@c", r.Congregacion ?? "");
                            cmdInsert.Parameters.AddWithValue("@f", r.Fecha ?? "");
                            cmdInsert.Parameters.AddWithValue("@fd", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                            cmdInsert.Parameters.AddWithValue("@url", r.UrlDetalle ?? "");
                            cmdInsert.Parameters.AddWithValue("@recComo", r.RecomendadoComo ?? "");
                            cmdInsert.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public static void ActualizarRecomendadoComo(string hermano, string recomendadoComo)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE RecomendacionesNombramientos SET RecomendadoComo = @rec WHERE Hermano = @h";
                cmd.Parameters.AddWithValue("@rec", recomendadoComo);
                cmd.Parameters.AddWithValue("@h", hermano);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<RecomendacionHub> ObtenerRecomendaciones()
        {
            var lista = new List<RecomendacionHub>();
            try
            {
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Hermano, Tipo, Congregacion, Fecha, FechaDescarga, UrlDetalle, RecomendadoComo FROM RecomendacionesNombramientos ORDER BY Congregacion, Tipo, Hermano";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new RecomendacionHub
                            {
                                Hermano = reader["Hermano"].ToString(),
                                Tipo = reader["Tipo"].ToString(),
                                Congregacion = reader["Congregacion"].ToString(),
                                Fecha = reader["Fecha"].ToString(),
                                UrlDetalle = reader["UrlDetalle"].ToString(),
                                RecomendadoComo = reader["RecomendadoComo"].ToString()
                            });
                        }
                    }
                }
            }
            catch { }

            return lista;
        }

        // S-303 puede procesar varias decisiones de la misma persona.
        // Borrar por nombre/congregación eliminaría también las pendientes.
        // UrlDetalle identifica la recomendación concreta extraída de Hub.
        public static void EliminarRecomendacionProcesada(RecomendacionHub recomendacion)
        {
            if (recomendacion == null || string.IsNullOrWhiteSpace(recomendacion.UrlDetalle))
                throw new ArgumentException("La recomendación procesada debe tener URL de detalle.");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM RecomendacionesNombramientos
                                    WHERE UrlDetalle = $url
                                      AND Congregacion = $congre
                                      AND Hermano = $hermano;";
                cmd.Parameters.AddWithValue("$url", recomendacion.UrlDetalle);
                cmd.Parameters.AddWithValue("$congre", recomendacion.Congregacion ?? "");
                cmd.Parameters.AddWithValue("$hermano", recomendacion.Hermano ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void EliminarRecomendacion(string hermano, string congregacion)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM RecomendacionesNombramientos WHERE Hermano = @h AND Congregacion = @c";
                cmd.Parameters.AddWithValue("@h", hermano);
                cmd.Parameters.AddWithValue("@c", congregacion);
                cmd.ExecuteNonQuery();
            }
        }


        #endregion

        #region 4. GESTIÓN DE IMPORTACIÓN Y VERSIONADO JSON

        public static void InsertarPersonaImportada(Persona p, string codigoCongregacionExtraido)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Personas (Nombre, Servicio, Nombramiento, Telefono, Email, EmailJW, Direccion, FechaInicioPrec, FechaUltimaEscuela, CongregacionCodigo, Grupo, NombreJWHub, FechaNacimiento, Solicitudes, Privilegios, Foto, FechaCreacion, NombreComun, Latitud, Longitud, OrigenRegistro, AprobadoExhibidores)
                    VALUES ($nombre, $servicio, $nombramiento, $tlf, $email, $emailjw, $dir, $inicio, $escuela, $codigo, 0, $nombreJW, $fechaNac, $sol, $priv, $foto, $fc, $nombreComun, 0, 0, 'JWHub', $aprobadoExhibidores);";
                cmd.Parameters.AddWithValue("$nombre", p.Nombre ?? "");
                cmd.Parameters.AddWithValue("$servicio", p.Servicio ?? "");
                cmd.Parameters.AddWithValue("$nombramiento", p.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$tlf", p.Telefono ?? "");
                cmd.Parameters.AddWithValue("$email", p.Email ?? "");
                cmd.Parameters.AddWithValue("$emailjw", p.EmailJW ?? "");
                cmd.Parameters.AddWithValue("$dir", p.Direccion ?? "");
                cmd.Parameters.AddWithValue("$inicio", p.FechaInicioPrec ?? "");
                cmd.Parameters.AddWithValue("$escuela", p.FechaUltimaEscuela ?? "");
                cmd.Parameters.AddWithValue("$codigo", codigoCongregacionExtraido ?? "");
                cmd.Parameters.AddWithValue("$nombreJW", p.NombreJWHub ?? "");
                cmd.Parameters.AddWithValue("$fechaNac", p.FechaNacimiento ?? "");
                cmd.Parameters.AddWithValue("$sol", p.Solicitudes ?? "");
                cmd.Parameters.AddWithValue("$priv", p.Privilegios ?? "");
                cmd.Parameters.AddWithValue("$foto", p.Foto ?? "");
                cmd.Parameters.AddWithValue("$fc", p.FechaCreacion ?? "");
                cmd.Parameters.AddWithValue("$nombreComun", p.NombreComun ?? "");
                cmd.Parameters.AddWithValue("$aprobadoExhibidores", p.AprobadoExhibidores ? 1 : 0);
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
                        Servicio = CASE WHEN $servicio != '' THEN $servicio ELSE Servicio END, 
                        Nombramiento = CASE WHEN $nombramiento != '' THEN $nombramiento ELSE Nombramiento END, 
                        NombreJWHub = $nombreJW,
                        NombreComun = CASE WHEN (NombreComun IS NULL OR trim(NombreComun) = '') THEN $nombreJW ELSE NombreComun END,
                        Solicitudes = CASE WHEN $solicitudes != '' THEN $solicitudes ELSE Solicitudes END,
                        Privilegios = CASE WHEN $privilegios != '' THEN $privilegios ELSE Privilegios END,
                        Foto = CASE WHEN $foto != '' THEN $foto ELSE Foto END,
                        CongregacionCodigo = CASE WHEN $codigo != '' THEN $codigo ELSE CongregacionCodigo END,
                        Telefono = CASE WHEN $tlf != '' THEN $tlf ELSE Telefono END,
                        Email = CASE WHEN $email != '' THEN $email ELSE Email END,
                        EmailJW = CASE WHEN $emailjw != '' THEN $emailjw ELSE EmailJW END,
                        Latitud = CASE WHEN $dir != '' AND trim($dir) != trim(Direccion) THEN 0 ELSE Latitud END,
                        Longitud = CASE WHEN $dir != '' AND trim($dir) != trim(Direccion) THEN 0 ELSE Longitud END,
                        Direccion = CASE WHEN $dir != '' THEN $dir ELSE Direccion END,
                        FechaInicioPrec = CASE WHEN $inicio != '' THEN $inicio ELSE FechaInicioPrec END,
                        FechaUltimaEscuela = CASE WHEN $escuela != '' THEN $escuela ELSE FechaUltimaEscuela END,
                        FechaNacimiento = CASE WHEN (FechaNacimiento IS NULL OR FechaNacimiento = '' OR FechaNacimiento LIKE '01/01/%') AND $fechaNac != '' THEN $fechaNac ELSE FechaNacimiento END,
                        OrigenRegistro = CASE
                            WHEN OrigenRegistro IS NULL OR trim(OrigenRegistro) = '' OR upper(OrigenRegistro) = 'DESCONOCIDO' THEN 'JWHub'
                            WHEN instr(lower(OrigenRegistro), 'jwhub') = 0 THEN OrigenRegistro || '+JWHub'
                            ELSE OrigenRegistro END
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$servicio", prec.Servicio ?? "");
                cmd.Parameters.AddWithValue("$nombramiento", prec.Nombramiento ?? "");
                cmd.Parameters.AddWithValue("$nombreJW", prec.NombreCompleto ?? "");
                cmd.Parameters.AddWithValue("$solicitudes", prec.Solicitudes ?? "");
                cmd.Parameters.AddWithValue("$privilegios", prec.Privilegios ?? "");
                cmd.Parameters.AddWithValue("$foto", prec.Foto ?? "");
                cmd.Parameters.AddWithValue("$codigo", prec.Congregacion ?? "");
                cmd.Parameters.AddWithValue("$fechaNac", prec.FechaNacimiento ?? "");
                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.Parameters.AddWithValue("$tlf", prec.Telefono ?? "");
                cmd.Parameters.AddWithValue("$email", prec.Email ?? "");
                cmd.Parameters.AddWithValue("$emailjw", prec.EmailJW ?? "");
                cmd.Parameters.AddWithValue("$dir", prec.Direccion ?? "");
                cmd.Parameters.AddWithValue("$inicio", prec.FechaInicioPrec ?? "");
                cmd.Parameters.AddWithValue("$escuela", prec.FechaUltimaEscuela ?? "");
                cmd.ExecuteNonQuery();
            }
        }


        // NUEVO MÉTODO HISTÓRICO: Cuando se importa un JSON, se actualiza el original y se crea un clon estático de ese mes.
        public static void GuardarVersionHistoricaJSON(string congreCodigo, string mesReferencia, List<PersonaJson> listaPersonas)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    string fechaExacta = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // Una persona puede venir en varios años de servicio
                    // (una tarjeta por año): la ficha y la instantánea
                    // histórica se guardan una sola vez por persona; la
                    // actividad se guarda para cada año.
                    var yaProcesadas = new HashSet<long>();

                    foreach (var p in listaPersonas)
                    {
                        long personaActualId = 0;
                        var cmdCheck = connection.CreateCommand();
                        cmdCheck.Transaction = transaction;
                        cmdCheck.CommandText = "SELECT Id FROM Personas WHERE Nombre = $nombre AND CongregacionCodigo = $congreCodigo AND MesHistorico IS NULL ORDER BY Id DESC LIMIT 1;";
                        cmdCheck.Parameters.AddWithValue("$nombre", p.Nombre);
                        cmdCheck.Parameters.AddWithValue("$congreCodigo", congreCodigo);
                        var idObj = cmdCheck.ExecuteScalar();

                        if (idObj != null)
                        {
                            personaActualId = (long)idObj;

                            // Ya procesada en otro año: no se repite la
                            // instantánea ni la actualización de la ficha;
                            // solo se añade su actividad de ese año.
                            if (yaProcesadas.Add(personaActualId))
                            {
                            var cmdClone = connection.CreateCommand();
                            cmdClone.Transaction = transaction;

                            cmdClone.CommandText = @"
                                INSERT INTO Personas (Nombre, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Nombramiento, Servicio, CongregacionCodigo, Grupo, Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, NombreJWHub, Foto, NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW, Solicitudes, Privilegios, RequisitoHoras, PersonaRaizId, MesHistorico, FechaExactaHistorico, Notas, OrigenRegistro, AprobadoExhibidores)
                                SELECT Nombre, $fnac, $sexo, $fbaut, $esp, $nomb, $serv, CongregacionCodigo, $grupo, Telefono, Email, FechaInicioPrec, FechaUltimaEscuela, NombreJWHub, Foto, NombreComun, Direccion, Latitud, Longitud, ConyugeId, EmailJW, Solicitudes, Privilegios, RequisitoHoras, $raiz, $mesH, $fechaEx, Notas, OrigenRegistro, AprobadoExhibidores
                                FROM Personas WHERE Id = $raiz;
                                SELECT last_insert_rowid();";
                            cmdClone.Parameters.AddWithValue("$fnac", p.FechaNacimiento ?? "");
                            cmdClone.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                            cmdClone.Parameters.AddWithValue("$fbaut", p.FechaBautismo ?? "");
                            cmdClone.Parameters.AddWithValue("$esp", p.Esperanza ?? "");
                            cmdClone.Parameters.AddWithValue("$nomb", p.Nombramiento ?? "");
                            cmdClone.Parameters.AddWithValue("$serv", p.Servicio ?? "");
                            cmdClone.Parameters.AddWithValue("$grupo", p.Grupo);
                            cmdClone.Parameters.AddWithValue("$raiz", personaActualId);
                            cmdClone.Parameters.AddWithValue("$mesH", mesReferencia);
                            cmdClone.Parameters.AddWithValue("$fechaEx", fechaExacta);
                            long idClon = (long)cmdClone.ExecuteScalar();

                            var cmdUpd = connection.CreateCommand();
                            cmdUpd.Transaction = transaction;
                            cmdUpd.CommandText = @"UPDATE Personas SET FechaNacimiento = $fnac, Sexo = $sexo, FechaBautismo = $fbaut, Esperanza = $esp, Nombramiento = $nomb, Servicio = $serv, Grupo = $grupo,
                                OrigenRegistro = CASE
                                    WHEN OrigenRegistro IS NULL OR trim(OrigenRegistro) = '' OR upper(OrigenRegistro) = 'DESCONOCIDO' THEN 'S21'
                                    WHEN instr(lower(OrigenRegistro), 's21') = 0 THEN OrigenRegistro || '+S21'
                                    ELSE OrigenRegistro END
                                WHERE Id = $id";
                            cmdUpd.Parameters.AddWithValue("$fnac", p.FechaNacimiento ?? "");
                            cmdUpd.Parameters.AddWithValue("$sexo", p.Sexo ?? "");
                            cmdUpd.Parameters.AddWithValue("$fbaut", p.FechaBautismo ?? "");
                            cmdUpd.Parameters.AddWithValue("$esp", p.Esperanza ?? "");
                            cmdUpd.Parameters.AddWithValue("$nomb", p.Nombramiento ?? "");
                            cmdUpd.Parameters.AddWithValue("$serv", p.Servicio ?? "");
                            cmdUpd.Parameters.AddWithValue("$grupo", p.Grupo);
                            cmdUpd.Parameters.AddWithValue("$id", personaActualId);
                            cmdUpd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            var cmdInsert = connection.CreateCommand();
                            cmdInsert.Transaction = transaction;
                            cmdInsert.CommandText = @"
                                INSERT INTO Personas (Nombre, FechaNacimiento, Sexo, FechaBautismo, Esperanza, Nombramiento, Servicio, CongregacionCodigo, Grupo, OrigenRegistro)
                                VALUES ($nombre, $fechaNacimiento, $sexo, $fechaBautismo, $esperanza, $nombramiento, $servicio, $congreCodigo, $grupo, 'S21');
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

                            var res = cmdInsert.ExecuteScalar();
                            if (res != null) personaActualId = Convert.ToInt64(res);

                            // Ya queda procesada: si vuelve por otro año,
                            // no se repite la ficha.
                            if (personaActualId > 0)
                            {
                                yaProcesadas.Add(personaActualId);
                            }
                        }

                        if (personaActualId > 0 && p.Informe != null)
                        {
                            foreach (var inf in p.Informe)
                            {
                                var cmdInf = connection.CreateCommand();
                                cmdInf.Transaction = transaction;
                                cmdInf.CommandText = @"
                                    INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Participacion, CursosBiblicos, Cursos, PrecursorAuxiliar, Horas, Notas)
                                    VALUES ($personaId, $mes, $anio, $part, $cursos, $cursosTexto, $prec, $horas, $notas)
                                    ON CONFLICT(PersonaId, Mes, AnioServicio) DO UPDATE SET
                                        Participacion = $part, CursosBiblicos = $cursos, Cursos = $cursosTexto, PrecursorAuxiliar = $prec, Horas = $horas, Notas = $notas;";

                                long idParaActividad = idObj != null ? (long)idObj : personaActualId;
                                cmdInf.Parameters.AddWithValue("$personaId", idParaActividad);
                                cmdInf.Parameters.AddWithValue("$mes", inf.Mes ?? "");
                                cmdInf.Parameters.AddWithValue("$anio", p.AnioServicio ?? "");
                                cmdInf.Parameters.AddWithValue("$part", inf.Participacion ? 1 : 0);
                                string cursosTexto = inf.CursosBiblicos?.ToString() ?? "";
                                int cursosEnteros = 0; if (!string.IsNullOrWhiteSpace(cursosTexto)) int.TryParse(cursosTexto, out cursosEnteros);
                                cmdInf.Parameters.AddWithValue("$cursos", cursosEnteros);
                                cmdInf.Parameters.AddWithValue("$cursosTexto", cursosTexto);
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

        public static void FusionarYBorrarDefinitivo(Persona personaDestino, int idOrigen)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        try
                        {
                            var cmdAct = connection.CreateCommand();
                            cmdAct.Transaction = transaction;
                            cmdAct.CommandText = "UPDATE ActividadPersonas SET PersonaId = $idDestino WHERE PersonaId = $idOrigen;";
                            cmdAct.Parameters.AddWithValue("$idDestino", personaDestino.Id);
                            cmdAct.Parameters.AddWithValue("$idOrigen", idOrigen);
                            cmdAct.ExecuteNonQuery();
                        }
                        catch { }

                        try
                        {
                            var cmdCheck = connection.CreateCommand();
                            cmdCheck.Transaction = transaction;
                            cmdCheck.CommandText = "PRAGMA table_info(TerritoriosVisitaV3);";
                            bool hasAsignadoAId = false; bool hasPersonaId = false;

                            using (var reader = cmdCheck.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string colName = reader.GetString(1);
                                    if (colName == "AsignadoAId") hasAsignadoAId = true;
                                    if (colName == "PersonaId") hasPersonaId = true;
                                }
                            }
                            if (hasAsignadoAId)
                            {
                                var cmdTerr = connection.CreateCommand();
                                cmdTerr.Transaction = transaction;
                                cmdTerr.CommandText = "UPDATE TerritoriosVisitaV3 SET AsignadoAId = $idDestino WHERE AsignadoAId = $idOrigen;";
                                cmdTerr.Parameters.AddWithValue("$idDestino", personaDestino.Id);
                                cmdTerr.Parameters.AddWithValue("$idOrigen", idOrigen);
                                cmdTerr.ExecuteNonQuery();
                            }
                            else if (hasPersonaId)
                            {
                                var cmdTerr = connection.CreateCommand();
                                cmdTerr.Transaction = transaction;
                                cmdTerr.CommandText = "UPDATE TerritoriosVisitaV3 SET PersonaId = $idDestino WHERE PersonaId = $idOrigen;";
                                cmdTerr.Parameters.AddWithValue("$idDestino", personaDestino.Id);
                                cmdTerr.Parameters.AddWithValue("$idOrigen", idOrigen);
                                cmdTerr.ExecuteNonQuery();
                            }
                        }
                        catch { }

                        // Si la persona origen tenía cónyuge, la ficha del cónyuge puede
                        // seguir apuntando al Id que vamos a borrar. Redirigimos esa referencia
                        // al Id destino para conservar la relación en ambos sentidos.
                        try
                        {
                            var cmdConyuge = connection.CreateCommand();
                            cmdConyuge.Transaction = transaction;
                            cmdConyuge.CommandText = "UPDATE Personas SET ConyugeId = $idDestino WHERE ConyugeId = $idOrigen;";
                            cmdConyuge.Parameters.AddWithValue("$idDestino", personaDestino.Id);
                            cmdConyuge.Parameters.AddWithValue("$idOrigen", idOrigen);
                            cmdConyuge.ExecuteNonQuery();
                        }
                        catch { }

                        // Si una de las dos fichas forma parte de una cadena de traslados,
                        // trasladamos esos enlaces al Id que sobrevivirá a la fusión. Esto es
                        // importante cuando una ficha recién trasladada se vincula después con
                        // un registro S-21 de la misma congregación.
                        int trasladoAnteriorFinal = personaDestino.TrasladoAnteriorId;
                        int trasladoSiguienteFinal = personaDestino.TrasladoSiguienteId;
                        string fechaTrasladoFinal = personaDestino.FechaTraslado ?? "";
                        try
                        {
                            int anteriorDestino = 0, siguienteDestino = 0;
                            string fechaDestino = "";
                            using (var cmdLinksDest = connection.CreateCommand())
                            {
                                cmdLinksDest.Transaction = transaction;
                                cmdLinksDest.CommandText = "SELECT COALESCE(TrasladoAnteriorId,0), COALESCE(TrasladoSiguienteId,0), COALESCE(FechaTraslado,'') FROM Personas WHERE Id=$id;";
                                cmdLinksDest.Parameters.AddWithValue("$id", personaDestino.Id);
                                using (var reader = cmdLinksDest.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        anteriorDestino = reader.GetInt32(0);
                                        siguienteDestino = reader.GetInt32(1);
                                        fechaDestino = reader.GetString(2);
                                    }
                                }
                            }

                            int anteriorOrigen = 0, siguienteOrigen = 0;
                            string fechaOrigen = "";
                            using (var cmdLinksOrigen = connection.CreateCommand())
                            {
                                cmdLinksOrigen.Transaction = transaction;
                                cmdLinksOrigen.CommandText = "SELECT COALESCE(TrasladoAnteriorId,0), COALESCE(TrasladoSiguienteId,0), COALESCE(FechaTraslado,'') FROM Personas WHERE Id=$id;";
                                cmdLinksOrigen.Parameters.AddWithValue("$id", idOrigen);
                                using (var reader = cmdLinksOrigen.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        anteriorOrigen = reader.GetInt32(0);
                                        siguienteOrigen = reader.GetInt32(1);
                                        fechaOrigen = reader.GetString(2);
                                    }
                                }
                            }

                            trasladoAnteriorFinal = anteriorDestino > 0 ? anteriorDestino : anteriorOrigen;
                            trasladoSiguienteFinal = siguienteDestino > 0 ? siguienteDestino : siguienteOrigen;
                            fechaTrasladoFinal = !string.IsNullOrWhiteSpace(fechaDestino) ? fechaDestino : fechaOrigen;

                            var cmdRedirectLinks = connection.CreateCommand();
                            cmdRedirectLinks.Transaction = transaction;
                            cmdRedirectLinks.CommandText = @"
                                UPDATE Personas SET TrasladoSiguienteId = $destino WHERE TrasladoSiguienteId = $origen;
                                UPDATE Personas SET TrasladoAnteriorId = $destino WHERE TrasladoAnteriorId = $origen;";
                            cmdRedirectLinks.Parameters.AddWithValue("$destino", personaDestino.Id);
                            cmdRedirectLinks.Parameters.AddWithValue("$origen", idOrigen);
                            cmdRedirectLinks.ExecuteNonQuery();
                        }
                        catch { }

                        // Conservamos la procedencia de ambos registros al fusionarlos.
                        try
                        {
                            var cmdOrigen = connection.CreateCommand();
                            cmdOrigen.Transaction = transaction;
                            cmdOrigen.CommandText = "SELECT OrigenRegistro FROM Personas WHERE Id = $idOrigen;";
                            cmdOrigen.Parameters.AddWithValue("$idOrigen", idOrigen);
                            var origenObj = cmdOrigen.ExecuteScalar();
                            if (origenObj != null && origenObj != DBNull.Value)
                                personaDestino.OrigenRegistro = CombinarOrigenes(personaDestino.OrigenRegistro, origenObj.ToString());
                        }
                        catch { }

                        var cmdDelete = connection.CreateCommand();
                        cmdDelete.Transaction = transaction;
                        cmdDelete.CommandText = "DELETE FROM Personas WHERE Id = $idOrigen OR PersonaRaizId = $idOrigen;";
                        cmdDelete.Parameters.AddWithValue("$idOrigen", idOrigen);
                        cmdDelete.ExecuteNonQuery();

                        var cmdUpdateDest = connection.CreateCommand();
                        cmdUpdateDest.Transaction = transaction;
                        cmdUpdateDest.CommandText = @"
                            UPDATE Personas SET 
                                Nombre = $nombre, NombreComun = $nombreComun, NombreJWHub = $nombreJW,
                                Sexo = $sexo, FechaNacimiento = $fnac, FechaBautismo = $fbaut,
                                Esperanza = $esp, Nombramiento = $nomb, Servicio = $serv,
                                CongregacionCodigo = $congre, Grupo = $grp, Telefono = $tlf,
                                Email = $em, EmailJW = $emjw, FechaInicioPrec = $finic, FechaUltimaEscuela = $fesc,
                                Solicitudes = $sols, Privilegios = $priv, Foto = $foto,
                                Direccion = $dir, Latitud = $lat, Longitud = $lon, ConyugeId = $conyuge, Notas = $notas,
                                OrigenRegistro = $origenRegistro, AprobadoExhibidores = $aprobadoExhibidores,
                                TrasladoAnteriorId = $trasladoAnterior, TrasladoSiguienteId = $trasladoSiguiente, FechaTraslado = $fechaTraslado
                            WHERE Id = $idDestino;";

                        cmdUpdateDest.Parameters.AddWithValue("$nombre", personaDestino.Nombre ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$nombreComun", personaDestino.NombreComun ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$nombreJW", personaDestino.NombreJWHub ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$sexo", personaDestino.Sexo ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$fnac", personaDestino.FechaNacimiento ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$fbaut", personaDestino.FechaBautismo ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$esp", personaDestino.Esperanza ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$nomb", personaDestino.Nombramiento ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$serv", personaDestino.Servicio ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$congre", personaDestino.CongregacionCodigo ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$grp", personaDestino.Grupo);
                        cmdUpdateDest.Parameters.AddWithValue("$tlf", personaDestino.Telefono ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$em", personaDestino.Email ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$emjw", personaDestino.EmailJW ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$finic", personaDestino.FechaInicioPrec ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$fesc", personaDestino.FechaUltimaEscuela ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$sols", personaDestino.Solicitudes ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$priv", personaDestino.Privilegios ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$foto", personaDestino.Foto ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$dir", personaDestino.Direccion ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$lat", personaDestino.Latitud);
                        cmdUpdateDest.Parameters.AddWithValue("$lon", personaDestino.Longitud);
                        cmdUpdateDest.Parameters.AddWithValue("$conyuge", personaDestino.ConyugeId);
                        cmdUpdateDest.Parameters.AddWithValue("$notas", personaDestino.Notas ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$origenRegistro", string.IsNullOrWhiteSpace(personaDestino.OrigenRegistro) ? "DESCONOCIDO" : personaDestino.OrigenRegistro);
                        cmdUpdateDest.Parameters.AddWithValue("$aprobadoExhibidores", personaDestino.AprobadoExhibidores ? 1 : 0);
                        cmdUpdateDest.Parameters.AddWithValue("$trasladoAnterior", trasladoAnteriorFinal);
                        cmdUpdateDest.Parameters.AddWithValue("$trasladoSiguiente", trasladoSiguienteFinal);
                        cmdUpdateDest.Parameters.AddWithValue("$fechaTraslado", fechaTrasladoFinal ?? "");
                        cmdUpdateDest.Parameters.AddWithValue("$idDestino", personaDestino.Id);
                        cmdUpdateDest.ExecuteNonQuery();

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region 5. GESTIÓN DE ACTIVIDAD MENSUAL (Personas)

        public static List<ActividadMensual> ObtenerActividadPersona(int personaId)
        {
            var lista = new List<ActividadMensual>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Mes, AnioServicio, Participacion,
                           COALESCE(NULLIF(Cursos, ''), CAST(CursosBiblicos AS TEXT), ''),
                           PrecursorAuxiliar, Horas, Notas
                    FROM ActividadPersonas
                    WHERE PersonaId = $id
                    ORDER BY CAST(AnioServicio AS INTEGER) DESC,
                             CASE lower(Mes)
                                 WHEN 'agosto' THEN 12 WHEN 'julio' THEN 11 WHEN 'junio' THEN 10 WHEN 'mayo' THEN 9
                                 WHEN 'abril' THEN 8 WHEN 'marzo' THEN 7 WHEN 'febrero' THEN 6 WHEN 'enero' THEN 5
                                 WHEN 'diciembre' THEN 4 WHEN 'noviembre' THEN 3 WHEN 'octubre' THEN 2 WHEN 'septiembre' THEN 1
                                 ELSE 0 END DESC;";
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

        public static void GuardarActividadMensualEditada(int personaId, string mes, string anioServicio, string horas, bool participacion, string cursos, bool precAux, string notas)
        {
            AnotarActividad(
                "Personas",
                "Registró la actividad de «" + NombrePersonaPorId(personaId) +
                "» para " + mes + " de " + anioServicio);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE ActividadPersonas 
                    SET Horas = $horas, Participacion = $participacion, 
                        Cursos = $cursos, CursosBiblicos = $cursosNumerico, PrecursorAuxiliar = $precAux, Notas = $notas
                    WHERE PersonaId = $id AND lower(Mes) = lower($mes) AND AnioServicio = $anio;
                    
                    INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Horas, Participacion, Cursos, CursosBiblicos, PrecursorAuxiliar, Notas)
                    SELECT $id, $mes, $anio, $horas, $participacion, $cursos, $cursosNumerico, $precAux, $notas
                    WHERE (SELECT changes() = 0);
                ";
                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.Parameters.AddWithValue("$mes", mes.ToLower());
                cmd.Parameters.AddWithValue("$anio", anioServicio);
                cmd.Parameters.AddWithValue("$horas", horas ?? "");
                cmd.Parameters.AddWithValue("$participacion", participacion);
                cmd.Parameters.AddWithValue("$cursos", cursos ?? "");
                int cursosNumerico = 0;
                int.TryParse(cursos ?? "", out cursosNumerico);
                cmd.Parameters.AddWithValue("$cursosNumerico", cursosNumerico);
                cmd.Parameters.AddWithValue("$precAux", precAux ? 1 : 0);
                cmd.Parameters.AddWithValue("$notas", notas ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static void ReemplazarActividadPersona(int personaId, List<ActividadMensual> actividad)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var cmdDelete = connection.CreateCommand();
                        cmdDelete.Transaction = transaction;
                        cmdDelete.CommandText = "DELETE FROM ActividadPersonas WHERE PersonaId = $id;";
                        cmdDelete.Parameters.AddWithValue("$id", personaId);
                        cmdDelete.ExecuteNonQuery();

                        foreach (var item in actividad ?? new List<ActividadMensual>())
                        {
                            var cmdInsert = connection.CreateCommand();
                            cmdInsert.Transaction = transaction;
                            cmdInsert.CommandText = @"
                                INSERT INTO ActividadPersonas
                                    (PersonaId, Mes, AnioServicio, Participacion, CursosBiblicos,
                                     PrecursorAuxiliar, Horas, Notas, Cursos)
                                VALUES
                                    ($id, $mes, $anio, $participacion, $cursosNumerico,
                                     $precAux, $horas, $notas, $cursos);";

                            string cursos = item.CursosBiblicos ?? "";
                            int cursosNumerico = 0;
                            int.TryParse(cursos, out cursosNumerico);

                            cmdInsert.Parameters.AddWithValue("$id", personaId);
                            cmdInsert.Parameters.AddWithValue("$mes", (item.Mes ?? "").Trim().ToLowerInvariant());
                            cmdInsert.Parameters.AddWithValue("$anio", (item.AnioServicio ?? "").Trim());
                            cmdInsert.Parameters.AddWithValue("$participacion", item.Participacion ? 1 : 0);
                            cmdInsert.Parameters.AddWithValue("$cursosNumerico", cursosNumerico);
                            cmdInsert.Parameters.AddWithValue("$precAux", item.PrecursorAuxiliar ? 1 : 0);
                            cmdInsert.Parameters.AddWithValue("$horas", item.Horas ?? "");
                            cmdInsert.Parameters.AddWithValue("$notas", item.Notas ?? "");
                            cmdInsert.Parameters.AddWithValue("$cursos", cursos);
                            cmdInsert.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public static void EliminarActividadMensual(int personaId, string mes, string anioServicio)
        {
            AnotarActividad(
                "Personas",
                "Quitó la actividad de «" + NombrePersonaPorId(personaId) +
                "» de " + mes + " de " + anioServicio);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM ActividadPersonas
                    WHERE PersonaId = $id
                      AND lower(Mes) = lower($mes)
                      AND AnioServicio = $anio;";
                cmd.Parameters.AddWithValue("$id", personaId);
                cmd.Parameters.AddWithValue("$mes", (mes ?? "").Trim());
                cmd.Parameters.AddWithValue("$anio", (anioServicio ?? "").Trim());
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region 6. GESTIÓN DE CONGREGACIONES, MAPAS KMZ Y SECCIONES

        /// <summary>
        /// Sustituye el programa de la visita de un mes y congregación por
        /// los eventos leídos del PDF del programa (los nombres de personas
        /// quedan en las notas).
        /// </summary>
        public static int ReemplazarProgramaMesVisita(
            string congreCodigo,
            string mesReferencia,
            System.Collections.Generic.IEnumerable<SCCosmos.Sincro.EventoProgramaVisita> eventos)
        {
            int total = 0;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using (var borrar = connection.CreateCommand())
                {
                    borrar.CommandText =
                        "DELETE FROM ProgramaVisitaV2 WHERE CongreCodigo = @Codigo AND MesReferencia = @Mes;";
                    borrar.Parameters.AddWithValue("@Codigo", congreCodigo ?? "");
                    borrar.Parameters.AddWithValue("@Mes", mesReferencia ?? "");
                    borrar.ExecuteNonQuery();
                }

                foreach (var evento in eventos)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ProgramaVisitaV2
                            (CongreCodigo, MesReferencia, DiaSemana, Turno,
                             Responsable, TipoActividad, Hora, NotasLibro,
                             CompaneroId, Direccion, Latitud, Longitud)
                        VALUES
                            (@Codigo, @Mes, @Dia, @Turno,
                             @Responsable, @Tipo, @Hora, @Notas,
                             0, @Direccion, 0, 0);";
                    cmd.Parameters.AddWithValue("@Codigo", congreCodigo ?? "");
                    cmd.Parameters.AddWithValue("@Mes", mesReferencia ?? "");
                    cmd.Parameters.AddWithValue("@Dia", evento.DiaSemana ?? "");
                    cmd.Parameters.AddWithValue("@Turno", evento.Turno ?? "");
                    cmd.Parameters.AddWithValue("@Responsable", evento.Responsable ?? "");
                    cmd.Parameters.AddWithValue("@Tipo", evento.TipoActividad ?? "");
                    cmd.Parameters.AddWithValue("@Hora", evento.Hora ?? "");
                    cmd.Parameters.AddWithValue("@Notas", evento.Notas ?? "");
                    cmd.Parameters.AddWithValue("@Direccion", evento.Ubicacion ?? "");
                    cmd.ExecuteNonQuery();

                    total++;
                }
            }
            catch
            {
            }

            return total;
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

        public static List<Congregacion> ObtenerCongregaciones()
        {
            var lista = new List<Congregacion>();

            // Forzamos que se creen las nuevas columnas si aún no existen
            InicializarDatabase();

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
                    // Obtenemos los nombres de las columnas que la base de datos nos ha devuelto realmente
                    var columnas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columnas.Add(reader.GetName(i));
                    }

                    while (reader.Read())
                    {
                        lista.Add(new Congregacion
                        {
                            Codigo = columnas.Contains("Codigo") ? reader["Codigo"].ToString() : "",
                            Nombre = columnas.Contains("Nombre") ? reader["Nombre"].ToString() : "",
                            Email = columnas.Contains("Email") && !reader.IsDBNull(reader.GetOrdinal("Email")) ? reader["Email"].ToString() : "",
                            SeccionId = columnas.Contains("SeccionId") && !reader.IsDBNull(reader.GetOrdinal("SeccionId")) ? Convert.ToInt32(reader["SeccionId"]) : 0,
                            NombreSeccion = columnas.Contains("NombreSeccion") && !reader.IsDBNull(reader.GetOrdinal("NombreSeccion")) ? reader["NombreSeccion"].ToString() : "Sin asignar",

                            // LECTURA SEGURA: Comprueba si existe antes de intentar leerla
                            Provincia = columnas.Contains("Provincia") && !reader.IsDBNull(reader.GetOrdinal("Provincia")) ? reader["Provincia"].ToString() : "",
                            Municipio = columnas.Contains("Municipio") && !reader.IsDBNull(reader.GetOrdinal("Municipio")) ? reader["Municipio"].ToString() : "",

                            DireccionSR = columnas.Contains("DireccionSR") && !reader.IsDBNull(reader.GetOrdinal("DireccionSR")) ? reader["DireccionSR"].ToString() : "",
                            DiaEntreSemana = columnas.Contains("DiaEntreSemana") && !reader.IsDBNull(reader.GetOrdinal("DiaEntreSemana")) ? reader["DiaEntreSemana"].ToString() : "",
                            HoraEntreSemana = columnas.Contains("HoraEntreSemana") && !reader.IsDBNull(reader.GetOrdinal("HoraEntreSemana")) ? reader["HoraEntreSemana"].ToString() : "",
                            DiaFinSemana = columnas.Contains("DiaFinSemana") && !reader.IsDBNull(reader.GetOrdinal("DiaFinSemana")) ? reader["DiaFinSemana"].ToString() : "",
                            HoraFinSemana = columnas.Contains("HoraFinSemana") && !reader.IsDBNull(reader.GetOrdinal("HoraFinSemana")) ? reader["HoraFinSemana"].ToString() : "",
                            Latitud = columnas.Contains("Latitud") && !reader.IsDBNull(reader.GetOrdinal("Latitud")) ? reader.GetDouble(reader.GetOrdinal("Latitud")) : 0,
                            Longitud = columnas.Contains("Longitud") && !reader.IsDBNull(reader.GetOrdinal("Longitud")) ? reader.GetDouble(reader.GetOrdinal("Longitud")) : 0,
                            Capacidad = columnas.Contains("Capacidad") && !reader.IsDBNull(reader.GetOrdinal("Capacidad")) ? Convert.ToInt32(reader["Capacidad"]) : 0,
                            Poblacion = columnas.Contains("Poblacion") && !reader.IsDBNull(reader.GetOrdinal("Poblacion")) ? Convert.ToInt32(reader["Poblacion"]) : 0
                        });
                    }
                }
            }
            return lista;
        }

        public static void InsertarCongregacion(Congregacion cong)
        {
            AnotarActividad("Circuito", "Añadió la congregación «" + (cong?.Nombre ?? cong?.Codigo ?? "") + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Congregaciones (Codigo, Nombre, Email, SeccionId, Provincia, Municipio, DireccionSR, DiaEntreSemana, HoraEntreSemana, DiaFinSemana, HoraFinSemana, Capacidad, Poblacion, IdZoom, ClaveZoom, EmailAV)
                    VALUES (@Codigo, @Nombre, @Email, @SeccionId, @Provincia, @Municipio, @DireccionSR, @DiaEntreSemana, @HoraEntreSemana, @DiaFinSemana, @HoraFinSemana, @Capacidad, @Poblacion, @IdZoom, @ClaveZoom, @EmailAV)";
                cmd.Parameters.AddWithValue("@Codigo", cong.Codigo ?? "");
                cmd.Parameters.AddWithValue("@Nombre", cong.Nombre ?? "");
                cmd.Parameters.AddWithValue("@Email", cong.Email ?? "");
                cmd.Parameters.AddWithValue("@SeccionId", cong.SeccionId);
                cmd.Parameters.AddWithValue("@Provincia", cong.Provincia ?? "");
                cmd.Parameters.AddWithValue("@Municipio", cong.Municipio ?? "");
                cmd.Parameters.AddWithValue("@DireccionSR", cong.DireccionSR ?? "");
                cmd.Parameters.AddWithValue("@IdZoom", cong.IdZoom ?? "");
                cmd.Parameters.AddWithValue("@ClaveZoom", cong.ClaveZoom ?? "");
                cmd.Parameters.AddWithValue("@EmailAV", cong.EmailAV ?? "");
                cmd.Parameters.AddWithValue("@DiaEntreSemana", cong.DiaEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraEntreSemana", cong.HoraEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@DiaFinSemana", cong.DiaFinSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraFinSemana", cong.HoraFinSemana ?? "");
                cmd.Parameters.AddWithValue("@Capacidad", cong.Capacidad);
                cmd.Parameters.AddWithValue("@Poblacion", cong.Poblacion);
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
                        Provincia = @Provincia, Municipio = @Municipio,
                        DireccionSR = @DireccionSR, DiaEntreSemana = @DiaEntreSemana, HoraEntreSemana = @HoraEntreSemana, 
                        DiaFinSemana = @DiaFinSemana, HoraFinSemana = @HoraFinSemana, Capacidad = @Capacidad, Poblacion = @Poblacion,
                        IdZoom = @IdZoom, ClaveZoom = @ClaveZoom, EmailAV = @EmailAV
                    WHERE Codigo = @Codigo";
                cmd.Parameters.AddWithValue("@Codigo", cong.Codigo ?? "");
                cmd.Parameters.AddWithValue("@Nombre", cong.Nombre ?? "");
                cmd.Parameters.AddWithValue("@Email", cong.Email ?? "");
                cmd.Parameters.AddWithValue("@SeccionId", cong.SeccionId);
                cmd.Parameters.AddWithValue("@Provincia", cong.Provincia ?? "");
                cmd.Parameters.AddWithValue("@Municipio", cong.Municipio ?? "");
                cmd.Parameters.AddWithValue("@DireccionSR", cong.DireccionSR ?? "");
                cmd.Parameters.AddWithValue("@IdZoom", cong.IdZoom ?? "");
                cmd.Parameters.AddWithValue("@ClaveZoom", cong.ClaveZoom ?? "");
                cmd.Parameters.AddWithValue("@EmailAV", cong.EmailAV ?? "");
                cmd.Parameters.AddWithValue("@DiaEntreSemana", cong.DiaEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraEntreSemana", cong.HoraEntreSemana ?? "");
                cmd.Parameters.AddWithValue("@DiaFinSemana", cong.DiaFinSemana ?? "");
                cmd.Parameters.AddWithValue("@HoraFinSemana", cong.HoraFinSemana ?? "");
                cmd.Parameters.AddWithValue("@Capacidad", cong.Capacidad);
                cmd.Parameters.AddWithValue("@Poblacion", cong.Poblacion);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// ID de Zoom, contraseña de Zoom y email del AV de una congregación.
        /// </summary>
        public static (string IdZoom, string ClaveZoom, string EmailAV) ObtenerDatosExtraCongregacion(string codigo)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                AsegurarColumnasExtraCongregacion(connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT IdZoom, ClaveZoom, EmailAV FROM Congregaciones WHERE Codigo = @Codigo LIMIT 1;";
                cmd.Parameters.AddWithValue("@Codigo", codigo ?? "");

                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return (
                        reader.IsDBNull(0) ? "" : reader.GetString(0),
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2));
                }
            }
            catch
            {
            }

            return ("", "", "");
        }


        public static void GuardarDatosExtraCongregacion(string codigo, string idZoom, string claveZoom, string emailAV)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                AsegurarColumnasExtraCongregacion(connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Congregaciones SET IdZoom = @IdZoom, ClaveZoom = @Clave, EmailAV = @Email WHERE Codigo = @Codigo;";
                cmd.Parameters.AddWithValue("@IdZoom", idZoom ?? "");
                cmd.Parameters.AddWithValue("@Clave", claveZoom ?? "");
                cmd.Parameters.AddWithValue("@Email", emailAV ?? "");
                cmd.Parameters.AddWithValue("@Codigo", codigo ?? "");
                cmd.ExecuteNonQuery();
            }
            catch
            {
            }
        }


        private static void AsegurarColumnasExtraCongregacion(SqliteConnection connection)
        {
            AsegurarColumna(connection, "Congregaciones", "IdZoom", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "ClaveZoom", "TEXT");
            AsegurarColumna(connection, "Congregaciones", "EmailAV", "TEXT");
        }


        public static void EliminarCongregacion(string codigo)
        {
            AnotarActividad("Circuito", "Eliminó la congregación «" + codigo + "»");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Congregaciones WHERE Codigo = $codigo;";
                command.Parameters.AddWithValue("$codigo", codigo);
                command.ExecuteNonQuery();
            }
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

        public static void ActualizarCoordenadasCongregacion(string nombreKmz, double lat, double lon)
        {
            string query = "UPDATE Congregaciones SET Latitud = @Lat, Longitud = @Lon WHERE Nombre LIKE '%' || @Nom || '%' OR @Nom LIKE '%' || Nombre || '%'";
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new Microsoft.Data.Sqlite.SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Lat", lat);
                    command.Parameters.AddWithValue("@Lon", lon);
                    command.Parameters.AddWithValue("@Nom", nombreKmz);
                    command.ExecuteNonQuery();
                }
            }
        }

        public static void ActualizarDatosSalonKMZ(string codigo, string direccion, double lat, double lon, int capacidad, string diaSemana, string hSemana, string diaFDS, string hFDS)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"UPDATE Congregaciones 
                                    SET DireccionSR = @dir, Latitud = @lat, Longitud = @lon, Capacidad = @cap, 
                                        DiaEntreSemana = @diaSemana, HoraEntreSemana = @hSemana, DiaFinSemana = @diaFds, HoraFinSemana = @hFds 
                                    WHERE Codigo = @cod";
                cmd.Parameters.AddWithValue("@dir", direccion ?? "");
                cmd.Parameters.AddWithValue("@lat", lat);
                cmd.Parameters.AddWithValue("@lon", lon);
                cmd.Parameters.AddWithValue("@cap", capacidad);
                cmd.Parameters.AddWithValue("@diaSemana", diaSemana ?? "");
                cmd.Parameters.AddWithValue("@hSemana", hSemana ?? "");
                cmd.Parameters.AddWithValue("@diaFds", diaFDS ?? "");
                cmd.Parameters.AddWithValue("@hFds", hFDS ?? "");
                cmd.Parameters.AddWithValue("@cod", codigo);
                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarPoblacionKMZ(string codigo, int poblacion)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Congregaciones SET Poblacion = @pob WHERE Codigo = @cod";
                cmd.Parameters.AddWithValue("@pob", poblacion);
                cmd.Parameters.AddWithValue("@cod", codigo);
                cmd.ExecuteNonQuery();
            }
        }

        public static void LimpiarElementosMapa()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM ElementosMapa;";
                cmd.ExecuteNonQuery();
            }
        }

        public static void InsertarElementoMapa(ElementoMapa e)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO ElementosMapa (Nombre, Tipo, Coordenadas, Descripcion) VALUES (@nom, @tipo, @coord, @desc)";
                cmd.Parameters.AddWithValue("@nom", e.Nombre ?? "");
                cmd.Parameters.AddWithValue("@tipo", e.Tipo ?? "");
                cmd.Parameters.AddWithValue("@coord", e.Coordenadas ?? "");
                cmd.Parameters.AddWithValue("@desc", e.Descripcion ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public static List<ElementoMapa> ObtenerElementosMapa()
        {
            var lista = new List<ElementoMapa>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Nombre, Tipo, Coordenadas, Descripcion FROM ElementosMapa";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ElementoMapa
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Tipo = reader.GetString(2),
                            Coordenadas = reader.GetString(3),
                            Descripcion = reader.GetString(4)
                        });
                    }
                }
            }
            return lista;
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

        #endregion

        #region 7. GESTIÓN DE CALENDARIO, SEMANAS Y VISITAS

        public static List<SemanaCalendario> ObtenerTodasLasSemanas()
        {
            var list = new List<SemanaCalendario>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM Semanas ORDER BY FechaMartes ASC, Id ASC";

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

        /// <summary>
        /// Próxima asamblea de circuito programada en el calendario. Si no
        /// hay ninguna futura, devuelve la última pasada. Los valores son
        /// los que usa la página de Asambleas: sección, tipo y año de
        /// servicio (de septiembre a agosto).
        /// </summary>
        /// <summary>
        /// Nombre de la congregación que se está visitando hoy (la semana
        /// actual), si la hay.
        /// </summary>
        public static string ObtenerCongregacionEnVisitaActual()
        {
            try
            {
                DateTime hoy = DateTime.Today;

                int diasDesdeLunes =
                    ((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

                DateTime martes =
                    hoy.AddDays(-diasDesdeLunes).Date.AddDays(1);

                var semana =
                    ObtenerSemana(martes.ToString("yyyy-MM-dd"));

                if (semana != null
                    && (semana.Actividad ?? "").Trim().Equals(
                        "Visita",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return semana.Congregacion ?? "";
                }
            }
            catch
            {
            }

            return "";
        }


        public static (string Seccion, string Tipo, string Anio) ObtenerProximaAsambleaCalendario()
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                // Todas las asambleas del calendario con su sección, de más
                // antigua a más reciente.
                var candidatas =
                    new List<(string Seccion, string Tipo, string Anio, DateTime Fecha)>();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT trim(SeccionAsamblea), TipoAsamblea, FechaMartes FROM Semanas " +
                        "WHERE Actividad = 'Asamblea de circuito' " +
                        "AND trim(COALESCE(SeccionAsamblea, '')) <> '' " +
                        "ORDER BY date(FechaMartes) ASC";

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        string seccion =
                            reader.IsDBNull(0) ? "" : reader.GetString(0);

                        string tipoAsamblea =
                            reader.IsDBNull(1) ? "" : reader.GetString(1);

                        DateTime fechaMartes =
                            reader.GetDateTime(2);

                        string tipo =
                            tipoAsamblea.ToUpperInvariant().Contains("BR")
                                ? "AC (con rep. de Betel)"
                                : "AC (con sup. de circuito)";

                        int anio =
                            fechaMartes.Month >= 9
                                ? fechaMartes.Year + 1
                                : fechaMartes.Year;

                        // La fecha real de la asamblea: la que ya está
                        // guardada en el programa (p. ej. 2026-11-22) o, si
                        // todavía no hay programa, el sábado de esa semana.
                        DateTime fecha =
                            fechaMartes.AddDays(4);

                        using (var cmdFecha = connection.CreateCommand())
                        {
                            cmdFecha.CommandText =
                                "SELECT Fecha FROM ProgramaAsambleas " +
                                "WHERE Anio = @Anio AND Tipo = @Tipo AND Seccion = @Seccion LIMIT 1";

                            cmdFecha.Parameters.AddWithValue("@Anio", anio.ToString());
                            cmdFecha.Parameters.AddWithValue("@Tipo", tipo);
                            cmdFecha.Parameters.AddWithValue("@Seccion", seccion);

                            object valor =
                                cmdFecha.ExecuteScalar();

                            if (valor != null
                                && DateTime.TryParse(valor.ToString(), out DateTime fechaGuardada))
                            {
                                fecha = fechaGuardada;
                            }
                        }

                        candidatas.Add((seccion, tipo, anio.ToString(), fecha));
                    }
                }

                if (candidatas.Count == 0)
                {
                    return ("", "", "");
                }

                // La primera que aún no ha pasado; si ya pasaron todas, la
                // última.
                var elegida =
                    candidatas.FirstOrDefault(c => c.Fecha.Date >= DateTime.Today);

                if (elegida.Seccion == null)
                {
                    elegida = candidatas[candidatas.Count - 1];
                }

                return (elegida.Seccion, elegida.Tipo, elegida.Anio);
            }
            catch
            {
                return ("", "", "");
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
            AnotarActividad(
                "Calendario",
                "Guardó la semana del " +
                (semana?.FechaMartes.ToString("dd/MM/yyyy") ?? ""));

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                if (semana.Id > 0)
                {
                    var cmdUpdate = connection.CreateCommand();
                    cmdUpdate.CommandText = @"
                        UPDATE Semanas SET 
                            Actividad = @Actividad, Congregacion = @Congregacion, Tipo = @Tipo, Acompanante = @Acompanante, Alojamiento = @Alojamiento, 
                            S302Enviado = @S302Enviado, DocEnviada = @DocEnviada, SeccionAsamblea = @SeccionAsamblea, FechaAsamblea = @FechaAsamblea, 
                            NotificacionEnviada = @NotificacionEnviada, RecordatorioEnviado = @RecordatorioEnviado, DatosImportados = @DatosImportados
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

        private static List<NotaVisita> GenerarPlantillaNotas(int semanaId)
        {
            return new List<NotaVisita>
            {
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Están ordenados y al día?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Se están aplicando las medidas de retención de documentos? (sfl-a)", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Se indica en las tarjetas de publicadores inactivos (bautizados) la fecha en la que empezaron a serlo?", Respuesta = "" },
                new NotaVisita { SemanaId = semanaId, Cabecera = "REGISTROS", Pregunta = "¿Tienen los nombres de publicadores la structure correcta?", Respuesta = "" },
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
                new NotaVisita { SemanaId = semanaId, Cabecera = "SALÓN DEL REINO Y SEGURIDAD", Pregunta = "¿Cómo es la asistencia en reference al número de publicadores?", Respuesta = "" },
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

                // La plantilla recién creada contiene objetos con Id = 0.
                // Antes de volver a leer desde SQLite hay que vaciar la lista;
                // de lo contrario se devuelven duplicadas las notas y las primeras
                // no se pueden actualizar porque ActualizarNotaVisitaUnica ignora Id = 0.
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
            AnotarActividad(
                "Visitas",
                "Guardó las notas de la visita (semana del " +
                FechaSemanaPorId(semanaId) + ")");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmdInsert = connection.CreateCommand();
                    cmdInsert.Transaction = transaction;
                    cmdInsert.CommandText = "INSERT INTO NotasVisita (SemanaId, Cabecera, Pregunta, Respuesta, EsAlerta) VALUES (@SemanaId, @Cabecera, @Pregunta, @Respuesta, @EsAlerta)";

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

            AnotarActividad("Visitas", "Editó una nota de la visita");
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
                return (long)cmd2.ExecuteScalar() > 0;
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
                if (result2 != null) return result2.ToString();

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

        public static void GuardarActividadPrograma(
            string congreCodigo,
            string mesReferenciaStr,
            string dia,
            string turno,
            string responsable,
            string tipoActividad,
            string hora,
            string notasLibro,
            int companeroId)
        {
            AnotarActividad(
                "Visitas",
                "Añadió " +
                (string.IsNullOrWhiteSpace(tipoActividad)
                    ? "una actividad"
                    : "«" + tipoActividad + "»") +
                $" al programa de la visita ({dia}, {turno})");

            GuardarActividadPrograma(
                congreCodigo,
                mesReferenciaStr,
                dia,
                turno,
                responsable,
                tipoActividad,
                hora,
                notasLibro,
                companeroId,
                "",
                0,
                0);
        }

        public static void GuardarActividadPrograma(
            string congreCodigo,
            string mesReferenciaStr,
            string dia,
            string turno,
            string responsable,
            string tipoActividad,
            string hora,
            string notasLibro,
            int companeroId,
            string direccion,
            double latitud,
            double longitud)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ProgramaVisitaV2
                    (
                        CongreCodigo,
                        MesReferencia,
                        DiaSemana,
                        Turno,
                        Responsable,
                        TipoActividad,
                        Hora,
                        NotasLibro,
                        CompaneroId,
                        Direccion,
                        Latitud,
                        Longitud
                    )
                    VALUES
                    (
                        $codigo,
                        $mes,
                        $dia,
                        $turno,
                        $responsable,
                        $tipo,
                        $hora,
                        $notas,
                        $comp,
                        $direccion,
                        $latitud,
                        $longitud
                    );";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferenciaStr);
                cmd.Parameters.AddWithValue("$dia", dia);
                cmd.Parameters.AddWithValue("$turno", turno);
                cmd.Parameters.AddWithValue("$responsable", responsable);
                cmd.Parameters.AddWithValue("$tipo", tipoActividad ?? "");
                cmd.Parameters.AddWithValue("$hora", hora ?? "");
                cmd.Parameters.AddWithValue("$notas", notasLibro ?? "");
                cmd.Parameters.AddWithValue("$comp", companeroId);
                cmd.Parameters.AddWithValue("$direccion", direccion ?? "");
                cmd.Parameters.AddWithValue("$latitud", latitud);
                cmd.Parameters.AddWithValue("$longitud", longitud);

                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarActividadPrograma(
            int actividadId,
            string tipoActividad,
            string hora,
            string notasLibro,
            int companeroId,
            string direccion,
            double latitud,
            double longitud)
        {
            AnotarActividad("Visitas", "Editó una actividad del programa de la visita");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE ProgramaVisitaV2
                    SET
                        TipoActividad = $tipo,
                        Hora = $hora,
                        NotasLibro = $notas,
                        CompaneroId = $comp,
                        Direccion = $direccion,
                        Latitud = $latitud,
                        Longitud = $longitud
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$tipo", tipoActividad ?? "");
                cmd.Parameters.AddWithValue("$hora", hora ?? "");
                cmd.Parameters.AddWithValue("$notas", notasLibro ?? "");
                cmd.Parameters.AddWithValue("$comp", companeroId);
                cmd.Parameters.AddWithValue("$direccion", direccion ?? "");
                cmd.Parameters.AddWithValue("$latitud", latitud);
                cmd.Parameters.AddWithValue("$longitud", longitud);
                cmd.Parameters.AddWithValue("$id", actividadId);

                cmd.ExecuteNonQuery();
            }
        }

        public static void ActualizarActividadPrograma(
            int actividadId,
            string responsable,
            string tipoActividad,
            string hora,
            string notasLibro,
            int companeroId,
            string direccion,
            double latitud,
            double longitud)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE ProgramaVisitaV2
                    SET
                        Turno = 'General',
                        Responsable = $responsable,
                        TipoActividad = $tipo,
                        Hora = $hora,
                        NotasLibro = $notas,
                        CompaneroId = $comp,
                        Direccion = $direccion,
                        Latitud = $latitud,
                        Longitud = $longitud
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$responsable",
                    responsable ?? "SC");

                cmd.Parameters.AddWithValue(
                    "$tipo",
                    tipoActividad ?? "");

                cmd.Parameters.AddWithValue(
                    "$hora",
                    hora ?? "");

                cmd.Parameters.AddWithValue(
                    "$notas",
                    notasLibro ?? "");

                cmd.Parameters.AddWithValue(
                    "$comp",
                    companeroId);

                cmd.Parameters.AddWithValue(
                    "$direccion",
                    direccion ?? "");

                cmd.Parameters.AddWithValue(
                    "$latitud",
                    latitud);

                cmd.Parameters.AddWithValue(
                    "$longitud",
                    longitud);

                cmd.Parameters.AddWithValue(
                    "$id",
                    actividadId);

                cmd.ExecuteNonQuery();
            }
        }


        public static void EliminarActividadPrograma(
            int actividadId)
        {
            AnotarActividad("Visitas", "Eliminó una actividad del programa de la visita");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText =
                    "DELETE FROM ProgramaVisitaV2 WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", actividadId);

                cmd.ExecuteNonQuery();
            }
        }

        public static void ImportarProgramaVisita(
            string congreCodigo,
            string mesReferenciaStr,
            IEnumerable<ActividadPrograma> actividades,
            bool reemplazarExistente)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (reemplazarExistente)
                        {
                            var borrar = connection.CreateCommand();
                            borrar.Transaction = transaction;
                            borrar.CommandText = @"
                                DELETE FROM ProgramaVisitaV2
                                WHERE CongreCodigo = $codigo
                                  AND MesReferencia = $mes;";
                            borrar.Parameters.AddWithValue("$codigo", congreCodigo ?? "");
                            borrar.Parameters.AddWithValue("$mes", mesReferenciaStr ?? "");
                            borrar.ExecuteNonQuery();
                        }

                        foreach (var actividad in actividades ?? Enumerable.Empty<ActividadPrograma>())
                        {
                            var cmd = connection.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO ProgramaVisitaV2
                                (
                                    CongreCodigo, MesReferencia, DiaSemana, Turno,
                                    Responsable, TipoActividad, Hora, NotasLibro,
                                    CompaneroId, Direccion, Latitud, Longitud
                                )
                                VALUES
                                (
                                    $codigo, $mes, $dia, 'General',
                                    $responsable, $tipo, $hora, $notas,
                                    $comp, $direccion, $latitud, $longitud
                                );";

                            cmd.Parameters.AddWithValue("$codigo", congreCodigo ?? "");
                            cmd.Parameters.AddWithValue("$mes", mesReferenciaStr ?? "");
                            cmd.Parameters.AddWithValue("$dia", actividad?.DiaSemana ?? "");
                            cmd.Parameters.AddWithValue("$responsable", actividad?.Responsable ?? "SC");
                            cmd.Parameters.AddWithValue("$tipo", actividad?.TipoActividad ?? "");
                            cmd.Parameters.AddWithValue("$hora", actividad?.Hora ?? "");
                            cmd.Parameters.AddWithValue("$notas", actividad?.NotasLibro ?? "");
                            cmd.Parameters.AddWithValue("$comp", actividad?.CompaneroId ?? 0);
                            cmd.Parameters.AddWithValue("$direccion", actividad?.Direccion ?? "");
                            cmd.Parameters.AddWithValue("$latitud", actividad?.Latitud ?? 0);
                            cmd.Parameters.AddWithValue("$longitud", actividad?.Longitud ?? 0);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }


        public static List<ActividadPrograma> ObtenerActividadesProgramaMes(
            string congreCodigo,
            string mesReferenciaStr)
        {
            var lista =
                new List<ActividadPrograma>();

            using (var connection =
                   new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText = @"
                    SELECT
                        Id,
                        DiaSemana,
                        Turno,
                        Responsable,
                        TipoActividad,
                        Hora,
                        NotasLibro,
                        CompaneroId,
                        Direccion,
                        Latitud,
                        Longitud
                    FROM ProgramaVisitaV2
                    WHERE
                        CongreCodigo = $codigo
                        AND MesReferencia = $mes
                    ORDER BY
                        CASE DiaSemana
                            WHEN 'Miércoles' THEN 1
                            WHEN 'Jueves' THEN 2
                            WHEN 'Viernes' THEN 3
                            WHEN 'Sábado' THEN 4
                            WHEN 'Domingo' THEN 5
                            ELSE 99
                        END,
                        Hora ASC,
                        Id ASC;";

                cmd.Parameters.AddWithValue(
                    "$codigo",
                    congreCodigo ?? "");

                cmd.Parameters.AddWithValue(
                    "$mes",
                    mesReferenciaStr ?? "");

                using (var reader =
                       cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            new ActividadPrograma
                            {
                                Id =
                                    reader.GetInt32(0),

                                DiaSemana =
                                    reader.IsDBNull(1)
                                        ? ""
                                        : reader.GetString(1),

                                Turno =
                                    reader.IsDBNull(2)
                                        ? ""
                                        : reader.GetString(2),

                                Responsable =
                                    reader.IsDBNull(3)
                                        ? ""
                                        : reader.GetString(3),

                                TipoActividad =
                                    reader.IsDBNull(4)
                                        ? ""
                                        : reader.GetString(4),

                                Hora =
                                    reader.IsDBNull(5)
                                        ? ""
                                        : reader.GetString(5),

                                NotasLibro =
                                    reader.IsDBNull(6)
                                        ? ""
                                        : reader.GetString(6),

                                CompaneroId =
                                    reader.IsDBNull(7)
                                        ? 0
                                        : reader.GetInt32(7),

                                Direccion =
                                    reader.IsDBNull(8)
                                        ? ""
                                        : reader.GetString(8),

                                Latitud =
                                    reader.IsDBNull(9)
                                        ? 0
                                        : reader.GetDouble(9),

                                Longitud =
                                    reader.IsDBNull(10)
                                        ? 0
                                        : reader.GetDouble(10)
                            });
                    }
                }
            }

            return lista;
        }

        public static List<ActividadPrograma> ObtenerActividadesPrograma(
            string congreCodigo,
            string mesReferenciaStr,
            string dia,
            string turno,
            string responsable)
        {
            var lista = new List<ActividadPrograma>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        Id,
                        TipoActividad,
                        Hora,
                        NotasLibro,
                        CompaneroId,
                        Direccion,
                        Latitud,
                        Longitud
                    FROM ProgramaVisitaV2
                    WHERE
                        CongreCodigo = $codigo
                        AND MesReferencia = $mes
                        AND DiaSemana = $dia
                        AND Turno = $turno
                        AND Responsable = $responsable
                    ORDER BY Hora ASC;";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferenciaStr);
                cmd.Parameters.AddWithValue("$dia", dia);
                cmd.Parameters.AddWithValue("$turno", turno);
                cmd.Parameters.AddWithValue("$responsable", responsable);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            new ActividadPrograma
                            {
                                Id =
                                    reader.GetInt32(0),

                                TipoActividad =
                                    reader.IsDBNull(1)
                                        ? ""
                                        : reader.GetString(1),

                                Hora =
                                    reader.IsDBNull(2)
                                        ? ""
                                        : reader.GetString(2),

                                NotasLibro =
                                    reader.IsDBNull(3)
                                        ? ""
                                        : reader.GetString(3),

                                CompaneroId =
                                    reader.IsDBNull(4)
                                        ? 0
                                        : reader.GetInt32(4),

                                Direccion =
                                    reader.IsDBNull(5)
                                        ? ""
                                        : reader.GetString(5),

                                Latitud =
                                    reader.IsDBNull(6)
                                        ? 0
                                        : reader.GetDouble(6),

                                Longitud =
                                    reader.IsDBNull(7)
                                        ? 0
                                        : reader.GetDouble(7)
                            });
                    }
                }
            }

            return lista;
        }

        public static ContabilidadMes ObtenerContabilidad(string congreCodigo, string mesReferencia)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Gastos, Mantenimiento, ObraMundial, ObraResolucion, Saldo, COALESCE(SaldoMaximo, 0) FROM ContabilidadMes WHERE CongreCodigo = $codigo AND MesReferencia = $mes;";
                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferencia);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new ContabilidadMes
                        {
                            Gastos = reader.IsDBNull(0) ? 0 : reader.GetDouble(0),
                            Mantenimiento = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                            ObraMundial = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                            ObraResolucion = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                            Saldo = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                            SaldoMaximo = reader.IsDBNull(5) ? 0 : reader.GetDouble(5)
                        };
                    }
                }
            }
            return new ContabilidadMes();
        }

        public static void GuardarContabilidad(string congreCodigo, string mesReferencia, ContabilidadMes datos)
        {
            AnotarActividad(
                "Visitas",
                "Guardó la contabilidad de " + congreCodigo +
                " (" + mesReferencia + ")");

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ContabilidadMes (CongreCodigo, MesReferencia, Gastos, Mantenimiento, ObraMundial, ObraResolucion, Saldo, SaldoMaximo)
                    VALUES ($codigo, $mes, $g, $m, $om, $or, $s, $smax)
                    ON CONFLICT(CongreCodigo, MesReferencia) DO UPDATE SET 
                    Gastos = $g,
                    Mantenimiento = $m,
                    ObraMundial = $om,
                    ObraResolucion = $or,
                    Saldo = $s,
                    SaldoMaximo = $smax;";

                cmd.Parameters.AddWithValue("$codigo", congreCodigo);
                cmd.Parameters.AddWithValue("$mes", mesReferencia);
                cmd.Parameters.AddWithValue("$g", datos.Gastos);
                cmd.Parameters.AddWithValue("$m", datos.Mantenimiento);
                cmd.Parameters.AddWithValue("$om", datos.ObraMundial);
                cmd.Parameters.AddWithValue("$or", datos.ObraResolucion);
                cmd.Parameters.AddWithValue("$s", datos.Saldo);
                cmd.Parameters.AddWithValue("$smax", datos.SaldoMaximo);
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region 8. GESTIÓN DE ASAMBLEAS Y PRECURSORES

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
                    cmd.CommandText = "SELECT p.*, c.Nombre as NombreCongregacion FROM Personas p LEFT JOIN Congregaciones c ON p.CongregacionCodigo = c.Codigo WHERE p.Id = @id";
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
                                else if (col == "nombrecomun") info.NombreComun = val;
                                else if (col == "nombrecongregacion") info.Congregacion = val;
                                else if (col == "telefono" || col == "movil" || col == "teléfono" || col == "móvil") { if (string.IsNullOrEmpty(info.Telefono)) info.Telefono = val; }
                                else if (col == "emailjw") info.EmailJW = val;
                                else if (col == "email" || col == "correo") { if (string.IsNullOrEmpty(info.Email)) info.Email = val; }
                                else if (col == "fechanacimiento")
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(val, @"\d{4}");
                                    if (match.Success) info.AnioNacimiento = match.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return info;
        }

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
                    cmd.CommandText = "SELECT Turno, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo, Enviado FROM ProgramaAsambleasPartes WHERE AsambleaId = @id ORDER BY Id ASC";
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
                                Tipo = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                Enviado = reader.FieldCount > 7 && !reader.IsDBNull(7) && reader.GetInt32(7) == 1
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
                        insertParte.CommandText = "INSERT INTO ProgramaAsambleasPartes (AsambleaId, Turno, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo, Enviado) VALUES (@id, @turno, @hora, @titulo, @asignado, @part, @ruta, @tipoParte, @enviado);";
                        insertParte.Parameters.AddWithValue("@id", asambleaId); insertParte.Parameters.AddWithValue("@turno", parte.Turno);
                        insertParte.Parameters.AddWithValue("@hora", parte.Hora ?? ""); insertParte.Parameters.AddWithValue("@titulo", parte.Titulo ?? "");
                        insertParte.Parameters.AddWithValue("@asignado", parte.AsignadoId); insertParte.Parameters.AddWithValue("@part", parte.Participantes ?? "");
                        insertParte.Parameters.AddWithValue("@ruta", parte.RutaArchivo ?? ""); insertParte.Parameters.AddWithValue("@tipoParte", parte.Tipo ?? "");
                        insertParte.Parameters.AddWithValue("@enviado", parte.Enviado ? 1 : 0);
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
                            cmdPartes.CommandText = "SELECT Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo, Enviado FROM ReunionPrecursoresPartes WHERE SesionId = @sesionId ORDER BY Id ASC";
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
                                        Tipo = readerPartes.IsDBNull(5) ? "" : readerPartes.GetString(5),
                                        Enviado = readerPartes.FieldCount > 6 && !readerPartes.IsDBNull(6) && readerPartes.GetInt32(6) == 1
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
                        insParte.CommandText = "INSERT INTO ReunionPrecursoresPartes (SesionId, Hora, Titulo, AsignadoId, Participantes, RutaArchivo, Tipo, Enviado) VALUES (@sesionId, @hora, @titulo, @asignado, @part, @ruta, @tipoParte, @enviado);";
                        insParte.Parameters.AddWithValue("@sesionId", newSesionId); insParte.Parameters.AddWithValue("@hora", parte.Hora ?? "");
                        insParte.Parameters.AddWithValue("@titulo", parte.Titulo ?? ""); insParte.Parameters.AddWithValue("@asignado", parte.AsignadoId);
                        insParte.Parameters.AddWithValue("@part", parte.Participantes ?? ""); insParte.Parameters.AddWithValue("@ruta", parte.RutaArchivo ?? "");
                        insParte.Parameters.AddWithValue("@tipoParte", parte.Tipo ?? "");
                        insParte.Parameters.AddWithValue("@enviado", parte.Enviado ? 1 : 0);
                        insParte.ExecuteNonQuery();
                    }
                }
            }
        }

        #endregion

        #region 9. GESTIÓN DE TERRITORIOS

        public static void GuardarTerritoriosImportados(string congreCodigo, List<TerritorioJSON> territorios)
        {
            AnotarActividad(
                "Visitas",
                "Importó " + (territorios?.Count ?? 0) +
                " territorios de " + congreCodigo);

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
                        while (reader.Read())
                        {
                            string nombre = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            if (!string.IsNullOrWhiteSpace(nombre))
                            {
                                dictCreacion[nombre] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            }
                        }
                    }

                    var cmdDelete = connection.CreateCommand();
                    cmdDelete.Transaction = transaction;
                    cmdDelete.CommandText = "DELETE FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $codigo;";
                    cmdDelete.Parameters.AddWithValue("$codigo", congreCodigo);
                    cmdDelete.ExecuteNonQuery();

                    var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO TerritoriosVisitaV3 (CongregacionCodigo, Nombre, UltimaFechaCompletado, AsignadoA, FechaAsignado, FechaCompletado, NombreVinculado, FechaCreacion) VALUES ($codigo, $nombre, $ultima, $asignado, $fechaAsig, $fechaComp, '', $creacion);";

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
                            Nombre = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            UltimaFechaCompletado = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            AsignadoA = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            FechaAsignado = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            FechaCompletado = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            NombreVinculado = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            FechaCreacion = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            FechaEliminacion = reader.IsDBNull(8) ? "" : reader.GetString(8)
                        });
                    }
                }
            }
            return lista;
        }

        public static void EliminarRegistroTerritorio(int idRegistro)
        {
            if (idRegistro <= 0) return;

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM TerritoriosVisitaV3 WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", idRegistro);
                cmd.ExecuteNonQuery();
            }
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

        #endregion

        #region 10. UTILIDADES (Documentos, Configuración y Algoritmos)

        public static void InsertarDocumento(string categoria, string nombreArchivo, string rutaLocal)
        {
            byte[] contenidoBinario = null;

            if (!string.IsNullOrWhiteSpace(rutaLocal) && File.Exists(rutaLocal))
                contenidoBinario = File.ReadAllBytes(rutaLocal);

            InsertarDocumentoInterno(
                categoria,
                nombreArchivo,
                rutaLocal,
                contenidoBinario);
        }

        public static void InsertarDocumentoDesdeArchivo(string categoria, string rutaArchivo)
        {
            if (string.IsNullOrWhiteSpace(rutaArchivo) || !File.Exists(rutaArchivo))
                throw new FileNotFoundException("No se encuentra el archivo seleccionado.", rutaArchivo);

            string nombreArchivo = Path.GetFileName(rutaArchivo);
            byte[] contenidoBinario = File.ReadAllBytes(rutaArchivo);

            InsertarDocumentoInterno(
                categoria,
                nombreArchivo,
                rutaArchivo,
                contenidoBinario);
        }

        private static void InsertarDocumentoInterno(
            string categoria,
            string nombreArchivo,
            string rutaLocal,
            byte[] contenidoBinario)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Documentos
                    (
                        Categoria,
                        NombreArchivo,
                        RutaLocal,
                        ContenidoArchivo
                    )
                    VALUES
                    (
                        $cat,
                        $nom,
                        $ruta,
                        $blob
                    );";

                cmd.Parameters.AddWithValue("$cat", categoria ?? "");
                cmd.Parameters.AddWithValue("$nom", nombreArchivo ?? "");
                cmd.Parameters.AddWithValue("$ruta", rutaLocal ?? "");
                cmd.Parameters.AddWithValue(
                    "$blob",
                    contenidoBinario != null
                        ? (object)contenidoBinario
                        : DBNull.Value);

                cmd.ExecuteNonQuery();
            }
        }

        private static string LimpiarNombreArchivoTemporal(
            string nombreArchivo)
        {
            string limpio =
                string.IsNullOrWhiteSpace(nombreArchivo)
                    ? "documento"
                    : nombreArchivo;

            foreach (char c in Path.GetInvalidFileNameChars())
                limpio = limpio.Replace(c, '_');

            return limpio;
        }

        private static string ObtenerDirectorioDocumentoTemporal(
            int documentoId)
        {
            return Path.Combine(
                Path.GetTempPath(),
                "SCCosmos",
                "DocumentosTemp",
                documentoId.ToString());
        }

        private static string MaterializarDocumentoTemporal(
            int documentoId,
            string nombreArchivo,
            byte[] contenido)
        {
            if (contenido == null || contenido.Length == 0)
                return null;

            string tempPath =
                ObtenerDirectorioDocumentoTemporal(documentoId);

            if (!Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            string filePath = Path.Combine(
                tempPath,
                LimpiarNombreArchivoTemporal(nombreArchivo));

            bool escribir = true;

            try
            {
                if (File.Exists(filePath))
                {
                    var info = new FileInfo(filePath);
                    escribir = info.Length != contenido.LongLength;
                }
            }
            catch
            {
                escribir = true;
            }

            if (escribir)
                File.WriteAllBytes(filePath, contenido);

            return filePath;
        }

        public static string ExtraerDocumentoDesdeBD(
            int documentoId,
            string nombreArchivo)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        ContenidoArchivo,
                        RutaLocal
                    FROM Documentos
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$id", documentoId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    if (!reader.IsDBNull(0))
                    {
                        byte[] fileBytes =
                            (byte[])reader.GetValue(0);

                        return MaterializarDocumentoTemporal(
                            documentoId,
                            nombreArchivo,
                            fileBytes);
                    }

                    // Compatibilidad con documentos antiguos que solo
                    // guardaban una ruta local.
                    string rutaAntigua =
                        reader.IsDBNull(1)
                            ? ""
                            : reader.GetString(1);

                    return File.Exists(rutaAntigua)
                        ? rutaAntigua
                        : null;
                }
            }
        }

        public static List<ArchivoDocumento> ObtenerDocumentos(
            string categoria)
        {
            var lista =
                new List<ArchivoDocumento>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        Id,
                        NombreArchivo,
                        RutaLocal,
                        ContenidoArchivo
                    FROM Documentos
                    WHERE Categoria = $cat
                    ORDER BY Id ASC;";

                cmd.Parameters.AddWithValue(
                    "$cat",
                    categoria ?? "");

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        string nombre =
                            reader.IsDBNull(1)
                                ? ""
                                : reader.GetString(1);

                        string rutaLocal =
                            reader.IsDBNull(2)
                                ? ""
                                : reader.GetString(2);

                        // Si existe BLOB, RutaLocal se sustituye por una
                        // copia temporal válida EN ESTE DISPOSITIVO.
                        // De este modo, cualquier código legacy que todavía
                        // use ArchivoDocumento.RutaLocal sigue funcionando
                        // aunque la base de datos venga de otro equipo.
                        if (!reader.IsDBNull(3))
                        {
                            byte[] contenido =
                                (byte[])reader.GetValue(3);

                            string temporal =
                                MaterializarDocumentoTemporal(
                                    id,
                                    nombre,
                                    contenido);

                            if (!string.IsNullOrWhiteSpace(temporal))
                                rutaLocal = temporal;
                        }

                        lista.Add(
                            new ArchivoDocumento
                            {
                                Id = id,
                                NombreArchivo = nombre,
                                RutaLocal = rutaLocal,
                                Categoria = categoria
                            });
                    }
                }
            }

            return lista;
        }

        public static void ActualizarDocumentoDesdeArchivo(
            int documentoId,
            string rutaArchivo)
        {
            if (documentoId <= 0)
                throw new ArgumentOutOfRangeException(nameof(documentoId));

            if (string.IsNullOrWhiteSpace(rutaArchivo) || !File.Exists(rutaArchivo))
                throw new FileNotFoundException("No se encuentra el archivo que se quiere guardar en la base de datos.", rutaArchivo);

            byte[] contenido = File.ReadAllBytes(rutaArchivo);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Documentos
                    SET ContenidoArchivo = $blob,
                        RutaLocal = $ruta
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$id", documentoId);
                cmd.Parameters.AddWithValue("$ruta", rutaArchivo);
                cmd.Parameters.AddWithValue("$blob", contenido);
                cmd.ExecuteNonQuery();
            }
        }

        public static void EliminarDocumento(int id)
        {
            string nombre = "";

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmdNombre = connection.CreateCommand();
                cmdNombre.CommandText =
                    "SELECT NombreArchivo FROM Documentos WHERE Id = $id;";
                cmdNombre.Parameters.AddWithValue("$id", id);

                object nombreObj = cmdNombre.ExecuteScalar();
                if (nombreObj != null && nombreObj != DBNull.Value)
                    nombre = nombreObj.ToString();

                var cmd = connection.CreateCommand();
                cmd.CommandText =
                    "DELETE FROM Documentos WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            // Limpieza únicamente de la copia temporal local.
            try
            {
                string tempDir =
                    ObtenerDirectorioDocumentoTemporal(id);

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }

        public static int MigrarDocumentosLegacyABlob()
        {
            int migrados = 0;
            var pendientes = new List<Tuple<int, string>>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT Id, RutaLocal
                    FROM Documentos
                    WHERE ContenidoArchivo IS NULL
                      AND RutaLocal IS NOT NULL
                      AND trim(RutaLocal) <> '';";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pendientes.Add(
                            Tuple.Create(
                                reader.GetInt32(0),
                                reader.IsDBNull(1)
                                    ? ""
                                    : reader.GetString(1)));
                    }
                }
            }

            foreach (var item in pendientes)
            {
                try
                {
                    if (!File.Exists(item.Item2))
                        continue;

                    byte[] contenido = File.ReadAllBytes(item.Item2);

                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        connection.Open();

                        var cmd = connection.CreateCommand();
                        cmd.CommandText = @"
                            UPDATE Documentos
                            SET ContenidoArchivo = $blob
                            WHERE Id = $id
                              AND ContenidoArchivo IS NULL;";

                        cmd.Parameters.AddWithValue("$id", item.Item1);
                        cmd.Parameters.AddWithValue("$blob", contenido);

                        if (cmd.ExecuteNonQuery() > 0)
                            migrados++;
                    }
                }
                catch
                {
                    // Un archivo legacy inaccesible se conserva sin tocar.
                }
            }

            return migrados;
        }

        // =====================================================
        // VARIABLES EDITABLES DE CORRESPONDENCIA
        // =====================================================
        public static void InicializarVariablesCorrespondencia()
        {
            InicializarDatabase();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmdTabla = connection.CreateCommand();
                cmdTabla.CommandText = @"
                    CREATE TABLE IF NOT EXISTS VariablesCorrespondencia
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Categoria TEXT NOT NULL,
                        Etiqueta TEXT NOT NULL,
                        Token TEXT NOT NULL,
                        ClaveSistema TEXT,
                        Orden INTEGER DEFAULT 0
                    );";
                cmdTabla.ExecuteNonQuery();
            }

            string migrada =
                ObtenerConfiguracion(
                    "VariablesCorrespondenciaMigradas",
                    "0");

            if (migrada == "1")
                return;

            // Las variables existentes se conservan exactamente como estaban
            // configuradas antes de esta migración.
            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con SCS)",
                "Nombre SCS",
                ObtenerConfiguracion(
                    "VarSCS",
                    "$_NOMBRE_SCS"),
                "VarSCS",
                0);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con SCS)",
                "Apellido SCS",
                ObtenerConfiguracion(
                    "VarApSCS",
                    "$_APELLIDO_SCS"),
                "VarApSCS",
                1);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con SCS)",
                "Su esposa",
                ObtenerConfiguracion(
                    "VarEsposaSCS",
                    "$_ESPOSA_SCS"),
                "VarEsposaSCS",
                2);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con visita de pastoreo)",
                "Hermano",
                ObtenerConfiguracion(
                    "VarPastoreo",
                    "$_NOMBRE_VISITA"),
                "VarPastoreo",
                0);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con visita de pastoreo)",
                "Apellido",
                ObtenerConfiguracion(
                    "VarApPastoreo",
                    "$_APELLIDO_VISITA"),
                "VarApPastoreo",
                1);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Documentación (con visita de pastoreo)",
                "Esposa",
                ObtenerConfiguracion(
                    "VarEsposaPastoreo",
                    "$_ESPOSA_VISITA"),
                "VarEsposaPastoreo",
                2);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación asamblea circuito",
                "Nombre orador",
                ObtenerConfiguracion(
                    "VarNomAsig",
                    "$_NOMBRE_ORADOR"),
                "VarNomAsig",
                0);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación asamblea circuito",
                "Apellido orador",
                ObtenerConfiguracion(
                    "VarApAsig",
                    "$_APELLIDO_ORADOR"),
                "VarApAsig",
                1);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación asamblea circuito",
                "Tema asignado",
                ObtenerConfiguracion(
                    "VarTemaAsig",
                    "$_TEMA_ASIGNADO"),
                "VarTemaAsig",
                2);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación reunión precursores",
                "Nombre orador",
                ObtenerConfiguracion(
                    "VarNomAsigPrec",
                    "$_NOMBRE_ORADOR"),
                "VarNomAsigPrec",
                0);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación reunión precursores",
                "Apellido orador",
                ObtenerConfiguracion(
                    "VarApAsigPrec",
                    "$_APELLIDO_ORADOR"),
                "VarApAsigPrec",
                1);

            InsertarVariableCorrespondenciaSiNoExiste(
                "Asignación reunión precursores",
                "Tema asignado",
                ObtenerConfiguracion(
                    "VarTemaAsigPrec",
                    "$_TEMA_ASIGNADO"),
                "VarTemaAsigPrec",
                2);

            GuardarConfiguracion(
                "VariablesCorrespondenciaMigradas",
                "1");
        }

        private static void InsertarVariableCorrespondenciaSiNoExiste(
            string categoria,
            string etiqueta,
            string token,
            string claveSistema,
            int orden)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var check = connection.CreateCommand();
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM VariablesCorrespondencia
                    WHERE Categoria = $categoria
                      AND ClaveSistema = $clave;";

                check.Parameters.AddWithValue(
                    "$categoria",
                    categoria ?? "");
                check.Parameters.AddWithValue(
                    "$clave",
                    claveSistema ?? "");

                long existe =
                    Convert.ToInt64(
                        check.ExecuteScalar() ?? 0);

                if (existe > 0)
                    return;

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO VariablesCorrespondencia
                    (
                        Categoria,
                        Etiqueta,
                        Token,
                        ClaveSistema,
                        Orden
                    )
                    VALUES
                    (
                        $categoria,
                        $etiqueta,
                        $token,
                        $clave,
                        $orden
                    );";

                cmd.Parameters.AddWithValue(
                    "$categoria",
                    categoria ?? "");
                cmd.Parameters.AddWithValue(
                    "$etiqueta",
                    etiqueta ?? "");
                cmd.Parameters.AddWithValue(
                    "$token",
                    token ?? "");
                cmd.Parameters.AddWithValue(
                    "$clave",
                    claveSistema ?? "");
                cmd.Parameters.AddWithValue(
                    "$orden",
                    orden);

                cmd.ExecuteNonQuery();
            }
        }

        public static List<VariableCorrespondencia>
            ObtenerVariablesCorrespondencia(string categoria)
        {
            var lista =
                new List<VariableCorrespondencia>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        Id,
                        Categoria,
                        Etiqueta,
                        Token,
                        ClaveSistema,
                        Orden
                    FROM VariablesCorrespondencia
                    WHERE Categoria = $categoria
                    ORDER BY Orden, Id;";

                cmd.Parameters.AddWithValue(
                    "$categoria",
                    categoria ?? "");

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            new VariableCorrespondencia
                            {
                                Id = reader.GetInt32(0),
                                Categoria =
                                    reader.IsDBNull(1)
                                        ? ""
                                        : reader.GetString(1),
                                Etiqueta =
                                    reader.IsDBNull(2)
                                        ? ""
                                        : reader.GetString(2),
                                Token =
                                    reader.IsDBNull(3)
                                        ? ""
                                        : reader.GetString(3),
                                ClaveSistema =
                                    reader.IsDBNull(4)
                                        ? ""
                                        : reader.GetString(4),
                                Orden =
                                    reader.IsDBNull(5)
                                        ? 0
                                        : reader.GetInt32(5)
                            });
                    }
                }
            }

            return lista;
        }

        public static int InsertarVariableCorrespondencia(
            VariableCorrespondencia variable)
        {
            if (variable == null)
                throw new ArgumentNullException(nameof(variable));

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var check = connection.CreateCommand();
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM VariablesCorrespondencia
                    WHERE Categoria = $categoria
                      AND Token = $token;";
                check.Parameters.AddWithValue(
                    "$categoria",
                    variable.Categoria ?? "");
                check.Parameters.AddWithValue(
                    "$token",
                    variable.Token ?? "");

                if (Convert.ToInt64(check.ExecuteScalar() ?? 0) > 0)
                    throw new InvalidOperationException(
                        "Ya existe una variable con ese código en esta categoría.");

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO VariablesCorrespondencia
                    (
                        Categoria,
                        Etiqueta,
                        Token,
                        ClaveSistema,
                        Orden
                    )
                    VALUES
                    (
                        $categoria,
                        $etiqueta,
                        $token,
                        $clave,
                        $orden
                    );";

                cmd.Parameters.AddWithValue(
                    "$categoria",
                    variable.Categoria ?? "");
                cmd.Parameters.AddWithValue(
                    "$etiqueta",
                    variable.Etiqueta ?? "");
                cmd.Parameters.AddWithValue(
                    "$token",
                    variable.Token ?? "");
                cmd.Parameters.AddWithValue(
                    "$clave",
                    variable.ClaveSistema ?? "");
                cmd.Parameters.AddWithValue(
                    "$orden",
                    variable.Orden);

                cmd.ExecuteNonQuery();

                var cmdId = connection.CreateCommand();
                cmdId.CommandText = "SELECT last_insert_rowid();";

                return Convert.ToInt32(cmdId.ExecuteScalar());
            }
        }

        public static void ActualizarVariableCorrespondencia(
            VariableCorrespondencia variable)
        {
            if (variable == null || variable.Id <= 0)
                return;

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var check = connection.CreateCommand();
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM VariablesCorrespondencia
                    WHERE Categoria = $categoria
                      AND Token = $token
                      AND Id <> $id;";
                check.Parameters.AddWithValue(
                    "$categoria",
                    variable.Categoria ?? "");
                check.Parameters.AddWithValue(
                    "$token",
                    variable.Token ?? "");
                check.Parameters.AddWithValue(
                    "$id",
                    variable.Id);

                if (Convert.ToInt64(check.ExecuteScalar() ?? 0) > 0)
                    throw new InvalidOperationException(
                        "Ya existe una variable con ese código en esta categoría.");

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE VariablesCorrespondencia
                    SET
                        Etiqueta = $etiqueta,
                        Token = $token,
                        Orden = $orden
                    WHERE Id = $id;";

                cmd.Parameters.AddWithValue(
                    "$id",
                    variable.Id);
                cmd.Parameters.AddWithValue(
                    "$etiqueta",
                    variable.Etiqueta ?? "");
                cmd.Parameters.AddWithValue(
                    "$token",
                    variable.Token ?? "");
                cmd.Parameters.AddWithValue(
                    "$orden",
                    variable.Orden);

                cmd.ExecuteNonQuery();
            }

            // Las variables originales siguen alimentando las claves
            // legacy usadas por otros módulos del programa.
            if (!string.IsNullOrWhiteSpace(variable.ClaveSistema))
            {
                GuardarConfiguracion(
                    variable.ClaveSistema,
                    variable.Token ?? "");
            }
        }

        public static void EliminarVariableCorrespondencia(int id)
        {
            string claveSistema = "";

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                var get = connection.CreateCommand();
                get.CommandText = @"
                    SELECT ClaveSistema
                    FROM VariablesCorrespondencia
                    WHERE Id = $id;";
                get.Parameters.AddWithValue("$id", id);

                object claveObj = get.ExecuteScalar();
                if (claveObj != null && claveObj != DBNull.Value)
                    claveSistema = claveObj.ToString();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM VariablesCorrespondencia
                    WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            // Si era una variable del sistema, dejamos una marca imposible
            // de encontrar en los documentos para que al eliminarla deje de
            // aplicarse en módulos legacy que todavía leen Configuracion.
            if (!string.IsNullOrWhiteSpace(claveSistema))
            {
                GuardarConfiguracion(
                    claveSistema,
                    "$_VARIABLE_DESACTIVADA_" + id);
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

        public static void GuardarMapeoNombre(string congreCodigo, string nombreS13, string nombreS21)
        {
            if (string.IsNullOrWhiteSpace(nombreS13)) return;
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO MapeoS13S21 (CongregacionCodigo, NombreS13, NombreS21) VALUES ($codigo, $s13, $s21) ON CONFLICT(CongregacionCodigo, NombreS13) DO UPDATE SET NombreS21 = $s21;";
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
                using (var reader = cmd.ExecuteReader()) { while (reader.Read()) dict[reader.GetString(0)] = reader.GetString(1); }
            }
            return dict;
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

            var palabrasOrigen = origenLimpio.Split(separadores, StringSplitOptions.RemoveEmptyEntries).Where(p => !palabrasIgnoradas.Contains(p)).ToArray();
            var palabrasDestino = destinoLimpio.Split(separadores, StringSplitOptions.RemoveEmptyEntries).Where(p => !palabrasIgnoradas.Contains(p)).ToArray();

            if (palabrasOrigen.Length == 0 || palabrasDestino.Length == 0) return 0;

            int puntos = 0; int maxPuntos = 0;

            foreach (var pOrig in palabrasOrigen)
            {
                int pesoPalabra = pOrig.Length;
                if (pOrig == nombrePrincipal) pesoPalabra *= 10;
                maxPuntos += pesoPalabra;

                int mejorCoincidencia = 0;
                foreach (var pDest in palabrasDestino)
                {
                    if (pOrig == pDest) { mejorCoincidencia = pesoPalabra; break; }
                    if (pOrig.Length > 3 && pDest.Length > 3) { if (DistanciaPalabra(pOrig, pDest) <= 1) mejorCoincidencia = Math.Max(mejorCoincidencia, (int)(pesoPalabra * 0.8)); }
                }
                puntos += mejorCoincidencia;
            }
            return (int)((double)puntos / maxPuntos * 100);
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
            foreach (var c in textoNormalizado) { if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) constructor.Append(c); }
            return constructor.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        #endregion

        #region 11. MODELOS DE DATOS SECUNDARIOS (Internos)

        public class InfoHermanoPDF
        {
            public string NombreFormateado { get; set; }
            public string NombreComun { get; set; }
            public string Congregacion { get; set; }
            public string Telefono { get; set; }
            public string Email { get; set; }
            public string EmailJW { get; set; }
            public string AnioNacimiento { get; set; }
        }

        public class AsignacionOrganigramaBD
        {
            public string Departamento { get; set; }
            public int AsignadoId { get; set; }
            public string Rol { get; set; }
        }

        public class ContabilidadMes
        {
            public double Gastos { get; set; }
            public double Mantenimiento { get; set; }
            public double ObraMundial { get; set; }
            public double ObraResolucion { get; set; }
            public double Saldo { get; set; }
            public double SaldoMaximo { get; set; }
        }

        public class ActividadPrograma
        {
            public string DiaSemana { get; set; }
            public string Turno { get; set; }
            public string Responsable { get; set; }

            public int Id { get; set; }
            public string TipoActividad { get; set; }
            public string Hora { get; set; }
            public string NotasLibro { get; set; }
            public int CompaneroId { get; set; }
            public string Direccion { get; set; }
            public double Latitud { get; set; }
            public double Longitud { get; set; }
        }

        #endregion
    }

    #region MODELOS DE DATOS GENERALES (Módulos externos)

    public class SeccionCircuito
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string LugarAsamblea { get; set; }
    }

    public class ElementoMapa
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string Coordenadas { get; set; }
        public string Descripcion { get; set; }
    }

    public class NotaVisita : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public int Id { get; set; }
        public int SemanaId { get; set; }
        public string Cabecera { get; set; }
        public string Pregunta { get; set; }
        private string _respuesta; public string Respuesta { get => _respuesta; set { _respuesta = value; OnPropertyChanged("Respuesta"); } }
        private bool _esAlerta; public bool EsAlerta { get => _esAlerta; set { _esAlerta = value; OnPropertyChanged("EsAlerta"); OnPropertyChanged("FondoRespuesta"); OnPropertyChanged("ColorTextoRespuesta"); } }

        public Brush FondoRespuesta => EsAlerta ? (Brush)new BrushConverter().ConvertFrom("#FFCDD2") : (Brush)new BrushConverter().ConvertFrom("#1E1E1E");
        public Brush ColorTextoRespuesta => EsAlerta ? Brushes.Black : Brushes.White;
    }

    #endregion
}