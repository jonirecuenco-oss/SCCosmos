using System.Globalization;

using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// LECTURA DE VISITA PARA ANDROID
// ============================================================================
//
// Requiere que DatabaseHelper.cs esté declarado:
//
//     public static partial class DatabaseHelper
//
// Esta parte NO modifica datos de la visita. Solo lee:
// - congregación,
// - publicadores,
// - actividad mensual,
// - programa,
// - notas,
// - territorios,
// - contabilidad.
// ============================================================================

public static partial class DatabaseHelper
{
    public static VistaVisitaMobile
        ObtenerVistaVisitaMobile(
            string congregacionNombre,
            DateTime fechaVisita)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        string mesReferencia =
            fechaVisita.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);

        VistaVisitaMobile vista =
            new()
            {
                CongregacionNombre =
                    congregacionNombre,

                MesReferencia =
                    mesReferencia
            };

        CargarCongregacionVistaMobile(
            connection,
            vista);

        if (string.IsNullOrWhiteSpace(
                vista.CongregacionCodigo))
        {
            return vista;
        }

        vista.Personas =
            CargarPersonasVistaMobile(
                connection,
                vista.CongregacionCodigo,
                mesReferencia);

        // Los cuadros terminan en el último mes con actividad real (como
        // en Windows). Así la pestaña Ministerio puede reutilizar esta
        // misma carga en vez de repetirla.
        DateTime fechaActividad =
            fechaVisita;

        try
        {
            DateTime? maxActividad =
                ObtenerMaximoMesActividadMobile(
                    vista.CongregacionCodigo);

            DateTime mesVisita =
                new(
                    fechaVisita.Year,
                    fechaVisita.Month,
                    1);

            if (maxActividad.HasValue
                && maxActividad.Value < mesVisita)
            {
                fechaActividad =
                    maxActividad.Value;
            }
        }
        catch
        {
        }

        CargarActividadSeisMesesMobile(
            connection,
            vista.Personas,
            fechaActividad);

        // Mismo cálculo de requisito / meta prorrateada que Windows.
        CalcularHorasPrecursoresMobile(
            connection,
            vista.Personas,
            fechaVisita);

        vista.Programa =
            CargarProgramaVistaMobile(
                connection,
                vista.CongregacionCodigo,
                mesReferencia);

        int semanaId =
            ObtenerIdSemanaVisitaMobile(
                connection,
                congregacionNombre,
                mesReferencia);

        vista.SemanaId =
            semanaId;

        if (semanaId > 0)
        {
            vista.Notas =
                CargarNotasVistaMobile(
                    connection,
                    semanaId);
        }

        vista.Territorios =
            CargarTerritoriosVistaMobile(
                connection,
                vista.CongregacionCodigo);

        vista.Contabilidad =
            CargarContabilidadVistaMobile(
                connection,
                vista.CongregacionCodigo,
                fechaVisita);

        CalcularResumenVistaMobile(
            vista);

        return vista;
    }


    // ========================================================================
    // MINISTERIO POR MES
    // ========================================================================

    public static List<VistaPersonaVisitaMobile>
        ObtenerPersonasMinisterioVisitaMobile(
            string codigo,
            DateTime mesReferencia,
            DateTime? mesFinCuadros = null)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        string mes =
            mesReferencia.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);

        List<VistaPersonaVisitaMobile> personas =
            CargarPersonasVistaMobile(
                connection,
                codigo,
                mes);

        // Los cuadros de los 6 meses terminan en el último mes del que
        // hay registro de actividad (igual que en Windows), y nunca
        // después del mes seleccionado.
        DateTime finCuadros =
            mesFinCuadros.HasValue
            && mesFinCuadros.Value < mesReferencia
                ? mesFinCuadros.Value
                : mesReferencia;

        CargarActividadSeisMesesMobile(
            connection,
            personas,
            finCuadros);

        CalcularHorasPrecursoresMobile(
            connection,
            personas,
            mesReferencia);

        return personas;
    }


    /// <summary>
    /// Último mes del que hay registro de actividad real (participación u
    /// horas) para la congregación indicada. Es la misma regla que usa
    /// Windows para terminar los cuadros de los 6 meses.
    /// </summary>
    public static DateTime? ObtenerMaximoMesActividadMobile(
        string codigo)
    {
        string[] nombresMeses =
        {
            "enero", "febrero", "marzo", "abril", "mayo", "junio",
            "julio", "agosto", "septiembre", "octubre", "noviembre",
            "diciembre"
        };

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        // Un mes solo cuenta como «con datos» si informó una parte
        // significativa de los publicadores (10 %, mínimo 2), igual que
        // en Windows. Así el mes en curso, con una o dos fichas
        // sueltas, no aparece como columna vacía.
        int activos = 0;

        using (SqliteCommand cmdActivos =
               connection.CreateCommand())
        {
            cmdActivos.CommandText =
                """
                SELECT COUNT(*)
                FROM Personas
                WHERE CongregacionCodigo = $codigo
                  AND MesHistorico IS NULL
                  AND Grupo > 0
                  AND (FechaEliminacion IS NULL OR FechaEliminacion = '');
                """;

            cmdActivos.Parameters.AddWithValue(
                "$codigo",
                codigo);

            activos =
                Convert.ToInt32(
                    cmdActivos.ExecuteScalar());
        }

        int minimo =
            Math.Max(
                2,
                (int)Math.Ceiling(
                    activos * 0.10));

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                lower(COALESCE(a.Mes, '')),
                COALESCE(a.AnioServicio, ''),
                COUNT(DISTINCT a.PersonaId)
            FROM ActividadPersonas a
            JOIN Personas p
              ON p.Id = a.PersonaId
            WHERE p.CongregacionCodigo = $codigo
              AND p.MesHistorico IS NULL
              AND (
                    a.Participacion = 1
                    OR CAST(
                           REPLACE(
                               REPLACE(
                                   TRIM(COALESCE(a.Horas, '0')),
                                   ' ', ''),
                               ',', '.')
                           AS REAL) > 0)
            GROUP BY
                lower(COALESCE(a.Mes, '')),
                COALESCE(a.AnioServicio, '');
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        var conteo =
            new Dictionary<DateTime, int>();

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string mesNombre =
                LeerTexto(
                    reader,
                    0)
                    .Trim()
                    .ToLowerInvariant();

            string anioServicio =
                LeerTexto(
                    reader,
                    1)
                    .Trim();

            int informaron =
                reader.IsDBNull(2)
                    ? 0
                    : reader.GetInt32(2);

            int indiceMes =
                Array.IndexOf(
                    nombresMeses,
                    mesNombre);

            if (indiceMes < 0
                || !int.TryParse(
                    anioServicio,
                    out int anio))
            {
                continue;
            }

            int year =
                indiceMes >= 8
                    ? anio - 1
                    : anio;

            conteo[new DateTime(
                year,
                indiceMes + 1,
                1)] = informaron;
        }

        if (conteo.Count == 0)
        {
            return null;
        }

        var validos =
            conteo
                .Where(par => par.Value >= minimo)
                .Select(par => par.Key)
                .ToList();

        // Si ningún mes llega al umbral (congregaciones pequeñas o
        // datos incompletos), se usa el último con cualquier dato.
        DateTime maximo =
            validos.Count > 0
                ? validos.Max()
                : conteo.Keys.Max();

        return maximo;
    }


    /// <summary>
    /// Devuelve el Id de la ficha actual (sin MesHistorico) de la persona
    /// indicada, resolviéndolo también si se recibe una instantánea
    /// histórica, para poder editarla desde la visita.
    /// </summary>
    public static int ObtenerIdPersonaActualMobile(
        int personaId)
    {
        if (personaId <= 0)
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        int raiz =
            personaId;

        using (SqliteCommand cmdRaiz =
               connection.CreateCommand())
        {
            cmdRaiz.CommandText =
                """
                SELECT
                    CASE
                        WHEN COALESCE(PersonaRaizId, 0) > 0
                        THEN PersonaRaizId
                        ELSE Id
                    END
                FROM Personas
                WHERE Id = $id
                LIMIT 1;
                """;

            cmdRaiz.Parameters.AddWithValue(
                "$id",
                personaId);

            object? valor =
                cmdRaiz.ExecuteScalar();

            if (valor is not null
                && valor != DBNull.Value)
            {
                raiz =
                    Convert.ToInt32(
                        valor);
            }
        }

        using SqliteCommand cmdActual =
            connection.CreateCommand();

        cmdActual.CommandText =
            """
            SELECT Id
            FROM Personas
            WHERE
            (
                Id = $raiz
                OR COALESCE(PersonaRaizId, 0) = $raiz
            )
              AND
              (
                  MesHistorico IS NULL
                  OR trim(MesHistorico) = ''
              )
            ORDER BY Id DESC
            LIMIT 1;
            """;

        cmdActual.Parameters.AddWithValue(
            "$raiz",
            raiz);

        object? actual =
            cmdActual.ExecuteScalar();

        if (actual is not null
            && actual != DBNull.Value)
        {
            return Convert.ToInt32(
                actual);
        }

        return raiz;
    }


    // ========================================================================
    // CONGREGACIÓN
    // ========================================================================

    private static void
        CargarCongregacionVistaMobile(
            SqliteConnection connection,
            VistaVisitaMobile vista)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Codigo,
                Nombre,
                Email,
                DireccionSR,
                DiaEntreSemana,
                HoraEntreSemana,
                DiaFinSemana,
                HoraFinSemana,
                Municipio,
                Provincia,
                Latitud,
                Longitud
            FROM Congregaciones
            WHERE Nombre = $nombre
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            vista.CongregacionNombre);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        if (!reader.Read())
        {
            return;
        }

        vista.CongregacionCodigo =
            LeerTexto(
                reader,
                0);

        vista.CongregacionNombre =
            LeerTexto(
                reader,
                1);

        vista.EmailCongregacion =
            LeerTexto(
                reader,
                2);

        vista.DireccionSalon =
            LeerTexto(
                reader,
                3);

        vista.ReunionEntreSemana =
            UnirDiaHora(
                LeerTexto(
                    reader,
                    4),
                LeerTexto(
                    reader,
                    5));

        vista.ReunionFinSemana =
            UnirDiaHora(
                LeerTexto(
                    reader,
                    6),
                LeerTexto(
                    reader,
                    7));

        vista.Municipio =
            LeerTexto(
                reader,
                8);

        vista.Provincia =
            LeerTexto(
                reader,
                9);

        vista.LatitudSalon =
            reader.IsDBNull(10)
                ? 0
                : Convert.ToDouble(
                    reader.GetValue(10),
                    CultureInfo.InvariantCulture);

        vista.LongitudSalon =
            reader.IsDBNull(11)
                ? 0
                : Convert.ToDouble(
                    reader.GetValue(11),
                    CultureInfo.InvariantCulture);
    }


    // ========================================================================
    // PERSONAS EN EL MES DE REFERENCIA
    //
    // Para cada persona lógica:
    // 1. usa la instantánea exacta del mes si existe;
    // 2. si avanzamos a un mes posterior, conserva la última instantánea
    //    S-21 conocida anterior a ese mes;
    // 3. si nunca hubo instantánea, usa la ficha raíz actual.
    //
    // Esto evita que desaparezcan en meses posteriores las personas que
    // entraron exclusivamente por S-21 y todavía no tienen cuenta JW Hub.
    // ========================================================================

    private static List<VistaPersonaVisitaMobile>
        CargarPersonasVistaMobile(
            SqliteConnection connection,
            string codigo,
            string mesReferencia)
    {
        var resultado =
            new List<VistaPersonaVisitaMobile>();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            WITH candidatos AS
            (
                SELECT
                    x.Id,
                    CASE
                        WHEN x.PersonaRaizId > 0
                        THEN x.PersonaRaizId
                        ELSE x.Id
                    END AS RaizLogica,
                    x.MesHistorico
                FROM Personas x
                WHERE x.CongregacionCodigo = $codigo
                  AND
                  (
                      x.MesHistorico IS NULL
                      OR x.MesHistorico <= $mes
                  )
            ),
            elegidas AS
            (
                SELECT c.Id
                FROM candidatos c
                WHERE c.Id =
                (
                    SELECT c2.Id
                    FROM candidatos c2
                    WHERE c2.RaizLogica = c.RaizLogica
                    ORDER BY
                        CASE
                            WHEN c2.MesHistorico IS NULL
                            THEN 0
                            ELSE 1
                        END DESC,
                        c2.MesHistorico DESC,
                        c2.Id DESC
                    LIMIT 1
                )
            )
            SELECT
                p.Id,
                p.Nombre,
                p.Nombramiento,
                p.Servicio,
                p.Grupo,
                p.FechaNacimiento,
                p.Sexo,
                p.FechaBautismo,
                p.NombreJWHub,
                p.FechaCreacion,
                p.FechaEliminacion,
                p.NombreComun,
                p.Foto,
                p.PersonaRaizId,
                p.MesHistorico,
                p.RequisitoHoras,
                p.Direccion,
                p.Latitud,
                p.Longitud,
                (
                    SELECT MIN(h.MesHistorico)
                    FROM Personas h
                    WHERE h.PersonaRaizId =
                        CASE
                            WHEN p.PersonaRaizId > 0
                            THEN p.PersonaRaizId
                            ELSE p.Id
                        END
                      AND h.MesHistorico IS NOT NULL
                      AND trim(h.MesHistorico) <> ''
                ) AS PrimeraInstantaneaMes,
                (
                    SELECT r.FechaEliminacion
                    FROM Personas r
                    WHERE r.Id =
                        CASE
                            WHEN p.PersonaRaizId > 0
                            THEN p.PersonaRaizId
                            ELSE p.Id
                        END
                    LIMIT 1
                ) AS FechaEliminacionRaiz
            FROM Personas p
            WHERE p.Id IN
            (
                SELECT Id
                FROM elegidas
            )
            ORDER BY p.Nombre ASC;
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        cmd.Parameters.AddWithValue(
            "$mes",
            mesReferencia);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        DateTime inicioMes =
            DateTime.ParseExact(
                mesReferencia + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

        while (reader.Read())
        {
            string fechaCreacion =
                LeerTexto(
                    reader,
                    9);

            string fechaEliminacion =
                LeerTexto(
                    reader,
                    10);

            string fechaEliminacionRaiz =
                LeerTexto(
                    reader,
                    20);

            // Si estamos mostrando una instantánea histórica, la fecha de
            // baja que manda es la de la ficha raíz actual. Así evitamos
            // arrastrar a meses posteriores a alguien que ya fue eliminado.
            if (!string.IsNullOrWhiteSpace(
                    fechaEliminacionRaiz))
            {
                fechaEliminacion =
                    fechaEliminacionRaiz;
            }

            DateTime? creada =
                ParseFechaFlexible(
                    fechaCreacion);

            DateTime? eliminada =
                ParseFechaFlexible(
                    fechaEliminacion);

            string mesHistorico =
                LeerTexto(
                    reader,
                    14);

            bool esInstantaneaExacta =
                string.Equals(
                    mesHistorico,
                    mesReferencia,
                    StringComparison.Ordinal);

            // Si existe una instantánea exacta para este mes,
            // esa instantánea es la evidencia autoritativa de que
            // la persona estaba incluida en el S-21 de la visita.
            //
            // No debemos descartarla por FechaCreacion de la ficha
            // raíz. Esto afectaba especialmente a personas creadas
            // al importar un S-21 antiguo: su FechaCreacion era la
            // fecha de importación (hoy), posterior al mes visitado.
            //
            // Los filtros de alta/baja solo se aplican cuando estamos
            // usando la ficha actual como fallback por no existir
            // instantánea histórica del mes.
            if (!esInstantaneaExacta)
            {
                // Fecha efectiva de alta:
                //
                // Solo se descarta a una persona cuando su fecha de alta es
                // conocida y es posterior al mes que se está viendo. Si no
                // hay fecha de alta no se puede afirmar que no existiera:
                // antes se usaba la primera instantánea como si fuera el
                // alta y eso dejaba fuera a casi toda la congregación en
                // los meses anteriores (solo aparecía quien tenía una
                // instantánea antigua).
                if (creada.HasValue)
                {
                    DateTime inicioExistencia =
                        new DateTime(
                            creada.Value.Year,
                            creada.Value.Month,
                            1);

                    // Si la ficha raíz se creó al importar hoy un S-21 de
                    // un mes anterior, FechaCreacion puede ser posterior a
                    // la fecha real en que sabemos que la persona ya
                    // pertenecía a la congregación. En ese caso usamos la
                    // primera instantánea histórica conocida como límite
                    // inferior.
                    string primeraInstantaneaMes =
                        LeerTexto(
                            reader,
                            19);

                    if (DateTime.TryParseExact(
                            primeraInstantaneaMes,
                            "yyyy-MM",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateTime primeraInstantanea))
                    {
                        DateTime inicioHistorico =
                            new DateTime(
                                primeraInstantanea.Year,
                                primeraInstantanea.Month,
                                1);

                        if (inicioHistorico <
                            inicioExistencia)
                        {
                            inicioExistencia =
                                inicioHistorico;
                        }
                    }

                    if (inicioExistencia >
                        inicioMes)
                    {
                        continue;
                    }
                }

                if (eliminada.HasValue
                    &&
                    new DateTime(
                        eliminada.Value.Year,
                        eliminada.Value.Month,
                        1)
                    <
                    inicioMes)
                {
                    continue;
                }
            }

            int id =
                reader.GetInt32(0);

            int personaRaizId =
                reader.IsDBNull(13)
                    ? 0
                    : reader.GetInt32(13);

            var persona =
                new VistaPersonaVisitaMobile
                {
                    Id =
                        id,

                    PersonaActividadId =
                        personaRaizId > 0
                            ? personaRaizId
                            : id,

                    Nombre =
                        LeerTexto(
                            reader,
                            1),

                    Nombramiento =
                        LeerTexto(
                            reader,
                            2),

                    Servicio =
                        LeerTexto(
                            reader,
                            3),

                    Grupo =
                        reader.IsDBNull(4)
                            ? 0
                            : reader.GetInt32(4),

                    FechaNacimiento =
                        LeerTexto(
                            reader,
                            5),

                    Sexo =
                        LeerTexto(
                            reader,
                            6),

                    FechaBautismo =
                        LeerTexto(
                            reader,
                            7),

                    NombreJWHub =
                        LeerTexto(
                            reader,
                            8),

                    NombreComun =
                        LeerTexto(
                            reader,
                            11),

                    Foto =
                        LeerTexto(
                            reader,
                            12),

                    RequisitoHoras =
                        reader.IsDBNull(15)
                            ? 0
                            : Convert.ToInt32(
                                reader.GetValue(15)),

                    Direccion =
                        LeerTexto(
                            reader,
                            16),

                    Latitud =
                        reader.IsDBNull(17)
                            ? 0
                            : Convert.ToDouble(
                                reader.GetValue(17),
                                CultureInfo.InvariantCulture),

                    Longitud =
                        reader.IsDBNull(18)
                            ? 0
                            : Convert.ToDouble(
                                reader.GetValue(18),
                                CultureInfo.InvariantCulture)
                };

            persona.NombreVisual =
                !string.IsNullOrWhiteSpace(
                    persona.NombreComun)
                    ? persona.NombreComun
                    : persona.Nombre;

            persona.Edad =
                CalcularEdad(
                    ParseFechaFlexible(
                        persona.FechaNacimiento),
                    inicioMes);

            persona.AniosBautizado =
                CalcularEdad(
                    ParseFechaFlexible(
                        persona.FechaBautismo),
                    inicioMes);

            resultado.Add(
                persona);
        }

        return resultado;
    }


    // ========================================================================
    // ACTIVIDAD DE LOS ÚLTIMOS 6 MESES
    // ========================================================================

    private static void
        CargarActividadSeisMesesMobile(
            SqliteConnection connection,
            List<VistaPersonaVisitaMobile> personas,
            DateTime fechaVisita)
    {
        DateTime mesFin =
            new(
                fechaVisita.Year,
                fechaVisita.Month,
                1);

        // Si la visita es de un mes futuro (aún sin datos), se muestran
        // los 6 meses anteriores a hoy, que sí suelen tener actividad.
        DateTime mesActual =
            new(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1);

        if (mesFin > mesActual)
        {
            mesFin = mesActual;
        }

        var meses =
            new List<(DateTime Fecha, string Mes, string Anio)>();

        for (int i = 5;
             i >= 0;
             i--)
        {
            DateTime mes =
                mesFin.AddMonths(
                    -i);

            string nombreMes =
                NombreMesDb(
                    mes.Month);

            string anioServicio =
                mes.Month >= 9
                    ? (mes.Year + 1)
                        .ToString(
                            CultureInfo.InvariantCulture)
                    : mes.Year
                        .ToString(
                            CultureInfo.InvariantCulture);

            meses.Add(
                (
                    mes,
                    nombreMes,
                    anioServicio
                ));
        }

        // Inicializamos siempre las 6 celdas por persona.
        foreach (
            VistaPersonaVisitaMobile persona
            in personas)
        {
            persona.Actividad.Clear();

            foreach (
                var mes
                in meses)
            {
                persona.Actividad.Add(
                    new VistaActividadMesMobile
                    {
                        Mes =
                            mes.Fecha,

                        Etiqueta =
                            mes.Fecha.ToString(
                                "MMM yy",
                                CultureInfo.GetCultureInfo(
                                    "es-ES"))
                    });
            }
        }

        int[] ids =
            personas
                .Select(
                    p =>
                        p.PersonaActividadId)
                .Where(
                    id =>
                        id > 0)
                .Distinct()
                .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        // Antes se ejecutaban 6 consultas POR persona.
        // Ahora cargamos toda la actividad necesaria de una sola vez.
        using SqliteCommand cmd =
            connection.CreateCommand();

        var parametrosId =
            new List<string>();

        for (int i = 0;
             i < ids.Length;
             i++)
        {
            string nombre =
                "$p" + i;

            parametrosId.Add(
                nombre);

            cmd.Parameters.AddWithValue(
                nombre,
                ids[i]);
        }

        string[] anios =
            meses
                .Select(
                    m =>
                        m.Anio)
                .Distinct()
                .ToArray();

        var parametrosAnio =
            new List<string>();

        for (int i = 0;
             i < anios.Length;
             i++)
        {
            string nombre =
                "$a" + i;

            parametrosAnio.Add(
                nombre);

            cmd.Parameters.AddWithValue(
                nombre,
                anios[i]);
        }

        cmd.CommandText =
            $"""
            SELECT
                PersonaId,
                lower(Mes),
                AnioServicio,
                Participacion,
                CursosBiblicos,
                PrecursorAuxiliar,
                Horas,
                Notas
            FROM ActividadPersonas
            WHERE PersonaId IN ({string.Join(", ", parametrosId)})
              AND AnioServicio IN ({string.Join(", ", parametrosAnio)});
            """;

        // Historial completo (no solo estos 6 meses): una persona que
        // informó hace un año y lleva 6 meses sin hacerlo está inactiva.
        // Antes solo se miraban los 6 meses cargados y esas personas se
        // contaban como activas (134 en vez de 125).
        try
        {
            using SqliteCommand cmdHistorial =
                connection.CreateCommand();

            cmdHistorial.CommandText =
                $"""
                SELECT DISTINCT PersonaId
                FROM ActividadPersonas
                WHERE PersonaId IN ({string.Join(", ", parametrosId)});
                """;

            for (int i = 0;
                 i < ids.Length;
                 i++)
            {
                cmdHistorial.Parameters.AddWithValue(
                    "$p" + i,
                    ids[i]);
            }

            var conHistorial =
                new HashSet<int>();

            using SqliteDataReader lectorHistorial =
                cmdHistorial.ExecuteReader();

            while (lectorHistorial.Read())
            {
                conHistorial.Add(
                    lectorHistorial.GetInt32(0));
            }

            foreach (VistaPersonaVisitaMobile persona in
                     personas)
            {
                persona.TieneHistorial =
                    conHistorial.Contains(
                        persona.PersonaActividadId);
            }
        }
        catch
        {
        }

        var porPersona =
            personas
                .GroupBy(
                    p =>
                        p.PersonaActividadId)
                .ToDictionary(
                    g =>
                        g.Key,
                    g =>
                        g.ToList());

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            int personaId =
                reader.GetInt32(0);

            string mesNombre =
                LeerTexto(
                    reader,
                    1)
                    .Trim()
                    .ToLowerInvariant();

            string anioServicio =
                LeerTexto(
                    reader,
                    2)
                    .Trim();

            int indiceMes =
                meses.FindIndex(
                    x =>
                        x.Mes ==
                        mesNombre
                        &&
                        x.Anio ==
                        anioServicio);

            if (indiceMes < 0)
            {
                continue;
            }

            if (!porPersona.TryGetValue(
                    personaId,
                    out List<VistaPersonaVisitaMobile>? personasDestino))
            {
                continue;
            }

            foreach (
                VistaPersonaVisitaMobile persona
                in personasDestino)
            {
                VistaActividadMesMobile act =
                    persona.Actividad[
                        indiceMes];

                act.ExisteRegistro =
                    true;

                act.Participacion =
                    LeerBool(
                        reader,
                        3);

                act.Cursos =
                    LeerTexto(
                        reader,
                        4);

                act.PrecursorAuxiliar =
                    LeerBool(
                        reader,
                        5);

                act.Horas =
                    LeerTexto(
                        reader,
                        6);

                act.Notas =
                    LeerTexto(
                        reader,
                        7);
            }
        }

        // Se marca quién no ha informado nada en estos 6 meses.
        foreach (VistaPersonaVisitaMobile persona
                 in personas)
        {
            bool tieneHistorial =
                persona.TieneHistorial;

            bool reporto =
                persona.Actividad.Any(a =>
                    a.ExisteRegistro
                    && (a.Participacion
                        || TieneHorasActividad(a.Horas)));

            persona.EsInactivo =
                tieneHistorial && !reporto;
        }
    }


    private static bool TieneHorasActividad(string? horas)
    {
        string limpio =
            (horas ?? "")
                .Replace(" ", "")
                .Replace(",", ".")
                .Trim();

        return limpio.Length > 0
            && limpio != "0"
            && limpio != "0.0"
            && double.TryParse(
                limpio,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double valor)
            && valor > 0;
    }


    // ========================================================================
    // HORAS DE PRECURSOR - MISMA REGLA DE WINDOWS
    // ========================================================================

    private static void
        CalcularHorasPrecursoresMobile(
            SqliteConnection connection,
            List<VistaPersonaVisitaMobile> personas,
            DateTime fechaVisita)
    {
        DateTime mesReferencia =
            new(
                fechaVisita.Year,
                fechaVisita.Month,
                1);

        string anioServicio =
            (mesReferencia.Month >= 9
                ? mesReferencia.Year + 1
                : mesReferencia.Year)
            .ToString(
                CultureInfo.InvariantCulture);

        int mesesTranscurridos =
            mesReferencia.Month >= 9
                ? mesReferencia.Month - 8
                : mesReferencia.Month + 4;

        List<VistaPersonaVisitaMobile> precursores =
            personas
                .Where(p =>
                    Contiene(p.Servicio, "Precursor")
                    ||
                    Contiene(p.Servicio, "Misionero"))
                .ToList();

        if (precursores.Count == 0)
        {
            return;
        }

        Dictionary<int, double> horasPorPersona =
            precursores
                .Select(p => p.PersonaActividadId)
                .Where(id => id > 0)
                .Distinct()
                .ToDictionary(id => id, _ => 0d);

        if (horasPorPersona.Count > 0)
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            var parametros =
                new List<string>();

            int i = 0;
            foreach (int id in horasPorPersona.Keys)
            {
                string p = "$hp" + i++;
                parametros.Add(p);
                cmd.Parameters.AddWithValue(p, id);
            }

            cmd.Parameters.AddWithValue(
                "$anioServicio",
                anioServicio);

            cmd.CommandText =
                $"""
                SELECT
                    PersonaId,
                    lower(Mes),
                    Horas
                FROM ActividadPersonas
                WHERE PersonaId IN ({string.Join(", ", parametros)})
                  AND AnioServicio = $anioServicio;
                """;

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                int id = reader.GetInt32(0);

                if (!horasPorPersona.ContainsKey(id))
                {
                    continue;
                }

                string nombreMes =
                    LeerTexto(reader, 1)
                        .Trim()
                        .ToLowerInvariant();

                int numeroMes =
                    NumeroMesDbMobile(nombreMes);

                if (numeroMes <= 0)
                {
                    continue;
                }

                int ordenMes =
                    numeroMes >= 9
                        ? numeroMes - 8
                        : numeroMes + 4;

                if (ordenMes > mesesTranscurridos)
                {
                    continue;
                }

                string horasTexto =
                    LeerTexto(reader, 2)
                        .Replace(" ", "")
                        .Trim();

                if (double.TryParse(
                        horasTexto.Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double horas))
                {
                    horasPorPersona[id] += horas;
                }
            }
        }

        foreach (VistaPersonaVisitaMobile persona in precursores)
        {
            int requisitoAnual =
                persona.RequisitoHoras;

            if (requisitoAnual == 0)
            {
                string servicio =
                    persona.Servicio
                        .Trim()
                        .ToLowerInvariant();

                if (servicio.Contains("precursor regular"))
                {
                    requisitoAnual = 600;
                }
                else if (servicio.Contains("especial")
                         ||
                         servicio.Contains("misionero"))
                {
                    requisitoAnual = 1200;

                    if (string.Equals(
                            persona.Sexo,
                            "Mujer",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime? nacimiento =
                            ParseFechaFlexible(
                                persona.FechaNacimiento);

                        if (nacimiento.HasValue)
                        {
                            int edad =
                                mesReferencia.Year
                                -
                                nacimiento.Value.Year;

                            if (nacimiento.Value.Date
                                >
                                mesReferencia.AddYears(-edad))
                            {
                                edad--;
                            }

                            if (edad >= 40)
                            {
                                requisitoAnual = 1080;
                            }
                        }
                    }
                }
            }

            persona.RequisitoHorasCalculado =
                requisitoAnual;

            if (!horasPorPersona.TryGetValue(
                    persona.PersonaActividadId,
                    out double horasAcumuladas))
            {
                horasAcumuladas = 0;
            }

            persona.HorasServicioAcumuladas =
                Math.Round(horasAcumuladas, 1);

            if (requisitoAnual <= 0)
            {
                continue;
            }

            double meta =
                (requisitoAnual / 12d)
                *
                mesesTranscurridos;

            persona.MetaHorasProrrateada =
                Math.Round(meta, 1);

            if (horasAcumuladas < meta)
            {
                persona.TieneAlertaHoras = true;
                persona.DeficitHoras =
                    Math.Round(
                        meta - horasAcumuladas,
                        1);
            }
        }

        CalcularAcumuladosAnioServicioMobile(
            connection,
            precursores);
    }


    /// <summary>
    /// Rellena, en cada uno de los 6 meses mostrados, las horas
    /// acumuladas del año de servicio hasta ese mes (los precursores
    /// regulares tienen 50 h/mes; especiales y misioneros, su
    /// requisito anual entre 12). En rojo si va por debajo del ritmo.
    /// </summary>
    private static void CalcularAcumuladosAnioServicioMobile(
        SqliteConnection connection,
        List<VistaPersonaVisitaMobile> precursores)
    {
        var meses =
            precursores
                .FirstOrDefault()
                ?.Actividad
                .Select(a => a.Mes)
                .Where(m => m != default)
                .ToList()
            ?? new List<DateTime>();

        if (meses.Count == 0)
        {
            return;
        }

        var anios =
            meses
                .Select(AnioServicioDeMesMobile)
                .Distinct()
                .ToList();

        var ids =
            precursores
                .Select(p => p.PersonaActividadId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

        // (persona, año de servicio) -> horas por orden de mes.
        var horas =
            new Dictionary<(int, string), Dictionary<int, double>>();

        if (ids.Count > 0 && anios.Count > 0)
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            var parametrosId =
                new List<string>();

            for (int i = 0; i < ids.Count; i++)
            {
                string nombre = "$ac" + i;
                parametrosId.Add(nombre);
                cmd.Parameters.AddWithValue(nombre, ids[i]);
            }

            var parametrosAnio =
                new List<string>();

            for (int i = 0; i < anios.Count; i++)
            {
                string nombre = "$ay" + i;
                parametrosAnio.Add(nombre);
                cmd.Parameters.AddWithValue(nombre, anios[i]);
            }

            cmd.CommandText =
                $"""
                SELECT
                    PersonaId,
                    lower(COALESCE(Mes, '')),
                    AnioServicio,
                    COALESCE(Horas, '')
                FROM ActividadPersonas
                WHERE PersonaId IN ({string.Join(", ", parametrosId)})
                  AND AnioServicio IN ({string.Join(", ", parametrosAnio)});
                """;

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                int personaId = reader.GetInt32(0);

                string mesNombre =
                    LeerTexto(reader, 1)
                        .Trim()
                        .ToLowerInvariant();

                string anio =
                    LeerTexto(reader, 2)
                        .Trim();

                int numeroMes = NumeroMesDbMobile(mesNombre);

                if (numeroMes <= 0 || anio.Length == 0)
                {
                    continue;
                }

                string horasTexto =
                    LeerTexto(reader, 3)
                        .Replace(" ", "")
                        .Trim();

                if (!double.TryParse(
                        horasTexto.Replace(",", "."),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double valorHoras)
                    || valorHoras <= 0)
                {
                    continue;
                }

                int orden =
                    numeroMes >= 9
                        ? numeroMes - 8
                        : numeroMes + 4;

                var clave = (personaId, anio);

                if (!horas.TryGetValue(
                        clave,
                        out Dictionary<int, double>? porMes))
                {
                    porMes = new Dictionary<int, double>();
                    horas[clave] = porMes;
                }

                porMes[orden] =
                    porMes.TryGetValue(orden, out double acumuladoMes)
                        ? acumuladoMes + valorHoras
                        : valorHoras;
            }
        }

        foreach (VistaPersonaVisitaMobile persona in precursores)
        {
            int requisito = persona.RequisitoHorasCalculado;

            if (requisito <= 0)
            {
                continue;
            }

            string anioPersona =
                "";

            for (int i = 0; i < persona.Actividad.Count; i++)
            {
                VistaActividadMesMobile mes =
                    persona.Actividad[i];

                if (mes.Mes == default)
                {
                    continue;
                }

                string anio =
                    AnioServicioDeMesMobile(mes.Mes);

                if (anio != anioPersona)
                {
                    anioPersona = anio;
                }

                int orden =
                    mes.Mes.Month >= 9
                        ? mes.Mes.Month - 8
                        : mes.Mes.Month + 4;

                double acumulado = 0;

                if (horas.TryGetValue(
                        (persona.PersonaActividadId, anio),
                        out Dictionary<int, double>? porMes))
                {
                    foreach (KeyValuePair<int, double> par in porMes)
                    {
                        if (par.Key <= orden)
                        {
                            acumulado += par.Value;
                        }
                    }
                }

                mes.AcumuladoTexto =
                    Math.Round(acumulado, 1)
                        .ToString(
                            "0.#",
                            CultureInfo.CurrentCulture);

                mes.AcumuladoEnRojo =
                    acumulado < (requisito / 12d) * orden;
            }
        }
    }


    private static string AnioServicioDeMesMobile(DateTime mes)
    {
        return (mes.Month >= 9
                ? mes.Year + 1
                : mes.Year)
            .ToString(CultureInfo.InvariantCulture);
    }


    private static int
        NumeroMesDbMobile(
            string nombreMes)
    {
        return nombreMes switch
        {
            "enero" => 1,
            "febrero" => 2,
            "marzo" => 3,
            "abril" => 4,
            "mayo" => 5,
            "junio" => 6,
            "julio" => 7,
            "agosto" => 8,
            "septiembre" => 9,
            "octubre" => 10,
            "noviembre" => 11,
            "diciembre" => 12,
            _ => 0
        };
    }


    // ========================================================================
    // PROGRAMA
    // ========================================================================

    private static List<VistaProgramaVisitaMobile>
        CargarProgramaVistaMobile(
            SqliteConnection connection,
            string codigo,
            string mesReferencia)
    {
        var resultado =
            new List<VistaProgramaVisitaMobile>();

        AsegurarColumnasProgramaVisitaUbicacionMobile(
            connection);

        using SqliteCommand cmd =
            connection.CreateCommand();

        // JOIN directo con Personas:
        // evita una consulta adicional por cada actividad.
        cmd.CommandText =
            """
            SELECT
                pv.Id,
                pv.DiaSemana,
                pv.Turno,
                pv.Responsable,
                pv.TipoActividad,
                pv.Hora,
                pv.NotasLibro,
                pv.CompaneroId,
                COALESCE(NULLIF(pv.Direccion, ''), pv.Ubicacion, ''),
                COALESCE(pv.Latitud, 0),
                COALESCE(pv.Longitud, 0),
                CASE
                    WHEN p.NombreComun IS NOT NULL
                         AND TRIM(p.NombreComun) <> ''
                    THEN p.NombreComun
                    ELSE COALESCE(p.Nombre, '')
                END AS Companero
            FROM ProgramaVisitaV2 pv
            LEFT JOIN Personas p
                ON p.Id = pv.CompaneroId
            WHERE pv.CongreCodigo = $codigo
              AND pv.MesReferencia = $mes
            ORDER BY
                CASE pv.DiaSemana
                    WHEN 'Miércoles' THEN 1
                    WHEN 'Jueves' THEN 2
                    WHEN 'Viernes' THEN 3
                    WHEN 'Sábado' THEN 4
                    WHEN 'Domingo' THEN 5
                    ELSE 6
                END,
                pv.Turno,
                pv.Hora;
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        cmd.Parameters.AddWithValue(
            "$mes",
            mesReferencia);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new VistaProgramaVisitaMobile
                {
                    Id =
                        reader.GetInt32(0),

                    Dia =
                        LeerTexto(
                            reader,
                            1),

                    Turno =
                        LeerTexto(
                            reader,
                            2),

                    Responsable =
                        LeerTexto(
                            reader,
                            3),

                    TipoActividad =
                        LeerTexto(
                            reader,
                            4),

                    Hora =
                        LeerTexto(
                            reader,
                            5),

                    Notas =
                        LeerTexto(
                            reader,
                            6),

                    CompaneroId =
                        reader.IsDBNull(7)
                            ? 0
                            : reader.GetInt32(7),

                    Ubicacion =
                        LeerTexto(
                            reader,
                            8),

                    Latitud =
                        reader.IsDBNull(9)
                            ? 0
                            : Convert.ToDouble(
                                reader.GetValue(9),
                                CultureInfo.InvariantCulture),

                    Longitud =
                        reader.IsDBNull(10)
                            ? 0
                            : Convert.ToDouble(
                                reader.GetValue(10),
                                CultureInfo.InvariantCulture),

                    Companero =
                        LeerTexto(
                            reader,
                            11)
                });
        }

        return resultado;
    }


    // ========================================================================
    // NOTAS
    // ========================================================================

    private static int
        ObtenerIdSemanaVisitaMobile(
            SqliteConnection connection,
            string congregacionNombre,
            string mesReferencia)
    {
        if (!ExisteTabla(
                connection,
                "Semanas"))
        {
            return 0;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Id
            FROM Semanas
            WHERE Congregacion = $nombre
              AND Actividad = 'Visita'
              AND FechaMartes LIKE $mes || '%'
            ORDER BY FechaMartes ASC, Id ASC
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            congregacionNombre);

        cmd.Parameters.AddWithValue(
            "$mes",
            mesReferencia);

        object? valor =
            cmd.ExecuteScalar();

        return valor is null
               ||
               valor == DBNull.Value
            ? 0
            : Convert.ToInt32(
                valor);
    }


    private static List<VistaNotaVisitaMobile>
        CargarNotasVistaMobile(
            SqliteConnection connection,
            int semanaId)
    {
        var resultado =
            new List<VistaNotaVisitaMobile>();

        if (!ExisteTabla(
                connection,
                "NotasVisita"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                SemanaId,
                Cabecera,
                Pregunta,
                Respuesta,
                EsAlerta
            FROM NotasVisita
            WHERE SemanaId = $semana
            ORDER BY Id ASC;
            """;

        cmd.Parameters.AddWithValue(
            "$semana",
            semanaId);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new VistaNotaVisitaMobile
                {
                    Id =
                        reader.GetInt32(0),

                    SemanaId =
                        reader.GetInt32(1),

                    Cabecera =
                        LeerTexto(
                            reader,
                            2),

                    Pregunta =
                        LeerTexto(
                            reader,
                            3),

                    Respuesta =
                        LeerTexto(
                            reader,
                            4),

                    EsAlerta =
                        LeerBool(
                            reader,
                            5)
                });
        }

        return resultado;
    }


    // ========================================================================
    // TERRITORIOS
    // ========================================================================

    private static List<VistaTerritorioMobile>
        CargarTerritoriosVistaMobile(
            SqliteConnection connection,
            string codigo)
    {
        var resultado =
            new List<VistaTerritorioMobile>();

        if (!ExisteTabla(
                connection,
                "TerritoriosVisitaV3"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Nombre,
                UltimaFechaCompletado,
                AsignadoA,
                FechaAsignado,
                FechaCompletado,
                NombreVinculado,
                FechaCreacion,
                FechaEliminacion
            FROM TerritoriosVisitaV3
            WHERE CongregacionCodigo = $codigo
            ORDER BY Nombre ASC;
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string fechaEliminacion =
                LeerTexto(
                    reader,
                    8);

            if (!string.IsNullOrWhiteSpace(
                    fechaEliminacion))
            {
                continue;
            }

            resultado.Add(
                new VistaTerritorioMobile
                {
                    Id =
                        reader.IsDBNull(0)
                            ? 0
                            : reader.GetInt32(0),

                    Nombre =
                        LeerTexto(
                            reader,
                            1),

                    UltimaFechaCompletado =
                        LeerTexto(
                            reader,
                            2),

                    AsignadoA =
                        LeerTexto(
                            reader,
                            3),

                    FechaAsignado =
                        LeerTexto(
                            reader,
                            4),

                    FechaCompletado =
                        LeerTexto(
                            reader,
                            5),

                    NombreVinculado =
                        LeerTexto(
                            reader,
                            6)
                });
        }

        return resultado;
    }


    // ========================================================================
    // CONTABILIDAD - 6 MESES
    // ========================================================================

    private static List<VistaContabilidadMobile>
        CargarContabilidadVistaMobile(
            SqliteConnection connection,
            string codigo,
            DateTime fechaVisita)
    {
        var resultado =
            new List<VistaContabilidadMobile>();

        if (!ExisteTabla(
                connection,
                "ContabilidadMes"))
        {
            return resultado;
        }

        DateTime mesBase =
            new(
                fechaVisita.Year,
                fechaVisita.Month,
                1);

        var meses =
            new List<(DateTime Fecha, string Ref)>();

        for (int i = 5;
             i >= 0;
             i--)
        {
            DateTime mes =
                mesBase.AddMonths(
                    -i);

            meses.Add(
                (
                    mes,
                    mes.ToString(
                        "yyyy-MM",
                        CultureInfo.InvariantCulture)
                ));

            resultado.Add(
                new VistaContabilidadMobile
                {
                    Mes =
                        mes,

                    EtiquetaMes =
                        mes.ToString(
                            "MMMM yyyy",
                            CultureInfo.GetCultureInfo(
                                "es-ES"))
                });
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        var parametros =
            new List<string>();

        for (int i = 0;
             i < meses.Count;
             i++)
        {
            string nombre =
                "$m" + i;

            parametros.Add(
                nombre);

            cmd.Parameters.AddWithValue(
                nombre,
                meses[i].Ref);
        }

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo);

        cmd.CommandText =
            $"""
            SELECT
                MesReferencia,
                Gastos,
                Mantenimiento,
                ObraMundial,
                ObraResolucion,
                Saldo
            FROM ContabilidadMes
            WHERE CongreCodigo = $codigo
              AND MesReferencia IN ({string.Join(", ", parametros)});
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string mesRef =
                LeerTexto(
                    reader,
                    0);

            int indice =
                meses.FindIndex(
                    x =>
                        x.Ref ==
                        mesRef);

            if (indice < 0)
            {
                continue;
            }

            VistaContabilidadMobile fila =
                resultado[indice];

            fila.Gastos =
                LeerDouble(
                    reader,
                    1);

            fila.Mantenimiento =
                LeerDouble(
                    reader,
                    2);

            fila.ObraMundial =
                LeerDouble(
                    reader,
                    3);

            fila.ObraResolucion =
                LeerDouble(
                    reader,
                    4);

            fila.Saldo =
                LeerDouble(
                    reader,
                    5);

            fila.TieneDatos =
                true;
        }

        return resultado;
    }


    // ========================================================================
    // RESUMEN
    // ========================================================================

    private static void
        CalcularResumenVistaMobile(
            VistaVisitaMobile vista)
    {
        List<VistaPersonaVisitaMobile>
            conGrupo =
                vista.Personas
                    .Where(
                        p =>
                            p.Grupo > 0
                            && !p.EsInactivo)
                    .ToList();

        vista.TotalPersonas =
            conGrupo.Count;

        vista.TotalHombres =
            conGrupo.Count(
                p =>
                    string.Equals(
                        p.Sexo,
                        "Hombre",
                        StringComparison
                            .OrdinalIgnoreCase));

        vista.TotalMujeres =
            conGrupo.Count(
                p =>
                    string.Equals(
                        p.Sexo,
                        "Mujer",
                        StringComparison
                            .OrdinalIgnoreCase));

        vista.Ancianos =
            conGrupo.Count(
                p =>
                    Contiene(
                        p.Nombramiento,
                        "Anciano"));

        vista.SiervosMinisteriales =
            conGrupo.Count(
                p =>
                    Contiene(
                        p.Nombramiento,
                        "Siervo"));

        vista.PrecursoresRegulares =
            conGrupo.Count(
                p =>
                    Contiene(
                        p.Servicio,
                        "Precursor regular"));

        vista.PrecursoresEspeciales =
            conGrupo.Count(
                p =>
                    Contiene(
                        p.Servicio,
                        "Precursor especial")
                    ||
                    Contiene(
                        p.Servicio,
                        "Misionero"));

        vista.NoBautizados =
            conGrupo.Count(
                p =>
                    string.IsNullOrWhiteSpace(
                        p.FechaBautismo));

        List<double> edades =
            conGrupo
                .Where(
                    p =>
                        p.Edad > 0
                        &&
                        p.Edad <= 120)
                .Select(
                    p =>
                        p.Edad)
                .ToList();

        vista.MediaEdad =
            edades.Count == 0
                ? 0
                : Math.Round(
                    edades.Average(),
                    1);

        vista.Grupos =
            conGrupo
                .GroupBy(
                    p =>
                        p.Grupo)
                .OrderBy(
                    g =>
                        g.Key)
                .Select(
                    g =>
                        new VistaGrupoMobile
                        {
                            Grupo =
                                g.Key,

                            Publicadores =
                                g.Count(),

                            Precursores =
                                g.Count(
                                    p =>
                                        Contiene(
                                            p.Servicio,
                                            "Precursor")
                                        ||
                                        Contiene(
                                            p.Servicio,
                                            "Misionero")),

                            Ancianos =
                                g.Count(
                                    p =>
                                        Contiene(
                                            p.Nombramiento,
                                            "Anciano")),

                            Siervos =
                                g.Count(
                                    p =>
                                        Contiene(
                                            p.Nombramiento,
                                            "Siervo"))
                        })
                .ToList();
    }


    // ========================================================================
    // HELPERS
    // ========================================================================

    private static bool
        ExisteTabla(
            SqliteConnection connection,
            string tabla)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tabla;
            """;

        cmd.Parameters.AddWithValue(
            "$tabla",
            tabla);

        return Convert.ToInt32(
                   cmd.ExecuteScalar())
               > 0;
    }


    private static string
        UnirDiaHora(
            string dia,
            string hora)
    {
        if (string.IsNullOrWhiteSpace(
                dia))
        {
            return hora;
        }

        if (string.IsNullOrWhiteSpace(
                hora))
        {
            return dia;
        }

        return dia +
               " · " +
               hora;
    }


    private static string
        LeerTexto(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return "";
        }

        return Convert.ToString(
                   reader.GetValue(
                       ordinal),
                   CultureInfo
                       .InvariantCulture)
               ?? "";
    }


    private static bool
        LeerBool(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return false;
        }

        object valor =
            reader.GetValue(
                ordinal);

        if (valor is long l)
        {
            return l != 0;
        }

        if (valor is int i)
        {
            return i != 0;
        }

        if (valor is bool b)
        {
            return b;
        }

        return int.TryParse(
                   valor.ToString(),
                   out int n)
               &&
               n != 0;
    }


    private static double
        LeerDouble(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return 0;
        }

        return Convert.ToDouble(
            reader.GetValue(
                ordinal),
            CultureInfo.InvariantCulture);
    }


    private static DateTime?
        ParseFechaFlexible(
            string? texto)
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return null;
        }

        string[] formatos =
        [
            "dd/MM/yyyy",
            "d/M/yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss"
        ];

        foreach (
            string formato
            in formatos)
        {
            if (DateTime.TryParseExact(
                    texto.Trim(),
                    formato,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime fecha))
            {
                return fecha;
            }
        }

        if (DateTime.TryParse(
                texto,
                CultureInfo.GetCultureInfo(
                    "es-ES"),
                DateTimeStyles.None,
                out DateTime otra))
        {
            return otra;
        }

        return null;
    }


    private static double
        CalcularEdad(
            DateTime? nacimiento,
            DateTime referencia)
    {
        if (!nacimiento.HasValue
            ||
            nacimiento.Value.Year <
            1900)
        {
            return 0;
        }

        return Math.Round(
            (referencia -
             nacimiento.Value)
            .TotalDays
            /
            365.2425,
            1);
    }


    private static string
        NombreMesDb(
            int mes)
    {
        string[] meses =
        [
            "enero",
            "febrero",
            "marzo",
            "abril",
            "mayo",
            "junio",
            "julio",
            "agosto",
            "septiembre",
            "octubre",
            "noviembre",
            "diciembre"
        ];

        return meses[
            mes - 1];
    }


    private static bool
        Contiene(
            string? texto,
            string valor)
    {
        return
            !string.IsNullOrWhiteSpace(
                texto)
            &&
            texto.Contains(
                valor,
                StringComparison
                    .OrdinalIgnoreCase);
    }
}


