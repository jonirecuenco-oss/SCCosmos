#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SCCosmos.Sincro;

// ============================================================
// SERVIDOR DEL FORMULARIO DEL PROGRAMA (WEB)
//
// Un servidor HTTP mínimo que solo escucha en el propio equipo
// (127.0.0.1): Tailscale Funnel lo publica con su dirección https.
// Solo sirve dos cosas, y solo con un código de visita válido:
//   GET  /programa/{código}  → la página, con los datos de la visita
//   POST /programa/{código}  → lo que envían los ancianos
// Todo lo demás, 404. Está en el código compartido para poder
// llevarlo más adelante al móvil o a una Raspberry.
// ============================================================

public sealed class ServidorProgramaWeb : IDisposable
{
    private const int MaxCabeceras = 16 * 1024;

    private const int MaxCuerpo = 128 * 1024;

    private static readonly TimeSpan EsperaEntreEnvios = TimeSpan.FromSeconds(3);

    private readonly int _puerto;

    private readonly Func<string, string?> _datosVisita;

    private readonly Func<string, string, bool> _guardarEnvio;

    private readonly string _pagina;

    private readonly ConcurrentDictionary<string, DateTime> _ultimoEnvio = new();

    private TcpListener? _escucha;

    private CancellationTokenSource? _cancelacion;


    /// <param name="datosVisita">Código → JSON con los datos de la visita para la página (null si no existe).</param>
    /// <param name="guardarEnvio">Código y JSON enviado → si se ha guardado.</param>
    /// <param name="pagina">La página; «/*VISITA*/null» se sustituye por los datos de la visita.</param>
    public ServidorProgramaWeb(
        int puerto,
        Func<string, string?> datosVisita,
        Func<string, string, bool> guardarEnvio,
        string pagina)
    {
        _puerto = puerto;
        _datosVisita = datosVisita;
        _guardarEnvio = guardarEnvio;
        _pagina = pagina;
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
                limite.CancelAfter(TimeSpan.FromSeconds(20));

                NetworkStream red = cliente.GetStream();

                (string metodo, string ruta, int largo, byte[] sobrante)? peticion =
                    await LeerCabecerasAsync(red, limite.Token);

                if (peticion is not { } p)
                {
                    await ResponderAsync(red, 400, "text/plain", "Petición no válida", limite.Token);
                    return;
                }

                await ProcesarAsync(red, p.metodo, p.ruta, p.largo, p.sobrante, limite.Token);
            }
            catch
            {
            }
        }
    }


    // Método, ruta, largo del cuerpo y lo que se leyó de más al buscar el
    // final de las cabeceras (el principio del cuerpo).
    private static async Task<(string, string, int, byte[])?> LeerCabecerasAsync(NetworkStream red, CancellationToken token)
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

            foreach (string linea in lineas)
            {
                if (linea.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && !int.TryParse(linea.Substring("Content-Length:".Length).Trim(), out largo))
                {
                    return null;
                }
            }

            return (primera[0].ToUpperInvariant(), primera[1], largo, sobrante);
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


    private async Task ProcesarAsync(NetworkStream red, string metodo, string ruta, int largo, byte[] sobrante, CancellationToken token)
    {
        // Solo /programa/{código} (sin consulta ni nada más).
        string camino = ruta.Split('?')[0];
        string[] partes = camino.Trim('/').Split('/');

        if (partes.Length != 2
            || partes[0] != "programa"
            || !ProgramaWeb.CodigoValido(partes[1]))
        {
            await ResponderAsync(red, 404, "text/plain", "No existe", token);
            return;
        }

        string codigo = partes[1];
        string? datos = _datosVisita(codigo);

        if (datos is null)
        {
            await ResponderAsync(red, 404, "text/plain", "Este enlace no es válido o ha caducado.", token);
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
            await ResponderAsync(red, 405, "text/plain", "Método no permitido", token);
            return;
        }

        if (largo <= 0 || largo > MaxCuerpo)
        {
            await ResponderAsync(red, 413, "application/json", "{\"ok\":false,\"error\":\"tamaño\"}", token);
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


    private static async Task ResponderAsync(NetworkStream red, int estado, string tipo, string texto, CancellationToken token)
    {
        byte[] cuerpo = Encoding.UTF8.GetBytes(texto);

        string motivo = estado switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
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
