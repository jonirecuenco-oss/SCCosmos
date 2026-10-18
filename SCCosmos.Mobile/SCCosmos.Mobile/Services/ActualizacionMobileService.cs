using Microsoft.Maui.Storage;

using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Services;

// ============================================================
// ACTUALIZACIONES DE LA APP (ANDROID)
//
// Comprueba si hay una versión nueva publicada en GitHub,
// descarga el APK y abre el instalador del sistema (Android pide
// confirmación; con "Actualizar automáticamente" se hace todo
// solo, salvo esa confirmación final del sistema).
// ============================================================

public static class ActualizacionMobileService
{
    private const string ClaveAuto =
        "SCCosmos_ActualizarAuto";


    /// <summary>Actualizar automáticamente (por dispositivo).</summary>
    public static bool AutoActiva
    {
        get => Preferences.Default.Get(
            ClaveAuto,
            false);

        set => Preferences.Default.Set(
            ClaveAuto,
            value);
    }


    /// <summary>Versión instalada (para la pantalla).</summary>
    public static string VersionInstalada =>
        ActualizadorSccosmos.VersionApp;


    /// <summary>Comprueba si hay una versión nueva publicada.</summary>
    public static Task<ResultadoComprobacion> ComprobarAsync()
    {
        return ActualizadorSccosmos.ComprobarAsync();
    }


    /// <summary>Hay una versión nueva disponible para Android.</summary>
    public static bool HayNueva(
        ResultadoComprobacion resultado)
    {
        return resultado != null
            && resultado.Correcto
            && resultado.Version != null
            && !string.IsNullOrWhiteSpace(
                resultado.Version.UrlApk)
            && ActualizadorSccosmos.EsMasNueva(
                resultado.Version.Numero,
                ActualizadorSccosmos.VersionApp);
    }


    /// <summary>Ruta del APK descargado (o vacío).</summary>
    public static string RutaApkDescargado
    {
        get;
        private set;
    } = "";


    /// <summary>
    /// Descarga el APK de la versión nueva. Devuelve false si no
    /// se pudo.
    /// </summary>
    public static async Task<bool> DescargarAsync(
        VersionPublicada version,
        Action<double> progreso = null)
    {
        try
        {
            string carpeta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    "actualizacion");

            Directory.CreateDirectory(
                carpeta);

            string destino =
                Path.Combine(
                    carpeta,
                    ActualizadorSccosmos.NombreApk);

            if (File.Exists(destino))
            {
                try
                {
                    File.Delete(destino);
                }
                catch
                {
                }
            }

            await ActualizadorSccosmos.DescargarAsync(
                version.UrlApk,
                destino,
                progreso);

            RutaApkDescargado =
                destino;

            return File.Exists(destino);
        }
        catch
        {
            RutaApkDescargado = "";
            return false;
        }
    }


    /// <summary>
    /// Descarga el APK de la versión nueva y abre el instalador.
    /// </summary>
    public static async Task<bool> DescargarEInstalarAsync(
        VersionPublicada version,
        Action<double> progreso = null)
    {
        if (!await DescargarAsync(
                version,
                progreso))
        {
            return false;
        }

        return Instalar();
    }


    /// <summary>
    /// Abre el instalador del sistema con el APK descargado.
    /// </summary>
    public static bool Instalar()
    {
#if ANDROID
        try
        {
            if (string.IsNullOrWhiteSpace(
                    RutaApkDescargado)
                || !File.Exists(
                    RutaApkDescargado))
            {
                return false;
            }

            var contexto =
                Android.App.Application.Context;

            var archivo =
                new Java.IO.File(
                    RutaApkDescargado);

            Android.Net.Uri uri =
                Microsoft.Maui.Storage.FileProvider
                    .GetUriForFile(
                        contexto,
                        contexto.PackageName
                            + ".fileProvider",
                        archivo);

            var intent =
                new Android.Content.Intent(
                    Android.Content.Intent.ActionView);

            intent.SetDataAndType(
                uri,
                "application/vnd.android.package-archive");

            intent.AddFlags(
                Android.Content.ActivityFlags.GrantReadUriPermission);

            intent.AddFlags(
                Android.Content.ActivityFlags.NewTask);

            contexto.StartActivity(
                intent);

            return true;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }
}