// ============================================================================
// DTOs DE SOLO LECTURA
// ============================================================================

public sealed class VistaVisitaMobile
{
    public string CongregacionCodigo
    { get; set; } = "";

    public string CongregacionNombre
    { get; set; } = "";

    public string MesReferencia
    { get; set; } = "";

    public int SemanaId
    { get; set; }

    public string Municipio
    { get; set; } = "";

    public string Provincia
    { get; set; } = "";

    public string EmailCongregacion
    { get; set; } = "";

    public string DireccionSalon
    { get; set; } = "";

    public double LatitudSalon
    { get; set; }

    public double LongitudSalon
    { get; set; }

    public string ReunionEntreSemana
    { get; set; } = "";

    public string ReunionFinSemana
    { get; set; } = "";

    public int TotalPersonas
    { get; set; }

    public int TotalHombres
    { get; set; }

    public int TotalMujeres
    { get; set; }

    public int Ancianos
    { get; set; }

    public int SiervosMinisteriales
    { get; set; }

    public int PrecursoresRegulares
    { get; set; }

    public int PrecursoresEspeciales
    { get; set; }

    public int NoBautizados
    { get; set; }

    public double MediaEdad
    { get; set; }

    public List<VistaPersonaVisitaMobile>
        Personas
    { get; set; } = new();

