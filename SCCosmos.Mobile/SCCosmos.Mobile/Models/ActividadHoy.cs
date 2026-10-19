namespace SCCosmos.Mobile.Models;

public class ActividadHoy
{
    public string Hora { get; set; } = "";
    public string TipoActividad { get; set; } = "";

    public string Responsable { get; set; } = "";
    public bool MostrarResponsable => !string.IsNullOrWhiteSpace(Responsable);

    public string CompaneroNombre { get; set; } = "";

    // Sustituye a Visibility de WPF
    public bool VerCompanero { get; set; }

    public string Ubicacion { get; set; } = "";
    public bool MostrarUbicacion => !string.IsNullOrWhiteSpace(Ubicacion);

    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }

    public bool MostrarMapa =>
        Latitud != 0
        || Longitud != 0
        || !string.IsNullOrWhiteSpace(Direccion);
}
