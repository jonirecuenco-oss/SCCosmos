using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// =============================================================
// ESQUEMA DE UN DISPOSITIVO NUEVO
//
// Android solo sabe crear parte de sus tablas: las demás venían en
// la copia de la base, que ya no se reparte. Un dispositivo nuevo
// se prepara con este esquema y recibe todo por sincronización.
//
// Debe crearse antes de la primera sincronización: las filas de una
// tabla que no existe se descartan y no se vuelven a enviar.
//
// Es el esquema de la base del PC. Si allí se añaden tablas o
// columnas, hay que añadirlas también aquí: lo comprueba el banco
// (SincroPruebas).
// =============================================================

public static class EsquemaBase
{
    /// <summary>
    /// Solo actúa si la base aún no tiene personas: a una base en uso no
    /// le cambia nada. Devuelve si ha preparado el esquema.
    /// </summary>
    public static bool CrearSiHaceFalta(
        string rutaDb)
    {
        using var conexion =
            new SqliteConnection(
                $"Data Source={rutaDb};Pooling=False");

        conexion.Open();

        using (var comprobar =
               conexion.CreateCommand())
        {
            comprobar.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Personas';";

            if (Convert.ToInt64(
                    comprobar.ExecuteScalar())
                > 0)
            {
                return false;
            }
        }

        // Igual que la base del PC: quien lee no espera a quien escribe.
        Ejecutar(
            conexion,
            null,
            "PRAGMA journal_mode = WAL;");

        using var transaccion =
            conexion.BeginTransaction();

        foreach (string sql in Tablas)
        {
            Ejecutar(
                conexion,
                transaccion,
                sql);
        }

        CompletarColumnas(
            conexion,
            transaccion);

        transaccion.Commit();

        return true;
    }


    /// <summary>
    /// La app crea por su cuenta algunas tablas al arrancar: si un dispositivo
    /// ya las tenía de antes, pueden faltarles columnas, y lo que llegara para
    /// ellas se perdería.
    /// </summary>
    private static void CompletarColumnas(
        SqliteConnection conexion,
        SqliteTransaction transaccion)
    {
        using var modelo =
            new SqliteConnection(
                "Data Source=:memory:");

        modelo.Open();

        foreach (string sql in Tablas)
        {
            Ejecutar(
                modelo,
                null,
                sql);
        }

        var tablas =
            new List<string>();

        using (var cmd =
               modelo.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                tablas.Add(
                    lector.GetString(0));
            }
        }

