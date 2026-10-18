namespace SCCosmos.Mobile.Models;

/// <summary>
/// Actividad del programa de la visita lista para importar/insertar.
/// Equivale a ActividadPrograma de WPF.
/// </summary>
public class ActividadProgramaMobile
{
    public string DiaSemana { get; set; } = "";
    public string Turno { get; set; } = "General";
    public string Responsable { get; set; } = "SC";
    public string TipoActividad { get; set; } = "";
    public string Hora { get; set; } = "";
    public string Notas { get; set; } = "";
    public int CompaneroId { get; set; }
    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
}
