using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// CIRCUITO (Android): secciones y congregaciones
// ============================================================================
//
// CRUD sobre las tablas compartidas SeccionesCircuito y Congregaciones.
// Solo se usan las columnas existentes de WPF; no se cambia el esquema.

public static partial class DatabaseHelper
{
    // ------------------------------------------------------------------
    // SECCIONES
    // ------------------------------------------------------------------

    public static List<SeccionCircuitoMobile>
        ObtenerSeccionesCircuitoMobile()
    {
        var resultado =
            new List<SeccionCircuitoMobile>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "SeccionesCircuito"))
        {
            return resultado;
        }

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "SELECT Id, Nombre FROM SeccionesCircuito ORDER BY Nombre;";

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                new SeccionCircuitoMobile
                {
                    Id =
                        reader.IsDBNull(0)
                            ? 0
                            : reader.GetInt32(0),

                    Nombre =
                        reader.IsDBNull(1)
                            ? ""
                            : reader.GetString(1)
                });
        }

        return resultado;
    }


    public static int InsertarSeccionCircuitoMobile(
        string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return 0;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            INSERT INTO SeccionesCircuito (Nombre)
            VALUES ($nombre);
            """;

        cmd.Parameters.AddWithValue(
            "$nombre",
            nombre.Trim());

        cmd.ExecuteNonQuery();

        using SqliteCommand idCmd =
            connection.CreateCommand();

        idCmd.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt32(
            idCmd.ExecuteScalar() ?? 0);
    }


    public static void EliminarSeccionCircuitoMobile(
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
            "DELETE FROM SeccionesCircuito WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    // ------------------------------------------------------------------
    // CONGREGACIONES
    // ------------------------------------------------------------------

    public static bool ExisteCodigoCongregacionMobile(
        string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return false;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "SELECT COUNT(*) FROM Congregaciones WHERE Codigo = $codigo;";

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo.Trim());

        return Convert.ToInt32(
                   cmd.ExecuteScalar() ?? 0)
               > 0;
    }


    public static Congregacion?
        ObtenerCongregacionPorCodigoMobile(
            string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return null;
        }

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Codigo, Nombre, Email, SeccionId, Provincia,
                   Municipio, DireccionSR, DiaEntreSemana,
                   HoraEntreSemana, DiaFinSemana, HoraFinSemana,
                   Latitud, Longitud, Capacidad, Poblacion,
                   (SELECT s.Nombre FROM SeccionesCircuito s
                    WHERE s.Id = Congregaciones.SeccionId) AS NombreSeccion
            FROM Congregaciones
            WHERE Codigo = $codigo
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo.Trim());

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return LeerCongregacion(
            reader);
    }


    public static List<Congregacion>
        ObtenerCongregacionesCompletasMobile()
    {
        var resultado =
            new List<Congregacion>();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT Codigo, Nombre, Email, SeccionId, Provincia,
                   Municipio, DireccionSR, DiaEntreSemana,
                   HoraEntreSemana, DiaFinSemana, HoraFinSemana,
                   Latitud, Longitud, Capacidad, Poblacion,
                   (SELECT s.Nombre FROM SeccionesCircuito s
                    WHERE s.Id = Congregaciones.SeccionId) AS NombreSeccion
            FROM Congregaciones
            ORDER BY Nombre;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            resultado.Add(
                LeerCongregacion(
                    reader));
        }

        return resultado;
    }


    public static void InsertarCongregacionMobile(
        Congregacion cong)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            INSERT INTO Congregaciones
                (Codigo, Nombre, Email, SeccionId, Provincia, Municipio,
                 DireccionSR, DiaEntreSemana, HoraEntreSemana,
                 DiaFinSemana, HoraFinSemana, Latitud, Longitud,
                 Capacidad, Poblacion)
            VALUES
                ($codigo, $nombre, $email, $seccion, $provincia, $municipio,
                 $direccion, $diaEntre, $horaEntre,
                 $diaFin, $horaFin, $latitud, $longitud,
                 $capacidad, $poblacion);
            """;

        AgregarParametrosCongregacion(
            cmd,
            cong,
            incluirCodigo: true);

        cmd.ExecuteNonQuery();
    }


    public static void ActualizarCongregacionMobile(
        Congregacion cong)
    {
        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            UPDATE Congregaciones
            SET Nombre = $nombre,
                Email = $email,
                SeccionId = $seccion,
                Provincia = $provincia,
                Municipio = $municipio,
                DireccionSR = $direccion,
                DiaEntreSemana = $diaEntre,
                HoraEntreSemana = $horaEntre,
                DiaFinSemana = $diaFin,
                HoraFinSemana = $horaFin,
                Capacidad = $capacidad,
                Poblacion = $poblacion
            WHERE Codigo = $codigo;
            """;

        AgregarParametrosCongregacion(
            cmd,
            cong,
            incluirCodigo: true);

        cmd.ExecuteNonQuery();
    }


    public static void EliminarCongregacionMobile(
        string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
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
            "DELETE FROM Congregaciones WHERE Codigo = $codigo;";

        cmd.Parameters.AddWithValue(
            "$codigo",
            codigo.Trim());

        cmd.ExecuteNonQuery();
    }


    private static Congregacion LeerCongregacion(
        SqliteDataReader reader)
    {
        return new Congregacion
        {
            Codigo =
                reader.IsDBNull(0) ? "" : reader.GetString(0),

            Nombre =
                reader.IsDBNull(1) ? "" : reader.GetString(1),

            Email =
                reader.IsDBNull(2) ? "" : reader.GetString(2),

            SeccionId =
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),

            Provincia =
                reader.IsDBNull(4) ? "" : reader.GetString(4),

            Municipio =
                reader.IsDBNull(5) ? "" : reader.GetString(5),

            DireccionSR =
                reader.IsDBNull(6) ? "" : reader.GetString(6),

            DiaEntreSemana =
                reader.IsDBNull(7) ? "" : reader.GetString(7),

            HoraEntreSemana =
                reader.IsDBNull(8) ? "" : reader.GetString(8),

            DiaFinSemana =
                reader.IsDBNull(9) ? "" : reader.GetString(9),

            HoraFinSemana =
                reader.IsDBNull(10) ? "" : reader.GetString(10),

            Latitud =
                reader.IsDBNull(11)
                    ? 0
                    : Convert.ToDouble(reader.GetValue(11)),

            Longitud =
                reader.IsDBNull(12)
                    ? 0
                    : Convert.ToDouble(reader.GetValue(12)),

            Capacidad =
                reader.IsDBNull(13) ? 0 : reader.GetInt32(13),

            Poblacion =
                reader.IsDBNull(14) ? 0 : reader.GetInt32(14),

            NombreSeccion =
                reader.IsDBNull(15) ? "" : reader.GetString(15)
        };
    }


    private static void AgregarParametrosCongregacion(
        SqliteCommand cmd,
        Congregacion cong,
        bool incluirCodigo)
    {
        if (incluirCodigo)
        {
            cmd.Parameters.AddWithValue(
                "$codigo",
                (cong.Codigo ?? "").Trim());
        }

        cmd.Parameters.AddWithValue(
            "$nombre",
            (cong.Nombre ?? "").Trim());

        cmd.Parameters.AddWithValue(
            "$email",
            cong.Email ?? "");

        cmd.Parameters.AddWithValue(
            "$seccion",
            cong.SeccionId);

        cmd.Parameters.AddWithValue(
            "$provincia",
            cong.Provincia ?? "");

        cmd.Parameters.AddWithValue(
            "$municipio",
            cong.Municipio ?? "");

        cmd.Parameters.AddWithValue(
            "$direccion",
            cong.DireccionSR ?? "");

        cmd.Parameters.AddWithValue(
            "$diaEntre",
            cong.DiaEntreSemana ?? "");

        cmd.Parameters.AddWithValue(
            "$horaEntre",
            cong.HoraEntreSemana ?? "");

        cmd.Parameters.AddWithValue(
            "$diaFin",
            cong.DiaFinSemana ?? "");

        cmd.Parameters.AddWithValue(
            "$horaFin",
            cong.HoraFinSemana ?? "");

        cmd.Parameters.AddWithValue(
            "$latitud",
            cong.Latitud);

        cmd.Parameters.AddWithValue(
            "$longitud",
            cong.Longitud);

        cmd.Parameters.AddWithValue(
            "$capacidad",
            cong.Capacidad);

        cmd.Parameters.AddWithValue(
            "$poblacion",
            cong.Poblacion);
    }
}


public sealed class SeccionCircuitoMobile
{
    public int Id { get; set; }

    public string Nombre { get; set; } = "";

    public override string ToString() =>
        Nombre;
}
