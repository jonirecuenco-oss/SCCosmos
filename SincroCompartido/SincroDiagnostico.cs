namespace SCCosmos.Sincro;

// =============================================================
// DIAGNÓSTICO DEL MOTOR DE SINCRONIZACIÓN
//
// Muchos errores del motor se capturaban en silencio (catch
// vacíos): la sincronización sigue igual, pero ahora quedan
// anotados en el registro de errores de cada app, que se engancha
// en Anotar al arrancar. Solo se anotan los inesperados (cortes de
// red y esperas canceladas son normales) y, como mucho, uno cada
// 10 minutos por sitio, para no llenar el registro si el mismo
// fallo se repite en cada pasada.
// =============================================================

public static class SincroDiagnostico
{
    /// <summary>
    /// Enganche de la app (su registro de errores). Si nadie se engancha,
    /// no se anota nada.
    /// </summary>
    public static Action<Exception, string>? Anotar;

    private static readonly TimeSpan Intervalo =
        TimeSpan.FromMinutes(10);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _ultimos =
        new();


    /// <summary>
    /// Error del motor (base de datos, carpeta, configuración).
    /// </summary>
    public static void Error(
        Exception ex,
        string sitio)
    {
        if (ex is OperationCanceledException
            || ex is ObjectDisposedException)
        {
            return;
        }

        Registrar(
            ex,
            sitio);
    }


    /// <summary>
    /// Error en la red: los cortes de conexión y los tiempos de espera
    /// son normales (el otro se apaga, cambia de red...) y no se anotan.
    /// </summary>
    public static void ErrorRed(
        Exception ex,
        string sitio)
    {
        if (EsNormalEnRed(ex))
        {
            return;
        }

        Registrar(
            ex,
            sitio);
    }


    private static bool EsNormalEnRed(
        Exception ex) =>
        ex is OperationCanceledException
        || ex is ObjectDisposedException
        || ex is TimeoutException
        || ex is System.IO.IOException
        || ex is System.Net.Sockets.SocketException
        || (ex is AggregateException agregada
            && agregada.InnerExceptions.All(EsNormalEnRed));


    private static void Registrar(
        Exception ex,
        string sitio)
    {
        try
        {
            if (Anotar is null)
            {
                return;
            }

            DateTime ahora =
                DateTime.UtcNow;

            if (_ultimos.TryGetValue(
                    sitio,
                    out DateTime ultimo)
                && ahora - ultimo < Intervalo)
            {
                return;
            }

            _ultimos[sitio] =
                ahora;

            Anotar(
                ex,
                "Sincro." + sitio);
        }
        catch
        {
            // El diagnóstico nunca debe romper la sincronización.
        }
    }
}
