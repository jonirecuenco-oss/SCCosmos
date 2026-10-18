using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// PDF de las secciones especiales del Archivo (hermanos con
/// potencial, problemas pendientes) con iText.
/// </summary>
public static class ArchivoEspecialPdfService
{
    public static byte[] Generar(
        string titulo,
        IList<ItemEspecialUi> filas)
    {
        using var salida =
            new MemoryStream();

        using (var pdf =
               new PdfDocument(
                   new PdfWriter(salida)))
        {
            using var documento =
                new Document(pdf);

            documento.Add(
                new Paragraph(titulo)
                    .SetFontSize(16)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(
                        $"Generado el " +
                        $"{DateTime.Now:dd/MM/yyyy HH:mm}")
                    .SetFontSize(9));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(6));

            var tabla =
                new Table(
                        UnitValue.CreatePercentArray(
                            new[] { 35f, 65f }))
                    .UseAllAvailableWidth();

            tabla.AddHeaderCell("Referencia");
            tabla.AddHeaderCell("Descripción");

            foreach (ItemEspecialUi fila in filas)
            {
                tabla.AddCell(
                    fila.Titulo ?? "");

                tabla.AddCell(
                    fila.Descripcion ?? "");
            }

            documento.Add(
                tabla);
        }

        return salida.ToArray();
    }
}
