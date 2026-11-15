using System;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos
{
    // =========================================================
    // PROGRAMA DE LA VISITA DESDE LA WEB (FORMULARIO DE LOS ANCIANOS)
    //
    // El enlace de cada visita lleva un código secreto; el código, lo
    // recibido y cuándo se pasó al programa van en Configuracion
    // (SincroCompartido/ProgramaWeb.cs), por congregación y mes.
    // =========================================================

    public static partial class DatabaseHelper
    {
        /// <summary>La visita (código de congregación y mes) de un código de enlace; null si no es de ninguna.</summary>
        public static (string Congregacion, string Mes)? BuscarVisitaProgramaWeb(string codigo)
        {
            if (!ProgramaWeb.CodigoValido(codigo))
            {
                return null;
            }

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Clave FROM Configuracion WHERE Clave LIKE $prefijo AND Valor = $codigo LIMIT 1;";
            cmd.Parameters.AddWithValue("$prefijo", ProgramaWeb.PrefijoClaveCodigo + "%");
            cmd.Parameters.AddWithValue("$codigo", codigo);

            if (cmd.ExecuteScalar() is not string clave)
            {
                return null;
            }

            // ProgramaWeb_Codigo_{congregación}_{yyyy-MM}
            string resto = clave.Substring(ProgramaWeb.PrefijoClaveCodigo.Length);
            int separador = resto.LastIndexOf('_');

            return separador > 0
                ? (resto.Substring(0, separador), resto.Substring(separador + 1))
                : null;
        }


        /// <summary>El martes de la visita de ese mes y si es con alojamiento (null si no hay visita).</summary>
        public static (DateTime Martes, bool ConAlojamiento)? ObtenerVisitaProgramaWeb(string congregacionCodigo, string mes)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT s.FechaMartes, COALESCE(s.Alojamiento, '')
                FROM Semanas s
                JOIN Congregaciones c ON c.Nombre = s.Congregacion
                WHERE c.Codigo = $codigo AND s.Actividad = 'Visita' AND s.FechaMartes LIKE $mes || '%'
                ORDER BY s.FechaMartes
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$codigo", congregacionCodigo ?? "");
            cmd.Parameters.AddWithValue("$mes", mes ?? "");

            using var lector = cmd.ExecuteReader();

            if (!lector.Read()
                || !DateTime.TryParse(lector.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime martes))
            {
                return null;
            }

            // Solo con «Sin alojamiento» en el calendario el formulario no lo
            // pregunta; sin indicar nada, sí.
            bool conAlojamiento =
                !lector.GetString(1).Trim().StartsWith("Sin", StringComparison.OrdinalIgnoreCase);

            return (martes.Date, conAlojamiento);
        }
    }
}
