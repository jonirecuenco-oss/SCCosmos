using System.Text;
using System.Text.Json.Nodes;

namespace SCCosmos.Sincro;

// ============================================================
// CANAL POR CARPETA (FUERA DE CASA)
//
// Cada dispositivo escribe SUS cambios (cifrados) en un archivo
// propio dentro de la carpeta compartida, y lee los de los demás.
// Como cada archivo tiene un único escritor, no puede haber
// conflictos; la carpeta (la que el usuario ya sincroniza) los
// lleva también por datos móviles sin configurar nada.
//
// El que escribe no avanza su "marca de enviado" hasta que el
// otro confirma con un mensaje "recibido" (también en la carpeta).
// ============================================================

public interface ICanalArchivosSincro
{
    Task<List<string>> ListarAsync(
        string prefijo);

    /// <summary>
    /// Como ListarAsync, pero con la «firma» de cada archivo (tamaño y
    /// fecha de modificación) para saber si ha cambiado sin leerlo.
    /// Firma null: no se sabe (el archivo se leerá siempre).
    /// </summary>
    Task<Dictionary<string, string?>> ListarConFirmasAsync(
        string prefijo);

    Task<byte[]?> LeerAsync(
        string nombre);

    Task<bool> EscribirAsync(
        string nombre,
        IReadOnlyList<string> lineas);

    Task<bool> AgregarAsync(
        string nombre,
        IReadOnlyList<string> lineas);
}


public sealed class CanalArchivosSincro
{
    private const long LimiteCompactacion =
        2 * 1024 * 1024;

    private const int MaxCambiosPorPase =
        400;

    // Cambios recientes que conserva el archivo propio. Los más antiguos
    // ya no caben (el archivo se compacta a ~2 MB): quien los necesite se
    // pone al día por la red.
    private const long VentanaCarpeta =
        2000;

    /// <summary>
    /// Enganche opcional para anotar mediciones en el registro de la app
    /// (Perf). Si nadie se engancha, no se anota nada.
    /// </summary>
    public static Action<string, long>? Medicion;

    // Firma (tamaño y fecha) de cada archivo ajeno en su última lectura,
    // para no volver a leerlo si no ha cambiado.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Firma, DateTime Hora)> _firmasLeidas =
        new(StringComparer.OrdinalIgnoreCase);

    // Aunque la firma no cambie, cada archivo se relee cada 2 minutos.
    private static readonly TimeSpan RelecturaSegura =
        TimeSpan.FromMinutes(2);


    private readonly SincroConfiguracion _config;

    private readonly SincroAlmacen _almacen;

    // Marca de la base en la última pasada sin nada que publicar (-1: hay
    // que consultar) y hora de esa consulta.
    private long _marcaSinPendientes =
        -1;

    private DateTime _ultimaConsulta =
        DateTime.MinValue;

    private readonly ICanalArchivosSincro _canal;

    private readonly byte[] _clave;


    public CanalArchivosSincro(
        SincroConfiguracion config,
        SincroAlmacen almacen,
        ICanalArchivosSincro canal)
    {
        _config =
            config;

        _almacen =
            almacen;

        _canal =
            canal;

        _clave =
            CanalCifrado.DerivarClave(
                config.Clave);
    }


    public string UltimoEstado
    {
        get;
        private set;
    } = "";


    /// <summary>
    /// Total de registros aplicados por este canal (para avisar a
    /// las pantallas).
    /// </summary>
    public long TotalAplicado
    {
        get;
        private set;
    }


    private string MiArchivo =>
        $"cambios-{_config.IdDispositivo}.jsonl";


    // ========================================================
    // UN PASE (ESCRIBIR + LEER)
    // ========================================================

    public async Task EjecutarPaseAsync()
    {
        try
        {
            await EscribirPendientesAsync();
        }
        catch
        {
        }

        try
        {
            await LeerParesAsync();
        }
        catch
        {
        }
    }


