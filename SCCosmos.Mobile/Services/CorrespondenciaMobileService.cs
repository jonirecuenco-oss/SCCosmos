using System.Net;
using System.Text.RegularExpressions;

using iText.Forms;
using iText.Kernel.Pdf;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Lógica compartida para preparar la correspondencia de las visitas
/// (S-302, documentación, WhatsApp y recordatorios) reutilizando las
/// categorías y claves de configuración que ya usa SCCosmos WPF.
///
/// IMPORTANTE: este servicio NUNCA envía nada automáticamente. Solo
/// genera documentos y abre las aplicaciones del dispositivo con los
/// datos precargados; el usuario decide si envía.
/// </summary>
public static class CorrespondenciaMobileService
{
    public const string CategoriaAvisoVisita =
        "Aviso de la visita";

    public const string CategoriaAvisoVisitaSCS =
        "Aviso S-302 (visita de SCS)";

    public const string CategoriaDocumentacion =
        "Documentación";

    // Claves de configuración compartidas con WPF.
    private const string ClaveWhatsApp =
        "Email_WhatsApp al CCA";

    // ========================================================================
    // DETECCIÓN DE TIPO
    // ========================================================================

    public static bool EsVisitaSCS(
        SemanaCalendario semana)
    {
        string tipo =
            semana.Tipo ?? "";

        return tipo.Contains(
                   "SCS",
                   StringComparison.OrdinalIgnoreCase)
               ||
               tipo.Equals(
                   "Visita de SCS",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string ObtenerCategoriaS302(
        SemanaCalendario semana) =>
        EsVisitaSCS(semana)
            ? CategoriaAvisoVisitaSCS
            : CategoriaAvisoVisita;

    // ========================================================================
    // WHATSAPP
    // ========================================================================

    /// <summary>
    /// Convierte el HTML enriquecido de las plantillas WPF al formato
    /// de texto que entiende WhatsApp (*negrita*, _cursiva_, saltos).
    /// </summary>
    public static string ConvertirHtmlAWhatsApp(
        string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        string mensaje =
            html
                .Replace("<br>", "\n")
                .Replace("<br/>", "\n")
                .Replace("<br />", "\n")
                .Replace("</p>", "\n\n")
                .Replace("<b>", "*")
                .Replace("</b>", "*")
                .Replace("<strong>", "*")
                .Replace("</strong>", "*")
                .Replace("<i>", "_")
                .Replace("</i>", "_")
                .Replace("<em>", "_")
                .Replace("</em>", "_");

        mensaje =
            Regex.Replace(
                mensaje,
                "<.*?>",
                string.Empty);

        mensaje =
            WebUtility.HtmlDecode(mensaje);

        return mensaje.Trim();
    }

    /// <summary>
    /// Normaliza un teléfono español para wa.me:
    /// quita espacios, guiones y "+"; antepone 34 si son 9 dígitos.
    /// </summary>
    public static string NormalizarTelefono(
        string? telefono)
    {
        string limpio =
            (telefono ?? "")
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("+", "")
                .Trim();

        if (limpio.Length == 9 &&
            !limpio.StartsWith("34"))
        {
            limpio = "34" + limpio;
        }

        return limpio;
    }

    /// <summary>
    /// Busca al coordinador de la congregación de la semana
    /// (mismo criterio que WPF: privilegio que contiene "Coordinador").
    /// </summary>
    public static Persona? BuscarCoordinador(
        SemanaCalendario semana)
    {
        if (semana is null ||
            string.IsNullOrWhiteSpace(
                semana.Congregacion))
        {
            return null;
        }

        List<Persona> personas =
            DatabaseHelper.ObtenerPersonas();

        return personas.FirstOrDefault(
            p =>
                string.Equals(
                    p.CongregacionNombre,
                    semana.Congregacion,
                    StringComparison.OrdinalIgnoreCase)
                &&
                !string.IsNullOrWhiteSpace(
                    p.Privilegios)
                &&
                p.Privilegios.Contains(
                    "Coordinador",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Construye el mensaje de WhatsApp para el coordinador.
    /// Devuelve null si no se pudo preparar (sin coordinador/teléfono).
    /// </summary>
    public static string? ConstruirMensajeWhatsApp(
        SemanaCalendario semana,
        out string error)
    {
        error = "";

        Persona? coordinador =
            BuscarCoordinador(semana);

        if (coordinador is null)
        {
            error =
                "No se ha encontrado el coordinador de la " +
                $"congregación {semana.Congregacion} en la base de datos.";

            return null;
        }

        string telefono =
            NormalizarTelefono(coordinador.Telefono);

        if (string.IsNullOrWhiteSpace(telefono))
        {
            error =
                "El coordinador no tiene teléfono registrado.";

            return null;
        }

        string plantillaHtml =
            DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                ClaveWhatsApp,
                "Querido hermano [Coordinador], te escribo en " +
                "relación a la próxima visita...");

        string mensaje =
            ConvertirHtmlAWhatsApp(plantillaHtml);

        string nombreHermano =
            !string.IsNullOrWhiteSpace(
                coordinador.NombreComun)
                ? coordinador.NombreComun
                : coordinador.Nombre;

        mensaje =
            mensaje
                .Replace(
                    "[Coordinador]",
                    nombreHermano)
                .Replace(
                    "[Congregacion]",
                    semana.Congregacion ?? "")
                .Replace(
                    "[Tipo]",
                    semana.Tipo ?? "")
                .Trim();

        return
            $"https://wa.me/{telefono}" +
            $"?text={Uri.EscapeDataString(mensaje)}";
    }

    // ========================================================================
    // S-302
    // ========================================================================

    public static string ConstruirTextoFechas(
        DateTime fechaMartes)
    {
        DateTime inicio =
            fechaMartes.Date;

        DateTime fin =
            inicio.AddDays(5);

        return
            $"Del {inicio.ToString("dd-MMM-yy").ToLower()} " +
            $"al {fin.ToString("dd-MMM-yy").ToLower()}";
    }

    /// <summary>
    /// Rellena la plantilla S-302 (mismos campos Text1..Text4 que WPF)
    /// y devuelve el PDF generado en memoria.
    /// </summary>
    public static byte[]? GenerarS302Pdf(
        SemanaCalendario semana,
        out string error)
    {
        error = "";

        string categoria =
            ObtenerCategoriaS302(semana);

        List<DocumentoVisitaMobile> plantillas =
            DatabaseHelper
                .ObtenerDocumentosVisitaMobile(
                    categoria);

        DocumentoVisitaMobile? plantilla =
            plantillas.FirstOrDefault(
                d =>
                    d.Contenido.Length > 0
                    ||
                    File.Exists(d.RutaLocal));

        if (plantilla is null)
        {
            error =
                "No hay ninguna plantilla S-302 subida en la " +
                $"categoría «{categoria}». Súbela desde la " +
                "correspondencia en el ordenador antes de continuar.";

            return null;
        }

        byte[] plantillaBytes =
            ObtenerContenido(plantilla);

        if (plantillaBytes.Length == 0)
        {
            error =
                "La plantilla S-302 está vacía o no es accesible.";

            return null;
        }

        string nombreSC =
            DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                "NombreSC",
                "");

        string apellidoSC =
            DatabaseHelper.ObtenerConfiguracionVisitaMobile(
                "ApellidoSC",
                "");

        string nombreCompletoSC =
            $"{nombreSC} {apellidoSC}".Trim();

        string text2Value =
            EsVisitaSCS(semana)
                ? "SCS"
                : nombreCompletoSC;

        try
        {
            using var entrada =
                new MemoryStream(
                    plantillaBytes,
                    writable: false);

            using var salida =
                new MemoryStream();

            using (
                var pdf =
                    new PdfDocument(
                        new PdfReader(entrada),
                        new PdfWriter(salida)))
            {
                PdfAcroForm? formulario =
                    PdfAcroForm.GetAcroForm(
                        pdf,
                        true);

                if (formulario is null)
                {
                    error =
                        "La plantilla S-302 no contiene un " +
                        "formulario PDF compatible.";

                    return null;
                }

                formulario
                    .GetField("Text1")
                    ?.SetValue(semana.Congregacion ?? "");

                formulario
                    .GetField("Text2")
                    ?.SetValue(text2Value);

                formulario
                    .GetField("Text3")
                    ?.SetValue(
                        ConstruirTextoFechas(
                            semana.FechaMartes));

                formulario
                    .GetField("Text4")
                    ?.SetValue(nombreCompletoSC);

                formulario.FlattenFields();
            }

            return salida.ToArray();
        }
        catch (Exception ex)
        {
            error =
                "No se pudo generar el PDF S-302: " +
                ex.Message;

            return null;
        }
    }

    private static byte[] ObtenerContenido(
        DocumentoVisitaMobile documento)
    {
        if (documento.Contenido.Length > 0)
        {
            return documento.Contenido;
        }

        if (!string.IsNullOrWhiteSpace(
                documento.RutaLocal)
            &&
            File.Exists(
                documento.RutaLocal))
        {
            try
            {
                return
                    File.ReadAllBytes(
                        documento.RutaLocal);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        return Array.Empty<byte>();
    }
}
