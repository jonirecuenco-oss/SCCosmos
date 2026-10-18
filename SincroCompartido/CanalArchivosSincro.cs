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


    private readonly SincroConfiguracion _config;

    private readonly SincroAlmacen _almacen;

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


    // ========================================================
    // ESCRIBIR MIS CAMBIOS PENDIENTES
    // ========================================================

    private async Task EscribirPendientesAsync()
    {
        long escrito =
            long.TryParse(
                _almacen.ObtenerEstado(
                    "ArchivoSeqEscrito"),
                out long valor)
                ? valor
                : 0;

        List<CambioSincro> pendientes =
            _almacen.ObtenerPendientes(
                escrito);

        if (pendientes.Count == 0)
        {
            return;
        }

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
            return;
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
    }


    /// <summary>
    /// Reescribe el archivo propio dejando solo lo que algún
    /// dispositivo no haya confirmado todavía.
    /// </summary>
    private async Task CompactarAsync()
    {
        long minimo =
            long.MaxValue;

        foreach (ParSincro par in
                 _almacen.ObtenerPares())
        {
            minimo =
                Math.Min(
                    minimo,
                    par.UltimoEnviado);
        }

        if (minimo == long.MaxValue)
        {
            minimo =
                0;
        }

        var lineas =
            new List<string>
            {
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Hola(
                        _config),
                    _clave)
            };

        long ultimo =
            minimo;

        List<CambioSincro> pendientes =
            _almacen.ObtenerPendientes(
                minimo);

        if (pendientes.Count > 0)
        {
            lineas.Add(
                CanalCifrado.CifrarMensaje(
                    SincroMensajes.Lote(
                        pendientes),
                    _clave));

            ultimo =
                pendientes[^1].Seq;
        }

        if (await _canal.EscribirAsync(
                MiArchivo,
                lineas))
        {
            _almacen.GuardarEstado(
                "ArchivoSeqEscrito",
                ultimo.ToString());
        }
    }


    // ========================================================
    // LEER LOS ARCHIVOS DE LOS DEMÁS
    // ========================================================

    private async Task LeerParesAsync()
    {
        List<string> nombres =
            await _canal.ListarAsync(
                "cambios-");

        foreach (string nombre in
                 nombres)
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

                byte[]? bytes =
                    await _canal.LeerAsync(
                        nombre);

                if (bytes is null
                    ||
                    bytes.Length == 0)
                {
                    continue;
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
            }
        }
    }
}
