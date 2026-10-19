using System.Globalization;

using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Widget de SCCosmos: programación de hoy (programa de la visita) y las
/// próximas tareas. Se actualiza al pulsar la app, al mandarla a segundo
/// plano y, de forma automática, cada media hora (updatePeriodMillis).
/// Al pulsar el widget se abre la aplicación.
/// </summary>
[BroadcastReceiver(
    Enabled = true,
    Exported = false,
    Label = "SCCosmos")]
[IntentFilter(
    new[]
    {
        AppWidgetManager.ActionAppwidgetUpdate
    })]
[MetaData(
    "android.appwidget.provider",
    Resource = "@xml/widget_sccosmos_info")]
public class SccosmosWidgetProvider : AppWidgetProvider
{
    private const int IdTocar =
        4800;


    public override void OnUpdate(
        Context? context,
        AppWidgetManager? appWidgetManager,
        int[]? appWidgetIds)
    {
        base.OnUpdate(
            context,
            appWidgetManager,
            appWidgetIds);

        if (context is null
            || appWidgetManager is null
            || appWidgetIds is null)
        {
            return;
        }

        foreach (int id in appWidgetIds)
        {
            ActualizarWidget(
                context,
                appWidgetManager,
                id);
        }
    }


    /// <summary>Actualiza todos los widgets puestos en el escritorio.</summary>
    public static void ActualizarTodos()
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            AppWidgetManager? gestor =
                AppWidgetManager.GetInstance(
                    contexto);

            if (gestor is null)
            {
                return;
            }

            var componente =
                new ComponentName(
                    contexto,
                    Java.Lang.Class.FromType(
                        typeof(SccosmosWidgetProvider)));

            int[]? ids =
                gestor.GetAppWidgetIds(
                    componente);

            if (ids is null)
            {
                return;
            }

