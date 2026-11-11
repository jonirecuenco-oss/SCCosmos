using Android.App;
using Android.Content;
using Android.OS;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Alarma de los recordatorios con hora de la página Avisos (quedar con el
/// CCA, el S-303 del domingo): al sonar publica lo que toque y programa el
/// siguiente. Si no hay ninguno a la vista, se vuelve a mirar al día
/// siguiente (por si cambia el calendario); si están apagados, nada.
/// </summary>
public static class RecordatoriosVisitaScheduler
{
    private const int IdAlarma =
        4404;


    public static void Revisar()
    {
        try
        {
            Context contexto =
                global::Android.App.Application.Context;

            var gestor =
                (AlarmManager?)contexto.GetSystemService(
                    Context.AlarmService);

            if (gestor is null)
            {
                return;
            }

            PendingIntent intencion =
                ObtenerIntencion(contexto);

            if (!RecordatoriosVisitaService.HayActivos())
            {
                gestor.Cancel(intencion);
                return;
            }

            DateTime? siguiente =
                RecordatoriosVisitaService.Verificar(
                    DateTime.Now,
                    r => AlertasNotificacionAndroid.PublicarRecordatorio(r.Clave, r.Titulo, r.Texto));

            DateTime cuando =
                siguiente
                ?? DateTime.Today.AddDays(1).AddMinutes(5);

            long momento =
                new DateTimeOffset(cuando).ToUnixTimeMilliseconds();

            bool exacta =
                Build.VERSION.SdkInt < BuildVersionCodes.S
                || gestor.CanScheduleExactAlarms();

            if (exacta)
            {
                gestor.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, momento, intencion);
            }
            else
            {
                gestor.SetAndAllowWhileIdle(AlarmType.RtcWakeup, momento, intencion);
            }
        }
        catch
        {
        }
    }


    private static PendingIntent ObtenerIntencion(
        Context contexto)
    {
        var intencion =
            new Intent(
                contexto,
                typeof(RecordatoriosVisitaReceiver));

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
public class RecordatoriosVisitaReceiver :
    BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent) =>
        RecordatoriosVisitaScheduler.Revisar();
}