    public List<VistaProgramaVisitaMobile>
        Programa
    { get; set; } = new();

    public List<VistaNotaVisitaMobile>
        Notas
    { get; set; } = new();

    public List<VistaTerritorioMobile>
        Territorios
    { get; set; } = new();

    public List<VistaContabilidadMobile>
        Contabilidad
    { get; set; } = new();

    public List<VistaGrupoMobile>
        Grupos
    { get; set; } = new();
}


public sealed class VistaPersonaVisitaMobile
{
    public int Id
    { get; set; }

    public int PersonaActividadId
    { get; set; }

    public string Nombre
    { get; set; } = "";

    public string NombreVisual
    { get; set; } = "";

    public string NombreComun
    { get; set; } = "";

    public string NombreJWHub
    { get; set; } = "";

    public string Nombramiento
    { get; set; } = "";

    public string Servicio
    { get; set; } = "";

    public int Grupo
    { get; set; }

    public string FechaNacimiento
    { get; set; } = "";

    public string FechaBautismo
    { get; set; } = "";

    public string Sexo
    { get; set; } = "";

    public string Foto
    { get; set; } = "";

    public string Direccion
    { get; set; } = "";

    public double Latitud
    { get; set; }

    public double Longitud
    { get; set; }

    public int RequisitoHoras
    { get; set; }