            foreach (int id in ids)
            {
                ActualizarWidget(
                    contexto,
                    gestor,
                    id);
            }
        }
        catch
        {
        }
    }


    private static void ActualizarWidget(
        Context contexto,
        AppWidgetManager gestor,
        int id)
    {
        try
        {
            var vistas =
                new RemoteViews(
                    contexto.PackageName,
                    Resource.Layout.widget_sccosmos);

            // Al pulsar el widget se abre la aplicación.
            Intent? intencion =
                contexto.PackageManager
                    ?.GetLaunchIntentForPackage(
                        contexto.PackageName ?? "");

            PendingIntent? pendiente =
                intencion is null
                    ? null
                    : PendingIntent.GetActivity(
                        contexto,
                        IdTocar,
                        intencion,
                        PendingIntentFlags.Immutable
                        | PendingIntentFlags.UpdateCurrent);

            if (pendiente is not null)
            {
                vistas.SetOnClickPendingIntent(
                    Resource.Id.widget_titulo,
                    pendiente);
            }

            vistas.SetTextViewText(
                Resource.Id.widget_fecha,
                DateTime.Now.ToString(
                    "ddd d MMM",
                    new CultureInfo("es-ES")));

            // Programa de hoy.
            List<string> programa =
                ObtenerProgramaDeHoy();

            RellenarLineas(
                vistas,
                new[]
                {
                    Resource.Id.widget_programa1,
                    Resource.Id.widget_programa2,
                    Resource.Id.widget_programa3
                },
                programa,
                "Sin programación para hoy");

            // Próximas tareas.
            RellenarLineas(
                vistas,
                new[]
                {
                    Resource.Id.widget_tarea1,
                    Resource.Id.widget_tarea2,
                    Resource.Id.widget_tarea3
                },
                ObtenerProximasTareas(),
                "Sin tareas pendientes");

            gestor.UpdateAppWidget(
                id,
                vistas);
        }
        catch
        {
        }
    }


    private static void RellenarLineas(
        RemoteViews vistas,
        int[] ids,
        List<string> lineas,
        string textoVacio)
    {
        if (lineas.Count == 0)
        {
            vistas.SetTextViewText(
                ids[0],
                textoVacio);
        }

        for (int i = 0;
             i < ids.Length;
             i++)
        {
            if (i < lineas.Count)
            {
                vistas.SetTextViewText(
                    ids[i],
                    lineas[i]);

                vistas.SetViewVisibility(
                    ids[i],
                    global::Android.Views
                        .ViewStates.Visible);
            }
            else if (lineas.Count > 0)
            {
                vistas.SetViewVisibility(
                    ids[i],
                    global::Android.Views
                        .ViewStates.Gone);
            }
            else
            {
                vistas.SetViewVisibility(
                    ids[i],
                    i == 0
                        ? global::Android.Views
                            .ViewStates.Visible
                        : global::Android.Views
                            .ViewStates.Gone);
            }
        }
    }


    private static List<string> ObtenerProgramaDeHoy()
    {
        var resultado =
            new List<string>();

        try
        {
            DateTime hoy =
                DateTime.Today;

            // La semana de la visita va de lunes a domingo y el martes es
            // la fecha con la que se guarda en la base.
            DateTime lunes =
                hoy.AddDays(
                    -(((int)hoy.DayOfWeek + 6) % 7));

            DateTime martes =
                lunes.AddDays(1);

            string diaDb =
                hoy.DayOfWeek switch
                {
                    DayOfWeek.Wednesday => "Miércoles",
                    DayOfWeek.Thursday => "Jueves",
                    DayOfWeek.Friday => "Viernes",
                    DayOfWeek.Saturday => "Sábado",
                    DayOfWeek.Sunday => "Domingo",
                    _ => ""
                };

            if (diaDb.Length == 0)
            {
                return resultado;
            }

            SemanaCalendario? semana =
                DatabaseHelper.ObtenerSemana(
                    martes.ToString("yyyy-MM-dd"));

            if (semana is null)
            {
                return resultado;
            }

            bool esVisita =
                (semana.Actividad ?? "")
                    .Trim()
                    .Equals(
                        "Visita",
                        StringComparison.OrdinalIgnoreCase)
                || (semana.Tipo ?? "")
                    .Trim()
                    .StartsWith(
                        "Visita",
                        StringComparison.OrdinalIgnoreCase);

            if (!esVisita)
            {
                return resultado;
            }

            string codigo =
                DatabaseHelper.ObtenerCodigoCongregacion(
                    semana.Congregacion);

            if (string.IsNullOrWhiteSpace(codigo))
            {
                return resultado;
            }

            string mes =
                martes.ToString("yyyy-MM");

            foreach (ActividadPrograma actividad
                     in DatabaseHelper
                         .ObtenerActividadesProgramaDia(
                             codigo,
                             mes,
                             diaDb))
            {
                string hora =
                    (actividad.Hora ?? "").Trim();

                string tipo =
                    (actividad.TipoActividad ?? "").Trim();

                string responsable =
                    (actividad.Responsable ?? "").Trim();

                // Formato antiguo: el responsable traía la actividad.
                if (responsable.Equals(
                        "Comida",
                        StringComparison.OrdinalIgnoreCase)
                    || responsable.Equals(
                        "Cenar",
                        StringComparison.OrdinalIgnoreCase)
                    || responsable.Equals(
                        "Dormir",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (tipo.Length == 0)
                    {
                        tipo = responsable;
                    }

                    responsable = "";
                }
                else if (responsable.Equals(
                             "Ambos",
                             StringComparison.OrdinalIgnoreCase))
                {
                    responsable = "";
                }
                else if (responsable.Equals(
                             "SC",
                             StringComparison.OrdinalIgnoreCase))
                {
                    responsable = "SC";
                }
                else if (responsable.Length > 0)
                {
                    responsable = "esposa";
                }

                string linea =
                    (hora.Length > 0
                        ? hora + " · "
                        : "")
                    + tipo
                    + (responsable.Length > 0
                        ? " (" + responsable + ")"
                        : "");

                if (linea.Trim().Length > 0)
                {
                    resultado.Add(
                        linea.Trim());
                }
            }
        }
        catch
        {
        }

        return resultado;
    }


    private static List<string> ObtenerProximasTareas()
    {
        var resultado =
            new List<string>();

        try
        {
            DateTime hoy =
                DateTime.Today;

            var tareas =
                DatabaseHelper
                    .ObtenerTareasMobile()
                    .Where(t => !t.Completada)
                    .OrderBy(
                        t => string.IsNullOrWhiteSpace(
                                 t.FechaInicio)
                             ? "9999"
                             : t.FechaInicio)
                    .ThenBy(t => t.Hora ?? "")
                    .Take(3);

            foreach (Tarea tarea in tareas)
            {
                string cuando =
                    "";

                if (DateTime.TryParse(
                        tarea.FechaInicio,
                        out DateTime fecha))
                {
                    cuando =
                        fecha.Date == hoy
                            ? "Hoy"
                            : fecha.Date == hoy.AddDays(1)
                                ? "Mañana"
                                : fecha.ToString(
                                    "d MMM",
                                    new CultureInfo("es-ES"));
                }

                if (!tarea.TodoElDia
                    && !string.IsNullOrWhiteSpace(
                        tarea.Hora))
                {
                    cuando =
                        cuando.Length > 0
                            ? cuando + " " + tarea.Hora.Trim()
                            : tarea.Hora.Trim();
                }

                string titulo =
                    string.IsNullOrWhiteSpace(
                        tarea.Titulo)
                        ? "Tarea"
                        : tarea.Titulo.Trim();

                resultado.Add(
                    cuando.Length > 0
                        ? cuando + " · " + titulo
                        : titulo);
            }
        }
        catch
        {
        }

        return resultado;
    }
}
