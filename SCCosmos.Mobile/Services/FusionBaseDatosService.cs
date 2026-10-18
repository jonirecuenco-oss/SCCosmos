using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;

// ============================================================================
// FUSIÓN AUTOMÁTICA DE LA BASE DE DATOS (MÓVIL)
// ============================================================================
//
// La carpeta compartida se sincroniza con Syncthing/OneDrive y puede tardar
// un rato; es normal que el PC (o otro móvil) cambie la base mientras aquí
// también se han hecho cambios.
//
// Para poder combinar sin perder nada, la base local lleva un pequeño
// registro (tabla SCCosmos_RegistroCambios + disparadores) con las filas que
// se han tocado desde la última sincronización:
//
//   - 'I': fila creada desde la última sincronización.
//   - 'U': fila que ya existía y se ha modificado.
//   - 'D': fila que se ha borrado.
//
// Al fusionar:
//   - Se parte de la base que llega de la carpeta (los cambios del otro
//     dispositivo se conservan).
//   - Encima se aplican los cambios hechos aquí (ganan los de aquí).
//   - Si una fila creada aquí choca con una del otro dispositivo, se le da
//     un identificador nuevo (y se corrigen las claves ajenas que la
//     apuntaban).
//
// El registro y los disparadores solo viven en la base local: antes de
// exportar se limpian de la copia que se sube.

public static class FusionBaseDatosService
{
    public const string TablaRegistro =
        "SCCosmos_RegistroCambios";

    private const string PrefijoDisparador =
        "SCCosmos_seg_";

