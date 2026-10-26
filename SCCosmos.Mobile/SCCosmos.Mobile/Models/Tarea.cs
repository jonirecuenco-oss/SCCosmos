namespace SCCosmos.Mobile.Models;

/// <summary>
/// Una tarea o evento programado, compartible con la esposa del SC.
/// </summary>
public class Tarea
{
    public int Id { get; set; }

    public string Titulo { get; set; } = "";

    public string Descripcion { get; set; } = "";

    /// <summary>Fecha de inicio en formato 'yyyy-MM-dd'.</summary>
    public string FechaInicio { get; set; } = "";

    /// <summary>Fecha final del rango (opcional), 'yyyy-MM-dd'.</summary>
    public string FechaFin { get; set; } = "";

    public bool TodoElDia { get; set; } = true;

    /// <summary>Hora 'HH:mm' cuando no es de todo el día.</summary>
    public string Hora { get; set; } = "";

    /// <summary>-1 = sin aviso; 0 = a la hora; minutos de antelación.</summary>
    public int AvisoMinutos { get; set; } = -1;

    /// <summary>Ninguna, Diaria, Semanal, Mensual o Anual.</summary>
    public string Repeticion { get; set; } = "Ninguna";

    public bool Compartida { get; set; }

    public bool Completada { get; set; }

    /// <summary>Personal, Calendario o Asambleas.</summary>
    public string Categoria { get; set; } = "Personal";

    /// <summary>SC o Esposa.</summary>
    public string CreadaPor { get; set; } = "";

    public string FechaCreacion { get; set; } = "";

    public string UltimaModificacion { get; set; } = "";


    public string TituloVisual =>
        string.IsNullOrWhiteSpace(Titulo)
            ? "(Sin título)"
            : Titulo;


    public string FechasVisual
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FechaInicio))
            {
                return "";
            }

            if (!DateTime.TryParse(FechaInicio, out DateTime inicio))
            {
                return FechaInicio;
            }

            string texto =
                inicio.ToString("ddd d MMM yyyy")
                    .Replace(".", "");

            if (!string.IsNullOrWhiteSpace(FechaFin)
                && DateTime.TryParse(FechaFin, out DateTime fin)
                && fin.Date != inicio.Date)
            {
                texto +=
                    "  →  " +
                    fin.ToString("ddd d MMM")
                        .Replace(".", "");
            }

            if (!TodoElDia
                && !string.IsNullOrWhiteSpace(Hora))
            {
                texto +=
                    "  ·  " + Hora;
            }

            return texto;
        }
    }


    public bool MostrarRepeticion =>
        Repeticion != "Ninguna";

    public string RepeticionVisual =>
        MostrarRepeticion
            ? "🔁 " + Repeticion
            : "";

    /// <summary>Etiqueta «Compartida» de la lista (según el interruptor).</summary>
    public bool EstaCompartida =>
        Compartida;


    public bool MostrarAviso =>
        AvisoMinutos >= 0;

    public string AvisoVisual =>
        AvisoMinutos switch
        {
            0 => "🔔 A la hora",
            10 => "🔔 10 min antes",
            30 => "🔔 30 min antes",
            60 => "🔔 1 hora antes",
            1440 => "🔔 1 día antes",
            _ => "🔔 " + AvisoMinutos + " min antes"
        };


    public bool MostrarEstado =>
        Completada;

    public string EstadoVisual =>
        Completada
            ? "✔ Completada"
            : "";

    public double Opacidad =>
        Completada
            ? 0.45
            : 1.0;
}
