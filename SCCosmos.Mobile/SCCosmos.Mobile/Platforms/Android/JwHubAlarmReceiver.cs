using Android.Content;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Platforms.Android;

[BroadcastReceiver(
    Enabled = true,
    Exported = false)]
public class JwHubAlarmReceiver :
    BroadcastReceiver
{
    public override void OnReceive(
        Context? context,
        Intent? intent)
    {
        // Programamos mañana antes de iniciar
        // el trabajo de hoy.
        JwHubAutoScheduler
            .ProgramarSiguiente();

        JwHubSyncController
            .IniciarAutomatico();
    }
}