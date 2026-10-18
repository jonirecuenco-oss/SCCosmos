using Android.App;
using Android.Content;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Revisión diaria de alertas: al sonar la alarma se recalcula el panel de
/// alertas y se actualiza la notificación (y el contador del icono).
/// </summary>
public static class AlertasAlarmScheduler
{
    private const int IdAlarma =
        4401;

    /// <summary>Hora del día en la que se revisan las alertas.</summary>
    public static TimeSpan HoraRevision
    {
        get
        {
            try
            {
                string texto =
                    Microsoft.Maui.Storage
                        .Preferences.Default.Get(
                            "SCCosmos_HoraAlertas",
                            "08:00");

                return TimeSpan.TryParse(
                           texto,
                           out TimeSpan hora)
                    ? hora
                    : new TimeSpan(8, 0, 0);
            }
            catch
            {
                return new TimeSpan(8, 0, 0);
            }
        }
    }


    public static void ProgramarSiguiente()
    {
        var contexto =
            global::Android.App.Application.Context;

        var gestor =
            (AlarmManager?)contexto.GetSystemService(
                Context.AlarmService);

        if (gestor is null)
        {
            return;
        }

        DateTime ahora =
            DateTime.Now;

        DateTime siguiente =
            ahora.Date.Add(
                HoraRevision);

        if (siguiente <= ahora)
        {
            siguiente =
                siguiente.AddDays(1);
        }

        long momento =
            new DateTimeOffset(
                siguiente)
            .ToUnixTimeMilliseconds();

        // Inexacta a propósito: no requiere permiso de alarmas exactas y el
        // sistema la agrupa con otras para no gastar batería.
        gestor.SetInexactRepeating(
            AlarmType.RtcWakeup,
            momento,
            AlarmManager.IntervalDay,
            ObtenerIntencion(contexto));
    }


    public static void Cancelar()
    {
        var contexto =
            global::Android.App.Application.Context;

        var gestor =
            (AlarmManager?)contexto.GetSystemService(
                Context.AlarmService);

        gestor?.Cancel(
            ObtenerIntencion(contexto));
    }


    private static PendingIntent ObtenerIntencion(
        Context contexto)
    {
        var intencion =
            new Intent(
                contexto,
                typeof(AlertasAlarmReceiver));

        return PendingIntent.GetBroadcast(
                   contexto,
                   IdAlarma,
                   intencion,
                   PendingIntentFlags.Immutable
                   | PendingIntentFlags.UpdateCurrent)
               ?? PendingIntent.GetBroadcast(
                   contexto,
                   IdAlarma,
                   intencion,
                   PendingIntentFlags.UpdateCurrent)!;
    }
}


[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public class AlertasAlarmReceiver :
    BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        // Reprogramamos la de mañana y revisamos las alertas de hoy.
        AlertasAlarmScheduler
            .ProgramarSiguiente();

        _ = AlertasNotificacionService
            .ActualizarAsync();
    }
}
