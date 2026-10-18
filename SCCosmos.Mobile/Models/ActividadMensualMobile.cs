namespace SCCosmos.Mobile.Models;

/// <summary>
/// Actividad mensual de un publicador. Se corresponde con la tabla
/// compartida <c>ActividadPersonas</c> de WPF.
/// </summary>
public class ActividadMensualMobile
{
    public string Mes { get; set; } = "";

    public string AnioServicio { get; set; } = "";

    public bool Participacion { get; set; }

    public string CursosBiblicos { get; set; } = "";

    public bool PrecursorAuxiliar { get; set; }

    public string Horas { get; set; } = "";

    public string Notas { get; set; } = "";

    public string Etiqueta =>
        string.IsNullOrWhiteSpace(Mes)
            ? ""
            : $"{Mes} {AnioServicio}".Trim();

    public string Resumen
    {
        get
        {
            var partes =
                new List<string>();

            if (Participacion)
            {
                partes.Add("Participa");
            }

            if (PrecursorAuxiliar)
            {
                partes.Add("Prec. auxiliar");
            }

            if (!string.IsNullOrWhiteSpace(Horas))
            {
                partes.Add($"Horas: {Horas}");
            }

            if (!string.IsNullOrWhiteSpace(CursosBiblicos))
            {
                partes.Add($"Cursos: {CursosBiblicos}");
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}
