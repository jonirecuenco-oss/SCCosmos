using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Data;

public static class DatabasePaths
{
    public static string RutaLocal =>
        Path.Combine(
            FileSystem.AppDataDirectory,
            "sccosmos.db");
}