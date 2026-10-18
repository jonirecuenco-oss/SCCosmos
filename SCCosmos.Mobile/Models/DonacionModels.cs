namespace SCCosmos.Mobile.Models;

/// <summary>
/// Donación recibida. La tabla Donaciones es compartida con SCCosmos (WPF):
/// mismas columnas y mismos valores para que la sincronización de la base de
/// datos la transporte igual que el resto de módulos.
/// </summary>
public class DonacionMobile
{
    public int Id { get; set; }

    public string Fecha { get; set; } = "";

    public int PersonaId { get; set; }

    public string NombrePersona { get; set; } = "";

    public string CongregacionCodigo { get; set; } = "";

    public string CongregacionNombre { get; set; } = "";

    public double Cantidad { get; set; }

    public string MedioPago { get; set; } = "";

    public string Notas { get; set; } = "";

    public string FechaCreacion { get; set; } = "";

    // ------------------------------------------------------------------
    // Texto ya preparado para la interfaz
    // ------------------------------------------------------------------

    public string PersonaVisual =>
        string.IsNullOrWhiteSpace(NombrePersona)
            ? "Desconocido"
            : NombrePersona;

    public string CongregacionVisual =>
        !string.IsNullOrWhiteSpace(CongregacionNombre)
            ? CongregacionNombre
            : (string.IsNullOrWhiteSpace(CongregacionCodigo)
                ? ""
                : CongregacionCodigo);

    public string CantidadVisual =>
        Cantidad.ToString(
            "N2",
            System.Globalization.CultureInfo.GetCultureInfo("es-ES"))
        + " €";

    public string FechaVisual
    {
        get
        {
            if (DateTime.TryParse(
                    Fecha,
                    out DateTime fecha))
            {
                return fecha.ToString("dd/MM/yyyy");
            }

            return Fecha;
        }
    }

    public string DetalleVisual
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(FechaVisual))
            {
                partes.Add(FechaVisual);
            }

            if (!string.IsNullOrWhiteSpace(CongregacionVisual))
            {
                partes.Add(CongregacionVisual);
            }

            if (!string.IsNullOrWhiteSpace(MedioPago))
            {
                partes.Add(MedioPago);
            }

            return string.Join(
                " · ",
                partes);
        }
    }

    public bool EsDesconocido =>
        PersonaId <= 0;

    public bool TieneNotas =>
        !string.IsNullOrWhiteSpace(Notas);
}
