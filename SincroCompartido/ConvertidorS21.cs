using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;

using Path = System.IO.Path;

namespace SCCosmos.Sincro;

// ============================================================
// CONVERSOR DE TARJETAS S-21 (PDF) AL JSON QUE IMPORTA SCCOSMOS
//
// Soporta tres formatos de tarjeta:
//   1. PDF rellenable (campos de formulario 900_1, 901_20...).
//   2. PDF impreso «Últimos 12 Meses» (casillas vectoriales).
//   3. PDF impreso «Registro de publicador» (casillas vectoriales
//      o glifos; una o dos secciones de año).
//
// Es el mismo motor que la herramienta de escritorio
// (Herramientas\convertir_s21.py), portado a C# con iText para
// que funcione dentro de la app en Windows y Android.
// ============================================================

public sealed class InformeS21
{
    [JsonPropertyName("mes")]
    public string Mes { get; set; } = "";

    [JsonPropertyName("participacion")]
    public bool Participacion { get; set; }

    [JsonPropertyName("cursos_biblicos")]
    public object CursosBiblicos { get; set; } = "";

    [JsonPropertyName("precursor_auxiliar")]
    public bool PrecursorAuxiliar { get; set; }

    [JsonPropertyName("horas")]
    public object Horas { get; set; } = "";

    [JsonPropertyName("notas")]
    public string Notas { get; set; } = "";
}


public sealed class TarjetaS21
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
    public List<InformeS21> Informe { get; set; } = new();
}


public sealed class OmitidoS21
{
    public string Archivo { get; set; } = "";
    public string Motivo { get; set; } = "";
}


public sealed class ResultadoConversionS21
{
    public List<TarjetaS21> Tarjetas { get; set; } = new();
    public int Repetidas { get; set; }
    public int ArchivosPdf { get; set; }
    public List<OmitidoS21> Omitidos { get; set; } = new();
}


public static class ConvertidorS21
{
    private static readonly string[] Meses =
    {
        "septiembre", "octubre", "noviembre", "diciembre",
        "enero", "febrero", "marzo", "abril",
        "mayo", "junio", "julio", "agosto",
    };