    /// <summary>
    /// Solo publica los cambios propios pendientes, sin leer los de los
    /// demás: es lo que hace falta al vaciar antes de cerrar o de pasar a
    /// segundo plano. Devuelve si había algo que publicar.
    /// </summary>
    public async Task<bool> PublicarPendientesAsync()
    {
        try
        {
            return await EscribirPendientesAsync();
        }
        catch
        {
            return true;
        }
    }


    // ========================================================
    // ESCRIBIR MIS CAMBIOS PENDIENTES
    // ========================================================

    private async Task<bool> EscribirPendientesAsync()
    {
        // Sin cambios en la base desde la última pasada sin nada que
        // publicar: no se recorre el registro de cambios (cada 6 s y al
        // vaciar). Por seguridad se consulta igualmente cada 30 s.
        long marca =
            _almacen.MarcaCambios();

        if (marca == _marcaSinPendientes
            && (DateTime.UtcNow - _ultimaConsulta).TotalSeconds < 30)
        {
            return false;
        }

        _ultimaConsulta =
            DateTime.UtcNow;

        long escrito =
            long.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoSeqEscrito"),
                out long valor)
                ? valor
                : 0;

        // Lo más antiguo que la ventana de la carpeta ya no se publica: no
        // cabría en el archivo y antes la carpeta se quedaba atascada
        // republicando cambios viejos.
        long ultimoSeq =
            _almacen.ObtenerUltimoSeq();

        if (escrito < ultimoSeq - VentanaCarpeta)
        {
            escrito =
                ultimoSeq - VentanaCarpeta;
        }

        List<CambioSincro> pendientes =
            _almacen.ObtenerPendientes(
                escrito);

        if (pendientes.Count == 0)
        {
            _marcaSinPendientes =
                marca;

            return false;
        }

        _marcaSinPendientes =
            -1;

        var lineas =
            new List<string>();

        byte[]? actual =
            await _canal.LeerAsync(
                MiArchivo);

