using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;

namespace SCCosmos.Sincro;

// ============================================================
// REGISTRO DE CAMBIOS Y APLICACIÓN EN LA BASE DE DATOS
//
// Cada base local lleva una tabla SCCosmos_Cambios con UNA fila
// por registro modificado (la última situación) y un número de
// secuencia creciente. Los cambios los anotan disparadores, igual
// para todas las tablas, en Windows y en Android.
//
// El motor de red lee de aquí lo que hay que enviar a cada
// dispositivo y aplica aquí lo que llega de los demás.
// ============================================================

public sealed class SincroAlmacen
{
    private const string TablaCambios =
        "SCCosmos_Cambios";

    private const string TablaContador =
        "SCCosmos_SincroContador";

    private const string TablaPares =
        "SCCosmos_SincroPares";

    private const string TablaSilencio =
        "SCCosmos_Silencio";

    private const string PrefijoDisparador =
        "SCCosmos_red_";

    /// <summary>
    /// Tamaño de lote al enviar cambios.
    /// </summary>
    public const int TamanoLote =
        200;


    private readonly string _rutaDb;

    private readonly string _idDispositivo;


    public SincroAlmacen(
        string rutaDb,
        string idDispositivo)
    {
        _rutaDb =
            rutaDb;

        _idDispositivo =
            idDispositivo ?? "";
    }


    public string Ruta =>
        _rutaDb;


    // ========================================================
    // ABRIR
    // ========================================================

    private SqliteConnection Abrir()
    {
        var conexion =
            new SqliteConnection(
                $"Data Source={_rutaDb};Pooling=False");

        conexion.Open();

        // Los cambios llegan en cualquier orden (p. ej., la actividad de
        // una persona antes que la persona). Las claves foráneas solo
        // deben vigilar las escrituras normales de la app, no la
        // aplicación de un lote: si se comprobaran aquí, se descartarían
        // filas válidas que se completan con el resto del lote.
        try
        {
            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "PRAGMA foreign_keys = OFF;";

            cmd.ExecuteNonQuery();
        }
        catch
        {
        }

        return conexion;
    }


    // ========================================================
    // ASEGURAR TABLAS Y DISPARADORES
    //
    // Se ejecuta al arrancar la aplicación: crea lo que falte y
    // reinstala los disparadores (por si cambió el Id del
    // dispositivo). También retira los disparadores antiguos del
    // sistema de carpeta (SCCosmos_seg_*).
    // ========================================================

