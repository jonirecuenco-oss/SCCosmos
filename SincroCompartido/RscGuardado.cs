using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// =============================================================
// GUARDAR UNA RSC: ASISTENTES, GRUPOS Y MIEMBROS
//
// La misma regla en Windows y en Android. Cada registro conserva su
// Id y solo se escribe lo que cambia, así que guardar lo mismo no
// viaja a los demás dispositivos. Si se borrase todo para volver a
// insertarlo con Id nuevos, dos dispositivos que guardasen la misma
// reunión a la vez se quedarían con los registros de ambos, con las
// personas duplicadas.
//
// El asistente y el miembro de una reunión se identifican por la
// persona (quien cambia de grupo conserva su registro); los grupos se
// reutilizan en orden. Lo que sobra se borra.
// =============================================================

public sealed class RscAsistenteAGuardar
{
    public int PersonaId { get; set; }

    public string? Nombre { get; set; }

    public bool Asiste { get; set; }

    public bool SaleAPredicar { get; set; }

    public bool AceptaGrupo { get; set; }

    public string? ArregloConPersonaIds { get; set; }

    public string? Sexo { get; set; }

    public string? Notas { get; set; }
}


public sealed class RscMiembroAGuardar
{
    public int PersonaId { get; set; }

    public string? Nombre { get; set; }
}


public sealed class RscGrupoAGuardar
{
    public int Numero { get; set; }

    public string? Notas { get; set; }

    public string? Territorio { get; set; }

    public List<RscMiembroAGuardar> Miembros { get; set; } =
        new();
}


public static class RscGuardado
{
    public static void GuardarAsistentes(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string reunionId,
        IEnumerable<RscAsistenteAGuardar> asistentes)
    {
        const string insertar =
            """
            INSERT INTO RSC_Asistentes
                (Id, RscReunionId, PersonaId, Nombre, Asiste, SaleAPredicar,
                 AceptaGrupo, ArregloConPersonaIds, Sexo, Notas)
            VALUES
                ($id, $reunion, $persona, $nombre, $asiste, $sale,
                 $acepta, $arreglos, $sexo, $notas);
            """;

        // ArregloConPersonaId es la columna antigua: un registro nuevo la
        // tendría a 0.
        const string actualizar =
            """
            UPDATE RSC_Asistentes
            SET Nombre = $nombre, Asiste = $asiste, SaleAPredicar = $sale,
                AceptaGrupo = $acepta, ArregloConPersonaIds = $arreglos,
                ArregloConPersonaId = 0, Sexo = $sexo, Notas = $notas
            WHERE Id = $id
              AND (Nombre IS NOT $nombre OR Asiste IS NOT $asiste
                   OR SaleAPredicar IS NOT $sale OR AceptaGrupo IS NOT $acepta
                   OR ArregloConPersonaIds IS NOT $arreglos
                   OR ArregloConPersonaId IS NOT 0
                   OR Sexo IS NOT $sexo OR Notas IS NOT $notas);
            """;

        Dictionary<int, Queue<string>> existentes =
            IdsPorPersona(
                conexion,
                transaccion,
                "RSC_Asistentes",
                reunionId);

        foreach (RscAsistenteAGuardar asistente in asistentes)
        {
            string? id =
                Siguiente(
                    existentes,
                    asistente.PersonaId);

            using var cmd =
                conexion.CreateCommand();

            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                id is null
                    ? insertar
                    : actualizar;

            cmd.Parameters.AddWithValue("$id", id ?? IdNuevo());
            cmd.Parameters.AddWithValue("$reunion", reunionId);
            cmd.Parameters.AddWithValue("$persona", asistente.PersonaId);
            cmd.Parameters.AddWithValue("$nombre", asistente.Nombre ?? "");
            cmd.Parameters.AddWithValue("$asiste", asistente.Asiste ? 1 : 0);
            cmd.Parameters.AddWithValue("$sale", asistente.SaleAPredicar ? 1 : 0);
            cmd.Parameters.AddWithValue("$acepta", asistente.AceptaGrupo ? 1 : 0);
            cmd.Parameters.AddWithValue("$arreglos", asistente.ArregloConPersonaIds ?? "");
            cmd.Parameters.AddWithValue("$sexo", asistente.Sexo ?? "");
            cmd.Parameters.AddWithValue("$notas", asistente.Notas ?? "");

            cmd.ExecuteNonQuery();
        }

        Borrar(
            conexion,
            transaccion,
            "RSC_Asistentes",
            existentes.Values.SelectMany(ids => ids));
    }


