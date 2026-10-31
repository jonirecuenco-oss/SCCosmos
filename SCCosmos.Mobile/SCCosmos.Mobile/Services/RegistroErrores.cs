namespace SCCosmos.Mobile.Services;

/// <summary>
/// Manejador global de errores: cualquier excepción no capturada queda
/// anotada, con su pila, en sccosmos_errores.log (carpeta externa de la app;
/// se recupera con adb pull) y en logcat con la etiqueta «SCCosmosError».
/// Los errores del hilo principal ya no cierran la app: se avisa con un
/// mensaje breve y se sigue trabajando.
/// </summary>
public static class RegistroErrores
{
    // El archivo empieza de nuevo al pasar de ~1 MB.
    private const long TamanoMaximo = 1_000_000;

    private static readonly object _candado = new();

    private static DateTime _ultimoAviso = DateTime.MinValue;

    private static bool _iniciado;

    public static void Iniciar()
    {
        if (_iniciado)
        {
            return;
        }

        _iniciado = true;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Anotar(
                e.ExceptionObject as Exception,
                e.IsTerminating ? "Fatal" : "Hilo");

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Anotar(e.Exception, "Tarea");

            e.SetObserved();
        };

        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            Anotar(e.Exception, "Interfaz");

            e.Handled = true;

            Avisar();
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
            global::Android.Util.Log.Error(
                "SCCosmosError",
                $"[{origen}] {ex}");
        }
        catch
        {
        }

        try
        {
            lock (_candado)
            {
                string carpeta =
                    global::Android.App.Application.Context
                        .GetExternalFilesDir(null)?.AbsolutePath
                    ?? FileSystem.CacheDirectory;

                string ruta =
                    Path.Combine(
                        carpeta,
                        "sccosmos_errores.log");

                var info = new FileInfo(ruta);

                if (info.Exists
                    && info.Length > TamanoMaximo)
                {
                    info.Delete();
                }

                File.AppendAllText(
                    ruta,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{origen}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static void Avisar()
    {
        // Como mucho un aviso cada 30 s.
        if ((DateTime.UtcNow - _ultimoAviso).TotalSeconds < 30)
        {
            return;
        }

        _ultimoAviso = DateTime.UtcNow;

        try
        {
            global::Android.Widget.Toast
                .MakeText(
                    global::Android.App.Application.Context,
                    "Se ha producido un error inesperado; queda anotado.",
                    global::Android.Widget.ToastLength.Long)?
                .Show();
        }
        catch
        {
        }
    }
}
