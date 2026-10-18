using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Calcula cuándo toca avisar de una tarea (con repeticiones y rangos) y
/// lanza la actualización de las notificaciones del sistema una sola vez por
/// aviso. El registro de avisos ya mostrados es local del dispositivo (no se
/// sincroniza), para que cada aparato avise por su cuenta.
/// </summary>
public static class TareasAvisoService
{
    private static readonly object _candado =
        new();

    private static HashSet<string>? _avisadas;

    private static string Ruta =>
        Path.Combine(
            FileSystem.AppDataDirectory,
            "tareas_avisadas.json");

    private static HashSet<string> Avisadas
    {
        get
        {
            if (_avisadas == null)
            {
                try
                {
                    _avisadas =
                        File.Exists(Ruta)
                            ? System.Text.Json.JsonSerializer
                                  .Deserialize<HashSet<string>>(
                                      File.ReadAllText(Ruta))
                              ?? new HashSet<string>(StringComparer.Ordinal)
                            : new HashSet<string>(StringComparer.Ordinal);
                }
                catch
                {
                    _avisadas =
                        new HashSet<string>(StringComparer.Ordinal);
                }
            }

            return _avisadas;
        }
    }

    private static void Guardar()
    {
        try
        {
            File.WriteAllText(
                Ruta,
                System.Text.Json.JsonSerializer.Serialize(
                    Avisadas));
        }
        catch
        {
        }
    }


    /// <summary>
    /// La cita (fecha + hora) de una ocurrencia concreta.
    /// </summary>
    private static DateTime CitaDe(
        Tarea tarea,
        DateTime fecha)
    {
        TimeSpan hora =
            TimeSpan.Zero;

        if (!tarea.TodoElDia
            && TimeSpan.TryParse(
                (tarea.Hora ?? "").Trim(),
                out TimeSpan horaParseada))
        {
            hora =
                horaParseada;
        }

        return fecha.Date + hora;
    }


    /// <summary>
    /// Ocurrencia más reciente cuyo aviso ya ha vencido (y cuyo siguiente
    /// aviso aún no vence). Null si no toca avisar.
    /// </summary>
    public static DateTime? OcurrenciaAvisoVencida(
        Tarea tarea,
        DateTime ahora)
    {
        if (string.IsNullOrWhiteSpace(tarea.FechaInicio))
        {
            return null;
        }

        if (!DateTime.TryParse(
                tarea.FechaInicio,
                out DateTime inicio))
        {
            return null;
        }

        if (tarea.AvisoMinutos < 0)
        {
            return null;
        }

        DateTime? finRango =
            null;

        if (!string.IsNullOrWhiteSpace(tarea.FechaFin)
            && DateTime.TryParse(
                tarea.FechaFin,
                out DateTime fin))
        {
            finRango =
                fin.Date;
        }

        int antelacion =
            Math.Max(
                0,
                tarea.AvisoMinutos);

        DateTime? mejor =
            null;

        DateTime fecha =
            inicio.Date;

        for (int i = 0; i < 4000; i++)
        {
            DateTime cita =
                CitaDe(
                    tarea,
                    fecha);

            DateTime aviso =
                cita.AddMinutes(-antelacion);

            if (aviso > ahora)
            {
                break;
            }

            if (finRango.HasValue
                && fecha.Date > finRango.Value)
            {
                break;
            }

            mejor =
                cita;

            switch ((tarea.Repeticion ?? "Ninguna").Trim())
            {
                case "Diaria":
                    fecha =
                        fecha.AddDays(1);
                    break;

                case "Semanal":
                    fecha =
                        fecha.AddDays(7);
                    break;

                case "Mensual":
                    fecha =
                        fecha.AddMonths(1);
                    break;

                case "Anual":
                    fecha =
                        fecha.AddYears(1);
                    break;

                default:
                    return mejor;
            }
        }

        return mejor;
    }


    /// <summary>
    /// Comprueba si hay avisos nuevos y, si los hay, actualiza las
    /// notificaciones del sistema (contador incluido).
    /// </summary>
    public static async Task VerificarAsync()
    {
        // La esposa del SC no recibe notificaciones de ningún tipo.
        if (PerfilAccesoService.Configurado
            && !PerfilAccesoService.EsHombre)
        {
            return;
        }

        try
        {
            List<Tarea> tareas =
                await Task.Run(
                    () => DatabaseHelper.ObtenerTareasMobile());

            DateTime ahora =
                DateTime.Now;

            bool hayNuevas =
                false;

            lock (_candado)
            {
                foreach (Tarea tarea in tareas)
                {
                    if (tarea.Completada
                        || tarea.AvisoMinutos < 0)
                    {
                        continue;
                    }

                    DateTime? cita =
                        OcurrenciaAvisoVencida(
                            tarea,
                            ahora);

                    if (cita == null)
                    {
                        continue;
                    }

                    string clave =
                        tarea.Id +
                        "|" +
                        cita.Value.ToString("yyyy-MM-dd-HHmm");

                    if (Avisadas.Add(clave))
                    {
                        hayNuevas =
                            true;
                    }
                }

                if (hayNuevas)
                {
                    Guardar();
                }
            }

            if (hayNuevas)
            {
                await AlertasNotificacionService
                    .ActualizarAsync();
            }
        }
        catch
        {
            // Silencioso: se reintentará en la siguiente comprobación.
        }
    }
}
