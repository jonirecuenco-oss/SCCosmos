using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// PLANTILLA S-21 (Android)
// ============================================================================
//
// Reutiliza la tabla compartida Documentos con la categoría "S-21"
// (igual que la app móvil ya hace con "Envío de S-303"). No se cambia
// el esquema ni los datos de WPF.

public static partial class DatabaseHelper
{
    public static DocumentoVisitaMobile?
        ObtenerPlantillaS21VisitaMobile()
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                Id,
                Categoria,
                NombreArchivo,
                RutaLocal,
                ContenidoArchivo
            FROM Documentos
            WHERE Categoria = 'S-21'
              AND lower(NombreArchivo) LIKE '%.pdf'
            ORDER BY Id DESC
            LIMIT 1;
            """;

        using SqliteDataReader reader =
            cmd.ExecuteReader();

        return reader.Read()
            ? LeerDocumentoVisitaMobile(reader)
            : null;
    }


    public static void GuardarPlantillaS21VisitaMobile(
        string nombreArchivo,
        string rutaLocal,
        byte[] contenido)
    {
        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand eliminar =
            connection.CreateCommand();

        eliminar.CommandText =
            "DELETE FROM Documentos WHERE Categoria = 'S-21';";

        eliminar.ExecuteNonQuery();

        InsertarDocumentoVisitaMobile(
            connection,
            "S-21",
            nombreArchivo,
            rutaLocal,
            contenido);
    }
}
