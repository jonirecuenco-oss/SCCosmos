using System.Security.Cryptography;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.App;
using Android.Content;
using Android.Provider;
#endif

namespace SCCosmos.Mobile.Services;

public static class SyncFolderService
{
    private const string ClaveUriCarpeta =
        "SCCosmos_UriCarpetaSincronizada";

    private const int RequestCodeCarpeta =
        8457;

#if ANDROID
    private static TaskCompletionSource<string?>? _resultadoSelector;
#endif

    public static string UriCarpeta =>
        Preferences.Default.Get(
            ClaveUriCarpeta,
            "");

    public static bool EstaConfigurada =>
        !string.IsNullOrWhiteSpace(UriCarpeta);

    public static string NombreCarpeta
    {
        get
        {
#if ANDROID
            if (!EstaConfigurada)
                return "";

            try
            {
                var uri =
                    Android.Net.Uri.Parse(UriCarpeta);

                string documentId =
                    DocumentsContract.GetTreeDocumentId(uri);

                int pos = documentId.LastIndexOf('/');

                return pos >= 0
                    ? documentId[(pos + 1)..]
                    : documentId;
            }
            catch
            {
                return "SCCosmos";
            }
#else
            return "";
#endif
        }
    }

    public static Task<string?> SeleccionarCarpetaAsync()
    {
#if ANDROID
        var activity =
            Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "No se encuentra la actividad Android actual.");

        _resultadoSelector =
            new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var intent =
            new Intent(Intent.ActionOpenDocumentTree);

        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission |
            ActivityFlags.GrantWriteUriPermission |
            ActivityFlags.GrantPersistableUriPermission |
            ActivityFlags.GrantPrefixUriPermission);

        activity.StartActivityForResult(
            intent,
            RequestCodeCarpeta);

        return _resultadoSelector.Task;
#else
        return Task.FromResult<string?>(null);
#endif
    }

#if ANDROID
    public static void ProcesarResultadoSeleccion(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        if (requestCode != RequestCodeCarpeta)
            return;

        if (_resultadoSelector == null)
            return;

        if (resultCode != Result.Ok ||
            data?.Data == null)
        {
            _resultadoSelector.TrySetResult(null);
            _resultadoSelector = null;
            return;
        }

        try
        {
            var uri = data.Data;

            var resolver =
                Android.App.Application.Context.ContentResolver;

            var permisos =
                data.Flags &
                (ActivityFlags.GrantReadUriPermission |
                 ActivityFlags.GrantWriteUriPermission);

            resolver.TakePersistableUriPermission(
                uri,
                permisos);

            Preferences.Default.Set(
                ClaveUriCarpeta,
                uri.ToString());

            _resultadoSelector.TrySetResult(
                uri.ToString());
        }
        catch (Exception ex)
        {
            _resultadoSelector.TrySetException(ex);
        }
        finally
        {
            _resultadoSelector = null;
        }
    }

    private static Android.Net.Uri ObtenerTreeUri()
    {
        if (!EstaConfigurada)
        {
            throw new InvalidOperationException(
                "No hay carpeta sincronizada configurada.");
        }

        return Android.Net.Uri.Parse(
            UriCarpeta);
    }

    private static Android.Net.Uri? BuscarArchivo(
        string nombreArchivo)
    {
        var treeUri =
            ObtenerTreeUri();

        var resolver =
            Android.App.Application.Context.ContentResolver;

        string treeId =
            DocumentsContract.GetTreeDocumentId(
                treeUri);

        var childrenUri =
            DocumentsContract.BuildChildDocumentsUriUsingTree(
                treeUri,
                treeId);

        string[] projection =
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName
        };

        using var cursor =
            resolver.Query(
                childrenUri,
                projection,
                null,
                null,
                null);

        if (cursor == null)
            return null;

        int indiceId =
            cursor.GetColumnIndex(
                DocumentsContract.Document.ColumnDocumentId);

        int indiceNombre =
            cursor.GetColumnIndex(
                DocumentsContract.Document.ColumnDisplayName);

        while (cursor.MoveToNext())
        {
            string id =
                cursor.GetString(indiceId);

            string nombre =
                cursor.GetString(indiceNombre);

            if (string.Equals(
                    nombre,
                    nombreArchivo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return DocumentsContract
                    .BuildDocumentUriUsingTree(
                        treeUri,
                        id);
            }
        }

        return null;
    }