    public void Asegurar()
    {
        using var conexion =
            Abrir();

        Ejecutar(
            conexion,
            $"""
            CREATE TABLE IF NOT EXISTS {TablaCambios}
            (
                Tabla TEXT NOT NULL,
                Clave TEXT NOT NULL,
                Tipo TEXT NOT NULL,
                Momento TEXT NOT NULL,
                Origen TEXT NOT NULL,
                Seq INTEGER NOT NULL,
                PRIMARY KEY (Tabla, Clave)
            );
            """);

        Ejecutar(
            conexion,
            $"""
            CREATE TABLE IF NOT EXISTS {TablaContador}
            (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Valor INTEGER NOT NULL
            );
            """);

        Ejecutar(
            conexion,
            $"INSERT OR IGNORE INTO {TablaContador} (Id, Valor) VALUES (1, 0);");

        Ejecutar(
            conexion,
            $"""
            CREATE TABLE IF NOT EXISTS {TablaPares}
            (
                Dispositivo TEXT PRIMARY KEY,
                UltimoEnviado INTEGER NOT NULL DEFAULT 0,
                UltimoRecibido INTEGER NOT NULL DEFAULT 0
            );
            """);

        Ejecutar(
            conexion,
            $"""
            CREATE TABLE IF NOT EXISTS {TablaSilencio}
            (
                Id INTEGER PRIMARY KEY
            );
            """);

        Ejecutar(
            conexion,
            """
            CREATE TABLE IF NOT EXISTS SCCosmos_SincroEstado
            (
                Clave TEXT PRIMARY KEY,
                Valor TEXT NOT NULL
            );
            """);

        // Limpieza del sistema antiguo (sincronización por carpeta).
        foreach (string nombre in
                 NombresDe(
                     conexion,
                     "trigger",
                     "SCCosmos_seg_%"))
        {
            Ejecutar(
                conexion,
                $"DROP TRIGGER IF EXISTS \"{nombre}\";");
        }

        Ejecutar(
            conexion,
            "DROP TABLE IF EXISTS SCCosmos_RegistroCambios;");

        // Disparadores nuevos.
        foreach (string nombre in
                 NombresDe(
                     conexion,
                     "trigger",
                     PrefijoDisparador + "%"))
        {
            Ejecutar(
                conexion,
                $"DROP TRIGGER IF EXISTS \"{nombre}\";");
        }

        foreach ((string tabla, string claves) in
                 TablasConClave(
                     conexion))
        {
            CrearDisparador(
                conexion,
                tabla,
                claves,
                "insert",
                "I",
                "NEW");

            CrearDisparador(
                conexion,
                tabla,
                claves,
                "update",
                "U",
                "NEW");

            CrearDisparador(
                conexion,
                tabla,
                claves,
                "delete",
                "D",
                "OLD");
        }

        // El contador nunca puede quedarse por detrás de lo ya enviado o
        // recibido: si una fusión restaura una copia antigua, los cambios
        // nuevos recibirían números de secuencia viejos y los demás
        // equipos no los pedirían nunca (se quedarían sin recibir).
        try
        {
            using var contador =
                conexion.CreateCommand();

            contador.CommandText =
                $"""
                UPDATE {TablaContador}
                SET Valor = MAX(
                    Valor,
                    COALESCE((SELECT MAX(Seq) FROM {TablaCambios}), 0),
                    COALESCE((SELECT MAX(UltimoEnviado) FROM {TablaPares}), 0),
                    COALESCE((SELECT MAX(UltimoRecibido) FROM {TablaPares}), 0));
                """;

            contador.ExecuteNonQuery();
        }
        catch
        {
        }

        SembrarFilasFaltantes(
            conexion);
    }