    public int RequisitoHorasCalculado
    { get; set; }

    public double HorasServicioAcumuladas
    { get; set; }

    public double MetaHorasProrrateada
    { get; set; }

    public double DeficitHoras
    { get; set; }

    public bool TieneAlertaHoras
    { get; set; }

    public double Edad
    { get; set; }

    public double AniosBautizado
    { get; set; }

    public List<VistaActividadMesMobile>
        Actividad
    { get; set; } = new();

    /// <summary>
    /// Sin actividad informada en los últimos 6 meses (con historial
    /// previo). No cuenta en estadísticas ni en el número de publicadores.
    /// </summary>
    public bool EsInactivo { get; set; }

    /// <summary>
    /// Tiene algún informe de actividad en cualquier mes (no solo en
    /// los 6 que se muestran). Sin historial no se puede saber si está
    /// inactivo y se deja como activo.
    /// </summary>
    public bool TieneHistorial { get; set; }
}


public sealed class VistaActividadMesMobile
{
    public bool ExisteRegistro
    { get; set; }

    public DateTime Mes
    { get; set; }

    public string Etiqueta
    { get; set; } = "";

    public bool Participacion
    { get; set; }

    public string Cursos
    { get; set; } = "";

    public bool PrecursorAuxiliar
    { get; set; }

