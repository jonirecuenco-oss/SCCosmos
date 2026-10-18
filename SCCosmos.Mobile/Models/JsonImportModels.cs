using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Maui.Graphics;

namespace SCCosmos.Mobile.Models;


// ============================================================================
// MODELOS DEL JSON S-21 / S-13
// ============================================================================
//
// Regla de identidad fijada para SCCosmos:
//
// Persona.Nombre       = nombre completo procedente del S-21 / JSON.
// Persona.NombreJWHub  = nombre procedente de JW Hub.
// Persona.NombreComun  = nombre común de JW Hub cuando existe cuenta JW.
//                        Si NO existe cuenta JW, coincide con Nombre (S-21).
//
// Las dos fuentes enriquecen UNA SOLA persona.
// ============================================================================

public sealed class PersonaJsonMobile
{
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "";

    [JsonPropertyName("grupo")]
    public int Grupo { get; set; }

    [JsonPropertyName("fecha_nacimiento")]
    public string FechaNacimiento { get; set; } = "";

    [JsonPropertyName("sexo")]
    public string Sexo { get; set; } = "";

    [JsonPropertyName("fecha_bautismo")]
    public string FechaBautismo { get; set; } = "";

    [JsonPropertyName("esperanza")]
    public string Esperanza { get; set; } = "";

    [JsonPropertyName("nombramiento")]
    public string Nombramiento { get; set; } = "";

    [JsonPropertyName("servicio")]
    public string Servicio { get; set; } = "";

    [JsonPropertyName("anio_servicio")]
    public string AnioServicio { get; set; } = "";

    [JsonPropertyName("informe")]
    public List<InformeJsonMobile> Informe { get; set; } = new();
}


public sealed class InformeJsonMobile
{
    [JsonPropertyName("mes")]
    public string Mes { get; set; } = "";

    // JsonElement permite aceptar valores bool, número o texto sin depender
    // de cómo haya serializado exactamente el exportador.
    [JsonPropertyName("participacion")]
    public JsonElement Participacion { get; set; }

    [JsonPropertyName("cursos_biblicos")]
    public JsonElement CursosBiblicos { get; set; }

    [JsonPropertyName("precursor_auxiliar")]
    public JsonElement PrecursorAuxiliar { get; set; }

    [JsonPropertyName("horas")]
    public JsonElement Horas { get; set; }

    [JsonPropertyName("notas")]
    public string Notas { get; set; } = "";

    public bool ParticipacionBool =>
        JsonImportValueHelper.LeerBool(Participacion);

    public bool PrecursorAuxiliarBool =>
        JsonImportValueHelper.LeerBool(PrecursorAuxiliar);

    public string CursosTexto =>
        JsonImportValueHelper.LeerTexto(CursosBiblicos);

    public string HorasTexto =>
        JsonImportValueHelper.LeerTexto(Horas);
}


// El modelo de escritorio no usa JsonPropertyName para S-13. Se mantiene
// compatible por nombre de propiedad con PropertyNameCaseInsensitive=true.
public sealed class TerritorioJsonMobile
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string UltimaFechaCompletado { get; set; } = "";
    public string AsignadoA { get; set; } = "";
    public string FechaAsignado { get; set; } = "";
    public string FechaCompletado { get; set; } = "";
    public string NombreVinculado { get; set; } = "";
    public string FechaCreacion { get; set; } = "";
    public string FechaEliminacion { get; set; } = "";
}


public enum TipoAnalisisJsonMobile
{
    Conflicto,
    Nuevo,
    Baja,
    Igual
}


public sealed class JsonImportItemMobile
{
    public TipoAnalisisJsonMobile Tipo { get; set; }

    public Persona? PersonaDB { get; set; }

    public PersonaJsonMobile? PersonaJson { get; set; }

    public string NombreVisual { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public List<string> CamposCambiados { get; set; } = new();

    // El elemento continúa existiendo en TodosItems para mantener la relación
    // con el JSON original, pero se omite al guardar definitivamente.
    public bool IgnorarImportacion { get; set; }

    // Nuevo S-21 revisado y aceptado como persona realmente nueva.
    // Se insertará dentro de la transacción final de importación.
    public bool CrearPersonaNueva { get; set; }

    // Una posible baja solo se aplica en la transacción final.
    public bool ConfirmarBaja { get; set; }

    public bool EsConflicto =>
        Tipo == TipoAnalisisJsonMobile.Conflicto;

    public bool EsNuevo =>
        Tipo == TipoAnalisisJsonMobile.Nuevo;

    public bool EsBaja =>
        Tipo == TipoAnalisisJsonMobile.Baja;

    public bool EsIgual =>
        Tipo == TipoAnalisisJsonMobile.Igual;

    public bool TieneAccion =>
        Tipo != TipoAnalisisJsonMobile.Igual;

    public string AccionTexto =>
        Tipo switch
        {
            TipoAnalisisJsonMobile.Conflicto => "Resolver",
            TipoAnalisisJsonMobile.Nuevo => "Revisar",
            TipoAnalisisJsonMobile.Baja => "Confirmar baja",
            _ => ""
        };

    public string EtiquetaOrigen =>
        Tipo switch
        {
            TipoAnalisisJsonMobile.Conflicto => "CONFLICTO",
            TipoAnalisisJsonMobile.Nuevo => "NUEVO EN S-21",
            TipoAnalisisJsonMobile.Baja => "POSIBLE BAJA",
            _ => "SIN CAMBIOS"
        };

    public Color ColorEtiqueta =>
        Tipo switch
        {
            TipoAnalisisJsonMobile.Conflicto => Color.FromArgb("#FFB900"),
            TipoAnalisisJsonMobile.Nuevo => Color.FromArgb("#55C855"),
            TipoAnalisisJsonMobile.Baja => Color.FromArgb("#FF7777"),
            _ => Color.FromArgb("#9EA4AE")
        };
}


public sealed class ResultadoImportacionJsonMobile
{
    public string CongregacionCodigo { get; set; } = "";