        if (actual is null)
        {
            lineas.Add(
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Hola(
                        _config),
                    _clave));
        }

        int totalCambios =
            0;

        while (pendientes.Count > 0
               &&
               totalCambios < MaxCambiosPorPase)
        {
            lineas.Add(
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Lote(
                        pendientes),
                    _clave));

            totalCambios +=
                pendientes.Count;

            escrito =
                pendientes[^1].Seq;

            if (totalCambios
                >= MaxCambiosPorPase)
            {
                break;
            }

            pendientes =
                _almacen.ObtenerPendientes(
                    escrito);
        }

        bool agregado;

        if (actual is null)
        {
            agregado =
                await _canal.EscribirAsync(
                    MiArchivo,
                    lineas);
        }
        else
        {
            agregado =
                await _canal.AgregarAsync(
                    MiArchivo,
                    lineas);
        }

        if (!agregado)
        {
            // No se pudo escribir: se reintentará en el próximo pase.
            return true;
        }

        _almacen.GuardarEstado(
            "ArchivoSeqEscrito",
            escrito.ToString());

        UltimoEstado =
            $"Carpeta: {totalCambios} cambios publicados.";

        // ---- Compactación si el archivo crece demasiado ----

        actual =
            await _canal.LeerAsync(
                MiArchivo);

        if (actual is not null
            &&
            actual.LongLength > LimiteCompactacion)
        {
            await CompactarAsync();
        }

        return true;
    }


    /// <summary>
    /// Reescribe el archivo propio dejando solo los cambios recientes ya
    /// publicados que algún dispositivo activo puede necesitar todavía. La
    /// posición publicada no retrocede nunca.
    /// </summary>
    private async Task CompactarAsync()
    {
        long escrito =
            long.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoSeqEscrito"),
                out long valor)
                ? valor
                : 0;

        long minimo =
            long.MaxValue;

        foreach (ParSincro par in
                 _almacen.ObtenerPares())
        {
            // Los dispositivos desmarcados no cuentan (tampoco se leen).
            if (!_config.EstaHabilitado(
                    par.Dispositivo))
            {
                continue;
            }

            minimo =
                Math.Min(
                    minimo,
                    par.UltimoEnviado);
        }

        // Antes se rehacía desde el dispositivo más atrasado (uno
        // abandonado lo dejaba en 0) y la posición publicada retrocedía:
        // la carpeta volvía a publicar lo mismo una y otra vez y nunca
        // llegaba a los cambios recientes. Ahora se conservan como mucho
        // los últimos cambios ya publicados.
        long desde =
            Math.Max(
                minimo == long.MaxValue ? 0 : minimo,
                escrito - VentanaCarpeta);

        var lineas =
            new List<string>
            {
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Hola(
                        _config),
                    _clave)
            };

        List<CambioSincro> pendientes =
            _almacen.ObtenerPendientes(
                desde);

        while (pendientes.Count > 0)
        {
            List<CambioSincro> publicados =
                pendientes
                    .Where(c => c.Seq <= escrito)
                    .ToList();

            if (publicados.Count == 0)
            {
                break;
            }

            lineas.Add(
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Lote(
                        publicados),
                    _clave));

            if (publicados.Count < pendientes.Count)
            {
                break;
            }

            pendientes =
                _almacen.ObtenerPendientes(
                    publicados[^1].Seq);
        }

        // Si aun así no cabe holgado, se quitan los lotes más antiguos.
        long total =
            lineas.Sum(l => (long)l.Length);

        while (lineas.Count > 2
               && total > LimiteCompactacion / 2)
        {
            total -= lineas[1].Length;
            lineas.RemoveAt(1);
        }

        await _canal.EscribirAsync(
            MiArchivo,
            lineas);
    }


    // ========================================================
    // LEER LOS ARCHIVOS DE LOS DEMÁS
    // ========================================================

    private async Task LeerParesAsync()
    {
        var reloj =
            System.Diagnostics.Stopwatch.StartNew();

        long bytesLeidos =
            0;

        int archivosLeidos =
            0;

        Dictionary<string, string?> archivos =
            await _canal.ListarConFirmasAsync(
                "cambios-");

        foreach ((string nombre, string? firma) in
                 archivos)
        {
            try
            {
                if (!nombre.EndsWith(
                        ".jsonl",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    string.Equals(
                        nombre,
                        MiArchivo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Si el archivo no ha cambiado (mismo tamaño y fecha) desde
                // la última lectura, no se vuelve a leer: antes se leían
                // enteros todos en cada pasada (~6 MB de memoria temporal
                // cada 6 s). Por seguridad se relee igualmente cada 2 min.
                if (firma is not null
                    && _firmasLeidas.TryGetValue(
                        nombre,
                        out var leida)
                    && leida.Firma == firma
                    && DateTime.UtcNow - leida.Hora < RelecturaSegura)
                {
                    continue;
                }

                byte[]? bytes =
                    await _canal.LeerAsync(
                        nombre);

                if (bytes is null
                    ||
                    bytes.Length == 0)
                {
                    continue;
                }

                bytesLeidos +=
                    bytes.Length;

                archivosLeidos++;

                // Si algo falla al procesarlo, se olvida (en el catch) y
                // se vuelve a leer en el próximo pase.
                if (firma is not null)
                {
                    _firmasLeidas[nombre] =
                        (firma, DateTime.UtcNow);
                }

                string texto =
                    Encoding.UTF8.GetString(
                        bytes);

                string[] lineas =
                    texto.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries);

                string idPar =
                    nombre
                        .Replace(
                            "cambios-",
                            "",
                            StringComparison.OrdinalIgnoreCase)
                        .Replace(
                            ".jsonl",
                            "",
                            StringComparison.OrdinalIgnoreCase);

                if (idPar.Length == 0
                    ||
                    string.Equals(
                        idPar,
                        _config.IdDispositivo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Dispositivo desmarcado: ni se lee ni se responde.
                if (!_config.EstaHabilitado(
                        idPar))
                {
                    continue;
                }

                string claveEstado =
                    "ArchivoLineas::" + nombre;

                int leidas =
                    int.TryParse(
                        _almacen.ObtenerEstado(
                            claveEstado),
                        out int valor)
                        ? valor
                        : 0;

                // Si el otro compactó el archivo (o lo recreó), se
                // relee desde el principio: aplicar es idempotente.
                if (lineas.Length < leidas)
                {
                    leidas =
                        0;
                }

                // Contar líneas no basta: si el otro compacta y el archivo
                // vuelve a crecer por encima de lo ya leído antes de que lo
                // leamos, se saltarían líneas. Por eso se guarda también la
                // huella de la última línea leída: si ya no coincide, el
                // archivo se reescribió y se relee desde el principio.
                string claveHuella =
                    "ArchivoHuella::" + nombre;

                if (leidas > 0)
                {
                    string huellaActual =
                        Huella(
                            lineas[leidas - 1]);

                    string? huellaGuardada =
                        _almacen.ObtenerEstado(
                            claveHuella);

                    if (huellaGuardada is null)
                    {
                        // Primera vez con esta versión: lo que hay ahora
                        // queda como referencia.
                        _almacen.GuardarEstado(
                            claveHuella,
                            huellaActual);
                    }
                    else if (huellaGuardada != huellaActual)
                    {
                        leidas =
                            0;
                    }
                }

                if (leidas >= lineas.Length)
                {
                    continue;
                }

                var acks =
                    new List<string>();

                for (int i = leidas;
                     i < lineas.Length;
                     i++)
                {
                    string? json =
                        CanalCifrado.DescifrarMensaje(
                            lineas[i],
                            _clave);

                    if (json is null)
                    {
                        continue;
                    }

                    JsonObject? mensaje =
                        JsonNode.Parse(
                            json)
                        as JsonObject;

                    if (mensaje is null)
                    {
                        continue;
                    }

                    string clase =
                        mensaje["t"]
                            ?.GetValue<string>()
                        ?? "";

                    if (clase == "lote")
                    {
                        List<CambioSincro> lote =
                            SincroMensajes.LeerLote(
                                mensaje);

                        if (lote.Count == 0)
                        {
                            continue;
                        }

                        ResultadoAplicacion resultado =
                            _almacen.AplicarLote(
                                lote);

                        _almacen.GuardarUltimoRecibido(
                            idPar,
                            resultado.MayorSeq);

                        acks.Add(
                            CanalCifrado.CifrarMensaje(
                                SincroMensajes.RecibidoPara(
                                    idPar,
                                    resultado.MayorSeq),
                                _clave));

                        if (resultado.Aplicados > 0)
                        {
                            TotalAplicado +=
                                resultado.Aplicados;

                            UltimoEstado =
                                $"Carpeta: {resultado.Aplicados} " +
                                $"cambios recibidos de {idPar[..8]}…";
                        }
                    }
                    else if (clase == "recibido")
                    {
                        string para =
                            mensaje["para"]
                                ?.GetValue<string>()
                            ?? "";

                        long hasta =
                            mensaje["hasta"]
                                ?.GetValue<long>()
                            ?? 0;

                        if (string.Equals(
                                para,
                                _config.IdDispositivo,
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            hasta > 0)
                        {
                            _almacen.GuardarUltimoEnviado(
                                idPar,
                                hasta);
                        }
                    }
                }

                _almacen.GuardarEstado(
                    claveEstado,
                    lineas.Length.ToString());

                _almacen.GuardarEstado(
                    claveHuella,
                    Huella(
                        lineas[^1]));

                if (acks.Count > 0)
                {
                    await _canal.AgregarAsync(
                        MiArchivo,
                        acks);
                }
            }
            catch
            {
                // Se reintentará en el próximo pase.
                _firmasLeidas.TryRemove(
                    nombre,
                    out _);
            }
        }

        // Solo se anota cuando se lee algún archivo: al arrancar, cuando
        // otro dispositivo cambia el suyo y en la relectura de seguridad.
        if (archivosLeidos > 0)
        {
            Medicion?.Invoke(
                $"Carpeta.Lectura {archivosLeidos} archivos, " +
                $"{bytesLeidos / 1024} KB leídos",
                reloj.ElapsedMilliseconds);
        }
    }


    /// <summary>
    /// Huella corta de una línea: permite saber si el archivo de otro
    /// dispositivo se reescribió aunque tenga las mismas o más líneas.
    /// </summary>
    private static string Huella(
        string linea)
    {
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    linea)),
            0,
            8);
    }
}
