using System.Configuration;
using System.Data;
using System.Windows;

namespace SCCosmos
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(
            StartupEventArgs e)
        {
            // Cada arranque empieza con el registro de tiempos limpio.
            Perf.Limpiar();

            // Errores no capturados: se anotan y, si son de la interfaz, la
            // aplicación sigue funcionando en lugar de cerrarse.
            RegistroErrores.Iniciar(this);

            // Mediciones del canal por carpeta, en el registro de Perf.
            global::SCCosmos.Sincro.CanalArchivosSincro.Medicion =
                Perf.Registrar;

            // Errores del motor de sincronización que antes se perdían.
            global::SCCosmos.Sincro.SincroDiagnostico.Anotar =
                RegistroErrores.Anotar;

            base.OnStartup(e);
        }
    }

}
