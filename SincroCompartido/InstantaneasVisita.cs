using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// =============================================================
// QUÉ DATOS DE CADA PERSONA MUESTRA UNA VISITA
//
// La misma regla en Windows y en Android.
//
// La última visita de una congregación, y cualquier mes posterior,
// muestran los datos actuales de la ficha: lo que cambie después, desde
// cualquier dispositivo o desde JW Hub, se ve en la tabla.
//
// Las visitas anteriores quedan congeladas: cada mes muestra la
// instantánea más reciente hasta ese mes. Una visita se congela al
// importar la siguiente, con los datos que tenía la ficha justo antes,
// que son los que se estaban viendo hasta ese momento. A partir de ahí
// no se modifica.
//
// Quien ya no está en la congregación sigue apareciendo en la visita en
// la que estuvo, con lo que tenía entonces.
// =============================================================

public static class InstantaneasVisita
{
    /// <summary>
    /// Los Id de las filas de Personas que corresponden a cada persona de la
    /// congregación $codigo en el mes $mes (yyyy-MM).
    /// </summary>
    public const string IdsDelMes =
        """
        SELECT elegida.Id
        FROM
        (
            SELECT
                x.Id,
                ROW_NUMBER() OVER
                (
                    PARTITION BY
                        CASE
                            WHEN x.PersonaRaizId > 0
                            THEN x.PersonaRaizId
                            ELSE x.Id
                        END
                    ORDER BY
                        CASE
                            WHEN x.MesHistorico IS NULL
                            THEN 0
                            ELSE 1
                        END DESC,
                        x.MesHistorico DESC,
                        x.Id DESC
                ) AS Orden
            FROM Personas x
            WHERE x.CongregacionCodigo = $codigo
              AND
              (
                  x.MesHistorico IS NULL
                  OR
                  (
                      x.MesHistorico <= $mes
                      AND $mes <
                      (
                          SELECT MAX(u.MesHistorico)
                          FROM Personas u
                          WHERE u.CongregacionCodigo = $codigo
                            AND u.MesHistorico IS NOT NULL
                      )
                  )
                  OR
                  (
                      x.MesHistorico = $mes
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM Personas r
                          WHERE r.Id = x.PersonaRaizId
                            AND r.CongregacionCodigo = $codigo
                      )
                  )
              )
        ) elegida
        WHERE elegida.Orden = 1
        """;


    /// <summary>
    /// Congela la que hasta ahora era la última visita, antes de importar otra
    /// más reciente: sus instantáneas pasan a tener lo que muestra la ficha.
    /// Reimportar el mismo mes o uno anterior no cambia nada.
    /// </summary>
    public static void CongelarAnterior(
        SqliteConnection conexion,
        SqliteTransaction? transaccion,
        string congregacion,
        string mesNuevo)
    {
        string? ultima;

        using (var consulta =
               conexion.CreateCommand())
        {
            consulta.Transaction =
                transaccion;

            consulta.CommandText =
                "SELECT MAX(MesHistorico) FROM Personas " +
                "WHERE CongregacionCodigo = $codigo AND MesHistorico IS NOT NULL;";

            consulta.Parameters.AddWithValue(
                "$codigo",
                congregacion);

            ultima =
                consulta.ExecuteScalar() as string;
        }

        if (string.IsNullOrEmpty(ultima)
            || string.CompareOrdinal(ultima, mesNuevo) >= 0)
        {
            return;
        }

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        // Quien ya se ha ido a otra congregación conserva lo que tenía aquí.
        cmd.CommandText =
            """
            UPDATE Personas AS s
            SET FechaNacimiento = r.FechaNacimiento,
                Sexo = r.Sexo,
                FechaBautismo = r.FechaBautismo,
                Esperanza = r.Esperanza,
                Nombramiento = r.Nombramiento,
                Servicio = r.Servicio,
                Grupo = r.Grupo,
                FechaInicioPrec = r.FechaInicioPrec,
                FechaUltimaEscuela = r.FechaUltimaEscuela,
                Solicitudes = r.Solicitudes,
                Privilegios = r.Privilegios
            FROM Personas AS r
            WHERE s.CongregacionCodigo = $codigo
              AND s.MesHistorico = $ultima
              AND r.Id = s.PersonaRaizId
              AND r.CongregacionCodigo = s.CongregacionCodigo
              AND
              (
                  s.FechaNacimiento IS NOT r.FechaNacimiento
                  OR s.Sexo IS NOT r.Sexo
                  OR s.FechaBautismo IS NOT r.FechaBautismo
                  OR s.Esperanza IS NOT r.Esperanza
                  OR s.Nombramiento IS NOT r.Nombramiento
                  OR s.Servicio IS NOT r.Servicio
                  OR s.Grupo IS NOT r.Grupo
                  OR s.FechaInicioPrec IS NOT r.FechaInicioPrec
                  OR s.FechaUltimaEscuela IS NOT r.FechaUltimaEscuela
                  OR s.Solicitudes IS NOT r.Solicitudes
                  OR s.Privilegios IS NOT r.Privilegios
              );
            """;

        cmd.Parameters.AddWithValue(
            "$codigo",
            congregacion);

        cmd.Parameters.AddWithValue(
            "$ultima",
            ultima);

        cmd.ExecuteNonQuery();
    }
}
