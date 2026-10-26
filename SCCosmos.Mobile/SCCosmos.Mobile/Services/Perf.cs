using System.Diagnostics;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Medidor de rendimiento muy ligero: escribe en logcat con la etiqueta
/// «SCCosmosPerf» el tiempo que tarda cada carga.
/// Se puede ver con:
///   adb logcat -s SCCosmosPerf
/// </summary>
public static class Perf
{
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