    /// <summary>
    /// Las filas que ya existían ANTES de activar la sincronización no
    /// están en el registro de cambios. Sin esto, un dispositivo nuevo
    /// (o uno que se quedó con una copia antigua) nunca las recibiría:
    /// el otro equipo no tiene nada que enviar porque nunca se modificaron.
    ///
    /// Se anotan una sola vez como «base» (Tipo B, momento antiguo). Al
    /// recibirlas, el otro equipo SOLO las inserta si esa fila no existe:
    /// nunca pisan datos que ya tenga. Así se rellenan huecos sin riesgo.
    /// </summary>
    private void SembrarFilasFaltantes(
        SqliteConnection conexion)
    {
        try
        {
            using (var comprobar =
                   conexion.CreateCommand())
            {
                comprobar.CommandText =
                    "SELECT COUNT(*) FROM SCCosmos_SincroEstado WHERE Clave = 'BaseSembrada' AND Valor = '2';";

                if (Convert.ToInt64(
                        comprobar.ExecuteScalar()) > 0)
                {
                    return;
                }
            }

            long contador;

            using (var cmd =
                   conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"SELECT Valor FROM {TablaContador};";

                contador =
                    Convert.ToInt64(
                        cmd.ExecuteScalar());
            }

            string origen =
                _idDispositivo.Replace(
                    "'",
                    "''");

            const string momento =
                "2000-01-01T00:00:00.000Z";

            foreach ((string tabla, string claves) in
                     TablasConClave(
                         conexion))
            {
                string tablaEscapada =
                    tabla.Replace(
                        "'",
                        "''");

                string claveSql =
                    ClaveSql(
                        claves,
                        "t");

                string faltanSql =
                    $"""
                    FROM "{tabla}" t
                    WHERE NOT EXISTS
                    (
                        SELECT 1 FROM {TablaCambios} c
                        WHERE c.Tabla = '{tablaEscapada}'
                          AND c.Clave = {claveSql}
                    );
                    """;

                long faltan;

                using (var contar =
                       conexion.CreateCommand())
                {
                    contar.CommandText =
                        "SELECT COUNT(*) " + faltanSql;

                    faltan =
                        Convert.ToInt64(
                            contar.ExecuteScalar());
                }

                if (faltan == 0)
                {
                    continue;
                }

                using (var insertar =
                       conexion.CreateCommand())
                {
                    insertar.CommandText =
                        $"""
                        INSERT INTO {TablaCambios}
                            (Tabla, Clave, Tipo, Momento, Origen, Seq)
                        SELECT
                            '{tablaEscapada}',
                            {claveSql},
                            'B',
                            '{momento}',
                            '{origen}',
                            $base + ROW_NUMBER() OVER (ORDER BY {claveSql})
                        {faltanSql}
                        """;

                    insertar.Parameters.AddWithValue(
                        "$base",
                        contador);

                    insertar.ExecuteNonQuery();
                }

                contador +=
                    faltan;
            }

            using (var cmd =
                   conexion.CreateCommand())
            {
                cmd.CommandText =
                    $"UPDATE {TablaContador} SET Valor = $valor;";

                cmd.Parameters.AddWithValue(
                    "$valor",
                    contador);

                cmd.ExecuteNonQuery();
            }

            using (var cmd =
                   conexion.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO SCCosmos_SincroEstado (Clave, Valor) " +
                    "VALUES ('BaseSembrada', '2') " +
                    "ON CONFLICT (Clave) DO UPDATE SET Valor = excluded.Valor;";

                cmd.ExecuteNonQuery();
            }
        }
        catch
        {
        }
    }


    private void CrearDisparador(
        SqliteConnection conexion,
        string tabla,
        string claves,
        string momento,
        string tipo,
        string lado)
    {
        string nombre =
            $"{PrefijoDisparador}{tabla}_{momento}";

        string claveSql =
            ClaveSql(
                claves,
                lado);

        string tablaEscapada =
            tabla.Replace(
                "'",
                "''");

        string idEscapado =
            _idDispositivo.Replace(
                "'",
                "''");

        Ejecutar(
            conexion,
            $"""
            CREATE TRIGGER "{nombre}"
            AFTER {momento.ToUpperInvariant()} ON "{tabla}"
            WHEN NOT EXISTS (SELECT 1 FROM {TablaSilencio})
            BEGIN
                UPDATE {TablaContador}
                SET Valor = Valor + 1;

                INSERT INTO {TablaCambios}
                    (Tabla, Clave, Tipo, Momento, Origen, Seq)
                VALUES
                    ('{tablaEscapada}',
                     {claveSql},
                     '{tipo}',
                     strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                     '{idEscapado}',
                     (SELECT Valor FROM {TablaContador}))
                ON CONFLICT (Tabla, Clave)
                DO UPDATE SET
                    Tipo = excluded.Tipo,
                    Momento = excluded.Momento,
                    Origen = excluded.Origen,
                    Seq = excluded.Seq;
            END;
            """);
    }


    // ========================================================
    // LEER CAMBIOS PENDIENTES PARA UN PAR
    // ========================================================

