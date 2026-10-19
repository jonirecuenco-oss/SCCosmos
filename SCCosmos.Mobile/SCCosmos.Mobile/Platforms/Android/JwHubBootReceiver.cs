using Android.App;
using Android.Content;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

[BroadcastReceiver(
    Enabled = true,
    Exported = true)]
[IntentFilter(
    new[]
    {
        Intent.ActionBootCompleted,
        Intent.ActionMyPackageReplaced
    })]
public class JwHubBootReceiver :
    BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        if (JwHubAutoSettings.Activo)
        {
            JwHubAutoScheduler
                .ProgramarSiguiente();
        }

        // Las alarmas se borran al reiniciar: se vuelven a programar la
        // revisión diaria de alertas y el próximo aviso de tareas.
        try
        {
            AlertasAlarmScheduler
                .ProgramarSiguiente();
        }
        catch
        {
        }

        try
        {
            TareasAvisoAlarmScheduler
                .ProgramarSiguiente();
        }
        catch
        {
        }
    }
}