    private static readonly Dictionary<string, int> MesesAbrev =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ene"] = 1, ["feb"] = 2, ["mar"] = 3, ["abr"] = 4,
            ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["ago"] = 8,
            ["sep"] = 9, ["sept"] = 9, ["oct"] = 10, ["nov"] = 11,
            ["dic"] = 12,
        };


    // ========================================================
    // API PÚBLICA
    // ========================================================

    /// <summary>Año del ciclo de servicio actual (o el indicado).</summary>
    public static int AnioCicloActual(DateTime? fecha = null)
    {
        var hoy = fecha ?? DateTime.Today;
        return hoy.Month >= 9 ? hoy.Year : hoy.Year - 1;
    }


    public static ResultadoConversionS21 ConvertirCarpeta(
        string carpeta,
        int anioServicio)
    {
        var resultado = new ResultadoConversionS21();

        if (!Directory.Exists(carpeta))
        {
            return resultado;
        }

        var archivos = Directory
            .EnumerateFiles(carpeta, "*.pdf", SearchOption.AllDirectories)
            .ToList();

        archivos.Sort(CompararArchivos);

        resultado.ArchivosPdf = archivos.Count;

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string ruta in archivos)
        {
            List<TarjetaS21> tarjetas;

            try
            {
                tarjetas = ConvertirArchivo(ruta, anioServicio);
            }
            catch (Exception ex)
            {
                resultado.Omitidos.Add(new OmitidoS21
                {
                    Archivo = Path.GetFileName(ruta),
                    Motivo = "no se pudo leer (" + ex.Message + ")",
                });

                continue;
            }

            if (tarjetas.Count == 0)
            {
                resultado.Omitidos.Add(new OmitidoS21
                {
                    Archivo = Path.GetFileName(ruta),
                    Motivo = "no es una tarjeta S-21 reconocible",
                });

                continue;
            }

            foreach (var tarjeta in tarjetas)
            {
                // Una tarjeta por persona y AÑO DE SERVICIO: la misma
                // persona puede tener actividad en varios años (por
                // ejemplo, los PDF de 2026 y de 2027). Solo se descartan
                // los duplicados de la misma persona en el mismo año.
                string clave = ClaveNombre(tarjeta.Nombre)
                    + "|"
                    + (tarjeta.AnioServicio ?? "").Trim();

                if (!vistos.Add(clave))
                {
                    resultado.Repetidas++;
                    continue;
                }

                resultado.Tarjetas.Add(tarjeta);
            }
        }

        return resultado;
    }


    /// <summary>Convierte un solo PDF (una o varias tarjetas).</summary>
    public static List<TarjetaS21> ConvertirArchivo(
        string ruta,
        int anioServicio)
    {
        var lista = new List<TarjetaS21>();

        // Si el nombre del archivo indica el año (S-21_2027, S-21 2026...),
        // se usa para las tarjetas que no lo traen impreso.
        int anioArchivo = AnioDelNombre(Path.GetFileName(ruta));

        if (anioArchivo > 0)
        {
            anioServicio = anioArchivo;
        }

        // 1) Intento en solo lectura (rápido): formulario rellenable.
        bool hayAnotaciones = false;

        using (var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>())))
        using (var documento = new PdfDocument(lector))
        {
            var desdeFormulario = TarjetaDeFormulario(documento);

            if (desdeFormulario != null)
            {
                desdeFormulario.Grupo = GrupoDeRuta(ruta);
                lista.Add(desdeFormulario);
                return lista;
            }

            for (int i = 1; i <= documento.GetNumberOfPages(); i++)
            {
                try
                {
                    var anotaciones = documento
                        .GetPage(i)
                        .GetAnnotations();

                    if (anotaciones != null && anotaciones.Count > 0)
                    {
                        hayAnotaciones = true;
                        break;
                    }
                }
                catch
                {
                }
            }

            if (!hayAnotaciones)
            {
                ProcesarPaginas(
                    documento,
                    ruta,
                    anioServicio,
                    lista,
                    null,
                    null,
                    null);

                return lista;
            }
        }

        // 2) Con widgets: se reabre en modo escritura (en memoria) para
        // dibujar las apariencias y poder leer los valores.
        using (var salida = new MemoryStream())
        using (var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>())))
        using (var documento = new PdfDocument(
            lector,
            new PdfWriter(salida)))
        {
            var aparienciasPorPagina = new Dictionary<int, byte[]>();
            var valoresPorPagina =
                new Dictionary<int, List<CaracterPdf>>();
            var marcasPorPagina =
                new Dictionary<int, List<TrazoPdf>>();

            for (int i = 1; i <= documento.GetNumberOfPages(); i++)
            {
                var pagina = documento.GetPage(i);

                byte[] apariencias = null;

                try
                {
                    apariencias = DibujarApariencias(
                        pagina,
                        documento);
                }
                catch
                {
                }

                if (apariencias != null)
                {
                    aparienciasPorPagina[i] = apariencias;
                }

                try
                {
                    var valores = LeerValoresCampos(pagina);

                    if (valores.Count > 0)
                    {
                        valoresPorPagina[i] = valores;
                    }
                }
                catch
                {
                }

                try
                {
                    var marcas = LeerMarcasCampos(pagina);

                    if (marcas.Count > 0)
                    {
                        marcasPorPagina[i] = marcas;
                    }
                }
                catch
                {
                }
            }

            ProcesarPaginas(
                documento,
                ruta,
                anioServicio,
                lista,
                aparienciasPorPagina,
                valoresPorPagina,
                marcasPorPagina);
        }

        return lista;
    }


    private static void ProcesarPaginas(
        PdfDocument documento,
        string ruta,
        int anioServicio,
        List<TarjetaS21> lista,
        Dictionary<int, byte[]> aparienciasPorPagina,
        Dictionary<int, List<CaracterPdf>> valoresPorPagina,
        Dictionary<int, List<TrazoPdf>> marcasPorPagina)
    {
        for (int i = 1; i <= documento.GetNumberOfPages(); i++)
        {
            var pagina = documento.GetPage(i);

            byte[] extra = null;

            aparienciasPorPagina?.TryGetValue(i, out extra);

            List<CaracterPdf> sinteticos = null;

            valoresPorPagina?.TryGetValue(i, out sinteticos);

            List<TrazoPdf> marcasSinteticas = null;

            marcasPorPagina?.TryGetValue(i, out marcasSinteticas);

            var contenido = LeerContenido(
                pagina,
                extra,
                sinteticos,
                marcasSinteticas);

            var impresa = TarjetaImpresa(contenido, anioServicio);

            if (impresa == null)
            {
                impresa = TarjetaAntigua(contenido, anioServicio);
            }

            if (impresa != null)
            {
                int grupo = GrupoDeRuta(ruta);

                if (grupo > 0)
                {
                    impresa.Grupo = grupo;
                }

                lista.Add(impresa);
            }
        }
    }


    /// <summary>Guarda el JSON junto a las tarjetas (con copia .bak).</summary>
    public static string GuardarJson(
        List<TarjetaS21> tarjetas,
        string carpeta,
        string nombreArchivo = "datos_s21.json")
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

        File.WriteAllText(ruta, GenerarJson(tarjetas), new UTF8Encoding(false));

        return ruta;
    }


    public static string GenerarJson(List<TarjetaS21> tarjetas)
    {
        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        return JsonSerializer.Serialize(tarjetas, opciones);
    }


    /// <summary>Saca un volcado de lo leído en una página (diagnóstico).</summary>
    public static string DepurarPagina(
        string ruta,
        int numeroPagina = 1)
    {
        using var salida = new MemoryStream();
        using var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>()));
        using var documento = new PdfDocument(
            lector,
            new PdfWriter(salida));

        var paginaDepurar = documento.GetPage(numeroPagina);

        byte[] aparienciasDepurar = null;

        try
        {
            aparienciasDepurar = DibujarApariencias(
                paginaDepurar,
                documento);
        }
        catch (Exception ex)
        {
            UltimoDiagnosticoApariencias =
                "EXCEPCION: " + ex.GetType().Name + ": " + ex.Message;
        }

        var contenido = LeerContenido(
            paginaDepurar,
            aparienciasDepurar,
            LeerValoresCampos(paginaDepurar),
            LeerMarcasCampos(paginaDepurar));

        var texto = new StringBuilder();

        texto.AppendLine("Palabras: " + contenido.Palabras.Count);
        texto.AppendLine("Glifos caja: " + contenido.GlifosCaja.Count
            + " | Glifos marca: " + contenido.GlifosMarca.Count);
        texto.AppendLine("Trazos: " + contenido.Trazos.Count);
        texto.AppendLine("CTM ejemplo: " + contenido.CtmEjemplo);

        var anotacionesDepurar = paginaDepurar.GetAnnotations();

        texto.AppendLine("Anotaciones: "
            + (anotacionesDepurar?.Count ?? 0));

        if (anotacionesDepurar != null && anotacionesDepurar.Count > 0)
        {
            int conApariencia = 0;
            int streamsDirectos = 0;

            foreach (var anotacion in anotacionesDepurar)
            {
                try
                {
                    var ap = anotacion.GetNormalAppearanceObject();

                    if (ap != null)
                    {
                        conApariencia++;

                        if (ap is PdfStream)
                        {
                            streamsDirectos++;
                        }
                    }
                }
                catch
                {
                }
            }

            texto.AppendLine("Con apariencia: " + conApariencia
                + " | streams directos: " + streamsDirectos);

            texto.AppendLine("Aplanado: " + UltimoDiagnosticoApariencias);

        texto.AppendLine("Glifos marca pos: " + string.Join(" ", contenido.GlifosMarca
            .Take(10)
            .Select(g => string.Format(CultureInfo.InvariantCulture, "({0:0.0},{1:0.0})", g.X, g.Y))));

        texto.AppendLine("Glifos caja pos: " + string.Join(" ", contenido.GlifosCaja
            .Take(10)
            .Select(g => string.Format(CultureInfo.InvariantCulture, "({0:0.0},{1:0.0})", g.X, g.Y))));

        texto.AppendLine("Fuentes: " + string.Join(
            " | ",
            contenido.Caracteres
                .GroupBy(c => c.Fuente)
                .OrderByDescending(g => g.Count())
                .Take(12)
                .Select(g => (g.Key.Length == 0 ? "(vacio)" : g.Key)
                    + "=" + g.Count())));

            try
            {
                var primera = anotacionesDepurar[0];

                texto.AppendLine("Subtype: " + primera.GetSubtype()
                    + " | estado: " + primera.GetAppearanceState());

                var ap = primera.GetNormalAppearanceObject();

                texto.AppendLine("Tipo de /N: "
                    + (ap == null ? "null" : ap.GetType().Name));

                if (ap != null && !(ap is PdfStream))
                {
                    texto.AppendLine("Claves /N: " + string.Join(
                        ",",
                        ap.KeySet().Select(k => k.ToString())));
                }
            }
            catch (Exception ex)
            {
                texto.AppendLine("Error inspeccion: " + ex.Message);
            }
        }

        foreach (string crudo in contenido.TrazosCrudos)
        {
            texto.AppendLine("  " + crudo);
        }

        foreach (var palabra in contenido.Palabras.Take(200))
        {
            texto.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  x={0,7:0.0} y={1,7:0.0}  {2}",
                palabra.X0,
                palabra.CentroY,
                palabra.Texto));
        }

        texto.AppendLine("Caracteres de la primera linea con ancho iText:");

        var primeraLinea = contenido.Palabras
            .Where(p => p.CentroY > 200 && p.X0 < 30)
            .OrderBy(p => p.CentroY)
            .FirstOrDefault()
            ?? contenido.Palabras
                .FirstOrDefault(p => p.Texto.StartsWith(
                    "REGISTRO",
                    StringComparison.OrdinalIgnoreCase))
            ?? contenido.Palabras.OrderBy(p => p.CentroY).FirstOrDefault();

        if (primeraLinea != null)
        {
            foreach (var caracter in contenido.Caracteres
                .Where(c => Math.Abs(c.Y - primeraLinea.CentroY) < 3)
                .OrderBy(c => c.X)
                .Take(30))
            {
                texto.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  '{0}' x={1:0.0} anchoItext={2:0.00} tam={3:0.0} escala={4:0.00} ancho={5:0.00}",
                    caracter.Texto,
                    caracter.X,
                    caracter.AnchoItext,
                    caracter.FuenteTamano,
                    caracter.Escala,
                    caracter.Ancho));
            }
        }

        foreach (var trazo in contenido.Trazos.Take(12))
        {
            texto.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  trazo ({0:0.0},{1:0.0}) -> ({2:0.0},{3:0.0})",
                trazo.X0, trazo.Y0, trazo.X1, trazo.Y1));
        }

        return texto.ToString();
    }


    // ========================================================
    // FORMATO 1: FORMULARIO RELLENABLE
    // ========================================================

    private static TarjetaS21 TarjetaDeFormulario(
        PdfDocument documento)
    {
        var formulario = PdfAcroForm.GetAcroForm(documento, false);

        var campos = formulario?.GetAllFormFields();

        if (campos == null || campos.Count == 0)
        {
            return null;
        }

        if (!campos.Keys.Any(k => k.StartsWith("900_")))
        {
            return null;
        }

        // El S-303 y otros impresos comparten nombres de campo: se
        // exigen también las fechas de una tarjeta real.
        if (!campos.ContainsKey("900_2_Text_SanSerif")
            && !campos.ContainsKey("900_5_Text_SanSerif"))
        {
            return null;
        }

        string nombre = LimpiarEspacios(Valor(campos, "900_1_Text_SanSerif"));

        if (!NombreValido(nombre))
        {
            return null;
        }

        string anio = Valor(campos, "900_13_Text_C_SanSerif").Trim();

        var rango = Regex.Match(anio, @"^\s*(20\d{2})\s*/\s*(\d{2,4})\s*$");

        string anioJson;

        if (rango.Success)
        {
            string final = rango.Groups[2].Value;
            anioJson = final.Length == 2 ? "20" + final : final;
        }
        else
        {
            anioJson = anio.Length == 4 && anio.All(char.IsDigit)
                ? (int.Parse(anio) + 1).ToString(CultureInfo.InvariantCulture)
                : anio;
        }

        var informe = new List<InformeS21>();

        for (int i = 0; i < Meses.Length; i++)
        {
            int fila = 20 + i;

            informe.Add(new InformeS21
            {
                Mes = Meses[i],
                Participacion = Marcado(campos, "901_" + fila + "_CheckBox"),
                CursosBiblicos = Numero(Valor(campos, "902_" + fila + "_Text_C_SanSerif")),
                PrecursorAuxiliar = Marcado(campos, "903_" + fila + "_CheckBox"),
                Horas = Numero(Valor(campos, "904_" + fila + "_S21_Value")),
                Notas = LimpiarEspacios(Valor(campos, "905_" + fila + "_Text_SanSerif")),
            });
        }

        return new TarjetaS21
        {
            Nombre = nombre,
            FechaNacimiento = FechaFormulario(Valor(campos, "900_2_Text_SanSerif")),
            Sexo = Marcado(campos, "900_3_CheckBox") ? "Hombre"
                : Marcado(campos, "900_4_CheckBox") ? "Mujer" : "",
            FechaBautismo = FechaFormulario(Valor(campos, "900_5_Text_SanSerif")),
            Esperanza = Marcado(campos, "900_6_CheckBox") ? "Otras ovejas"
                : Marcado(campos, "900_7_CheckBox") ? "Ungido" : "",
            Nombramiento = Marcado(campos, "900_8_CheckBox") ? "Anciano"
                : Marcado(campos, "900_9_CheckBox") ? "Siervo ministerial" : "",
            Servicio = Marcado(campos, "900_10_CheckBox") ? "Precursor regular"
                : Marcado(campos, "900_11_CheckBox") ? "Precursor especial"
                : Marcado(campos, "900_12_CheckBox") ? "Misionero" : "",
            AnioServicio = anioJson,
            Informe = informe,
        };
    }


    private static string Valor(
        IDictionary<string, PdfFormField> campos,
        string nombre)
    {
        if (!campos.TryGetValue(nombre, out var campo) || campo == null)
        {
            return "";
        }

        try
        {
            string valor = campo.GetValueAsString() ?? "";
            valor = valor.TrimStart('/');

            if (valor.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return valor;
        }
        catch
        {
            return "";
        }
    }


    private static bool Marcado(
        IDictionary<string, PdfFormField> campos,
        string nombre)
    {
        return Valor(campos, nombre)
            .Equals("Yes", StringComparison.OrdinalIgnoreCase);
    }


    // ========================================================
    // LECTURA DEL CONTENIDO (palabras, glifos y trazos)
    // ========================================================

    private sealed class CaracterPdf
    {
        public string Texto = "";
        public float X;
        public float Y;
        public float Ancho;
        public float AnchoItext;
        public float Escala;
        public float FuenteTamano;
        public string Fuente = "";
    }

    private sealed class TrazoPdf
    {
        public float X0;
        public float Y0;
        public float X1;
        public float Y1;

        public float Ancho => Math.Abs(X1 - X0);
        public float Alto => Math.Abs(Y1 - Y0);
    }

    private sealed class PalabraPdf
    {
        public string Texto = "";
        public float X0;
        public float Y0;
        public float X1;
        public float Y1;

        public float CentroY => (Y0 + Y1) / 2;
    }

    private sealed class ContenidoPagina
    {
        public List<PalabraPdf> Palabras = new();
        public List<CaracterPdf> Caracteres = new();
        public List<CaracterPdf> GlifosCaja = new();
        public List<CaracterPdf> GlifosMarca = new();
        public List<TrazoPdf> Trazos = new();
        public string CtmEjemplo = "";
        public List<string> TrazosCrudos = new();
    }

    private sealed class Recolector : IEventListener
    {
        private readonly List<CaracterPdf> _caracteres = new();
        private readonly List<TrazoPdf> _trazos = new();

        public string CtmEjemplo = "";
        public List<string> TrazosCrudos = new();

        public void EventOccurred(
            IEventData data,
            EventType type)
        {
            if (type == EventType.RENDER_TEXT)
            {
                RecolectarTexto((TextRenderInfo)data);
            }
            else if (type == EventType.RENDER_PATH)
            {
                RecolectarTrazo((PathRenderInfo)data);
            }
        }

        private void RecolectarTexto(TextRenderInfo info)
        {
            IList<TextRenderInfo> caracteres;

            try
            {
                caracteres = info.GetCharacterRenderInfos();
            }
            catch
            {
                return;
            }

            string fuente = "";

            try
            {
                fuente = info.GetFont()?
                    .GetFontProgram()?
                    .GetFontNames()?
                    .GetFontName()
                    ?? "";
            }
            catch
            {
            }

            float tamano = 10f;

            try
            {
                tamano = info.GetFontSize();
            }
            catch
            {
            }

            // Posiciones por carácter y ancho aproximado (diferencia con
            // el carácter siguiente del mismo bloque).
            var delBloque = new List<CaracterPdf>();

            foreach (var caracter in caracteres)
            {
                try
                {
                    string texto = caracter.GetText();

                    if (string.IsNullOrEmpty(texto))
                    {
                        continue;
                    }

                    var punto = caracter
                        .GetBaseline()
                        .GetStartPoint();

                    delBloque.Add(new CaracterPdf
                    {
                        Texto = texto,
                        X = punto.Get(0),
                        Y = punto.Get(1),
                        Ancho = Math.Max(0.5f, tamano * 0.5f),
                        AnchoItext = caracter.GetUnscaledWidth(),
                        FuenteTamano = tamano,
                        Fuente = fuente,
                    });
                }
                catch
                {
                }
            }

            // Escala por par de caracteres consecutivos del mismo bloque:
            // avance en página / ancho sin escalar del glifo.
            for (int i = 0; i + 1 < delBloque.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(delBloque[i].Texto)
                    || string.IsNullOrWhiteSpace(delBloque[i + 1].Texto))
                {
                    continue;
                }

                float avance = delBloque[i + 1].X - delBloque[i].X;

                if (avance > 0.2f && avance < 200f
                    && delBloque[i].AnchoItext > 0.05f)
                {
                    float escala = avance / delBloque[i].AnchoItext;

                    if (escala > 0.2f && escala < 300f)
                    {
                        delBloque[i].Escala = escala;
                    }
                }
            }

            _caracteres.AddRange(delBloque);
        }

        private void RecolectarTrazo(PathRenderInfo info)
        {
            try
            {
                var matriz = info.GetCtm();

                if (CtmEjemplo.Length == 0)
                {
                    var valores = new List<string>();

                    for (int i = 0; i <= 8; i++)
                    {
                        try
                        {
                            valores.Add(string.Format(
                                CultureInfo.InvariantCulture,
                                "{0:0.00}",
                                matriz.Get(i)));
                        }
                        catch
                        {
                            valores.Add("?");
                        }
                    }

                    CtmEjemplo = "[" + string.Join(" ", valores) + "]";
                }

                var camino = info.GetPath();

                foreach (var subcamino in camino.GetSubpaths())
                {
                    foreach (var segmento in subcamino.GetSegments())
                    {
                        if (segmento is Line linea)
                        {
                            var puntos = linea.GetBasePoints();

                            if (puntos.Count >= 2)
                            {
                                if (TrazosCrudos.Count < 8)
                                {
                                    TrazosCrudos.Add(string.Format(
                                        CultureInfo.InvariantCulture,
                                        "crudo ({0:0.0},{1:0.0})->({2:0.0},{3:0.0})",
                                        puntos[0].GetX(), puntos[0].GetY(),
                                        puntos[1].GetX(), puntos[1].GetY()));
                                }

                                AgregarLinea(
                                    puntos[0].GetX(),
                                    puntos[0].GetY(),
                                    puntos[1].GetX(),
                                    puntos[1].GetY(),
                                    matriz);
                            }
                        }
                        else if (segmento is Rectangle rectangulo)
                        {
                            var esquinas = new[]
                            {
                                (rectangulo.GetLeft(), rectangulo.GetBottom()),
                                (rectangulo.GetRight(), rectangulo.GetBottom()),
                                (rectangulo.GetRight(), rectangulo.GetTop()),
                                (rectangulo.GetLeft(), rectangulo.GetTop()),
                            };

                            for (int i = 0; i < 4; i++)
                            {
                                AgregarLinea(
                                    esquinas[i].Item1,
                                    esquinas[i].Item2,
                                    esquinas[(i + 1) % 4].Item1,
                                    esquinas[(i + 1) % 4].Item2,
                                    matriz);
                            }
                        }
                        else if (segmento is BezierCurve curva)
                        {
                            var puntos = curva.GetBasePoints();

                            float minX = (float)puntos.Min(p => p.GetX());
                            float maxX = (float)puntos.Max(p => p.GetX());
                            float minY = (float)puntos.Min(p => p.GetY());
                            float maxY = (float)puntos.Max(p => p.GetY());

                            _trazos.Add(new TrazoPdf
                            {
                                X0 = minX,
                                Y0 = minY,
                                X1 = maxX,
                                Y1 = maxY,
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void AgregarLinea(
            double inicioX,
            double inicioY,
            double finX,
            double finY,
            Matrix matriz)
        {
            // Matriz 3x3 en fila mayor: a=0, b=1, c=3, d=4, e=6, f=7.
            float ax = (float)(inicioX * matriz.Get(0)
                + inicioY * matriz.Get(3)
                + matriz.Get(6));

            float ay = (float)(inicioX * matriz.Get(1)
                + inicioY * matriz.Get(4)
                + matriz.Get(7));

            float bx = (float)(finX * matriz.Get(0)
                + finY * matriz.Get(3)
                + matriz.Get(6));

            float by = (float)(finX * matriz.Get(1)
                + finY * matriz.Get(4)
                + matriz.Get(7));

            _trazos.Add(new TrazoPdf
            {
                X0 = ax,
                Y0 = ay,
                X1 = bx,
                Y1 = by,
            });
        }

        public ContenidoPagina Terminar(float altoPagina)
        {
            var contenido = new ContenidoPagina();

            // Normalizar a origen arriba-izquierda (como el resto del
            // motor): iText usa origen abajo-izquierda.
            foreach (var caracter in _caracteres)
            {
                caracter.Y = altoPagina - caracter.Y;
            }

            foreach (var trazo in _trazos)
            {
                trazo.Y0 = altoPagina - trazo.Y0;
                trazo.Y1 = altoPagina - trazo.Y1;
            }

            contenido.Trazos = _trazos;
            contenido.Caracteres = _caracteres;
            contenido.CtmEjemplo = CtmEjemplo;
            contenido.TrazosCrudos = TrazosCrudos;

            // Glifos de casilla y marca (formato antiguo)
            foreach (var caracter in _caracteres)
            {
                if (caracter.Fuente.Contains("Ornament", StringComparison.OrdinalIgnoreCase)
                    || caracter.Fuente.Contains("WtOn", StringComparison.OrdinalIgnoreCase))
                {
                    contenido.GlifosCaja.Add(caracter);
                }
                else if (caracter.Fuente.Contains("Dingbats", StringComparison.OrdinalIgnoreCase))
                {
                    contenido.GlifosMarca.Add(caracter);
                }
            }

            // Palabras: se agrupan los caracteres por líneas y saltos.
            var normales = _caracteres
                .Where(c => !contenido.GlifosCaja.Contains(c)
                            && !contenido.GlifosMarca.Contains(c))
                .OrderBy(c => c.Y)
                .ThenBy(c => c.X)
                .ToList();

            var lineas = new List<List<CaracterPdf>>();

            foreach (var caracter in normales)
            {
                var linea = lineas.LastOrDefault();

                if (linea != null
                    && Math.Abs(linea[0].Y - caracter.Y) <= 2.5f)
                {
                    linea.Add(caracter);
                }
                else
                {
                    lineas.Add(new List<CaracterPdf> { caracter });
                }
            }

            // Escala por carácter: el menor de los cocientes
            // avance/ancho de sus dos pares vecinos (el mayor suele ser
            // un salto entre palabras). Se hereda del vecino más
            // cercano cuando no se puede medir.
            float ultimaEscala = 0;

            foreach (var linea in lineas)
            {
                linea.Sort((a, b) => a.X.CompareTo(b.X));

                for (int i = 0; i < linea.Count; i++)
                {
                    float escala = 0;

                    if (i > 0
                        && CandidataDePar(
                            linea[i - 1],
                            linea[i],
                            out float anterior))
                    {
                        escala = anterior;
                    }

                    if (i + 1 < linea.Count
                        && CandidataDePar(
                            linea[i],
                            linea[i + 1],
                            out float siguiente))
                    {
                        escala = escala > 0
                            ? Math.Min(escala, siguiente)
                            : siguiente;
                    }

                    linea[i].Escala = escala;
                }

                for (int i = 0; i < linea.Count; i++)
                {
                    if (linea[i].Escala > 0)
                    {
                        continue;
                    }

                    float escala = 0;

                    for (int j = i - 1; j >= 0 && escala == 0; j--)
                    {
                        if (linea[j].Escala > 0)
                        {
                            escala = linea[j].Escala;
                        }
                    }

                    for (int j = i + 1; j < linea.Count && escala == 0; j++)
                    {
                        if (linea[j].Escala > 0)
                        {
                            escala = linea[j].Escala;
                        }
                    }

                    linea[i].Escala = escala > 0 ? escala : ultimaEscala;
                }

                var conEscala = linea
                    .LastOrDefault(c => c.Escala > 0);

                if (conEscala != null)
                {
                    ultimaEscala = conEscala.Escala;
                }

                foreach (var caracter in linea)
                {
                    caracter.Ancho = caracter.Escala > 0
                        && caracter.AnchoItext > 0.05f
                            ? Math.Max(0.5f, caracter.AnchoItext * caracter.Escala)
                            : Math.Max(0.5f, caracter.FuenteTamano * 0.5f);
                }

                var palabra = new StringBuilder();
                float inicioX = 0;
                float ultimaX = 0;
                float anchoUltimo = 0;
                float y = linea[0].Y;

                void CerrarPalabra()
                {
                    if (palabra.Length == 0)
                    {
                        return;
                    }

                    contenido.Palabras.Add(new PalabraPdf
                    {
                        Texto = palabra.ToString(),
                        X0 = inicioX,
                        Y0 = y,
                        X1 = ultimaX + anchoUltimo,
                        Y1 = y,
                    });

                    palabra.Clear();
                }

                foreach (var caracter in linea)
                {
                    bool esEspacio = string.IsNullOrWhiteSpace(caracter.Texto);

                    bool salto = palabra.Length > 0
                        && caracter.X - (ultimaX + anchoUltimo) > 2.0f;

                    if (esEspacio || salto)
                    {
                        CerrarPalabra();

                        if (esEspacio)
                        {
                            continue;
                        }
                    }

                    if (palabra.Length == 0)
                    {
                        inicioX = caracter.X;
                    }

                    palabra.Append(caracter.Texto);
                    ultimaX = caracter.X;
                    anchoUltimo = caracter.Ancho;
                }

                CerrarPalabra();
            }

            return contenido;
        }

        public void Dispose()
        {
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            // null = todos los eventos.
            return null;
        }

        public void AgregarSinteticos(List<CaracterPdf> caracteres)
        {
            if (caracteres != null && caracteres.Count > 0)
            {
                _caracteres.AddRange(caracteres);
            }
        }

        public void AgregarTrazosSinteticos(List<TrazoPdf> trazos)
        {
            if (trazos != null && trazos.Count > 0)
            {
                _trazos.AddRange(trazos);
            }
        }
    }


    private static ContenidoPagina LeerContenido(
        PdfPage pagina,
        byte[] apariencias,
        List<CaracterPdf> sinteticos,
        List<TrazoPdf> marcasSinteticas)
    {
        var recolector = new Recolector();

        var procesador = new PdfCanvasProcessor(recolector);

        bool fallo = false;

        try
        {
            procesador.ProcessPageContent(pagina);
        }
        catch
        {
            // Algunos PDFs traen recursos rotos (p. ej. ExtGState);
            // se conserva lo leído hasta el punto del fallo.
            fallo = true;
        }

        if (fallo && apariencias != null)
        {
            // El contenido normal falló antes de llegar a las
            // apariencias dibujadas: se procesan por separado.
            try
            {
                procesador.ProcessContent(
                    apariencias,
                    pagina.GetResources());
            }
            catch
            {
            }
        }

        recolector.AgregarSinteticos(sinteticos);
        recolector.AgregarTrazosSinteticos(marcasSinteticas);

        float alto = 842f;

        try
        {
            alto = pagina.GetPageSize().GetHeight();
        }
        catch
        {
        }

        return recolector.Terminar(alto);
    }


    /// <summary>
    /// Dibuja las apariencias de los widgets (valores de formularios
    /// rellenados) dentro del contenido de la página. Devuelve los bytes
    /// del flujo añadido, o null si no había nada que dibujar.
    /// </summary>
    private static byte[] DibujarApariencias(
        PdfPage pagina,
        PdfDocument documento)
    {
        UltimoDiagnosticoApariencias = "iniciado";

        IList<PdfAnnotation> anotaciones;

        try
        {
            anotaciones = pagina.GetAnnotations();
        }
        catch
        {
            return null;
        }

        if (anotaciones == null || anotaciones.Count == 0)
        {
            return null;
        }

        PdfStream nuevo = pagina.NewContentStreamAfter();

        var lienzo = new PdfCanvas(
            nuevo,
            pagina.GetResources(),
            documento);

        bool dibujado = false;
        int dibujadas = 0;
        var errores = new List<string>();
        var diagnosticoFormas = new List<string>();

        foreach (var anotacion in anotaciones)
        {
            try
            {
                string valorTexto = ValorDeAnotacion(anotacion);

                if (valorTexto != null && valorTexto.Length > 0)
                {
                    // El texto del valor se sintetiza aparte (como hace
                    // PyMuPDF al dibujar los widgets).
                    continue;
                }

                var apariencia = anotacion.GetNormalAppearanceObject();

                if (apariencia == null)
                {
                    continue;
                }

                PdfStream flujo = null;

                if (apariencia is PdfStream directo)
                {
                    flujo = directo;
                }
                else
                {
                    var estado = EstadoDeAnotacion(anotacion);

                    if (estado != null)
                    {
                        flujo = apariencia.GetAsStream(estado);
                    }

                    if (flujo == null)
                    {
                        flujo = apariencia.GetAsStream(new PdfName("Off"));
                    }

                    if (flujo == null)
                    {
                        foreach (var nombre in apariencia.KeySet())
                        {
                            flujo = apariencia.GetAsStream(nombre);

                            if (flujo != null)
                            {
                                break;
                            }
                        }
                    }
                }

                if (flujo == null)
                {
                    continue;
                }

                var rectangulo = anotacion.GetRectangle();

                if (rectangulo == null)
                {
                    continue;
                }

                var forma = new PdfFormXObject(flujo);

                var caja = forma.GetBBox().ToRectangle();
                var rect = rectangulo.ToRectangle();

                if (diagnosticoFormas.Count < 4)
                {
                    diagnosticoFormas.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "rect=({0:0.0},{1:0.0} {2:0.0}x{3:0.0}) bbox=({4:0.0},{5:0.0} {6:0.0}x{7:0.0})",
                        rect.GetX(), rect.GetY(),
                        rect.GetWidth(), rect.GetHeight(),
                        caja.GetX(), caja.GetY(),
                        caja.GetWidth(), caja.GetHeight()));
                }

                lienzo.AddXObjectFittedIntoRectangle(forma, rect);

                dibujado = true;
                dibujadas++;
            }
            catch (Exception ex)
            {
                if (errores.Count < 3)
                {
                    errores.Add(ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        lienzo.Release();

        byte[] bytes = dibujado ? nuevo.GetBytes() : null;

        UltimoDiagnosticoApariencias = "dibujadas=" + dibujadas
            + " bytes=" + (bytes?.Length ?? 0)
            + (errores.Count > 0
                ? " | " + string.Join(" | ", errores)
                : "")
            + (diagnosticoFormas.Count > 0
                ? " || " + string.Join(" || ", diagnosticoFormas)
                : "");

        return bytes;
    }

    /// <summary>Diagnóstico del último aplanado de apariencias.</summary>
    public static string UltimoDiagnosticoApariencias = "";


    /// <summary>Valor de texto (/V) de una anotación, o null.</summary>
    private static string ValorDeAnotacion(PdfAnnotation anotacion)
    {
        try
        {
            var valor = anotacion.GetPdfObject().Get(PdfName.V);

            if (valor is PdfIndirectReference referencia)
            {
                valor = referencia.GetRefersTo();
            }

            if (valor is PdfString cadena)
            {
                return LimpiarEspacios(cadena.ToUnicodeString());
            }
        }
        catch
        {
        }

        return null;
    }


    /// <summary>Estado de apariencia de una anotación (o el de /V).</summary>
    private static PdfName EstadoDeAnotacion(PdfAnnotation anotacion)
    {
        try
        {
            var estado = anotacion.GetAppearanceState();

            if (estado != null
                && !string.IsNullOrEmpty(estado.GetValue()))
            {
                return estado;
            }
        }
        catch
        {
        }

        try
        {
            var valor = anotacion.GetPdfObject().Get(PdfName.V);

            if (valor is PdfIndirectReference referencia)
            {
                valor = referencia.GetRefersTo();
            }

            if (valor is PdfName nombre)
            {
                return nombre;
            }
        }
        catch
        {
        }

        return null;
    }


    /// <summary>
    /// Valores de texto de los widgets de la página, como caracteres
    /// sintéticos colocados en su rectángulo (igual que PyMuPDF).
    /// </summary>
    private static List<CaracterPdf> LeerValoresCampos(PdfPage pagina)
    {
        var lista = new List<CaracterPdf>();

        try
        {
            var anotaciones = pagina.GetAnnotations();

            if (anotaciones == null)
            {
                return lista;
            }

            foreach (var anotacion in anotaciones)
            {
                try
                {
                    string texto = ValorDeAnotacion(anotacion);

                    if (string.IsNullOrEmpty(texto))
                    {
                        continue;
                    }

                    var rectangulo = anotacion.GetRectangle();

                    if (rectangulo == null)
                    {
                        continue;
                    }

                    var rect = rectangulo.ToRectangle();

                    lista.Add(new CaracterPdf
                    {
                        Texto = texto,
                        X = rect.GetX() + 1f,
                        Y = rect.GetY() + rect.GetHeight() / 2f,
                        Ancho = Math.Max(1f, rect.GetWidth()),
                        FuenteTamano = 10f,
                        Fuente = "",
                    });
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return lista;
    }


    /// <summary>
    /// Marcas sintéticas de las casillas marcadas (/V distinto de /Off),
    /// en el centro de su rectángulo.
    /// </summary>
    private static List<TrazoPdf> LeerMarcasCampos(PdfPage pagina)
    {
        var lista = new List<TrazoPdf>();

        try
        {
            var anotaciones = pagina.GetAnnotations();

            if (anotaciones == null)
            {
                return lista;
            }

            foreach (var anotacion in anotaciones)
            {
                try
                {
                    var valor = anotacion.GetPdfObject().Get(PdfName.V);

                    if (valor is PdfIndirectReference referencia)
                    {
                        valor = referencia.GetRefersTo();
                    }

                    if (!(valor is PdfName nombre)
                        || nombre.GetValue() == "Off")
                    {
                        continue;
                    }

                    var rectangulo = anotacion.GetRectangle();

                    if (rectangulo == null)
                    {
                        continue;
                    }

                    var rect = rectangulo.ToRectangle();

                    if (rect.GetWidth() > 30 || rect.GetHeight() > 30)
                    {
                        continue;
                    }

                    float centroX = rect.GetX() + rect.GetWidth() / 2f;
                    float centroY = rect.GetY() + rect.GetHeight() / 2f;

                    lista.Add(new TrazoPdf
                    {
                        X0 = centroX - 1.5f,
                        Y0 = centroY - 1.5f,
                        X1 = centroX + 1.5f,
                        Y1 = centroY + 1.5f,
                    });
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return lista;
    }


    // ========================================================
    // CASILLAS Y MARCAS
    // ========================================================

    private sealed class Casilla
    {
        public float X0;
        public float Y0;
        public float X1;
        public float Y1;
        public bool Marcada;

        public float CentroX => (X0 + X1) / 2;
        public float CentroY => (Y0 + Y1) / 2;
    }

    private static List<Casilla> DetectarCasillas(ContenidoPagina contenido)
    {
        var verticales = new List<TrazoPdf>();
        var horizontales = new List<TrazoPdf>();
        var diagonales = new List<TrazoPdf>();

        foreach (var trazo in contenido.Trazos)
        {
            if (trazo.Ancho < 0.8f && trazo.Alto >= 5f && trazo.Alto <= 12f)
            {
                verticales.Add(trazo);
            }
            else if (trazo.Alto < 0.8f && trazo.Ancho >= 5f && trazo.Ancho <= 12f)
            {
                horizontales.Add(trazo);
            }
            else if (trazo.Ancho > 0.4f && trazo.Alto > 0.4f)
            {
                diagonales.Add(trazo);
            }
        }

        var casillas = new List<Casilla>();

        foreach (var vertical in verticales)
        {
            float vx = vertical.X0;
            float vy0 = Math.Min(vertical.Y0, vertical.Y1);
            float vy1 = Math.Max(vertical.Y0, vertical.Y1);

            TrazoPdf superior = null;
            TrazoPdf inferior = null;

            foreach (var horizontal in horizontales)
            {
                float hy = horizontal.Y0;
                float hx0 = Math.Min(horizontal.X0, horizontal.X1);
                float hx1 = Math.Max(horizontal.X0, horizontal.X1);

                if (Math.Abs(hy - vy0) <= 1.5f && hx0 - 1 <= vx && vx <= hx1 + 1)
                {
                    superior = horizontal;
                }

                if (Math.Abs(hy - vy1) <= 1.5f && hx0 - 1 <= vx && vx <= hx1 + 1)
                {
                    inferior = horizontal;
                }
            }

            if (superior == null || inferior == null)
            {
                continue;
            }

            float x0 = Math.Min(superior.X0, superior.X1);
            float x1 = Math.Max(superior.X0, superior.X1);
            float y0 = Math.Min(superior.Y0, inferior.Y0);
            float y1 = Math.Max(superior.Y0, inferior.Y0);

            if (x1 - x0 < 5 || y1 - y0 < 5)
            {
                continue;
            }

            bool yaEsta = casillas.Any(c =>
                Math.Abs(c.X0 - x0) < 0.6f
                && Math.Abs(c.Y0 - y0) < 0.6f
                && Math.Abs(c.X1 - x1) < 0.6f
                && Math.Abs(c.Y1 - y1) < 0.6f);

            if (yaEsta)
            {
                continue;
            }

            // El propio borde de la casilla (si se dibuja como un
            // rectángulo) no cuenta como marca.
            bool marcada = diagonales.Any(d =>
                !(Math.Abs(Math.Min(d.X0, d.X1) - x0) <= 0.6f
                  && Math.Abs(Math.Max(d.X0, d.X1) - x1) <= 0.6f
                  && Math.Abs(Math.Min(d.Y0, d.Y1) - y0) <= 0.6f
                  && Math.Abs(Math.Max(d.Y0, d.Y1) - y1) <= 0.6f)
                && Math.Min(d.X0, d.X1) >= x0 - 0.4f
                && Math.Max(d.X0, d.X1) <= x1 + 0.4f
                && Math.Min(d.Y0, d.Y1) >= y0 - 0.4f
                && Math.Max(d.Y0, d.Y1) <= y1 + 0.4f);

            casillas.Add(new Casilla
            {
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1,
                Marcada = marcada,
            });
        }

        return casillas;
    }

    private sealed class Marcas
    {
        public List<(float X, float Y)> Puntos = new();

        public bool Hay(float x, float y, float radio = 9f)
        {
            foreach (var (mx, my) in Puntos)
            {
                if (Math.Abs(mx - x) <= radio && Math.Abs(my - y) <= radio)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class ColumnaTabla
    {
        public float X0;
        public float X1;
        public string Tipo = "";
    }



    /// <summary>
    /// Diagnóstico (solo lectura): columnas detectadas en la primera
    /// página de un PDF. Útil para revisar plantillas nuevas.
    /// </summary>
    public static string DescribirColumnas(string ruta)
    {
        var sb = new StringBuilder();

        using (var lector = new PdfReader(
            ruta,
            new ReaderProperties().SetPassword(Array.Empty<byte>())))
        using (var documento = new PdfDocument(lector))
        {
            var pagina = documento.GetPage(1);

            var recolector = new Recolector();
            new PdfCanvasProcessor(recolector).ProcessPageContent(pagina);

            var contenido = recolector.Terminar(
                pagina.GetPageSize().GetHeight());

            var filas = FilasDeMeses(contenido, 160);

            foreach (var columna in DetectarColumnasTabla(contenido, filas))
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.0}-{1:0.0} = {2}",
                    columna.X0,
                    columna.X1,
                    columna.Tipo));
            }
        }

        return sb.ToString();
    }


    /// <summary>
    /// Detecta las columnas de la tabla de meses por su propia rejilla y
    /// por sus cabeceras. Así funciona aunque cada plantilla del S-21
    /// reparta el ancho de otra forma (no se usan posiciones fijas).
    /// </summary>
    private static List<ColumnaTabla> DetectarColumnasTabla(
        ContenidoPagina contenido,
        List<(string Mes, float Y)> filasMeses)
    {
        var columnas = new List<ColumnaTabla>();

        if (filasMeses.Count == 0)
        {
            return columnas;
        }

        float arriba = filasMeses[0].Y - 14;
        float abajo = filasMeses[^1].Y + 14;

        // Alto de una fila, para no confundir los bordes de las casillas
        // (7-9 px) con los separadores de columna.
        float altoFila = filasMeses.Count > 1
            ? (filasMeses[^1].Y - filasMeses[0].Y) / (filasMeses.Count - 1)
            : 15f;

        float minimoAlto = Math.Max(6f, altoFila * 0.7f);

        // Bordes verticales de la tabla.
        var xs = new List<float>();

        foreach (var trazo in contenido.Trazos)
        {
            if (trazo.Ancho < 1f && trazo.Alto >= minimoAlto)
            {
                float cy = (trazo.Y0 + trazo.Y1) / 2;

                if (cy >= arriba - 20 && cy <= abajo + 20)
                {
                    xs.Add(Math.Min(trazo.X0, trazo.X1));
                }
            }
        }

        xs.Sort();

        var bordes = new List<float>();

        foreach (float x in xs)
        {
            if (bordes.Count == 0 || Math.Abs(x - bordes[^1]) > 2f)
            {
                bordes.Add(x);
            }
        }

        for (int i = 0; i + 1 < bordes.Count; i++)
        {
            if (bordes[i + 1] - bordes[i] >= 10)
            {
                columnas.Add(new ColumnaTabla
                {
                    X0 = bordes[i],
                    X1 = bordes[i + 1],
                });
            }
        }

        if (columnas.Count < 2)
        {
            return new List<ColumnaTabla>();
        }

        // Cabeceras: cada palabra de encima de la tabla, en su columna.
        var textos = new string[columnas.Count];

        foreach (var palabra in contenido.Palabras)
        {
            float cy = palabra.CentroY;

            if (cy > arriba - 2 || cy < arriba - 90)
            {
                continue;
            }

            float cx = (palabra.X0 + palabra.X1) / 2;

            for (int i = 0; i < columnas.Count; i++)
            {
                if (cx >= columnas[i].X0 - 2 && cx <= columnas[i].X1 + 2)
                {
                    textos[i] += " " + palabra.Texto.ToLowerInvariant();
                    break;
                }
            }
        }

        for (int i = 0; i < columnas.Count; i++)
        {
            string texto = textos[i] ?? "";

            columnas[i].Tipo =
                texto.Contains("participa") ? "participacion"
                : texto.Contains("cursos") || texto.Contains("bibl") ? "cursos"
                : texto.Contains("auxiliar") ? "auxiliar"
                : texto.Contains("hora") ? "horas"
                : texto.Contains("nota") ? "notas"
                : Meses.Any(m => texto.Contains(m)) ? "mes"
                : "";
        }

        // Las columnas sin cabecera clara se rellenan en el orden clásico.
        string[] orden =
        {
            "mes", "participacion", "cursos", "auxiliar", "horas", "notas",
        };

        var usados = new HashSet<string>(
            columnas.Where(c => c.Tipo.Length > 0).Select(c => c.Tipo));

        foreach (var columna in columnas.Where(c => c.Tipo.Length == 0))
        {
            foreach (string candidato in orden)
            {
                if (!usados.Contains(candidato))
                {
                    columna.Tipo = candidato;
                    usados.Add(candidato);
                    break;
                }
            }
        }

        return columnas;
    }


    /// <summary>Lee una fila de meses usando las columnas detectadas.</summary>
    private static InformeS21 LeerFilaTabla(
        string mes,
        float y,
        List<ColumnaTabla> columnas,
        ContenidoPagina contenido,
        List<Casilla> casillas)
    {
        bool participa = false;
        bool auxiliar = false;
        object cursos = "";
        object horas = "";
        string notas = "";

        foreach (var columna in columnas)
        {
            bool Marcada() => casillas.Any(c =>
                c.Marcada
                && c.CentroX >= columna.X0 - 1
                && c.CentroX <= columna.X1 + 1
                && Math.Abs(c.CentroY - y) <= 9);

            switch (columna.Tipo)
            {
                case "participacion":
                    participa = Marcada();
                    break;

                case "auxiliar":
                    auxiliar = Marcada();
                    break;

                case "cursos":
                case "horas":
                {
                    foreach (var palabra in contenido.Palabras)
                    {
                        if (Math.Abs(palabra.CentroY - y) > 5)
                        {
                            continue;
                        }

                        float cx = (palabra.X0 + palabra.X1) / 2;

                        if (cx < columna.X0 - 2 || cx > columna.X1 + 2)
                        {
                            continue;
                        }

                        string limpio = palabra.Texto.Trim();

                        if (limpio.Length > 0 && limpio.All(char.IsDigit))
                        {
                            if (columna.Tipo == "cursos")
                            {
                                cursos = int.Parse(limpio, CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                horas = int.Parse(limpio, CultureInfo.InvariantCulture);
                            }

                            break;
                        }
                    }

                    break;
                }

                case "notas":
                {
                    var propias = new List<string>();

                    foreach (var palabra in contenido.Palabras)
                    {
                        if (Math.Abs(palabra.CentroY - y) > 5)
                        {
                            continue;
                        }

                        float cx = (palabra.X0 + palabra.X1) / 2;

                        if (cx >= columna.X0 - 2 && cx <= columna.X1 + 2)
                        {
                            propias.Add(palabra.Texto);
                        }
                    }

                    notas = LimpiarEspacios(string.Join(" ", propias));
                    break;
                }
            }
        }

        return new InformeS21
        {
            Mes = mes,
            Participacion = participa,
            CursosBiblicos = cursos,
            PrecursorAuxiliar = auxiliar,
            Horas = horas,
            Notas = notas,
        };
    }


    private static bool FilaConDatos(InformeS21 fila)
    {
        return fila.Participacion
            || fila.PrecursorAuxiliar
            || !(fila.Horas is string horas && horas.Length == 0)
            || !(fila.CursosBiblicos is string cursos && cursos.Length == 0)
            || !string.IsNullOrWhiteSpace(fila.Notas);
    }


    private static Marcas DetectarMarcas(ContenidoPagina contenido)
    {
        var marcas = new Marcas();

        foreach (var trazo in contenido.Trazos)
        {
            if (trazo.Ancho > 0.4f && trazo.Alto > 0.4f)
            {
                marcas.Puntos.Add((
                    (trazo.X0 + trazo.X1) / 2,
                    (trazo.Y0 + trazo.Y1) / 2));
            }
        }

        foreach (var glifo in contenido.GlifosMarca)
        {
            marcas.Puntos.Add((glifo.X, glifo.Y));
        }

        return marcas;
    }


    // ========================================================
    // FORMATO 2: IMPRESO «ÚLTIMOS 12 MESES»
    // ========================================================

    private static TarjetaS21 TarjetaImpresa(
        ContenidoPagina contenido,
        int anioDefecto)
    {
        var textos = contenido.Palabras
            .Select(p => p.Texto.ToLowerInvariant())
            .ToList();

        if (!textos.Any(t => t.Contains("ltimos"))
            || !textos.Any(t => t.Contains("eses")))
        {
            return null;
        }

        if (!textos.Any(t => t.StartsWith("nombre")))
        {
            return null;
        }

        var filasMeses = FilasDeMeses(contenido, 160);

        if (filasMeses.Count < 12)
        {
            return null;
        }

        filasMeses = filasMeses.Take(12).ToList();

        float tablaY = filasMeses[0].Y - 10;

        var casillas = DetectarCasillas(contenido);

        var cabecera = casillas.Where(c => c.Y1 <= tablaY).ToList();
        var tabla = casillas.Where(c => c.Y0 >= tablaY).ToList();

        // Cabecera en filas
        var filas = new List<List<Casilla>>();

        foreach (var casilla in cabecera.OrderBy(c => c.Y0))
        {
            if (filas.Count > 0
                && Math.Abs(casilla.Y0 - filas[^1][0].Y0) <= 4)
            {
                filas[^1].Add(casilla);
            }
            else
            {
                filas.Add(new List<Casilla> { casilla });
            }
        }

        if (filas.Count < 3
            || filas[0].Count < 2
            || filas[1].Count < 2
            || filas[2].Count < 5)
        {
            return null;
        }

        var r1 = filas[0].OrderBy(c => c.X0).ToList();
        var r2 = filas[1].OrderBy(c => c.X0).ToList();
        var r3 = filas[2].OrderBy(c => c.X0).ToList();

        string sexo = r1[0].Marcada ? "Hombre"
            : r1[1].Marcada ? "Mujer" : "";

        string esperanza = r2[0].Marcada ? "Otras ovejas"
            : r2[1].Marcada ? "Ungido" : "";

        string nombramiento = r3[0].Marcada ? "Anciano"
            : r3[1].Marcada ? "Siervo ministerial" : "";

        string servicio = r3[2].Marcada ? "Precursor regular"
            : r3[3].Marcada ? "Precursor especial"
            : r3[4].Marcada ? "Misionero" : "";

        string nombre = LimpiarEspacios(
            string.Join(" ", PalabrasDerechaDe(contenido.Palabras, "nombre")
                .Select(p => p.Texto)));

        string nacimiento = FechaImpresa(
            PalabrasDerechaDe(contenido.Palabras, "nacimiento"));

        string bautismo = FechaImpresa(
            PalabrasDerechaDe(contenido.Palabras, "bautismo"));

        if (!NombreValido(nombre))
        {
            return null;
        }

        // Columnas de la tabla: primera casilla de cada fila =
        // participación; segunda = auxiliar (como la referencia).
        var columnasParticipacion = new List<Casilla>();
        var columnasAuxiliar = new List<Casilla>();

        foreach (var (_, y) in filasMeses)
        {
            var fila = tabla
                .Where(c => Math.Abs(c.CentroY - y) <= 7)
                .OrderBy(c => c.X0)
                .ToList();

            if (fila.Count >= 2)
            {
                columnasParticipacion.Add(fila[0]);
                columnasAuxiliar.Add(fila[1]);
            }
        }

        if (columnasParticipacion.Count == 0 || columnasAuxiliar.Count == 0)
        {
            return null;
        }

        float auxX0 = columnasAuxiliar.Min(c => c.X0);
        float auxX1 = columnasAuxiliar.Max(c => c.X1);

        float notasX = -1;

        foreach (var palabra in contenido.Palabras)
        {
            if (palabra.Texto.Trim().Equals("Notas", StringComparison.OrdinalIgnoreCase)
                && palabra.Y0 < 170)
            {
                notasX = palabra.X0;
                break;
            }
        }

        if (notasX < 0)
        {
            notasX = auxX1 + 190;
        }

        var informe = new List<InformeS21>();

        // Columnas detectadas por la rejilla y las cabeceras; si no se
        // reconocen, se usa el método anterior.
        var columnas = DetectarColumnasTabla(contenido, filasMeses);

        foreach (var (mes, y) in filasMeses)
        {
            if (columnas.Count > 0)
            {
                informe.Add(LeerFilaTabla(
                    mes,
                    y,
                    columnas,
                    contenido,
                    casillas));

                continue;
            }

            var fila = tabla
                .Where(c => Math.Abs(c.CentroY - y) <= 7)
                .OrderBy(c => c.X0)
                .ToList();

            bool participa = fila.Count >= 1 && fila[0].Marcada;
            bool auxiliar = fila.Count >= 2 && fila[1].Marcada;

            object cursos = "";
            object horas = "";
            string notas = "";

            foreach (var palabra in contenido.Palabras)
            {
                if (Math.Abs(palabra.CentroY - y) > 5)
                {
                    continue;
                }

                if (palabra.X0 < 160)
                {
                    continue;
                }

                if (palabra.X0 >= notasX - 4)
                {
                    notas = LimpiarEspacios(
                        notas.Length == 0
                            ? palabra.Texto
                            : notas + " " + palabra.Texto);

                    continue;
                }

                if (palabra.X0 > auxX1 + 3)
                {
                    horas = Numero(palabra.Texto);
                    continue;
                }

                if (palabra.X1 < auxX0 - 2)
                {
                    cursos = Numero(palabra.Texto);
                }
            }

            informe.Add(new InformeS21
            {
                Mes = mes,
                Participacion = participa,
                CursosBiblicos = cursos,
                PrecursorAuxiliar = auxiliar,
                Horas = horas,
                Notas = notas,
            });
        }

        var porMes = informe.ToDictionary(i => i.Mes, i => i);

        return new TarjetaS21
        {
            Nombre = nombre,
            FechaNacimiento = nacimiento,
            Sexo = sexo,
            FechaBautismo = bautismo,
            Esperanza = esperanza,
            Nombramiento = nombramiento,
            Servicio = servicio,
            AnioServicio = anioDefecto.ToString(CultureInfo.InvariantCulture),
            Informe = Meses.Select(m => porMes.TryGetValue(m, out var i)
                ? i
                : new InformeS21 { Mes = m }).ToList(),
        };
    }

    private static float primeraY(
        List<(string Mes, float Y)> filas) => filas[0].Y;


    // ========================================================
    // FORMATO 3: IMPRESO «REGISTRO DE PUBLICADOR»
    // ========================================================

    private static TarjetaS21 TarjetaAntigua(
        ContenidoPagina contenido,
        int anioDefecto)
    {
        var textos = contenido.Palabras
            .Select(p => p.Texto.ToLowerInvariant())
            .ToList();

        if (!textos.Any(t => t == "registro"))
        {
            return null;
        }

        var filasMeses = FilasDeMeses(contenido, 80);

        if (filasMeses.Count != 12 && filasMeses.Count != 24)
        {
            return null;
        }

        var secciones = new List<List<(string Mes, float Y)>>();

        if (filasMeses.Count == 24)
        {
            secciones.Add(filasMeses.Take(12).ToList());
            secciones.Add(filasMeses.Skip(12).ToList());
        }
        else
        {
            secciones.Add(filasMeses);
        }

        // Años: número justo debajo de «Año de servicio»
        var anios = new List<(string Anio, float Y)>();

        foreach (var palabra in contenido.Palabras)
        {
            if (!palabra.Texto.Equals("servicio", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var candidato in contenido.Palabras)
            {
                string texto = candidato.Texto.Trim();

                if (texto.Length == 4
                    && texto.All(char.IsDigit)
                    && candidato.Y0 - palabra.CentroY >= 2
                    && candidato.Y0 - palabra.CentroY <= 20
                    && Math.Abs(candidato.X0 - palabra.X0) < 60)
                {
                    anios.Add((texto, candidato.CentroY));
                    break;
                }
            }
        }

        anios.Sort((a, b) => a.Y.CompareTo(b.Y));

        var casillas = DetectarCasillas(contenido);
        var marcas = DetectarMarcas(contenido);

        var casillasTodas = casillas
            .Select(c => (X: c.CentroX, Y: c.CentroY, c.Marcada))
            .Concat(contenido.GlifosCaja.Select(g => (X: g.X, Y: g.Y, Marcada: false)))
            .ToList();

        float primeraY = filasMeses[0].Y;

        // Cabecera
        var cabecera = casillasTodas
            .Where(c => c.Y < primeraY - 12)
            .ToList();

        var filasCab = AgruparFilas(
            cabecera.Select(c => (c.X, c.Y)).ToList(),
            7);

        string sexo = "";
        string esperanza = "";
        string nombramiento = "";
        string servicio = "";

        for (int idx = 0; idx < filasCab.Count; idx++)
        {
            float y = filasCab[idx][0].Y;

            string etiqueta = EtiquetasDe(contenido.Palabras, y);

            bool CabeceraMarcada(float desde, float hasta)
            {
                foreach (var (x, cy, m) in casillasTodas)
                {
                    if (x >= desde && x <= hasta
                        && Math.Abs(cy - y) <= 7)
                    {
                        return m || marcas.Hay(x, cy);
                    }
                }

                return false;
            }

            if (etiqueta.Contains("hombre") && etiqueta.Contains("mujer"))
            {
                sexo = CabeceraMarcada(0, 470) ? "Hombre"
                    : CabeceraMarcada(470, 9999) ? "Mujer" : "";
            }
            else if (etiqueta.Contains("ovejas") || etiqueta.Contains("ungido"))
            {
                esperanza = CabeceraMarcada(0, 470) ? "Otras ovejas"
                    : CabeceraMarcada(470, 9999) ? "Ungido" : "";
            }
            else if (etiqueta.Contains("anciano") || etiqueta.Contains("misionero"))
            {
                nombramiento = CabeceraMarcada(0, 80) ? "Anciano"
                    : CabeceraMarcada(80, 180) ? "Siervo ministerial" : "";

                servicio = CabeceraMarcada(180, 310) ? "Precursor regular"
                    : CabeceraMarcada(310, 440) ? "Precursor especial"
                    : CabeceraMarcada(440, 9999) ? "Misionero" : "";
            }
        }

        // Nombre y fechas
        string nombre = "";

        foreach (var palabra in contenido.Palabras)
        {
            if (palabra.Texto.ToLowerInvariant().StartsWith("nombre"))
            {
                nombre = LimpiarEspacios(
                    string.Join(" ",
                        contenido.Palabras
                            .Where(p => p.X0 > palabra.X1 - 1
                                        && Math.Abs(p.CentroY - palabra.CentroY) <= 9)
                            .OrderBy(p => p.X0)
                            .Select(p => p.Texto)));

                break;
            }
        }

        if (!NombreValido(nombre))
        {
            return null;
        }

        string nacimiento = "";
        string bautismo = "";

        foreach (var palabra in contenido.Palabras)
        {
            string limpio = palabra.Texto.ToLowerInvariant();

            if (limpio.StartsWith("nacimiento") && nacimiento.Length == 0)
            {
                nacimiento = FechaImpresa(
                    contenido.Palabras
                        .Where(p => p.X0 > palabra.X1 - 1
                                    && Math.Abs(p.CentroY - palabra.CentroY) <= 9)
                        .OrderBy(p => p.X0)
                        .ToList());
            }
            else if (limpio.StartsWith("bautismo") && bautismo.Length == 0)
            {
                bautismo = FechaImpresa(
                    contenido.Palabras
                        .Where(p => p.X0 > palabra.X1 - 1
                                    && Math.Abs(p.CentroY - palabra.CentroY) <= 9)
                        .OrderBy(p => p.X0)
                        .ToList());
            }
        }

        // Columnas de la tabla: por la rejilla y las cabeceras de esta
        // misma tarjeta (cada plantilla puede repartirlas distinto).
        var columnas = DetectarColumnasTabla(contenido, filasMeses);

        List<InformeS21> LeerSeccion(
            List<(string Mes, float Y)> filas,
            out int rellenos)
        {
            var datos = new List<InformeS21>();
            int cuenta = 0;

            if (columnas.Count > 0)
            {
                foreach (var (mes, y) in filas)
                {
                    var fila = LeerFilaTabla(
                        mes,
                        y,
                        columnas,
                        contenido,
                        casillas);

                    if (FilaConDatos(fila))
                    {
                        cuenta++;
                    }

                    datos.Add(fila);
                }

                rellenos = cuenta;
                return datos;
            }

            // Sin rejilla reconocible: se usan las casillas como antes.
            var tablaCajas = casillasTodas
                .Where(c => c.Y >= primeraY - 12
                            && c.Y <= filasMeses[^1].Y + 14)
                .ToList();

            float corte = 0;

            if (tablaCajas.Count > 0)
            {
                var xs = tablaCajas.Select(c => c.X).OrderBy(x => x).ToList();

                float mayorSalto = 0;

                for (int i = 0; i < xs.Count - 1; i++)
                {
                    float salto = xs[i + 1] - xs[i];

                    if (salto > mayorSalto)
                    {
                        mayorSalto = salto;
                        corte = (xs[i] + xs[i + 1]) / 2;
                    }
                }
            }

            float partX = corte > 0
                ? tablaCajas.Where(c => c.X < corte)
                    .Select(c => c.X).DefaultIfEmpty(133f).Average()
                : 133f;

            float auxX = corte > 0
                ? tablaCajas.Where(c => c.X >= corte)
                    .Select(c => c.X).DefaultIfEmpty(235f).Average()
                : 235f;

            foreach (var (mes, y) in filas)
            {
                bool participa = marcas.Hay(partX, y);
                bool auxiliar = marcas.Hay(auxX, y);

                object cursos = "";
                object horas = "";
                string notas = "";

                foreach (var palabra in contenido.Palabras)
                {
                    if (Math.Abs(palabra.CentroY - y) > 6)
                    {
                        continue;
                    }

                    if (palabra.X0 < 80)
                    {
                        continue;
                    }

                    if (palabra.X0 >= 340)
                    {
                        notas = LimpiarEspacios(
                            notas.Length == 0
                                ? palabra.Texto
                                : notas + " " + palabra.Texto);
                    }
                    else if (palabra.X0 >= 250)
                    {
                        horas = Numero(palabra.Texto);
                    }
                    else
                    {
                        cursos = Numero(palabra.Texto);
                    }
                }

                bool tieneDatos = participa
                    || !(cursos is string sc && sc.Length == 0)
                    || !(horas is string sh && sh.Length == 0)
                    || notas.Length > 0;

                if (tieneDatos)
                {
                    cuenta++;
                }

                datos.Add(new InformeS21
                {
                    Mes = mes,
                    Participacion = participa,
                    CursosBiblicos = cursos,
                    PrecursorAuxiliar = auxiliar,
                    Horas = horas,
                    Notas = notas,
                });
            }

            rellenos = cuenta;
            return datos;
        }

        // Evaluar cada sección y elegir la más reciente (año mayor);
        // si esa está vacía, la que tenga más datos.
        var evaluadas = new List<(
            List<InformeS21> Datos,
            int Rellenos,
            string Anio,
            int Clave)>();

        for (int i = 0; i < secciones.Count; i++)
        {
            var datos = LeerSeccion(secciones[i], out int rellenos);

            string anio = i < anios.Count ? anios[i].Anio : null;

            int clave = anio != null
                && anio.Length > 0
                && anio.All(char.IsDigit)
                    ? int.Parse(anio)
                    : 0;

            evaluadas.Add((datos, rellenos, anio, clave));
        }

        var elegida = evaluadas.OrderByDescending(e => e.Clave).First();

        if (elegida.Rellenos == 0)
        {
            var conDatos = evaluadas.Where(e => e.Rellenos > 0).ToList();

            if (conDatos.Count > 0)
            {
                elegida = conDatos
                    .OrderByDescending(e => e.Rellenos)
                    .First();
            }
        }

        var mejorDatos = elegida.Datos;
        string mejorAnio = elegida.Anio;

        var porMes = mejorDatos.ToDictionary(i => i.Mes, i => i);

        string anioJson = mejorAnio != null
            && mejorAnio.Length > 0
            && mejorAnio.All(char.IsDigit)
                ? mejorAnio
                : anioDefecto.ToString(CultureInfo.InvariantCulture);

        return new TarjetaS21
        {
            Nombre = nombre,
            FechaNacimiento = nacimiento,
            Sexo = sexo,
            FechaBautismo = bautismo,
            Esperanza = esperanza,
            Nombramiento = nombramiento,
            Servicio = servicio,
            AnioServicio = anioJson,
            Informe = Meses.Select(m => porMes.TryGetValue(m, out var i)
                ? i
                : new InformeS21 { Mes = m }).ToList(),
        };
    }


    private static List<List<(float X, float Y)>> AgruparFilas(
        List<(float X, float Y)> puntos,
        float tolerancia)
    {
        var filas = new List<List<(float X, float Y)>>();

        foreach (var punto in puntos.OrderBy(p => p.Y))
        {
            if (filas.Count > 0
                && Math.Abs(punto.Y - filas[^1][0].Y) <= tolerancia)
            {
                filas[^1].Add(punto);
            }
            else
            {
                filas.Add(new List<(float X, float Y)> { punto });
            }
        }

        return filas;
    }


    private static string EtiquetasDe(
        List<PalabraPdf> palabras,
        float y)
    {
        var etiquetas = palabras
            .Where(p => Math.Abs(p.CentroY - y) <= 7 && p.X0 > 150)
            .Select(p => p.Texto.ToLowerInvariant());

        return string.Join(" ", etiquetas);
    }


    // ========================================================
    // AYUDAS
    // ========================================================

    private static List<PalabraPdf> PalabrasDerechaDe(
        List<PalabraPdf> palabras,
        string etiqueta)
    {
        foreach (var palabra in palabras)
        {
            if (palabra.Texto.ToLowerInvariant().StartsWith(etiqueta))
            {
                return palabras
                    .Where(p => p.X0 > palabra.X1 - 1
                                && Math.Abs(p.CentroY - palabra.CentroY) <= 6)
                    .OrderBy(p => p.X0)
                    .ToList();
            }
        }

        return new List<PalabraPdf>();
    }


    private static string FechaImpresa(List<PalabraPdf> palabras)
    {
        if (palabras.Count < 3)
        {
            return "";
        }

        if (!MesesAbrev.TryGetValue(
                palabras[0].Texto.Trim().Trim('.').ToLowerInvariant(),
                out int mes))
        {
            return "";
        }

        string dia = palabras[1].Texto;
        string anio = palabras[2].Texto;

        if (!dia.All(char.IsDigit) || !anio.All(char.IsDigit))
        {
            return "";
        }

        return int.Parse(dia).ToString("00", CultureInfo.InvariantCulture)
            + "-" + mes.ToString("00", CultureInfo.InvariantCulture)
            + "-" + anio;
    }


    private static string FechaFormulario(string texto)
    {
        texto = (texto ?? "").Trim().Replace("/", "-");

        var coincidencia = Regex.Match(
            texto,
            @"^(\d{1,2})-(\d{1,2})-(\d{2,4})$");

        if (!coincidencia.Success)
        {
            return texto;
        }

        int dia = int.Parse(coincidencia.Groups[1].Value);
        int mes = int.Parse(coincidencia.Groups[2].Value);
        string anio = coincidencia.Groups[3].Value;

        if (anio.Length == 2)
        {
            anio = (int.Parse(anio) > 30 ? "19" : "20") + anio;
        }

        return dia.ToString("00", CultureInfo.InvariantCulture)
            + "-" + mes.ToString("00", CultureInfo.InvariantCulture)
            + "-" + anio;
    }


    private static object Numero(string texto)
    {
        texto = (texto ?? "").Trim();

        return texto.Length > 0 && texto.All(char.IsDigit)
            ? int.Parse(texto, CultureInfo.InvariantCulture)
            : texto;
    }


    private static string LimpiarEspacios(string texto)
    {
        return Regex.Replace(texto ?? "", @"\s+", " ").Trim();
    }


    /// <summary>Cociente avance/ancho de un par de caracteres, si es
    /// una medida plausible (entre 0,2 y 20).</summary>
    private static bool CandidataDePar(
        CaracterPdf anterior,
        CaracterPdf siguiente,
        out float escala)
    {
        escala = 0;

        if (string.IsNullOrWhiteSpace(anterior.Texto)
            || string.IsNullOrWhiteSpace(siguiente.Texto))
        {
            return false;
        }

        float avance = siguiente.X - anterior.X;

        if (avance <= 0.2f || avance >= 60f
            || anterior.AnchoItext <= 0.05f)
        {
            return false;
        }

        escala = avance / anterior.AnchoItext;

        return escala > 0.2f && escala < 20f;
    }


    /// <summary>Clave de deduplicación de nombres (sin espacios ni
    /// acentos, para unir variantes de un mismo nombre).</summary>
    private static string ClaveNombre(string nombre)
    {
        string descompuesto = LimpiarEspacios(nombre)
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var texto = new StringBuilder();

        foreach (char caracter in descompuesto)
        {
            if (char.IsWhiteSpace(caracter))
            {
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            texto.Append(caracter);
        }

        return texto.ToString();
    }


    /// <summary>
    /// Filas de meses (etiqueta a la izquierda), uniendo las letras de
    /// cada línea por si el productor dibuja cada carácter suelto.
    /// </summary>
    private static List<(string Mes, float Y)> FilasDeMeses(
        ContenidoPagina contenido,
        float limiteX)
    {
        var filas = new List<(string Mes, float Y)>();

        foreach (var grupo in contenido.Palabras
            .Where(p => p.X0 < limiteX && p.Y0 > 140)
            .GroupBy(p => (int)Math.Round(p.CentroY / 3.0)))
        {
            var palabras = grupo
                .OrderBy(p => p.X0)
                .ToList();

            // 1) Una palabra que sea exactamente el mes.
            var exacta = palabras.FirstOrDefault(p =>
                Meses.Contains(p.Texto.Trim().ToLowerInvariant()));

            if (exacta != null)
            {
                filas.Add((
                    exacta.Texto.Trim().ToLowerInvariant(),
                    exacta.CentroY));

                continue;
            }

            // 2) Letras sueltas que, unidas, forman el mes.
            string texto = LimpiarEspacios(
                    string.Concat(palabras.Select(p => p.Texto)))
                .ToLowerInvariant();

            if (Meses.Contains(texto))
            {
                filas.Add((texto, palabras[0].CentroY));
            }
        }

        filas.Sort((a, b) => a.Y.CompareTo(b.Y));

        return filas;
    }


    private static bool NombreValido(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)
            || nombre.Trim().Length < 3)
        {
            return false;
        }

        return nombre.Any(char.IsLetter);
    }


    private static int GrupoDeRuta(string ruta)
    {
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));

        while (!string.IsNullOrEmpty(carpeta))
        {
            string nombre = Path.GetFileName(carpeta);

            var coincidencia = Regex.Match(nombre, @"^[Gg](\d{1,2})$");

            if (coincidencia.Success)
            {
                return int.Parse(coincidencia.Groups[1].Value);
            }

            carpeta = Path.GetDirectoryName(carpeta);
        }

        return 0;
    }


    private static int CompararArchivos(string a, string b)
    {
        int pa = Prioridad(Path.GetFileName(a));
        int pb = Prioridad(Path.GetFileName(b));

        if (pa != pb)
        {
            return pa.CompareTo(pb);
        }

        int anioA = AnioDelNombre(Path.GetFileName(a));
        int anioB = AnioDelNombre(Path.GetFileName(b));

        if (anioA != anioB)
        {
            return anioB.CompareTo(anioA);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }


    private static int Prioridad(string nombre)
    {
        string n = nombre.ToLowerInvariant();

        if (n.StartsWith("publicadores"))
        {
            return 0;
        }

        if (n.Contains("tarjeta")
            || n.StartsWith("prec")
            || n.Contains("precursor"))
        {
            return 1;
        }

        if (n.StartsWith("s-21") || n.StartsWith("s21"))
        {
            return 2;
        }

        return 3;
    }


    private static int AnioDelNombre(string nombre)
    {
        var coincidencia = Regex.Match(nombre, @"(20\d{2})");

        return coincidencia.Success
            ? int.Parse(coincidencia.Groups[1].Value)
            : 0;
    }
}
