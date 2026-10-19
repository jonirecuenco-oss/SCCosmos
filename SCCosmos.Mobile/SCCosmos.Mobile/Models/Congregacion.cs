namespace SCCosmos.Mobile.Models;

public class Congregacion
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";

    public int SeccionId { get; set; }

    public string NombreSeccion { get; set; } = "";
    public string DireccionSR { get; set; } = "";
    public string DiaEntreSemana { get; set; } = "";
    public string HoraEntreSemana { get; set; } = "";
    public string DiaFinSemana { get; set; } = "";
    public string HoraFinSemana { get; set; } = "";

    public double Latitud { get; set; }
    public double Longitud { get; set; }

    public int Capacidad { get; set; }
    public int Poblacion { get; set; }

    public string Provincia { get; set; } = "";
    public string Municipio { get; set; } = "";

    // ID y contraseña de Zoom y email del AV (campos nuevos).
    public string IdZoom { get; set; } = "";
    public string ClaveZoom { get; set; } = "";
    public string EmailAV { get; set; } = "";

    public string DetalleLista
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Codigo))
            {
                partes.Add(Codigo);
            }

            if (!string.IsNullOrWhiteSpace(NombreSeccion))
            {
                partes.Add(
                    NombreSeccion.StartsWith(
                        "Sección",
                        StringComparison.OrdinalIgnoreCase)
                        ? NombreSeccion
                        : "Sección " + NombreSeccion);
            }

            if (!string.IsNullOrWhiteSpace(Municipio))
            {
                partes.Add(Municipio);
            }
            else if (!string.IsNullOrWhiteSpace(Provincia))
            {
                partes.Add(Provincia);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}

public class ElementoMapa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Coordenadas { get; set; } = "";
    public string Descripcion { get; set; } = "";
}