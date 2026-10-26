using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

using Path = System.IO.Path;

namespace SCCosmos.Sincro;

// ============================================================
// CONVERSOR DE REGISTROS DE TERRITORIOS (S-13)
//
// Lee el formulario oficial «Registro de asignación de territorio»
// (S-13-S) sea cual sea el reparto de sus columnas, los registros con
// un territorio por página y los tableros de control en Excel.
//
// De cada territorio se queda la ÚLTIMA asignación.
// ============================================================

public sealed class TerritorioS13
{
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "";

    [JsonPropertyName("ultima_fecha_completado")]
    public string UltimaFechaCompletado { get; set; } = "";

    [JsonPropertyName("asignado_a")]
    public string AsignadoA { get; set; } = "";

    [JsonPropertyName("fecha_asignado")]
    public string FechaAsignado { get; set; } = "";

    [JsonPropertyName("fecha_completado")]
    public string FechaCompletado { get; set; } = "";

    [JsonIgnore]
    public string Area { get; set; } = "";

    [JsonIgnore]
    public string Localidad { get; set; } = "";
}


public sealed class ResultadoConversionS13
{
    public List<TerritorioS13> Territorios { get; set; } = new();

    public List<string> Archivos { get; set; } = new();

    public List<OmitidoS13> Omitidos { get; set; } = new();

    public int Paginas { get; set; }
}


public sealed class OmitidoS13
{
    public string Archivo { get; set; } = "";
    public string Motivo { get; set; } = "";
}


public static partial class ConvertidorS13
{
    // ========================================================
    // FECHAS
    // ========================================================

