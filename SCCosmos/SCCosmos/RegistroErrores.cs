using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SCCosmos
{
    /// <summary>
    /// Manejador global de errores: cualquier excepción no capturada queda
    /// anotada, con su pila, en %LOCALAPPDATA%\SCCosmos\errores.log. Los
    /// errores de la interfaz ya no cierran la aplicación: se avisa y se
    /// sigue trabajando.
    /// </summary>
    public static class RegistroErrores
    {
        // Con una base de pruebas, junto a ella: no se mezcla con el de la app.
        private static string Ruta =>
            DatabaseHelper.RutaParaPruebas is string pruebas
                ? Path.Combine(Path.GetDirectoryName(pruebas) ?? "", "errores.log")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos",
                    "errores.log");

        // El archivo empieza de nuevo al pasar de ~1 MB.
        private const long TamanoMaximo = 1_000_000;

        private static readonly object _candado = new();

        private static DateTime _ultimoAviso = DateTime.MinValue;

        public static void Iniciar(
            Application aplicacion)
        {
            aplicacion.DispatcherUnhandledException += (s, e) =>
            {
                Anotar(e.Exception, "Interfaz");

                e.Handled = true;

                Avisar();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Anotar(
                    e.ExceptionObject as Exception,
                    e.IsTerminating ? "Fatal" : "Hilo");

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Anotar(e.Exception, "Tarea");

                e.SetObserved();
            };
        }

        public static void Anotar(
            Exception? ex,
            string origen)
        {
            if (ex is null)
            {
                return;
            }

            try
            {
                lock (_candado)
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(Ruta)!);

                    var info = new FileInfo(Ruta);

                    if (info.Exists
                        && info.Length > TamanoMaximo)
                    {
                        info.Delete();
                    }

                    File.AppendAllText(
                        Ruta,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{origen}] {ex}{Environment.NewLine}{Environment.NewLine}");
                }

                Perf.Registrar(
                    $"Error.{origen} {ex.GetType().Name}: {ex.Message}",
                    0);
            }
            catch
            {
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _ultimosPorSitio = new();

        /// <summary>
        /// Para los catch que antes se tragaban el error en silencio: lo anota
        /// sin avisar al usuario (el funcionamiento no cambia) y, como mucho,
        /// una vez cada 10 minutos por sitio, por si el fallo se repite.
        /// </summary>
        public static void AnotarSilencioso(
            Exception? ex,
            string sitio)
        {
            if (ex is null
                || ex is OperationCanceledException)
            {
                return;
            }

            DateTime ahora = DateTime.UtcNow;

            if (_ultimosPorSitio.TryGetValue(sitio, out DateTime ultimo)
                && ahora - ultimo < TimeSpan.FromMinutes(10))
            {
                return;
            }

            _ultimosPorSitio[sitio] = ahora;

            Anotar(ex, sitio);
        }

        private static void Avisar()
        {
            // Como mucho un aviso cada 30 s (un error repetido no debe
            // llenar la pantalla de ventanas). En las mediciones
            // automáticas no se muestra.
            if ((DateTime.UtcNow - _ultimoAviso).TotalSeconds < 30
                || Environment.GetEnvironmentVariable("SCCOSMOS_PERF") == "1")
            {
                return;
            }

            _ultimoAviso = DateTime.UtcNow;

            try
            {
                MessageBox.Show(
                    "Se ha producido un error inesperado, pero la aplicación sigue funcionando.\n\nQueda anotado en:\n" + Ruta,
                    "SCCosmos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
            }
        }
    }
}
