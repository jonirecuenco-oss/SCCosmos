namespace SCCosmos.Mobile.Services;

/// <summary>
/// Borra una sola vez lo que dejó la sincronización antigua por carpeta (se
/// quitó en la v2.1.49): la base de la fusión, las copias «antes_de_fusion»
/// (unos 128 MB entre todo) y los temporales que ese código ya no borrará.
/// Solo esos nombres; la base de la app y el resto no se tocan.
/// </summary>
public static class RestosSincroAntigua
{
    private const string ClaveHecho =
        "SCCosmos_RestosSincroAntiguaBorrados";

    private static readonly string[] Archivos =
    {
        "sccosmos_fusion.db",
        "sccosmos_carpeta.db",
        "sccosmos_copia_conflicto.db",
        "sccosmos_exportando.db"
    };

    private static readonly string[] Sufijos =
    {
        "", "-wal", "-shm", "-journal"
    };


    public static void BorrarUnaVez()
    {
        if (Preferences.Default.Get(ClaveHecho, false))
        {
            return;
        }

        long bytes = 0;
        bool todoBorrado = true;
        string carpeta = FileSystem.AppDataDirectory;

        foreach (string nombre in Archivos)
        {
            foreach (string sufijo in Sufijos)
            {
                Borrar(Path.Combine(carpeta, nombre + sufijo), ref bytes, ref todoBorrado);
            }
        }

        string copias = Path.Combine(carpeta, "copias");

        if (Directory.Exists(copias))
        {
            foreach (string copia in Directory.GetFiles(copias, "sccosmos_antes_de_fusion_*"))
            {
                Borrar(copia, ref bytes, ref todoBorrado);
            }

            if (!Directory.EnumerateFileSystemEntries(copias).Any())
            {
                Directory.Delete(copias);
            }
        }

        // Si algo no se pudo borrar, se vuelve a intentar en el siguiente arranque.
        if (todoBorrado)
        {
            Preferences.Default.Set(ClaveHecho, true);
        }

        // Se ve desde el PC en sccosmos_perf.log (archivos de la app).
        Perf.Registrar("Limpieza.RestosSincroAntiguaKB", bytes / 1024);
    }


    private static void Borrar(
        string ruta,
        ref long bytes,
        ref bool todoBorrado)
    {
        try
        {
            var archivo = new FileInfo(ruta);

            if (!archivo.Exists)
            {
                return;
            }

            long tamano = archivo.Length;
            archivo.Delete();
            bytes += tamano;
        }
        catch
        {
            todoBorrado = false;
        }
    }
}