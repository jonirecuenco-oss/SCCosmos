using System;
using System.Linq;

namespace SCCosmos.Mobile.Models;

public class SemanaCalendario
{
    public int Id { get; set; }

    public DateTime FechaMartes { get; set; }

    public DateTime FechaLunes =>
        FechaMartes.AddDays(-1);

    public DateTime FechaDomingo =>
        FechaMartes.AddDays(5);

    public string IdIso =>
        FechaMartes.ToString("yyyy-MM-dd");

    public double OpacidadSemana =>
        DateTime.Today > FechaDomingo ? 0.4 : 1.0;

    public string RangoFechas
    {
        get
        {
            string formatoFechas =
                "Del " +
                $"{FechaLunes:dd-MMM} al {FechaDomingo:dd-MMM}".ToLower();

            if (Actividad == "Asamblea de circuito" &&
                !string.IsNullOrEmpty(FechaAsamblea))
            {
                return $"{formatoFechas} ({FechaAsamblea})";
            }

            return formatoFechas;
        }
    }

    public string Banner =>
        FechaLunes.ToString("MMMM yyyy").ToUpper();

    public string Actividad { get; set; } = "";
    public string Congregacion { get; set; } = "";
    public string Acompanante { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Alojamiento { get; set; } = "";

    public bool TieneDatosImportados { get; set; }

    public string SeccionAsamblea { get; set; } = "";
    public string FechaAsamblea { get; set; } = "";

    public bool NotificacionEnviada { get; set; }
    public bool RecordatorioEnviado { get; set; }

    public string CongregacionVisual =>
        Actividad == "Asamblea de circuito" &&
        !string.IsNullOrEmpty(SeccionAsamblea)
            ? $"Sección {SeccionAsamblea}"
            : Congregacion;

    public bool IsActual
    {
        get
        {
            DateTime hoy = DateTime.Today;
            return hoy >= FechaLunes && hoy <= FechaDomingo;
        }
    }

    public bool S302Enviado { get; set; }
    public bool DocEnviada { get; set; }

    public string TipoAsamblea { get; set; } = "";

    public string RepBetelNombre { get; set; } = "";
    public string RepBetelTelefono { get; set; } = "";
    public string RepBetelEmail { get; set; } = "";

    public bool RepBetelMensajeEnviado { get; set; }

    // En MAUI usamos bool en vez de System.Windows.Visibility

    public bool MostrarAsambleaBR =>
        Actividad == "Asamblea de circuito" &&
        TipoAsamblea == "AC-BR";

    public bool MostrarVisita =>
        Actividad == "Visita";

    public bool MostrarAsamblea =>
        Actividad == "Asamblea de circuito";

    public bool MostrarVerVisita =>
        Actividad == "Visita" &&
        TieneDatosImportados;

    public bool EsClonada { get; set; }

    // De momento conservamos estos valores.
    // Más adelante podemos trasladar los colores al XAML MAUI.

    public string ColorBotonRepBetel =>
        RepBetelMensajeEnviado
            ? "#15FFFFFF"
            : "#252528";

    public string ColorBotonNotificacion =>
        NotificacionEnviada
            ? "#10893E"
            : "Transparent";

    public string ColorBotonRecordatorio =>
        RecordatorioEnviado
            ? "#10893E"
            : "Transparent";

    public string ColorBotonS302 =>
        S302Enviado
            ? "#10893E"
            : "Transparent";

    public string ColorBotonDoc =>
        DocEnviada
            ? "#10893E"
            : "Transparent";

    public string AcompananteVisual
    {
        get
        {
            if (string.IsNullOrEmpty(Acompanante))
                return "";

            string marido = Acompanante
                .Split(
                    new[] { " y " },
                    StringSplitOptions.None)[0]
                .Trim();

            if (marido.Contains(","))
                return marido;

            string[] partes = marido.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length > 1)
            {
                string apellido = partes[^1];

                string nombre = string.Join(
                    " ",
                    partes.Take(partes.Length - 1));

                return $"{apellido}, {nombre}";
            }

            return marido;
        }
    }
}