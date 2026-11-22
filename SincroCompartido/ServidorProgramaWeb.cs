#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// SERVIDOR DEL FORMULARIO DEL PROGRAMA (WEB)
//
// Un servidor HTTP mínimo que solo escucha en el propio equipo
// (127.0.0.1): Tailscale Funnel lo publica con su dirección https.
// Solo sirve esto, y solo con un código de visita válido:
//   GET    /programa/{código}                          → la página, con los datos de la visita
//   POST   /programa/{código}                          → lo que envían los ancianos
//   GET    /programa/{código}/archivos                 → los archivos ya subidos
//   PUT    /programa/{código}/archivos/{tipo}/{nombre} → subir un archivo (ArchivosProgramaWeb)
//   DELETE /programa/{código}/archivos/{tipo}/{nombre} → quitarlo
//   GET    /programa/{código}/archivos/{tipo}/{nombre}?h=…&f=… → descargarlo desde otro PC
//          del usuario (firmado con la clave de sincronización; el enlace solo no basta)
//   GET    /programa/{código}/estado                   → cuándo lo enviaron (para el móvil)
//   GET    /programa/{código}/lugares?q=…&s=…          → sugerencias de direcciones (Google Maps o TomTom)
//   GET    /programa/{código}/lugar?id=…&s=…           → las coordenadas de la elegida (las de Google)
// Todo lo demás, 404. Está en el código compartido para poder
// llevarlo más adelante al móvil o a una Raspberry.
// ============================================================

public sealed class ServidorProgramaWeb : IDisposable
{
    private const int MaxCabeceras = 16 * 1024;

    private const int MaxCuerpo = 128 * 1024;

    private static readonly TimeSpan EsperaEntreEnvios = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan EsperaPeticion = TimeSpan.FromSeconds(20);

    // Un archivo grande desde un móvil tarda: más margen que para lo demás.
    private static readonly TimeSpan EsperaSubida = TimeSpan.FromMinutes(15);

    private readonly int _puerto;

    private readonly Func<string, string?> _datosVisita;

    private readonly Func<string, string, bool> _guardarEnvio;

    private readonly Func<string, string?>? _carpetaArchivos;

    private readonly string _pagina;

    private readonly ConcurrentDictionary<string, DateTime> _ultimoEnvio = new();

    private TcpListener? _escucha;

    private CancellationTokenSource? _cancelacion;


    /// <summary>Han subido o quitado un archivo (código del enlace). Llega desde otro hilo.</summary>
    public event Action<string>? ArchivosCambiados;

    /// <summary>
    /// Dirección por la que llegó la petición (cabecera Host) → servidor al que
    /// mandar la página («https://…»), o null para servirla aquí. Sirve para que
    /// los enlaces enviados con una dirección anterior lleven al servidor elegido.
    /// </summary>
    public Func<string, string?>? Redireccion { get; set; }

    /// <summary>
    /// Código → cuándo se envió («{"enviado":"…"}»), o null si el enlace no vale.
    /// Lo pregunta el móvil cada media hora (GET /programa/{código}/estado) para
    /// avisar aunque la app esté cerrada. Solo lo sabe quien tiene el enlace.
    /// </summary>
    public Func<string, string?>? Estado { get; set; }

    /// <summary>
    /// Código, lo escrito y token de sesión → sugerencias de direcciones (JSON), o
    /// null si no hay (sin Google, límite del día…): la página busca entonces con OpenStreetMap.
    /// </summary>
    public Func<string, string, string, CancellationToken, Task<string?>>? Lugares { get; set; }

    /// <summary>Código, id del lugar elegido y token de sesión → sus coordenadas (JSON), o null.</summary>
    public Func<string, string, string, CancellationToken, Task<string?>>? Lugar { get; set; }

    /// <summary>
    /// La clave de sincronización de este equipo: con ella firman los demás PC
    /// del usuario la descarga de un archivo. Sin clave, no se descarga nada.
    /// </summary>
    public Func<string?>? ClaveDescarga { get; set; }

    // Firmas malas por enlace: a la décima en 10 minutos, se deja de atender ese
    // enlace un rato (así no se puede ir probando claves).
    private const int MaxFallosDescarga = 10;

    private static readonly TimeSpan VentanaFallosDescarga = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (int Fallos, DateTime Desde)> _fallosDescarga = new();


