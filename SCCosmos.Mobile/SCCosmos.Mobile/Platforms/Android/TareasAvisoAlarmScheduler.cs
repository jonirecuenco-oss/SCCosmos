using Android.App;
using Android.Content;
using Android.OS;

using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

/// <summary>
/// Alarma para el siguiente aviso de tareas: se programa en el momento
/// exacto del aviso (empezar el día para las tareas de todo el día, la hora
/// de la tarea o la antelación del aviso previo). Al sonar se publican los
/// avisos y se programa la siguiente.
/// </summary>
public static class TareasAvisoAlarmScheduler
{
    private const int IdAlarma =
        4402;


    public static void ProgramarSiguiente()
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

            var (_, proximo) =
                TareasAvisoService.CalcularAvisos(
                    DateTime.Now);

            // Si no hay ningún aviso a la vista, se revisa igualmente al
            // empezar el día siguiente (por si acaso).
            DateTime cuando =
                proximo
                ?? DateTime.Today.AddDays(1).AddMinutes(1);

            long momento =
                new DateTimeOffset(
                    cuando)
                    .ToUnixTimeMilliseconds();

            PendingIntent intencion =
                ObtenerIntencion(
                    contexto);

            bool exacta =
                Build.VERSION.SdkInt
                    < BuildVersionCodes.S
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


    private static PendingIntent ObtenerIntencion(
        Context contexto)
    {
        var intencion =
            new Intent(
                contexto,
                typeof(TareasAvisoAlarmReceiver));

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
public class TareasAvisoAlarmReceiver :
    BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        // Publica los avisos que toquen (y reprograma la siguiente).
        _ = TareasAvisoService
            .VerificarAsync();
    }
}
