using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

using ITextCell = iText.Layout.Element.Cell;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// PDFs del programa de la escuela: talleres, horario y programa de
/// limpieza (iText, igual que en Windows).
/// </summary>
public static class ProgramaEscuelaPdfService
{
    private static Dictionary<int, string> _congregaciones;


    private static string CongregacionDe(
        int personaId)
    {
        try
        {
            if (_congregaciones is null)
            {
                _congregaciones =
                    new Dictionary<int, string>();

                foreach (Persona p in
                         DatabaseHelper.ObtenerPersonas())
                {
                    _congregaciones[p.Id] =
                        string.IsNullOrWhiteSpace(p.CongregacionNombre)
                            ? p.CongregacionCodigo
                            : p.CongregacionNombre;
                }
            }

            if (_congregaciones.TryGetValue(
                    personaId,
                    out string congregacion)
                && !string.IsNullOrWhiteSpace(congregacion))
            {
                return congregacion;
            }
        }
        catch
        {
        }

        return "sin congregación";
    }


    // =========================================================
    // TALLERES
    // =========================================================

    public static void GenerarTalleres(
        EscuelaClaseMobile escuela,
        List<EscuelaTallerPuntoMobile> puntos,
        string ruta)
    {
        using (var pdf = new PdfDocument(new PdfWriter(ruta)))
        {
            using var documento =
                new Document(
                    pdf,
                    iText.Kernel.Geom.PageSize.A4.Rotate());

            documento.SetMargins(28, 28, 28, 28);

            documento.Add(
                new Paragraph("TALLERES DE LA ESCUELA")
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

            var tabla =
                new Table(
                        iText.Layout.Properties.UnitValue.CreatePercentArray(
                            new[] { 7f, 5f, 40f, 18f, 18f, 8f }))
                    .UseAllAvailableWidth();

            tabla.AddHeaderCell("Taller");
            tabla.AddHeaderCell("Min");
            tabla.AddHeaderCell("Tema");
            tabla.AddHeaderCell("Asignado");
            tabla.AddHeaderCell("Acompañante");
            tabla.AddHeaderCell("Ensayo");

            foreach (EscuelaTallerPuntoMobile punto in puntos)
            {
                tabla.AddCell($"{punto.TallerTexto} · {punto.Numero}");
                tabla.AddCell(punto.Minutos);
                tabla.AddCell(punto.Tema);
                tabla.AddCell(
                    string.IsNullOrWhiteSpace(punto.AsignadoNombre)
                        ? "—"
                        : punto.AsignadoNombre + " (" + CongregacionDe(punto.AsignadoId) + ")");
                tabla.AddCell(
                    string.IsNullOrWhiteSpace(punto.AcompananteNombre)
                        ? "—"
                        : punto.AcompananteNombre + " (" + CongregacionDe(punto.AcompananteId) + ")");
                tabla.AddCell(
                    string.IsNullOrWhiteSpace(punto.HoraEnsayo)
                        ? "—"
                        : punto.HoraEnsayo);
            }

            documento.Add(
                tabla);
        }
    }


    // =========================================================
    // HORARIO
    // =========================================================

    public static void GenerarHorario(
        EscuelaClaseMobile escuela,
        List<EscuelaHorarioDiaMobile> dias,
        string ruta)
    {
        using (var pdf = new PdfDocument(new PdfWriter(ruta)))
        {
            using var documento =
                new Document(
                    pdf,
                    iText.Kernel.Geom.PageSize.A4.Rotate());

            documento.SetMargins(24, 24, 24, 24);

            documento.Add(
                new Paragraph("HORARIO DE LA ESCUELA")
                    .SetFontSize(16)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(
                        escuela.SemanaTexto
                        + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                            ? ""
                            : " · " + escuela.Congregacion))
                    .SetFontSize(10.5f)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(8));

            var tabla =
                new Table(
                        iText.Layout.Properties.UnitValue.CreatePercentArray(
                            new[] { 1f, 1f, 1f, 1f, 1f, 1f }))
                    .UseAllAvailableWidth();

            var fondoDia =
                new iText.Kernel.Colors.DeviceRgb(0xD9, 0xE2, 0xF3);

            var fondoOracion =
                new iText.Kernel.Colors.DeviceRgb(0xE2, 0xEF, 0xDA);

            ITextCell Celda(
                string linea1,
                string linea2,
                bool esOracion)
            {
                var celda =
                    new ITextCell()
                        .SetPadding(4);

                if (!string.IsNullOrWhiteSpace(linea1))
                {
                    celda.Add(
                        new Paragraph(linea1)
                            .SetFontSize(9.5f)
                            .SetMargin(0)
                            .SetTextAlignment(
                                iText.Layout.Properties.TextAlignment.CENTER));
                }

                if (!string.IsNullOrWhiteSpace(linea2))
                {
                    celda.Add(
                        new Paragraph(linea2)
                            .SetFontSize(8.5f)
                            .SetMargin(0)
                            .SetTextAlignment(
                                iText.Layout.Properties.TextAlignment.CENTER));
                }

                if (esOracion)
                {
                    celda.SetBackgroundColor(
                        fondoOracion);
                }

                return celda;
            }

            ITextCell CeldaPausa(
                string texto,
                int columnas)
            {
                return new ITextCell(1, columnas)
                    .Add(new Paragraph(texto)
                        .SetFontSize(9)
                        .SetTextAlignment(
                            iText.Layout.Properties.TextAlignment.CENTER))
                    .SetBackgroundColor(
                        fondoDia)
                    .SetPadding(3);
            }

            foreach (EscuelaHorarioDiaMobile dia in dias)
            {
                tabla.AddHeaderCell(
                    new ITextCell()
                        .Add(new Paragraph(dia.Dia.ToUpperInvariant())
                            .SetFontSize(11)
                            .SetTextAlignment(
                                iText.Layout.Properties.TextAlignment.CENTER))
                        .SetTextAlignment(
                            iText.Layout.Properties.TextAlignment.CENTER)
                        .SetBackgroundColor(
                            fondoDia));
            }

            foreach (EscuelaHorarioDiaMobile dia in dias)
            {
                tabla.AddCell(
                    Celda(
                        DatabaseHelper.CancionInicialDe(dia.Dia),
                        "Oración: "
                        + (string.IsNullOrWhiteSpace(dia.OracionInicialNombre)
                            ? "—"
                            : dia.OracionInicialNombre),
                        false));
            }

            for (int i = 0; i < 6; i++)
            {
                if (i == 2)
                {
                    tabla.AddCell(
                        CeldaPausa(
                            "Pausa 15 minutos",
                            6));
                }

                if (i == 3)
                {
                    foreach (EscuelaHorarioDiaMobile dia in dias)
                    {
                        tabla.AddCell(
                            Celda(
                                "Oración",
                                string.IsNullOrWhiteSpace(dia.OracionIntermediaNombre)
                                    ? "—"
                                    : dia.OracionIntermediaNombre,
                                true));
                    }

                    tabla.AddCell(
                        CeldaPausa(
                            "Pausa 60 minutos",
                            6));
                }

                if (i == 4)
                {
                    tabla.AddCell(
                        CeldaPausa(
                            "Pausa 15 minutos",
                            5));

                    tabla.AddCell(
                        CeldaPausa(
                            "Pausa 10 minutos",
                            1));
                }

                foreach (EscuelaHorarioDiaMobile dia in dias)
                {
                    var lecciones =
                        DatabaseHelper.LeccionesDe(
                            dia.Dia);

                    string linea1 =
                        "";

                    string linea2 =
                        "";

                    if (i < lecciones.Count)
                    {
                        linea1 =
                            lecciones[i].Nombre;

                        linea2 =
                            "(" + lecciones[i].Horario + ")";
                    }

                    tabla.AddCell(
                        Celda(
                            linea1,
                            linea2,
                            false));
                }
            }

            foreach (EscuelaHorarioDiaMobile dia in dias)
            {
                tabla.AddCell(
                    Celda(
                        DatabaseHelper.CancionFinalDe(dia.Dia),
                        "Oración: "
                        + (string.IsNullOrWhiteSpace(dia.OracionFinalNombre)
                            ? "—"
                            : dia.OracionFinalNombre),
                        false));
            }

            documento.Add(
                tabla);
        }
    }


