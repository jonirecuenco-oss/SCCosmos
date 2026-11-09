using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Dónde se va a estar cada día, para el tiempo de AEMET en Inicio: el
    /// salón de la congregación que se visita (de martes a domingo) o, si no,
    /// donde está el móvil del SC (el móvil lo deja en Configuracion, que se
    /// sincroniza). Lo de AEMET es común: SincroCompartido/TiempoAemet.cs.
    /// </summary>
    public static class TiempoPc
    {
        // La misma clave que usa el móvil (TiempoAemetService.ClaveUbicacionCompartida).
        private const string ClaveUbicacionMovil = "TiempoUbicacionMovil";

        static TiempoPc()
        {
            TiempoAemet.CarpetaCache =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos",
                    "tiempo");
        }


        public static LugarTiempo LugarParaDia(DateTime dia) =>
            LugarVisita(dia) ?? UbicacionMovil();


        private static LugarTiempo LugarVisita(DateTime dia)
        {
            // El lunes ya no se está en la congregación (la visita acaba el domingo).
            if (dia.DayOfWeek == DayOfWeek.Monday)
            {
                return null;
            }

            DateTime martes =
                dia.Date.AddDays(-(((int)dia.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7));

            try
            {
                using var conexion =
                    new SqliteConnection($"Data Source={DatabaseHelper.RutaBaseDatos};Pooling=True");

                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText = @"
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
                    LIMIT 1;";

                cmd.Parameters.AddWithValue(
                    "$martes",
                    martes.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                using var lector =
                    cmd.ExecuteReader();

                if (!lector.Read() || lector.IsDBNull(0))
                {
                    return null;
                }

                string municipio = lector.GetString(1);
                string ine = TiempoAemet.BuscarIne(municipio, lector.GetString(2));

                return ine == null
                    ? null
                    : new LugarTiempo(
                        ine,
                        TiempoAemet.NombreMunicipio(ine, municipio),
                        "Visita a " + lector.GetString(0),
                        Convert.ToDouble(lector.GetValue(3), CultureInfo.InvariantCulture),
                        Convert.ToDouble(lector.GetValue(4), CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }


        private static LugarTiempo UbicacionMovil()
        {
            string[] partes =
                (DatabaseHelper.ObtenerConfiguracion(ClaveUbicacionMovil, "") ?? "").Split('|');

            if (partes.Length < 4
                || !double.TryParse(partes[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
                || !double.TryParse(partes[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud))
            {
                return null;
            }

            return new LugarTiempo(
                partes[0],
                partes[1],
                "Ubicación del móvil",
                latitud,
                longitud);
        }
    }
}
