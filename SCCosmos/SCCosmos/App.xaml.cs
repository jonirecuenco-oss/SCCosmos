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

            base.OnStartup(e);
        }
    }

}