    /// <summary>
    /// Cambios con Seq mayor que <paramref name="desdeSeq"/>,
    /// listos para enviar (con la fila actual o borrado).
    /// </summary>
    public List<CambioSincro> ObtenerPendientes(
        long desdeSeq)
    {
        var resultado =
            new List<CambioSincro>();

        using var conexion =
            Abrir();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"""
            SELECT Tabla, Clave, Tipo, Momento, Origen, Seq
            FROM {TablaCambios}
            WHERE Seq > $desde
            ORDER BY Seq
            LIMIT {TamanoLote};
            """;

        cmd.Parameters.AddWithValue(
            "$desde",
            desdeSeq);

        using (var lector =
               cmd.ExecuteReader())
        {
            while (lector.Read())
            {
                string tabla =
                    lector.GetString(0);

                string clave =
                    lector.GetString(1);

                string tipo =
                    lector.GetString(2);

                string momento =
                    lector.GetString(3);

                string origen =
                    lector.GetString(4);

                long seq =
                    lector.GetInt64(5);

                resultado.Add(
                    new CambioSincro
                    {
                        Tabla =
                            tabla,

                        Clave =
                            clave,

                        Tipo =
                            tipo,

                        Momento =
                            momento,

                        Origen =
                            origen,

                        Seq =
                            seq
                    });
            }
        }

        // Las filas se leen fuera del lector (otra consulta en la
        // misma conexión).

        foreach (CambioSincro cambio in
                 resultado)
        {
            if (cambio.Tipo == "D")
            {
                continue;
            }

            string claves =
                ClavesDeTabla(
                    conexion,
                    cambio.Tabla);

            if (claves.Length == 0)
            {
                cambio.Tipo =
                    "D";

                continue;
            }

            Dictionary<string, object?> fila =
                LeerFila(
                    conexion,
                    cambio.Tabla,
                    claves,
                    cambio.Clave);

            if (fila.Count == 0)
            {
                cambio.Tipo =
                    "D";

                continue;
            }

            cambio.Fila =
                fila;
        }

        return resultado;
    }


    /// <summary>
    /// Seq más alto registrado.
    /// </summary>
    public long SecuenciaMaxima()
    {
        using var conexion =
            Abrir();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"SELECT COALESCE(MAX(Seq), 0) FROM {TablaCambios};";

        return Convert.ToInt64(
            cmd.ExecuteScalar()
            ?? 0L,
            CultureInfo.InvariantCulture);
    }


    // ========================================================
    // APLICAR CAMBIOS QUE LLEGAN DE OTRO DISPOSITIVO
    // ========================================================

    /// <summary>
    /// Aplica un lote entero en una transacción. El "silencio"
    /// se activa y desactiva dentro de la misma transacción: así
    /// los demás escritores no ven nunca silenciados sus cambios.
    ///
    /// Devuelve cuántos se aplicaron, cuántos se ignoraron (por
    /// ser más antiguos) y el mayor Seq visto.
    /// </summary>
    public ResultadoAplicacion AplicarLote(
        IReadOnlyList<CambioSincro> cambios)
    {
        var resultado =
            new ResultadoAplicacion();

        if (cambios.Count == 0)
        {
            return resultado;
        }

        using var conexion =
            Abrir();

        using var transaccion =
            conexion.BeginTransaction(
                deferred: false);

        try
        {
            Ejecutar(
                conexion,
                $"INSERT OR IGNORE INTO {TablaSilencio} (Id) VALUES (1);",
                transaccion);

            foreach (CambioSincro cambio in
                     cambios)
            {
                resultado.MayorSeq =
                    Math.Max(
                        resultado.MayorSeq,
                        cambio.Seq);

                try
                {
                    if (AplicarCambio(
                            conexion,
                            transaccion,
                            cambio))
                    {
                        resultado.Aplicados++;
                    }
                    else
                    {
                        resultado.Ignorados++;
                    }
                }
                catch (Exception error)
                {
                    // Un cambio con datos incompatibles no debe
                    // tumbar el lote entero. Se guarda el último error
                    // para poder diagnosticar desde Configuración.
                    resultado.Ignorados++;

                    try
                    {
                        using var anotar =
                            conexion.CreateCommand();

                        anotar.Transaction =
                            transaccion;

                        anotar.CommandText =
                            "INSERT INTO SCCosmos_SincroEstado (Clave, Valor) " +
                            "VALUES ('UltimoError', $valor) " +
                            "ON CONFLICT (Clave) DO UPDATE SET Valor = excluded.Valor;";

                        string texto =
                            cambio.Tabla + "/" + cambio.Clave + ": " +
                            error.GetType().Name + ": " + error.Message;

                        anotar.Parameters.AddWithValue(
                            "$valor",
                            texto.Length > 300
                                ? texto.Substring(0, 300)
                                : texto);

                        anotar.ExecuteNonQuery();
                    }
                    catch
                    {
                    }
                }
            }

            Ejecutar(
                conexion,
                $"DELETE FROM {TablaSilencio};",
                transaccion);

            transaccion.Commit();
        }
        catch
        {
            transaccion.Rollback();

            throw;
        }

        return resultado;
    }


