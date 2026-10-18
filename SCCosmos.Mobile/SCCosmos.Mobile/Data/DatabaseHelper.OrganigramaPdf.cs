using System.Globalization;

using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;

/// <summary>
/// Datos de una persona para el PDF del organigrama (los mismos que usa la
/// app de Windows: nombre común o formateado, año de nacimiento, teléfono,
/// correo y congregación).
/// </summary>
public sealed class InfoHermanoOrganigramaMobile
{
    public string NombreComun { get; set; } = "";

    public string NombreFormateado { get; set; } = "";

    public string AnioNacimiento { get; set; } = "";

    public string Telefono { get; set; } = "";

    public string EmailJW { get; set; } = "";

    public string Email { get; set; } = "";

    public string Congregacion { get; set; } = "";
}


public static partial class DatabaseHelper
{
    public static InfoHermanoOrganigramaMobile
        ObtenerInfoHermanoOrganigramaMobile(
            int id)
    {
        var info =
            new InfoHermanoOrganigramaMobile();

        if (id <= 0)
        {
            return info;
        }

        try
        {
            using var connection =
                new SqliteConnection(
                    ConnectionString);

            connection.Open();

            using var cmd =
                connection.CreateCommand();

            cmd.CommandText =
                """
                SELECT
                    p.Nombre,
                    p.NombreComun,
                    p.FechaNacimiento,
                    p.Telefono,
                    p.Email,
                    p.EmailJW,
                    p.CongregacionCodigo,
                    c.Nombre
                FROM Personas p
                LEFT JOIN Congregaciones c
                  ON c.Codigo = p.CongregacionCodigo
                WHERE p.Id = $id
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            if (!reader.Read())
            {
                return info;
            }

            info.NombreFormateado =
                LeerTexto(
                    reader,
                    0);

            info.NombreComun =
                LeerTexto(
                    reader,
                    1);

            info.AnioNacimiento =
                AnioDeFecha(
                    LeerTexto(
                        reader,
                        2));

            info.Telefono =
                LeerTexto(
                    reader,
                    3);

            info.Email =
                LeerTexto(
                    reader,
                    4);

            info.EmailJW =
                LeerTexto(
                    reader,
                    5);

            info.Congregacion =
                LeerTexto(
                    reader,
                    7);
        }
        catch
        {
            // Sin datos: la caja se dibuja vacía.
        }

        return info;
    }


    private static string AnioDeFecha(
        string fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
        {
            return "";
        }

        for (int i = 0;
             i + 4 <= fecha.Length;
             i++)
        {
            string trozo =
                fecha.Substring(
                    i,
                    4);

            if (trozo.All(char.IsDigit)
                && int.TryParse(
                    trozo,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out int anio)
                && anio >= 1900
                && anio <= 2100)
            {
                return trozo;
            }
        }

        return "";
    }
}
