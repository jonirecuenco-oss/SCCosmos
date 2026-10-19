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

                if (_destino == "alertas")
                {
                    // Alertas pendientes (campana).
                    try
                    {
                        if (Shell.Current is not null)
                        {
                            await MainThread
                                .InvokeOnMainThreadAsync(
                                    () => Shell.Current
                                        .Navigation
                                        .PushAsync(
                                            new Views.AlertasPage()));

                            _destino = "";
                            return;
                        }
                    }
                    catch
                    {
                    }

                    continue;
                }

                if (_destino == "visita")
                {
                    // Programa de la visita de la semana actual.
                    try
                    {
                        DateTime hoy = DateTime.Today;

                        int diasDesdeLunes =
                            ((int)hoy.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

                        DateTime martes =
                            hoy.AddDays(-diasDesdeLunes).Date.AddDays(1);

                        var semana =
                            Data.DatabaseHelper.ObtenerSemana(
                                martes.ToString("yyyy-MM-dd"));

                        if (semana is not null && Shell.Current is not null)
                        {
                            await MainThread
                                .InvokeOnMainThreadAsync(
                                    () => Shell.Current
                                        .Navigation
                                        .PushAsync(
                                            new Views.VisitaDetallePage(
                                                semana)));

                            _destino = "";
                            return;
                        }
                    }
                    catch
                    {
                    }

                    continue;
                }

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