    private bool AplicarCambio(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        CambioSincro cambio)
    {
        string claves =
            ClavesDeTabla(
                conexion,
                cambio.Tabla);

        if (claves.Length == 0)
        {
            // La tabla no existe en esta base.
            return false;
        }

        // Las filas de «base» solo rellenan huecos: si la fila ya existe
        // aquí (aunque nunca se haya registrado un cambio), no se toca.
        if (string.Equals(
                cambio.Tipo,
                "B",
                StringComparison.Ordinal)
            &&
            ExisteFila(
                conexion,
                transaccion,
                cambio.Tabla,
                claves,
                cambio.Clave))
        {
            return false;
        }

        // ---- ¿Es más antiguo que lo que ya hay? ----

        string? momentoActual =
            null;

        string? origenActual =
            null;

        using (var cmd =
               conexion.CreateCommand())
        {
            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                $"""
                SELECT Momento, Origen
                FROM {TablaCambios}
                WHERE Tabla = $tabla AND Clave = $clave
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue(
                "$tabla",
                cambio.Tabla);

            cmd.Parameters.AddWithValue(
                "$clave",
                cambio.Clave);

            using var lector =
                cmd.ExecuteReader();

            if (lector.Read())
            {
                momentoActual =
                    lector.GetString(0);

                origenActual =
                    lector.GetString(1);
            }
        }

        if (momentoActual is not null)
        {
            int comparacion =
                string.CompareOrdinal(
                    cambio.Momento,
                    momentoActual);

            if (comparacion < 0)
            {
                return false;
            }

            if (comparacion == 0
                &&
                string.CompareOrdinal(
                    cambio.Origen,
                    origenActual)
                <= 0)
            {
                return false;
            }
        }

        // ---- Aplicar la fila ----

        if (cambio.Tipo == "D")
        {
            Borrar(
                conexion,
                transaccion,
                cambio.Tabla,
                claves,
                cambio.Clave);
        }
        else
        {
            Dictionary<string, object?> fila =
                cambio.Fila
                ?? new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

            if (fila.Count == 0)
            {
                return false;
            }

            EscribirFila(
                conexion,
                transaccion,
                cambio.Tabla,
                fila);
        }

        // ---- Anotar el cambio (con el momento y origen
        //      originales, para poder reenviarlo tal cual) ----

        using (var cmd =
               conexion.CreateCommand())
        {
            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                $"""
                UPDATE {TablaContador}
                SET Valor = Valor + 1;

                INSERT INTO {TablaCambios}
                    (Tabla, Clave, Tipo, Momento, Origen, Seq)
                VALUES
                    ($tabla, $clave, $tipo, $momento, $origen,
                     (SELECT Valor FROM {TablaContador}))
                ON CONFLICT (Tabla, Clave)
                DO UPDATE SET
                    Tipo = excluded.Tipo,
                    Momento = excluded.Momento,
                    Origen = excluded.Origen,
                    Seq = excluded.Seq;
                """;

            cmd.Parameters.AddWithValue(
                "$tabla",
                cambio.Tabla);

            cmd.Parameters.AddWithValue(
                "$clave",
                cambio.Clave);

            cmd.Parameters.AddWithValue(
                "$tipo",
                cambio.Tipo);

            cmd.Parameters.AddWithValue(
                "$momento",
                cambio.Momento);

            cmd.Parameters.AddWithValue(
                "$origen",
                cambio.Origen);

            cmd.ExecuteNonQuery();
        }

        return true;
    }


    // ========================================================
    // MARCAS DE AGUA POR DISPOSITIVO
    // ========================================================

    /// <summary>
    /// Olvida hasta dónde se había intercambiado con cada dispositivo:
    /// la próxima conexión vuelve a enviar y recibir el estado completo.
    /// Cada cambio se aplica solo si es más reciente que el que ya hay,
    /// así que sirve para reparar un dispositivo con datos viejos.
    /// </summary>
    public void ReiniciarIntercambio()
    {
        using var conexion =
            Abrir();

        Ejecutar(
            conexion,
            $"DELETE FROM {TablaPares};");
    }


    public (long UltimoEnviado, long UltimoRecibido) ObtenerPar(
        string idPar)
    {
        using var conexion =
            Abrir();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"""
            SELECT UltimoEnviado, UltimoRecibido
            FROM {TablaPares}
            WHERE Dispositivo = $id
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$id",
            idPar);

        using var lector =
            cmd.ExecuteReader();

        if (lector.Read())
        {
            return (
                lector.GetInt64(0),
                lector.GetInt64(1));
        }

        return (0, 0);
    }