    private static readonly Dictionary<string, int> MesesEs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ene"] = 1, ["feb"] = 2, ["mar"] = 3, ["abr"] = 4,
            ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["ago"] = 8,
            ["sep"] = 9, ["sept"] = 9, ["set"] = 9, ["oct"] = 10,
            ["nov"] = 11, ["dic"] = 12,
        };

    private static readonly Regex FechaNumerica = new(
        @"^\d{1,2}\s*[/\-.]\s*\d{1,2}\s*[/\-.]\s*\d{2,4}$");

    private static readonly Regex FechaMesAnio = new(
        @"^(\d{1,2})\s*[/\-.]\s*(\d{2}|\d{4})$");

    private static readonly Regex FechaTexto = new(
        @"^(\d{1,2})\s*[-\s]\s*([a-zA-Záéíóúñ]{3,10})\.?\s*[-\s,]\s*(\d{2,4})$",
        RegexOptions.IgnoreCase);


    private static int AnioCompleto(int anio)
    {
        if (anio < 100)
        {
            return anio <= 40 ? 2000 + anio : 1900 + anio;
        }

        // Erratas habituales: 3025 en lugar de 2025.
        return anio > 2100 ? anio - 1000 : anio;
    }


    public static bool EsFecha(string texto)
    {
        string t = (texto ?? "").Trim().TrimEnd('*');

        if (t.Length == 0)
        {
            return false;
        }

        return FechaNumerica.IsMatch(t)
            || FechaMesAnio.IsMatch(t)
            || FechaTexto.IsMatch(t);
    }


    public static string NormalizarFecha(string texto)
    {
        string t = (texto ?? "").Trim().TrimEnd('*');

        if (t.Length == 0)
        {
            return "";
        }

        if (FechaNumerica.IsMatch(t))
        {
            var partes = Regex.Split(t, @"\s*[/\-.]\s*");

            if (partes.Length == 3
                && int.TryParse(partes[0], out int dia)
                && int.TryParse(partes[1], out int mes)
                && int.TryParse(partes[2], out int anio))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}/{1:00}/{2:0000}",
                    dia,
                    mes,
                    AnioCompleto(anio));
            }
        }

        var m = FechaTexto.Match(t);

        if (m.Success
            && int.TryParse(m.Groups[1].Value, out int diaTexto)
            && int.TryParse(m.Groups[3].Value, out int anioTexto))
        {
            string nombreMes = m.Groups[2].Value.ToLowerInvariant();

            if (MesesEs.TryGetValue(nombreMes, out int mes))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}/{1:00}/{2:0000}",
                    diaTexto,
                    mes,
                    AnioCompleto(anioTexto));
            }
        }

        var mm = FechaMesAnio.Match(t);

        if (mm.Success
            && int.TryParse(mm.Groups[1].Value, out int mesNum)
            && int.TryParse(mm.Groups[2].Value, out int anioNum))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}/{1:0000}",
                mesNum,
                AnioCompleto(anioNum));
        }

        return t;
    }


    private static int ClaveFecha(string texto)
    {
        string normal = NormalizarFecha(texto);

        var m = Regex.Match(normal, @"^(\d{2})/(\d{2})/(\d{4})$");

        if (m.Success)
        {
            return int.Parse(m.Groups[3].Value) * 10000
                + int.Parse(m.Groups[2].Value) * 100
                + int.Parse(m.Groups[1].Value);
        }

        m = Regex.Match(normal, @"^(\d{2})/(\d{4})$");

        if (m.Success)
        {
            return int.Parse(m.Groups[2].Value) * 10000
                + int.Parse(m.Groups[1].Value) * 100;
        }

        return 0;
    }


    private static string Plano(string texto)
    {
        string descompuesto = (texto ?? "").ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder();

        foreach (char c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c)
                != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }


    private static readonly HashSet<string> Etiquetas = new(StringComparer.OrdinalIgnoreCase)
    {
        "entregado", "devuelto", "fecha", "asignado", "asignó", "asigno",
        "completó", "completo", "completado", "completa", "el:", "el",
        "en", "que", "se", "de", "del", "territorio", "terr.", "num.",
        "última", "ultima", "año", "ano", "servicio", "asignación",
        "registro", "aún", "aun", "no", "pendiente", "sin", "asignar",
        "devuelta", "entregada", "ene", "feb", "mar", "abr", "may",
        "jun", "jul", "ago", "sep", "sept", "set", "oct", "nov", "dic",
    };


    private static bool EsNombre(string texto)
    {
        string t = (texto ?? "").Trim();

        if (t.Length == 0 || EsFecha(t))
        {
            return false;
        }

        if (Regex.IsMatch(t, @"^[\d\W_]+$"))
        {
            return false;
        }

        if (Etiquetas.Contains(Plano(t).Trim(':', '.')))
        {
            return false;
        }

        return Regex.IsMatch(t, "[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]");
    }


    // ========================================================
    // API
    // ========================================================

    public static ResultadoConversionS13 ConvertirCarpeta(string carpeta)
    {
        var resultado = new ResultadoConversionS13();

        if (!Directory.Exists(carpeta))
        {
            return resultado;
        }

        var archivos = Directory
            .EnumerateFiles(carpeta, "*", SearchOption.AllDirectories)
            .Where(EsArchivoDeTerritorios)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        resultado.Archivos = archivos
            .Select(Path.GetFileName)
            .ToList();

        var territorios = new Dictionary<string, TerritorioS13>(
            StringComparer.OrdinalIgnoreCase);

        var orden = new List<string>();

        foreach (string ruta in archivos)
        {
            List<TerritorioS13> lista;

            try
            {
                lista = ConvertirArchivo(ruta);
            }
            catch (Exception ex)
            {
                resultado.Omitidos.Add(new OmitidoS13
                {
                    Archivo = Path.GetFileName(ruta),
                    Motivo = "no se pudo leer (" + ex.Message + ")",
                });

                continue;
            }

            if (lista.Count == 0)
            {
                resultado.Omitidos.Add(new OmitidoS13
                {
                    Archivo = Path.GetFileName(ruta),
                    Motivo = "no se reconoce el registro de territorios",
                });

                continue;
            }

            foreach (var territorio in lista)
            {
                Fusionar(territorios, orden, territorio);
            }
        }

        resultado.Territorios = ComponerNombres(territorios, orden);
        return resultado;
    }


    public static List<TerritorioS13> ConvertirArchivo(string ruta)
    {
        string extension = Path.GetExtension(ruta).ToLowerInvariant();

        if (extension == ".xlsx" || extension == ".xlsm")
        {
            return ConvertirExcel(ruta);
        }

        if (extension == ".pdf")
        {
            return ConvertirPdf(ruta);
        }

        return new List<TerritorioS13>();
    }


    /// <summary>¿El nombre del archivo sugiere un registro de territorios?</summary>
    public static bool EsArchivoDeTerritorios(string ruta)
    {
        string nombre = Path.GetFileName(ruta).ToLowerInvariant();
        string extension = Path.GetExtension(nombre);

        if (extension != ".pdf" && extension != ".xlsx" && extension != ".xlsm")
        {
            return false;
        }

        if (nombre.Contains("territorios para") || nombre.Contains("para predicar"))
        {
            return false;
        }

        return nombre.Contains("s-13")
            || nombre.Contains("s13")
            || nombre.Contains("territor");
    }


    private static void Fusionar(
        Dictionary<string, TerritorioS13> territorios,
        List<string> orden,
        TerritorioS13 nuevo)
    {
        string clave = (nuevo.Area ?? "").Trim() + "|" + nuevo.Nombre;

        if (!territorios.TryGetValue(clave, out var anterior))
        {
            territorios[clave] = nuevo;
            orden.Add(clave);
            return;
        }

        if (ClaveFecha(nuevo.FechaAsignado) > ClaveFecha(anterior.FechaAsignado))
        {
            string ultima = anterior.UltimaFechaCompletado;

            territorios[clave] = nuevo;

            if (ClaveFecha(ultima) > ClaveFecha(nuevo.UltimaFechaCompletado))
            {
                nuevo.UltimaFechaCompletado = ultima;
            }
        }
        else if (ClaveFecha(nuevo.UltimaFechaCompletado)
                 > ClaveFecha(anterior.UltimaFechaCompletado))
        {
            anterior.UltimaFechaCompletado = nuevo.UltimaFechaCompletado;
        }

        if (string.IsNullOrWhiteSpace(territorios[clave].AsignadoA)
            && !string.IsNullOrWhiteSpace(anterior.AsignadoA))
        {
            territorios[clave].AsignadoA = anterior.AsignadoA;
        }
    }


    private static List<TerritorioS13> ComponerNombres(
        Dictionary<string, TerritorioS13> territorios,
        List<string> orden)
    {
        var areas = new HashSet<string>(
            territorios.Values
                .Select(t => (t.Area ?? "").Trim())
                .Where(a => a.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var localidades = new HashSet<string>(
            territorios.Values
                .Select(t => (t.Localidad ?? "").Trim())
                .Where(l => l.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var resultado = new List<TerritorioS13>();

        foreach (string clave in orden)
        {
            var t = territorios[clave];
            string numero = t.Nombre;

            var partes = new List<string>();

            if (t.Localidad.Length > 0 && localidades.Count > 1)
            {
                partes.Add(t.Localidad);
            }

            if (t.Area.Length > 0 && areas.Count > 1)
            {
                partes.Add(t.Area);
            }

            partes.Add(numero);

            t.Nombre = string.Join(" · ", partes);
            resultado.Add(t);
        }

        return resultado;
    }


    public static string GenerarJson(List<TerritorioS13> territorios)
    {
        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                .UnsafeRelaxedJsonEscaping,
        };

        return JsonSerializer.Serialize(territorios, opciones);
    }


    /// <summary>Guarda el JSON en la carpeta (con copia .bak).</summary>
    public static string GuardarJson(
        List<TerritorioS13> territorios,
        string carpeta,
        string nombreArchivo = "datos_s13.json")
    {
        string ruta = Path.Combine(carpeta, nombreArchivo);

        if (File.Exists(ruta))
        {
            try
            {
                File.Copy(ruta, ruta + ".bak", true);
            }
            catch
            {
            }
        }

        File.WriteAllText(
            ruta,
            GenerarJson(territorios),
            new UTF8Encoding(false));

        return ruta;
    }
}
