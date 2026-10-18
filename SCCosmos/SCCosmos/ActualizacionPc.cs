using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Actualización de la app en Windows: comprueba la versión publicada,
    /// descarga el ZIP y se reemplaza sola (un pequeño actualizador espera
    /// a que la app se cierre, copia los archivos y la vuelve a abrir).
    /// </summary>
    public static class ActualizacionPc
    {
        private static string Carpeta =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SCCosmos");


        private static string RutaAjustes =>
            Path.Combine(
                Carpeta,
                "actualizaciones.json");


        /// <summary>Versión instalada (para la pantalla).</summary>
        public static string VersionInstalada =>
            ActualizadorSccosmos.VersionApp;


        /// <summary>Actualizar automáticamente (por equipo).</summary>
        public static bool AutoActiva
        {
            get
            {
                try
                {
                    if (!File.Exists(RutaAjustes))
                    {
                        return false;
                    }

                    using var documento =
                        JsonDocument.Parse(
                            File.ReadAllText(RutaAjustes));

                    return documento.RootElement
                        .TryGetProperty(
                            "Auto",
                            out JsonElement valor)
                        && valor.GetBoolean();
                }
                catch
                {
                    return false;
                }
            }

            set
            {
                try
                {
                    Directory.CreateDirectory(
                        Carpeta);

                    File.WriteAllText(
                        RutaAjustes,
                        JsonSerializer.Serialize(
                            new { Auto = value }));
                }
                catch
                {
                }
            }
        }


        /// <summary>Comprueba si hay una versión nueva publicada.</summary>
        public static Task<ResultadoComprobacion> ComprobarAsync()
        {
            return ActualizadorSccosmos
                .ComprobarAsync();
        }


        /// <summary>Hay una versión nueva disponible para Windows.</summary>
        public static bool HayNueva(
            ResultadoComprobacion resultado)
        {
            return resultado != null
                && resultado.Correcto
                && resultado.Version != null
                && !string.IsNullOrWhiteSpace(
                    resultado.Version.UrlWindows)
                && ActualizadorSccosmos.EsMasNueva(
                    resultado.Version.Numero,
                    ActualizadorSccosmos.VersionApp);
        }


        /// <summary>Hay una versión preparada para aplicarse al cerrar.</summary>
        public static bool HayPreparada =>
            !string.IsNullOrEmpty(_scriptPreparado)
            && File.Exists(_scriptPreparado);


        private static string _scriptPreparado = "";


        /// <summary>
        /// Descarga la versión nueva y deja preparado el reemplazo.
        /// </summary>
        public static async Task<bool> PrepararAsync(
            VersionPublicada version,
            Action<double> progreso = null)
        {
            _scriptPreparado = "";

            string carpetaTemp =
                Path.Combine(
                    Path.GetTempPath(),
                    "sccosmos_actualizacion_"
                    + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(
                carpetaTemp);

            string zip =
                Path.Combine(
                    carpetaTemp,
                    ActualizadorSccosmos.NombreWindows);

            await ActualizadorSccosmos
                .DescargarAsync(
                    version.UrlWindows,
                    zip,
                    progreso);

            string carpetaNueva =
                Path.Combine(
                    carpetaTemp,
                    "nueva");

            ZipFile.ExtractToDirectory(
                zip,
                carpetaNueva,
                true);

            // El ejecutable debe venir en la raíz del ZIP.
            if (!File.Exists(
                    Path.Combine(
                        carpetaNueva,
                        "SCCosmos.exe")))
            {
                return false;
            }

            string appDir =
                AppContext.BaseDirectory
                    .TrimEnd(
                        Path.DirectorySeparatorChar);

            string script =
                Path.Combine(
                    carpetaTemp,
                    "actualizar.cmd");

            File.WriteAllText(
                script,
                "@echo off\r\n"
                + "rem Espera a que la aplicación termine.\r\n"
                + ":espera\r\n"
                + "tasklist /FI \"IMAGENAME eq SCCosmos.exe\" | find /I \"SCCosmos.exe\" >nul\r\n"
                + "if not errorlevel 1 (\r\n"
                + "  timeout /t 1 /nobreak >nul\r\n"
                + "  goto espera\r\n"
                + ")\r\n"
                + "robocopy \"" + carpetaNueva + "\" \"" + appDir + "\" /E /NFL /NDL /NJH /NJS /R:5 /W:1 >nul\r\n"
                + "start \"\" \"" + Path.Combine(appDir, "SCCosmos.exe") + "\"\r\n"
                + "del \"%~f0\"\r\n");

            _scriptPreparado = script;

            return true;
        }


        /// <summary>
        /// Lanza el reemplazo preparado: la app debe cerrarse justo después
        /// (el script espera a que termine, copia y la vuelve a abrir).
        /// </summary>
        public static bool AplicarAhora()
        {
            if (!HayPreparada)
            {
                return false;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo(
                        "cmd.exe",
                        "/c \"" + _scriptPreparado + "\"")
                    {
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                return true;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// Descarga la versión nueva y programa el reemplazo: la app se
        /// cierra, el script copia los archivos y la vuelve a abrir.
        /// </summary>
        public static async Task<bool> DescargarYAplicarAsync(
            VersionPublicada version,
            Action<double> progreso = null)
        {
            if (!await PrepararAsync(
                    version,
                    progreso))
            {
                return false;
            }

            return AplicarAhora();
        }
    }
}
