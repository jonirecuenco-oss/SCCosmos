using Microsoft.Data.Sqlite;

using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Data;

// =========================================================
// FORMULARIO WEB DEL PROGRAMA DE LA VISITA (lo sirve el PC; aquí solo se lee)
// =========================================================

public static partial class DatabaseHelper
{
    /// <summary>
    /// Las visitas con enlace del formulario: congregación, mes («yyyy-MM»),
    /// código del enlace y cuándo pulsaron «Enviar» («» si aún no).
    /// </summary>
    public static List<(string Congregacion, string Mes, string Codigo, string Enviado)> EnlacesProgramaWeb()
    {
        var enlaces = new List<(string, string, string, string)>();

        using var conexion =
            new SqliteConnection(ConnectionString);

        conexion.Open();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            "SELECT Clave, Valor FROM Configuracion WHERE Clave LIKE $prefijo;";

        cmd.Parameters.AddWithValue(
            "$prefijo",
            ProgramaWeb.PrefijoClaveCodigo + "%");

        var codigos = new List<(string Congregacion, string Mes, string Codigo)>();

        using (var lector = cmd.ExecuteReader())
        {
            while (lector.Read())
            {
                string clave = lector.GetString(0);
                string codigo = lector.IsDBNull(1) ? "" : lector.GetString(1);

                // ProgramaWeb_Codigo_{congregación}_{yyyy-MM}
                if (!clave.StartsWith(ProgramaWeb.PrefijoClaveCodigo, StringComparison.Ordinal)
                    || !ProgramaWeb.CodigoValido(codigo))
                {
                    continue;
                }

                string resto = clave[ProgramaWeb.PrefijoClaveCodigo.Length..];
                int separador = resto.LastIndexOf('_');

                if (separador > 0)
                {
                    codigos.Add((resto[..separador], resto[(separador + 1)..], codigo));
                }
            }
        }

        foreach (var (congregacion, mes, codigo) in codigos)
        {
            enlaces.Add((
                congregacion,
                mes,
                codigo,
                ObtenerConfiguracion(conexion, ProgramaWeb.ClaveEnviado(congregacion, mes), "")));
        }

        return enlaces;
    }
}
