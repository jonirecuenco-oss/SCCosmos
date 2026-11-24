using System.Text;
using Android.Bluetooth;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// La conexión con el OBDLink: Bluetooth clásico, como un puerto serie. Se le
/// manda una orden de texto y responde hasta el «>» con que acaba cada respuesta.
/// Solo admite una conexión: si la app de OBDLink está conectada, falla.
/// </summary>
public sealed class ConexionObd : IDisposable
{
    private static readonly Java.Util.UUID PuertoSerie =
        Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    private readonly BluetoothSocket _socket;
    private readonly Stream _salida;
    private readonly StringBuilder _recibido = new();
    private readonly SemaphoreSlim _llega = new(0);

    private ConexionObd(BluetoothSocket socket)
    {
        _socket = socket;
        _salida = socket.OutputStream!;

        Stream entrada = socket.InputStream!;
        Task.Run(() => Leer(entrada));
    }

    /// <summary>El OBDLink entre los dispositivos emparejados, por su nombre.</summary>
    public static BluetoothDevice? Buscar(BluetoothAdapter adaptador, out string emparejados)
    {
        List<BluetoothDevice> lista = adaptador.BondedDevices?.ToList() ?? new();

        emparejados = string.Join(", ", lista.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

        return lista.FirstOrDefault(d => d.Name?.Contains("OBDLink", StringComparison.OrdinalIgnoreCase) == true)
            ?? lista.FirstOrDefault(d => d.Name?.Contains("OBD", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Se conecta (tarda unos segundos, fuera del hilo de la pantalla); si no puede, lanza la excepción.</summary>
    public static ConexionObd Conectar(BluetoothDevice dispositivo)
    {
        BluetoothSocket socket = dispositivo.CreateRfcommSocketToServiceRecord(PuertoSerie)!;

        try
        {
            socket.Connect();
        }
        catch (Exception)
        {
            socket.Close();

            // Algunos adaptadores solo aceptan la conexión sin cifrar.
            socket = dispositivo.CreateInsecureRfcommSocketToServiceRecord(PuertoSerie)!;

            try
            {
                socket.Connect();
            }
            catch (Exception)
            {
                socket.Close();
                throw;
            }
        }

        return new ConexionObd(socket);
    }

    /// <summary>Manda una orden y devuelve lo que responde, sin el «>»; null si no responde a tiempo.</summary>
    public string? Enviar(string orden, int milisegundos = 5000)
    {
        lock (_recibido)
        {
            _recibido.Clear();
        }

        byte[] bytes = Encoding.ASCII.GetBytes(orden + "\r");
        _salida.Write(bytes, 0, bytes.Length);
        _salida.Flush();

        DateTime limite = DateTime.UtcNow.AddMilliseconds(milisegundos);

        while (true)
        {
            lock (_recibido)
            {
                string texto = _recibido.ToString();
                int fin = texto.IndexOf('>');

                if (fin >= 0)
                {
                    return texto[..fin];
                }
            }

            TimeSpan queda = limite - DateTime.UtcNow;

            if (queda <= TimeSpan.Zero)
            {
                return null;
            }

            _llega.Wait(queda);
        }
    }

    private void Leer(Stream entrada)
    {
        byte[] buffer = new byte[512];

        try
        {
            int leidos;

            while ((leidos = entrada.Read(buffer, 0, buffer.Length)) > 0)
            {
                lock (_recibido)
                {
                    _recibido.Append(Encoding.ASCII.GetString(buffer, 0, leidos));
                }

                _llega.Release();
            }
        }
        catch (Exception)
        {
            // Al cerrar la conexión se corta la lectura.
        }
    }

    public void Dispose()
    {
        try
        {
            _socket.Close();
        }
        catch (Exception)
        {
        }

        _socket.Dispose();
    }
}
