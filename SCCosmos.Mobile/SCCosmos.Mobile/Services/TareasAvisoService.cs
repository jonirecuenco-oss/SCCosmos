using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Platforms.Android;

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
    /// Comprueba si hay avisos de tareas nuevos y los publica como
    /// notificaciones del sistema (una por aviso). Después reprograma la
    /// siguiente alarma de tareas.
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
            var (nuevos, _) =
                await Task.Run(
                    () => CalcularAvisos(DateTime.Now));

            if (nuevos.Count > 0)
            {
                foreach (AvisoDeTarea aviso in nuevos)
                {
                    AlertasNotificacionAndroid
                        .PublicarAvisoTarea(
                            aviso.Clave,
                            aviso.Titulo,
                            aviso.Texto);
                }

                lock (_candado)
                {
                    foreach (AvisoDeTarea aviso in nuevos)
                    {
                        Avisadas.Add(aviso.Clave);
                    }

                    Guardar();
                }
            }
        }
        catch
        {
        }

        ProgramarSiguiente();
    }


    /// <summary>
    /// Programa la siguiente alarma de avisos de tareas (Android).
    /// </summary>
    public static void ProgramarSiguiente()
    {
#if ANDROID
        try
        {
            SCCosmos.Mobile.Platforms.Android
                .TareasAvisoAlarmScheduler
                .ProgramarSiguiente();
        }
        catch
        {
        }
#endif
    }


    /// <summary>
    /// Avisos de tareas ya vencidos que aún no se han mostrado (de las
    /// últimas 24 horas) y el momento del siguiente aviso:
    ///
    ///   · Tarea de todo el día → al empezar el día (00:00).
    ///   · Tarea con una hora → a su hora.
    ///   · Con aviso previo → además, con la antelación configurada.
    /// </summary>
    public static (List<AvisoDeTarea> Nuevos, DateTime? Proximo)
        CalcularAvisos(
            DateTime ahora)
    {
        var nuevos =
            new List<AvisoDeTarea>();

        DateTime? proximo =
            null;

        try
        {
            foreach (Tarea tarea
                     in DatabaseHelper.ObtenerTareasMobile())
            {
                if (tarea.Completada
                    || string.IsNullOrWhiteSpace(tarea.FechaInicio)
                    || !DateTime.TryParse(
                        tarea.FechaInicio,
                        out DateTime inicio))
                {
                    continue;
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

                string repeticion =
                    (tarea.Repeticion ?? "Ninguna").Trim();

                DateTime fecha =
                    inicio.Date;

                for (int i = 0; i < 4000; i++)
                {
                    if (finRango.HasValue
                        && fecha.Date > finRango.Value)
                    {
                        break;
                    }

                    DateTime cita =
                        CitaDe(
                            tarea,
                            fecha);

                    foreach ((DateTime momento, bool esPrevio)
                             in MomentosDe(tarea, cita))
                    {
                        if (momento > ahora)
                        {
                            if (proximo == null
                                || momento < proximo)
                            {
                                proximo =
                                    momento;
                            }

                            break;
                        }

                        if (momento < ahora.AddHours(-24))
                        {
                            continue;
                        }

                        string clave =
                            Clave(
                                tarea,
                                fecha,
                                esPrevio);

                        if (Avisadas.Contains(
                                clave))
                        {
                            continue;
                        }

                        nuevos.Add(
                            new AvisoDeTarea
                            {
                                Clave = clave,
                                Titulo =
                                    "Tarea: "
                                    + (string.IsNullOrWhiteSpace(
                                            tarea.Titulo)
                                        ? "sin título"
                                        : tarea.Titulo),
                                Texto =
                                    TextoDe(
                                        tarea,
                                        momento,
                                        esPrevio)
                            });
                    }

                    if (repeticion == "Diaria")
                    {
                        fecha =
                            fecha.AddDays(1);
                    }
                    else if (repeticion == "Semanal")
                    {
                        fecha =
                            fecha.AddDays(7);
                    }
                    else if (repeticion == "Mensual")
                    {
                        fecha =
                            fecha.AddMonths(1);
                    }
                    else if (repeticion == "Anual")
                    {
                        fecha =
                            fecha.AddYears(1);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return (nuevos, proximo);
    }


    /// <summary>
    /// Los momentos de aviso de una ocurrencia: el aviso previo (si hay
    /// antelación) y el momento de inicio.
    /// </summary>
    private static IEnumerable<(DateTime Momento, bool EsPrevio)>
        MomentosDe(
            Tarea tarea,
            DateTime cita)
    {
        int antelacion =
            Math.Max(
                0,
                tarea.AvisoMinutos);

        if (antelacion > 0)
        {
            yield return (
                cita.AddMinutes(-antelacion),
                true);
        }

        yield return (cita, false);
    }


    private static string Clave(
        Tarea tarea,
        DateTime fecha,
        bool esPrevio)
    {
        return tarea.Id
               + "|"
               + fecha.ToString("yyyy-MM-dd")
               + (esPrevio
                   ? "|previo"
                   : "|inicio");
    }


    private static string TextoDe(
        Tarea tarea,
        DateTime momento,
        bool esPrevio)
    {
        DateTime hoy =
            DateTime.Today;

        string cuando =
            momento.Date == hoy
                ? $"Empieza hoy a las {momento:HH:mm}"
                : momento.Date == hoy.AddDays(1)
                    ? $"Empieza mañana a las {momento:HH:mm}"
                    : "Empieza el "
                      + momento.ToString("dd MMM 'a las' HH:mm");

        if (esPrevio)
        {
            cuando =
                $"En {Math.Max(0, tarea.AvisoMinutos)} min · "
                + cuando;
        }

        string texto =
            cuando;

        if (!string.IsNullOrWhiteSpace(
                tarea.Descripcion))
        {
            texto +=
                " · " + tarea.Descripcion;
        }

        return texto;
    }
}


/// <summary>Un aviso de tarea listo para publicar como notificación.</summary>
public sealed class AvisoDeTarea
{
    public string Clave { get; set; } = "";

    public string Titulo { get; set; } = "";

    public string Texto { get; set; } = "";
}
