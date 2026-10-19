namespace SCCosmos.Mobile.Services;

/// <summary>
/// Destino de una notificación pulsada: la app se abre directamente en la
/// página que toca (Tareas, Personas...).
/// </summary>
public static class NavegacionNotificacion
{
    private static string _destino = "";

    private static bool _procesando;


    public static void Solicitar(string destino)
    {
        _destino =
            (destino ?? "")
                .Trim()
                .ToLowerInvariant();

        if (_destino.Length == 0)
        {
            return;
        }

        _ = ProcesarAsync();
    }


    public static async Task ProcesarAsync()
    {
        if (_procesando)
        {
            return;
        }

        _procesando =
            true;

        try
        {
            // Se espera a que el Shell esté listo (la app puede estar
            // arrancando todavía).
            for (int intento = 0;
                 intento < 25;
                 intento++)
            {
                await Task.Delay(300);

                string ruta =
                    _destino switch
                    {
                        "tareas" => "//tareas",
                        "personas" => "//personas",
                        _ => ""
                    };

                if (ruta.Length == 0)
                {
                    _destino = "";
                    return;
                }

                if (Shell.Current is null)
                {
                    continue;
                }

                try
                {
                    await MainThread
                        .InvokeOnMainThreadAsync(
                            () => Shell.Current
                                .GoToAsync(ruta));
                }
                catch
                {
                    continue;
                }

                // Destino cumplido: se limpia (si tocan otra
                // notificación, se pedirá de nuevo).
                _destino = "";
                return;
            }
        }
        finally
        {
            _procesando = false;
        }
    }
}
