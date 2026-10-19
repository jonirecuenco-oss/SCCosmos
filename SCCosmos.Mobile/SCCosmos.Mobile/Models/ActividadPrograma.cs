namespace SCCosmos.Mobile.Models;

public class ActividadPrograma
{
    public string TipoActividad { get; set; } = "";
    public string Hora { get; set; } = "";
    public string NotasLibro { get; set; } = "";
    public int CompaneroId { get; set; }

    public string Responsable { get; set; } = "";
    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
}
