public class Congregacion
{
    // LAS QUE YA TENÍAS:
    public string Codigo { get; set; }
    public string Nombre { get; set; }
    public string Email { get; set; }

    // LAS NUEVAS PROPIEDADES QUE DEBES AÑADIR:
    public int SeccionId { get; set; }
    public string NombreSeccion { get; set; }
    public string DireccionSR { get; set; }
    public string HoraEntreSemana { get; set; }
    public string DiaFinSemana { get; set; }
    public string HoraFinSemana { get; set; }
}