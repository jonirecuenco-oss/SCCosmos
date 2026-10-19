using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Notificaciones del sistema para las alertas pendientes y los avisos de
/// tareas.
///
/// Se publica una notificación por alerta (los lanzadores cuentan las
/// notificaciones de la app y las usan como contador en el icono; además se
/// fija el número con SetNumber, que es lo que muestran Samsung One UI y
/// otros) y todas abren SCCosmos en la página que corresponda:
///
///   · Alertas de tareas y avisos de tareas → página de Tareas.
///   · Revisión de JW Hub pendiente        → página de Personas.
///   · El resto                            → Inicio.
/// </summary>
public static class AlertasNotificacionAndroid
{
    private const string CanalId =
        "sccosmos_alertas";

    private const string CanalNombre =
        "Alertas de SCCosmos";

    private const string CanalTareasId =
        "sccosmos_tareas";

    private const string CanalTareasNombre =
        "Tareas de SCCosmos";

    private const int IdBase =
        4300;

    private const int Maximo =
        20;

    private const int IdTareasBase =
        4600;

    private const int MaxTareas =
        500;

    private const string ExtraDestino =
        "sccosmos_destino";


    public static void Actualizar(
        int pendientes,
        IList<string> titulos,
        IList<string>? destinos = null)
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            NotificationManagerCompat gestor =
                NotificationManagerCompat.From(
                    contexto);

            CrearCanal(
                contexto,
                gestor);

            // Fuera las notificaciones anteriores de alertas.
            for (int i = 0;
                 i < Maximo;
                 i++)
            {
                gestor.Cancel(
                    IdBase + i);
            }

            if (pendientes <= 0)
            {
                return;
            }

            int total =
                Math.Min(
                    titulos.Count,
                    Maximo);

            for (int i = 0;
                 i < total;
                 i++)
            {
                string destino =
                    destinos != null
                    && i < destinos.Count
                        ? destinos[i] ?? ""
                        : "";

                gestor.Notify(
                    IdBase + i,
                    CrearNotificacion(
                        contexto,
                        string.IsNullOrWhiteSpace(
                            titulos[i])
                            ? $"Alerta {i + 1}"
                            : titulos[i],
                        pendientes,
                        destino));
            }
        }
        catch
        {
            // Sin permiso o sin servicio de notificaciones: no pasa nada.
        }
    }


    /// <summary>
    /// Publica (o reemplaza) la notificación de un aviso de tarea. Al
    /// pulsarla se abre la página de Tareas.
    /// </summary>
    public static void PublicarAvisoTarea(
        string clave,
        string titulo,
        string texto)
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            NotificationManagerCompat gestor =
                NotificationManagerCompat.From(
                    contexto);

            CrearCanalTareas(
                contexto,
                gestor);

            int id =
                IdTareasBase
                + Math.Abs(
                    (clave ?? "").GetHashCode())
                % MaxTareas;

            gestor.Notify(
                id,
                CrearNotificacion(
                    contexto,
                    texto,
                    -1,
                    "tareas",
                    CanalTareasId,
                    titulo));
        }
        catch
        {
        }
    }


    private static void CrearCanal(
        Context contexto,
        NotificationManagerCompat gestor)
    {
        if (global::Android.OS.Build.VERSION.SdkInt
            < global::Android.OS.BuildVersionCodes.O)
        {
            return;
        }

        if (gestor.GetNotificationChannel(
                CanalId)
            is not null)
        {
            return;
        }

        var canal =
            new NotificationChannel(
                CanalId,
                CanalNombre,
                NotificationImportance.Default)
            {
                Description =
                    "Avisos de visitas, envíos pendientes y " +
                    "revisión de JW Hub."
            };

        gestor.CreateNotificationChannel(
            canal);
    }


    private static void CrearCanalTareas(
        Context contexto,
        NotificationManagerCompat gestor)
    {
        if (global::Android.OS.Build.VERSION.SdkInt
            < global::Android.OS.BuildVersionCodes.O)
        {
            return;
        }

        if (gestor.GetNotificationChannel(
                CanalTareasId)
            is not null)
        {
            return;
        }

        var canal =
            new NotificationChannel(
                CanalTareasId,
                CanalTareasNombre,
                NotificationImportance.High)
            {
                Description =
                    "Avisos de tareas: al empezar el día, a su " +
                    "hora y avisos previos."
            };

        gestor.CreateNotificationChannel(
            canal);
    }


    private static Notification CrearNotificacion(
        Context contexto,
        string texto,
        int pendientes,
        string destino = "",
        string canal = CanalId,
        string titulo = "SCCosmos")
    {
        Intent? intencion =
            contexto.PackageManager
                ?.GetLaunchIntentForPackage(
                    contexto.PackageName ?? "");

        int codigo =
            destino switch
            {
                "tareas" => 4601,
                "personas" => 4602,
                _ => 4600
            };

        if (intencion is not null
            && !string.IsNullOrWhiteSpace(
                destino))
        {
            intencion.PutExtra(
                ExtraDestino,
                destino);
        }

        PendingIntent? pendiente =
            intencion is null
                ? null
                : PendingIntent.GetActivity(
                    contexto,
                    codigo,
                    intencion,
                    PendingIntentFlags.Immutable
                    | PendingIntentFlags.UpdateCurrent);

        var constructor =
            new NotificationCompat.Builder(
                    contexto,
                    canal)
                .SetSmallIcon(
                    Resource.Mipmap.appicon)
                .SetContentTitle(
                    titulo)
                .SetContentText(
                    texto)
                .SetStyle(
                    new NotificationCompat
                        .BigTextStyle()
                        .BigText(texto))
                .SetAutoCancel(
                    true)
                .SetPriority(
                    canal == CanalTareasId
                        ? (int)NotificationPriority.High
                        : (int)NotificationPriority.Default);

        if (pendientes >= 0)
        {
            constructor
                .SetNumber(
                    pendientes)
                .SetSubText(
                    pendientes == 1
                        ? "1 alerta pendiente"
                        : $"{pendientes} alertas pendientes");
        }

        if (pendiente is not null)
        {
            constructor.SetContentIntent(
                pendiente);
        }

        return constructor.Build();
    }
}