        foreach (string tabla in tablas)
        {
            var existentes =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            using (var cmd =
                   conexion.CreateCommand())
            {
                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    $"SELECT name FROM pragma_table_info('{tabla}');";

                using var lector =
                    cmd.ExecuteReader();

                while (lector.Read())
                {
                    existentes.Add(
                        lector.GetString(0));
                }
            }

            using var columnas =
                modelo.CreateCommand();

            columnas.CommandText =
                $"SELECT name, type, dflt_value FROM pragma_table_info('{tabla}');";

            using var filas =
                columnas.ExecuteReader();

            while (filas.Read())
            {
                string columna =
                    filas.GetString(0);

                if (existentes.Contains(
                        columna))
                {
                    continue;
                }

                string predeterminado =
                    filas.IsDBNull(2)
                        ? ""
                        : $" DEFAULT {filas.GetString(2)}";

                Ejecutar(
                    conexion,
                    transaccion,
                    $"ALTER TABLE \"{tabla}\" ADD COLUMN \"{columna}\" {filas.GetString(1)}{predeterminado};");
            }
        }
    }


    private static void Ejecutar(
        SqliteConnection conexion,
        SqliteTransaction? transaccion,
        string sql)
    {
        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            sql;

        cmd.ExecuteNonQuery();
    }


    private static readonly string[] Tablas =
    {
        """
        CREATE TABLE IF NOT EXISTS "ActividadPersonas" ( Id INTEGER PRIMARY KEY AUTOINCREMENT, PersonaId INTEGER, Mes TEXT, AnioServicio TEXT, Participacion INTEGER, CursosBiblicos INTEGER, PrecursorAuxiliar INTEGER DEFAULT 0, Horas INTEGER, Notas TEXT, Cursos TEXT, UNIQUE(PersonaId, Mes, AnioServicio), FOREIGN KEY(PersonaId) REFERENCES Personas(Id) ON DELETE CASCADE )
        """,

        """
        CREATE TABLE IF NOT EXISTS AlertasLeidas ( Clave TEXT PRIMARY KEY )
        """,

        """
        CREATE TABLE IF NOT EXISTS Calendario ( Id INTEGER PRIMARY KEY AUTOINCREMENT, FechaMartes TEXT UNIQUE, Actividad TEXT, Congregacion TEXT, Acompanante TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS Configuracion (Clave TEXT PRIMARY KEY, Valor TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS Congregaciones ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Codigo TEXT, Nombre TEXT NOT NULL, Email TEXT , SeccionId INTEGER DEFAULT 0, DireccionSR TEXT, HoraEntreSemana TEXT, DiaFinSemana TEXT, HoraFinSemana TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, Capacidad INTEGER DEFAULT 0, Poblacion INTEGER DEFAULT 0, DiaEntreSemana TEXT, Provincia TEXT, Municipio TEXT, IdZoom TEXT, ClaveZoom TEXT, EmailAV TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS ContabilidadMes ( CongreCodigo TEXT, MesReferencia TEXT, Gastos REAL, Mantenimiento REAL, ObraMundial REAL, ObraResolucion REAL, Saldo REAL, SaldoMaximo REAL DEFAULT 0, PRIMARY KEY(CongreCodigo, MesReferencia) )
        """,

        """
        CREATE TABLE IF NOT EXISTS Documentos ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Categoria TEXT, NombreArchivo TEXT, RutaLocal TEXT , ContenidoArchivo BLOB)
        """,

        """
        CREATE TABLE IF NOT EXISTS Donaciones ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Fecha TEXT, PersonaId INTEGER DEFAULT 0, NombrePersona TEXT, CongregacionCodigo TEXT, CongregacionNombre TEXT, Cantidad REAL DEFAULT 0, MedioPago TEXT, Notas TEXT, FechaCreacion TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS ElementosMapa (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT, Tipo TEXT, Coordenadas TEXT, Descripcion TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS EscuelasAlumnos ( Id TEXT PRIMARY KEY, EscuelaId TEXT DEFAULT '', PersonaId INTEGER DEFAULT 0, Nombre TEXT DEFAULT '', Asiento INTEGER DEFAULT 0, LimpiezaDia TEXT DEFAULT '' )
        """,

        """
        CREATE TABLE IF NOT EXISTS EscuelasClases ( Id TEXT PRIMARY KEY, SemanaInicio TEXT DEFAULT '', Congregacion TEXT DEFAULT '', InstructorId INTEGER DEFAULT 0, InstructorNombre TEXT DEFAULT '', SustitutoId INTEGER DEFAULT 0, SustitutoNombre TEXT DEFAULT '', Asientos INTEGER DEFAULT 12, Notas TEXT DEFAULT '', FechaCreacion TEXT DEFAULT '' , Filas INTEGER DEFAULT 0, Columnas INTEGER DEFAULT 0, Pasillos TEXT DEFAULT '')
        """,

        """
        CREATE TABLE IF NOT EXISTS EscuelasHorario ( Id TEXT PRIMARY KEY, EscuelaId TEXT DEFAULT '', Dia TEXT DEFAULT '', OracionInicialId INTEGER DEFAULT 0, OracionInicialNombre TEXT DEFAULT '', OracionIntermediaId INTEGER DEFAULT 0, OracionIntermediaNombre TEXT DEFAULT '', OracionFinalId INTEGER DEFAULT 0, OracionFinalNombre TEXT DEFAULT '' )
        """,

        """
        CREATE TABLE IF NOT EXISTS EscuelasTalleres ( Id TEXT PRIMARY KEY, EscuelaId TEXT DEFAULT '', Taller INTEGER DEFAULT 1, Numero INTEGER DEFAULT 0, Minutos TEXT DEFAULT '', Tema TEXT DEFAULT '', AsignadoId INTEGER DEFAULT 0, AsignadoNombre TEXT DEFAULT '', AcompananteId INTEGER DEFAULT 0, AcompananteNombre TEXT DEFAULT '', HoraEnsayo TEXT DEFAULT '' )
        """,

        """
        CREATE TABLE IF NOT EXISTS HorasDia ( Usuario TEXT DEFAULT '', Fecha TEXT DEFAULT '', Horas REAL DEFAULT 0, CBs INTEGER DEFAULT 0, PRIMARY KEY (Usuario, Fecha) )
        """,

        """
        CREATE TABLE IF NOT EXISTS HorasMes ( Usuario TEXT DEFAULT '', Mes TEXT DEFAULT '', Horas REAL DEFAULT 0, CBs INTEGER DEFAULT 0, PRIMARY KEY (Usuario, Mes) )
        """,

        """
        CREATE TABLE IF NOT EXISTS MapeoS13S21 ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, NombreS13 TEXT, NombreS21 TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS NotasVisita ( Id INTEGER PRIMARY KEY AUTOINCREMENT, SemanaId INTEGER, Cabecera TEXT, Pregunta TEXT, Respuesta TEXT , EsAlerta INTEGER DEFAULT 0)
        """,

        """
        CREATE TABLE IF NOT EXISTS Personas ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT NOT NULL, FechaNacimiento TEXT, Sexo TEXT, FechaBautismo TEXT, Esperanza TEXT, Nombramiento TEXT, Servicio TEXT, CongregacionCodigo TEXT, Grupo INTEGER DEFAULT 0, Telefono TEXT, Email TEXT, FechaInicioPrec TEXT, FechaUltimaEscuela TEXT, NombreJWHub TEXT, Foto TEXT , FechaCreacion TEXT, FechaEliminacion TEXT, Solicitudes TEXT, Privilegios TEXT, RequisitoHoras INTEGER DEFAULT 0, NombreComun TEXT, Direccion TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, ConyugeId INTEGER DEFAULT 0, EmailJW TEXT, PersonaRaizId INTEGER DEFAULT 0, MesHistorico TEXT, FechaExactaHistorico TEXT, Notas TEXT, OrigenRegistro TEXT DEFAULT 'DESCONOCIDO', AprobadoExhibidores INTEGER DEFAULT 0, TrasladoAnteriorId INTEGER DEFAULT 0, TrasladoSiguienteId INTEGER DEFAULT 0, FechaTraslado TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS ProgramaAsambleas ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Anio TEXT, Tipo TEXT, Seccion TEXT, Titulo TEXT, TextoTematico TEXT , Lugar TEXT, Fecha TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS ProgramaAsambleasOrganigrama ( Id INTEGER PRIMARY KEY AUTOINCREMENT, AsambleaId INTEGER, Departamento TEXT, AsignadoId INTEGER , Rol TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS ProgramaAsambleasPartes ( Id INTEGER PRIMARY KEY AUTOINCREMENT, AsambleaId INTEGER, Turno TEXT, Hora TEXT, Titulo TEXT, AsignadoId INTEGER, Participantes TEXT , Tipo TEXT, RutaArchivo TEXT, Enviado INTEGER DEFAULT 0)
        """,

        """
        CREATE TABLE IF NOT EXISTS ProgramaVisita ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongreCodigo TEXT NOT NULL, MesReferencia TEXT NOT NULL, DiaSemana TEXT NOT NULL, Turno TEXT NOT NULL, Responsable TEXT NOT NULL, TipoActividad TEXT, Hora TEXT, NotasLibro TEXT, CompaneroId INTEGER, UNIQUE(CongreCodigo, MesReferencia, DiaSemana, Turno, Responsable) )
        """,

        """
        CREATE TABLE IF NOT EXISTS ProgramaVisitaV2 ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongreCodigo TEXT NOT NULL, MesReferencia TEXT NOT NULL, DiaSemana TEXT NOT NULL, Turno TEXT NOT NULL, Responsable TEXT NOT NULL, TipoActividad TEXT, Hora TEXT, NotasLibro TEXT, CompaneroId INTEGER , Ubicacion TEXT, Latitud REAL DEFAULT 0, Longitud REAL DEFAULT 0, Direccion TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS RSC_Asistentes ( Id TEXT PRIMARY KEY, RscReunionId TEXT DEFAULT '', PersonaId INTEGER DEFAULT 0, Nombre TEXT DEFAULT '', Asiste INTEGER DEFAULT 0, SaleAPredicar INTEGER DEFAULT 0, AceptaGrupo INTEGER DEFAULT 1, ArregloConPersonaId INTEGER DEFAULT 0, Notas TEXT DEFAULT '' , Sexo TEXT DEFAULT '', ArregloConPersonaIds TEXT DEFAULT '')
        """,

        """
        CREATE TABLE IF NOT EXISTS RSC_Grupos ( Id TEXT PRIMARY KEY, RscReunionId TEXT DEFAULT '', Numero INTEGER DEFAULT 0, Notas TEXT DEFAULT '', Territorio TEXT DEFAULT '', Orden INTEGER DEFAULT 0 )
        """,

        """
        CREATE TABLE IF NOT EXISTS RSC_Miembros ( Id TEXT PRIMARY KEY, RscGrupoId TEXT DEFAULT '', RscReunionId TEXT DEFAULT '', PersonaId INTEGER DEFAULT 0, Nombre TEXT DEFAULT '', Orden INTEGER DEFAULT 0 )
        """,

        """
        CREATE TABLE IF NOT EXISTS RSC_Reuniones ( Id TEXT PRIMARY KEY, CongregacionCodigo TEXT DEFAULT '', Fecha TEXT DEFAULT '', Turno TEXT DEFAULT '', Notas TEXT DEFAULT '', FechaCreacion TEXT DEFAULT '' )
        """,

        """
        CREATE TABLE IF NOT EXISTS RecomendacionesNombramientos ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Hermano TEXT, Tipo TEXT, Congregacion TEXT, Fecha TEXT, FechaDescarga TEXT , UrlDetalle TEXT, RecomendadoComo TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS RegistroActividad ( Id TEXT PRIMARY KEY, FechaHora TEXT NOT NULL, Categoria TEXT DEFAULT '', Dispositivo TEXT DEFAULT '', Perfil TEXT DEFAULT '', Descripcion TEXT NOT NULL )
        """,

        """
        CREATE TABLE IF NOT EXISTS ReunionPrecursores ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Anio TEXT, Titulo TEXT, TextoTematico TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS ReunionPrecursoresPartes ( Id INTEGER PRIMARY KEY AUTOINCREMENT, ReunionId INTEGER, Hora TEXT, Titulo TEXT, AsignadoId INTEGER, Participantes TEXT, RutaArchivo TEXT , SesionId INTEGER, Tipo TEXT, Enviado INTEGER DEFAULT 0)
        """,

        """
        CREATE TABLE IF NOT EXISTS ReunionPrecursoresSesiones ( Id INTEGER PRIMARY KEY AUTOINCREMENT, ReunionId INTEGER, Fecha TEXT, Hora TEXT, Congregaciones TEXT , Numero INTEGER, Lugar TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS SeccionesCircuito ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT, LugarAsamblea TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS Semanas ( Id INTEGER PRIMARY KEY AUTOINCREMENT, FechaMartes DATETIME, Actividad TEXT, Congregacion TEXT, Tipo TEXT, Acompanante TEXT, Alojamiento TEXT, S302Enviado INTEGER DEFAULT 0, DocEnviada INTEGER DEFAULT 0 , SeccionAsamblea TEXT, NotificacionEnviada INTEGER DEFAULT 0, RecordatorioEnviado INTEGER DEFAULT 0, FechaAsamblea TEXT, DatosImportados INTEGER DEFAULT 0, TipoAsamblea TEXT, RepBetelNombre TEXT, RepBetelTelefono TEXT, RepBetelEmail TEXT, RepBetelMensajeEnviado INTEGER DEFAULT 0)
        """,

        """
        CREATE TABLE IF NOT EXISTS Tareas ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Titulo TEXT DEFAULT '', Descripcion TEXT DEFAULT '', FechaInicio TEXT DEFAULT '', FechaFin TEXT DEFAULT '', TodoElDia INTEGER DEFAULT 1, Hora TEXT DEFAULT '', AvisoMinutos INTEGER DEFAULT -1, Repeticion TEXT DEFAULT 'Ninguna', Compartida INTEGER DEFAULT 0, Completada INTEGER DEFAULT 0, CreadaPor TEXT DEFAULT '', FechaCreacion TEXT DEFAULT '', UltimaModificacion TEXT DEFAULT '' , Categoria TEXT DEFAULT 'Personal')
        """,

        """
        CREATE TABLE IF NOT EXISTS TerritoriosVisita ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, Numero INTEGER, Poblacion TEXT, UltimaFechaCompletado TEXT, AsignadoA TEXT, FechaAsignado TEXT, FechaCompletado TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS TerritoriosVisitaV2 ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, Nombre TEXT, UltimaFechaCompletado TEXT, AsignadoA TEXT, FechaAsignado TEXT, FechaCompletado TEXT )
        """,

        """
        CREATE TABLE IF NOT EXISTS TerritoriosVisitaV3 ( Id INTEGER PRIMARY KEY AUTOINCREMENT, CongregacionCodigo TEXT, Nombre TEXT, UltimaFechaCompletado TEXT, AsignadoA TEXT, FechaAsignado TEXT, FechaCompletado TEXT, NombreVinculado TEXT , FechaCreacion TEXT, FechaEliminacion TEXT)
        """,

        """
        CREATE TABLE IF NOT EXISTS VariablesCorrespondencia ( Id INTEGER PRIMARY KEY AUTOINCREMENT, Categoria TEXT NOT NULL, Etiqueta TEXT NOT NULL, Token TEXT NOT NULL, ClaveSistema TEXT, Orden INTEGER DEFAULT 0 )
        """,
    };
}
