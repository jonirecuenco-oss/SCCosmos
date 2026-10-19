using System.Globalization;

using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Widget de SCCosmos: programación del día elegido (programa de la visita)
/// y sus tareas. Se puede mover un día adelante o atrás con las flechas.
/// Al pulsar una actividad se abre el programa de la visita y al pulsar una
/// tarea se abre la página de Tareas.
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

    private const int IdDiaAtras =
        4801;

    private const int IdDiaAdelante =
        4802;

    private const string AccionDiaAtras =
        "sccosmos.widget.dia_atras";

    private const string AccionDiaAdelante =
        "sccosmos.widget.dia_adelante";

    private const string ClaveDia =
        "Sccosmos_WidgetDia";

    private const string ExtraDestino =
        "sccosmos_destino";


    public static DateTime ObtenerDiaSeleccionado()
    {
        try
        {
            string texto =
                Preferences.Default.Get(
                    ClaveDia,
                    "");

            if (DateTime.TryParseExact(
                    texto,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime dia))
            {
                return dia.Date;
            }
        }
        catch
        {
        }

        return DateTime.Today;
    }


    private static void GuardarDiaSeleccionado(DateTime dia)
    {
        try
        {
            Preferences.Default.Set(
                ClaveDia,
                dia.ToString("yyyy-MM-dd"));
        }
        catch
        {
        }
    }


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


    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        string accion =
            intent?.Action ?? "";

        if (accion == AccionDiaAtras
            || accion == AccionDiaAdelante)
        {
            DateTime dia =
                ObtenerDiaSeleccionado()
                    .AddDays(
                        accion == AccionDiaAtras
                            ? -1
                            : 1);

            GuardarDiaSeleccionado(dia);
            ActualizarTodos();

            return;
        }

        base.OnReceive(context, intent);
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

            // Al pulsar el título se abre la aplicación.
            PendingIntent? abrir =
                CrearActividad(
                    contexto,
                    IdTocar,
                    "");

            if (abrir is not null)
            {
                vistas.SetOnClickPendingIntent(
                    Resource.Id.widget_titulo,
                    abrir);
            }

            DateTime dia =
                ObtenerDiaSeleccionado();

            DateTime hoy =
                DateTime.Today;

            string etiquetaDia =
                dia == hoy
                    ? "Hoy"
                    : dia == hoy.AddDays(1)
                        ? "Mañana"
                        : dia == hoy.AddDays(-1)
                            ? "Ayer"
                            : dia.ToString(
                                "ddd d MMM",
                                new CultureInfo("es-ES"));

            vistas.SetTextViewText(
                Resource.Id.widget_fecha,
                etiquetaDia);

            // Flechas para moverse de día.
            vistas.SetOnClickPendingIntent(
                Resource.Id.widget_dia_atras,
                CrearAccionDia(
                    contexto,
                    IdDiaAtras,
                    AccionDiaAtras));

            vistas.SetOnClickPendingIntent(
                Resource.Id.widget_dia_adelante,
                CrearAccionDia(
                    contexto,
                    IdDiaAdelante,
                    AccionDiaAdelante));

            // Programación del día: programa de la visita.
            List<string> programa =
                ObtenerProgramaDelDia(dia);

            vistas.SetTextViewText(
                Resource.Id.widget_seccion_hoy,
                "PROGRAMACIÓN · " + etiquetaDia.ToUpperInvariant());

            int[] filasPrograma =
            {
                Resource.Id.widget_programa1,
                Resource.Id.widget_programa2,
                Resource.Id.widget_programa3
            };

            RellenarLineas(
                vistas,
                filasPrograma,
                programa,
                "Sin programación para ese día");

            for (int i = 0;
                 i < filasPrograma.Length;
                 i++)
            {
                PendingIntent? tocar =
                    CrearActividad(
                        contexto,
                        4810 + i,
                        "visita");

                if (tocar is not null)
                {
                    vistas.SetOnClickPendingIntent(
                        filasPrograma[i],
                        tocar);
                }
            }

            // Tareas del día.
            List<string> tareas =
                ObtenerTareasDelDia(dia);

            int[] filasTareas =
            {
                Resource.Id.widget_tarea1,
                Resource.Id.widget_tarea2,
                Resource.Id.widget_tarea3
            };

            RellenarLineas(
                vistas,
                filasTareas,
                tareas,
                "Sin tareas para ese día");

            for (int i = 0;
                 i < filasTareas.Length;
                 i++)
            {
                PendingIntent? tocar =
                    CrearActividad(
                        contexto,
                        4820 + i,
                        "tareas");

                if (tocar is not null)
                {
                    vistas.SetOnClickPendingIntent(
                        filasTareas[i],
                        tocar);
                }
            }

            // Alertas pendientes de resolver.
            List<string> alertas =
                ObtenerAlertasPendientes();

            int[] filasAlertas =
            {
                Resource.Id.widget_alerta1,
                Resource.Id.widget_alerta2,
                Resource.Id.widget_alerta3
            };

            vistas.SetViewVisibility(
                Resource.Id.widget_seccion_alertas,
                alertas.Count > 0
                    ? global::Android.Views
                        .ViewStates.Visible
                    : global::Android.Views
                        .ViewStates.Gone);

            for (int i = 0;
                 i < filasAlertas.Length;
                 i++)
            {
                bool visible =
                    i < alertas.Count;

                if (visible)
                {
                    vistas.SetTextViewText(
                        filasAlertas[i],
                        "⚠️ " + alertas[i]);
                }

                vistas.SetViewVisibility(
                    filasAlertas[i],
                    visible
                        ? global::Android.Views
                            .ViewStates.Visible
                        : global::Android.Views
                            .ViewStates.Gone);

                PendingIntent? tocar =
                    CrearActividad(
                        contexto,
                        4830 + i,
                        "alertas");

                if (tocar is not null)
                {
                    vistas.SetOnClickPendingIntent(
                        filasAlertas[i],
                        tocar);
                }
            }

            gestor.UpdateAppWidget(
                id,
                vistas);
        }
        catch
        {
        }
    }


    private static PendingIntent? CrearActividad(
        Context contexto,
        int codigo,
        string destino)
    {
        Intent? intencion =
            contexto.PackageManager
                ?.GetLaunchIntentForPackage(
                    contexto.PackageName ?? "");

        if (intencion is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(destino))
        {
            intencion.PutExtra(
                ExtraDestino,
                destino);
        }

        return PendingIntent.GetActivity(
            contexto,
            codigo,
            intencion,
            PendingIntentFlags.Immutable
            | PendingIntentFlags.UpdateCurrent);
    }


    private static PendingIntent CrearAccionDia(
        Context contexto,
        int codigo,
        string accion)
    {
        var intencion =
            new Intent(
                contexto,
                typeof(SccosmosWidgetProvider));

        intencion.SetAction(
            accion);

        return PendingIntent.GetBroadcast(
                   contexto,
                   codigo,
                   intencion,
                   PendingIntentFlags.Immutable
                   | PendingIntentFlags.UpdateCurrent)
               ?? PendingIntent.GetBroadcast(
                   contexto,
                   codigo,
                   intencion,
                   PendingIntentFlags.UpdateCurrent)!;
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


    private static List<string> ObtenerProgramaDelDia(
        DateTime dia)
    {
        var resultado =
            new List<string>();

        try
        {
            // La semana va de lunes a domingo y el martes es la fecha con
            // la que se guardan las semanas.
            DateTime lunes =
                dia.AddDays(
                    -(((int)dia.DayOfWeek + 6) % 7));

            DateTime martes =
                lunes.AddDays(1);

            string diaDb =
                dia.DayOfWeek switch
                {
                    DayOfWeek.Monday => "Lunes",
                    DayOfWeek.Tuesday => "Martes",
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


    private static List<string> ObtenerAlertasPendientes()
    {
        var resultado =
            new List<string>();

        try
        {
            foreach (AlertaMobile alerta
                     in Services.AlertasMobileService
                         .Calcular())
            {
                string texto =
                    string.IsNullOrWhiteSpace(
                        alerta.Descripcion)
                        ? alerta.Titulo
                        : alerta.Titulo
                          + " — "
                          + alerta.Descripcion;

                if (!string.IsNullOrWhiteSpace(texto))
                {
                    resultado.Add(texto);
                }

                if (resultado.Count >= 3)
                {
                    break;
                }
            }
        }
        catch
        {
        }

        return resultado;
    }


    private static List<string> ObtenerTareasDelDia(
        DateTime dia)
    {
        var resultado =
            new List<string>();

        try
        {
            foreach (Tarea tarea
                     in DatabaseHelper
                         .ObtenerTareasMobile()
                         .Where(t => !t.Completada)
                         .OrderBy(t => t.Hora ?? ""))
            {
                if (!DateTime.TryParse(
                        tarea.FechaInicio,
                        out DateTime fecha)
                    || fecha.Date != dia.Date)
                {
                    continue;
                }

                string cuando =
                    !tarea.TodoElDia
                    && !string.IsNullOrWhiteSpace(
                        tarea.Hora)
                        ? tarea.Hora.Trim() + " · "
                        : "";

                string titulo =
                    string.IsNullOrWhiteSpace(
                        tarea.Titulo)
                        ? "Tarea"
                        : tarea.Titulo.Trim();

                resultado.Add(
                    cuando + titulo);

                if (resultado.Count >= 3)
                {
                    break;
                }
            }
        }
        catch
        {
        }

        return resultado;
    }
}
