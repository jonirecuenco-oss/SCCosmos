using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// =============================================================
// IMPORTACIÓN DE TERRITORIOS (S-13)
//
// La misma regla en Windows y en Android. Cada registro es una
// asignación de un territorio a una persona, así que un territorio
// tiene varios registros.
//
// Los registros que ya hay se reutilizan en orden: el i-ésimo de la
// importación ocupa el i-ésimo existente, así la lista, que se muestra
// por Id, conserva el orden importado. Solo se escribe lo que cambia,
// para que lo que no ha cambiado no viaje a los demás dispositivos.
//
// El S-13 no trae el vínculo con la persona del S-21: se conserva el
// de la misma asignación y, si es nueva, el de esa persona si no hay
// duda (el mismo nombre puede corresponder a varias personas). Lo demás
// queda en blanco. No se conserva por nombre de territorio: cada
// asignación de un territorio puede ser de una persona distinta.
// =============================================================

public sealed class TerritorioImportado
{
    public string? Nombre { get; set; }

    public string? UltimaFechaCompletado { get; set; }

    public string? AsignadoA { get; set; }

    public string? FechaAsignado { get; set; }

    public string? FechaCompletado { get; set; }
}


public static class TerritoriosImportados
{
    public static void Guardar(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string congregacion,
        IEnumerable<TerritorioImportado> territorios)
    {
        var registros =
            new List<long>();

        var creacion =
            new Dictionary<string, string>();

        var vinculosAsignacion =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var vinculosPersona =
            new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);

        using (var cmd =
               conexion.CreateCommand())
        {
            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                """
                SELECT Id, Nombre, AsignadoA, FechaAsignado, NombreVinculado, FechaCreacion
                FROM TerritoriosVisitaV3
                WHERE CongregacionCodigo = $codigo
                ORDER BY Id;
                """;

            cmd.Parameters.AddWithValue(
                "$codigo",
                congregacion);

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                registros.Add(
                    lector.GetInt64(0));

                string nombre =
                    Texto(lector, 1);

                if (!string.IsNullOrWhiteSpace(nombre)
                    && !lector.IsDBNull(5))
                {
                    creacion[nombre] =
                        lector.GetString(5);
                }

                string vinculo =
                    Texto(lector, 4).Trim();

                string asignado =
                    Texto(lector, 2).Trim();

                if (vinculo.Length == 0
                    || asignado.Length == 0)
                {
                    continue;
                }

                vinculosAsignacion[
                    ClaveAsignacion(
                        nombre,
                        asignado,
                        Texto(lector, 3))] =
                    vinculo;

                if (!vinculosPersona.TryGetValue(
                        asignado,
                        out HashSet<string>? deEsaPersona))
                {
                    deEsaPersona =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase);

                    vinculosPersona[asignado] =
                        deEsaPersona;
                }

                deEsaPersona.Add(
                    vinculo);
            }
        }

        const string actualizar =
            """
            UPDATE TerritoriosVisitaV3
            SET Nombre = $nombre, UltimaFechaCompletado = $ultima, AsignadoA = $asignado,
                FechaAsignado = $fechaAsig, FechaCompletado = $fechaComp,
                NombreVinculado = $vinculo, FechaCreacion = $creacion, FechaEliminacion = NULL
            WHERE Id = $id
              AND (Nombre IS NOT $nombre OR UltimaFechaCompletado IS NOT $ultima
                   OR AsignadoA IS NOT $asignado OR FechaAsignado IS NOT $fechaAsig
                   OR FechaCompletado IS NOT $fechaComp OR NombreVinculado IS NOT $vinculo
                   OR FechaCreacion IS NOT $creacion OR FechaEliminacion IS NOT NULL);
            """;

        const string insertar =
            """
            INSERT INTO TerritoriosVisitaV3
            (CongregacionCodigo, Nombre, UltimaFechaCompletado, AsignadoA, FechaAsignado,
             FechaCompletado, NombreVinculado, FechaCreacion)
            VALUES
            ($codigo, $nombre, $ultima, $asignado, $fechaAsig, $fechaComp, $vinculo, $creacion);
            """;

        int usados =
            0;

        foreach (TerritorioImportado territorio in territorios)
        {
            string nombre =
                territorio.Nombre ?? "";

            string fechaCreacion =
                creacion.TryGetValue(
                    nombre,
                    out string? anterior)
                && !string.IsNullOrWhiteSpace(anterior)
                    ? anterior
                    : DateTime.Today.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture);

            using var cmd =
                conexion.CreateCommand();

            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                usados < registros.Count
                    ? actualizar
                    : insertar;

            cmd.Parameters.AddWithValue("$nombre", nombre);
            cmd.Parameters.AddWithValue("$ultima", territorio.UltimaFechaCompletado ?? "");
            cmd.Parameters.AddWithValue("$asignado", territorio.AsignadoA ?? "");
            cmd.Parameters.AddWithValue("$fechaAsig", territorio.FechaAsignado ?? "");
            cmd.Parameters.AddWithValue("$fechaComp", territorio.FechaCompletado ?? "");
            cmd.Parameters.AddWithValue("$creacion", fechaCreacion);

            cmd.Parameters.AddWithValue(
                "$vinculo",
                VinculoDe(
                    territorio,
                    vinculosAsignacion,
                    vinculosPersona));

            if (usados < registros.Count)
            {
                cmd.Parameters.AddWithValue(
                    "$id",
                    registros[usados]);
            }
            else
            {
                cmd.Parameters.AddWithValue(
                    "$codigo",
                    congregacion);
            }

            usados++;

            cmd.ExecuteNonQuery();
        }

        // Los registros que sobran (la importación trae menos).
        for (int i = usados; i < registros.Count; i++)
        {
            using var cmd =
                conexion.CreateCommand();

            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                "DELETE FROM TerritoriosVisitaV3 WHERE Id = $id;";

            cmd.Parameters.AddWithValue(
                "$id",
                registros[i]);

            cmd.ExecuteNonQuery();
        }
    }


    private static string VinculoDe(
        TerritorioImportado territorio,
        Dictionary<string, string> vinculosAsignacion,
        Dictionary<string, HashSet<string>> vinculosPersona)
    {
        string asignado =
            (territorio.AsignadoA ?? "").Trim();

        if (asignado.Length == 0)
        {
            return "";
        }

        if (vinculosAsignacion.TryGetValue(
                ClaveAsignacion(
                    territorio.Nombre,
                    asignado,
                    territorio.FechaAsignado),
                out string? vinculo))
        {
            return vinculo;
        }

        return vinculosPersona.TryGetValue(
                   asignado,
                   out HashSet<string>? deEsaPersona)
               && deEsaPersona.Count == 1
            ? deEsaPersona.First()
            : "";
    }


    private static string ClaveAsignacion(
        string? territorio,
        string asignado,
        string? fechaAsignado)
    {
        return (territorio ?? "").Trim()
               + "\u0001"
               + asignado
               + "\u0001"
               + (fechaAsignado ?? "").Trim();
    }


    private static string Texto(
        SqliteDataReader lector,
        int columna)
    {
        return lector.IsDBNull(columna)
            ? ""
            : lector.GetString(columna);
    }
}
