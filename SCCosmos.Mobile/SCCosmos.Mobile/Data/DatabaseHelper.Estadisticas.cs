using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// ESTADÍSTICAS AGREGADAS DE LA ACTIVIDAD (Android)
// ============================================================================
//
// Cursos bíblicos del último mes con datos y media de precursores
// auxiliares de los últimos meses. Solo lectura sobre ActividadPersonas.

public static partial class DatabaseHelper
{
    public sealed class EstadisticasExtraMobile
    {
        public int CursosUltimoMes { get; set; }
        public double MediaPrecursoresAuxiliares { get; set; }
    }


    public static EstadisticasExtraMobile
        ObtenerEstadisticasExtraMobile()
    {
        var resultado =
            new EstadisticasExtraMobile();

        using var connection =
            new SqliteConnection(
                ConnectionString);

        connection.Open();

        if (!ExisteTabla(
                connection,
                "ActividadPersonas"))
        {
            return resultado;
        }

        // Misma regla que Windows: SincroCompartido/ResumenActividadInicio.cs.
        // Antes se tomaban los últimos meses con filas, y las filas preparadas
        // de los meses aún sin informar (con los auxiliares continuos) daban
        // 0 cursos y una media de 2 auxiliares. Ahora solo cuentan los meses
        // con informes de verdad y las fichas actuales, como en Windows.
        ResumenActividadInicio.Resultado resumen =
            ResumenActividadInicio.Calcular(
                connection,
                DateTime.Today);

        resultado.CursosUltimoMes =
            resumen.CursosBiblicos;

        resultado.MediaPrecursoresAuxiliares =
            resumen.MediaAuxiliares;

        return resultado;
    }
}
