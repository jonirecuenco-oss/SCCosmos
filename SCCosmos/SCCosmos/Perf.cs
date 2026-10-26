using System;
using System.IO;

namespace SCCosmos
{
    /// <summary>
    /// Medidor de rendimiento ligero para Windows: escribe en
    /// %TEMP%\sccosmos_perf.log el tiempo de cada carga.
    /// </summary>
    public static class Perf
    {
        private static readonly string Ruta =
            Path.Combine(
                Path.GetTempPath(),
                "sccosmos_perf.log");

        public static void Registrar(
            string nombre,
            long milisegundos)
        {
            try
            {
                File.AppendAllText(
                    Ruta,
                    $"{DateTime.Now:HH:mm:ss.fff}\t{nombre}\t{milisegundos} ms{Environment.NewLine}");
            }
            catch
            {
            }
        }

        public static void Limpiar()
        {
            try
            {
                if (File.Exists(Ruta))
                {
                    File.Delete(Ruta);
                }
            }
            catch
            {
            }
        }
    }
}
