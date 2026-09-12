namespace SCCosmos.Mobile.Models;

public class ActividadPrograma
{
    public string TipoActividad { get; set; } = "";
    public string Hora { get; set; } = "";
    public string NotasLibro { get; set; } = "";
    public int CompaneroId { get; set; }
}