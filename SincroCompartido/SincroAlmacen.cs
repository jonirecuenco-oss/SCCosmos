using System.Globalization;
using System.Security.Cryptography;
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

    /// <summary>
    /// Hueco que deja este dispositivo antes de su siguiente alta; 0 si
    /// numera sin huecos (ver SaltarIds).
    /// </summary>
    private readonly long _saltoIds;


    public SincroAlmacen(
        string rutaDb,
        string idDispositivo,
        bool saltarIds = false)
    {
        _rutaDb =
            rutaDb;

        _idDispositivo =
            idDispositivo ?? "";

        _saltoIds =
            saltarIds
                ? CalcularSaltoIds(
                    _idDispositivo)
                : 0;
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "Abrir");
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

        try
        {
            EsquemaBase.AsegurarColumnasNuevas(
                conexion);
        }
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "ColumnasNuevas");
        }

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

        // Disparadores nuevos. Se rehacen en una sola transacción: es mucho
        // más rápido (un único commit en vez de ~240) y nadie llega a ver la
        // base sin disparadores, así que un cambio hecho justo en ese
        // momento no se pierde para la sincronización.
        List<string> anteriores =
            NombresDe(
                conexion,
                "trigger",
                PrefijoDisparador + "%");

        var tablas =
            TablasConClave(
                conexion);

        using (var transaccion =
               conexion.BeginTransaction())
        {
            foreach (string nombre in anteriores)
            {
                Ejecutar(
                    conexion,
                    $"DROP TRIGGER IF EXISTS \"{nombre}\";",
                    transaccion);
            }

            foreach ((string tabla, string claves) in tablas)
            {
                CrearDisparador(
                    conexion,
                    tabla,
                    claves,
                    "insert",
                    "I",
                    "NEW",
                    transaccion);

                CrearDisparador(
                    conexion,
                    tabla,
                    claves,
                    "update",
                    "U",
                    "NEW",
                    transaccion);

                CrearDisparador(
                    conexion,
                    tabla,
                    claves,
                    "delete",
                    "D",
                    "OLD",
                    transaccion);
            }

            transaccion.Commit();
        }

        try
        {
            RepararBinarios(
                conexion);
        }
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "RepararBinarios");
        }

        try
        {
            // Diferida: casi siempre no hay nada que escribir.
            using var transaccion =
                conexion.BeginTransaction(
                    deferred: true);

            SaltarIds(
                conexion,
                transaccion);

            transaccion.Commit();
        }
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "SaltarIds");
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "Asegurar");
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "SembrarFilasFaltantes");
        }
    }


    private void CrearDisparador(
        SqliteConnection conexion,
        string tabla,
        string claves,
        string momento,
        string tipo,
        string lado,
        SqliteTransaction? transaccion = null)
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
            """,
            transaccion);
    }


    // ========================================================
    // LEER CAMBIOS PENDIENTES PARA UN PAR
    // ========================================================

    /// <summary>
    /// Marca barata de «la base ha cambiado»: la fecha de escritura del
    /// archivo y de su diario WAL (dos consultas al sistema de archivos, sin
    /// abrir la base). Sirve para no recorrer el registro de cambios cada
    /// pocos segundos cuando no ha cambiado nada.
    /// </summary>
    public long MarcaCambios()
    {
        try
        {
            long marca =
                System.IO.File.GetLastWriteTimeUtc(_rutaDb).Ticks;

            string wal =
                _rutaDb + "-wal";

            if (System.IO.File.Exists(wal))
            {
                marca =
                    Math.Max(
                        marca,
                        System.IO.File.GetLastWriteTimeUtc(wal).Ticks);
            }

            return marca;
        }
        catch
        {
            // Ante la duda, se consulta.
            return DateTime.UtcNow.Ticks;
        }
    }


    /// <summary>
    /// Último número de secuencia asignado (el contador del registro).
    /// </summary>
    public long ObtenerUltimoSeq()
    {
        try
        {
            using var conexion =
                Abrir();

            using var cmd =
                conexion.CreateCommand();

            cmd.CommandText =
                $"SELECT Valor FROM {TablaContador} WHERE Id = 1;";

            object? valor =
                cmd.ExecuteScalar();

            return valor is null or DBNull
                ? 0
                : Convert.ToInt64(valor);
        }
        catch
        {
            return 0;
        }
    }


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

        // Antes de que salgan: lo que se envía deja de ser solo de este
        // dispositivo, y otro puede seguir numerando a partir de ahí.
        if (resultado.Count > 0)
        {
            try
            {
                SaltarIds(
                    conexion,
                    null,
                    resultado.Select(
                        cambio => cambio.Tabla));
            }
            catch (Exception errorSincro)
            {
                SincroDiagnostico.Error(errorSincro, "SaltarIds");
            }
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
                    catch (Exception errorSincro)
                    {
                        SincroDiagnostico.Error(errorSincro, "AplicarLote");
                    }
                }
            }

            Ejecutar(
                conexion,
                $"DELETE FROM {TablaSilencio};",
                transaccion);

            if (resultado.Aplicados > 0)
            {
                try
                {
                    SaltarIds(
                        conexion,
                        transaccion,
                        cambios.Select(
                            cambio => cambio.Tabla));
                }
                catch (Exception errorSincro)
                {
                    SincroDiagnostico.Error(errorSincro, "SaltarIds");
                }
            }

            transaccion.Commit();
        }
        catch
        {
            transaccion.Rollback();

            throw;
        }

        return resultado;
    }


    // ========================================================
    // ALTAS SIN CHOCAR ENTRE DISPOSITIVOS
    //
    // SQLite da a cada alta el mayor Id que conoce más uno, así que
    // dos dispositivos que dan de alta sin verse eligen el mismo, y al
    // sincronizarse la fila más reciente sustituye a la otra, que se
    // pierde sin aviso.
    //
    // Los móviles dejan un hueco antes de su siguiente alta, distinto en
    // cada uno: lo amplían al arrancar, al recibir cambios y antes de
    // enviar los suyos, de modo que los demás no lleguen a ese Id por su
    // cuenta. sqlite_sequence no se sincroniza, así que no se anota como
    // cambio. El PC sigue numerando como siempre.
    // ========================================================

    private const long SaltoIdsMinimo =
        100_000;

    private const long SaltoIdsPaso =
        100;

    private const uint SaltoIdsVariantes =
        1_000;

    /// <summary>
    /// Las apps leen los Id como int: no se salta más allá de este límite.
    /// </summary>
    private const long IdsLimite =
        1_500_000_000;


    private static long CalcularSaltoIds(
        string idDispositivo)
    {
        byte[] huella =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    idDispositivo));

        return SaltoIdsMinimo
               + BitConverter.ToUInt32(huella, 0) % SaltoIdsVariantes
               * SaltoIdsPaso;
    }


    private void SaltarIds(
        SqliteConnection conexion,
        SqliteTransaction? transaccion,
        IEnumerable<string>? soloTablas = null)
    {
        if (_saltoIds == 0)
        {
            return;
        }

        HashSet<string>? filtro =
            soloTablas is null
                ? null
                : new HashSet<string>(
                    soloTablas,
                    StringComparer.OrdinalIgnoreCase);

        var tablas =
            new List<string>();

        using (var cmd =
               conexion.CreateCommand())
        {
            cmd.Transaction =
                transaccion;

            cmd.CommandText =
                "SELECT name FROM sqlite_master " +
                "WHERE type = 'table' AND sql LIKE '%AUTOINCREMENT%' AND name NOT LIKE 'SCCosmos_%';";

            using var lector =
                cmd.ExecuteReader();

            while (lector.Read())
            {
                string tabla =
                    lector.GetString(0);

                if (filtro is null
                    || filtro.Contains(tabla))
                {
                    tablas.Add(
                        tabla);
                }
            }
        }

        foreach (string tabla in tablas)
        {
            long mayor;

            long? secuencia;

            using (var cmd =
                   conexion.CreateCommand())
            {
                cmd.Transaction =
                    transaccion;

                cmd.CommandText =
                    $"""
                    SELECT COALESCE((SELECT MAX(rowid) FROM "{tabla}"), 0),
                           (SELECT seq FROM sqlite_sequence WHERE name = $tabla);
                    """;

                cmd.Parameters.AddWithValue(
                    "$tabla",
                    tabla);

                using var lector =
                    cmd.ExecuteReader();

                lector.Read();

                mayor =
                    lector.GetInt64(0);

                secuencia =
                    lector.IsDBNull(1)
                        ? null
                        : lector.GetInt64(1);
            }

            long objetivo =
                mayor + _saltoIds;

            if (objetivo > IdsLimite
                || secuencia >= objetivo)
            {
                continue;
            }

            using var escribir =
                conexion.CreateCommand();

            escribir.Transaction =
                transaccion;

            escribir.CommandText =
                secuencia is null
                    ? "INSERT INTO sqlite_sequence (name, seq) VALUES ($tabla, $seq);"
                    : "UPDATE sqlite_sequence SET seq = $seq WHERE name = $tabla;";

            escribir.Parameters.AddWithValue(
                "$tabla",
                tabla);

            escribir.Parameters.AddWithValue(
                "$seq",
                objetivo);

            escribir.ExecuteNonQuery();
        }
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "GuardarEstado");
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
        Dictionary<string, string> columnas =
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
            if (!columnas.TryGetValue(
                    campo.Key,
                    out string? tipo))
            {
                continue;
            }

            nombres.Add(
                $"\"{campo.Key}\"");

            parametros.Add(
                $"$v{indice}");

            valores.Add(
                RestaurarBinario(
                    Normalizar(
                        campo.Value),
                    tipo));

            indice++;
        }

        if (nombres.Count == 0)
        {
            return;
        }

        // Un dispositivo con una versión anterior no conoce las columnas
        // nuevas, y REPLACE las dejaría con su valor predeterminado: lo que
        // haya aquí se conserva.
        if (columnas.Count > nombres.Count)
        {
            ConservarColumnasAusentes(
                conexion,
                transaccion,
                tabla,
                fila,
                columnas,
                nombres,
                parametros,
                valores);
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


    private static void ConservarColumnasAusentes(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        string tabla,
        Dictionary<string, object?> fila,
        Dictionary<string, string> columnas,
        List<string> nombres,
        List<string> parametros,
        List<object?> valores)
    {
        var recibidas =
            new HashSet<string>(
                fila.Keys,
                StringComparer.OrdinalIgnoreCase);

        List<string> ausentes =
            columnas.Keys
                .Where(c => !recibidas.Contains(c))
                .ToList();

        string[] claves =
            ClavesDeTabla(
                    conexion,
                    tabla)
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        if (ausentes.Count == 0
            || claves.Length == 0
            || !claves.All(recibidas.Contains))
        {
            return;
        }

        using var cmd =
            conexion.CreateCommand();

        cmd.Transaction =
            transaccion;

        cmd.CommandText =
            $"SELECT {string.Join(", ", ausentes.Select(c => $"\"{c}\""))} FROM \"{tabla}\" " +
            $"WHERE {string.Join(" AND ", claves.Select((c, i) => $"\"{c}\" = $k{i}"))};";

        for (int i = 0; i < claves.Length; i++)
        {
            string nombreClave =
                fila.Keys.First(
                    k => string.Equals(
                        k,
                        claves[i],
                        StringComparison.OrdinalIgnoreCase));

            cmd.Parameters.AddWithValue(
                $"$k{i}",
                Normalizar(fila[nombreClave]) ?? DBNull.Value);
        }

        using var lector =
            cmd.ExecuteReader();

        if (!lector.Read())
        {
            return;
        }

        for (int i = 0; i < ausentes.Count; i++)
        {
            nombres.Add(
                $"\"{ausentes[i]}\"");

            parametros.Add(
                $"$v{valores.Count}");

            valores.Add(
                lector.IsDBNull(i)
                    ? null
                    : lector.GetValue(i));
        }
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


    /// <summary>
    /// Columnas de una tabla con su tipo declarado.
    /// </summary>
    private static Dictionary<string, string> ColumnasDe(
        SqliteConnection conexion,
        string tabla)
    {
        var columnas =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"PRAGMA table_info(\"{tabla}\");";

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            columnas[lector.GetString(1)] =
                lector.IsDBNull(2)
                    ? ""
                    : lector.GetString(2);
        }

        return columnas;
    }


    /// <summary>
    /// Los binarios viajan en base64 (SincroMensajes): en las columnas BLOB se
    /// devuelven a binario. Un texto que no sea base64 se deja como llegó.
    /// </summary>
    private static object? RestaurarBinario(
        object? valor,
        string tipoColumna)
    {
        return valor is string texto
               && EsTipoBinario(tipoColumna)
               && DesdeBase64(texto) is byte[] bytes
            ? bytes
            : valor;
    }


    private static bool EsTipoBinario(
        string tipoColumna)
    {
        return string.Equals(
            tipoColumna,
            "BLOB",
            StringComparison.OrdinalIgnoreCase);
    }


    private static byte[]? DesdeBase64(
        string texto)
    {
        var bytes =
            new byte[(texto.Length / 4 + 1) * 3];

        return Convert.TryFromBase64String(
            texto,
            bytes,
            out int escritos)
            ? bytes[..escritos]
            : null;
    }


    /// <summary>
    /// Los binarios recibidos antes de RestaurarBinario quedaron como texto
    /// base64 en las columnas BLOB: se corrigen sin anotarlos como cambios,
    /// porque el contenido es el mismo y no hay nada que reenviar.
    /// </summary>
    private static void RepararBinarios(
        SqliteConnection conexion)
    {
        foreach ((string tabla, string _) in
                 TablasConClave(
                     conexion))
        {
            foreach ((string columna, string tipo) in
                     ColumnasDe(
                         conexion,
                         tabla))
            {
                if (!EsTipoBinario(
                        tipo))
                {
                    continue;
                }

                var reparar =
                    new List<(long Fila, byte[] Bytes)>();

                using (var cmd =
                       conexion.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT rowid, \"{columna}\" FROM \"{tabla}\" WHERE typeof(\"{columna}\") = 'text';";

                    using var lector =
                        cmd.ExecuteReader();

                    while (lector.Read())
                    {
                        if (DesdeBase64(
                                lector.GetString(1)) is byte[] bytes)
                        {
                            reparar.Add(
                                (lector.GetInt64(0), bytes));
                        }
                    }
                }

                if (reparar.Count == 0)
                {
                    continue;
                }

                using var transaccion =
                    conexion.BeginTransaction();

                Ejecutar(
                    conexion,
                    $"INSERT OR IGNORE INTO {TablaSilencio} (Id) VALUES (1);",
                    transaccion);

                foreach ((long fila, byte[] bytes) in reparar)
                {
                    using var cmd =
                        conexion.CreateCommand();

                    cmd.Transaction =
                        transaccion;

                    cmd.CommandText =
                        $"UPDATE \"{tabla}\" SET \"{columna}\" = $bytes WHERE rowid = $fila;";

                    cmd.Parameters.AddWithValue(
                        "$bytes",
                        bytes);

                    cmd.Parameters.AddWithValue(
                        "$fila",
                        fila);

                    cmd.ExecuteNonQuery();
                }

                Ejecutar(
                    conexion,
                    $"DELETE FROM {TablaSilencio};",
                    transaccion);

                transaccion.Commit();
            }
        }
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
