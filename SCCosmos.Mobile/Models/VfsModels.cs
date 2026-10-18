using System.Text.Json.Serialization;

namespace SCCosmos.Mobile.Models;

// Modelos del Archivo del circuito (mismos nombres de campo que WPF).

public class SeccionVfsMobile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string NombreLimpio { get; set; } = "";

    public int IdEspecial { get; set; }
}

public class CarpetaVfsMobile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SeccionId { get; set; } = "";

    /// <summary>
    /// Carpeta padre; null (o vacío) = carpeta de primer nivel.
    /// Windows guarda null en la raíz, así que se mantiene null para que
    /// ambos programas lean y escriban exactamente lo mismo.
    /// </summary>
    public string? ParentId { get; set; }

    public string Nombre { get; set; } = "";

    public bool EsRaiz =>
        string.IsNullOrWhiteSpace(ParentId);
}

public class ArchivoVfsMobile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SeccionId { get; set; } = "";

    /// <summary>
    /// Carpeta que lo contiene; null (o vacío) = primer nivel de la sección.
    /// </summary>
    public string? CarpetaId { get; set; }

    public string NombreOriginal { get; set; } = "";

    public string NombreFisico { get; set; } = "";

    public bool EsRaiz =>
        string.IsNullOrWhiteSpace(CarpetaId);
}
