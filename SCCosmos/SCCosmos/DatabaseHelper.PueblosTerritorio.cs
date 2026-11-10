using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos
{
    // =========================================================
    // PUEBLOS DEL TERRITORIO DE CADA CONGREGACIÓN
    //
    // Además del pueblo del salón: su tiempo en Circuito y los
    // avisos rojos del móvil. Van en Configuracion, que se
    // sincroniza con el móvil (SincroCompartido/PueblosTerritorio.cs).
    // =========================================================

    public static partial class DatabaseHelper
    {
        public static List<PuebloTerritorio> ObtenerPueblosTerritorio(string codigoCongregacion) =>
            PueblosTerritorio.Leer(
                ObtenerConfiguracion(PueblosTerritorio.Clave(codigoCongregacion), ""));


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

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT MAX(Direccion)
                        FROM Personas
                        WHERE CongregacionCodigo = $codigo
                          AND COALESCE(Direccion, '') <> ''
                        GROUP BY COALESCE(NULLIF(PersonaRaizId, 0), Id);";

                    cmd.Parameters.AddWithValue("$codigo", codigoCongregacion ?? "");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return lista;
        }
    }
}
