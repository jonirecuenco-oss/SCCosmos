using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// =============================================================
// AL FUSIONAR DOS FICHAS DE UNA MISMA PERSONA
//
// Los programas, las escuelas y las donaciones apuntan a la persona por
// su Id. Si no se pasan a la ficha que se conserva, al borrar la otra se
// quedan apuntando a nadie y la asignación desaparece de la pantalla.
//
// La actividad, los territorios, el cónyuge y los traslados tienen sus
// propias reglas y cada fusión los trata aparte.
// =============================================================

public static class ReferenciasPersona
{
    private static readonly (string Tabla, string Columna)[] Columnas =
    [
        ("Donaciones", "PersonaId"),
        ("EscuelasAlumnos", "PersonaId"),
        ("EscuelasClases", "InstructorId"),
        ("EscuelasClases", "SustitutoId"),
        ("EscuelasHorario", "OracionInicialId"),
        ("EscuelasHorario", "OracionIntermediaId"),
        ("EscuelasHorario", "OracionFinalId"),
        ("EscuelasTalleres", "AsignadoId"),
        ("EscuelasTalleres", "AcompananteId"),
        ("ProgramaAsambleasOrganigrama", "AsignadoId"),
        ("ProgramaAsambleasPartes", "AsignadoId"),
        ("ProgramaVisita", "CompaneroId"),
        ("ProgramaVisitaV2", "CompaneroId"),
        ("ReunionPrecursoresPartes", "AsignadoId"),
        ("RSC_Asistentes", "PersonaId"),
        ("RSC_Asistentes", "ArregloConPersonaId"),
        ("RSC_Miembros", "PersonaId")
    ];


    public static void Redirigir(
        SqliteConnection conexion,
        SqliteTransaction? transaccion,
        long destino,
        long origen)
    {
        var existentes =
            new HashSet<(string Tabla, string Columna)>();

        using (var consulta =
               conexion.CreateCommand())
        {
            consulta.Transaction =
                transaccion;

            consulta.CommandText =
                """
                SELECT m.name, p.name
                FROM sqlite_master m
                JOIN pragma_table_info(m.name) p
                WHERE m.type = 'table';
                """;

            using var lector =
                consulta.ExecuteReader();

            while (lector.Read())
            {
                existentes.Add(
                    (lector.GetString(0), lector.GetString(1)));
            }
        }

        foreach ((string Tabla, string Columna) referencia in Columnas)
        {
            // Un dispositivo puede no tener alguna de estas tablas.
            if (!existentes.Contains(referencia))
            {
                continue;
            }

            using var cmd =
                conexion.CreateCommand();

            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                $"UPDATE \"{referencia.Tabla}\" SET \"{referencia.Columna}\" = $destino " +
                $"WHERE \"{referencia.Columna}\" = $origen;";

            cmd.Parameters.AddWithValue(
                "$destino",
                destino);

            cmd.Parameters.AddWithValue(
                "$origen",
                origen);

            cmd.ExecuteNonQuery();
        }
    }
}