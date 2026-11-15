namespace SCCosmos.Sincro;

/// <summary>
/// Un evento del programa de la visita, tal como se guarda: lo dan el PDF
/// del programa (ImportadorProgramaVisita) y el formulario web (ProgramaWeb).
/// </summary>
public sealed class EventoProgramaVisita
{
    public string DiaSemana { get; set; } = "";

    /// <summary>Mañana, Tarde o General.</summary>
    public string Turno { get; set; } = "";

    public string Hora { get; set; } = "";

    public string TipoActividad { get; set; } = "";

    /// <summary>SC, Esposa, Ambos o vacío.</summary>
    public string Responsable { get; set; } = "";

    public string Notas { get; set; } = "";

    /// <summary>Dirección/ubicación del evento (p. ej. el salón del Reino).</summary>
    public string Ubicacion { get; set; } = "";

    /// <summary>Coordenadas de la ubicación, si se conocen (el formulario web las da; el PDF no).</summary>
    public double Latitud { get; set; }

    public double Longitud { get; set; }
}
