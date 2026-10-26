using System.Globalization;

using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// PERSISTENCIA DE IMPORTACIÓN S-21 / S-13
// ============================================================================
//
// Requiere que DatabaseHelper.cs siga siendo:
//
//     public static partial class DatabaseHelper
//
// La importación JSON:
// - NO escribe NombreJWHub;
// - solo ajusta NombreComun cuando corresponde a la regla de identidad;
// - guarda una instantánea mensual de la ficha ya resuelta;
// - actualiza ActividadPersonas;
// - conserva NombreVinculado y FechaCreacion de territorios ya conocidos;
// - marca la semana como DatosImportados.
// ============================================================================

public static partial class DatabaseHelper
{
    // ========================================================================
    // FINALIZAR IMPORTACIÓN - UNA SOLA TRANSACCIÓN
    // ========================================================================
    //
    // Hasta llegar aquí, la revisión solo ha modificado objetos en memoria.
    // De este modo, cancelar la importación NO deja cambios parciales.
    //
    // Al finalizar, en la misma transacción:
    // - se insertan los nuevos aceptados;
    // - se actualizan los campos S-21 elegidos;
    // - se aplican las bajas confirmadas;
    // - se crea/sustituye la instantánea histórica del mes;
    // - se guarda la actividad;
    // - se actualizan territorios;
    // - se marca la semana como importada.
    // ========================================================================

