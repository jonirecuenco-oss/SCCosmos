using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnStop()
        {
            // Al cerrar o mandar la app a segundo plano, se vacían los
            // cambios pendientes antes de que el sistema la suspenda.
            try
            {
                SincronizacionMobile
                    .VaciarPendientesSiHaceFaltaAsync()
                    .Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
            }

            base.OnStop();
        }

        protected override void OnActivityResult(
            int requestCode,
            Result resultCode,
            Intent? data)
        {
            base.OnActivityResult(
                requestCode,
                resultCode,
                data);

            SyncFolderService.ProcesarResultadoSeleccion(
                requestCode,
                resultCode,
                data);
        }
    }
}
