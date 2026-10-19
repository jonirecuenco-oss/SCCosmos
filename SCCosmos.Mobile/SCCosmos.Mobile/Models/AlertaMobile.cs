namespace SCCosmos.Mobile.Models;

/// <summary>
/// Una alerta pendiente mostrada al usuario. Réplica ligera del motor
/// de alertas de WPF, adaptada a las capacidades del móvil.
/// </summary>
public class AlertaMobile
{
    public string Titulo { get; set; } = "";

    public string Descripcion { get; set; } = "";

    /// <summary>
    /// Categoría interna: JWHub, S302, Documentacion, S303,
    /// Recordatorio.
    /// </summary>
    public string Tipo { get; set; } = "";

    /// <summary>
    /// Identidad estable de la alerta (compartida con Windows):
    /// permite marcar como leída en cualquier dispositivo.
    /// </summary>
    public string Identidad { get; set; } = "";
}
