namespace SCCosmos.Mobile.Models;

using SCCosmos.Mobile.Services;

// =========================================================
// RSC · REUNIÓN PARA EL SERVICIO DEL CAMPO (MÓVIL)
//
// Una RSC es una reunión breve antes de predicar. Se marca
// quién asiste, quién sale a predicar, quién acepta ir en
// grupo y los arreglos con otras personas (uno o varios).
// Con eso se forman grupos (de 4 o 6, y nunca menos de 4
// salvo arreglo), intentando que sean del mismo sexo.
//
// El superintendente de circuito y su esposa no son
// publicadores locales: se representan con PersonaId
// negativo (ver RscVirtualesMobile). Su nombre y sexo se
// guardan en la propia RSC.
// =========================================================

/// <summary>
/// Identificadores reservados para el superintendente de
/// circuito y su esposa dentro de una RSC.
/// </summary>
public static class RscVirtualesMobile
{
    public const int Superintendente = -1;
    public const int Esposa = -2;
}


public class RscReunionMobile
{
    public string Id { get; set; } = "";
    public string CongregacionCodigo { get; set; } = "";
    public string CongregacionNombre { get; set; } = "";
    public string Fecha { get; set; } = "";      // yyyy-MM-dd
    public string Turno { get; set; } = "";      // Mañana / Tarde
    public string Notas { get; set; } = "";
    public string FechaCreacion { get; set; } = "";

    public int NumeroGrupos { get; set; }
    public int NumeroPersonas { get; set; }

    public string FechaVisual =>
        DateTime.TryParse(
            Fecha,
            out DateTime valor)
            ? valor.ToString("dd/MM/yyyy")
            : Fecha;

    public string Titulo =>
        FechaVisual
        + " · "
        + Turno;

    public string TituloCompleto =>
        string.IsNullOrWhiteSpace(
            CongregacionNombre)
            ? Titulo
            : Titulo
              + " · "
              + CongregacionNombre;
}


public class RscPersonaMobile
{
    public int PersonaId { get; set; }
    public string Nombre { get; set; } = "";

    /// <summary>Hombre / Mujer (para agrupar por sexo).</summary>
    public string Sexo { get; set; } = "";

    public int GrupoPredicacion { get; set; }
    public string Territorio { get; set; } = "";
    public bool Asiste { get; set; }
    public bool SaleAPredicar { get; set; }
    public bool AceptaGrupo { get; set; } = true;

    /// <summary>Personas con las que va en arreglo (una o varias).</summary>
    public List<int> ArregloConPersonaIds { get; set; } =
        new List<int>();

    public string Notas { get; set; } = "";

    /// <summary>Número de grupo dentro de la RSC (0 = sin asignar).</summary>
    public int GrupoNumero { get; set; }

    public bool EsVirtual =>
        PersonaId < 0;

    public bool TieneTerritorio =>
        !string.IsNullOrWhiteSpace(
            Territorio);

    public bool SaleEnGrupos =>
        Asiste
        && SaleAPredicar;

    public bool TieneArreglo =>
        ArregloConPersonaIds != null
        && ArregloConPersonaIds.Count > 0;

    /// <summary>Texto útil del sexo: 1 hombre, 2 mujer, 0 sin dato.</summary>
    public int ClaseSexo
    {
        get
        {
            string sexo =
                (Sexo ?? "").ToLowerInvariant();

            if (sexo.Contains("hombre"))
            {
                return 1;
            }

            if (sexo.Contains("mujer"))
            {
                return 2;
            }

            return 0;
        }
    }
}


public class RscGrupoMobile
{
    public string Id { get; set; } = "";
    public string RscReunionId { get; set; } = "";
    public int Numero { get; set; }
    public string Notas { get; set; } = "";
    public string Territorio { get; set; } = "";

    public List<RscPersonaMobile> Miembros { get; set; } =
        new List<RscPersonaMobile>();

    public bool TieneTerritorio =>
        !string.IsNullOrWhiteSpace(
            Territorio);

    public string Titulo =>
        "Grupo "
        + Numero
        + " · "
        + Miembros.Count
        + (Miembros.Count == 1 ? " persona" : " personas");

    public string TerritoriosMiembros
    {
        get
        {
            var territorios =
                Miembros
                    .Where(m => m.TieneTerritorio)
                    .Select(m => m.Territorio.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToList();

            return territorios.Count == 0
                ? ""
                : "con territorio: "
                  + string.Join(
                        ", ",
                        territorios);
        }
    }

    public bool BajoMinimo =>
        Miembros.Count > 0
        && Miembros.Count
           < RscMotor.MinimoGrupo;
}


/// <summary>
/// Un arreglo ya apuntado en el programa de la visita
/// (ProgramaVisitaV2): el SC o su esposa predicarán ese día
/// con alguien en concreto.
/// </summary>
public class RscArregloDetectadoMobile
{
    public bool ParaSuperintendente { get; set; }
    public bool ParaEsposa { get; set; }
    public string Responsable { get; set; } = "";
    public int CompaneroId { get; set; }
    public string CompaneroNombre { get; set; } = "";
    public string Turno { get; set; } = "";
    public string Hora { get; set; } = "";
    public string Tipo { get; set; } = "";
}
