using Microsoft.Data.Sqlite;

namespace SCCosmos.Mobile.Data;


// ============================================================================
// DOCUMENTOS / CORRESPONDENCIA (Android)
// ============================================================================
//
// Acceso genérico a la tabla compartida Documentos (categorías de
// correspondencia) y a las claves de configuración Asunto_*/Email_*.
// No se modifica el esquema.

public static partial class DatabaseHelper
{
    public static void GuardarDocumentoVisitaMobile(
        string categoria,
        string nombreArchivo,
        string rutaLocal,
        byte[] contenido)
    {
        if (string.IsNullOrWhiteSpace(
                categoria))
        {
            return;
        }

        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        InsertarDocumentoVisitaMobile(
            connection,
            categoria,
            nombreArchivo ?? "",
            rutaLocal ?? "",
            contenido ?? Array.Empty<byte>());
    }


    public static void EliminarDocumentoVisitaMobile(
        int id)
    {
        if (id <= 0)
        {
            return;
        }

        using var connection =
            AbrirConexionDocumentosVisitaMobile();

        using SqliteCommand cmd =
            connection.CreateCommand();

        cmd.CommandText =
            "DELETE FROM Documentos WHERE Id = $id;";

        cmd.Parameters.AddWithValue(
            "$id",
            id);

        cmd.ExecuteNonQuery();
    }


    /// <summary>
    /// Devuelve las categorías de correspondencia conocidas: las que ya
    /// tienen claves Asunto_* en la tabla Configuracion, unidas a las
    /// categorías estándar de WPF.
    /// </summary>
    public static List<string>
        ObtenerCategoriasCorrespondenciaMobile()
    {
        var categorias =
            new List<string>();

        using (var connection =
               new SqliteConnection(
                   ConnectionString))
        {
            connection.Open();

            if (ExisteTabla(
                    connection,
                    "Configuracion"))
            {
                using SqliteCommand cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    """
                    SELECT Clave
                    FROM Configuracion
                    WHERE Clave LIKE 'Asunto!_%' ESCAPE '!'
                       OR Clave LIKE 'Email!_%' ESCAPE '!';
                    """;

                try
                {
                    using SqliteDataReader reader =
                        cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        string clave =
                            reader.IsDBNull(0)
                                ? ""
                                : reader.GetString(0);

                        string categoria =
                            clave.StartsWith(
                                "Asunto_",
                                StringComparison.Ordinal)
                                ? clave.Substring(
                                    "Asunto_".Length)
                                : clave.Substring(
                                    "Email_".Length);

                        if (!string.IsNullOrWhiteSpace(
                                categoria)
                            &&
                            !categorias.Contains(
                                categoria))
                        {
                            categorias.Add(
                                categoria);
                        }
                    }
                }
                catch
                {
                    // Si falla la consulta, seguimos con las estándar.
                }
            }
        }

        foreach (string estandar in CategoriasEstandar)
        {
            if (!categorias.Contains(
                    estandar))
            {
                categorias.Add(
                    estandar);
            }
        }

        categorias.Sort(
            StringComparer.CurrentCultureIgnoreCase);

        return categorias;
    }


    private static readonly string[] CategoriasEstandar =
    {
        "Aviso de la visita",
        "Aviso S-302 (visita de SCS)",
        "Documentación",
        "Documentación (con SCS)",
        "Documentación (con visita de pastoreo)",
        "Nombramientos",
        "Envío de S-303",
        "WhatsApp al CCA",
        "Notificación AC",
        "Recordatorio AC",
        "Asignación asamblea circuito",
        "Asignación reunión precursores",
        "Envío de organigrama",
        "Mensaje a rep. de Betel"
    };
}
