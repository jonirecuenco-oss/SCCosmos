using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Notificaciones del sistema para las alertas pendientes.
///
/// Se publica una notificación por alerta (los lanzadores cuentan las
/// notificaciones de la app y las usan como contador en el icono; además se
/// fija el número con SetNumber, que es lo que muestran Samsung One UI y
/// otros) y todas abren SCCosmos.
/// </summary>
public static class AlertasNotificacionAndroid
{
    private const string CanalId =
        "sccosmos_alertas";

    private const string CanalNombre =
        "Alertas de SCCosmos";

    private const int IdBase =
        4300;

    private const int Maximo =
        20;


    public static void Actualizar(
        int pendientes,
        IList<string> titulos)
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
                gestor.Notify(
                    IdBase + i,
                    CrearNotificacion(
                        contexto,
                        string.IsNullOrWhiteSpace(
                            titulos[i])
                            ? $"Alerta {i + 1}"
                            : titulos[i],
                        pendientes));
            }
        }
        catch
        {
            // Sin permiso o sin servicio de notificaciones: no pasa nada.
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


    private static Notification CrearNotificacion(
        Context contexto,
        string texto,
        int pendientes)
    {
        Intent? intencion =
            contexto.PackageManager
                ?.GetLaunchIntentForPackage(
                    contexto.PackageName ?? "");

        PendingIntent? pendiente =
            intencion is null
                ? null
                : PendingIntent.GetActivity(
                    contexto,
                    0,
                    intencion,
                    PendingIntentFlags.Immutable
                    | PendingIntentFlags.UpdateCurrent);

        var constructor =
            new NotificationCompat.Builder(
                    contexto,
                    CanalId)
                .SetSmallIcon(
                    Resource.Mipmap.appicon)
                .SetContentTitle(
                    "SCCosmos")
                .SetContentText(
                    texto)
                .SetStyle(
                    new NotificationCompat
                        .BigTextStyle()
                        .BigText(texto))
                .SetNumber(
                    pendientes)
                .SetSubText(
                    pendientes == 1
                        ? "1 alerta pendiente"
                        : $"{pendientes} alertas pendientes")
                .SetAutoCancel(
                    true)
                .SetPriority(
                    (int)NotificationPriority.Default);

        if (pendiente is not null)
        {
            constructor.SetContentIntent(
                pendiente);
        }

        return constructor.Build();
    }
}