#endif

    public static bool ExisteBaseDatos()
    {
#if ANDROID
        try
        {
            return BuscarArchivo(
                "sccosmos.db") != null;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public static async Task CopiarBaseDatosALocalAsync(
        string destino)
    {
#if ANDROID
        var archivoUri =
            BuscarArchivo("sccosmos.db");

        if (archivoUri == null)
        {
            throw new FileNotFoundException(
                "No se encuentra sccosmos.db en la carpeta seleccionada.");
        }

        var resolver =
            Android.App.Application.Context.ContentResolver;

        using var entrada =
            resolver.OpenInputStream(
                archivoUri)
            ?? throw new IOException(
                "Android no pudo abrir sccosmos.db.");

        string? directorio =
            Path.GetDirectoryName(destino);

        if (!string.IsNullOrWhiteSpace(directorio))
        {
            Directory.CreateDirectory(
                directorio);
        }

        using var salida =
            File.Create(destino);

        await entrada.CopyToAsync(
            salida);

        await salida.FlushAsync();
#else
        await Task.CompletedTask;
#endif
    }

    public static string CalcularHashBaseDatos()
    {
#if ANDROID
        var archivoUri =
            BuscarArchivo("sccosmos.db")
            ?? throw new FileNotFoundException(
                "No se encuentra sccosmos.db.");

        var resolver =
            Android.App.Application.Context.ContentResolver;

        using var stream =
            resolver.OpenInputStream(
                archivoUri)
            ?? throw new IOException(
                "No se pudo abrir sccosmos.db.");

        using var sha =
            SHA256.Create();

        return Convert.ToHexString(
            sha.ComputeHash(stream));
#else
        return "";
#endif
    }

    public static async Task CopiarLocalABaseDatosAsync(
    string origenLocal)
    {
#if ANDROID
        if (!File.Exists(origenLocal))
        {
            throw new FileNotFoundException(
                "No existe la base de datos local que se quiere exportar.");
        }

        var archivoUri =
            BuscarArchivo("sccosmos.db");

        if (archivoUri == null)
        {
            throw new FileNotFoundException(
                "No se encuentra sccosmos.db en la carpeta sincronizada.");
        }

        var resolver =
            Android.App.Application.Context.ContentResolver;

        using var entrada =
            File.OpenRead(origenLocal);

        using var salida =
            resolver.OpenOutputStream(
                archivoUri,
                "wt")
            ?? throw new IOException(
                "Android no pudo abrir sccosmos.db para escritura.");

        await entrada.CopyToAsync(salida);
        await salida.FlushAsync();
#else
    await Task.CompletedTask;
#endif
    }


    // =========================================================
    // EXPLORACIÓN DE ARCHIVOS DE LA CARPETA SINCRONIZADA
    // (Archivo del circuito: JSON + carpeta ArchivosGuardados)
    // =========================================================

    public static List<string> ListarNombres(
        string? subcarpeta)
    {
        var resultado =
            new List<string>();

#if ANDROID
        if (!EstaConfigurada)
        {
            return resultado;
        }

        try
        {
            var treeUri =
                ObtenerTreeUri();

            string treeId =
                DocumentsContract.GetTreeDocumentId(
                    treeUri);

            string docId =
                ConstruirDocumentId(
                    treeId,
                    subcarpeta);

            var childrenUri =
                DocumentsContract
                    .BuildChildDocumentsUriUsingTree(
                        treeUri,
                        docId);

            var resolver =
                Android.App.Application.Context.ContentResolver;

            string[] projection =
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName
            };

            using var cursor =
                resolver.Query(
                    childrenUri,
                    projection,
                    null,
                    null,
                    null);

            if (cursor == null)
            {
                return resultado;
            }

            int idCol =
                cursor.GetColumnIndex(
                    DocumentsContract.Document.ColumnDocumentId);

            int nameCol =
                cursor.GetColumnIndex(
                    DocumentsContract.Document.ColumnDisplayName);

            while (cursor.MoveToNext())
            {
                string nombre =
                    cursor.GetString(nameCol) ?? "";

                if (!string.IsNullOrWhiteSpace(
                        nombre))
                {
                    resultado.Add(
                        nombre);
                }
            }
        }
        catch
        {
            // Carpeta no accesible.
        }
#endif

        resultado.Sort(
            StringComparer.CurrentCultureIgnoreCase);

        return resultado;
    }


    public static async Task<byte[]> LeerArchivoAsync(
        string? subcarpeta,
        string nombreArchivo)
    {
#if ANDROID
        if (!EstaConfigurada
            ||
            string.IsNullOrWhiteSpace(
                nombreArchivo))
        {
            return Array.Empty<byte>();
        }

        try
        {
            var treeUri =
                ObtenerTreeUri();

            string treeId =
                DocumentsContract.GetTreeDocumentId(
                    treeUri);

            string docId =
                ConstruirDocumentId(
                    treeId,
                    subcarpeta);

            var childrenUri =
                DocumentsContract
                    .BuildChildDocumentsUriUsingTree(
                        treeUri,
                        docId);

            var resolver =
                Android.App.Application.Context.ContentResolver;

            string[] projection =
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName
            };

            using (var cursor =
                   resolver.Query(
                       childrenUri,
                       projection,
                       null,
                       null,
                       null))
            {
                if (cursor == null)
                {
                    return Array.Empty<byte>();
                }

                int idCol =
                    cursor.GetColumnIndex(
                        DocumentsContract.Document.ColumnDocumentId);

                int nameCol =
                    cursor.GetColumnIndex(
                        DocumentsContract.Document.ColumnDisplayName);

                while (cursor.MoveToNext())
                {
                    string nombre =
                        cursor.GetString(nameCol) ?? "";

                    if (!string.Equals(
                            nombre,
                            nombreArchivo,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string id =
                        cursor.GetString(idCol) ?? "";

                    var fileUri =
                        DocumentsContract
                            .BuildDocumentUriUsingTree(
                                treeUri,
                                id);

                    using var entrada =
                        resolver.OpenInputStream(
                            fileUri);

                    if (entrada == null)
                    {
                        return Array.Empty<byte>();
                    }

                    using var memoria =
                        new MemoryStream();

                    await entrada.CopyToAsync(
                        memoria);

                    return memoria.ToArray();
                }
            }
        }
        catch
        {
        }

        return Array.Empty<byte>();
#else
        await Task.CompletedTask;
        return Array.Empty<byte>();
#endif
    }


#if ANDROID
    private static string ConstruirDocumentId(
        string treeId,
        string? subcarpeta)
    {
        if (string.IsNullOrWhiteSpace(
                subcarpeta))
        {
            return treeId;
        }

        string limpia =
            subcarpeta.Replace(
                '\\',
                '/')
                .Trim('/');

        if (string.IsNullOrWhiteSpace(
                limpia))
        {
            return treeId;
        }

        return treeId +
               "/" +
               limpia;
    }


    private static Android.Net.Uri? BuscarHijoUri(
        string? subcarpeta,
        string nombre)
    {
        var tree =
            ObtenerTreeUri();

        string treeId =
            DocumentsContract.GetTreeDocumentId(
                tree);

        string docId =
            ConstruirDocumentId(
                treeId,
                subcarpeta);

        var childrenUri =
            DocumentsContract
                .BuildChildDocumentsUriUsingTree(
                    tree,
                    docId);

        var resolver =
            Android.App.Application.Context.ContentResolver;

        using var cursor =
            resolver.Query(
                childrenUri,
                new[]
                {
                    DocumentsContract.Document.ColumnDocumentId,
                    DocumentsContract.Document.ColumnDisplayName
                },
                null,
                null,
                null);

        if (cursor == null)
        {
            return null;
        }

        int idCol =
            cursor.GetColumnIndex(
                DocumentsContract.Document.ColumnDocumentId);

        int nameCol =
            cursor.GetColumnIndex(
                DocumentsContract.Document.ColumnDisplayName);

        while (cursor.MoveToNext())
        {
            string n =
                cursor.GetString(nameCol) ?? "";

            if (string.Equals(
                    n,
                    nombre,
                    StringComparison.OrdinalIgnoreCase))
            {
                return DocumentsContract
                    .BuildDocumentUriUsingTree(
                        tree,
                        cursor.GetString(idCol) ?? "");
            }
        }

        return null;
    }
#endif


    public static bool ExisteArchivo(
        string? subcarpeta,
        string nombre)
    {
#if ANDROID
        try
        {
            return BuscarHijoUri(
                subcarpeta,
                nombre) != null;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }


    public static async Task<string> LeerTextoAsync(
        string? subcarpeta,
        string nombre)
    {
        byte[] bytes =
            await LeerArchivoAsync(
                subcarpeta,
                nombre);

        return bytes.Length == 0
            ? ""
            : System.Text.Encoding.UTF8.GetString(
                bytes);
    }


    public static async Task<bool> EscribirBytesAsync(
        string? subcarpeta,
        string nombre,
        byte[] contenido)
    {
#if ANDROID
        try
        {
            var resolver =
                Android.App.Application.Context.ContentResolver;

            var uri =
                BuscarHijoUri(
                    subcarpeta,
                    nombre);

            if (uri == null)
            {
                var tree =
                    ObtenerTreeUri();

                string treeId =
                    DocumentsContract.GetTreeDocumentId(
                        tree);

                string docId =
                    ConstruirDocumentId(
                        treeId,
                        subcarpeta);

                var parentUri =
                    DocumentsContract
                        .BuildDocumentUriUsingTree(
                            tree,
                            docId);

                uri =
                    DocumentsContract.CreateDocument(
                        resolver,
                        parentUri,
                        "application/octet-stream",
                        nombre);
            }

            if (uri == null)
            {
                return false;
            }

            using var salida =
                resolver.OpenOutputStream(
                    uri,
                    "wt");

            if (salida == null)
            {
                return false;
            }

            await salida.WriteAsync(
                contenido,
                0,
                contenido.Length);

            await salida.FlushAsync();

            return true;
        }
        catch
        {
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }


    public static async Task<bool> EscribirTextoAsync(
        string? subcarpeta,
        string nombre,
        string contenido)
    {
        return await EscribirBytesAsync(
            subcarpeta,
            nombre,
            System.Text.Encoding.UTF8.GetBytes(
                contenido ?? ""));
    }


    public static bool EliminarArchivo(
        string? subcarpeta,
        string nombre)
    {
#if ANDROID
        try
        {
            var uri =
                BuscarHijoUri(
                    subcarpeta,
                    nombre);

            if (uri == null)
            {
                return false;
            }

            var resolver =
                Android.App.Application.Context.ContentResolver;

            return resolver.Delete(
                       uri,
                       null,
                       null)
                   > 0;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }


    public static bool AsegurarCarpetaRaiz(
        string nombre)
    {
#if ANDROID
        try
        {
            if (BuscarHijoUri(
                    "",
                    nombre) != null)
            {
                return true;
            }

            var tree =
                ObtenerTreeUri();

            var resolver =
                Android.App.Application.Context.ContentResolver;

            string treeId =
                DocumentsContract.GetTreeDocumentId(
                    tree);

            var rootUri =
                DocumentsContract
                    .BuildDocumentUriUsingTree(
                        tree,
                        treeId);

            var creada =
                DocumentsContract.CreateDocument(
                    resolver,
                    rootUri,
                    "vnd.android.document/directory",
                    nombre);

            return creada != null;
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