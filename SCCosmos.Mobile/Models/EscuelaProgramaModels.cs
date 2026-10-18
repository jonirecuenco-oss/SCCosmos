namespace SCCosmos.Mobile.Models;

/// <summary>
/// Un punto de un taller (asignado, acompañante y hora de ensayo).
/// </summary>
public class EscuelaTallerPuntoMobile
{
    public string Id { get; set; } = "";
    public string EscuelaId { get; set; } = "";
    public int Taller { get; set; } = 1;
    public int Numero { get; set; }
    public string Minutos { get; set; } = "";
    public string Tema { get; set; } = "";
    public int AsignadoId { get; set; }
    public string AsignadoNombre { get; set; } = "";
    public int AcompananteId { get; set; }
    public string AcompananteNombre { get; set; } = "";
    public string HoraEnsayo { get; set; } = "";

    public string TallerTexto =>
        "T" + Taller;

    public string Titulo =>
        Numero + " · " + Tema + " (" + Minutos + " min)";

    public string AsignadoTexto =>
        string.IsNullOrWhiteSpace(
            AsignadoNombre)
            ? "— nadie —"
            : AsignadoNombre;

    public string AcompananteTexto =>
        string.IsNullOrWhiteSpace(
            AcompananteNombre)
            ? "— nadie —"
            : AcompananteNombre;

    public string EnsayoTexto =>
        string.IsNullOrWhiteSpace(
            HoraEnsayo)
            ? "—"
            : HoraEnsayo;
}


/// <summary>
/// Un día del horario, con sus tres oraciones.
/// </summary>
public class EscuelaHorarioDiaMobile
{
    public string Id { get; set; } = "";
    public string EscuelaId { get; set; } = "";
    public string Dia { get; set; } = "";
    public int OracionInicialId { get; set; }
    public string OracionInicialNombre { get; set; } = "";
    public int OracionIntermediaId { get; set; }
    public string OracionIntermediaNombre { get; set; } = "";
    public int OracionFinalId { get; set; }
    public string OracionFinalNombre { get; set; } = "";

    public string OracionInicialTexto =>
        string.IsNullOrWhiteSpace(
            OracionInicialNombre)
            ? "— asignar —"
            : OracionInicialNombre;

    public string OracionIntermediaTexto =>
        string.IsNullOrWhiteSpace(
            OracionIntermediaNombre)
            ? "— asignar —"
            : OracionIntermediaNombre;

    public string OracionFinalTexto =>
        string.IsNullOrWhiteSpace(
            OracionFinalNombre)
            ? "— asignar —"
            : OracionFinalNombre;
}