    /// <summary>
    /// Columnas que apuntan a la clave primaria de otra tabla (para corregir
    /// las referencias cuando hay que renumerar una fila).
    /// </summary>
    private static readonly Dictionary<string, string> ClavesAjenas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PersonaId"] = "Personas",
            ["ConyugeId"] = "Personas",
            ["PersonaRaizId"] = "Personas",
            ["TrasladoAnteriorId"] = "Personas",
            ["TrasladoSiguienteId"] = "Personas",
            ["CompaneroId"] = "Personas",
            ["AsignadoId"] = "Personas",
            ["SemanaId"] = "Semanas",
            ["AsambleaId"] = "ProgramaAsambleas",
            ["ReunionId"] = "ReunionPrecursores",
            ["SesionId"] = "ReunionPrecursoresSesiones",
            ["SeccionId"] = "SeccionesCircuito"
        };



    // ========================================================================
    // REGISTRO DE CAMBIOS
    // ========================================================================

    /// <summary>
    /// Crea el registro y los disparadores en la base local (si no están).
    /// </summary>
    public static void AsegurarRegistro()
    {
        if (!File.Exists(DatabasePaths.RutaLocal))
        {
            return;
        }

        using var conexion =
            new SqliteConnection(
                $"Data Source={DatabasePaths.RutaLocal}");

        conexion.Open();

        AsegurarRegistro(conexion);
    }


    public static void AsegurarRegistro(
        SqliteConnection conexion)
    {
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText =
                $"""
                CREATE TABLE IF NOT EXISTS {TablaRegistro}
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Tabla TEXT NOT NULL,
                    Clave TEXT NOT NULL,
                    Tipo TEXT NOT NULL,
                    Momento TEXT
                );
                """;

            cmd.ExecuteNonQuery();
        }

        List<(string Tabla, string Clave)> tablas =
            TablasConClave(
                conexion);

        foreach ((string tabla, string clave) in tablas)
        {
            CrearDisparador(
                conexion,
                tabla,
                clave,
                "INSERT",
                "I",
                "NEW");

            CrearDisparador(
                conexion,
                tabla,
                clave,
                "UPDATE",
                "U",
                "NEW");

            CrearDisparador(
                conexion,
                tabla,
                clave,
                "DELETE",
                "D",
                "OLD");
        }
    }


    private static void CrearDisparador(
        SqliteConnection conexion,
        string tabla,
        string clave,
        string momento,
        string tipo,
        string lado)
    {
        string nombre =
            $"{PrefijoDisparador}{tabla}_{momento.ToLowerInvariant()}";

        using var cmd = conexion.CreateCommand();

        cmd.CommandText =
            $"""
            CREATE TRIGGER IF NOT EXISTS "{nombre}"
            AFTER {momento} ON "{tabla}"
            BEGIN
                INSERT INTO {TablaRegistro}
                    (Tabla, Clave, Tipo, Momento)
                VALUES
                    ('{tabla.Replace("'", "''")}',
                     {ClaveSql(clave, lado)},
                     '{tipo}',
                     datetime('now'));
            END;
            """;

        cmd.ExecuteNonQuery();
    }


    /// <summary>
    /// Expresión SQL que devuelve la clave de la fila como texto.
    /// </summary>
    private static string ClaveSql(
        string claves,
        string lado)
    {
        string[] partes =
            claves.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (partes.Length == 1)
        {
            return $"CAST({lado}.\"{partes[0]}\" AS TEXT)";
        }

        var texto =
            new StringBuilder();

        foreach (string parte in partes)
        {
            if (texto.Length > 0)
            {
                texto.Append(" || '|' || ");
            }

            texto.Append(
                $"COALESCE(CAST({lado}.\"{parte}\" AS TEXT), '')");
        }

        return texto.ToString();
    }


    /// <summary>
    /// Tablas de la base (excepto las internas) con el nombre de su clave
    /// primaria (o claves, separadas por comas).
    /// </summary>
    private static List<(string Tabla, string Clave)> TablasConClave(
        SqliteConnection conexion)
    {
        var tablas =
            new List<(string, string)>();

        var nombres =
            new List<string>();

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;
                """;

            using SqliteDataReader reader =
                cmd.ExecuteReader();

            while (reader.Read())
            {
                nombres.Add(
                    reader.GetString(0));
            }
        }

        foreach (string nombre in nombres)
        {
            if (string.Equals(
                    nombre,
                    TablaRegistro,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var claves =
                new List<string>();

            using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"PRAGMA table_info(\"{nombre}\");";

                using SqliteDataReader reader =
                    cmd.ExecuteReader();

                while (reader.Read())
                {
                    int pk =
                        reader.IsDBNull(5)
                            ? 0
                            : Convert.ToInt32(
                                reader.GetValue(5));

                    if (pk > 0)
                    {
                        claves.Add(
                            reader.GetString(1));
                    }
                }
            }

            if (claves.Count == 0)
            {
                continue;
            }

            tablas.Add(
                (nombre, string.Join(",", claves)));
        }

        return tablas;
    }


    /// <summary>
    /// Quita el registro y los disparadores de una copia que se va a subir
    /// a la carpeta compartida.
    /// </summary>
    public static void LimpiarRegistro(
        string ruta)
    {
        try
        {
            using var conexion =
                new SqliteConnection(
                    $"Data Source={ruta}");

            conexion.Open();

            var disparadores =
                new List<string>();

            using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"""
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'trigger'
                      AND name LIKE '{PrefijoDisparador}%';
                    """;

                using SqliteDataReader reader =
                    cmd.ExecuteReader();

                while (reader.Read())
                {
                    disparadores.Add(
                        reader.GetString(0));
                }
            }

            foreach (string nombre in disparadores)
            {
                using var cmd = conexion.CreateCommand();

                cmd.CommandText =
                    $"DROP TRIGGER IF EXISTS \"{nombre}\";";

                cmd.ExecuteNonQuery();
            }

            using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"DROP TABLE IF EXISTS {TablaRegistro};";

                cmd.ExecuteNonQuery();
            }

            using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText =
                    "VACUUM;";

                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
            // La copia se sube igualmente: el registro no molesta.
        }
    }


    /// <summary>
    /// Vacía el registro de cambios de la base local (se usa después de
    /// subirla: lo que había anotado ya está en la carpeta).
    /// </summary>
    public static void LimpiarRegistroLocal()
    {
        try
        {
            if (!File.Exists(
                    DatabasePaths.RutaLocal))
            {
                return;
            }

            using var conexion =
                new SqliteConnection(
                    $"Data Source={DatabasePaths.RutaLocal}");

            conexion.Open();

            using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"DELETE FROM {TablaRegistro};";

                cmd.ExecuteNonQuery();
            }

            // Los disparadores siguen instalados: el registro se rellenará
            // con los próximos cambios.
            AsegurarRegistro(
                conexion);
        }
        catch
        {
        }
    }


    /// <summary>
    /// ¿Hay cambios locales anotados desde la última sincronización?
    /// </summary>
    public static bool HayCambiosPendientes()
    {
        try
        {
            if (!File.Exists(
                    DatabasePaths.RutaLocal))
            {
                return false;
            }

            using var conexion =
                new SqliteConnection(
                    $"Data Source={DatabasePaths.RutaLocal}");

            conexion.Open();

            using var cmd = conexion.CreateCommand();

            cmd.CommandText =
                $"SELECT COUNT(*) FROM {TablaRegistro};";

            return Convert.ToInt32(
                cmd.ExecuteScalar()
                ?? 0) > 0;
        }
        catch
        {
            return false;
        }
    }


    // ========================================================================
    // FUSIÓN
    // ========================================================================

    /// <summary>
    /// Combina los cambios hechos aquí con la base que llega de la carpeta.
    /// El resultado se escribe en <paramref name="rutaSalida"/>.
    /// </summary>
    public static ResultadoFusion Fusionar(
        string rutaMia,
        string rutaSuya,
        string rutaSalida)
    {
        try
        {
            if (!File.Exists(rutaMia)
                || !File.Exists(rutaSuya))
            {
                return new ResultadoFusion(
                    false,
                    "Falta alguna de las bases para fusionar.",
                    0,
                    0,
                    "");
            }

            string copia =
                CopiaDeSeguridad(
                    rutaMia);

            File.Copy(
                rutaSuya,
                rutaSalida,
                overwrite: true);

            Dictionary<(string Tabla, string Clave), string> cambios =
                LeerRegistro(
                    rutaMia);

            if (cambios.Count == 0)
            {
                return new ResultadoFusion(
                    true,
                    "No había cambios locales que aplicar.",
                    0,
                    0,
                    copia);
            }

            using var mia =
                new SqliteConnection(
                    $"Data Source={rutaMia}");

            using var destino =
                new SqliteConnection(
                    $"Data Source={rutaSalida}");

            mia.Open();
            destino.Open();

            Dictionary<string, string> clavesPorTabla =
                new(StringComparer.OrdinalIgnoreCase);

            foreach ((string Tabla, string Clave) t in
                     TablasConClave(destino))
            {
                clavesPorTabla[t.Tabla] =
                    t.Clave;
            }

            var renumerados =
                new Dictionary<(string Tabla, long Id), long>();

            var maximoPorTabla =
                new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase);

            int aplicados = 0;
            int renumeradosTotal = 0;

            // Primero las altas (para saber qué identificadores hay que
            // renumerar), y después las modificaciones.
            foreach (bool soloAltas in new[] { true, false })
            {
                foreach (var cambio in cambios)
                {
                    string tabla =
                        cambio.Key.Tabla;

                    string clave =
                        cambio.Key.Clave;

                    string tipo =
                        cambio.Value;

                    bool esAlta =
                        tipo.Contains('I');

                    if (esAlta != soloAltas)
                    {
                        continue;
                    }

                    if (!clavesPorTabla.TryGetValue(
                            tabla,
                            out string? clavesTabla))
                    {
                        continue;
                    }

                    bool existeAqui =
                        Existe(
                            mia,
                            tabla,
                            clavesTabla,
                            clave);

                    if (!existeAqui)
                    {
                        // Se borró aquí. Solo se borra en el resultado si
                        // la fila no se había creado aquí desde la última
                        // sincronización (en ese caso no hay nada que
                        // borrar en la otra base).
                        if (!tipo.Contains('I'))
                        {
                            Borrar(
                                destino,
                                tabla,
                                clavesTabla,
                                clave);

                            aplicados++;
                        }

                        continue;
                    }

                    // Fila actual en la base de este dispositivo.
                    Dictionary<string, object?> fila =
                        LeerFila(
                            mia,
                            tabla,
                            clavesTabla,
                            clave);

                    if (fila.Count == 0)
                    {
                        continue;
                    }

                    // ¿La clave choca con una fila distinta de la otra base?
                    string? claveNueva = null;

                    if (esAlta)
                    {
                        bool ocupada =
                            Existe(
                                destino,
                                tabla,
                                clavesTabla,
                                clave);

                        if (ocupada)
                        {
                            long idViejo =
                                Convert.ToInt64(
                                    fila.Values.First(),
                                    CultureInfo.InvariantCulture);

                            if (!maximoPorTabla.TryGetValue(
                                    tabla,
                                    out long maximo))
                            {
                                maximo =
                                    Maximo(
                                        destino,
                                        tabla,
                                        clavesTabla);

                                maximoPorTabla[tabla] =
                                    maximo;
                            }

                            maximo++;

                            maximoPorTabla[tabla] =
                                maximo;

                            renumerados[(tabla, idViejo)] =
                                maximo;

                            claveNueva =
                                maximo.ToString(
                                    CultureInfo.InvariantCulture);

                            renumeradosTotal++;
                        }
                    }

                    EscribirFila(
                        destino,
                        tabla,
                        clavesTabla,
                        fila,
                        claveNueva,
                        renumerados);

                    aplicados++;
                }
            }

            string mensaje =
                renumeradosTotal > 0
                    ? $"{aplicados} cambios aplicados " +
                      $"({renumeradosTotal} altas renumeradas por coincidir con las del otro dispositivo)."
                    : $"{aplicados} cambios aplicados.";

            return new ResultadoFusion(
                true,
                mensaje,
                aplicados,
                renumeradosTotal,
                copia);
        }
        catch (Exception ex)
        {
            return new ResultadoFusion(
                false,
                "No se pudieron combinar los cambios: " + ex.Message,
                0,
                0,
                "");
        }
    }


    /// <summary>
    /// Combina en la base local los datos de una copia de conflicto de
    /// Syncthing (sccosmos.sync-conflict-*.db). Añade las filas que solo
    /// existen en la copia y aplica su versión en las filas que aquí no se
    /// han tocado desde la última sincronización (los cambios locales
    /// pendientes no se pisan nunca; no se borran filas).
    /// </summary>
    public static ResultadoFusion FusionarCopiaDeConflicto(
        string rutaLocal,
        string rutaCopia)
    {
        try
        {
            if (!File.Exists(rutaLocal)
                || !File.Exists(rutaCopia))
            {
                return new ResultadoFusion(
                    false,
                    "Falta la base local o la copia de conflicto.",
                    0,
                    0,
                    "");
            }

            string copiaSeguridad =
                CopiaDeSeguridad(
                    rutaLocal);

            Dictionary<(string Tabla, string Clave), string> pendientes =
                LeerRegistro(
                    rutaLocal);

            using var mia =
                new SqliteConnection(
                    $"Data Source={rutaLocal}");

            using var otra =
                new SqliteConnection(
                    $"Data Source={rutaCopia};Mode=ReadOnly");

            mia.Open();
            otra.Open();

            Dictionary<string, string> clavesLocal =
                new(StringComparer.OrdinalIgnoreCase);

            foreach ((string Tabla, string Clave) t in
                     TablasConClave(mia))
            {
                clavesLocal[t.Tabla] =
                    t.Clave;
            }

            int insertadas = 0;
            int actualizadas = 0;

            foreach ((string tabla, string clavesCopia) in
                     TablasConClave(otra))
            {
                if (string.Equals(
                        tabla,
                        TablaRegistro,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!clavesLocal.TryGetValue(
                        tabla,
                        out string? clavesTabla))
                {
                    continue;
                }

                HashSet<string> columnas =
                    ColumnasDe(
                        mia,
                        tabla);

                string[] partesClave =
                    clavesTabla.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

                using var cmd =
                    otra.CreateCommand();

                cmd.CommandText =
                    $"SELECT * FROM \"{tabla}\";";

                using SqliteDataReader reader =
                    cmd.ExecuteReader();

                while (reader.Read())
                {
                    var filaCopia =
                        new Dictionary<string, object?>(
                            StringComparer.OrdinalIgnoreCase);

                    for (int i = 0;
                         i < reader.FieldCount;
                         i++)
                    {
                        string nombre =
                            reader.GetName(i);

                        if (!columnas.Contains(nombre))
                        {
                            continue;
                        }

                        filaCopia[nombre] =
                            reader.IsDBNull(i)
                                ? null
                                : reader.GetValue(i);
                    }

                    bool claveCompleta =
                        true;

                    foreach (string parte in partesClave)
                    {
                        if (!filaCopia.ContainsKey(
                                parte))
                        {
                            claveCompleta =
                                false;

                            break;
                        }
                    }

                    if (!claveCompleta)
                    {
                        continue;
                    }

                    string clave =
                        ConstruirClave(
                            filaCopia,
                            partesClave);

                    // Lo tocado aquí desde la última sincronización gana.
                    if (pendientes.ContainsKey(
                            (tabla, clave)))
                    {
                        continue;
                    }

                    Dictionary<string, object?> actual =
                        LeerFila(
                            mia,
                            tabla,
                            clavesTabla,
                            clave);

                    if (actual.Count == 0)
                    {
                        EscribirFila(
                            mia,
                            tabla,
                            clavesTabla,
                            filaCopia,
                            null,
                            SinRenumerar);

                        insertadas++;

                        continue;
                    }

                    bool distinta =
                        false;

                    foreach (KeyValuePair<string, object?> campo in
                             filaCopia)
                    {
                        if (!actual.TryGetValue(
                                campo.Key,
                                out object? valorActual))
                        {
                            continue;
                        }

                        if (!Iguales(
                                campo.Value,
                                valorActual))
                        {
                            distinta =
                                true;

                            break;
                        }
                    }

                    if (!distinta)
                    {
                        continue;
                    }

                    // Se conservan las columnas locales que la copia no traiga.
                    var filaCompleta =
                        new Dictionary<string, object?>(
                            actual,
                            StringComparer.OrdinalIgnoreCase);

                    foreach (KeyValuePair<string, object?> campo in
                             filaCopia)
                    {
                        filaCompleta[campo.Key] =
                            campo.Value;
                    }

                    EscribirFila(
                        mia,
                        tabla,
                        clavesTabla,
                        filaCompleta,
                        null,
                        SinRenumerar);

                    actualizadas++;
                }
            }

            string mensaje =
                "Copia de conflicto combinada: " +
                $"{insertadas} registros añadidos y " +
                $"{actualizadas} actualizados.";

            return new ResultadoFusion(
                true,
                mensaje,
                insertadas + actualizadas,
                0,
                copiaSeguridad);
        }
        catch (Exception ex)
        {
            return new ResultadoFusion(
                false,
                "No se pudo combinar la copia de conflicto: " + ex.Message,
                0,
                0,
                "");
        }
    }


    /// <summary>
    /// Registro de cambios reducido a la última situación por fila:
    /// combinación de las marcas ('I', 'U', 'D') que ha tenido.
    /// </summary>
    private static Dictionary<(string Tabla, string Clave), string> LeerRegistro(
        string ruta)
    {
        var resultado =
            new Dictionary<(string, string), string>();

        using var conexion =
            new SqliteConnection(
                $"Data Source={ruta}");

        conexion.Open();

        using var cmd = conexion.CreateCommand();

        cmd.CommandText =
            $"SELECT Tabla, Clave, Tipo FROM {TablaRegistro} ORDER BY Id;";

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string tabla =
                reader.GetString(0);

            string clave =
                reader.GetString(1);

            string tipo =
                reader.GetString(2);

            var llave =
                (tabla, clave);

            if (resultado.TryGetValue(
                    llave,
                    out string? acumulado))
            {
                if (!acumulado.Contains(tipo))
                {
                    resultado[llave] =
                        acumulado + tipo;
                }
            }
            else
            {
                resultado[llave] =
                    tipo;
            }
        }

        return resultado;
    }


    private static bool ClaveEsNumerica(
        string claves)
    {
        return !claves.Contains(',');
    }


    private static HashSet<string> ColumnasDe(
        SqliteConnection conexion,
        string tabla)
    {
        var columnas =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"PRAGMA table_info(\"{tabla}\");";

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            columnas.Add(
                reader.GetString(1));
        }

        return columnas;
    }


    private static string ConstruirClave(
        Dictionary<string, object?> fila,
        string[] partes)
    {
        var texto =
            new StringBuilder();

        foreach (string parte in partes)
        {
            if (texto.Length > 0)
            {
                texto.Append('|');
            }

            texto.Append(
                Convert.ToString(
                    fila[parte],
                    CultureInfo.InvariantCulture)
                ?? "");
        }

        return texto.ToString();
    }


    private static bool Iguales(
        object? a,
        object? b)
    {
        if (a is null
            && b is null)
        {
            return true;
        }

        if (a is null
            || b is null)
        {
            return false;
        }

        if (a is byte[] bytesA
            && b is byte[] bytesB)
        {
            return bytesA.AsSpan()
                .SequenceEqual(
                    bytesB);
        }

        if (a is byte[]
            || b is byte[])
        {
            return false;
        }

        return string.Equals(
            Convert.ToString(
                a,
                CultureInfo.InvariantCulture),
            Convert.ToString(
                b,
                CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }


    /// <summary>
    /// Mapa de renumeración vacío (solo se lee; se comparte).
    /// </summary>
    private static readonly Dictionary<(string Tabla, long Id), long> SinRenumerar =
        new();


    private static bool Existe(
        SqliteConnection conexion,
        string tabla,
        string claves,
        string clave)
    {
        if (ClaveEsNumerica(claves))
        {
            using var cmd = conexion.CreateCommand();

            cmd.CommandText =
                $"SELECT 1 FROM \"{tabla}\" WHERE \"{claves}\" = $clave LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$clave",
                long.TryParse(
                    clave,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out long numero)
                    ? numero
                    : clave);

            return cmd.ExecuteScalar() is not null;
        }

        Dictionary<string, object?> fila =
            LeerFila(
                conexion,
                tabla,
                claves,
                clave);

        return fila.Count > 0;
    }


    private static Dictionary<string, object?> LeerFila(
        SqliteConnection conexion,
        string tabla,
        string claves,
        string clave)
    {
        var fila =
            new Dictionary<string, object?>(
                StringComparer.OrdinalIgnoreCase);

        using var cmd = conexion.CreateCommand();

        if (ClaveEsNumerica(claves))
        {
            cmd.CommandText =
                $"SELECT * FROM \"{tabla}\" WHERE \"{claves}\" = $clave LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$clave",
                long.TryParse(
                    clave,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out long numero)
                    ? numero
                    : clave);
        }
        else
        {
            string[] partes =
                claves.Split(
                    ',');

            var condiciones =
                new List<string>();

            string[] valores =
                clave.Split('|');

            for (int i = 0;
                 i < partes.Length
                 && i < valores.Length;
                 i++)
            {
                condiciones.Add(
                    $"COALESCE(CAST(\"{partes[i].Trim()}\" AS TEXT), '') = $p{i}");

                cmd.Parameters.AddWithValue(
                    $"$p{i}",
                    valores[i]);
            }

            if (condiciones.Count == 0)
            {
                return fila;
            }

            cmd.CommandText =
                $"SELECT * FROM \"{tabla}\" WHERE {string.Join(" AND ", condiciones)} LIMIT 1;";
        }

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        if (!reader.Read())
        {
            return fila;
        }

        for (int i = 0;
             i < reader.FieldCount;
             i++)
        {
            fila[reader.GetName(i)] =
                reader.IsDBNull(i)
                    ? null
                    : reader.GetValue(i);
        }

        return fila;
    }


    private static void EscribirFila(
        SqliteConnection conexion,
        string tabla,
        string claves,
        Dictionary<string, object?> fila,
        string? claveNueva,
        Dictionary<(string Tabla, long Id), long> renumerados)
    {
        string[] columnas =
            fila.Keys.ToArray();

        // La columna que hace de clave (en tablas de clave simple).
        string? columnaClave =
            claves.Contains(',')
                ? null
                : claves;

        var nombres =
            new List<string>();

        var parametros =
            new List<string>();

        var valores =
            new List<object?>();

        for (int i = 0;
             i < columnas.Length;
             i++)
        {
            string columna =
                columnas[i];

            object? valor =
                fila[columna];

            if (columnaClave is not null
                && string.Equals(
                    columna,
                    columnaClave,
                    StringComparison.OrdinalIgnoreCase)
                && claveNueva is not null)
            {
                valor =
                    long.Parse(
                        claveNueva,
                        CultureInfo.InvariantCulture);
            }
            else if (valor is not null
                     && ClavesAjenas.TryGetValue(
                         columna,
                         out string? tablaAjen))
            {
                long id = Convert.ToInt64(
                    valor,
                    CultureInfo.InvariantCulture);

                if (renumerados.TryGetValue(
                        (tablaAjen, id),
                        out long nuevo))
                {
                    valor =
                        nuevo;
                }
            }

            nombres.Add(
                $"\"{columna}\"");

            parametros.Add(
                $"$c{i}");

            valores.Add(
                valor);
        }

        using var cmd = conexion.CreateCommand();

        cmd.CommandText =
            $"INSERT OR REPLACE INTO \"{tabla}\" ({string.Join(", ", nombres)}) VALUES ({string.Join(", ", parametros)});";

        for (int i = 0;
             i < valores.Count;
             i++)
        {
            cmd.Parameters.AddWithValue(
                $"$c{i}",
                valores[i]
                ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }


    private static void Borrar(
        SqliteConnection conexion,
        string tabla,
        string claves,
        string clave)
    {
        using var cmd = conexion.CreateCommand();

        if (ClaveEsNumerica(claves))
        {
            cmd.CommandText =
                $"DELETE FROM \"{tabla}\" WHERE \"{claves}\" = $clave;";

            cmd.Parameters.AddWithValue(
                "$clave",
                long.TryParse(
                    clave,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out long numero)
                    ? numero
                    : clave);

            cmd.ExecuteNonQuery();

            return;
        }

        string[] partes =
            claves.Split(
                ',');

        string[] valores =
            clave.Split('|');

        var condiciones =
            new List<string>();

        for (int i = 0;
             i < partes.Length
             && i < valores.Length;
             i++)
        {
            condiciones.Add(
                $"COALESCE(CAST(\"{partes[i].Trim()}\" AS TEXT), '') = $p{i}");

            cmd.Parameters.AddWithValue(
                $"$p{i}",
                valores[i]);
        }

        if (condiciones.Count == 0)
        {
            return;
        }

        cmd.CommandText =
            $"DELETE FROM \"{tabla}\" WHERE {string.Join(" AND ", condiciones)};";

        cmd.ExecuteNonQuery();
    }


    private static long Maximo(
        SqliteConnection conexion,
        string tabla,
        string claves)
    {
        using var cmd = conexion.CreateCommand();

        cmd.CommandText =
            $"SELECT COALESCE(MAX(\"{claves}\"), 0) FROM \"{tabla}\";";

        return Convert.ToInt64(
            cmd.ExecuteScalar()
            ?? 0L,
            CultureInfo.InvariantCulture);
    }


    private static string CopiaDeSeguridad(
        string ruta)
    {
        try
        {
            string carpeta =
                Path.Combine(
                    FileSystem.AppDataDirectory,
                    "copias");

            Directory.CreateDirectory(
                carpeta);

            string destino =
                Path.Combine(
                    carpeta,
                    $"sccosmos_antes_de_fusion_{DateTime.Now:yyyyMMdd_HHmmss}.db");

            File.Copy(
                ruta,
                destino,
                overwrite: true);

            // Se conservan las tres copias más recientes.
            var copias =
                new DirectoryInfo(carpeta)
                    .GetFiles("sccosmos_antes_de_fusion_*.db")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(3)
                    .ToList();

            foreach (FileInfo copia in copias)
            {
                copia.Delete();
            }

            return destino;
        }
        catch
        {
            return "";
        }
    }
}

public sealed record ResultadoFusion(
    bool Correcto,
    string Mensaje,
    int Aplicados,
    int Renumerados,
    string CopiaSeguridad);