    public string CongregacionNombre { get; set; } = "";

    public string MesReferencia { get; set; } = "";

    public int SemanaId { get; set; }

    public List<PersonaJsonMobile> PersonasJson { get; set; } = new();

    public List<TerritorioJsonMobile>? Territorios { get; set; }

    public List<JsonImportItemMobile> TodosItems { get; set; } = new();

    public List<JsonImportItemMobile> Conflictos { get; set; } = new();

    public List<JsonImportItemMobile> Nuevos { get; set; } = new();

    public List<JsonImportItemMobile> Bajas { get; set; } = new();

    public List<JsonImportItemMobile> Iguales { get; set; } = new();

    public int TotalConflictos => Conflictos.Count;
    public int TotalNuevos => Nuevos.Count;
    public int TotalBajas => Bajas.Count;
    public int TotalIguales => Iguales.Count;

    public string CrearResumen() =>
        $"Conflictos: {TotalConflictos} · " +
        $"Nuevos: {TotalNuevos} · " +
        $"Posibles bajas: {TotalBajas} · " +
        $"Sin cambios: {TotalIguales}";
}


public sealed class CampoJsonResolucionMobile
{
    public string Clave { get; set; } = "";

    public string Etiqueta { get; set; } = "";

    public string ValorBd { get; set; } = "";

    public string ValorJson { get; set; } = "";

    public List<string> Opciones { get; } =
        ["Base de datos", "S-21"];

    // En una importación S-21, el valor S-21 es la propuesta inicial para los
    // campos que pertenecen a esa fuente. El usuario puede conservar la BD.
    public string Seleccion { get; set; } = "S-21";
}


public sealed class CandidatoVinculoJsonMobile
{
    public Persona Persona { get; set; } = new();

    public int Puntuacion { get; set; }

    public string NombreVisual =>
        !string.IsNullOrWhiteSpace(Persona.NombreComun)
            ? Persona.NombreComun
            : !string.IsNullOrWhiteSpace(Persona.Nombre)
                ? Persona.Nombre
                : Persona.NombreJWHub;

    public string Nombres =>
        "S-21: " +
        (string.IsNullOrWhiteSpace(Persona.Nombre) ? "—" : Persona.Nombre) +
        "\nJW: " +
        (string.IsNullOrWhiteSpace(Persona.NombreJWHub) ? "—" : Persona.NombreJWHub) +
        "\nComún: " +
        (string.IsNullOrWhiteSpace(Persona.NombreComun) ? "—" : Persona.NombreComun);

    public string Datos =>
        string.Join(
            " · ",
            new[]
            {
                string.IsNullOrWhiteSpace(Persona.FechaNacimiento)
                    ? ""
                    : "Nac. " + Persona.FechaNacimiento,

                string.IsNullOrWhiteSpace(Persona.FechaBautismo)
                    ? ""
                    : "Baut. " + Persona.FechaBautismo,

                Persona.Grupo > 0
                    ? $"Grupo {Persona.Grupo}"
                    : ""
            }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    public string Coincidencia =>
        $"Coincidencia estimada: {Puntuacion}%";

    public Color ColorCoincidencia =>
        Puntuacion >= 80
            ? Color.FromArgb("#72D58A")
            : Puntuacion >= 55
                ? Color.FromArgb("#FFCC66")
                : Color.FromArgb("#9EA4AE");
}


public static class JsonImportValueHelper
{
    public static string LeerTexto(JsonElement elemento)
    {
        try
        {
            return elemento.ValueKind switch
            {
                JsonValueKind.String => elemento.GetString() ?? "",
                JsonValueKind.Number => elemento.GetRawText(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                JsonValueKind.Null => "",
                JsonValueKind.Undefined => "",
                _ => elemento.ToString()
            };
        }
        catch
        {
            return "";
        }
    }


    public static bool LeerBool(JsonElement elemento)
    {
        try
        {
            switch (elemento.ValueKind)
            {
                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Number:
                    return elemento.TryGetInt32(out int numero) && numero != 0;

                case JsonValueKind.String:
                    string texto =
                        elemento.GetString()
                            ?.Trim()
                            .ToLowerInvariant()
                        ?? "";

                    return texto is
                        "1" or
                        "true" or
                        "sí" or
                        "si" or
                        "yes";

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
