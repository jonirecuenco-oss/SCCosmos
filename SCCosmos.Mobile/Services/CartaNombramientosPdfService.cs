using System.Globalization;

using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Genera la Carta de nombramientos en PDF directamente con iText
/// (sin Word), a partir de las decisiones S-2 marcadas como aprobadas.
/// Sustituye en Android al flujo DOCX→PDF de WPF.
/// </summary>
public static class CartaNombramientosPdfService
{
    public static byte[] Generar(
        string congregacion,
        string congregacionCodigo,
        string nombreSuperintendente,
        string circuito,
        DateTime fecha,
        IEnumerable<RecomendacionS2Mobile> aprobadas)
    {
        var lista =
            (aprobadas ?? Enumerable.Empty<RecomendacionS2Mobile>())
                .ToList();

        List<string> bajas =
            lista.Where(EsBaja)
                .Select(r => r.Hermano ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        List<string> ancianos =
            lista.Where(r =>
                    !EsBaja(r)
                    &&
                    TextoNombramiento(r).Contains(
                        "anciano",
                        StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Hermano ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        List<string> siervos =
            lista.Where(r =>
                    !EsBaja(r)
                    &&
                    TextoNombramiento(r).Contains(
                        "siervo",
                        StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Hermano ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        using var salida =
            new MemoryStream();

        using (var pdf =
               new PdfDocument(
                   new PdfWriter(salida)))
        {
            using var documento =
                new Document(pdf);

            documento.Add(
                new Paragraph("CARTA DE NOMBRAMIENTOS")
                    .SetFontSize(16)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(6));

            string congre =
                (congregacion ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(
                    congregacionCodigo))
            {
                congre += " (" +
                          congregacionCodigo.Trim() +
                          ")";
            }

            documento.Add(
                new Paragraph(
                        $"Circuito: {circuito}")
                    .SetFontSize(11));

            documento.Add(
                new Paragraph(
                        $"Congregación: {congre}")
                    .SetFontSize(11));

            documento.Add(
                new Paragraph(
                        $"Fecha: {fecha.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-ES"))}")
                    .SetFontSize(11));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(6));

            documento.Add(
                new Paragraph(
                        "Estimados hermanos:")
                    .SetFontSize(11));

            documento.Add(
                new Paragraph(
                        "Por medio de la presente les comunicamos las " +
                        "siguientes decisiones relativas a los hermanos " +
                        "de su congregación.")
                    .SetFontSize(11));

            AñadirSeccion(
                documento,
                "Nombrados como ancianos",
                ancianos);

            AñadirSeccion(
                documento,
                "Nombrados como siervos ministeriales",
                siervos);

            AñadirSeccion(
                documento,
                "Cesan en sus responsabilidades",
                bajas);

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(8));

            documento.Add(
                new Paragraph(
                        "Reciban un cordial saludo,")
                    .SetFontSize(11));

            documento.Add(
                new Paragraph(
                        string.IsNullOrWhiteSpace(
                            nombreSuperintendente)
                            ? "El superintendente de circuito"
                            : nombreSuperintendente)
                    .SetFontSize(11));
        }

        return salida.ToArray();
    }


    private static void AñadirSeccion(
        Document documento,
        string titulo,
        List<string> nombres)
    {
        documento.Add(
            new Paragraph(" ")
                .SetFontSize(6));

        documento.Add(
            new Paragraph(titulo)
                .SetFontSize(12));

        if (nombres.Count == 0)
        {
            documento.Add(
                new Paragraph("— (ninguno)")
                    .SetFontSize(11));

            return;
        }

        foreach (string nombre in nombres)
        {
            documento.Add(
                new Paragraph("• " + nombre)
                    .SetFontSize(11));
        }
    }


    private static bool EsBaja(
        RecomendacionS2Mobile rec)
    {
        string texto =
            ((rec.Tipo ?? "") + " " +
             (rec.RecomendadoComo ?? ""))
                .ToLowerInvariant();

        return texto.Contains("baja")
               || texto.Contains("cese")
               || texto.Contains("cesar")
               || texto.Contains("remoci")
               || texto.Contains("relevo")
               || texto.Contains("dejar de servir")
               || texto.Contains("deja de servir");
    }


    private static string TextoNombramiento(
        RecomendacionS2Mobile rec)
    {
        return (rec.RecomendadoComo ?? "") +
               " " +
               (rec.Tipo ?? "");
    }
}
