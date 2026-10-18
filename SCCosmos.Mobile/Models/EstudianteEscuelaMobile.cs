namespace SCCosmos.Mobile.Models;

/// <summary>
/// Candidato a la Escuela de Precursores. Mismo modelo que usa WPF.
/// </summary>
public class EstudianteEscuelaMobile
{
    public int Id { get; set; }
    public string NombreComun { get; set; } = "";
    public string Congregacion { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Edad { get; set; } = "";
    public string FechaInicio { get; set; } = "";
    public string FechaUltimaEscuela { get; set; } = "";
    public bool IsDescartado { get; set; }

    public string TextoAccion =>
        IsDescartado
            ? "Incluir"
            : "Descartar";

    public string Detalle
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Congregacion))
            {
                partes.Add(Congregacion);
            }

            if (!string.IsNullOrWhiteSpace(Edad)
                &&
                Edad != "-")
            {
                partes.Add(Edad + " años");
            }

            if (!string.IsNullOrWhiteSpace(FechaInicio))
            {
                partes.Add("Prec. desde " + FechaInicio);
            }

            if (!string.IsNullOrWhiteSpace(FechaUltimaEscuela))
            {
                partes.Add("Escuela: " + FechaUltimaEscuela);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}
