using System;
using Microsoft.Data.Sqlite;

using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Data;

// =============================================================
// REGISTRO DE ACTIVIDAD (MÓVIL)
//
// Anota en lenguaje natural todo lo que se hace en la app, con
// la fecha, el dispositivo y el perfil. La tabla viaja con el
// motor de sincronización, así que cualquier dispositivo puede
// leer el historial completo.
//
// Para funciones nuevas, basta con llamar a:
//     DatabaseHelper.AnotarActividadMobile("Categoría", "frase…");
// =============================================================

public static partial class DatabaseHelper
{
    public static void AsegurarTablaRegistroActividadMobile()
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            AsegurarTablaRegistroActividad(
                conexion);
        }
        catch
        {
        }
    }


    public static void AsegurarTablaRegistroActividad(
        SqliteConnection connection)
    {
        using var cmd =
            connection.CreateCommand();

        cmd.CommandText =
            @"CREATE TABLE IF NOT EXISTS RegistroActividad (
                Id TEXT PRIMARY KEY,
                FechaHora TEXT NOT NULL,
                Categoria TEXT DEFAULT '',
                Dispositivo TEXT DEFAULT '',
                Perfil TEXT DEFAULT '',
                Descripcion TEXT NOT NULL
            );";

        cmd.ExecuteNonQuery();
    }


    /// <summary>
    /// Anota una acción en el historial (lenguaje natural).
    /// </summary>
    public static void AnotarActividadMobile(
        string categoria,
        string descripcion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    descripcion))
            {
                return;
            }

            string dispositivo =
                "";

            try
            {
                dispositivo =
                    SincronizacionMobile
                        .NombreDispositivo;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(
                    dispositivo))
            {
                dispositivo =
                    "Móvil " +
                    (Microsoft.Maui.Devices
                        .DeviceInfo.Current
                        ?.Model
                     ?? "");
            }

            string perfil;

            try
            {
                perfil =
                    PerfilAccesoService.EsHombre
                        ? "SC"
                        : "Esposa";
            }
            catch
            {
                perfil =
                    "SC";
            }

            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            AsegurarTablaRegistroActividad(
                conexion);

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                @"INSERT INTO RegistroActividad
                    (Id, FechaHora, Categoria, Dispositivo,
                     Perfil, Descripcion)
                  VALUES
                    ($id, $fecha, $categoria, $dispositivo,
                     $perfil, $descripcion);";

            cmd.Parameters.AddWithValue(
                "$id",
                Guid.NewGuid()
                    .ToString("N"));

            cmd.Parameters.AddWithValue(
                "$fecha",
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

            cmd.Parameters.AddWithValue(
                "$categoria",
                categoria ?? "");

            cmd.Parameters.AddWithValue(
                "$dispositivo",
                dispositivo);

            cmd.Parameters.AddWithValue(
                "$perfil",
                perfil);

            cmd.Parameters.AddWithValue(
                "$descripcion",
                descripcion);

            cmd.ExecuteNonQuery();
        }
        catch
        {
            // El historial nunca debe tumbar una acción.
        }
    }


    // =====================================================
    // AYUDAS PARA COMPONER FRASES
    // =====================================================

    public static string MostrarNombreMobile(
        Persona p)
    {
        if (p is null)
        {
            return "";
        }

        return !string.IsNullOrWhiteSpace(
                p.NombreComun)
            ? p.NombreComun
            : (p.Nombre ?? "");
    }


    public static string NombrePersonaPorIdMobile(
        int id)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                @"SELECT CASE
                    WHEN COALESCE(NombreComun, '') <> ''
                    THEN NombreComun
                    ELSE COALESCE(Nombre, '')
                  END
                  FROM Personas
                  WHERE Id = $id
                  LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            return cmd.ExecuteScalar()?
                .ToString()
                ?? "";
        }
        catch
        {
            return "";
        }
    }


    private static string NombreTareaPorIdMobile(
        int id)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(Titulo, '') FROM Tareas WHERE Id = $id LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            return cmd.ExecuteScalar()?
                .ToString()
                ?? "";
        }
        catch
        {
            return "";
        }
    }


    private static string DescripcionDonacionPorIdMobile(
        int id)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(NombrePersona, '') || '|' || COALESCE(Cantidad, 0) FROM Donaciones WHERE Id = $id LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$id",
                id);

            string texto =
                cmd.ExecuteScalar()?
                    .ToString()
                ?? "";

            string[] partes =
                texto.Split('|');

            string nombre =
                partes.Length > 0
                    ? partes[0]
                    : "";

            string cantidad =
                partes.Length > 1
                    ? partes[1]
                    : "";

            return string.IsNullOrWhiteSpace(
                    nombre)
                ? $"{cantidad} €"
                : $"{cantidad} € de «{nombre}»";
        }
        catch
        {
            return "";
        }
    }


    private static string FechaSemanaPorIdMobile(
        int semanaId)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT COALESCE(FechaMartes, '') FROM Semanas WHERE Id = $id LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$id",
                semanaId);

            string fecha =
                cmd.ExecuteScalar()?
                    .ToString()
                ?? "";

            if (DateTime.TryParse(
                    fecha,
                    out DateTime valor))
            {
                return valor.ToString(
                    "dd/MM/yyyy");
            }

            return fecha;
        }
        catch
        {
            return "";
        }
    }


    // =====================================================
    // LEER EL HISTORIAL (PÁGINA ACTIVIDAD)
    // =====================================================

    public static List<EntradaActividadMobile>
        ObtenerActividadMobile(
            string categoria,
            int limite)
    {
        var lista =
            new List<EntradaActividadMobile>();

        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            AsegurarTablaRegistroActividad(
                conexion);

            using var cmd =
                conexion.CreateCommand();

            string filtro =
                string.IsNullOrWhiteSpace(
                        categoria)
                ||
                categoria == "Todas"
                    ? ""
                    : "WHERE Categoria = $categoria";

            cmd.CommandText =
                $@"SELECT Id, FechaHora, Categoria,
                          Dispositivo, Perfil, Descripcion
                   FROM RegistroActividad
                   {filtro}
                   ORDER BY FechaHora DESC, Id DESC
                   LIMIT {Math.Max(1, limite)};";

            if (filtro.Length > 0)
            {
                cmd.Parameters.AddWithValue(
                    "$categoria",
                    categoria);
            }

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                lista.Add(
                    new EntradaActividadMobile
                    {
                        Id =
                            lector.GetString(0),

                        FechaHora =
                            lector.GetString(1),

                        Categoria =
                            lector.IsDBNull(2)
                                ? ""
                                : lector.GetString(2),

                        Dispositivo =
                            lector.IsDBNull(3)
                                ? ""
                                : lector.GetString(3),

                        Perfil =
                            lector.IsDBNull(4)
                                ? ""
                                : lector.GetString(4),

                        Descripcion =
                            lector.IsDBNull(5)
                                ? ""
                                : lector.GetString(5)
                    });
            }
        }
        catch
        {
        }

        return lista;
    }


    public static List<string>
        ObtenerCategoriasActividadMobile()
    {
        var lista =
            new List<string>
            {
                "Todas"
            };

        try
        {
            using var conexion =
                new SqliteConnection(
                    ConnectionString);

            conexion.Open();

            AsegurarTablaRegistroActividad(
                conexion);

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT DISTINCT Categoria FROM RegistroActividad WHERE Categoria <> '' ORDER BY Categoria;";

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                lista.Add(
                    lector.GetString(0));
            }
        }
        catch
        {
        }

        return lista;
    }
}


public sealed class EntradaActividadMobile
{
    public string Id { get; set; } = "";

    public string FechaHora { get; set; } = "";

    public string Categoria { get; set; } = "";

    public string Dispositivo { get; set; } = "";

    public string Perfil { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public string FechaVisual
    {
        get
        {
            return DateTime.TryParse(
                    FechaHora,
                    out DateTime valor)
                ? valor.ToString(
                    "dd/MM/yyyy HH:mm")
                : FechaHora;
        }
    }

    public string Cabecera =>
        $"{FechaVisual} · {Dispositivo}" +
        (string.IsNullOrWhiteSpace(
             Perfil)
            ? ""
            : $" ({Perfil})");
}