    public static void FinalizarImportacionJson(
        ResultadoImportacionJsonMobile resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using SqliteTransaction tx =
            connection.BeginTransaction();

        try
        {
            // ---------------------------------------------------------------
            // BAJAS CONFIRMADAS
            // ---------------------------------------------------------------

            foreach (JsonImportItemMobile item
                     in resultado.TodosItems
                         .Where(x => x.ConfirmarBaja))
            {
                if (item.PersonaDB is null ||
                    item.PersonaDB.Id <= 0)
                {
                    continue;
                }

                using SqliteCommand cmdBaja =
                    connection.CreateCommand();

                cmdBaja.Transaction =
                    tx;

                cmdBaja.CommandText =
                    """
                    UPDATE Personas
                    SET FechaEliminacion = $fecha
                    WHERE Id = $id
                      AND MesHistorico IS NULL;
                    """;

                cmdBaja.Parameters.AddWithValue(
                    "$fecha",
                    DateTime.Today.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture));

                cmdBaja.Parameters.AddWithValue(
                    "$id",
                    item.PersonaDB.Id);

                cmdBaja.ExecuteNonQuery();
            }


            // ---------------------------------------------------------------
            // PERSONAS + HISTÓRICO + ACTIVIDAD
            // ---------------------------------------------------------------

            foreach (JsonImportItemMobile item
                     in resultado.TodosItems)
            {
                if (item.IgnorarImportacion ||
                    item.PersonaDB is null ||
                    item.PersonaJson is null)
                {
                    continue;
                }

                Persona persona =
                    item.PersonaDB;

                int personaId;

                if (item.CrearPersonaNueva ||
                    persona.Id <= 0)
                {
                    personaId =
                        InsertarPersonaJsonTx(
                            connection,
                            tx,
                            persona,
                            resultado.CongregacionCodigo,
                            resultado.MesReferencia);

                    persona.Id =
                        personaId;

                    item.CrearPersonaNueva =
                        false;
                }
                else
                {
                    personaId =
                        persona.Id;

                    // Un conflicto sin resolver conserva la ficha actual.
                    // Aun así podremos importar su actividad mensual.
                    if (item.Tipo !=
                        TipoAnalisisJsonMobile.Conflicto)
                    {
                        ActualizarPersonaJsonTx(
                            connection,
                            tx,
                            persona);
                    }
                }

                AsegurarNombreComunSinJw(
                    connection,
                    tx,
                    personaId);

                GuardarInstantaneaMensual(
                    connection,
                    tx,
                    personaId,
                    resultado.MesReferencia);

                // La misma persona puede traer varios años de servicio
                // (una tarjeta por año): se guarda la actividad de CADA
                // año, no solo la de la tarjeta que se revisó.
                string nombreItem =
                    (item.PersonaJson.Nombre ?? "").Trim();

                var tarjetasPersona =
                    resultado.PersonasJson
                        .Where(j => string.Equals(
                            (j.Nombre ?? "").Trim(),
                            nombreItem,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (tarjetasPersona.Count == 0)
                {
                    tarjetasPersona.Add(item.PersonaJson);
                }

                foreach (PersonaJsonMobile tarjeta
                         in tarjetasPersona)
                {
                    GuardarActividadJson(
                        connection,
                        tx,
                        personaId,
                        resultado.MesReferencia,
                        tarjeta);
                }
            }


            // ---------------------------------------------------------------
            // TERRITORIOS
            // ---------------------------------------------------------------

            if (resultado.Territorios is not null)
            {
                GuardarTerritoriosJson(
                    connection,
                    tx,
                    resultado.CongregacionCodigo,
                    resultado.Territorios);
            }


            // ---------------------------------------------------------------
            // SEMANA IMPORTADA
            // ---------------------------------------------------------------

            if (resultado.SemanaId > 0)
            {
                using SqliteCommand cmdSemana =
                    connection.CreateCommand();

                cmdSemana.Transaction =
                    tx;

                cmdSemana.CommandText =
                    "UPDATE Semanas " +
                    "SET DatosImportados = 1 " +
                    "WHERE Id = $id;";

                cmdSemana.Parameters.AddWithValue(
                    "$id",
                    resultado.SemanaId);

                cmdSemana.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }


    // ========================================================================
    // INSERTAR / ACTUALIZAR PERSONA DENTRO DE LA TRANSACCIÓN
    // ========================================================================

    private static int InsertarPersonaJsonTx(
        SqliteConnection connection,
        SqliteTransaction tx,
        Persona persona,
        string congregacionCodigo,
        string mesReferencia)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            tx;

        cmd.CommandText =
            """
            INSERT INTO Personas
            (
                Nombre,
                CongregacionCodigo,
                Grupo,
                Nombramiento,
                Servicio,
                FechaNacimiento,
                Sexo,
                FechaBautismo,
                Esperanza,
                NombreJWHub,
                NombreComun,
                FechaCreacion
            )
            VALUES
            (
                $nombre,
                $codigo,
                $grupo,
                $nombramiento,
                $servicio,
                $fechaNacimiento,
                $sexo,
                $fechaBautismo,
                $esperanza,
                '',
                $nombreComun,
                $fechaCreacion
            );
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            persona.Nombre ?? "");

        cmd.Parameters.AddWithValue(
            "$codigo",
            congregacionCodigo ?? "");

        cmd.Parameters.AddWithValue(
            "$grupo",
            persona.Grupo);

        cmd.Parameters.AddWithValue(
            "$nombramiento",
            persona.Nombramiento ?? "");

        cmd.Parameters.AddWithValue(
            "$servicio",
            persona.Servicio ?? "");

        cmd.Parameters.AddWithValue(
            "$fechaNacimiento",
            persona.FechaNacimiento ?? "");

        cmd.Parameters.AddWithValue(
            "$sexo",
            persona.Sexo ?? "");

        cmd.Parameters.AddWithValue(
            "$fechaBautismo",
            persona.FechaBautismo ?? "");

        cmd.Parameters.AddWithValue(
            "$esperanza",
            persona.Esperanza ?? "");

        string nombreComun =
            string.IsNullOrWhiteSpace(
                persona.NombreComun)
                ? persona.Nombre ?? ""
                : persona.NombreComun;

        cmd.Parameters.AddWithValue(
            "$nombreComun",
            nombreComun);

        DateTime fechaCreacion =
            DateTime.Today;

        if (DateTime.TryParseExact(
                mesReferencia + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime inicioMesReferencia))
        {
            fechaCreacion =
                inicioMesReferencia;
        }

        // Para una persona que nace en SCCosmos a partir de un S-21
        // histórico, sabemos con certeza que ya estaba presente en
        // ese mes. No usamos la fecha física de importación.
        cmd.Parameters.AddWithValue(
            "$fechaCreacion",
            fechaCreacion.ToString(
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture));

        object? id =
            cmd.ExecuteScalar();

        if (id is null ||
            id == DBNull.Value)
        {
            throw new InvalidOperationException(
                "No se pudo obtener el Id de la nueva persona.");
        }

        return Convert.ToInt32(
            id,
            CultureInfo.InvariantCulture);
    }


    private static void ActualizarPersonaJsonTx(
        SqliteConnection connection,
        SqliteTransaction tx,
        Persona persona)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            tx;

        // Solo campos controlados por S-21 + NombreComun ya resuelto.
        // No se toca NombreJWHub, foto, teléfonos, email, dirección,
        // privilegios, solicitudes, cónyuge ni otros datos JW/locales.
        cmd.CommandText =
            """
            UPDATE Personas
            SET
                Nombre = $nombre,
                FechaNacimiento = $fechaNacimiento,
                Sexo = $sexo,
                FechaBautismo = $fechaBautismo,
                Esperanza = $esperanza,
                Nombramiento = $nombramiento,
                Servicio = $servicio,
                Grupo = $grupo,
                NombreComun = $nombreComun
            WHERE Id = $id
              AND MesHistorico IS NULL;
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            persona.Nombre ?? "");

        cmd.Parameters.AddWithValue(
            "$fechaNacimiento",
            persona.FechaNacimiento ?? "");

        cmd.Parameters.AddWithValue(
            "$sexo",
            persona.Sexo ?? "");

        cmd.Parameters.AddWithValue(
            "$fechaBautismo",
            persona.FechaBautismo ?? "");

        cmd.Parameters.AddWithValue(
            "$esperanza",
            persona.Esperanza ?? "");

        cmd.Parameters.AddWithValue(
            "$nombramiento",
            persona.Nombramiento ?? "");

        cmd.Parameters.AddWithValue(
            "$servicio",
            persona.Servicio ?? "");

        cmd.Parameters.AddWithValue(
            "$grupo",
            persona.Grupo);

        cmd.Parameters.AddWithValue(
            "$nombreComun",
            persona.NombreComun ?? "");

        cmd.Parameters.AddWithValue(
            "$id",
            persona.Id);

        if (cmd.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException(
                "No se pudo actualizar la ficha raíz de " +
                (persona.Nombre ?? "la persona") +
                ".");
        }
    }


    // ========================================================================
    // INSTANTÁNEA DEL MES
    // ========================================================================

    private static void GuardarInstantaneaMensual(
        SqliteConnection connection,
        SqliteTransaction tx,
        int personaRaizId,
        string mesReferencia)
    {
        // Si ya se importó ese mismo mes, sustituimos solamente esa
        // instantánea. La actividad mensual está asociada a la persona raíz,
        // no al clon histórico.
        using (SqliteCommand cmdDelete = connection.CreateCommand())
        {
            cmdDelete.Transaction = tx;
            cmdDelete.CommandText =
                "DELETE FROM Personas " +
                "WHERE PersonaRaizId = $raiz AND MesHistorico = $mes;";
            cmdDelete.Parameters.AddWithValue("$raiz", personaRaizId);
            cmdDelete.Parameters.AddWithValue("$mes", mesReferencia);
            cmdDelete.ExecuteNonQuery();
        }

        string fechaExacta = DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);

        using SqliteCommand cmdInsert = connection.CreateCommand();
        cmdInsert.Transaction = tx;
        cmdInsert.CommandText =
            """
            INSERT INTO Personas
            (
                Nombre,
                FechaNacimiento,
                Sexo,
                FechaBautismo,
                Esperanza,
                Nombramiento,
                Servicio,
                CongregacionCodigo,
                Grupo,
                Telefono,
                Email,
                FechaInicioPrec,
                FechaUltimaEscuela,
                NombreJWHub,
                Foto,
                FechaCreacion,
                FechaEliminacion,
                Solicitudes,
                Privilegios,
                RequisitoHoras,
                NombreComun,
                Direccion,
                Latitud,
                Longitud,
                ConyugeId,
                EmailJW,
                PersonaRaizId,
                MesHistorico,
                FechaExactaHistorico,
                Notas
            )
            SELECT
                Nombre,
                FechaNacimiento,
                Sexo,
                FechaBautismo,
                Esperanza,
                Nombramiento,
                Servicio,
                CongregacionCodigo,
                Grupo,
                Telefono,
                Email,
                FechaInicioPrec,
                FechaUltimaEscuela,
                NombreJWHub,
                Foto,
                FechaCreacion,
                FechaEliminacion,
                Solicitudes,
                Privilegios,
                RequisitoHoras,
                NombreComun,
                Direccion,
                Latitud,
                Longitud,
                ConyugeId,
                EmailJW,
                $raiz,
                $mes,
                $fecha,
                Notas
            FROM Personas
            WHERE Id = $raiz;
            """;

        cmdInsert.Parameters.AddWithValue("$raiz", personaRaizId);
        cmdInsert.Parameters.AddWithValue("$mes", mesReferencia);
        cmdInsert.Parameters.AddWithValue("$fecha", fechaExacta);
        cmdInsert.ExecuteNonQuery();
    }


