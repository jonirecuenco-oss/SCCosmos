using Android.App;
using Android.Runtime;

namespace SCCosmos.Mobile
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
            // Errores no capturados: se anotan y, si son del hilo
            // principal, la app sigue funcionando en lugar de cerrarse.
            global::SCCosmos.Mobile.Services.RegistroErrores.Iniciar();

            // Mediciones del canal por carpeta, en el registro de Perf.
            global::SCCosmos.Sincro.CanalArchivosSincro.Medicion =
                global::SCCosmos.Mobile.Services.Perf.Registrar;
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
