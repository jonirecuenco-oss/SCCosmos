using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace SCCosmos
{
    /// <summary>
    /// Medidor de rendimiento ligero para Windows: escribe en
    /// %TEMP%\sccosmos_perf.log el tiempo de cada carga y los bloqueos de
    /// la interfaz que detecta el vigilante («Bloqueo.UI»).
    /// </summary>
    public static class Perf
    {
        private static readonly string Ruta =
            Path.Combine(
                Path.GetTempPath(),
                "sccosmos_perf.log");

        // El registro empieza de nuevo al pasar de ~2 MB.
        private const long TamanoMaximo = 2_000_000;

        private static readonly object _candado = new();

        /// <summary>
        /// Pantalla activa; se anota junto a cada bloqueo de la interfaz.
        /// </summary>
        public static string Contexto { get; set; } = "Arranque";

        public static void Registrar(
            string nombre,
            long milisegundos)
        {
            try
            {
                // El vigilante escribe desde otro hilo: se serializa.
                lock (_candado)
                {
                    var info = new FileInfo(Ruta);

                    if (info.Exists
                        && info.Length > TamanoMaximo)
                    {
                        info.Delete();
                    }

                    File.AppendAllText(
                        Ruta,
                        $"{DateTime.Now:HH:mm:ss.fff}\t{nombre}\t{milisegundos} ms{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        public static void Limpiar()
        {
            try
            {
                lock (_candado)
                {
                    if (File.Exists(Ruta))
                    {
                        File.Delete(Ruta);
                    }
                }
            }
            catch
            {
            }
        }

        // =========================================================
        // VIGILANTE DEL HILO DE LA INTERFAZ
        // =========================================================

        private const int IntervaloVigilanteMs = 100;

        private const int UmbralBloqueoMs = 200;

        private static Thread? _vigilante;

        /// <summary>
        /// Cada 100 ms comprueba que la interfaz atiende un aviso con la
        /// misma prioridad que el teclado y el ratón. Si tarda más de
        /// 200 ms, anota «Bloqueo.UI» con la pantalla activa y la duración
        /// total del bloqueo. Solo mide: no cambia el funcionamiento.
        /// </summary>
        public static void IniciarVigilanteUI(
            Dispatcher dispatcher)
        {
            if (_vigilante is not null)
            {
                return;
            }

            _vigilante =
                new Thread(() => Vigilar(dispatcher))
                {
                    IsBackground = true,
                    Name = "SCCosmos.VigilanteUI",
                    Priority = ThreadPriority.AboveNormal
                };

            _vigilante.Start();
        }

        private static void Vigilar(
            Dispatcher dispatcher)
        {
            var atendido =
                new ManualResetEventSlim(false);

            while (!dispatcher.HasShutdownStarted)
            {
                atendido.Reset();

                long inicio =
                    Stopwatch.GetTimestamp();

                try
                {
                    dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(atendido.Set));
                }
                catch
                {
                    return;
                }

                if (!atendido.Wait(UmbralBloqueoMs))
                {
                    // Interfaz bloqueada: se espera a que responda para
                    // anotar la duración completa.
                    atendido.Wait();

                    Registrar(
                        "Bloqueo.UI " + Contexto,
                        (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
                }

                Thread.Sleep(IntervaloVigilanteMs);
            }
        }
    }
}
