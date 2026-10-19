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
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            ProcesarDestinoNotificacion(Intent);
        }


        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);

            ProcesarDestinoNotificacion(intent);
        }


        /// <summary>
        /// Si la app se abrió desde una notificación (Tareas, Personas...),
        /// se navega a esa página en cuanto el Shell esté listo.
        /// </summary>
        private static void ProcesarDestinoNotificacion(Intent? intent)
        {
            try
            {
                string? destino =
                    intent?.GetStringExtra(
                        "sccosmos_destino");

                if (!string.IsNullOrWhiteSpace(destino))
                {
                    NavegacionNotificacion.Solicitar(
                        destino);
                }
            }
            catch
            {
            }
        }


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