    public void GuardarUltimoEnviado(
        string idPar,
        long hasta)
    {
        GuardarPar(
            idPar,
            "UltimoEnviado",
            hasta);
    }


    public void GuardarUltimoRecibido(
        string idPar,
        long hasta)
    {
        GuardarPar(
            idPar,
            "UltimoRecibido",
            hasta);
    }


    private void GuardarPar(
        string idPar,
        string columna,
        long hasta)
    {
        using var conexion =
            Abrir();

        Ejecutar(
            conexion,
            $"""
            INSERT INTO {TablaPares} (Dispositivo, {columna})
            VALUES ($id, $hasta)
            ON CONFLICT (Dispositivo)
            DO UPDATE SET {columna} =
                MAX({columna}, excluded.{columna});
            """,
            null,
            ("$id", idPar),
            ("$hasta", hasta));
    }


    public List<ParSincro> ObtenerPares()
    {
        var pares =
            new List<ParSincro>();

        using var conexion =
            Abrir();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"""
            SELECT Dispositivo, UltimoEnviado, UltimoRecibido
            FROM {TablaPares}
            ORDER BY Dispositivo;
            """;

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            pares.Add(
                new ParSincro
                {
                    Dispositivo =
                        lector.GetString(0),

                    UltimoEnviado =
                        lector.GetInt64(1),

                    UltimoRecibido =
                        lector.GetInt64(2)
                });
        }

