using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Services;

public static class JwHubAutoSettings
{
    private const string ClaveActivo =
        "JwHub_AutoActivo";

    private const string ClaveHora =
        "JwHub_AutoHora";

    public static bool Activo
    {
        get =>
            Preferences.Default.Get(
                ClaveActivo,
                false);

        set =>
            Preferences.Default.Set(
                ClaveActivo,
                value);
    }

    public static TimeSpan Hora
    {
        get
        {
            string valor =
                Preferences.Default.Get(
                    ClaveHora,
                    "03:00");

            return TimeSpan.TryParse(
                valor,
                out TimeSpan hora)
                    ? hora
                    : new TimeSpan(3, 0, 0);
        }

        set =>
            Preferences.Default.Set(
                ClaveHora,
                value.ToString(@"hh\:mm"));
    }
}