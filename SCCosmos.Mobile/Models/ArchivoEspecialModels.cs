namespace SCCosmos.Mobile.Models;

// Secciones especiales del Archivo del circuito (mismos campos que WPF).

public class HermanoPotencialMobile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public int PersonaId { get; set; }

    public string Descripcion { get; set; } = "";
}

public class ProblemaPendienteMobile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Congregacion { get; set; } = "";

    public string Descripcion { get; set; } = "";
}

/// <summary>Vista unificada de una fila de sección especial.</summary>
public sealed class ItemEspecialUi
{
    public string Id { get; set; } = "";

    public string Titulo { get; set; } = "";

    public string Descripcion { get; set; } = "";
}
