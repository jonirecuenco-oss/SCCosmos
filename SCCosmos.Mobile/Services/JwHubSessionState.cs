using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Services;

public static class JwHubSessionState
{
    private const string ClaveSesionConfigurada =
        "JwHub_SesionConfigurada";

    private const string ClaveUltimaValidacion =
        "JwHub_UltimaValidacionSesion";

    public static bool SesionConfigurada =>
        Preferences.Default.Get(
            ClaveSesionConfigurada,
            false);

    public static string UltimaValidacion =>
        Preferences.Default.Get(
            ClaveUltimaValidacion,
            "");

    public static void MarcarSesionValida()
    {
        Preferences.Default.Set(
            ClaveSesionConfigurada,
            true);

        Preferences.Default.Set(
            ClaveUltimaValidacion,
            DateTime.Now.ToString(
                "dd/MM/yyyy HH:mm:ss"));
    }

    public static void MarcarSesionNoValida()
    {
        Preferences.Default.Set(
            ClaveSesionConfigurada,
            false);
    }
}