    public string Horas
    { get; set; } = "";

    public string Notas
    { get; set; } = "";

    /// <summary>
    /// Horas acumuladas en el año de servicio hasta este mes (solo
    /// precursores con requisito anual). Vacío si no aplica.
    /// </summary>
    public string AcumuladoTexto
    { get; set; } = "";

    public bool AcumuladoEnRojo
    { get; set; }
}


public sealed class VistaProgramaVisitaMobile
{
    public int Id
    { get; set; }

    public int CompaneroId
    { get; set; }

    public string Dia
    { get; set; } = "";

    public string Turno
    { get; set; } = "";

    public string Responsable
    { get; set; } = "";

    public string TipoActividad
    { get; set; } = "";

    public string Hora
    { get; set; } = "";

    public string Notas
    { get; set; } = "";

    public string Ubicacion
    { get; set; } = "";

    public double Latitud
    { get; set; }

    public double Longitud
    { get; set; }

    public string Companero
    { get; set; } = "";
}


public sealed class VistaNotaVisitaMobile
{
    public int Id
    { get; set; }

    public int SemanaId
    { get; set; }

    public string Cabecera
    { get; set; } = "";

    public string Pregunta
    { get; set; } = "";

    public string Respuesta
    { get; set; } = "";

    public bool EsAlerta
    { get; set; }
}


public sealed class VistaTerritorioMobile
{
    public int Id
    { get; set; }

    public string Nombre
    { get; set; } = "";

    public string UltimaFechaCompletado
    { get; set; } = "";

    public string AsignadoA
    { get; set; } = "";

    public string FechaAsignado
    { get; set; } = "";

    public string FechaCompletado
    { get; set; } = "";

    public string NombreVinculado
    { get; set; } = "";
}


public sealed class VistaContabilidadMobile
{
    public DateTime Mes
    { get; set; }

    public string EtiquetaMes
    { get; set; } = "";

    public double Gastos
    { get; set; }

    public double Mantenimiento
    { get; set; }

    public double ObraMundial
    { get; set; }

    public double ObraResolucion
    { get; set; }

    public double Saldo
    { get; set; }

    public bool TieneDatos
    { get; set; }
}


public sealed class VistaGrupoMobile
{
    public int Grupo
    { get; set; }

    public int Publicadores
    { get; set; }

    public int Precursores
    { get; set; }

    public int Ancianos
    { get; set; }

    public int Siervos
    { get; set; }
}
