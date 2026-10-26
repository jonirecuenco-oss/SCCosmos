using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;

// =============================================================
// CONTADORES DEL INICIO (consulta ligera)
//
// Antes se cargaban TODAS las columnas de todas las personas para
// contar en memoria (~930 filas). Con esta consulta solo se leen los
// campos que intervienen en los contadores.
// =============================================================

public static partial class DatabaseHelper
{
    public sealed class PersonaEstadisticaMobile
    {
        public string Sexo { get; set; } = "";

        public string Servicio { get; set; } = "";

        public string Nombramiento { get; set; } = "";

        public string Solicitudes { get; set; } = "";

        public string Privilegios { get; set; } = "";

        public string FechaBautismo { get; set; } = "";

        public string FechaNacimiento { get; set; } = "";
    }


    /// <summary>
    /// Personas activas con los campos que usan los contadores de Inicio.
    /// Es mucho más rápida que ObtenerPersonas() porque no trae fotos,
    /// direcciones, notas ni el resto de columnas.
    /// </summary>
    public static List<PersonaEstadisticaMobile>
        ObtenerPersonasParaEstadisticasMobile()
    {
        var lista =
            new List<PersonaEstadisticaMobile>();

        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT
                    COALESCE(Sexo, ''),
                    COALESCE(Servicio, ''),
                    COALESCE(Nombramiento, ''),
                    COALESCE(Solicitudes, ''),
                    COALESCE(Privilegios, ''),
                    COALESCE(FechaBautismo, ''),
                    COALESCE(FechaNacimiento, '')
                FROM Personas
                WHERE MesHistorico IS NULL
                  AND (FechaEliminacion IS NULL
                       OR TRIM(FechaEliminacion) = '');
                """;

            using var reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                lista.Add(new PersonaEstadisticaMobile
                {
                    Sexo = LeerTexto(reader, 0),
                    Servicio = LeerTexto(reader, 1),
                    Nombramiento = LeerTexto(reader, 2),
                    Solicitudes = LeerTexto(reader, 3),
                    Privilegios = LeerTexto(reader, 4),
                    FechaBautismo = LeerTexto(reader, 5),
                    FechaNacimiento = LeerTexto(reader, 6)
                });
            }
        }
        catch
        {
        }

        return lista;
    }


    /// <summary>Foto (base64) de una persona, para cargarla bajo demanda.</summary>
    public static string ObtenerFotoPersonaMobile(int id)
    {
        if (id <= 0)
        {
            return "";
        }

        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(Foto, '') FROM Personas WHERE Id = $id LIMIT 1;";

            cmd.Parameters.AddWithValue("$id", id);

            return cmd.ExecuteScalar()?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