    /// <summary>
    /// Los grupos sin miembros no se guardan.
    /// </summary>
    public static void GuardarGrupos(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string reunionId,
        IEnumerable<RscGrupoAGuardar> grupos)
    {
        Queue<string> gruposExistentes =
            IdsDeGrupos(
                conexion,
                transaccion,
                reunionId);

        Dictionary<int, Queue<string>> miembrosExistentes =
            IdsPorPersona(
                conexion,
                transaccion,
                "RSC_Miembros",
                reunionId);

        int ordenGrupo =
            0;

        foreach (RscGrupoAGuardar grupo in grupos)
        {
            if (grupo.Miembros.Count == 0)
            {
                continue;
            }

            string grupoId =
                GuardarGrupo(
                    conexion,
                    transaccion,
                    reunionId,
                    grupo,
                    ordenGrupo,
                    gruposExistentes);

            ordenGrupo++;

            int ordenMiembro =
                0;

            foreach (RscMiembroAGuardar miembro in grupo.Miembros)
            {
                GuardarMiembro(
                    conexion,
                    transaccion,
                    reunionId,
                    grupoId,
                    miembro,
                    ordenMiembro,
                    miembrosExistentes);

                ordenMiembro++;
            }
        }

        Borrar(
            conexion,
            transaccion,
            "RSC_Miembros",
            miembrosExistentes.Values.SelectMany(ids => ids));

        Borrar(
            conexion,
            transaccion,
            "RSC_Grupos",
            gruposExistentes);
    }


    private static string GuardarGrupo(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string reunionId,
        RscGrupoAGuardar grupo,
        int orden,
        Queue<string> existentes)
    {
        const string insertar =
            """
            INSERT INTO RSC_Grupos
                (Id, RscReunionId, Numero, Notas, Territorio, Orden)
            VALUES
                ($id, $reunion, $numero, $notas, $territorio, $orden);
            """;

        const string actualizar =
            """
            UPDATE RSC_Grupos
            SET Numero = $numero, Notas = $notas, Territorio = $territorio, Orden = $orden
            WHERE Id = $id
              AND (Numero IS NOT $numero OR Notas IS NOT $notas
                   OR Territorio IS NOT $territorio OR Orden IS NOT $orden);
            """;

        bool existia =
            existentes.TryDequeue(
                out string? existente);

        string id =
            existente ?? IdNuevo();

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            existia
                ? actualizar
                : insertar;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$reunion", reunionId);
        cmd.Parameters.AddWithValue("$numero", grupo.Numero);
        cmd.Parameters.AddWithValue("$notas", grupo.Notas ?? "");
        cmd.Parameters.AddWithValue("$territorio", grupo.Territorio ?? "");
        cmd.Parameters.AddWithValue("$orden", orden);

        cmd.ExecuteNonQuery();

        return id;
    }


    private static void GuardarMiembro(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string reunionId,
        string grupoId,
        RscMiembroAGuardar miembro,
        int orden,
        Dictionary<int, Queue<string>> existentes)
    {
        const string insertar =
            """
            INSERT INTO RSC_Miembros
                (Id, RscGrupoId, RscReunionId, PersonaId, Nombre, Orden)
            VALUES
                ($id, $grupo, $reunion, $persona, $nombre, $orden);
            """;

        const string actualizar =
            """
            UPDATE RSC_Miembros
            SET RscGrupoId = $grupo, Nombre = $nombre, Orden = $orden
            WHERE Id = $id
              AND (RscGrupoId IS NOT $grupo OR Nombre IS NOT $nombre OR Orden IS NOT $orden);
            """;

        string? id =
            Siguiente(
                existentes,
                miembro.PersonaId);

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            id is null
                ? insertar
                : actualizar;

        cmd.Parameters.AddWithValue("$id", id ?? IdNuevo());
        cmd.Parameters.AddWithValue("$grupo", grupoId);
        cmd.Parameters.AddWithValue("$reunion", reunionId);
        cmd.Parameters.AddWithValue("$persona", miembro.PersonaId);
        cmd.Parameters.AddWithValue("$nombre", miembro.Nombre ?? "");
        cmd.Parameters.AddWithValue("$orden", orden);

        cmd.ExecuteNonQuery();
    }


    private static Queue<string> IdsDeGrupos(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string reunionId)
    {
        var ids =
            new Queue<string>();

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            "SELECT Id FROM RSC_Grupos WHERE RscReunionId = $id ORDER BY Orden, Numero, Id;";

        cmd.Parameters.AddWithValue(
            "$id",
            reunionId);

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            ids.Enqueue(
                lector.GetString(0));
        }

        return ids;
    }


    private static Dictionary<int, Queue<string>> IdsPorPersona(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        string reunionId)
    {
        var ids =
            new Dictionary<int, Queue<string>>();

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            $"SELECT Id, PersonaId FROM {tabla} WHERE RscReunionId = $id ORDER BY Id;";

        cmd.Parameters.AddWithValue(
            "$id",
            reunionId);

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            int persona =
                lector.IsDBNull(1)
                    ? 0
                    : lector.GetInt32(1);

            if (!ids.TryGetValue(
                    persona,
                    out Queue<string>? deEsaPersona))
            {
                deEsaPersona =
                    new Queue<string>();

                ids[persona] =
                    deEsaPersona;
            }

            deEsaPersona.Enqueue(
                lector.GetString(0));
        }

        return ids;
    }


    private static string? Siguiente(
        Dictionary<int, Queue<string>> existentes,
        int persona)
    {
        return existentes.TryGetValue(
                   persona,
                   out Queue<string>? ids)
               && ids.TryDequeue(
                   out string? id)
            ? id
            : null;
    }


    private static void Borrar(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            using var cmd =
                conexion.CreateCommand();

            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                $"DELETE FROM {tabla} WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            cmd.ExecuteNonQuery();
        }
    }


    private static string IdNuevo()
    {
        return Guid.NewGuid()
            .ToString("N");
    }
}