    /// <param name="datosVisita">Código → JSON con los datos de la visita para la página (null si no existe).</param>
    /// <param name="guardarEnvio">Código y JSON enviado → si se ha guardado.</param>
    /// <param name="pagina">La página; «/*VISITA*/null» se sustituye por los datos de la visita.</param>
    /// <param name="carpetaArchivos">Código → carpeta para los archivos de esa visita (null: no se admiten).</param>
    public ServidorProgramaWeb(
        int puerto,
        Func<string, string?> datosVisita,
        Func<string, string, bool> guardarEnvio,
        string pagina,
        Func<string, string?>? carpetaArchivos = null)
    {
        _puerto = puerto;
        _datosVisita = datosVisita;
        _guardarEnvio = guardarEnvio;
        _pagina = pagina;
        _carpetaArchivos = carpetaArchivos;
    }


    public bool Activo => _escucha is not null;


    public void Iniciar()
    {
        if (_escucha is not null)
        {
            return;
        }

        _cancelacion = new CancellationTokenSource();
        _escucha = new TcpListener(IPAddress.Loopback, _puerto);
        _escucha.Start();

        CancellationToken token = _cancelacion.Token;

        _ = Task.Run(() => EscucharAsync(token));
    }


    private async Task EscucharAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _escucha is { } escucha)
        {
            TcpClient cliente;

            try
            {
                cliente = await escucha.AcceptTcpClientAsync(token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => AtenderAsync(cliente, token));
        }
    }


    private async Task AtenderAsync(TcpClient cliente, CancellationToken token)
    {
        using (cliente)
        {
            try
            {
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(token);
                limite.CancelAfter(EsperaPeticion);

                NetworkStream red = cliente.GetStream();

                (string metodo, string ruta, int largo, byte[] sobrante, string host)? peticion =
                    await LeerCabecerasAsync(red, limite.Token);

                if (peticion is not { } p)
                {
                    await ResponderAsync(red, 400, "text/plain", "Petición no válida", limite.Token);
                    return;
                }

                await ProcesarAsync(red, p.metodo, p.ruta, p.largo, p.sobrante, p.host, limite);
            }
            catch
            {
            }
        }
    }


    // Método, ruta, largo del cuerpo, lo que se leyó de más al buscar el
    // final de las cabeceras (el principio del cuerpo) y la cabecera Host.
    private static async Task<(string, string, int, byte[], string)?> LeerCabecerasAsync(NetworkStream red, CancellationToken token)
    {
        var acumulado = new MemoryStream();
        byte[] trozo = new byte[4096];

        while (acumulado.Length < MaxCabeceras)
        {
            int leidos = await red.ReadAsync(trozo, token);

            if (leidos == 0)
            {
                return null;
            }

            acumulado.Write(trozo, 0, leidos);

            byte[] datos = acumulado.ToArray();
            int fin = IndiceFinCabeceras(datos);

            if (fin < 0)
            {
                continue;
            }

            string cabeceras = Encoding.ASCII.GetString(datos, 0, fin);
            byte[] sobrante = datos[(fin + 4)..];

            string[] lineas = cabeceras.Split("\r\n");
            string[] primera = lineas[0].Split(' ');

            if (primera.Length < 2)
            {
                return null;
            }

            int largo = 0;
            string host = "";

            foreach (string linea in lineas)
            {
                if (linea.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && !int.TryParse(linea.Substring("Content-Length:".Length).Trim(), out largo))
                {
                    return null;
                }

                if (linea.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    host = linea.Substring("Host:".Length).Trim();
                }
            }

            return (primera[0].ToUpperInvariant(), primera[1], largo, sobrante, host);
        }

        return null;
    }


    private static int IndiceFinCabeceras(byte[] datos)
    {
        for (int i = 0; i + 3 < datos.Length; i++)
        {
            if (datos[i] == '\r' && datos[i + 1] == '\n' && datos[i + 2] == '\r' && datos[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }


    private async Task ProcesarAsync(NetworkStream red, string metodo, string ruta, int largo, byte[] sobrante, string host, CancellationTokenSource limite)
    {
        CancellationToken token = limite.Token;

        // /programa/{código} y /programa/{código}/archivos[/{tipo}/{nombre}] (sin consulta).
        string camino = ruta.Split('?')[0];
        string[] partes = camino.Trim('/').Split('/');

        if (partes.Length is < 2 or > 5
            || partes[0] != "programa"
            || !ProgramaWeb.CodigoValido(partes[1]))
        {
            await RechazarAsync(red, 404, "text/plain", "No existe", largo, sobrante, token);
            return;
        }

        string codigo = partes[1];

        // Un enlace con otra dirección (la de un servidor anterior): la página se
        // abre en el servidor elegido. Lo demás (envíos y archivos de una página
        // que ya estaba abierta) se sigue atendiendo aquí: llega por sincronización.
        if (partes.Length == 2 && metodo == "GET" && Redireccion?.Invoke(host) is { Length: > 0 } destino)
        {
            await RedirigirAsync(red, destino.TrimEnd('/') + "/programa/" + codigo, token);
            return;
        }

        if (partes.Length == 3 && partes[2] == "estado")
        {
            string? estado = metodo == "GET" ? Estado?.Invoke(codigo) : null;

            if (estado is null)
            {
                await RechazarAsync(red, 404, "text/plain", "No existe", largo, sobrante, token);
                return;
            }

            await ResponderAsync(red, 200, "application/json", estado, token);
            return;
        }

        // Direcciones con Google Maps o TomTom: las pide el PC (las claves no salen de aquí).
        if (partes.Length == 3 && partes[2] is "lugares" or "lugar")
        {
            bool sugerencias = partes[2] == "lugares";
            Func<string, string, string, CancellationToken, Task<string?>>? buscar = sugerencias ? Lugares : Lugar;
            string? resultado = null;

            if (metodo == "GET" && buscar is not null)
            {
                try
                {
                    resultado = await buscar(codigo, Parametro(ruta, sugerencias ? "q" : "id"), Parametro(ruta, "s"), token);
                }
                catch
                {
                    resultado = null;
                }
            }

            if (resultado is null)
            {
                await RechazarAsync(red, 503, "application/json", "{\"ok\":false}", largo, sobrante, token);
                return;
            }

            await ResponderAsync(red, 200, "application/json", resultado, token);
            return;
        }

        if (partes.Length > 2)
        {
            await ArchivosAsync(red, metodo, codigo, partes, ruta, largo, sobrante, limite);
            return;
        }

        string? datos = _datosVisita(codigo);

        if (datos is null)
        {
            await RechazarAsync(red, 404, "text/plain", "Este enlace no es válido o ha caducado.", largo, sobrante, token);
            return;
        }

        if (metodo == "GET")
        {
            string pagina = _pagina.Replace("/*VISITA*/null", datos);

            await ResponderAsync(red, 200, "text/html; charset=utf-8", pagina, token);
            return;
        }

        if (metodo != "POST")
        {
            await RechazarAsync(red, 405, "text/plain", "Método no permitido", largo, sobrante, token);
            return;
        }

        if (largo <= 0 || largo > MaxCuerpo)
        {
            await RechazarAsync(red, 413, "application/json", "{\"ok\":false,\"error\":\"tamaño\"}", largo, sobrante, token);
            return;
        }

        // Se lee el cuerpo entero antes de contestar nada (si se cierra con
        // datos sin leer, la conexión se corta en vez de dar la respuesta).
        byte[] cuerpo = new byte[largo];
        int copiados = Math.Min(sobrante.Length, largo);

        Array.Copy(sobrante, cuerpo, copiados);

        while (copiados < largo)
        {
            int leidos = await red.ReadAsync(cuerpo.AsMemory(copiados, largo - copiados), token);

            if (leidos == 0)
            {
                return;
            }

            copiados += leidos;
        }

        // Un envío cada pocos segundos por enlace.
        DateTime ahora = DateTime.UtcNow;

        if (_ultimoEnvio.TryGetValue(codigo, out DateTime anterior) && ahora - anterior < EsperaEntreEnvios)
        {
            await ResponderAsync(red, 429, "application/json", "{\"ok\":false,\"error\":\"espera\"}", token);
            return;
        }

        _ultimoEnvio[codigo] = ahora;

        string json = Encoding.UTF8.GetString(cuerpo);

        bool guardado;

        try
        {
            guardado = _guardarEnvio(codigo, json);
        }
        catch
        {
            guardado = false;
        }

        await ResponderAsync(
            red,
            guardado ? 200 : 400,
            "application/json",
            guardado ? "{\"ok\":true}" : "{\"ok\":false}",
            token);
    }


    // ---- Archivos: listar, subir y quitar, solo dentro de la carpeta de la visita ----
    private async Task ArchivosAsync(NetworkStream red, string metodo, string codigo, string[] partes, string rutaPeticion, int largo, byte[] sobrante, CancellationTokenSource limite)
    {
        CancellationToken token = limite.Token;

        string? carpeta = partes[2] == "archivos" ? _carpetaArchivos?.Invoke(codigo) : null;

        if (carpeta is null)
        {
            await RechazarAsync(red, 404, "text/plain", "Este enlace no es válido o ha caducado.", largo, sobrante, token);
            return;
        }

        if (partes.Length == 3)
        {
            if (metodo != "GET")
            {
                await RechazarAsync(red, 405, "text/plain", "Método no permitido", largo, sobrante, token);
                return;
            }

            await ResponderAsync(red, 200, "application/json", ArchivosProgramaWeb.ListaJson(carpeta), token);
            return;
        }

        string? subcarpeta = partes.Length == 5 ? ArchivosProgramaWeb.Carpeta(partes[3]) : null;

        if (subcarpeta is null)
        {
            await RechazarAsync(red, 404, "text/plain", "No existe", largo, sobrante, token);
            return;
        }

        string? nombre = ArchivosProgramaWeb.NombreSeguro(Uri.UnescapeDataString(partes[4]));

        if (nombre is null)
        {
            await RechazarAsync(red, 415, "application/json", "{\"ok\":false,\"error\":\"tipo\"}", largo, sobrante, token);
            return;
        }

        string destino = Path.Combine(carpeta, subcarpeta);

        if (metodo == "GET")
        {
            await DescargarAsync(red, codigo, partes[3], nombre, Path.Combine(destino, nombre), rutaPeticion, largo, sobrante, limite);
            return;
        }

        if (metodo == "DELETE")
        {
            string ruta = Path.Combine(destino, nombre);
            bool quitado = File.Exists(ruta);

            if (quitado)
            {
                File.Delete(ruta);
                Avisar(codigo);
            }

            await ResponderAsync(red, 200, "application/json", quitado ? "{\"ok\":true}" : "{\"ok\":false}", token);
            return;
        }

        if (metodo != "PUT")
        {
            await RechazarAsync(red, 405, "text/plain", "Método no permitido", largo, sobrante, token);
            return;
        }

        if (largo <= 0
            || largo > ArchivosProgramaWeb.MaxBytesArchivo
            || ArchivosProgramaWeb.Ocupado(carpeta) + largo > ArchivosProgramaWeb.MaxBytesVisita)
        {
            await RechazarAsync(red, 413, "application/json", "{\"ok\":false,\"error\":\"tamaño\"}", largo, sobrante, token);
            return;
        }

        limite.CancelAfter(EsperaSubida);

        // Se escribe a disco según llega (no se guarda entero en memoria) y,
        // completo, se le pone su nombre; si se corta, no queda nada.
        Directory.CreateDirectory(destino);

        string temporal = ArchivosProgramaWeb.RutaTemporal(destino);

        try
        {
            await using (var archivo = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                int copiados = Math.Min(sobrante.Length, largo);

                await archivo.WriteAsync(sobrante.AsMemory(0, copiados), token);

                byte[] trozo = new byte[81920];

                while (copiados < largo)
                {
                    int leidos = await red.ReadAsync(trozo.AsMemory(0, Math.Min(trozo.Length, largo - copiados)), token);

                    if (leidos == 0)
                    {
                        throw new IOException("La conexión se cortó a medias.");
                    }

                    await archivo.WriteAsync(trozo.AsMemory(0, leidos), token);
                    copiados += leidos;
                }
            }

            string final = ArchivosProgramaWeb.NombreLibre(destino, nombre);

            File.Move(temporal, Path.Combine(destino, final));
            Avisar(codigo);

            var respuesta = new JsonObject { ["ok"] = true, ["nombre"] = final, ["tamano"] = largo };

            await ResponderAsync(red, 200, "application/json", respuesta.ToJsonString(), token);
        }
        finally
        {
            if (File.Exists(temporal))
            {
                try
                {
                    File.Delete(temporal);
                }
                catch
                {
                }
            }
        }
    }


    // Un archivo para otro PC del usuario: solo con la firma de la clave de sincronización.
    private async Task DescargarAsync(NetworkStream red, string codigo, string tipo, string nombre, string archivo, string ruta, int largo, byte[] sobrante, CancellationTokenSource limite)
    {
        CancellationToken token = limite.Token;
        DateTime ahora = DateTime.UtcNow;

        if (_fallosDescarga.TryGetValue(codigo, out var fallos)
            && fallos.Fallos >= MaxFallosDescarga
            && ahora - fallos.Desde < VentanaFallosDescarga)
        {
            await RechazarAsync(red, 429, "text/plain", "Demasiados intentos", largo, sobrante, token);
            return;
        }

        if (!ArchivosProgramaWeb.DescargaValida(ClaveDescarga?.Invoke(), codigo, tipo, nombre, Parametro(ruta, "h"), Parametro(ruta, "f"), DateTimeOffset.UtcNow))
        {
            _fallosDescarga.AddOrUpdate(
                codigo,
                (1, ahora),
                (_, f) => ahora - f.Desde < VentanaFallosDescarga ? (f.Fallos + 1, f.Desde) : (1, ahora));

            await RechazarAsync(red, 403, "text/plain", "No permitido", largo, sobrante, token);
            return;
        }

        if (!File.Exists(archivo))
        {
            await RechazarAsync(red, 404, "text/plain", "No existe", largo, sobrante, token);
            return;
        }

        // Un archivo grande por datos móviles tarda: el mismo margen que para subirlo.
        limite.CancelAfter(EsperaSubida);

        await using var lectura = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        string cabeceras =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            $"Content-Length: {lectura.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n";

        await red.WriteAsync(Encoding.ASCII.GetBytes(cabeceras), token);
        await lectura.CopyToAsync(red, 81920, token);
        await red.FlushAsync(token);
    }


    // Antes de rechazar una petición con un cuerpo pequeño, se lee entero:
    // si se cierra con datos sin leer, el navegador ve un corte y no la
    // respuesta (y no puede decir «tipo no admitido» o «ha caducado»).
    private const int MaxDescartar = 1024 * 1024;

    private static async Task RechazarAsync(NetworkStream red, int estado, string tipo, string texto, int largo, byte[] sobrante, CancellationToken token)
    {
        if (largo > 0 && largo <= MaxDescartar)
        {
            int pendientes = largo - Math.Min(sobrante.Length, largo);
            byte[] trozo = new byte[16384];

            while (pendientes > 0)
            {
                int leidos = await red.ReadAsync(trozo.AsMemory(0, Math.Min(trozo.Length, pendientes)), token);

                if (leidos == 0)
                {
                    break;
                }

                pendientes -= leidos;
            }
        }

        await ResponderAsync(red, estado, tipo, texto, token);
    }


    private void Avisar(string codigo)
    {
        try
        {
            ArchivosCambiados?.Invoke(codigo);
        }
        catch
        {
        }
    }


    // Un parámetro de la consulta («?q=…&s=…»), ya decodificado («» si no está).
    private static string Parametro(string ruta, string nombre)
    {
        int interrogacion = ruta.IndexOf('?');

        if (interrogacion < 0)
        {
            return "";
        }

        foreach (string par in ruta[(interrogacion + 1)..].Split('&'))
        {
            int igual = par.IndexOf('=');

            if ((igual < 0 ? par : par[..igual]) == nombre)
            {
                try
                {
                    return Uri.UnescapeDataString((igual < 0 ? "" : par[(igual + 1)..]).Replace('+', ' '));
                }
                catch (UriFormatException)
                {
                    return "";
                }
            }
        }

        return "";
    }


    // Temporal (302, sin guardar en caché): si el servidor vuelve a cambiar, el enlace sigue al nuevo.
    private static async Task RedirigirAsync(NetworkStream red, string destino, CancellationToken token)
    {
        string cabeceras =
            "HTTP/1.1 302 Found\r\n" +
            $"Location: {destino}\r\n" +
            "Content-Length: 0\r\n" +
            "Cache-Control: no-store\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "Connection: close\r\n\r\n";

        await red.WriteAsync(Encoding.ASCII.GetBytes(cabeceras), token);
        await red.FlushAsync(token);
    }


    private static async Task ResponderAsync(NetworkStream red, int estado, string tipo, string texto, CancellationToken token)
    {
        byte[] cuerpo = Encoding.UTF8.GetBytes(texto);

        string motivo = estado switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            415 => "Unsupported Media Type",
            429 => "Too Many Requests",
            503 => "Service Unavailable",
            _ => "Error"
        };

        string cabeceras =
            $"HTTP/1.1 {estado} {motivo}\r\n" +
            $"Content-Type: {tipo}\r\n" +
            $"Content-Length: {cuerpo.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "X-Frame-Options: DENY\r\n" +
            "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
            "connect-src 'self' https://photon.komoot.io; img-src data:; base-uri 'none'; form-action 'none'\r\n" +
            "Connection: close\r\n\r\n";

        await red.WriteAsync(Encoding.ASCII.GetBytes(cabeceras), token);
        await red.WriteAsync(cuerpo, token);
        await red.FlushAsync(token);
    }


    public void Dispose()
    {
        try
        {
            _cancelacion?.Cancel();
            _escucha?.Stop();
        }
        catch
        {
        }

        _escucha = null;
    }
}
