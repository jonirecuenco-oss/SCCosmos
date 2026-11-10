using System.Globalization;

using Microsoft.Data.Sqlite;

using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// TIEMPO: DÓNDE SE ESTÁ CADA DÍA (Android)
// ============================================================================

public static partial class DatabaseHelper
{
    /// <summary>
    /// La visita de la semana que empieza ese martes: congregación, su
    /// municipio y provincia, y las coordenadas del salón. Null si esa semana
    /// no hay visita.
    /// </summary>
    public static (string Congregacion, string Municipio, string Provincia, double Latitud, double Longitud)?
        ObtenerVisitaParaTiempo(DateTime martes)
    {
        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT s.Congregacion,
                       COALESCE(c.Municipio, ''),
                       COALESCE(c.Provincia, ''),
                       COALESCE(c.Latitud, 0),
                       COALESCE(c.Longitud, 0)
                FROM Semanas s
                LEFT JOIN Congregaciones c
                    ON c.Nombre = s.Congregacion
                WHERE date(s.FechaMartes) = date($martes)
                  AND s.Actividad LIKE '%isita%'
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue(
                "$martes",
                martes.ToString("yyyy-MM-dd"));

            using var reader =
                cmd.ExecuteReader();

            if (!reader.Read()
                || reader.IsDBNull(0))
            {
                return null;
            }

            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToDouble(reader.GetValue(3)),
                Convert.ToDouble(reader.GetValue(4)));
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Las congregaciones del circuito con su municipio, provincia y las
    /// coordenadas del salón (para los avisos rojos de AEMET).
    /// </summary>
    public static List<(string Codigo, string Nombre, string Municipio, string Provincia, double Latitud, double Longitud)>
        ObtenerCongregacionesParaTiempo()
    {
        var lista = new List<(string Codigo, string Nombre, string Municipio, string Provincia, double Latitud, double Longitud)>();

        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT COALESCE(Codigo, ''),
                       Nombre,
                       COALESCE(Municipio, ''),
                       COALESCE(Provincia, ''),
                       COALESCE(Latitud, 0),
                       COALESCE(Longitud, 0)
                FROM Congregaciones
                WHERE COALESCE(Nombre, '') <> ''
                ORDER BY Nombre;
                """;

            using var reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                lista.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
                    Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture)));
            }
        }
        catch
        {
        }

        return lista;
    }


    // ------------------------------------------------------------------------
    // PUEBLOS DEL TERRITORIO (en Configuracion: se sincroniza con Windows)
    // ------------------------------------------------------------------------

    public static List<PuebloTerritorio> ObtenerPueblosTerritorio(string codigoCongregacion) =>
        PueblosTerritorio.Leer(
            ObtenerConfiguracion(PueblosTerritorio.Clave(codigoCongregacion)));


    public static void GuardarPueblosTerritorio(
        string codigoCongregacion,
        IEnumerable<PuebloTerritorio> pueblos) =>
        GuardarConfiguracion(
            PueblosTerritorio.Clave(codigoCongregacion),
            PueblosTerritorio.Escribir(pueblos));


    /// <summary>Una dirección por publicador de la congregación (para sugerir sus pueblos).</summary>
    public static List<string> ObtenerDireccionesCongregacion(string codigoCongregacion)
    {
        var lista = new List<string>();

        try
        {
            using var connection =
                new SqliteConnection(ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT MAX(Direccion)
                FROM Personas
                WHERE CongregacionCodigo = $codigo
                  AND COALESCE(Direccion, '') <> ''
                GROUP BY COALESCE(NULLIF(PersonaRaizId, 0), Id);
                """;

            cmd.Parameters.AddWithValue("$codigo", codigoCongregacion);

            using var reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                lista.Add(reader.GetString(0));
            }
        }
        catch
        {
        }

        return lista;
    }
}
