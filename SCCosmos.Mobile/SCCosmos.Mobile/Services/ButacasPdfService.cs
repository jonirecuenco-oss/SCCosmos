using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

using SCCosmos.Mobile.Models;

using ITextCell = iText.Layout.Element.Cell;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// PDF con la disposición de butacas de una escuela: mesa del
/// instructor centrada delante de la primera fila, filas, pasillos y
/// nombres de los ocupantes.
/// </summary>
public static class ButacasPdfService
{
    public static void Generar(
        EscuelaClaseMobile escuela,
        List<EscuelaAlumnoMobile> alumnos,
        string ruta)
    {
        int filas =
            escuela.FilasEfectivas;

        int columnas =
            escuela.ColumnasEfectivas;

        List<int> pasillos =
            escuela.PasillosLista;

        using (var pdf = new PdfDocument(new PdfWriter(ruta)))
        {
            using var documento =
                new Document(
                    pdf,
                    iText.Kernel.Geom.PageSize.A4.Rotate());

            documento.SetMargins(28, 28, 28, 28);

            documento.Add(
                new Paragraph("DISPOSICIÓN DE BUTACAS")
                    .SetFontSize(16)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(
                        escuela.SemanaTexto
                        + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                            ? ""
                            : " · " + escuela.Congregacion)
                        + (string.IsNullOrWhiteSpace(escuela.InstructorNombre)
                            ? ""
                            : " · Instructor: " + escuela.InstructorNombre))
                    .SetFontSize(10.5f)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(8));

            // Anchos: una columna por butaca + un hueco por pasillo.
            var anchos =
                new List<float>();

            for (int c = 1; c <= columnas; c++)
            {
                anchos.Add(1f);

                if (pasillos.Contains(c))
                {
                    anchos.Add(0.22f);
                }
            }

            var tabla =
                new Table(
                        iText.Layout.Properties.UnitValue.CreatePercentArray(
                            anchos.ToArray()))
                    .UseAllAvailableWidth();

            // Mesa del instructor centrada, delante de la primera fila.
            tabla.AddCell(
                new ITextCell(1, anchos.Count)
                    .Add(new Paragraph("MESA DEL INSTRUCTOR")
                        .SetFontSize(11)
                        .SetTextAlignment(
                            iText.Layout.Properties.TextAlignment.CENTER))
                    .SetBackgroundColor(
                        new iText.Kernel.Colors.DeviceRgb(0xCC, 0xD9, 0xE8))
                    .SetPadding(6));

            int numero =
                0;

            for (int f = 1; f <= filas; f++)
            {
                for (int c = 1; c <= columnas; c++)
                {
                    numero++;

                    var ocupante =
                        alumnos.FirstOrDefault(
                            a => a.Asiento == numero);

                    var celda =
                        new ITextCell()
                            .SetPadding(4);

                    celda.Add(
                        new Paragraph(numero.ToString())
                            .SetFontSize(12)
                            .SetMargin(0)
                            .SetTextAlignment(
                                iText.Layout.Properties.TextAlignment.CENTER));

                    celda.Add(
                        new Paragraph(ocupante == null ? "Libre" : ocupante.Nombre)
                            .SetFontSize(8.5f)
                            .SetMargin(0)
                            .SetTextAlignment(
                                iText.Layout.Properties.TextAlignment.CENTER));

                    if (ocupante != null)
                    {
                        celda.SetBackgroundColor(
                            new iText.Kernel.Colors.DeviceRgb(0xDD, 0xE8, 0xF5));
                    }

                    tabla.AddCell(
                        celda);

                    if (pasillos.Contains(c))
                    {
                        tabla.AddCell(
                            new ITextCell()
                                .SetBorder(
                                    iText.Layout.Borders.Border.NO_BORDER));
                    }
                }
            }

            documento.Add(
                tabla);

            documento.Add(
                new Paragraph("Butacas ocupadas: sombreadas con el nombre. Libres: solo el número.")
                    .SetFontSize(8.5f)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER)
                    .SetMarginTop(10f));
        }
    }
}