    // ========================================================================
    // ACTIVIDAD S-21
    // ========================================================================

    private static void GuardarActividadJson(
        SqliteConnection connection,
        SqliteTransaction tx,
        int personaId,
        string mesReferencia,
        PersonaJsonMobile persona)
    {
        if (persona.Informe is null)
            return;

        foreach (InformeJsonMobile informe in persona.Informe)
        {
            string mes = (informe.Mes ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(mes))
                continue;

            string anioServicio = (persona.AnioServicio ?? "").Trim();
            if (string.IsNullOrWhiteSpace(anioServicio))
                anioServicio = CalcularAnioServicioJson(mesReferencia);

            int cursos = ParseEnteroFlexibleJson(informe.CursosTexto);
            int horas = ParseEnteroFlexibleJson(informe.HorasTexto);

            using SqliteCommand cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO ActividadPersonas
                (
                    PersonaId,
                    Mes,
                    AnioServicio,
                    Participacion,
                    CursosBiblicos,
                    PrecursorAuxiliar,
                    Horas,
                    Notas
                )
                VALUES
                (
                    $persona,
                    $mes,
                    $anio,
                    $participacion,
                    $cursos,
                    $precursor,
                    $horas,
                    $notas
                )
                ON CONFLICT(PersonaId, Mes, AnioServicio)
                DO UPDATE SET
                    Participacion = excluded.Participacion,
                    CursosBiblicos = excluded.CursosBiblicos,
                    PrecursorAuxiliar = excluded.PrecursorAuxiliar,
                    Horas = excluded.Horas,
                    Notas = excluded.Notas;
                """;

            cmd.Parameters.AddWithValue("$persona", personaId);
            cmd.Parameters.AddWithValue("$mes", mes);
            cmd.Parameters.AddWithValue("$anio", anioServicio);
            cmd.Parameters.AddWithValue("$participacion", informe.ParticipacionBool ? 1 : 0);
            cmd.Parameters.AddWithValue("$cursos", cursos);
            cmd.Parameters.AddWithValue("$precursor", informe.PrecursorAuxiliarBool ? 1 : 0);
            cmd.Parameters.AddWithValue("$horas", horas);
            cmd.Parameters.AddWithValue("$notas", informe.Notas ?? "");
            cmd.ExecuteNonQuery();
        }
    }


    // ========================================================================
    // TERRITORIOS S-13
    // ========================================================================

    private static void GuardarTerritoriosJson(
        SqliteConnection connection,
        SqliteTransaction tx,
        string congregacionCodigo,
        List<TerritorioJsonMobile> territorios)
    {
        var existentes = new Dictionary<
            string,
            (string Vinculo, string FechaCreacion)>(
                StringComparer.OrdinalIgnoreCase);

        using (SqliteCommand cmdExistentes = connection.CreateCommand())
        {
            cmdExistentes.Transaction = tx;
            cmdExistentes.CommandText =
                """
                SELECT Nombre, NombreVinculado, FechaCreacion
                FROM TerritoriosVisitaV3
                WHERE CongregacionCodigo = $codigo;
                """;
            cmdExistentes.Parameters.AddWithValue("$codigo", congregacionCodigo);

            using SqliteDataReader reader = cmdExistentes.ExecuteReader();
            while (reader.Read())
            {
                string nombre = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                existentes[nombre] =
                (
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2)
                );
            }
        }

        using (SqliteCommand cmdDelete = connection.CreateCommand())
        {
            cmdDelete.Transaction = tx;
            cmdDelete.CommandText =
                "DELETE FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $codigo;";
            cmdDelete.Parameters.AddWithValue("$codigo", congregacionCodigo);
            cmdDelete.ExecuteNonQuery();
        }

        foreach (TerritorioJsonMobile territorio in territorios)
        {
            string nombre = territorio.Nombre ?? "";
            existentes.TryGetValue(nombre, out var anterior);

            string fechaCreacion = string.IsNullOrWhiteSpace(anterior.FechaCreacion)
                ? DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : anterior.FechaCreacion;

            using SqliteCommand cmdInsert = connection.CreateCommand();
            cmdInsert.Transaction = tx;
            cmdInsert.CommandText =
                """
                INSERT INTO TerritoriosVisitaV3
                (
                    CongregacionCodigo,
                    Nombre,
                    UltimaFechaCompletado,
                    AsignadoA,
                    FechaAsignado,
                    FechaCompletado,
                    NombreVinculado,
                    FechaCreacion,
                    FechaEliminacion
                )
                VALUES
                (
                    $codigo,
                    $nombre,
                    $ultima,
                    $asignado,
                    $fechaAsig,
                    $fechaComp,
                    $vinculo,
                    $creacion,
                    ''
                );
                """;

            cmdInsert.Parameters.AddWithValue("$codigo", congregacionCodigo);
            cmdInsert.Parameters.AddWithValue("$nombre", nombre);
            cmdInsert.Parameters.AddWithValue("$ultima", territorio.UltimaFechaCompletado ?? "");
            cmdInsert.Parameters.AddWithValue("$asignado", territorio.AsignadoA ?? "");
            cmdInsert.Parameters.AddWithValue("$fechaAsig", territorio.FechaAsignado ?? "");
            cmdInsert.Parameters.AddWithValue("$fechaComp", territorio.FechaCompletado ?? "");
            cmdInsert.Parameters.AddWithValue("$vinculo", anterior.Vinculo ?? "");
            cmdInsert.Parameters.AddWithValue("$creacion", fechaCreacion);
            cmdInsert.ExecuteNonQuery();
        }
    }


    /// <summary>
    /// Importa los territorios de un S-13 directamente (por ejemplo desde
    /// la página de la visita). Sustituye los de esa congregación y
    /// conserva la fecha de creación y el vínculo de los ya conocidos.
    /// </summary>
    public static void GuardarTerritoriosImportadosMobile(
        string congregacionCodigo,
        List<TerritorioJsonMobile> territorios)
    {
        if (string.IsNullOrWhiteSpace(congregacionCodigo)
            || territorios is null
            || territorios.Count == 0)
        {
            return;
        }

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using SqliteTransaction tx =
            connection.BeginTransaction();

        GuardarTerritoriosJson(
            connection,
            tx,
            congregacionCodigo,
            territorios);

        tx.Commit();

        AnotarActividadMobile(
            "Visitas",
            "Importó " + territorios.Count +
            " territorios de " + congregacionCodigo);
    }


    // ========================================================================
    // HELPERS
    // ========================================================================

    private static void AsegurarNombreComunSinJw(
        SqliteConnection connection,
        SqliteTransaction tx,
        int personaId)
    {
        // Regla de identidad:
        // si la persona no tiene cuenta JW y todavía no tiene nombre común,
        // el nombre común debe ser el nombre S-21.
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE Personas
            SET NombreComun = Nombre
            WHERE Id = $id
              AND (NombreJWHub IS NULL OR TRIM(NombreJWHub) = '')
              AND (NombreComun IS NULL OR TRIM(NombreComun) = '');
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            personaId);

        cmd.ExecuteNonQuery();
    }


    private static int ParseEnteroFlexibleJson(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return 0;

        string limpio = valor.Trim().Replace(",", ".", StringComparison.Ordinal);
        if (double.TryParse(
                limpio,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double numero))
        {
            return (int)Math.Round(numero);
        }

        return 0;
    }

    private static string CalcularAnioServicioJson(string mesReferencia)
    {
        if (DateTime.TryParseExact(
                mesReferencia + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime fecha))
        {
            return fecha.Month >= 9
                ? (fecha.Year + 1).ToString(CultureInfo.InvariantCulture)
                : fecha.Year.ToString(CultureInfo.InvariantCulture);
        }

        return "";
    }
}
