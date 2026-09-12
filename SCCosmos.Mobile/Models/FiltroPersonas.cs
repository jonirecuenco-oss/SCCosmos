namespace SCCosmos.Mobile.Models;

public class FiltrosPersonas
{
    public string Nombramiento { get; set; } = "";
    public string Sexo { get; set; } = "";
    public string Esperanza { get; set; } = "";

    public List<string> Congregaciones { get; set; } = new();
    public List<string> Servicios { get; set; } = new();
    public List<string> Solicitudes { get; set; } = new();
    public List<string> Privilegios { get; set; } = new();

    public bool TieneFiltros =>
        !string.IsNullOrWhiteSpace(Nombramiento)
        || !string.IsNullOrWhiteSpace(Sexo)
        || !string.IsNullOrWhiteSpace(Esperanza)
        || Congregaciones.Count > 0
        || Servicios.Count > 0
        || Solicitudes.Count > 0
        || Privilegios.Count > 0;

    public FiltrosPersonas Clonar()
    {
        return new FiltrosPersonas
        {
            Nombramiento = Nombramiento,
            Sexo = Sexo,
            Esperanza = Esperanza,

            Congregaciones = new List<string>(Congregaciones),
            Servicios = new List<string>(Servicios),
            Solicitudes = new List<string>(Solicitudes),
            Privilegios = new List<string>(Privilegios)
        };
    }
}