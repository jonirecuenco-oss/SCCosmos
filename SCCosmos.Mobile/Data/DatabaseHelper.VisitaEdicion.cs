using System.Globalization;
using System.IO;

using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// EDICIÓN DE LA VISITA EN ANDROID
// ============================================================================

public static partial class DatabaseHelper
{
    // ========================================================================
    // PROGRAMA
    // ========================================================================

    public static List<VistaProgramaVisitaMobile>
        ObtenerProgramaVisitaEditableMobile(
            string codigo,
            string mesReferencia)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        return CargarProgramaVistaMobile(
            connection,
            codigo,
            mesReferencia);
    }


    public static void GuardarActividadProgramaVisitaMobile(
        string codigo,
        string mesReferencia,
        VistaProgramaVisitaMobile actividad)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        AsegurarColumnasProgramaVisitaUbicacionMobile(
            connection);

        AnotarActividadMobile(
            "Visitas",
            (actividad.Id > 0 ? "Editó" : "Añadió") +
            " una actividad al programa de la visita");

        bool responsableUnico =
            actividad.Responsable is
                "Comida" or "Cenar" or "Dormir";

        if (responsableUnico)
        {
            using SqliteCommand comprobar =
                connection.CreateCommand();

            comprobar.CommandText =
                """
                SELECT COUNT(*)
                FROM ProgramaVisitaV2
                WHERE CongreCodigo = $codigo
                  AND MesReferencia = $mes
                  AND DiaSemana = $dia
                  AND Turno = $turno
                  AND Responsable = $responsable
                  AND Id <> $id;
                """;

            comprobar.Parameters.AddWithValue("$codigo", codigo);
            comprobar.Parameters.AddWithValue("$mes", mesReferencia);
            comprobar.Parameters.AddWithValue("$dia", actividad.Dia ?? "");
            comprobar.Parameters.AddWithValue("$turno", actividad.Turno ?? "");
            comprobar.Parameters.AddWithValue("$responsable", actividad.Responsable ?? "");
            comprobar.Parameters.AddWithValue("$id", actividad.Id);

            long existentes =
                Convert.ToInt64(
                    comprobar.ExecuteScalar() ?? 0);

            if (existentes > 0)
            {
                throw new InvalidOperationException(
                    $"Ya existe una asignación para {actividad.Responsable} " +
                    $"el {actividad.Dia} en {actividad.Turno}.");
            }
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        if (actividad.Id > 0)
        {
            cmd.CommandText =
                """
                UPDATE ProgramaVisitaV2
                SET DiaSemana = $dia,
                    Turno = $turno,
                    Responsable = $responsable,
                    TipoActividad = $tipo,
                    Hora = $hora,
                    NotasLibro = $notas,
                    CompaneroId = $companero,
                    Direccion = $direccion,
                    Latitud = $latitud,
                    Longitud = $longitud
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                actividad.Id);
        }
        else
        {
            cmd.CommandText =
                """
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
                    $companero,
                    $direccion,
                    $latitud,
                    $longitud
                );
                """;
        }

        cmd.Parameters.AddWithValue("$codigo", codigo);
        cmd.Parameters.AddWithValue("$mes", mesReferencia);
        cmd.Parameters.AddWithValue("$dia", actividad.Dia ?? "");
        cmd.Parameters.AddWithValue("$turno", actividad.Turno ?? "");
        cmd.Parameters.AddWithValue("$responsable", actividad.Responsable ?? "");
        cmd.Parameters.AddWithValue("$tipo", actividad.TipoActividad ?? "");
        cmd.Parameters.AddWithValue("$hora", actividad.Hora ?? "");
        cmd.Parameters.AddWithValue("$notas", actividad.Notas ?? "");
        cmd.Parameters.AddWithValue("$companero", actividad.CompaneroId);
        cmd.Parameters.AddWithValue("$direccion", actividad.Ubicacion ?? "");
        cmd.Parameters.AddWithValue("$latitud", actividad.Latitud);
        cmd.Parameters.AddWithValue("$longitud", actividad.Longitud);

        cmd.ExecuteNonQuery();
    }


    public static void EliminarActividadProgramaVisitaMobile(
        int id)
    {
        if (id <= 0)
        {
            return;
        }

        AnotarActividadMobile(
            "Visitas",
            "Eliminó una actividad del programa de la visita");

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM ProgramaVisitaV2 WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    private static void AsegurarColumnasProgramaVisitaUbicacionMobile(
        SqliteConnection connection)
    {
        using (SqliteCommand crear =
               connection.CreateCommand())
        {
            crear.CommandText =
                """
                CREATE TABLE IF NOT EXISTS ProgramaVisitaV2
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CongreCodigo TEXT NOT NULL,
                    MesReferencia TEXT NOT NULL,
                    DiaSemana TEXT NOT NULL,
                    Turno TEXT NOT NULL,
                    Responsable TEXT NOT NULL,
                    TipoActividad TEXT,
                    Hora TEXT,
                    NotasLibro TEXT,
                    CompaneroId INTEGER,
                    Direccion TEXT,
                    Latitud REAL DEFAULT 0,
                    Longitud REAL DEFAULT 0
                );
                """;

            crear.ExecuteNonQuery();
        }

        var columnas =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        using (SqliteCommand pragma =
               connection.CreateCommand())
        {
            pragma.CommandText =
                "PRAGMA table_info(ProgramaVisitaV2);";

            using SqliteDataReader reader =
                pragma.ExecuteReader();

            while (reader.Read())
            {
                if (!reader.IsDBNull(1))
                {
                    columnas.Add(
                        reader.GetString(1));
                }
            }
        }

        // Compatibilidad WPF: la columna real que WPF lee/escribe es
        // ProgramaVisitaV2.Direccion. Se crea si falta para poder escribir
        // ubicaciones que WPF pueda ver. "Ubicacion" se conserva como
        // columna heredada de versiones anteriores de la app móvil y solo
        // se usa como respaldo de lectura (no se elimina ni se renombra).
        if (!columnas.Contains("Direccion"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "ALTER TABLE ProgramaVisitaV2 ADD COLUMN Direccion TEXT;";

            cmd.ExecuteNonQuery();
        }

        if (!columnas.Contains("Ubicacion"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "ALTER TABLE ProgramaVisitaV2 ADD COLUMN Ubicacion TEXT;";

            cmd.ExecuteNonQuery();
        }

        if (!columnas.Contains("Latitud"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "ALTER TABLE ProgramaVisitaV2 ADD COLUMN Latitud REAL DEFAULT 0;";

            cmd.ExecuteNonQuery();
        }

        if (!columnas.Contains("Longitud"))
        {
            using SqliteCommand cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "ALTER TABLE ProgramaVisitaV2 ADD COLUMN Longitud REAL DEFAULT 0;";

            cmd.ExecuteNonQuery();
        }
    }


    // ========================================================================
    // NOTAS PERSONALES
    // ========================================================================

    public static List<VistaNotaVisitaMobile>
        ObtenerOCrearNotasVisitaMobile(
            int semanaId)
    {
        if (semanaId <= 0)
        {
            return new List<VistaNotaVisitaMobile>();
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cuenta =
            connection.CreateCommand();

        cuenta.CommandText =
            "SELECT COUNT(*) FROM NotasVisita WHERE SemanaId = $semana;";

        cuenta.Parameters.AddWithValue(
            "$semana",
            semanaId);

        long total =
            Convert.ToInt64(
                cuenta.ExecuteScalar() ?? 0);

        if (total == 0)
        {
            using SqliteTransaction tx =
                connection.BeginTransaction();

            InsertarPlantillaNotasVisitaMobile(
                connection,
                tx,
                semanaId);

            tx.Commit();
        }

        return CargarNotasVistaMobile(
            connection,
            semanaId);
    }


    public static void ActualizarNotaVisitaMobile(
        VistaNotaVisitaMobile nota)
    {
        if (nota.Id <= 0)
        {
            return;
        }

        AnotarActividadMobile(
            "Visitas",
            "Editó una nota de la visita");

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            UPDATE NotasVisita
            SET Respuesta = $respuesta,
                EsAlerta = $alerta
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue(
            "$respuesta",
            nota.Respuesta ?? "");

        cmd.Parameters.AddWithValue(
            "$alerta",
            nota.EsAlerta ? 1 : 0);

        cmd.Parameters.AddWithValue(
            "$id",
            nota.Id);

        cmd.ExecuteNonQuery();
    }


    private static void InsertarPlantillaNotasVisitaMobile(
        SqliteConnection connection,
        SqliteTransaction tx,
        int semanaId)
    {
            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REGISTROS",
                "¿Están ordenados y al día?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REGISTROS",
                "¿Se están aplicando las medidas de retención de documentos? (sfl-a)");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REGISTROS",
                "¿Se indica en las tarjetas de publicadores inactivos (bautizados) la fecha en la que empezaron a serlo?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REGISTROS",
                "¿Tienen los nombres de publicadores la structure correcta?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REGISTROS",
                "¿Están todos los datos de contacto de los publicadores (incluidos inactivos)?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN CON CCA",
                "Apunta aquí los deberes para el CCA");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN CON CCA",
                "¿Quién hace la oración final?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN DE ANCIANOS",
                "Información sobre cada una de las recomendaciones. ¿Son unánimes?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN DE ANCIANOS",
                "Si hay recomendaciones de baja, ¿qué requisitos bíblicos no cumple el hermano?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN DE ANCIANOS",
                "Comentarios sobre los puntos a tratar");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIÓN DE ANCIANOS",
                "¿Hay algún otro problema grave que requiera atención?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PASTOREO",
                "Comentarios sobre las visitas de pastoreo y llamadas programadas para la semana");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PASTOREO",
                "¿Cómo se está llevando a cabo el pastoreo en general?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PASTOREO",
                "¿A qué publicadores podríamos llamar durante la semana?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PASTOREO",
                "Si existe un grupo, ¿están recibiendo pastoreo sus miembros? ¿Muestran los A vivo interés en el progreso del grupo?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PASTOREO",
                "¿Se ha hecho la visita anual a aquellos que cumplen un año bautizados?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay algún PR que esté pasando por dificultades o necesite ayuda (horario, eficacia, desánimo, invisible, etc.)?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay algún PR que resulta obvio que no puede continuar? Comenta el asunto con el CSC");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay algún PR de salud delicada o que podría encajar en tal perfil?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay algún precursor especial o misionero? ¿Necesita ayuda/consejo? ¿Está apuntado para predicar?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay graduados de la EER? ¿Cómo están? ¿Cómo se les usa? ¿Se les podría usar más plenamente?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PRECURSOR",
                "¿Hay alguien a quien podría animarse a servir como PR o PAux?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay publicadores que se hayan mudado a la congregación? Pregunta por ellos");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay publicadores en prisión que no estén recibiendo ayuda espiritual? ¿Se ha informado a la cong de contacto?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay algún hermano con dificultades para obtener las cosas básicas? ¿Sirvió en el pasado como SETC?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Cómo se puede ayudar a los publicadores irregulares?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "Los publicadores inactivos, ¿permiten que se les contacte? ¿Hay posibilidad de ayudarles?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay algún sacado/desasociado reciente? ¿O algún comité/investigación en curso? ¿Por qué motivo bíblico?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay hermanos que no sean ejemplares? ¿Hay hermanos con restricciones? ¿Se les van alzando paulatinamente?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay algún noviazgo en vigor o matrimonio reciente? Si hay algún caso de libertad bíblica, informar a la sucursal");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay algún extranjero indocumentado? ¿Ha dado pasos en firme para obtener la residencia legal?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "PUBLICADORES",
                "¿Hay algún hermano/a que solo puede salir el fin de semana y que se beneficiaría especialmente de salir juntos?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "¿Se trata este punto en las reuniones trimestrales?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "¿Están los jóvenes bien integrados y progresando? ¿Cómo se les usa? ¿Alguno que preocupe?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "Pregunta por algunos varones, quizás con posibilidad de asumir más responsabilidades");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "Los varones, ¿presentan asignaciones aparte de lecturas y discursos? ¿Presentan discursos los pub no bautizados?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "¿Qué trabajos hacen los SM? (Dptos, partes en VyMC, pastoreo, discursos públicos, etc.)");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "¿Se ha capacitado al siervo de publicaciones para cumplir con su asignación?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CAPACITACIÓN",
                "¿Quién se encarga del mantenimiento? ¿Se capacita a padres e hijos en labores de mantenimiento y limpieza?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIONES",
                "¿Qué tal la asistencia presencial a las reuniones?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIONES",
                "¿Cómo se muestra hospitalidad a los oradores? ¿Se les ofrece dinero y comidas?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIONES",
                "Si existe un grupo, ¿hay hermanos con posibilidad de apoyarlo? ¿Se organizan salidas con este? ¿Y durante la semana?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "REUNIONES",
                "Si existe un grupo, ¿celebra las reuniones en el mismo horario o diferente de la congregación anfitriona?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Qué tal la asistencia presencial al ministerio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Quién es el SS? ¿Cómo hace las visitas a los grupos?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Cómo se lleva a cabo la predicación como grupo?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Apoyan la predicación semanalmente los varones nombrados? ¿Y sus familias?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿En qué aspectos del ministerio están obteniendo los publicadores buenos resultados?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Cómo se organizan las reuniones para la predicación? ¿Se habilitará zoom durante la visita? ¿Quién lo organiza?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Hay un calendario para la PPOC? ¿Cómo funciona?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Habrá suficiente territorio para cada día de la visita?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Se motiva a los publicadores a servir como precursores auxiliares en meses clave?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Hay PR con asignaciones teocráticas adicionales? (Betel, RTO, LDC, CEH, etc.)");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Se ha abarcado todo el territorio en el último año de servicio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Hay territorios más de 2 años sin trabajar?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Hay suficientes territorios en manos de los hermanos?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Se aprovechan bien las campañas?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Están equilibrados los grupos de predicación?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "¿Hay existencias de las publicaciones que mejor encajan en el territorio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "MINISTERIO",
                "Pregunta a un miembro del CSC sobre las tendencias que haya detectado, aspectos que merezcan elogio o consejo");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Han recibido los hermanos capacitación para la predicación pública basada en el S-148?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "El programa de PPOC, ¿es eficaz? ¿Podría hacerse algún ajuste (horario, lugar, etc.)? ¿Está el equipo en buen estado?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Existen residencias de ancianos o universidades en las que se pueda dar testimonio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "Si hay personas que han pedido que no se les visite, ¿está la anotación en la tarjeta? ¿Se les contacta cada 2-3 años?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Están progresando los CB?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "Los CB, ¿son mayoritariamente familiares de hermanos, o más bien del territorio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Hay personas que dependan de otro idioma diferente de la congregación?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Saben los publicadores usar bien el kit de enseñanza?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "En general, cumplen los PR con el requisito de horas?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OBSERVACIONES DE LA PREDICACIÓN",
                "¿Se ha formado algún campamento de refugiados en la zona? En tal caso habrá que avisar a SRV");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se están indicando correctamente los fondos enviados a la sucursal?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "Las auditorías, ¿se llevan a cabo regularmente?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "Los fondos de la congregación, ¿superan el máximo establecido?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se está enviando la cantidad acordada a la cuenta de mantenimiento?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se han detectado irregularidades en las auditorías?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se ha conciliado correctamente el último estado de cuentas siguiendo las Instrucciones de contabilidad (S-27)?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Pone el CCA sus iniciales en las facturas?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se hacen los ingresos semanalmente o hay una resolución aprobada para hacerlos con menos regularidad?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CONTABILIDAD",
                "¿Se han detectado otros puntos a mejorar?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Se ciñe la organización de las reuniones a las pautas de audio y video?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Hay un programa de limpieza establecido y colgado en el tablero de anuncios?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Está el cartel de aviso transmisión colgado?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Se hace uso de los programas grabados en JW Stream? ¿Con qué frecuencia se presentan discursos por zoom?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Cómo es la asistencia en reference al número de publicadores?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Ha habido algún incidente, quasi accidente, incendio, robo o allanamiento? ¿Se ha enviado el TO-5?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Se presentan las necesidades relacionadas con la seguridad cada año?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Cómo funciona el plan de emergencia para proteger a los publicadores con necesidades especiales y los registros?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "SALÓN DEL REINO Y SEGURIDAD",
                "¿Entienden los A cómo proceder en caso de desastre? ¿Convendría recordárselo?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Están equilibradas las responsabilidades? ¿Cumplen bien los ancianos con ellas?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Cómo es la relación entre los varones nombrados?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Han animado a todos los publicadores a rellenar su DPA? ¿Conocen el procedimiento para contactar con el CEH?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Qué tal la calidad de la enseñanza? Puedes valorar a quienes han tenido participación. Si algún anciano califica para tener una asignación en la AC, también se puede indicar");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Saben los ancianos redactar cartas claras y concisas? ¿Convendría capacitarles en este aspecto?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Conviene recordarles la importancia de ser juzgar con justicia, discernimiento y sabiduría?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Ponen normas innecesarias?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "En las RA, ¿toman nota solo de las decisiones? ¿O hacen informes demasiado detallados? (sfl 1:8)");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Dedican tiempo a predicar con los PR en diversas facetas? Comenta en la RA la actividad y circunstancias de los PR");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Están al tanto de las pautas sobre información pública? (sfl cap. 30)");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "CUERPO DE ANCIANOS",
                "¿Tienen los ancianos la costumbre de elogiar de forma específica y sincera?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "Hasta donde se sabe, ¿tienen los hermanos buenas rutinas de estudio?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Comentan los hermanos ejemplarmente en las reuniones?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Celebran las familias la NAF regularmente? ¿Invitan a otros hermanos?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Hay algún hermano/a con habilidades en un oficio que todavía no haya rellenado el A-2?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Podría animarse a alguien a rellenar la solicitud A-19?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Hermanos con potencial para servir como SM, A, PR, CEH, Betel, LDC, etc?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Es la congregación hospitalaria? ¿Hacen actividades sanas juntos? ¿Llevan los varones nombrados la delantera?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Necesita ayuda la congregación de algún tipo? ¿Convendría enviar un S-310?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Hay que enviar algún S-326? Si merece consideración especial, indícalo en una carta a SRV - Ministros en el Campo");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Hay alguna solicitud G-8 para atender? ¿Hay alguien excepcional a quien se pueda invitar a rellenarla?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Sigue reuniendo la entidad las condiciones expuestas en sfl apéndice B?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "¿Convendría dividir, disolver, proponer un grupo/pregrupo o hacer algún otro cambio ahora o a corto plazo?");

            InsertarNotaPlantillaVisitaMobile(
                connection,
                tx,
                semanaId,
                "OTRAS ANOTACIONES",
                "Anotaciones para próximas visitas:");
    }


    private static void InsertarNotaPlantillaVisitaMobile(
        SqliteConnection connection,
        SqliteTransaction tx,
        int semanaId,
        string cabecera,
        string pregunta)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.Transaction =
            tx;

        cmd.CommandText =
            """
            INSERT INTO NotasVisita
            (
                SemanaId,
                Cabecera,
                Pregunta,
                Respuesta,
                EsAlerta
            )
            VALUES
            (
                $semana,
                $cabecera,
                $pregunta,
                '',
                0
            );
            """;

        cmd.Parameters.AddWithValue("$semana", semanaId);
        cmd.Parameters.AddWithValue("$cabecera", cabecera);
        cmd.Parameters.AddWithValue("$pregunta", pregunta);
        cmd.ExecuteNonQuery();
    }


    // ========================================================================
    // CONTABILIDAD
    // ========================================================================

    public static List<VistaContabilidadMobile>
        ObtenerContabilidadVisitaEditableMobile(
            string codigo,
            DateTime mesFinal)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        return CargarContabilidadVistaMobile(
                connection,
                codigo,
                mesFinal)
            .OrderByDescending(x => x.Mes)
            .ToList();
    }


    public static void GuardarContabilidadVisitaMobile(
        string codigo,
        VistaContabilidadMobile fila)
    {
        AnotarActividadMobile(
            "Visitas",
            "Guardó la contabilidad de " + codigo);

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        string mes =
            fila.Mes.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);

        cmd.CommandText =
            """
            INSERT INTO ContabilidadMes
            (
                CongreCodigo,
                MesReferencia,
                Gastos,
                Mantenimiento,
                ObraMundial,
                ObraResolucion,
                Saldo
            )
            VALUES
            (
                $codigo,
                $mes,
                $gastos,
                $mantenimiento,
                $obraMundial,
                $obraResolucion,
                $saldo
            )
            ON CONFLICT(CongreCodigo, MesReferencia)
            DO UPDATE SET
                Gastos = excluded.Gastos,
                Mantenimiento = excluded.Mantenimiento,
                ObraMundial = excluded.ObraMundial,
                ObraResolucion = excluded.ObraResolucion,
                Saldo = excluded.Saldo;
            """;

        cmd.Parameters.AddWithValue("$codigo", codigo);
        cmd.Parameters.AddWithValue("$mes", mes);
        cmd.Parameters.AddWithValue("$gastos", fila.Gastos);
        cmd.Parameters.AddWithValue("$mantenimiento", fila.Mantenimiento);
        cmd.Parameters.AddWithValue("$obraMundial", fila.ObraMundial);
        cmd.Parameters.AddWithValue("$obraResolucion", fila.ObraResolucion);
        cmd.Parameters.AddWithValue("$saldo", fila.Saldo);
        cmd.ExecuteNonQuery();
    }


    // ========================================================================
    // S-303 / DOCUMENTOS
    // ========================================================================

    public static DocumentoVisitaMobile?
        ObtenerS303VisitaMobile(
            string codigo,
            string mesReferencia)
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        string identificador =
            $"S-303_{codigo}_{mesReferencia}";

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Categoria,
                NombreArchivo,
                RutaLocal,
                ContenidoArchivo
            FROM Documentos
            WHERE Categoria = 'S-303'
              AND NombreArchivo LIKE $identificador
            ORDER BY Id DESC
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$identificador",
            "%" + identificador + "%");

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        return reader.Read()
            ? LeerDocumentoVisitaMobile(reader)
            : null;
    }


    public static DocumentoVisitaMobile?
        ObtenerPlantillaS303VisitaMobile()
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Categoria,
                NombreArchivo,
                RutaLocal,
                ContenidoArchivo
            FROM Documentos
            WHERE Categoria = 'Envío de S-303'
              AND lower(NombreArchivo) LIKE '%.pdf'
            ORDER BY Id DESC
            LIMIT 1;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        return reader.Read()
            ? LeerDocumentoVisitaMobile(reader)
            : null;
    }


    public static void GuardarPlantillaS303VisitaMobile(
        string nombreArchivo,
        string rutaLocal,
        byte[] contenido)
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        InsertarDocumentoVisitaMobile(
            connection,
            "Envío de S-303",
            nombreArchivo,
            rutaLocal,
            contenido);
    }


    public static void GuardarS303VisitaMobile(
        string codigo,
        string mesReferencia,
        string rutaLocal,
        byte[] contenido)
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        string identificador =
            $"S-303_{codigo}_{mesReferencia}";

        using (SqliteCommand eliminar =
               connection.CreateCommand())
        {
            eliminar.CommandText =
                """
                DELETE FROM Documentos
                WHERE Categoria = 'S-303'
                  AND NombreArchivo LIKE $identificador;
                """;

            eliminar.Parameters.AddWithValue(
                "$identificador",
                "%" + identificador + "%");

            eliminar.ExecuteNonQuery();
        }

        InsertarDocumentoVisitaMobile(
            connection,
            "S-303",
            identificador + ".pdf",
            rutaLocal,
            contenido);
    }


    public static void EliminarS303VisitaMobile(
        int id)
    {
        if (id <= 0)
        {
            return;
        }

        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM Documentos WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    private static SqliteConnection
        AbrirConexionDocumentosVisitaMobile()
    {
        var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using (SqliteCommand crear =
               connection.CreateCommand())
        {
            crear.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Documentos
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Categoria TEXT,
                    NombreArchivo TEXT,
                    RutaLocal TEXT,
                    ContenidoArchivo BLOB
                );
                """;

            crear.ExecuteNonQuery();
        }

        bool tieneBlob = false;

        using (SqliteCommand info =
               connection.CreateCommand())
        {
            info.CommandText =
                "PRAGMA table_info(Documentos);";

            using SqliteDataReader reader =
                info.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(
                        reader.GetString(1),
                        "ContenidoArchivo",
                        StringComparison.OrdinalIgnoreCase))
                {
                    tieneBlob = true;
                    break;
                }
            }
        }

        if (!tieneBlob)
        {
            using SqliteCommand alter =
                connection.CreateCommand();

            alter.CommandText =
                "ALTER TABLE Documentos ADD COLUMN ContenidoArchivo BLOB;";

            alter.ExecuteNonQuery();
        }

        return connection;
    }


    private static DocumentoVisitaMobile
        LeerDocumentoVisitaMobile(
            SqliteDataReader reader)
    {
        byte[] contenido =
            Array.Empty<byte>();

        if (!reader.IsDBNull(4))
        {
            contenido =
                (byte[])reader.GetValue(4);
        }

        string ruta =
            reader.IsDBNull(3)
                ? ""
                : reader.GetString(3);

        if (contenido.Length == 0
            &&
            !string.IsNullOrWhiteSpace(ruta)
            &&
            File.Exists(ruta))
        {
            try
            {
                contenido =
                    File.ReadAllBytes(ruta);
            }
            catch
            {
                contenido =
                    Array.Empty<byte>();
            }
        }

        return new DocumentoVisitaMobile
        {
            Id = reader.GetInt32(0),
            Categoria = reader.IsDBNull(1) ? "" : reader.GetString(1),
            NombreArchivo = reader.IsDBNull(2) ? "" : reader.GetString(2),
            RutaLocal = ruta,
            Contenido = contenido
        };
    }


    private static void InsertarDocumentoVisitaMobile(
        SqliteConnection connection,
        string categoria,
        string nombreArchivo,
        string rutaLocal,
        byte[] contenido)
    {
        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            INSERT INTO Documentos
            (
                Categoria,
                NombreArchivo,
                RutaLocal,
                ContenidoArchivo
            )
            VALUES
            (
                $categoria,
                $nombre,
                $ruta,
                $contenido
            );
            """;

        cmd.Parameters.AddWithValue("$categoria", categoria);
        cmd.Parameters.AddWithValue("$nombre", nombreArchivo);
        cmd.Parameters.AddWithValue("$ruta", rutaLocal ?? "");
        cmd.Parameters.AddWithValue("$contenido", contenido);
        cmd.ExecuteNonQuery();
    }


    // ========================================================================
    // S-2 / CONFIGURACIÓN
    // ========================================================================

    public static List<RecomendacionS2Mobile>
        ObtenerRecomendacionesS2VisitaMobile(
            string congregacionNombre,
            string congregacionCodigo = "")
    {
        var resultado =
            new List<RecomendacionS2Mobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "RecomendacionesNombramientos"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        // No filtramos en SQL: Hub puede devolver la congregación con
        // el código entre paréntesis ("Nombre (1234)"). Filtramos en
        // memoria con la misma tolerancia que WPF.
        cmd.CommandText =
            """
            SELECT
                Hermano,
                Tipo,
                RecomendadoComo,
                Congregacion
            FROM RecomendacionesNombramientos
            ORDER BY Tipo, Hermano;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string congregacionHub =
                LeerTexto(reader, 3);

            if (!EsRecomendacionDeCongregacionMobile(
                    congregacionHub,
                    congregacionNombre,
                    congregacionCodigo))
            {
                continue;
            }

            resultado.Add(
                new RecomendacionS2Mobile
                {
                    Hermano = LeerTexto(reader, 0),
                    Tipo = LeerTexto(reader, 1),
                    RecomendadoComo = LeerTexto(reader, 2)
                });
        }

        return resultado;
    }


    /// <summary>
    /// Compara la congregación de una recomendación con la actual,
    /// tolerando el sufijo "(código)" que añade JW Hub. Réplica de la
    /// lógica de VisitasPage de WPF.
    /// </summary>
    public static bool EsRecomendacionDeCongregacionMobile(
        string congregacionHub,
        string congregacionNombre,
        string congregacionCodigo)
    {
        string recibido =
            NormalizarTextoCongregacionMobile(
                congregacionHub);

        string actual =
            NormalizarTextoCongregacionMobile(
                congregacionNombre);

        if (string.IsNullOrEmpty(recibido)
            ||
            string.IsNullOrEmpty(actual))
        {
            return false;
        }

        if (recibido == actual)
        {
            return true;
        }

        string codigo =
            (congregacionCodigo ?? "").Trim();

        if (!string.IsNullOrEmpty(codigo))
        {
            string patron =
                @"\s*\(" +
                System.Text.RegularExpressions.Regex.Escape(
                    codigo) +
                @"\)\s*$";

            string sinCodigo =
                System.Text.RegularExpressions.Regex.Replace(
                    congregacionHub ?? "",
                    patron,
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (NormalizarTextoCongregacionMobile(
                    sinCodigo) == actual)
            {
                return true;
            }
        }

        return false;
    }


    private static string NormalizarTextoCongregacionMobile(
        string valor)
    {
        string descompuesto =
            (valor ?? "")
                .Trim()
                .Normalize(
                    System.Text.NormalizationForm.FormD);

        var sb =
            new System.Text.StringBuilder();

        foreach (char c in descompuesto)
        {
            if (System.Globalization.CharUnicodeInfo
                    .GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(
                    char.ToLowerInvariant(c));
            }
        }

        return System.Text.RegularExpressions.Regex
            .Replace(
                sb.ToString(),
                @"\s+",
                " ")
            .Trim();
    }


    /// <summary>
    /// Lista completa de recomendaciones de nombramientos (tabla
    /// compartida con WPF). Solo lectura: la aprobación/cancelación
    /// real requiere la automatización de JW Hub del escritorio.
    /// </summary>
    public static List<RecomendacionNombramientoMobile>
        ObtenerRecomendacionesNombramientosMobile()
    {
        var resultado =
            new List<RecomendacionNombramientoMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "RecomendacionesNombramientos"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Hermano,
                Tipo,
                Congregacion,
                Fecha,
                RecomendadoComo,
                COALESCE(UrlDetalle, '')
            FROM RecomendacionesNombramientos
            ORDER BY Congregacion, Tipo, Hermano;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new RecomendacionNombramientoMobile
                {
                    Id =
                        reader.IsDBNull(0)
                            ? 0
                            : reader.GetInt32(0),

                    Hermano =
                        LeerTexto(reader, 1),

                    Tipo =
                        LeerTexto(reader, 2),

                    Congregacion =
                        LeerTexto(reader, 3),

                    Fecha =
                        LeerTexto(reader, 4),

                    RecomendadoComo =
                        LeerTexto(reader, 5),

                    UrlDetalle =
                        LeerTexto(reader, 6)
                });
        }

        return resultado;
    }


    /// <summary>
    /// Borra una recomendación ya procesada (por Id). Espejo de
    /// EliminarRecomendacionProcesada de WPF.
    /// </summary>
    public static void EliminarRecomendacionNombramientoMobile(
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
            "DELETE FROM RecomendacionesNombramientos WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    public static string ObtenerConfiguracionVisitaMobile(
        string clave,
        string valorPorDefecto = "")
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "Configuracion"))
        {
            return valorPorDefecto;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "SELECT Valor FROM Configuracion WHERE Clave = $clave LIMIT 1;";

        cmd.Parameters.AddWithValue(
            "$clave",
            clave);

        object? valor =
            cmd.ExecuteScalar();

        return valor is null
               ||
               valor == DBNull.Value
            ? valorPorDefecto
            : valor.ToString() ?? valorPorDefecto;
    }


    // ========================================================================
    // DOCUMENTOS POR CATEGORÍA (Correspondencia / plantillas)
    // ========================================================================

    /// <summary>
    /// Devuelve los documentos almacenados en la tabla compartida
    /// <c>Documentos</c> para una categoría concreta (por ejemplo
    /// "Aviso de la visita"). Reutiliza la misma tabla que WPF.
    /// </summary>
    public static List<DocumentoVisitaMobile>
        ObtenerDocumentosVisitaMobile(
            string categoria)
    {
        var resultado =
            new List<DocumentoVisitaMobile>();

        if (string.IsNullOrWhiteSpace(categoria))
        {
            return resultado;
        }

        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Categoria,
                NombreArchivo,
                RutaLocal,
                ContenidoArchivo
            FROM Documentos
            WHERE Categoria = $categoria
            ORDER BY Id DESC;
            """;

        cmd.Parameters.AddWithValue(
            "$categoria",
            categoria);

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                LeerDocumentoVisitaMobile(
                    reader));
        }

        return resultado;
    }
}


public sealed class DocumentoVisitaMobile
{
    public int Id { get; set; }
    public string Categoria { get; set; } = "";
    public string NombreArchivo { get; set; } = "";
    public string RutaLocal { get; set; } = "";
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}


public sealed class RecomendacionS2Mobile
{
    public string Hermano { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string RecomendadoComo { get; set; } = "";
}


public sealed class RecomendacionNombramientoMobile
{
    public int Id { get; set; }
    public string Hermano { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Congregacion { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string RecomendadoComo { get; set; } = "";
    public string UrlDetalle { get; set; } = "";

    public string Resumen
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Congregacion))
            {
                partes.Add(Congregacion);
            }

            if (!string.IsNullOrWhiteSpace(Tipo))
            {
                partes.Add(Tipo);
            }

            if (!string.IsNullOrWhiteSpace(RecomendadoComo))
            {
                partes.Add("→ " + RecomendadoComo);
            }

            if (!string.IsNullOrWhiteSpace(Fecha))
            {
                partes.Add(Fecha);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}