    // =========================================================
    // PROGRAMA DE LIMPIEZA
    // =========================================================

    public static void GenerarLimpieza(
        EscuelaClaseMobile escuela,
        List<EscuelaAlumnoMobile> alumnos,
        string ruta)
    {
        using (var pdf = new PdfDocument(new PdfWriter(ruta)))
        {
            using var documento =
                new Document(
                    pdf,
                    iText.Kernel.Geom.PageSize.A4);

            documento.SetMargins(36, 36, 36, 36);

            documento.Add(
                new Paragraph("PROGRAMA DE LIMPIEZA")
                    .SetFontSize(16)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(
                        escuela.SemanaTexto
                        + (string.IsNullOrWhiteSpace(escuela.Congregacion)
                            ? ""
                            : " · " + escuela.Congregacion))
                    .SetFontSize(10.5f)
                    .SetTextAlignment(
                        iText.Layout.Properties.TextAlignment.CENTER));

            documento.Add(
                new Paragraph(" ")
                    .SetFontSize(8));

            var tabla =
                new Table(
                        iText.Layout.Properties.UnitValue.CreatePercentArray(
                            new[] { 25f, 75f }))
                    .UseAllAvailableWidth();

            tabla.AddHeaderCell("Día");
            tabla.AddHeaderCell("Quién limpia");

            foreach (string dia in
                     new[]
                     {
                         "Lunes",
                         "Martes",
                         "Miércoles",
                         "Jueves",
                         "Viernes",
                         "Sábado"
                     })
            {
                var nombres =
                    alumnos
                        .Where(
                            a => string.Equals(
                                a.LimpiezaDia,
                                dia,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(
                            a => a.Nombre)
                        .ToList();

                tabla.AddCell(dia);
                tabla.AddCell(
                    nombres.Count == 0
                        ? "— nadie todavía —"
                        : string.Join(
                            ", ",
                            nombres));
            }

            documento.Add(
                tabla);
        }
    }
}
