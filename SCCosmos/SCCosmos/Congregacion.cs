public class Congregacion
{
    public string Codigo { get; set; }
    public string Nombre { get; set; }
    public string Email { get; set; }
    public int SeccionId { get; set; }
    public string NombreSeccion { get; set; }
    public string DireccionSR { get; set; }
    public string DiaEntreSemana { get; set; }
    public string HoraEntreSemana { get; set; }
    public string DiaFinSemana { get; set; }
    public string HoraFinSemana { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public int Capacidad { get; set; }
    public int Poblacion { get; set; }
    public String Provincia { get; set; }
    public String Municipio { get; set; }

    // ID y contraseña de Zoom y email del AV (campos nuevos).
    public string IdZoom { get; set; }
    public string ClaveZoom { get; set; }
    public string EmailAV { get; set; }
}

public class ElementoMapa
{
    public int Id { get; set; }
    public string Nombre { get; set; }
    public string Tipo { get; set; } // "Point" o "Polygon"
    public string Coordenadas { get; set; } // Array JS de coordenadas
    public string Descripcion { get; set; } // Todo el HTML original del KMZ
}