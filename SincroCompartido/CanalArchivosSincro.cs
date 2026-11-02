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

    /// <summary>
    /// El archivo desde esa posición (en bytes) hasta el final; vacío si es
    /// más corto. Null si no se puede leer.
    /// </summary>
    Task<byte[]?> LeerDesdeAsync(
        string nombre,
        long desde);

    /// <summary>
    /// Tamaño del archivo en bytes, sin leerlo; -1 si no existe. Si existe y
    /// no se puede saber cuánto ocupa, 0.
    /// </summary>
    Task<long> TamanoAsync(
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "EjecutarPaseAsync#1");
        }

        try
        {
            await LeerParesAsync();
        }
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "EjecutarPaseAsync#2");
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
        catch (Exception errorSincro)
        {
            SincroDiagnostico.Error(errorSincro, "PublicarPendientesAsync");
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

        bool existe =
            await _canal.TamanoAsync(
                MiArchivo) >= 0;

        if (!existe)
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

        if (!existe)
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

        if (await _canal.TamanoAsync(
                MiArchivo) > LimiteCompactacion)
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

                // Basta con lo añadido desde la última lectura. Si no se puede
                // (primera vez, o el otro compactó o recreó su archivo), se lee
                // entero.
                LecturaArchivo? lectura =
                    await LeerLoNuevoAsync(
                        nombre)
                    ?? await LeerEnteroAsync(
                        nombre);

                if (lectura is null)
                {
                    continue;
                }

                bytesLeidos +=
                    lectura.Bytes;

                archivosLeidos++;

                // Si algo falla al procesarlo, se olvida (en el catch) y
                // se vuelve a leer en el próximo pase.
                if (firma is not null)
                {
                    _firmasLeidas[nombre] =
                        (firma, DateTime.UtcNow);
                }

                var acks =
                    new List<string>();

                foreach (string linea in
                         lectura.Nuevas)
                {
                    string? json =
                        CanalCifrado.DescifrarMensaje(
                            linea,
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

                if (lectura.CambiaEstado)
                {
                    _almacen.GuardarEstado(
                        "ArchivoLineas::" + nombre,
                        lectura.TotalLineas.ToString());

                    _almacen.GuardarEstado(
                        "ArchivoHuella::" + nombre,
                        Huella(
                            lectura.UltimaLinea));

                    _almacen.GuardarEstado(
                        "ArchivoPosicion::" + nombre,
                        lectura.PosicionUltima.ToString());
                }

                if (acks.Count > 0)
                {
                    await _canal.AgregarAsync(
                        MiArchivo,
                        acks);
                }
            }
            catch (Exception errorSincro)
            {
                // Se reintentará en el próximo pase.
                SincroDiagnostico.Error(errorSincro, "LeerParesAsync");
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
    /// Lo que hay que procesar de un archivo ajeno y dónde queda la lectura.
    /// </summary>
    private sealed record LecturaArchivo(
        IReadOnlyList<string> Nuevas,
        string UltimaLinea,
        long PosicionUltima,
        int TotalLineas,
        long Bytes,
        bool CambiaEstado);


    /// <summary>
    /// Lee desde el principio de la última línea leída: si sigue ahí (misma
    /// huella), lo que viene detrás es lo nuevo. Null si hay que leer el archivo
    /// entero: no hay posición guardada, o el otro lo compactó o lo recreó.
    /// </summary>
    private async Task<LecturaArchivo?> LeerLoNuevoAsync(
        string nombre)
    {
        string? huella =
            _almacen.ObtenerEstado(
                "ArchivoHuella::" + nombre);

        if (huella is null
            || !long.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoPosicion::" + nombre),
                out long posicion)
            || !int.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoLineas::" + nombre),
                out int leidas))
        {
            return null;
        }

        byte[]? cola =
            await _canal.LeerDesdeAsync(
                nombre,
                posicion);

        if (cola is null
            || cola.Length == 0)
        {
            return null;
        }

        List<(int Inicio, string Texto)> lineas =
            LineasCompletas(
                cola);

        if (lineas.Count == 0
            || Huella(lineas[0].Texto) != huella)
        {
            return null;
        }

        return new LecturaArchivo(
            lineas.Skip(1)
                .Select(l => l.Texto)
                .ToList(),
            lineas[^1].Texto,
            posicion + lineas[^1].Inicio,
            leidas + lineas.Count - 1,
            cola.Length,
            lineas.Count > 1);
    }


    private async Task<LecturaArchivo?> LeerEnteroAsync(
        string nombre)
    {
        byte[]? bytes =
            await _canal.LeerAsync(
                nombre);

        if (bytes is null
            || bytes.Length == 0)
        {
            return null;
        }

        List<(int Inicio, string Texto)> lineas =
            LineasCompletas(
                bytes);

        if (lineas.Count == 0)
        {
            return null;
        }

        int leidas =
            int.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoLineas::" + nombre),
                out int valor)
                ? valor
                : 0;

        // Si el otro compactó el archivo (o lo recreó), se
        // relee desde el principio: aplicar es idempotente.
        if (lineas.Count < leidas)
        {
            leidas =
                0;
        }

        // Contar líneas no basta: si el otro compacta y el archivo
        // vuelve a crecer por encima de lo ya leído antes de que lo
        // leamos, se saltarían líneas. Por eso se guarda también la
        // huella de la última línea leída: si ya no coincide, el
        // archivo se reescribió y se relee desde el principio.
        if (leidas > 0)
        {
            string? huellaGuardada =
                _almacen.ObtenerEstado(
                    "ArchivoHuella::" + nombre);

            if (huellaGuardada is not null
                && huellaGuardada != Huella(lineas[leidas - 1].Texto))
            {
                leidas =
                    0;
            }
        }

        return new LecturaArchivo(
            lineas.Skip(leidas)
                .Select(l => l.Texto)
                .ToList(),
            lineas[^1].Texto,
            lineas[^1].Inicio,
            lineas.Count,
            bytes.Length,
            true);
    }


    /// <summary>
    /// Las líneas terminadas y dónde empieza cada una. Lo que venga detrás del
    /// último salto de línea es una línea a medias de escribir: se leerá
    /// cuando esté completa.
    /// </summary>
    private static List<(int Inicio, string Texto)> LineasCompletas(
        byte[] bytes)
    {
        var lineas =
            new List<(int Inicio, string Texto)>();

        int inicio =
            0;

        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
            {
                continue;
            }

            if (i > inicio)
            {
                lineas.Add(
                    (inicio,
                     Encoding.UTF8.GetString(
                         bytes,
                         inicio,
                         i - inicio)));
            }

            inicio =
                i + 1;
        }

        return lineas;
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
