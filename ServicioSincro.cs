using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace SCCosmos.Sincro;

// ============================================================
// SERVICIO DE SINCRONIZACIÓN ENTRE DISPOSITIVOS
//
// - Escucha en TCP (Puerto) y en UDP (descubrimiento en la red
//   local, puerto 47312).
// - Se conecta solo a los dispositivos descubiertos y a los de la
//   lista manual (wifi local, Tailscale, dirección de casa...).
// - Intercambia los cambios pendientes y los aplica, empujando los
//   nuevos cada segundo para que todo llegue al momento.
// ============================================================

public sealed class ServicioSincro
    : IDisposable
{
    private readonly SincroConfiguracion _config;

    private readonly SincroAlmacen _almacen;

    private readonly CanalArchivosSincro? _canalArchivos;

    private CancellationTokenSource?
        _cancelacion;

    private TcpListener?
        _escucha;

    private UdpClient?
        _udp;

    private readonly object _candado =
        new();

    private readonly Dictionary<string, ConexionPar>
        _conexiones =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Descubierto>
        _descubiertos =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DateTime>
        _intentos =
            new(
                StringComparer.OrdinalIgnoreCase);


    public ServicioSincro(
        SincroConfiguracion config,
        SincroAlmacen almacen,
        ICanalArchivosSincro? canalArchivos = null)
    {
        _config =
            config;

        _almacen =
            almacen;

        _canalArchivos =
            canalArchivos is null
                ? null
                : new CanalArchivosSincro(
                    config,
                    almacen,
                    canalArchivos);
    }


    // ========================================================
    // ESTADO PARA LA INTERFAZ
    // ========================================================

    public event Action?
        Cambio;


    /// <summary>
    /// Se dispara cuando se aplican cambios llegados de otro
    /// dispositivo (nº de registros). Las pantallas lo usan para
    /// actualizarse solas.
    /// </summary>
    public event Action<int>?
        DatosRecibidos;


    public string UltimoEstado
    {
        get;
        private set;
    } = "";


    public long CambiosEnviados
    {
        get;
        private set;
    }


    public long CambiosRecibidos
    {
        get;
        private set;
    }


    public DateTime? UltimaActividad
    {
        get;
        private set;
    }


    public bool Activo =>
        _cancelacion is not null;


    public List<InfoDispositivoSincro>
        ObtenerDispositivos()
    {
        var lista =
            new List<InfoDispositivoSincro>();

        lock (_candado)
        {
            var conectados =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ConexionPar conexion in
                     _conexiones.Values)
            {
                conectados.Add(
                    conexion.Id);

                lista.Add(
                    new InfoDispositivoSincro
                    {
                        Id =
                            conexion.Id,

                        Nombre =
                            conexion.Nombre,

                        Direccion =
                            conexion.Direccion,

                        Conectado =
                            true,

                        Visto =
                            conexion.Visto
                    });
            }

            foreach (KeyValuePair<string, Descubierto> par in
                     _descubiertos)
            {
                if (conectados.Contains(
                        par.Key))
                {
                    continue;
                }

                lista.Add(
                    new InfoDispositivoSincro
                    {
                        Id =
                            par.Key,

                        Nombre =
                            par.Value.Nombre,

                        Direccion =
                            par.Value.Direccion,

                        Conectado =
                            false,

                        Visto =
                            par.Value.Visto
                    });
            }
        }

        return lista;
    }


    // ========================================================
    // INICIAR / DETENER
    // ========================================================

    public void Iniciar()
    {
        if (_cancelacion is not null)
        {
            return;
        }

        _cancelacion =
            new CancellationTokenSource();

        CancellationToken token =
            _cancelacion.Token;

        Avisar(
            "Sincronización entre dispositivos iniciada.");

        _ = Task.Run(
            () => BucleEscuchaAsync(
                token));

        if (_config.Descubrimiento)
        {
            _ = Task.Run(
                () => BucleUdpAsync(
                    token));

            _ = Task.Run(
                () => BucleAnuncioAsync(
                    token));
        }

        // Las conexiones directas siempre se intentan (por si hay
        // direcciones añadidas a mano).
        _ = Task.Run(
            () => BucleConexionesAsync(
                token));

        _ = Task.Run(
            () => BucleEmpujeAsync(
                token));

        if (_canalArchivos is not null)
        {
            _ = Task.Run(
                () => BucleArchivosAsync(
                    token));
        }
    }


    public void Detener()
    {
        CancellationTokenSource?
            cancelacion =
                _cancelacion;

        _cancelacion =
            null;

        try
        {
            cancelacion?.Cancel();
            cancelacion?.Dispose();
        }
        catch
        {
        }

        try
        {
            _escucha?.Stop();
        }
        catch
        {
        }

        try
        {
            _udp?.Dispose();
        }
        catch
        {
        }

        lock (_candado)
        {
            foreach (ConexionPar conexion in
                     _conexiones.Values)
            {
                try
                {
                    conexion.Canal.Dispose();
                }
                catch
                {
                }
            }

            _conexiones.Clear();
        }

        Avisar(
            "Sincronización entre dispositivos detenida.");

        Cambio?.Invoke();
    }


    public void Dispose()
    {
        Detener();
    }


    // ========================================================
    // ESCUCHA TCP
    // ========================================================

    private async Task BucleEscuchaAsync(
        CancellationToken token)
    {
        try
        {
            _escucha =
                new TcpListener(
                    IPAddress.Any,
                    _config.Puerto);

            _escucha.Start();
        }
        catch (Exception ex)
        {
            Avisar(
                $"No se pudo abrir el puerto " +
                $"{_config.Puerto}: {ex.Message}");

            return;
        }

        while (!token.IsCancellationRequested)
        {
            TcpClient? cliente =
                null;

            try
            {
                cliente =
                    await _escucha
                        .AcceptTcpClientAsync(
                            token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            TcpClient recibido =
                cliente;

            _ = Task.Run(
                () => SesionAsync(
                    recibido,
                    false,
                    token),
                CancellationToken.None);
        }
    }


    // ========================================================
    // DESCUBRIMIENTO UDP
    // ========================================================

    private async Task BucleUdpAsync(
        CancellationToken token)
    {
        try
        {
            _udp =
                new UdpClient(
                    SincroConfiguracion
                        .PuertoDescubrimiento);

            _udp.EnableBroadcast =
                true;
        }
        catch
        {
            // Sin descubrimiento automático: quedan las
            // direcciones manuales.
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult recibido =
                    await _udp.ReceiveAsync(
                        token);

                byte[] datos =
                    recibido.Buffer;

                IPEndPoint remoto =
                    recibido.RemoteEndPoint;

                string texto =
                    System.Text.Encoding.UTF8
                        .GetString(
                            datos);

                JsonObject? nodo =
                    JsonNode.Parse(
                        texto)
                    as JsonObject;

                string tipo =
                    nodo?["t"]
                        ?.GetValue<string>()
                    ?? "";

                if (nodo is null)
                {
                    continue;
                }

                string id =
                    nodo["id"]
                        ?.GetValue<string>()
                    ?? "";

                if (id.Length == 0
                    ||
                    id == _config.IdDispositivo)
                {
                    continue;
                }

                RegistrarDescubierto(
                    id,
                    nodo["nom"]
                        ?.GetValue<string>()
                    ?? id,
                    remoto.Address
                        .ToString(),
                    nodo["puerto"]
                        ?.GetValue<int>()
                    ?? _config.Puerto);

                if (tipo == "descubrir")
                {
                    byte[] respuesta =
                        System.Text.Encoding.UTF8
                            .GetBytes(
                                SincroMensajes
                                    .AquiEstoy(
                                        _config));

                    await _udp.SendAsync(
                        respuesta,
                        new IPEndPoint(
                            remoto.Address,
                            SincroConfiguracion
                                .PuertoDescubrimiento),
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Se reintenta.
            }
        }
    }


    private async Task BucleAnuncioAsync(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udp is not null)
                {
                    byte[] datos =
                        System.Text.Encoding.UTF8
                            .GetBytes(
                                SincroMensajes
                                    .Descubrimiento(
                                        _config));

                    await _udp.SendAsync(
                        datos,
                        new IPEndPoint(
                            IPAddress.Broadcast,
                            SincroConfiguracion
                                .PuertoDescubrimiento),
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }

            try
            {
                await Task.Delay(
                    6000,
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }


    private void RegistrarDescubierto(
        string id,
        string nombre,
        string direccion,
        int puerto)
    {
        lock (_candado)
        {
            _descubiertos[id] =
                new Descubierto
                {
                    Nombre =
                        nombre,

                    Direccion =
                        direccion,

                    Puerto =
                        puerto,

                    Visto =
                        DateTime.UtcNow
                };
        }
    }


    // ========================================================
    // CONEXIONES
    // ========================================================

    private async Task BucleConexionesAsync(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            List<(string Host, int Puerto)> destinos =
                DireccionesCandidatas();

            foreach ((string host, int puerto) in
                     destinos)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                string clave =
                    $"{host}:{puerto}";

                lock (_candado)
                {
                    if (_intentos.TryGetValue(
                            clave,
                            out DateTime ultimo)
                        &&
                        (DateTime.UtcNow - ultimo)
                            .TotalSeconds < 20)
                    {
                        continue;
                    }

                    _intentos[clave] =
                        DateTime.UtcNow;
                }

                await ConectarAsync(
                    host,
                    puerto,
                    token);
            }

            try
            {
                await Task.Delay(
                    5000,
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }


    private List<(string Host, int Puerto)>
        DireccionesCandidatas()
    {
        var lista =
            new List<(string, int)>();

        var direccionesVistas =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        lock (_candado)
        {
            foreach (ConexionPar conexion in
                     _conexiones.Values)
            {
                direccionesVistas.Add(
                    conexion.Direccion);
            }

            foreach (KeyValuePair<string, Descubierto> entrada in
                     _descubiertos)
            {
                if (!_config.EstaHabilitado(
                        entrada.Key))
                {
                    continue;
                }

                Descubierto descubierto =
                    entrada.Value;

                if ((DateTime.UtcNow - descubierto.Visto)
                    .TotalSeconds > 90)
                {
                    continue;
                }

                string direccion =
                    $"{descubierto.Direccion}:{descubierto.Puerto}";

                if (direccionesVistas.Contains(
                        direccion)
                    ||
                    direccionesVistas.Contains(
                        descubierto.Direccion))
                {
                    continue;
                }

                lista.Add(
                    (descubierto.Direccion,
                     descubierto.Puerto));
            }
        }

        foreach (string manual in
                 _config.Direcciones)
        {
            (string host, int puerto) =
                SincroConfiguracion
                    .SepararDireccion(
                        manual,
                        _config.Puerto);

            if (host.Length > 0)
            {
                lista.Add(
                    (host, puerto));
            }
        }

        return lista;
    }


    private async Task ConectarAsync(
        string host,
        int puerto,
        CancellationToken token)
    {
        var cliente =
            new TcpClient();

        try
        {
            using var limite =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        token);

            limite.CancelAfter(
                4000);

            await cliente.ConnectAsync(
                host,
                puerto,
                limite.Token);
        }
        catch
        {
            cliente.Dispose();

            return;
        }

        await SesionAsync(
            cliente,
            true,
            token);
    }


    // ========================================================
    // SESIÓN CON UN DISPOSITIVO
    // ========================================================

    private async Task SesionAsync(
        TcpClient cliente,
        bool iniciadaPorMi,
        CancellationToken token)
    {
        ConexionPar? conexion =
            null;

        try
        {
            cliente.NoDelay =
                true;

            NetworkStream flujo =
                cliente.GetStream();

            var canal =
                new CanalCifrado(
                    flujo,
                    _config.Clave);

            if (iniciadaPorMi)
            {
                await canal.EnviarAsync(
                    SincroMensajes.Hola(
                        _config),
                    token);
            }

            string? primera =
                await canal.RecibirAsync(
                    token);

            if (primera is null)
            {
                canal.Dispose();

                return;
            }

            JsonObject? saludo =
                JsonNode.Parse(
                    primera)
                as JsonObject;

            string tipo =
                saludo?["t"]
                    ?.GetValue<string>()
                ?? "";

            if (tipo != "hola"
                &&
                tipo != "hola_ok")
            {
                canal.Dispose();

                return;
            }

            string idPar =
                saludo?["id"]
                    ?.GetValue<string>()
                ?? "";

            string nombrePar =
                saludo?["nom"]
                    ?.GetValue<string>()
                ?? idPar;

            if (idPar.Length == 0
                ||
                string.Equals(
                    idPar,
                    _config.IdDispositivo,
                    StringComparison.OrdinalIgnoreCase))
            {
                canal.Dispose();

                return;
            }

            if (!iniciadaPorMi)
            {
                await canal.EnviarAsync(
                    SincroMensajes.HolaOk(
                        _config),
                    token);
            }

            // Si los dos dispositivos se conectan a la vez (o uno
            // llega por descubrimiento y otro por dirección manual),
            // con este dispositivo ya conectado solo sobrevive la
            // conexión iniciada por el Id menor.
            string direccion =
                ClienteRemoto(
                    cliente);

            (long ultimoEnviado, long ultimoRecibido) =
                _almacen.ObtenerPar(
                    idPar);

            conexion =
                new ConexionPar
                {
                    Id =
                        idPar,

                    Nombre =
                        nombrePar,

                    Direccion =
                        direccion,

                    IniciadaPorMi =
                        iniciadaPorMi,

                    Cliente =
                        cliente,

                    Canal =
                        canal,

                    Enviado =
                        ultimoEnviado,

                    ConfirmadoEnDb =
                        ultimoEnviado,

                    Recibido =
                        ultimoRecibido,

                    Visto =
                        DateTime.UtcNow
                };

            // Dispositivo desmarcado en la lista: no se sincroniza.
            if (!_config.EstaHabilitado(
                    idPar))
            {
                canal.Dispose();

                return;
            }

            lock (_candado)
            {
                if (_conexiones.TryGetValue(
                        idPar,
                        out ConexionPar? anterior))
                {
                    bool ganaLaMia =
                        (iniciadaPorMi
                         &&
                         string.CompareOrdinal(
                             _config.IdDispositivo,
                             idPar) < 0)
                        ||
                        (!iniciadaPorMi
                         &&
                         string.CompareOrdinal(
                             idPar,
                             _config.IdDispositivo) < 0);

                    if (!ganaLaMia)
                    {
                        canal.Dispose();

                        return;
                    }

                    try
                    {
                        anterior.Canal.Dispose();
                    }
                    catch
                    {
                    }
                }

                _conexiones[idPar] =
                    conexion;
            }

            Avisar(
                $"Conectado con {nombrePar}.");

            Cambio?.Invoke();

            // La recepción arranca antes que el primer envío: así dos
            // lotes grandes nunca pueden bloquearse mutuamente.
            Task lectura =
                Task.Run(
                    () => LeerBucleAsync(
                        conexion,
                        idPar,
                        nombrePar,
                        token),
                    CancellationToken.None);

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await EmpujarAsync(
                            conexion,
                            token);
                    }
                    catch
                    {
                    }
                },
                CancellationToken.None);

            await lectura;
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.Security.Cryptography
            .CryptographicException)
        {
            Avisar(
                "Clave de sincronización incorrecta " +
                "con otro dispositivo.");
        }
        catch
        {
            // Se reintentará más adelante.
        }
        finally
        {
            if (conexion is not null)
            {
                bool eraLaActual =
                    false;

                lock (_candado)
                {
                    if (_conexiones.TryGetValue(
                            conexion.Id,
                            out ConexionPar? actual)
                        &&
                        ReferenceEquals(
                            actual,
                            conexion))
                    {
                        _conexiones.Remove(
                            conexion.Id);

                        eraLaActual =
                            true;
                    }
                }

                if (eraLaActual)
                {
                    Avisar(
                        $"Desconectado de {conexion.Nombre}.");

                    Cambio?.Invoke();
                }
            }

            try
            {
                cliente.Dispose();
            }
            catch
            {
            }
        }
    }


    // ========================================================
    // LECTURA DE UNA CONEXIÓN
    // ========================================================

    private async Task LeerBucleAsync(
        ConexionPar conexion,
        string idPar,
        string nombrePar,
        CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? texto =
                    await conexion.Canal
                        .RecibirAsync(
                            token);

                if (texto is null)
                {
                    break;
                }

                conexion.Visto =
                    DateTime.UtcNow;

                JsonObject? mensaje =
                    JsonNode.Parse(
                        texto)
                    as JsonObject;

                if (mensaje is null)
                {
                    continue;
                }

                string clase =
                    mensaje["t"]
                        ?.GetValue<string>()
                    ?? "";

                switch (clase)
                {
                    case "lote":
                    {
                        List<CambioSincro> lote =
                            SincroMensajes
                                .LeerLote(
                                    mensaje);

                        if (lote.Count == 0)
                        {
                            break;
                        }

                        ResultadoAplicacion resultado =
                            _almacen.AplicarLote(
                                lote);

                        _almacen.GuardarUltimoRecibido(
                            idPar,
                            resultado.MayorSeq);

                        conexion.Recibido =
                            resultado.MayorSeq;

                        if (resultado.Aplicados > 0)
                        {
                            CambiosRecibidos +=
                                resultado.Aplicados;

                            UltimaActividad =
                                DateTime.Now;

                            Avisar(
                                $"Cambios de {nombrePar}: " +
                                $"{resultado.Aplicados} aplicados.");

                            Cambio?.Invoke();

                            DatosRecibidos?.Invoke(
                                resultado.Aplicados);
                        }

                        await EnviarConCandadoAsync(
                            conexion,
                            SincroMensajes.Recibido(
                                resultado.MayorSeq),
                            token);

                        break;
                    }

                    case "recibido":
                    {
                        long hasta =
                            mensaje["hasta"]
                                ?.GetValue<long>()
                            ?? 0;

                        if (hasta > conexion.ConfirmadoEnDb)
                        {
                            conexion.ConfirmadoEnDb =
                                hasta;

                            _almacen.GuardarUltimoEnviado(
                                idPar,
                                hasta);

                            UltimaActividad =
                                DateTime.Now;
                        }

                        break;
                    }

                    case "ping":
                    {
                        await EnviarConCandadoAsync(
                            conexion,
                            SincroMensajes.Pong(),
                            token);

                        break;
                    }

                    case "pong":
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            try
            {
                conexion.Canal.Dispose();
            }
            catch
            {
            }
        }
    }


    // ========================================================
    // EMPUJAR CAMBIOS NUEVOS
    // ========================================================

    private async Task BucleEmpujeAsync(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            List<ConexionPar> conexiones;

            lock (_candado)
            {
                conexiones =
                    _conexiones
                        .Values
                        .ToList();
            }

            foreach (ConexionPar conexion in
                     conexiones)
            {
                if (!_config.EstaHabilitado(
                        conexion.Id))
                {
                    Desconectar(
                        conexion.Id);

                    continue;
                }

                try
                {
                    await EmpujarAsync(
                        conexion,
                        token);

                    // Latido para mantener viva la conexión
                    // (importante en redes móviles).
                    if ((DateTime.UtcNow - conexion.UltimoLatido)
                        .TotalSeconds > 25)
                    {
                        conexion.UltimoLatido =
                            DateTime.UtcNow;

                        await EnviarConCandadoAsync(
                            conexion,
                            SincroMensajes.Ping(),
                            token);
                    }
                }
                catch
                {
                    // La conexión caerá sola.
                }
            }

            try
            {
                await Task.Delay(
                    1000,
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }


    private async Task EmpujarAsync(
        ConexionPar conexion,
        CancellationToken token)
    {
        await conexion.Envio
            .WaitAsync(
                token);

        try
        {
            List<CambioSincro> pendientes =
                _almacen.ObtenerPendientes(
                    conexion.Enviado);

            if (pendientes.Count == 0)
            {
                return;
            }

            await conexion.Canal
                .EnviarAsync(
                    SincroMensajes.Lote(
                        pendientes),
                    token);

            conexion.Enviado =
                pendientes[^1].Seq;

            CambiosEnviados +=
                pendientes.Count;

            UltimaActividad =
                DateTime.Now;
        }
        finally
        {
            conexion.Envio.Release();
        }
    }


    private async Task EnviarConCandadoAsync(
        ConexionPar conexion,
        string texto,
        CancellationToken token)
    {
        await conexion.Envio
            .WaitAsync(
                token);

        try
        {
            await conexion.Canal
                .EnviarAsync(
                    texto,
                    token);
        }
        finally
        {
            conexion.Envio.Release();
        }
    }


    // ========================================================
    // VACIAR PENDIENTES (ANTES DE CERRAR)
    // ========================================================

    /// <summary>
    /// Envía ahora mismo lo que quede pendiente: a los dispositivos
    /// conectados (TCP) y a la carpeta compartida. Se usa antes de
    /// cerrar la aplicación para no perder los últimos cambios.
    /// </summary>
    public async Task VaciarPendientesAsync(
        TimeSpan? limite = null)
    {
        DateTime final =
            DateTime.UtcNow
            + (limite ?? TimeSpan.FromSeconds(4));

        // 1) Empujón a cada dispositivo conectado.
        List<ConexionPar> conexiones;

        lock (_candado)
        {
            conexiones =
                _conexiones
                    .Values
                    .ToList();
        }

        foreach (ConexionPar conexion in conexiones)
        {
            if (DateTime.UtcNow >= final)
            {
                break;
            }

            if (!_config.EstaHabilitado(
                    conexion.Id))
            {
                continue;
            }

            try
            {
                using var caducidad =
                    new CancellationTokenSource(
                        final - DateTime.UtcNow);

                await EmpujarAsync(
                    conexion,
                    caducidad.Token);
            }
            catch
            {
            }
        }

        // 2) Pases del canal por carpeta (Syncthing) hasta ponerse al día
        //    o agotar el tiempo: cada pase publica hasta 400 cambios.
        int sinProgreso = 0;

        for (int pase = 0;
             pase < 20
             && _canalArchivos is not null
             && DateTime.UtcNow < final;
             pase++)
        {
            string antes =
                _almacen.ObtenerEstado(
                    "ArchivoSeqEscrito")
                ?? "0";

            try
            {
                using var caducidad =
                    new CancellationTokenSource(
                        final - DateTime.UtcNow);

                await _canalArchivos
                    .EjecutarPaseAsync()
                    .WaitAsync(
                        caducidad.Token);
            }
            catch
            {
            }

            string despues =
                _almacen.ObtenerEstado(
                    "ArchivoSeqEscrito")
                ?? "0";

            if (string.Equals(
                    antes,
                    despues,
                    StringComparison.Ordinal))
            {
                sinProgreso++;

                if (sinProgreso >= 2)
                {
                    // Nada más que publicar.
                    break;
                }

                // Un bloqueo momentáneo de la carpeta (p. ej.
                // Syncthing leyéndola): se reintenta una vez.
                await Task.Delay(400);
            }
            else
            {
                sinProgreso = 0;
            }
        }
    }


    // ========================================================
    // CANAL POR CARPETA (fuera de casa)
    // ========================================================

    private async Task BucleArchivosAsync(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                long antes =
                    _canalArchivos!
                        .TotalAplicado;

                await _canalArchivos!
                    .EjecutarPaseAsync();

                long despues =
                    _canalArchivos!
                        .TotalAplicado;

                if (despues > antes)
                {
                    DatosRecibidos?.Invoke(
                        (int)Math.Min(
                            despues - antes,
                            int.MaxValue));
                }

                string estado =
                    _canalArchivos!
                        .UltimoEstado;

                if (!string.IsNullOrWhiteSpace(
                        estado)
                    &&
                    !string.Equals(
                        estado,
                        UltimaCarpeta,
                        StringComparison.Ordinal))
                {
                    UltimaCarpeta =
                        estado;

                    Avisar(
                        estado);
                }
            }
            catch
            {
            }

            try
            {
                await Task.Delay(
                    6000,
                    token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }


    private string UltimaCarpeta =
        "";


    /// <summary>
    /// Cierra la conexión con un dispositivo (por ejemplo, al
    /// desmarcarlo de la sincronización).
    /// </summary>
    public void Desconectar(
        string idPar)
    {
        ConexionPar? conexion =
            null;

        lock (_candado)
        {
            _conexiones.TryGetValue(
                idPar,
                out conexion);
        }

        try
        {
            conexion?.Canal.Dispose();
        }
        catch
        {
        }
    }


    // ========================================================
    // AYUDAS
    // ========================================================

    private static string ClienteRemoto(
        TcpClient cliente)
    {
        try
        {
            IPEndPoint? remoto =
                cliente.Client
                    .RemoteEndPoint
                as IPEndPoint;

            return remoto?.Address
                .ToString()
                ?? "";
        }
        catch
        {
            return "";
        }
    }


    private void Avisar(
        string mensaje)
    {
        UltimoEstado =
            mensaje;

        Cambio?.Invoke();
    }


    private sealed class ConexionPar
    {
        public string Id =
            "";

        public string Nombre =
            "";

        public string Direccion =
            "";

        public bool IniciadaPorMi;

        public TcpClient Cliente =
            null!;

        public CanalCifrado Canal =
            null!;

        public SemaphoreSlim Envio =
            new(1, 1);

        public long Enviado;

        public long ConfirmadoEnDb;

        public long Recibido;

        public DateTime Visto =
            DateTime.UtcNow;

        public DateTime UltimoLatido =
            DateTime.UtcNow;
    }


    private sealed class Descubierto
    {
        public string Nombre =
            "";

        public string Direccion =
            "";

        public int Puerto;

        public DateTime Visto =
            DateTime.UtcNow;
    }
}


// ============================================================
// MODELO PARA LA INTERFAZ
// ============================================================

public sealed class InfoDispositivoSincro
{
    public string Id
    {
        get;
        set;
    } = "";

    public string Nombre
    {
        get;
        set;
    } = "";

    public string Direccion
    {
        get;
        set;
    } = "";

    public bool Conectado
    {
        get;
        set;
    }

    public DateTime? Visto
    {
        get;
        set;
    }
}
