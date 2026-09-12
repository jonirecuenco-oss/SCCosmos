namespace SCCosmos.Mobile.Models;

public class ActividadHoy
{
    public string Hora { get; set; } = "";
    public string TipoActividad { get; set; } = "";
    public string CompaneroNombre { get; set; } = "";

    // Sustituye a Visibility de WPF
    public bool VerCompanero { get; set; }
}