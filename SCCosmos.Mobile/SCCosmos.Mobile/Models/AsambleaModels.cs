namespace SCCosmos.Mobile.Models;

public class AsambleaMobile
{
    public int Id { get; set; }
    public string Anio { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Seccion { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string TextoTematico { get; set; } = "";
    public string Lugar { get; set; } = "";
    public string Fecha { get; set; } = "";

    // Rellenados por ObtenerAsambleasConDatosMobile: permiten saber si la
    // asamblea está realmente programada (como en Windows).
    public int PartesCount { get; set; }
    public int PartesConAsignado { get; set; }
    public int OrganigramaCount { get; set; }

    public bool TienePrograma =>
        PartesCount > 0;

    public bool TieneOrganigrama =>
        OrganigramaCount > 0;

    /// <summary>
    /// Lo marca la página cuando esta asamblea es la próxima programada en
    /// el calendario (se resalta su tarjeta).
    /// </summary>
    public bool EsProxima { get; set; }

    public string ResumenPrograma =>
        PartesCount == 0
            ? "Sin programa todavía"
            : $"{PartesCount} partes · " +
              $"{PartesConAsignado} con participantes";

    public string ResumenOrganigrama =>
        OrganigramaCount == 0
            ? "Sin organigrama todavía"
            : $"{OrganigramaCount} asignaciones";

    public string TituloCabecera =>
        string.IsNullOrWhiteSpace(Titulo)
            ? (string.IsNullOrWhiteSpace(Seccion)
                ? "Asamblea de circuito"
                : "Sección " + Seccion)
            : Titulo;

    public string Detalle
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Anio))
            {
                partes.Add(Anio);
            }

            if (!string.IsNullOrWhiteSpace(Tipo))
            {
                partes.Add(Tipo);
            }

            if (!string.IsNullOrWhiteSpace(Seccion))
            {
                partes.Add("Sección " + Seccion);
            }

            if (!string.IsNullOrWhiteSpace(Fecha))
            {
                partes.Add(Fecha);
            }

            if (!string.IsNullOrWhiteSpace(Lugar))
            {
                partes.Add(Lugar);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}

public class ParteAsambleaMobile
{
    public int Id { get; set; }
    public int AsignadoId { get; set; }
    public string Turno { get; set; } = "";
    public string Hora { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Participantes { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Asignado { get; set; } = "";

    public string Detalle
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Hora))
            {
                partes.Add(Hora);
            }

            if (!string.IsNullOrWhiteSpace(Asignado))
            {
                partes.Add(Asignado);
            }

            if (!string.IsNullOrWhiteSpace(Participantes))
            {
                partes.Add(Participantes);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}

public class OrganigramaAsambleaMobile
{
    public int Id { get; set; }
    public int AsignadoId { get; set; }
    public string Departamento { get; set; } = "";
    public string Rol { get; set; } = "";
    public string Asignado { get; set; } = "";

    public string Detalle =>
        string.IsNullOrWhiteSpace(Asignado)
            ? "(sin asignar)"
            : Asignado;
}

public class ReunionPrecursoresMobile
{
    public int Id { get; set; }
    public string Anio { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string TextoTematico { get; set; } = "";

    public List<SesionPrecursoresMobile> Sesiones
    { get; set; } = new();

    public string Detalle =>
        string.IsNullOrWhiteSpace(Anio)
            ? Titulo
            : $"{Anio} · {Titulo}".Trim(
                ' ', '·');
}

public class ParteSesionPrecursoresMobile
{
    public int Id { get; set; }
    public int SesionId { get; set; }
    public int AsignadoId { get; set; }
    public string Hora { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Participantes { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Asignado { get; set; } = "";

    public string Detalle
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Hora))
            {
                partes.Add(Hora);
            }

            if (!string.IsNullOrWhiteSpace(Asignado))
            {
                partes.Add(Asignado);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}


// ============================================================================
// MATRIZ DE ORADORES (misma lógica que Windows)
// ============================================================================

/// <summary>
/// Una participación real de un hermano en una parte (asamblea o reunión
/// de precursores) en un año concreto.
/// </summary>
public class ParticipacionOradorMobile
{
    public string Anio { get; set; } = "";

    /// <summary>"AC-BR", "AC-CO" o "RP".</summary>
    public string Grupo { get; set; } = "";

    public int AsignadoId { get; set; }

    public string TipoParte { get; set; } = "";

    public string Titulo { get; set; } = "";
}


/// <summary>
/// Fila de un año dentro de la matriz (equivale a FilaAnio de Windows).
/// </summary>
public class FilaAnioOradorMobile
{
    public int OradorId { get; set; }

    public string Anio { get; set; } = "";

    // Valores que se muestran (manual si existe, si no la participación real).
    public string Acbr { get; set; } = "";
    public string Rp { get; set; } = "";
    public string Acco { get; set; } = "";

    public bool Evaluacion { get; set; }

    public bool TieneDatos =>
        !string.IsNullOrWhiteSpace(Acbr)
        || !string.IsNullOrWhiteSpace(Rp)
        || !string.IsNullOrWhiteSpace(Acco);

    public string AcbrFondo => ColorFondo(Acbr);
    public string RpFondo => ColorFondo(Rp);
    public string AccoFondo => ColorFondo(Acco);

    /// <summary>Igual que GetBgColor de Windows.</summary>
    public static string ColorFondo(
        string? texto)
    {
        string s =
            (texto ?? "")
                .ToLowerInvariant();

        if (s.Contains("discurso"))
        {
            return "#005AB5";
        }

        if (s.Contains("atalaya"))
        {
            return "#DC3220";
        }

        if (s.Contains("entrevista"))
        {
            return "#009E73";
        }

        if (s.Contains("presidencia"))
        {
            return "#762A83";
        }

        return "#242830";
    }
}


/// <summary>
/// Orador de la matriz (equivale a OradorFila de Windows).
/// </summary>
public class OradorMatrizMobile
{
    public int Id { get; set; }

    public string Nombre { get; set; } = "";

    public string Seccion { get; set; } = "";

    public string Bos { get; set; } = "";

    public bool TieneHistorial { get; set; }

    public string SeccionVisual =>
        string.IsNullOrWhiteSpace(Seccion)
            ? ""
            : "Sección " + Seccion;

    public string BosVisual =>
        string.IsNullOrWhiteSpace(Bos)
            ? "BOS: — (toca para editar)"
            : "BOS: " + Bos;

    public List<FilaAnioOradorMobile> Anios
    { get; set; } = new();
}

public class SesionPrecursoresMobile
{
    public int Id { get; set; }
    public int ReunionId { get; set; }
    public string Numero { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Hora { get; set; } = "";
    public string Congregaciones { get; set; } = "";
    public string Lugar { get; set; } = "";

    public string Detalle
    {
        get
        {
            var partes =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(Numero))
            {
                partes.Add("Sesión " + Numero);
            }

            if (!string.IsNullOrWhiteSpace(Fecha))
            {
                partes.Add(Fecha);
            }

            if (!string.IsNullOrWhiteSpace(Hora))
            {
                partes.Add(Hora);
            }

            if (!string.IsNullOrWhiteSpace(Lugar))
            {
                partes.Add(Lugar);
            }

            return string.Join(
                " · ",
                partes);
        }
    }
}
