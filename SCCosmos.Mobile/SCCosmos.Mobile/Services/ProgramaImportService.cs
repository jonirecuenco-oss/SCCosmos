using System.Globalization;
using System.Text.Json;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Importación del programa de la visita desde JSON, portada de
/// SCCosmos WPF (BtnImportarProgramaJson_Click). Acepta un array de
/// actividades o un objeto con "actividades"/"programa"/"eventos".
/// </summary>
public static class ProgramaImportService
{
    public sealed class ResultadoImportacionPrograma
    {
        public List<ActividadProgramaMobile> Actividades { get; } = new();

        public List<string> Avisos { get; } = new();
    }

    public static ResultadoImportacionPrograma Parsear(
        string contenidoJson,
        IReadOnlyList<Persona> personas)
    {
        var resultado =
            new ResultadoImportacionPrograma();

        using JsonDocument documento =
            JsonDocument.Parse(
                contenidoJson);

        JsonElement raiz =
            documento.RootElement;

        JsonElement listaJson;

        if (raiz.ValueKind ==
            JsonValueKind.Array)
        {
            listaJson =
                raiz;
        }
        else if (raiz.ValueKind ==
                 JsonValueKind.Object
                 &&
                 TryObtenerPropiedadJson(
                     raiz,
                     out JsonElement actividadesElement,
                     "actividades",
                     "programa",
                     "eventos"))
        {
            listaJson =
                actividadesElement;
        }
        else
        {
            throw new InvalidDataException(
                "El JSON debe ser un array de actividades o un " +
                "objeto con una propiedad 'actividades'.");
        }

        if (listaJson.ValueKind !=
            JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "La propiedad de actividades del JSON debe " +
                "contener una lista.");
        }

        int numeroRegistro = 0;

        foreach (JsonElement item in
                 listaJson.EnumerateArray())
        {
            numeroRegistro++;

            if (item.ValueKind !=
                JsonValueKind.Object)
            {
                resultado.Avisos.Add(
                    $"Registro {numeroRegistro}: no es un objeto " +
                    "y se ha omitido.");

                continue;
            }

            string dia =
                NormalizarDia(
                    LeerTextoJson(
                        item,
                        "dia",
                        "día",
                        "diaSemana",
                        "díaSemana"));

            string hora =
                LeerTextoJson(
                    item,
                    "hora");

            if (string.IsNullOrWhiteSpace(
                    dia))
            {
                resultado.Avisos.Add(
                    $"Registro {numeroRegistro}: día no válido. " +
                    "Solo se admiten miércoles, jueves, viernes, " +
                    "sábado y domingo.");

                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    hora))
            {
                resultado.Avisos.Add(
                    $"Registro {numeroRegistro} ({dia}): falta " +
                    "la hora y se ha omitido.");

                continue;
            }

            string tipo =
                NormalizarTipo(
                    LeerTextoJson(
                        item,
                        "tipoActividad",
                        "tipo",
                        "actividad"));

            if (string.IsNullOrWhiteSpace(
                    tipo))
            {
                tipo = "Otra";
            }

            string responsable =
                NormalizarResponsable(
                    LeerTextoJson(
                        item,
                        "responsable") is { Length: > 0 } r
                        ? r
                        : "SC");

            string nombreAcompanante =
                LeerTextoJson(
                    item,
                    "acompanante",
                    "acompañante",
                    "companero",
                    "compañero");

            int companeroId =
                ResolverAcompanante(
                    personas,
                    nombreAcompanante,
                    out bool ambiguo);

            if (!string.IsNullOrWhiteSpace(
                    nombreAcompanante)
                &&
                !EsTextoSinAcompanante(
                    nombreAcompanante)
                &&
                companeroId <= 0)
            {
                resultado.Avisos.Add(
                    ambiguo
                        ? $"Registro {numeroRegistro} ({dia} {hora}): " +
                          $"hay más de una coincidencia para el " +
                          $"acompañante '{nombreAcompanante}'. Se " +
                          "importó sin acompañante."
                        : $"Registro {numeroRegistro} ({dia} {hora}): " +
                          $"no se encontró el acompañante " +
                          $"'{nombreAcompanante}'. Se importó sin " +
                          "acompañante.");
            }

