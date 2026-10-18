using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// DONACIONES (Android)
// ============================================================================
//
// Misma tabla y mismas columnas que usa SCCosmos (WPF) para que la
// sincronización de sccosmos.db transporte las donaciones igual que el
// resto de la información.

public static partial class DatabaseHelper
{
    public const string MedioEfectivo =
        "Efectivo";

    public const string MedioBizumTransferencia =
        "Bizum/Transferencia";

    public const string DonanteDesconocido =
        "Desconocido";


    public static void AsegurarTablaDonacionesMobile()
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Donaciones (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Fecha TEXT,
                PersonaId INTEGER DEFAULT 0,
                NombrePersona TEXT,
                CongregacionCodigo TEXT,
                CongregacionNombre TEXT,
                Cantidad REAL DEFAULT 0,
                MedioPago TEXT,
                Notas TEXT,
                FechaCreacion TEXT
            );
            """;

        cmd.ExecuteNonQuery();
    }


    public static List<DonacionMobile> ObtenerDonacionesMobile(
        string anio = "",
        string mes = "",
        string congregacionCodigo = "",
        string personaTexto = "",
        string medioPago = "")
    {
        var resultado =
            new List<DonacionMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "Donaciones"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        var condiciones =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(anio))
        {
            condiciones.Add(
                "substr(Fecha, 1, 4) = $anio");

            cmd.Parameters.AddWithValue(
                "$anio",
                anio.Trim());
        }

        if (!string.IsNullOrWhiteSpace(mes))
        {
            condiciones.Add(
                "substr(Fecha, 6, 2) = $mes");

            cmd.Parameters.AddWithValue(
                "$mes",
                mes.Trim().PadLeft(2, '0'));
        }

        if (!string.IsNullOrWhiteSpace(
                congregacionCodigo))
        {
            condiciones.Add(
                "IFNULL(CongregacionCodigo, '') = $congre");

            cmd.Parameters.AddWithValue(
                "$congre",
                congregacionCodigo.Trim());
        }

        if (!string.IsNullOrWhiteSpace(personaTexto))
        {
            condiciones.Add(
                "lower(IFNULL(NombrePersona, '')) LIKE $persona");

            cmd.Parameters.AddWithValue(
                "$persona",
                "%" + personaTexto.Trim().ToLowerInvariant() + "%");
        }

        if (!string.IsNullOrWhiteSpace(medioPago))
        {
            condiciones.Add(
                "IFNULL(MedioPago, '') = $medio");

            cmd.Parameters.AddWithValue(
                "$medio",
                medioPago.Trim());
        }

        cmd.CommandText =
            """
            SELECT
                Id,
                Fecha,
                PersonaId,
                NombrePersona,
                CongregacionCodigo,
                CongregacionNombre,
                Cantidad,
                MedioPago,
                Notas,
                FechaCreacion
            FROM Donaciones
            """
            + " "
            + (condiciones.Count > 0
                ? "WHERE " + string.Join(" AND ", condiciones) + " "
                : "")
            + "ORDER BY Fecha DESC, Id DESC;";

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new DonacionMobile
                {
                    Id = LeerIntAsamblea(reader, 0),
                    Fecha = LeerTexto(reader, 1),
                    PersonaId = LeerIntAsamblea(reader, 2),
                    NombrePersona = LeerTexto(reader, 3),
                    CongregacionCodigo = LeerTexto(reader, 4),
                    CongregacionNombre = LeerTexto(reader, 5),
                    Cantidad = LeerDoubleDonacion(reader, 6),
                    MedioPago = LeerTexto(reader, 7),
                    Notas = LeerTexto(reader, 8),
                    FechaCreacion = LeerTexto(reader, 9)
                });
        }

        return resultado;
    }


    public static int GuardarDonacionMobile(
        DonacionMobile donacion)
    {
        if (donacion is null)
        {
            return 0;
        }

        AnotarActividadMobile(
            "Rendimientos",
            (donacion.Id > 0 ? "Editó" : "Añadió") +
            " un registro de " +
            donacion.Cantidad.ToString("N2") + " €" +
            (string.IsNullOrWhiteSpace(donacion.NombrePersona)
                ? ""
                : " de «" + donacion.NombrePersona + "»"));

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        if (donacion.Id > 0)
        {
            cmd.CommandText =
                """
                UPDATE Donaciones SET
                    Fecha = $fecha,
                    PersonaId = $personaId,
                    NombrePersona = $nombre,
                    CongregacionCodigo = $congreCodigo,
                    CongregacionNombre = $congreNombre,
                    Cantidad = $cantidad,
                    MedioPago = $medio,
                    Notas = $notas
                WHERE Id = $id;
                """;

            cmd.Parameters.AddWithValue(
                "$id",
                donacion.Id);
        }
        else
        {
            cmd.CommandText =
                """
                INSERT INTO Donaciones
                    (Fecha, PersonaId, NombrePersona,
                     CongregacionCodigo, CongregacionNombre,
                     Cantidad, MedioPago, Notas, FechaCreacion)
                VALUES
                    ($fecha, $personaId, $nombre,
                     $congreCodigo, $congreNombre,
                     $cantidad, $medio, $notas, $creacion);
                """;

            cmd.Parameters.AddWithValue(
                "$creacion",
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));
        }

        cmd.Parameters.AddWithValue(
            "$fecha",
            donacion.Fecha ?? "");

        cmd.Parameters.AddWithValue(
            "$personaId",
            donacion.PersonaId);

        cmd.Parameters.AddWithValue(
            "$nombre",
            donacion.NombrePersona ?? "");

        cmd.Parameters.AddWithValue(
            "$congreCodigo",
            donacion.CongregacionCodigo ?? "");

        cmd.Parameters.AddWithValue(
            "$congreNombre",
            donacion.CongregacionNombre ?? "");

        cmd.Parameters.AddWithValue(
            "$cantidad",
            donacion.Cantidad);

        cmd.Parameters.AddWithValue(
            "$medio",
            donacion.MedioPago ?? "");

        cmd.Parameters.AddWithValue(
            "$notas",
            donacion.Notas ?? "");

        cmd.ExecuteNonQuery();

        if (donacion.Id <= 0)
        {
            using SqliteCommand cmdId =
                connection.CreateCommand();

            cmdId.CommandText =
                "SELECT last_insert_rowid();";

            donacion.Id =
                Convert.ToInt32(
                    cmdId.ExecuteScalar() ?? 0);
        }

        return donacion.Id;
    }


    public static void EliminarDonacionMobile(
        int id)
    {
        if (id <= 0)
        {
            return;
        }

        AnotarActividadMobile(
            "Rendimientos",
            "Eliminó un registro de " +
            DescripcionDonacionPorIdMobile(id));

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM Donaciones WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    private static double LeerDoubleDonacion(
        SqliteDataReader reader,
        int indice)
    {
        if (indice < 0
            ||
            indice >= reader.FieldCount
            ||
            reader.IsDBNull(indice))
        {
            return 0;
        }

        try
        {
            return reader.GetDouble(indice);
        }
        catch
        {
            return 0;
        }
    }
}