        return pares;
    }


    // ========================================================
    // ESTADO INTERNO DEL MOTOR (puntos de lectura de archivos…)
    // ========================================================

    public string? ObtenerEstado(
        string clave)
    {
        try
        {
            using var conexion =
                Abrir();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                "SELECT Valor FROM SCCosmos_SincroEstado WHERE Clave = $c LIMIT 1;";

            cmd.Parameters.AddWithValue(
                "$c",
                clave);

            object? valor =
                cmd.ExecuteScalar();

            return valor is null
                   ||
                   valor == DBNull.Value
                ? null
                : Convert.ToString(
                    valor,
                    CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }


    public void GuardarEstado(
        string clave,
        string valor)
    {
        try
        {
            using var conexion =
                Abrir();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                """
                INSERT INTO SCCosmos_SincroEstado (Clave, Valor)
                VALUES ($c, $v)
                ON CONFLICT (Clave)
                DO UPDATE SET Valor = excluded.Valor;
                """;

            cmd.Parameters.AddWithValue(
                "$c",
                clave);

            cmd.Parameters.AddWithValue(
                "$v",
                valor ?? "");

            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }


    // ========================================================
    // AYUDAS DE ESQUEMA
    // ========================================================

    private static List<(string Tabla, string Claves)>
        TablasConClave(
            SqliteConnection conexion)
    {
        var tablas =
            new List<(string, string)>();

        var nombres =
            new List<string>();

        using (var cmd =
               conexion.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT name FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name NOT LIKE 'SCCosmos_%'
                ORDER BY name;
                """;

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                nombres.Add(
                    lector.GetString(0));
            }
        }

        foreach (string nombre in
                 nombres)
        {
            List<string> claves =
                ClavesDeTablaLista(
                    conexion,
                    nombre);

            if (claves.Count == 0)
            {
                continue;
            }

            tablas.Add(
                (nombre, string.Join(",", claves)));
        }

        return tablas;
    }


    private static List<string> ClavesDeTablaLista(
        SqliteConnection conexion,
        string tabla)
    {
        var claves =
            new List<string>();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"PRAGMA table_info(\"{tabla}\");";

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            int pk =
                lector.IsDBNull(5)
                    ? 0
                    : Convert.ToInt32(
                        lector.GetValue(5));

            if (pk > 0)
            {
                claves.Add(
                    lector.GetString(1));
            }
        }

        return claves;
    }


    private static string ClavesDeTabla(
        SqliteConnection conexion,
        string tabla)
    {
        return string.Join(
            ",",
            ClavesDeTablaLista(
                conexion,
                tabla));
    }


    private static List<string> NombresDe(
        SqliteConnection conexion,
        string tipo,
        string patron)
    {
        var nombres =
            new List<string>();

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"SELECT name FROM sqlite_master WHERE type = '{tipo}' AND name LIKE '{patron}';";

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            nombres.Add(
                lector.GetString(0));
        }

        return nombres;
    }


    // ========================================================
    // FILAS
    // ========================================================

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

        foreach (string parte in
                 partes)
        {
            if (texto.Length > 0)
            {
                texto.Append(
                    " || '|' || ");
            }

            texto.Append(
                $"COALESCE(CAST({lado}.\"{parte}\" AS TEXT), '')");
        }

        return texto.ToString();
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

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            ConstruirSelect(
                tabla,
                claves,
                clave,
                cmd);

        using var lector =
            cmd.ExecuteReader();

        if (!lector.Read())
        {
            return fila;
        }

        for (int i = 0;
             i < lector.FieldCount;
             i++)
        {
            fila[lector.GetName(i)] =
                lector.IsDBNull(i)
                    ? null
                    : lector.GetValue(i);
        }

        return fila;
    }


    private static bool ExisteFila(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        string claves,
        string clave)
    {
        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            ConstruirSelect(
                tabla,
                claves,
                clave,
                cmd);

        using var lector =
            cmd.ExecuteReader();

        return lector.Read();
    }


    private static string ConstruirSelect(
        string tabla,
        string claves,
        string clave,
        SqliteCommand cmd)
    {
        string[] partes =
            claves.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (partes.Length == 1)
        {
            cmd.Parameters.AddWithValue(
                "$clave",
                ValorDeClave(
                    clave));

            return $"SELECT * FROM \"{tabla}\" WHERE \"{partes[0]}\" = $clave LIMIT 1;";
        }

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
                $"COALESCE(CAST(\"{partes[i]}\" AS TEXT), '') = $p{i}");

            cmd.Parameters.AddWithValue(
                $"$p{i}",
                valores[i]);
        }

        return $"SELECT * FROM \"{tabla}\" WHERE {string.Join(" AND ", condiciones)} LIMIT 1;";
    }


    private static object ValorDeClave(
        string clave)
    {
        return long.TryParse(
            clave,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out long numero)
            ? numero
            : clave;
    }


    private static void EscribirFila(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        Dictionary<string, object?> fila)
    {
        // Solo columnas que existen en la tabla local.
        HashSet<string> columnas =
            ColumnasDe(
                conexion,
                tabla);

        var nombres =
            new List<string>();

        var parametros =
            new List<string>();

        var valores =
            new List<object?>();

        int indice =
            0;

        foreach (KeyValuePair<string, object?> campo in
                 fila)
        {
            if (!columnas.Contains(
                    campo.Key))
            {
                continue;
            }

            nombres.Add(
                $"\"{campo.Key}\"");

            parametros.Add(
                $"$v{indice}");

            valores.Add(
                Normalizar(
                    campo.Value));

            indice++;
        }

        if (nombres.Count == 0)
        {
            return;
        }

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            $"INSERT OR REPLACE INTO \"{tabla}\" ({string.Join(", ", nombres)}) VALUES ({string.Join(", ", parametros)});";

        for (int i = 0;
             i < valores.Count;
             i++)
        {
            cmd.Parameters.AddWithValue(
                $"$v{i}",
                valores[i]
                ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }


    private static void Borrar(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        string claves,
        string clave)
    {
        string[] partes =
            claves.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        if (partes.Length == 1)
        {
            cmd.CommandText =
                $"DELETE FROM \"{tabla}\" WHERE \"{partes[0]}\" = $clave;";

            cmd.Parameters.AddWithValue(
                "$clave",
                ValorDeClave(
                    clave));

            cmd.ExecuteNonQuery();

            return;
        }

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
                $"COALESCE(CAST(\"{partes[i]}\" AS TEXT), '') = $p{i}");

            cmd.Parameters.AddWithValue(
                $"$p{i}",
                valores[i]);
        }

        cmd.CommandText =
            $"DELETE FROM \"{tabla}\" WHERE {string.Join(" AND ", condiciones)};";

        cmd.ExecuteNonQuery();
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

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            columnas.Add(
                lector.GetString(1));
        }

        return columnas;
    }


    private static object? Normalizar(
        object? valor)
    {
        if (valor is null
            ||
            valor is DBNull)
        {
            return null;
        }

        return valor;
    }


    // ========================================================
    // EJECUTAR
    // ========================================================

    private static void Ejecutar(
        SqliteConnection conexion,
        string sql,
        SqliteTransaction? transaccion = null,
        params (string Nombre, object? Valor)[] parametros)
    {
        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            sql;

        foreach ((string nombre, object? valor) in
                 parametros)
        {
            cmd.Parameters.AddWithValue(
                nombre,
                valor
                ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}


// ============================================================
// MODELOS
// ============================================================

public sealed class CambioSincro
{
    public string Tabla
    {
        get;
        set;
    } = "";

    public string Clave
    {
        get;
        set;
    } = "";

    /// <summary>'I' alta, 'U' modificación, 'D' borrado.</summary>
    public string Tipo
    {
        get;
        set;
    } = "U";

    /// <summary>Momento original UTC (milisegundos).</summary>
    public string Momento
    {
        get;
        set;
    } = "";

    /// <summary>Dispositivo que hizo el cambio original.</summary>
    public string Origen
    {
        get;
        set;
    } = "";

    public long Seq
    {
        get;
        set;
    }

    /// <summary>Datos de la fila (null en borrados).</summary>
    public Dictionary<string, object?>? Fila
    {
        get;
        set;
    }
}


public sealed class ResultadoAplicacion
{
    public int Aplicados
    {
        get;
        set;
    }

    public int Ignorados
    {
        get;
        set;
    }

    public long MayorSeq
    {
        get;
        set;
    }
}


public sealed class ParSincro
{
    public string Dispositivo
    {
        get;
        set;
    } = "";

    public long UltimoEnviado
    {
        get;
        set;
    }

    public long UltimoRecibido
    {
        get;
        set;
    }
}