            resultado.Actividades.Add(
                new ActividadProgramaMobile
                {
                    DiaSemana = dia,
                    Turno = "General",
                    Responsable = responsable,
                    TipoActividad = tipo,
                    Hora = hora.Trim(),
                    Notas =
                        LeerTextoJson(
                            item,
                            "notas",
                            "nota"),
                    CompaneroId = companeroId,
                    Direccion =
                        LeerTextoJson(
                            item,
                            "direccion",
                            "dirección",
                            "ubicacion",
                            "ubicación"),
                    Latitud =
                        LeerNumeroJson(
                            item,
                            "latitud",
                            "lat"),
                    Longitud =
                        LeerNumeroJson(
                            item,
                            "longitud",
                            "lon",
                            "lng")
                });
        }

        return resultado;
    }

    private static bool TryObtenerPropiedadJson(
        JsonElement elemento,
        out JsonElement valor,
        params string[] nombres)
    {
        if (elemento.ValueKind ==
            JsonValueKind.Object)
        {
            foreach (JsonProperty propiedad in
                     elemento.EnumerateObject())
            {
                if (nombres.Any(n =>
                        string.Equals(
                            propiedad.Name,
                            n,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    valor = propiedad.Value;
                    return true;
                }
            }
        }

        valor = default;
        return false;
    }

    private static string LeerTextoJson(
        JsonElement elemento,
        params string[] nombres)
    {
        if (!TryObtenerPropiedadJson(
                elemento,
                out JsonElement valor,
                nombres))
        {
            return "";
        }

        if (valor.ValueKind ==
            JsonValueKind.String)
        {
            return valor.GetString()?.Trim() ?? "";
        }

        if (valor.ValueKind ==
                JsonValueKind.Null
            ||
            valor.ValueKind ==
                JsonValueKind.Undefined)
        {
            return "";
        }

        return valor.ToString().Trim();
    }

    private static double LeerNumeroJson(
        JsonElement elemento,
        params string[] nombres)
    {
        if (!TryObtenerPropiedadJson(
                elemento,
                out JsonElement valor,
                nombres))
        {
            return 0;
        }

        if (valor.ValueKind ==
                JsonValueKind.Number
            &&
            valor.TryGetDouble(
                out double numero))
        {
            return numero;
        }

        string texto =
            valor.ValueKind ==
                JsonValueKind.String
                ? valor.GetString()
                : valor.ToString();

        if (double.TryParse(
                (texto ?? "").Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double resultado))
        {
            return resultado;
        }

        return 0;
    }

    private static string NormalizarDia(
        string dia)
    {
        switch ((dia ?? "").Trim().ToLowerInvariant())
        {
            case "miércoles":
            case "miercoles":
                return "Miércoles";

            case "jueves":
                return "Jueves";

            case "viernes":
                return "Viernes";

            case "sábado":
            case "sabado":
                return "Sábado";

            case "domingo":
                return "Domingo";

            default:
                return "";
        }
    }

    private static string NormalizarTipo(
        string tipo)
    {
        string valor =
            (tipo ?? "").Trim();

        if (string.IsNullOrWhiteSpace(
                valor))
        {
            return "";
        }

        string minusculas =
            valor.ToLowerInvariant();

        return minusculas switch
        {
            "rsc" => "RSC",
            "predicación" or "predicacion" => "Predicación",
            "visita de pastoreo" or "pastoreo" => "Visita de pastoreo",
            "comida" => "Comida",
            "alojamiento" => "Alojamiento",
            "otra" or "otro" => "Otra",
            _ => valor
        };
    }

    private static string NormalizarResponsable(
        string? responsable)
    {
        string valor =
            responsable?.Trim() ?? "";

        if (string.Equals(
                valor,
                "Ambos",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Ambos";
        }

        if (string.Equals(
                valor,
                "Rebe",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                valor,
                "Esposa",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Esposa";
        }

        return "SC";
    }

    private static bool EsTextoSinAcompanante(
        string texto)
    {
        string valor =
            (texto ?? "").Trim();

        return
            string.IsNullOrWhiteSpace(valor)
            || valor.Equals(
                "sin acompañante",
                StringComparison.OrdinalIgnoreCase)
            || valor.Equals(
                "sin acompanante",
                StringComparison.OrdinalIgnoreCase)
            || valor.Equals(
                "ninguno",
                StringComparison.OrdinalIgnoreCase)
            || valor.Equals(
                "-",
                StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolverAcompanante(
        IReadOnlyList<Persona> personas,
        string nombre,
        out bool ambiguo)
    {
        ambiguo = false;

        if (EsTextoSinAcompanante(
                nombre))
        {
            return 0;
        }

        List<int> coincidencias =
            personas
                .Where(p =>
                    TextoCoincide(
                        nombre,
                        p.NombreComun)
                    ||
                    TextoCoincide(
                        nombre,
                        p.NombreJWHub)
                    ||
                    TextoCoincide(
                        nombre,
                        p.Nombre))
                .Select(p =>
                    p.PersonaRaizId > 0
                        ? p.PersonaRaizId
                        : p.Id)
                .Distinct()
                .ToList();

        if (coincidencias.Count == 1)
        {
            return coincidencias[0];
        }

        ambiguo =
            coincidencias.Count > 1;

        return 0;
    }

    private static bool TextoCoincide(
        string a,
        string b)
    {
        if (string.IsNullOrWhiteSpace(a)
            ||
            string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return
            CultureInfo
                .GetCultureInfo("es-ES")
                .CompareInfo
                .Compare(
                    a.Trim(),
                    b.Trim(),
                    CompareOptions.IgnoreCase
                    | CompareOptions.IgnoreNonSpace)
            == 0;
    }
}
