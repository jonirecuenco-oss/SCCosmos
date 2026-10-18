using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    /// <summary>
    /// Donaciones recibidas (personas del circuito o donantes desconocidos).
    /// La tabla es compartida con SCCosmos Mobile: mismas columnas y mismos
    /// valores, de modo que la sincronización de la base de datos funciona
    /// igual que con el resto de módulos.
    /// </summary>
    public static partial class DatabaseHelper
    {
        public const string MedioEfectivo =
            "Efectivo";

        public const string MedioBizumTransferencia =
            "Bizum/Transferencia";

        public const string DonanteDesconocido =
            "Desconocido";

        public class DonacionBD
        {
            public long Id { get; set; }

            public string Fecha { get; set; } = "";

            public int PersonaId { get; set; }

            public string NombrePersona { get; set; } = "";

            public string CongregacionCodigo { get; set; } = "";

            public string CongregacionNombre { get; set; } = "";

            public double Cantidad { get; set; }

            public string MedioPago { get; set; } = "";

            public string Notas { get; set; } = "";

            public string FechaCreacion { get; set; } = "";

            // ------- columnas calculadas para la tabla -------

            public string FechaVisual =>
                string.IsNullOrWhiteSpace(Fecha)
                    ? ""
                    : Fecha;

            public string CantidadVisual =>
                Cantidad.ToString(
                    "N2",
                    CultureInfo.GetCultureInfo("es-ES")) + " €";

            public string PersonaVisual =>
                string.IsNullOrWhiteSpace(NombrePersona)
                    ? DonanteDesconocido
                    : NombrePersona;

            public string CongregacionVisual =>
                !string.IsNullOrWhiteSpace(CongregacionNombre)
                    ? CongregacionNombre
                    : (string.IsNullOrWhiteSpace(CongregacionCodigo)
                        ? ""
                        : CongregacionCodigo);

            public bool EsDesconocido =>
                PersonaId <= 0;
        }


        /// <summary>
        /// Crea la tabla de donaciones si no existe. Igual que el resto de
        /// tablas opcionales, se llama al abrir la página que la usa.
        /// </summary>
        public static void AsegurarTablaDonaciones()
        {
            try
            {
                using (var connection =
                       new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    var cmd =
                        connection.CreateCommand();

                    cmd.CommandText =
                        @"CREATE TABLE IF NOT EXISTS Donaciones (
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
                        );";

                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }


        /// <summary>
        /// Donaciones filtradas por año, mes, congregación, persona y medio
        /// de pago. Los filtros vacíos no se aplican.
        /// </summary>
        public static List<DonacionBD> ObtenerDonaciones(
            string anio = "",
            string mes = "",
            string congregacionCodigo = "",
            string personaTexto = "",
            string medioPago = "")
        {
            var resultado =
                new List<DonacionBD>();

            try
            {
                using (var connection =
                       new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    var cmd =
                        connection.CreateCommand();

                    var condiciones =
                        new List<string>();

                    if (!string.IsNullOrWhiteSpace(anio))
                    {
                        condiciones.Add(
                            "substr(Fecha, 1, 4) = @anio");

                        cmd.Parameters.AddWithValue(
                            "@anio",
                            anio.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(mes))
                    {
                        condiciones.Add(
                            "substr(Fecha, 6, 2) = @mes");

                        cmd.Parameters.AddWithValue(
                            "@mes",
                            mes.Trim().PadLeft(2, '0'));
                    }

                    if (!string.IsNullOrWhiteSpace(
                            congregacionCodigo))
                    {
                        condiciones.Add(
                            "IFNULL(CongregacionCodigo, '') = @congre");

                        cmd.Parameters.AddWithValue(
                            "@congre",
                            congregacionCodigo.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(
                            personaTexto))
                    {
                        condiciones.Add(
                            "lower(IFNULL(NombrePersona, '')) LIKE @persona");

                        cmd.Parameters.AddWithValue(
                            "@persona",
                            "%" + personaTexto.Trim().ToLower() + "%");
                    }

                    if (!string.IsNullOrWhiteSpace(medioPago))
                    {
                        condiciones.Add(
                            "IFNULL(MedioPago, '') = @medio");

                        cmd.Parameters.AddWithValue(
                            "@medio",
                            medioPago.Trim());
                    }

                    cmd.CommandText =
                        "SELECT Id, Fecha, PersonaId, NombrePersona, " +
                        "CongregacionCodigo, CongregacionNombre, Cantidad, " +
                        "MedioPago, Notas, FechaCreacion " +
                        "FROM Donaciones " +
                        (condiciones.Count > 0
                            ? "WHERE " + string.Join(" AND ", condiciones) + " "
                            : "") +
                        "ORDER BY Fecha DESC, Id DESC;";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            resultado.Add(
                                LeerDonacion(reader));
                        }
                    }
                }
            }
            catch
            {
            }

            return resultado;
        }


        public static double CalcularTotalDonaciones(
            IEnumerable<DonacionBD> donaciones)
        {
            return donaciones?.Sum(d => d.Cantidad) ?? 0;
        }


        public static long GuardarDonacion(
            DonacionBD donacion)
        {
            if (donacion == null)
            {
                return 0;
            }

            AnotarActividad(
                "Rendimientos",
                (donacion.Id > 0 ? "Editó" : "Añadió") +
                " un registro de " +
                donacion.Cantidad.ToString("N2") + " €" +
                (string.IsNullOrWhiteSpace(donacion.NombrePersona)
                    ? ""
                    : " de «" + donacion.NombrePersona + "»"));

            try
            {
                using (var connection =
                       new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    var cmd =
                        connection.CreateCommand();

                    if (donacion.Id > 0)
                    {
                        cmd.CommandText =
                            @"UPDATE Donaciones SET
                                Fecha = @fecha,
                                PersonaId = @personaId,
                                NombrePersona = @nombre,
                                CongregacionCodigo = @congreCodigo,
                                CongregacionNombre = @congreNombre,
                                Cantidad = @cantidad,
                                MedioPago = @medio,
                                Notas = @notas
                              WHERE Id = @id;";

                        cmd.Parameters.AddWithValue(
                            "@id",
                            donacion.Id);
                    }
                    else
                    {
                        cmd.CommandText =
                            @"INSERT INTO Donaciones
                                (Fecha, PersonaId, NombrePersona,
                                 CongregacionCodigo, CongregacionNombre,
                                 Cantidad, MedioPago, Notas, FechaCreacion)
                              VALUES
                                (@fecha, @personaId, @nombre,
                                 @congreCodigo, @congreNombre,
                                 @cantidad, @medio, @notas, @creacion);";
                    }

                    cmd.Parameters.AddWithValue(
                        "@fecha",
                        donacion.Fecha ?? "");

                    cmd.Parameters.AddWithValue(
                        "@personaId",
                        donacion.PersonaId);

                    cmd.Parameters.AddWithValue(
                        "@nombre",
                        donacion.NombrePersona ?? "");

                    cmd.Parameters.AddWithValue(
                        "@congreCodigo",
                        donacion.CongregacionCodigo ?? "");

                    cmd.Parameters.AddWithValue(
                        "@congreNombre",
                        donacion.CongregacionNombre ?? "");

                    cmd.Parameters.AddWithValue(
                        "@cantidad",
                        donacion.Cantidad);

                    cmd.Parameters.AddWithValue(
                        "@medio",
                        donacion.MedioPago ?? "");

                    cmd.Parameters.AddWithValue(
                        "@notas",
                        donacion.Notas ?? "");

                    if (donacion.Id <= 0)
                    {
                        cmd.Parameters.AddWithValue(
                            "@creacion",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    }

                    cmd.ExecuteNonQuery();

                    if (donacion.Id <= 0)
                    {
                        cmd.CommandText =
                            "SELECT last_insert_rowid();";

                        cmd.Parameters.Clear();

                        donacion.Id =
                            Convert.ToInt64(
                                cmd.ExecuteScalar() ?? 0L);
                    }
                }
            }
            catch
            {
            }

            return donacion.Id;
        }


        public static void EliminarDonacion(long id)
        {
            AnotarActividad(
                "Rendimientos",
                "Eliminó un registro de " + DescripcionDonacionPorId(id));

            if (id <= 0)
            {
                return;
            }

            try
            {
                using (var connection =
                       new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    var cmd =
                        connection.CreateCommand();

                    cmd.CommandText =
                        "DELETE FROM Donaciones WHERE Id = @id;";

                    cmd.Parameters.AddWithValue(
                        "@id",
                        id);

                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }


        private static DonacionBD LeerDonacion(
            SqliteDataReader reader)
        {
            return new DonacionBD
            {
                Id = reader.IsDBNull(0)
                    ? 0
                    : reader.GetInt64(0),

                Fecha = reader.IsDBNull(1)
                    ? ""
                    : reader.GetString(1),

                PersonaId = reader.IsDBNull(2)
                    ? 0
                    : reader.GetInt32(2),

                NombrePersona = reader.IsDBNull(3)
                    ? ""
                    : reader.GetString(3),

                CongregacionCodigo = reader.IsDBNull(4)
                    ? ""
                    : reader.GetString(4),

                CongregacionNombre = reader.IsDBNull(5)
                    ? ""
                    : reader.GetString(5),

                Cantidad = reader.IsDBNull(6)
                    ? 0
                    : reader.GetDouble(6),

                MedioPago = reader.IsDBNull(7)
                    ? ""
                    : reader.GetString(7),

                Notas = reader.IsDBNull(8)
                    ? ""
                    : reader.GetString(8),

                FechaCreacion = reader.IsDBNull(9)
                    ? ""
                    : reader.GetString(9)
            };
        }
    }
}
