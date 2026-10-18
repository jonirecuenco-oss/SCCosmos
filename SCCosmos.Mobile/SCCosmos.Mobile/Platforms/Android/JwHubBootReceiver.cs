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
    }
}