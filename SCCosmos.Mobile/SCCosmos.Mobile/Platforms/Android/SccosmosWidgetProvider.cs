using System.Globalization;

using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Widget de SCCosmos: programación del día elegido (programa de la
/// visita), tareas, alertas y predicación. El contenido va en una lista
/// desplazable (widget de colección) para que todo quepa aunque la celda
/// del escritorio sea pequeña. La cabecera (icono, título, flechas de
/// día) queda fija arriba.
/// </summary>
[BroadcastReceiver(
    Enabled = true,
    Exported = false,
    Label = "SCCosmos")]
[IntentFilter(
    new[]
    {
        AppWidgetManager.ActionAppwidgetUpdate,
        // Cambio de hora, de zona o de día: se repinta con el día nuevo.
        Intent.ActionTimeChanged,
        Intent.ActionTimezoneChanged,
        Intent.ActionDateChanged
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

    // Día en que se eligió ClaveDia: lo elegido con las flechas solo vale
    // ese día; al cambiar de día el widget vuelve a «Hoy».
    private const string ClaveDiaElegidoEl =
        "Sccosmos_WidgetDiaElegidoEl";

    // Alarma de las 00:00 para que el widget cambie de día él solo.
    private const string AccionMedianoche =
        "sccosmos.widget.medianoche";

    private const int IdMedianoche =
        4803;

    private const string ExtraDestino =
        "sccosmos_destino";

    private const string AccionTareaOk =
        "sccosmos_widget_tarea_ok";

    // Acción única para los elementos de la lista: en un widget de
    // colección los clics de las filas se envían con «fill-in intents»
    // sobre esta plantilla, no con PendingIntent por vista.
    internal const string AccionItem =
        "sccosmos.widget.item";

    internal const string ExtraAccionItem =
        "w_accion";

    internal const string ExtraTareaItem =
        "w_tarea";

    internal const string ExtraAlertaItem =
        "w_alerta";

    internal const string ExtraConsultaItem =
        "w_consulta";

    internal const string ExtraDestinoItem =
        "w_destino";

    private const string AccionAlertaOk =
        "sccosmos_widget_alerta_ok";

    private const string ExtraTareaId =
        "sccosmos_widget_tarea_id";

    private const string ExtraAlertaFirma =
        "sccosmos_widget_alerta_firma";

    // Colores compartidos con las filas de la lista.
    internal const string ColorTareaVencida =
        "#FF7B7B";

    internal const string ColorSeccionTareas =
        "#8BC98B";

    // Minutos de margen antes de ocultar una actividad ya pasada.
    private const int MargenActividadMinutos =
        30;


    public static DateTime ObtenerDiaSeleccionado()
    {
        try
        {
            string texto =
                Preferences.Default.Get(
                    ClaveDia,
                    "");

            string elegidoEl =
                Preferences.Default.Get(
                    ClaveDiaElegidoEl,
                    "");

            // Lo elegido otro día ya no vale: desde las 00:00 se enseña el
            // día nuevo (antes se quedaba en el día elegido).
            if (elegidoEl == FechaClave(DateTime.Today)
                &&
                DateTime.TryParseExact(
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
                FechaClave(dia));

            Preferences.Default.Set(
                ClaveDiaElegidoEl,
                FechaClave(DateTime.Today));
        }
        catch
        {
        }
    }


    // Cuánto se ha movido el tiempo del widget en el día que se ve: «yyyy-MM-dd|horas».
    private const string ClaveTiempoDesplazamiento =
        "Widget_TiempoDesplazamiento";


    /// <summary>Horas que se ha movido el tiempo en ese día (0 si es otro día).</summary>
    internal static int DesplazamientoTiempo(DateTime dia)
    {
        string[] partes =
            Preferences.Default.Get(ClaveTiempoDesplazamiento, "").Split('|');

        return partes.Length == 2
               && partes[0] == FechaClave(dia)
               && int.TryParse(partes[1], out int horas)
            ? horas
            : 0;
    }


    private static void MoverTiempo(int paso)
    {
        DateTime dia = ObtenerDiaSeleccionado();

        Preferences.Default.Set(
            ClaveTiempoDesplazamiento,
            FechaClave(dia) + "|" + (DesplazamientoTiempo(dia) + paso * 6));
    }


    private static string FechaClave(DateTime dia) =>
        dia.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);


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

        ProgramarMedianoche(
            context);

        // Y la revisión del tiempo cada 2 horas (avisos de AEMET).
        TiempoAvisosAndroid.ProgramarRevision();
    }


    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        string accion =
            intent?.Action ?? "";

        if (accion == AccionItem)
        {
            EjecutarAccionItem(context, intent);
            return;
        }

        // A las 00:00 (alarma propia) o si cambian la hora, la zona o el
        // día: se vuelve a pintar, ya con el día nuevo.
        if (accion == AccionMedianoche
            || accion == Intent.ActionTimeChanged
            || accion == Intent.ActionTimezoneChanged
            || accion == Intent.ActionDateChanged)
        {
            ActualizarTodos();
            return;
        }

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

        if (accion == AccionTareaOk)
        {
            int tareaId =
                intent?.GetIntExtra(
                    ExtraTareaId,
                    0) ?? 0;

            if (tareaId > 0)
            {
                DatabaseHelper
                    .MarcarTareaCompletadaMobile(
                        tareaId,
                        true);

                ActualizarTodos();
            }

            return;
        }

        if (accion == AccionAlertaOk)
        {
            string firma =
                intent?.GetStringExtra(
                    ExtraAlertaFirma) ?? "";

            if (!string.IsNullOrWhiteSpace(firma))
            {
                Services.AlertasMobileService
                    .MarcarLeidaPorFirma(
                        firma);

                _ = Services.AlertasNotificacionService
                    .ActualizarAsync();

                ActualizarTodos();
            }

            return;
        }

        base.OnReceive(context, intent);
    }


    /// <summary>
    /// Ejecuta lo que se ha pulsado en una fila de la lista del widget.
    /// </summary>
    private static void EjecutarAccionItem(
        Context? contexto,
        Intent? intent)
    {
        if (contexto is null || intent is null)
        {
            return;
        }

        string accion =
            intent.GetStringExtra(ExtraAccionItem) ?? "";

        switch (accion)
        {
            case "tarea":
            {
                int id = intent.GetIntExtra(ExtraTareaItem, 0);

                if (id > 0)
                {
                    DatabaseHelper.MarcarTareaCompletadaMobile(id, true);
                    ActualizarTodos();
                }

                break;
            }

            case "alerta":
            {
                string firma =
                    intent.GetStringExtra(ExtraAlertaItem) ?? "";

                if (firma.Length > 0)
                {
                    Services.AlertasMobileService.MarcarLeidaPorFirma(firma);

                    _ = Services.AlertasNotificacionService.ActualizarAsync();

                    ActualizarTodos();
                }

                break;
            }

            case "mapa":
            {
                string consulta =
                    intent.GetStringExtra(ExtraConsultaItem) ?? "";

                if (consulta.Length > 0)
                {
                    try
                    {
                        var uri =
                            global::Android.Net.Uri.Parse(
                                "https://www.google.com/maps/search/?api=1&query="
                                + global::Android.Net.Uri.Encode(consulta));

                        var ver =
                            new Intent(Intent.ActionView, uri);

                        ver.AddFlags(ActivityFlags.NewTask);

                        contexto.StartActivity(ver);
                    }
                    catch
                    {
                    }
                }

                break;
            }

            case "tiempo_antes":
            case "tiempo_despues":
            {
                // Moverse por las horas del tiempo (de 6 en 6).
                MoverTiempo(accion == "tiempo_antes" ? -1 : 1);
                ActualizarTodos();
                break;
            }

            case "horas":
            {
                try
                {
                    var horas =
                        new Intent(contexto, typeof(HorasDialogActivity));

                    horas.AddFlags(ActivityFlags.NewTask);

                    contexto.StartActivity(horas);
                }
                catch
                {
                }

                break;
            }

            default:
            {
                string destino =
                    intent.GetStringExtra(ExtraDestinoItem) ?? "";

                Intent? abrir =
                    contexto.PackageManager?.GetLaunchIntentForPackage(
                        contexto.PackageName ?? "");

                if (abrir is not null)
                {
                    if (destino.Length > 0)
                    {
                        abrir.PutExtra(ExtraDestino, destino);
                    }

                    abrir.AddFlags(ActivityFlags.NewTask);

                    try
                    {
                        contexto.StartActivity(abrir);
                    }
                    catch
                    {
                    }
                }

                break;
            }
        }
    }


    /// <summary>Plantilla de clic para las filas de la lista.</summary>
    private static PendingIntent? CrearPlantillaItem(Context contexto)
    {
        try
        {
            var intencion =
                new Intent(contexto, typeof(SccosmosWidgetProvider));

            intencion.SetAction(AccionItem);

            return PendingIntent.GetBroadcast(
                contexto,
                4890,
                intencion,
                PendingIntentFlags.Mutable
                | PendingIntentFlags.UpdateCurrent);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>Rellena el clic de un elemento de la lista.</summary>
    internal static void PonerClicItem(
        RemoteViews vistas,
        int idVista,
        string accion,
        int tarea = 0,
        string alerta = "",
        string consulta = "",
        string destino = "")
    {
        try
        {
            var relleno = new Intent();

            relleno.PutExtra(ExtraAccionItem, accion);

            if (tarea > 0)
            {
                relleno.PutExtra(ExtraTareaItem, tarea);
            }

            if (!string.IsNullOrWhiteSpace(alerta))
            {
                relleno.PutExtra(ExtraAlertaItem, alerta);
            }

            if (!string.IsNullOrWhiteSpace(consulta))
            {
                relleno.PutExtra(ExtraConsultaItem, consulta);
            }

            if (!string.IsNullOrWhiteSpace(destino))
            {
                relleno.PutExtra(ExtraDestinoItem, destino);
            }

            vistas.SetOnClickFillInIntent(idVista, relleno);
        }
        catch
        {
        }
    }


    /// <summary>
    /// Alarma de las 00:00 para que el widget cambie de día él solo. Antes
    /// solo cambiaba cuando Android lo refrescaba (cada media hora como
    /// pronto) o al abrir la app. Es exacta si el móvil lo permite, como los
    /// avisos de tareas; si no, aproximada.
    /// </summary>
    private static void ProgramarMedianoche(
        Context contexto)
    {
        try
        {
            var gestor =
                (AlarmManager?)contexto.GetSystemService(
                    Context.AlarmService);

            if (gestor is null)
            {
                return;
            }

            long momento =
                new DateTimeOffset(
                    DateTime.Today.AddDays(1).AddSeconds(2))
                    .ToUnixTimeMilliseconds();

            PendingIntent intencion =
                CrearAccionMedianoche(
                    contexto);

            bool exacta =
                global::Android.OS.Build.VERSION.SdkInt
                    < global::Android.OS.BuildVersionCodes.S
                || gestor.CanScheduleExactAlarms();

            if (exacta)
            {
                gestor.SetExactAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    momento,
                    intencion);
            }
            else
            {
                gestor.SetAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    momento,
                    intencion);
            }
        }
        catch
        {
        }
    }


    private static PendingIntent CrearAccionMedianoche(
        Context contexto)
    {
        var intencion =
            new Intent(
                contexto,
                typeof(SccosmosWidgetProvider));

        intencion.SetAction(
            AccionMedianoche);

        return PendingIntent.GetBroadcast(
                   contexto,
                   IdMedianoche,
                   intencion,
                   PendingIntentFlags.Immutable
                   | PendingIntentFlags.UpdateCurrent)!;
    }


    /// <summary>Se ha quitado el último widget: fuera la alarma.</summary>
    public override void OnDisabled(
        Context? context)
    {
        base.OnDisabled(
            context);

        if (context is null)
        {
            return;
        }

        try
        {
            ((AlarmManager?)context.GetSystemService(
                Context.AlarmService))
                ?.Cancel(
                    CrearAccionMedianoche(
                        context));
        }
        catch
        {
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

            if (ids.Length > 0)
            {
                ProgramarMedianoche(
                    contexto);

                // Y la revisión del tiempo cada 2 horas (avisos de AEMET).
                TiempoAvisosAndroid.ProgramarRevision();
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

            var es =
                new CultureInfo("es-ES");

            // Arriba «Hoy», «Mañana», «Ayer» o el día de la semana; debajo,
            // la fecha completa.
            string etiquetaDia =
                dia == hoy
                    ? "Hoy"
                    : dia == hoy.AddDays(1)
                        ? "Mañana"
                        : dia == hoy.AddDays(-1)
                            ? "Ayer"
                            : es.TextInfo.ToTitleCase(
                                dia.ToString("dddd", es));

            bool relativo =
                Math.Abs((dia - hoy).TotalDays) <= 1;

            string fechaLarga =
                dia.ToString(
                    (relativo ? "dddd, " : "")
                    + "d 'de' MMMM"
                    + (dia.Year != hoy.Year ? " 'de' yyyy" : ""),
                    es);

            vistas.SetTextViewText(
                Resource.Id.widget_fecha,
                etiquetaDia);

            vistas.SetTextViewText(
                Resource.Id.widget_fecha_larga,
                fechaLarga);

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

            // Contenido desplazable: lista con filas (sección, evento,
            // tarea, alerta, predicación).
            try
            {
                var intencion =
                    new Intent(
                        contexto,
                        typeof(SccosmosWidgetService));

                intencion.PutExtra(
                    AppWidgetManager.ExtraAppwidgetId,
                    id);

                intencion.SetData(
                    global::Android.Net.Uri.Parse(
                        intencion.ToUri(
                            global::Android.Content.IntentUriType.Scheme)));

                vistas.SetRemoteAdapter(
                    id,
                    Resource.Id.widget_lista,
                    intencion);

                PendingIntent? plantilla = CrearPlantillaItem(contexto);

                if (plantilla is not null)
                {
                    vistas.SetPendingIntentTemplate(
                        Resource.Id.widget_lista,
                        plantilla);
                }
            }
            catch
            {
            }

            gestor.UpdateAppWidget(
                id,
                vistas);

            gestor.NotifyAppWidgetViewDataChanged(
                id,
                Resource.Id.widget_lista);
        }
        catch
        {
        }
    }


    internal static PendingIntent? CrearActividad(
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


    internal static PendingIntent? CrearAccionTarea(
        Context contexto,
        int codigo,
        int tareaId)
    {
        try
        {
            var intencion =
                new Intent(
                    contexto,
                    typeof(SccosmosWidgetProvider));

            intencion.SetAction(
                AccionTareaOk);

            intencion.PutExtra(
                ExtraTareaId,
                tareaId);

            return PendingIntent.GetBroadcast(
                contexto,
                codigo,
                intencion,
                PendingIntentFlags.Immutable
                | PendingIntentFlags.UpdateCurrent);
        }
        catch
        {
            return null;
        }
    }


    internal static PendingIntent? CrearAccionAlertaLeida(
        Context contexto,
        int codigo,
        string firma)
    {
        try
        {
            var intencion =
                new Intent(
                    contexto,
                    typeof(SccosmosWidgetProvider));

            intencion.SetAction(
                AccionAlertaOk);

            intencion.PutExtra(
                ExtraAlertaFirma,
                firma);

            return PendingIntent.GetBroadcast(
                contexto,
                codigo,
                intencion,
                PendingIntentFlags.Immutable
                | PendingIntentFlags.UpdateCurrent);
        }
        catch
        {
            return null;
        }
    }


    internal static PendingIntent? CrearAbrirMapa(
        Context contexto,
        int codigo,
        string consulta)
    {
        try
        {
            var uri =
                global::Android.Net.Uri.Parse(
                    "https://www.google.com/maps/search/?api=1&query="
                    + global::Android.Net.Uri.Encode(
                        consulta));

            var intencion =
                new Intent(
                    Intent.ActionView,
                    uri);

            intencion.AddFlags(
                ActivityFlags.NewTask);

            return PendingIntent.GetActivity(
                contexto,
                codigo,
                intencion,
                PendingIntentFlags.Immutable
                | PendingIntentFlags.UpdateCurrent);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>Minutos de la hora («9:30» → 570); sin hora, al final.</summary>
    internal static int OrdenHora(
        string hora)
    {
        string valor =
            (hora ?? "").Trim();

        if (valor.Length == 0)
        {
            return int.MaxValue;
        }

        string[] formatos =
        {
            @"h\:mm",
            @"hh\:mm",
            @"h\.mm",
            @"hh\.mm"
        };

        if (TimeSpan.TryParseExact(
                valor,
                formatos,
                CultureInfo.InvariantCulture,
                out TimeSpan ts))
        {
            return (int)ts.TotalMinutes;
        }

        if (DateTime.TryParse(
                valor,
                CultureInfo.GetCultureInfo("es-ES"),
                DateTimeStyles.NoCurrentDateDefault,
                out DateTime dt))
        {
            return dt.Hour * 60 + dt.Minute;
        }

        return int.MaxValue - 1;
    }


    /// <summary>
    /// A quién pertenece la actividad: (SC, esposa). «Ambos» y los
    /// valores desconocidos cuentan para los dos.
    /// </summary>
    internal static (bool SC, bool Esposa) ClasificarResponsable(
        string valor)
    {
        string v =
            (valor ?? "").Trim();

        if (v.Length == 0
            || v.Equals("Ambos", StringComparison.OrdinalIgnoreCase))
        {
            return (true, true);
        }

        if (v.Equals("SC", StringComparison.OrdinalIgnoreCase))
        {
            return (true, false);
        }

        if (v.Equals("Esposa", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Rebe", StringComparison.OrdinalIgnoreCase))
        {
            return (false, true);
        }

        try
        {
            string nombreSC =
                DatabaseHelper
                    .ObtenerConfiguracion(
                        "NombreSC",
                        "")
                    .Trim();

            if (nombreSC.Length > 0
                && v.Equals(
                    nombreSC,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (true, false);
            }

            string nombreEsposa =
                DatabaseHelper
                    .ObtenerConfiguracion(
                        "NombreEsposaSC",
                        "")
                    .Trim();

            if (nombreEsposa.Length > 0
                && v.Equals(
                    nombreEsposa,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (false, true);
            }
        }
        catch
        {
        }

        // Cualquier otro nombre (p. ej. un acompañante): cuenta para los
        // dos, así nunca se pierde el tiempo del próximo evento.
        return (true, true);
    }


    internal static List<(string Texto, string ConsultaMapa, string Descripcion, string Tiempo)>
        ObtenerProgramaDelDia(
            DateTime dia)
    {
        var resultado =
            new List<(string Texto, string ConsultaMapa, string Descripcion, string Tiempo)>();

        // Destinos del PRÓXIMO evento (del SC y de la esposa) sin tiempo
        // calculado todavía; se calculan en segundo plano.
        var pendientes =
            new List<(double Lat, double Lng, string Direccion)>();

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

            DateTime hoy =
                DateTime.Today;

            bool esHoy =
                dia.Date == hoy;

            int ahora =
                DateTime.Now.Hour * 60
                + DateTime.Now.Minute;

            var filas =
                new List<(
                    string Texto,
                    string ConsultaMapa,
                    string Descripcion,
                    double Lat,
                    double Lng,
                    string Direccion,
                    int Minutos,
                    bool SC,
                    bool Esposa)>();

            foreach (ActividadPrograma actividad
                     in DatabaseHelper
                         .ObtenerActividadesProgramaDia(
                             codigo,
                             mes,
                             diaDb)
                         .OrderBy(a => OrdenHora(a.Hora)))
            {
                string hora =
                    (actividad.Hora ?? "").Trim();

                string tipo =
                    (actividad.TipoActividad ?? "").Trim();

                string responsableCrudo =
                    (actividad.Responsable ?? "").Trim();

                string responsable =
                    responsableCrudo;

                bool esLegacy =
                    responsableCrudo.Equals(
                        "Comida",
                        StringComparison.OrdinalIgnoreCase)
                    || responsableCrudo.Equals(
                        "Cenar",
                        StringComparison.OrdinalIgnoreCase)
                    || responsableCrudo.Equals(
                        "Dormir",
                        StringComparison.OrdinalIgnoreCase);

                if (esLegacy)
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

                if (linea.Trim().Length == 0)
                {
                    continue;
                }

                string consulta =
                    "";

                if (actividad.Latitud != 0
                    || actividad.Longitud != 0)
                {
                    consulta =
                        actividad.Latitud.ToString(CultureInfo.InvariantCulture)
                        + ","
                        + actividad.Longitud.ToString(CultureInfo.InvariantCulture);
                }
                else if (!string.IsNullOrWhiteSpace(
                             actividad.Direccion))
                {
                    consulta =
                        actividad.Direccion.Trim();
                }

                int minutos =
                    OrdenHora(hora);

                if (minutos >= int.MaxValue - 1)
                {
                    minutos = -1;
                }

                // Las actividades van desapareciendo conforme pasa su hora
                // (con media hora de margen), solo al mostrar el día de hoy.
                if (esHoy
                    && minutos >= 0
                    && minutos + MargenActividadMinutos <= ahora)
                {
                    continue;
                }

                (bool paraSC, bool paraEsposa) =
                    ClasificarResponsable(
                        responsableCrudo);

                filas.Add((
                    linea.Trim(),
                    consulta,
                    (actividad.NotasLibro ?? "").Trim(),
                    actividad.Latitud,
                    actividad.Longitud,
                    (actividad.Direccion ?? "").Trim(),
                    minutos,
                    paraSC,
                    paraEsposa));
            }

            // Próximo evento (por hora) del SC y de la esposa: solo esos
            // dos muestran el tiempo en coche.
            int siguienteSC =
                -1;

            int siguienteEsposa =
                -1;

            // El tiempo en coche solo aparece en el primer evento de HOY
            // (el próximo por hora): ni en días siguientes ni en días ya
            // pasados.
            if (esHoy)
            {
                for (int i = 0;
                     i < filas.Count;
                     i++)
                {
                    int minutos =
                        filas[i].Minutos;

                    if (minutos < 0
                        || minutos < ahora)
                    {
                        continue;
                    }

                    if (siguienteSC < 0
                        && filas[i].SC)
                    {
                        siguienteSC = i;
                    }

                    if (siguienteEsposa < 0
                        && filas[i].Esposa)
                    {
                        siguienteEsposa = i;
                    }

                    if (siguienteSC >= 0
                        && siguienteEsposa >= 0)
                    {
                        break;
                    }
                }
            }

            for (int i = 0;
                 i < filas.Count;
                 i++)
            {
                string tiempo =
                    "";

                if (i == siguienteSC
                    || i == siguienteEsposa)
                {
                    tiempo =
                        TiempoViajeService
                            .ObtenerTiempoTextoCacheado(
                                filas[i].Lat,
                                filas[i].Lng,
                                filas[i].Direccion)
                        ?? "";

                    if (tiempo.Length == 0
                        && !string.IsNullOrWhiteSpace(
                            filas[i].ConsultaMapa)
                        && !TiempoViajeService.EnFalloReciente(
                            filas[i].Lat,
                            filas[i].Lng,
                            filas[i].Direccion))
                    {
                        pendientes.Add((
                            filas[i].Lat,
                            filas[i].Lng,
                            filas[i].Direccion));
                    }
                }

                resultado.Add((
                    filas[i].Texto,
                    filas[i].ConsultaMapa,
                    filas[i].Descripcion,
                    tiempo));
            }

            if (pendientes.Count > 0)
            {
                TiempoViajeService.RefrescarEnSegundoPlano(
                    pendientes,
                    ActualizarTodos);
            }
        }
        catch
        {
        }

        return resultado;
    }


    internal static List<(string Firma, string Texto)>
        ObtenerAlertasPendientes()
    {
        var resultado =
            new List<(string Firma, string Texto)>();

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
                    resultado.Add((
                        Services.AlertasMobileService
                            .FirmaDe(alerta),
                        texto));
                }
            }
        }
        catch
        {
        }

        return resultado;
    }


    internal static (
        List<(int Id, string Texto, bool Vencida)> Tareas,
        int Vencidas)
        ObtenerTareasDelDia(
            DateTime dia)
    {
        var seleccion =
            new List<(int Id, string Texto, bool Vencida)>();

        int totalVencidas =
            0;

        try
        {
            DateTime hoy =
                DateTime.Today;

            var pendientes =
                new List<(Tarea Tarea, DateTime Fecha)>();

            foreach (Tarea tarea
                     in DatabaseHelper
                         .ObtenerTareasMobile())
            {
                if (tarea.Completada
                    || !DateTime.TryParse(
                        tarea.FechaInicio,
                        out DateTime fecha))
                {
                    continue;
                }

                pendientes.Add((
                    tarea,
                    fecha));
            }

            // Tareas de días anteriores sin completar (la más reciente
            // primero): van en rojo y delante de las del día.
            var vencidas =
                pendientes
                    .Where(p => p.Fecha.Date < hoy)
                    .OrderByDescending(p => p.Fecha)
                    .ToList();

            totalVencidas =
                vencidas.Count;

            var delDia =
                pendientes
                    .Where(p => p.Fecha.Date == dia.Date)
                    .OrderBy(p => p.Tarea.Hora ?? "")
                    .ToList();

            void Agregar(
                Tarea tarea,
                bool vencida)
            {
                if (seleccion.Any(
                        s => s.Id == tarea.Id))
                {
                    return;
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

                seleccion.Add((
                    tarea.Id,
                    cuando + titulo,
                    vencida));
            }

            foreach (var v in vencidas)
            {
                Agregar(v.Tarea, true);
            }

            foreach (var d in delDia)
            {
                Agregar(
                    d.Tarea,
                    d.Fecha.Date < hoy);
            }
        }
        catch
        {
        }

        return (seleccion, totalVencidas);
    }


    /// <summary>
    /// Título y texto de la predicación en vigor: el mes actual con su año.
    /// </summary>
    internal static (string Titulo, string Texto)
        ObtenerPredicacionWidget()
    {
        try
        {
            string usuario =
                PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            DateTime hoy =
                DateTime.Today;

            string mesHoras =
                hoy.ToString("yyyy-MM");

            double horasMes =
                DatabaseHelper
                    .ObtenerHorasMesMobile(
                        usuario,
                        mesHoras);

            int cbsMes =
                DatabaseHelper
                    .ObtenerCbsMesMobile(
                        usuario,
                        mesHoras);

            string mesNombre =
                hoy.ToString(
                    "MMMM",
                    new CultureInfo("es-ES"));

            string titulo =
                "PREDICACIÓN · "
                + mesNombre.ToUpperInvariant()
                + " " + hoy.Year;

            string texto =
                horasMes.ToString(
                    "0.#",
                    new CultureInfo("es-ES"))
                + " h  ·  "
                + cbsMes.ToString()
                + " CBs";

            return (titulo, texto);
        }
        catch
        {
            return ("PREDICACIÓN", "0 h  ·  0 CBs");
        }
    }
}
