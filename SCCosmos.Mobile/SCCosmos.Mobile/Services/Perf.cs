using System.Diagnostics;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Medidor de rendimiento muy ligero: escribe en logcat con la etiqueta
/// «SCCosmosPerf» el tiempo que tarda cada carga, y lo guarda también en
/// sccosmos_perf.log (carpeta externa de la app), para poder recuperarlo
/// por USB en cualquier móvil.
/// Se puede ver con:
///   adb logcat -s SCCosmosPerf
///   adb pull /sdcard/Android/data/com.companyname.sccosmos.mobile/files/sccosmos_perf.log
/// Incluye un vigilante que anota «Bloqueo.UI» cuando el hilo principal
/// tarda más de 200 ms en responder.
/// </summary>
public static class Perf
{
    // El archivo empieza de nuevo al pasar de ~1 MB.
    private const long TamanoMaximo = 1_000_000;

    private static readonly object _candado = new();

    private static string? _ruta;

    public static IDisposable Medir(string nombre)
    {
        return new Medidor(nombre);
    }

    public static void Registrar(string nombre, long milisegundos)
    {
        try
        {
            global::Android.Util.Log.Info(
                "SCCosmosPerf",
                $"{nombre}: {milisegundos} ms");
        }
        catch
        {
        }

        EscribirArchivo(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t{nombre}\t{milisegundos} ms");
    }

    private static void EscribirArchivo(string linea)
    {
        try
        {
            lock (_candado)
            {
                _ruta ??= Path.Combine(
                    global::Android.App.Application.Context
                        .GetExternalFilesDir(null)?.AbsolutePath
                    ?? FileSystem.CacheDirectory,
                    "sccosmos_perf.log");

                var info = new FileInfo(_ruta);

                if (info.Exists
                    && info.Length > TamanoMaximo)
                {
                    info.Delete();
                }

                File.AppendAllText(
                    _ruta,
                    linea + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    // =========================================================
    // VIGILANTE DEL HILO PRINCIPAL
    // =========================================================

    private const int IntervaloVigilanteMs = 100;

    private const int UmbralBloqueoMs = 200;

    private static Thread? _vigilante;

    private static volatile bool _enPrimerPlano = true;

    private static string _pantalla = "Arranque";

    /// <summary>
    /// Cada 100 ms comprueba que el hilo principal atiende un aviso. Si
    /// tarda más de 200 ms, anota «Bloqueo.UI» con la pantalla y la
    /// duración total del bloqueo. Solo mide: no cambia el funcionamiento.
    /// </summary>
    public static void IniciarVigilanteUI()
    {
        if (_vigilante is not null)
        {
            return;
        }

        _vigilante =
            new Thread(Vigilar)
            {
                IsBackground = true,
                Name = "SCCosmos.VigilanteUI",
                Priority = ThreadPriority.AboveNormal
            };

        _vigilante.Start();
    }

    /// <summary>
    /// En segundo plano el vigilante descansa (no gasta batería).
    /// </summary>
    public static void EstablecerPrimerPlano(bool enPrimerPlano)
    {
        _enPrimerPlano = enPrimerPlano;
    }

    private static void Vigilar()
    {
        var atendido =
            new ManualResetEventSlim(false);

        while (true)
        {
            if (!_enPrimerPlano)
            {
                Thread.Sleep(1000);
                continue;
            }

            atendido.Reset();

            long inicio =
                Stopwatch.GetTimestamp();

            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // La pantalla solo se consulta si el aviso llegó tarde.
                    if (Stopwatch.GetElapsedTime(inicio).TotalMilliseconds
                        > UmbralBloqueoMs)
                    {
                        try
                        {
                            _pantalla =
                                Shell.Current?.CurrentPage?.GetType().Name
                                ?? "";
                        }
                        catch
                        {
                        }
                    }

                    atendido.Set();
                });
            }
            catch
            {
                Thread.Sleep(1000);
                continue;
            }

            if (!atendido.Wait(UmbralBloqueoMs))
            {
                // Hilo principal bloqueado: se espera a que responda para
                // anotar la duración completa.
                atendido.Wait();

                Registrar(
                    "Bloqueo.UI " + _pantalla,
                    (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            }

            Thread.Sleep(IntervaloVigilanteMs);
        }
    }

    private sealed class Medidor : IDisposable
    {
        private readonly string _nombre;

        private readonly Stopwatch _reloj;

        public Medidor(string nombre)
        {
            _nombre = nombre;
            _reloj = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _reloj.Stop();
            Registrar(_nombre, _reloj.ElapsedMilliseconds);
        }
    }
}
