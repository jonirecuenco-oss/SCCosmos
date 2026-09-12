using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Services;

public static class DeviceSettings
{
    private const string ClaveCarpeta = "SCCosmos_SyncFolderUri";

    public static string CarpetaSincronizada
    {
        get => Preferences.Default.Get(ClaveCarpeta, "");
        set => Preferences.Default.Set(ClaveCarpeta, value ?? "");
    }

    public static bool CarpetaConfigurada =>
        !string.IsNullOrWhiteSpace(CarpetaSincronizada);
}