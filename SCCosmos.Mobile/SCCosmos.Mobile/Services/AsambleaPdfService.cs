using System.Globalization;

using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout.Renderer;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

// Los nombres chocan con los tipos de MAUI: se fijan los de iText.
using Border = iText.Layout.Borders.Border;
using Cell = iText.Layout.Element.Cell;
using Color = iText.Kernel.Colors.Color;
using TextAlignment = iText.Layout.Properties.TextAlignment;
using VerticalAlignment = iText.Layout.Properties.VerticalAlignment;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Generación de PDF del programa y del organigrama de una asamblea con
/// iText (el mismo motor y el mismo diseño que la aplicación de Windows).
/// </summary>
public static class AsambleaPdfService
{
    // Colores del organigrama (los mismos que en Windows).
    private static readonly Color ColorSupAsamblea =
        new DeviceRgb(184, 220, 235);

    private static readonly Color ColorSupAuxiliar =
        new DeviceRgb(250, 203, 164);

    private static readonly Color ColorDepartamentosAzul =
        new DeviceRgb(222, 235, 247);

    private static readonly Color ColorDepartamentosNaranja =
        new DeviceRgb(253, 233, 217);

    private static readonly Color ColorAuditor =
        new DeviceRgb(146, 205, 220);

    private static readonly Color ColorSeguridad =
        new DeviceRgb(217, 150, 148);

    private const float Separacion =
        12f;


    /// <summary>
    /// Programa de la asamblea: mismo diseño que el PDF de Windows
    /// (título centrado, texto temático y tabla maestra de cuatro columnas
    /// con los turnos de mañana y tarde).
    /// </summary>
    public static byte[] GenerarPrograma(
        AsambleaMobile asamblea,
        IList<ParteAsambleaMobile> partes)
    {
        using var salida =
            new MemoryStream();

        using (var pdf =
               new PdfDocument(
                   new PdfWriter(salida)))
        {
            using var documento =
                new Document(pdf);

            documento.SetMargins(
                15,
                15,
                15,
                15);

            PdfFont fuenteNegrita =
                Fuente(
                    true);

            PdfFont fuenteNormal =
                Fuente(
                    false);

            string titulo =
                string.IsNullOrWhiteSpace(
                    asamblea.Titulo)
                    ? "Asamblea de Circuito"
                    : asamblea.Titulo.ToUpper();

            documento.Add(
                new Paragraph(
                        new Text(titulo)
                            .SetFont(fuenteNegrita))
                    .SetTextAlignment(
                        TextAlignment.CENTER)
                    .SetFontSize(16)
                    .SetFontColor(
                        new DeviceRgb(47, 84, 150))
                    .SetMarginBottom(2));

            string texto =
                string.IsNullOrWhiteSpace(
                    asamblea.TextoTematico)
                    ? ""
                    : $"({asamblea.TextoTematico})";

            if (!string.IsNullOrWhiteSpace(
                    texto))
            {
                documento.Add(
                    new Paragraph(
                            new Text(texto)
                                .SetFont(fuenteNormal))
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetFontSize(10)
                        .SetFontColor(
                            ColorConstants.BLACK)
                        .SetMarginTop(0)
                        .SetMarginBottom(10));
            }
            else
            {
                documento.Add(
                    new Paragraph("")
                        .SetMarginBottom(10));
            }

            var tablaMaestra =
                new Table(
                        new float[] { 1, 3, 2, 3 })
                    .UseAllAvailableWidth();

            void AnadirFilas(
                string turno,
                List<ParteAsambleaMobile> delTurno)
            {
                if (delTurno.Count == 0)
                {
                    return;
                }

                tablaMaestra.AddCell(
                    new Cell(1, 4)
                        .Add(
                            new Paragraph(
                                    new Text(turno)
                                        .SetFont(fuenteNegrita))
                                .SetFontSize(10)
                                .SetFontColor(
                                    ColorConstants.DARK_GRAY))
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPaddingTop(6)
                        .SetPaddingBottom(3)
                        .SetPaddingLeft(0));

                foreach (ParteAsambleaMobile parte in delTurno)
                {
                    tablaMaestra.AddCell(
                        new Cell()
                            .Add(
                                new Paragraph(
                                        parte.Hora ?? "")
                                    .SetFont(fuenteNormal)
                                    .SetFontSize(8))
                            .SetPadding(2)
                            .SetVerticalAlignment(
                                VerticalAlignment.MIDDLE));

                    tablaMaestra.AddCell(
                        new Cell()
                            .Add(
                                new Paragraph(
                                        parte.Titulo ?? "")
                                    .SetFont(fuenteNormal)
                                    .SetFontSize(8))
                            .SetPadding(2)
                            .SetVerticalAlignment(
                                VerticalAlignment.MIDDLE));

                    tablaMaestra.AddCell(
                        new Cell()
                            .Add(
                                new Paragraph(
                                        parte.Asignado ?? "")
                                    .SetFont(fuenteNormal)
                                    .SetFontSize(8))
                            .SetPadding(2)
                            .SetVerticalAlignment(
                                VerticalAlignment.MIDDLE));

                    tablaMaestra.AddCell(
                        new Cell()
                            .Add(
                                new Paragraph(
                                        parte.Participantes ?? "")
                                    .SetFont(fuenteNormal)
                                    .SetFontSize(8))
                            .SetPadding(2)
                            .SetVerticalAlignment(
                                VerticalAlignment.MIDDLE));
                }
            }

            static bool EsTurno(
                ParteAsambleaMobile parte,
                string turno) =>
                string.Equals(
                    (parte.Turno ?? "").Trim(),
                    turno,
                    StringComparison.OrdinalIgnoreCase);

            AnadirFilas(
                "MAÑANA",
                partes
                    .Where(p => EsTurno(p, "MAÑANA")
                                || EsTurno(p, "Mañana"))
                    .OrderBy(p => p.Hora)
                    .ToList());

            AnadirFilas(
                "TARDE",
                partes
                    .Where(p => EsTurno(p, "TARDE")
                                || EsTurno(p, "Tarde"))
                    .OrderBy(p => p.Hora)
                    .ToList());

            // Cualquier otro turno que pudiera existir se añade al final,
            // para no perder ninguna parte.
            foreach (IGrouping<string, ParteAsambleaMobile> otro in
                     partes
                         .Where(p =>
                             !EsTurno(p, "MAÑANA")
                             && !EsTurno(p, "Mañana")
                             && !EsTurno(p, "TARDE")
                             && !EsTurno(p, "Tarde"))
                         .GroupBy(p =>
                             string.IsNullOrWhiteSpace(
                                 p.Turno)
                                 ? "(sin turno)"
                                 : p.Turno))
            {
                AnadirFilas(
                    otro.Key.ToUpper(),
                    otro
                        .OrderBy(p => p.Hora)
                        .ToList());
            }

            documento.Add(
                tablaMaestra);
        }

        return salida.ToArray();
    }


    /// <summary>
    /// Organigrama de la asamblea: mismo diseño que el PDF de Windows
    /// (cajas por departamento, dos columnas y líneas de unión).
    /// </summary>
    public static byte[] GenerarOrganigrama(
        AsambleaMobile asamblea,
        IList<OrganigramaAsambleaMobile> filas)
    {
        using var salida =
            new MemoryStream();

        using (var pdf =
               new PdfDocument(
                   new PdfWriter(salida)))
        {
            using var documento =
                new Document(pdf);

            documento.SetMargins(
                15,
                20,
                15,
                20);

            PdfFont fuenteNegrita =
                Fuente(
                    true);

            PdfFont fuenteNormal =
                Fuente(
                    false);

            string titulo =
                string.IsNullOrWhiteSpace(
                    asamblea.Titulo)
                    ? "ASAMBLEA DE CIRCUITO"
                    : asamblea.Titulo.ToUpper();

            documento.Add(
                new Paragraph(
                        new Text(titulo)
                            .SetFont(fuenteNegrita))
                    .SetTextAlignment(
                        TextAlignment.CENTER)
                    .SetFontSize(16)
                    .SetFontColor(
                        new DeviceRgb(47, 84, 150))
                    .SetMarginBottom(10f));

            var cabecera =
                new Table(
                        new float[] { 1, 1 })
                    .UseAllAvailableWidth()
                    .SetMarginBottom(8);

            cabecera.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(
                                FormatearFechaPdf(
                                    asamblea.Fecha))
                            .SetFont(fuenteNormal)
                            .SetFontSize(8)
                            .SetFontColor(
                                new DeviceRgb(47, 84, 150)))
                    .SetBorder(
                        Border.NO_BORDER)
                    .SetBorderTop(
                        new SolidBorder(
                            new DeviceRgb(143, 170, 220),
                            1))
                    .SetBorderBottom(
                        new SolidBorder(
                            new DeviceRgb(143, 170, 220),
                            1))
                    .SetPadding(2));

            cabecera.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(
                                asamblea.Lugar ?? "")
                            .SetFont(fuenteNormal)
                            .SetFontSize(8)
                            .SetFontColor(
                                new DeviceRgb(47, 84, 150))
                            .SetTextAlignment(
                                TextAlignment.RIGHT))
                    .SetBorder(
                        Border.NO_BORDER)
                    .SetBorderTop(
                        new SolidBorder(
                            new DeviceRgb(143, 170, 220),
                            1))
                    .SetBorderBottom(
                        new SolidBorder(
                            new DeviceRgb(143, 170, 220),
                            1))
                    .SetPadding(2));

            documento.Add(
                cabecera);

            Dictionary<string, DepartamentoPdf> departamentos =
                AgruparDepartamentos(
                    filas);

            DepartamentoPdf? Buscar(
                string nombre) =>
                departamentos.TryGetValue(
                    nombre,
                    out DepartamentoPdf? dept)
                    ? dept
                    : null;

            bool TieneGente(
                DepartamentoPdf? d)
            {
                if (d is null)
                {
                    return false;
                }

                return d.SuperintendenteId > 0
                       || d.AuditorId > 0
                       || d.Auxiliares.Any(
                           a => a > 0);
            }

            void AnadirHermano(
                Table destino,
                int id,
                bool esPrimero,
                bool esCompacta)
            {
                if (id <= 0)
                {
                    return;
                }

                InfoHermanoOrganigramaMobile info =
                    DatabaseHelper
                        .ObtenerInfoHermanoOrganigramaMobile(
                            id);

                var celda =
                    new Cell()
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0);

                if (!esPrimero)
                {
                    celda.SetBorderTop(
                        new SolidBorder(
                            ColorConstants.GRAY,
                            0.5f));
                }

                var interior =
                    new Table(
                            UnitValue.CreatePercentArray(
                                new float[] { 50, 50 }))
                        .UseAllAvailableWidth()
                        .SetMarginBottom(0);

                string nombreAMostrar =
                    !string.IsNullOrWhiteSpace(
                        info.NombreComun)
                        ? info.NombreComun
                        : info.NombreFormateado;

                string nombre =
                    nombreAMostrar +
                    (!string.IsNullOrWhiteSpace(
                         info.AnioNacimiento)
                         ? $" ({info.AnioNacimiento})"
                         : "");

                string telefono =
                    FormatearTelefonoPdf(
                        info.Telefono);

                string correo =
                    !string.IsNullOrWhiteSpace(
                        info.EmailJW)
                        ? info.EmailJW
                        : info.Email;

                float padArriba1 =
                    esCompacta ? 0f : 2f;

                float padArriba2 =
                    esCompacta ? 0f : 1f;

                float padAbajo2 =
                    esCompacta ? 0f : 2f;

                interior.AddCell(
                    new Cell()
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetVerticalAlignment(
                            VerticalAlignment.MIDDLE)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPaddingTop(padArriba1)
                        .SetPaddingBottom(0)
                        .Add(
                            new Paragraph(nombre)
                                .SetFont(fuenteNegrita)
                                .SetFontSize(7.5f)
                                .SetMargin(0)));

                interior.AddCell(
                    new Cell()
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetVerticalAlignment(
                            VerticalAlignment.MIDDLE)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPaddingTop(padArriba1)
                        .SetPaddingBottom(0)
                        .Add(
                            new Paragraph(
                                    info.Congregacion)
                                .SetFont(fuenteNormal)
                                .SetFontSize(7.5f)
                                .SetFontColor(
                                    ColorConstants.BLACK)
                                .SetMargin(0)));

                interior.AddCell(
                    new Cell()
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetVerticalAlignment(
                            VerticalAlignment.MIDDLE)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPaddingTop(padArriba2)
                        .SetPaddingBottom(padAbajo2)
                        .Add(
                            new Paragraph(telefono)
                                .SetFont(fuenteNormal)
                                .SetFontSize(7.5f)
                                .SetMargin(0)));

                interior.AddCell(
                    new Cell()
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetVerticalAlignment(
                            VerticalAlignment.MIDDLE)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPaddingTop(padArriba2)
                        .SetPaddingBottom(padAbajo2)
                        .Add(
                            new Paragraph(correo)
                                .SetFont(fuenteNormal)
                                .SetFontSize(7.5f)
                                .SetFontColor(
                                    ColorConstants.BLUE)
                                .SetMargin(0)));

                celda.Add(
                    interior);

                destino.AddCell(
                    celda);
            }

            Table CrearCaja(
                DepartamentoPdf dept,
                Color fondo,
                bool esCompacta = false)
            {
                float rellenoCabecera =
                    esCompacta ? 0f : 3f;

                var tabla =
                    new Table(1)
                        .UseAllAvailableWidth()
                        .SetMarginBottom(0);

                tabla.AddCell(
                    new Cell()
                        .SetTextAlignment(
                            TextAlignment.CENTER)
                        .SetVerticalAlignment(
                            VerticalAlignment.MIDDLE)
                        .SetBackgroundColor(fondo)
                        .SetBorder(
                            new SolidBorder(
                                ColorConstants.BLACK,
                                0.5f))
                        .SetPaddingTop(rellenoCabecera)
                        .SetPaddingBottom(rellenoCabecera)
                        .Add(
                            new Paragraph(dept.Nombre)
                                .SetFont(fuenteNormal)
                                .SetFontSize(8.5f)
                                .SetMargin(0)));

                var contenido =
                    new Cell()
                        .SetBorder(
                            new SolidBorder(
                                ColorConstants.BLACK,
                                0.5f))
                        .SetBorderTop(
                            Border.NO_BORDER)
                        .SetPadding(0);

                var personas =
                    new Table(1)
                        .UseAllAvailableWidth();

                bool esPrimero =
                    true;

                if (dept.SuperintendenteId > 0)
                {
                    AnadirHermano(
                        personas,
                        dept.SuperintendenteId,
                        esPrimero,
                        esCompacta);

                    esPrimero =
                        false;
                }

                foreach (int auxiliar in dept.Auxiliares)
                {
                    AnadirHermano(
                        personas,
                        auxiliar,
                        esPrimero,
                        esCompacta);

                    esPrimero =
                        false;
                }

                contenido.Add(
                    personas);

                if (string.Equals(
                        dept.Nombre,
                        "CONTABILIDAD",
                        StringComparison.OrdinalIgnoreCase)
                    && dept.AuditorId > 0)
                {
                    personas.AddCell(
                        new Cell(1, 2)
                            .SetTextAlignment(
                                TextAlignment.CENTER)
                            .SetVerticalAlignment(
                                VerticalAlignment.MIDDLE)
                            .SetBackgroundColor(
                                ColorAuditor)
                            .SetBorder(
                                Border.NO_BORDER)
                            .SetBorderTop(
                                new SolidBorder(
                                    ColorConstants.BLACK,
                                    0.5f))
                            .SetBorderBottom(
                                new SolidBorder(
                                    ColorConstants.BLACK,
                                    0.5f))
                            .SetPaddingTop(1)
                            .SetPaddingBottom(1)
                            .Add(
                                new Paragraph("Auditor")
                                    .SetFont(fuenteNormal)
                                    .SetFontSize(8.5f)
                                    .SetMargin(0)));

                    AnadirHermano(
                        personas,
                        dept.AuditorId,
                        true,
                        false);
                }

                tabla.AddCell(
                    contenido);

                return tabla;
            }

            Cell CrearCeldaHija(
                DepartamentoPdf dept,
                Color fondo,
                bool esUltimo)
            {
                var celda =
                    new Cell()
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0)
                        .SetPaddingLeft(25f)
                        .SetPaddingBottom(Separacion);

                celda.Add(
                    CrearCaja(
                        dept,
                        fondo,
                        false));

                celda.SetNextRenderer(
                    new LineaArbolRenderer(
                        celda,
                        esUltimo));

                return celda;
            }

            Cell CrearCeldaApilada(
                List<(DepartamentoPdf Dept, Color Fondo)> lista,
                bool esUltimo)
            {
                var celda =
                    new Cell()
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0)
                        .SetPaddingLeft(25f)
                        .SetPaddingBottom(Separacion);

                for (int i = 0;
                     i < lista.Count;
                     i++)
                {
                    bool esCompacta =
                        string.Equals(
                            lista[i].Dept.Nombre,
                            "AUDIO",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            lista[i].Dept.Nombre,
                            "PLATAFORMA",
                            StringComparison.OrdinalIgnoreCase);

                    celda.Add(
                        CrearCaja(
                            lista[i].Dept,
                            lista[i].Fondo,
                            esCompacta));

                    if (i < lista.Count - 1)
                    {
                        var hueco =
                            new Cell()
                                .SetHeight(Separacion)
                                .SetBorder(
                                    Border.NO_BORDER)
                                .SetPadding(0);

                        hueco.SetNextRenderer(
                            new HuecoApiladoRenderer(
                                hueco));

                        var tablaHueco =
                            new Table(1)
                                .UseAllAvailableWidth()
                                .SetMarginLeft(25f);

                        tablaHueco.AddCell(
                            hueco);

                        celda.Add(
                            tablaHueco);
                    }
                }

                celda.SetNextRenderer(
                    new LineaArbolRenderer(
                        celda,
                        esUltimo));

                return celda;
            }

            // ----- Columna izquierda -----
            var izquierda =
                new Table(1)
                    .UseAllAvailableWidth();

            DepartamentoPdf? supAsamblea =
                Buscar(
                    "SUPERINTENDENTE DE ASAMBLEA");

            if (TieneGente(supAsamblea))
            {
                izquierda.AddCell(
                    new Cell()
                        .Add(
                            CrearCaja(
                                supAsamblea!,
                                ColorSupAsamblea,
                                false))
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0));

                var huecoRaiz =
                    new Cell()
                        .SetHeight(Separacion)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0);

                huecoRaiz.SetNextRenderer(
                    new HuecoRaizRenderer(
                        huecoRaiz));

                izquierda.AddCell(
                    huecoRaiz);
            }

            var hijosIzquierda =
                new List<(DepartamentoPdf Dept, Color Fondo)>();

            foreach (string nombre in new[]
                     {
                         "ACOMODADORES",
                         "CONTABILIDAD",
                         "ESTACIONAMIENTO",
                         "LIMPIEZA",
                         "PRIMEROS AUXILIOS"
                     })
            {
                DepartamentoPdf? dept =
                    Buscar(
                        nombre);

                if (TieneGente(dept))
                {
                    hijosIzquierda.Add(
                        (dept!, ColorDepartamentosAzul));
                }
            }

            for (int i = 0;
                 i < hijosIzquierda.Count;
                 i++)
            {
                izquierda.AddCell(
                    CrearCeldaHija(
                        hijosIzquierda[i].Dept,
                        hijosIzquierda[i].Fondo,
                        i == hijosIzquierda.Count - 1));
            }

            DepartamentoPdf? seguridad =
                Buscar(
                    "COORDINADOR DE SEGURIDAD");

            if (TieneGente(seguridad))
            {
                var celda =
                    new Cell()
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0)
                        .SetPaddingLeft(25f)
                        .SetPaddingBottom(Separacion);

                celda.Add(
                    CrearCaja(
                        seguridad!,
                        ColorSeguridad,
                        false));

                izquierda.AddCell(
                    celda);
            }

            // ----- Columna derecha -----
            var derecha =
                new Table(1)
                    .UseAllAvailableWidth();

            DepartamentoPdf? supAuxiliar =
                Buscar(
                    "SUPERINTENDENTE AUXILIAR DE ASAMBLEA");

            if (TieneGente(supAuxiliar))
            {
                derecha.AddCell(
                    new Cell()
                        .Add(
                            CrearCaja(
                                supAuxiliar!,
                                ColorSupAuxiliar,
                                false))
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0));

                var huecoRaiz =
                    new Cell()
                        .SetHeight(Separacion)
                        .SetBorder(
                            Border.NO_BORDER)
                        .SetPadding(0);

                huecoRaiz.SetNextRenderer(
                    new HuecoRaizRenderer(
                        huecoRaiz));

                derecha.AddCell(
                    huecoRaiz);
            }

            var bloques =
                new List<object>();

            var apilados =
                new List<(DepartamentoPdf Dept, Color Fondo)>();

            foreach (string nombre in new[]
                     {
                         "AUDIO Y VÍDEO",
                         "AUDIO",
                         "PLATAFORMA"
                     })
            {
                DepartamentoPdf? dept =
                    Buscar(
                        nombre);

                if (TieneGente(dept))
                {
                    apilados.Add(
                        (dept!, ColorDepartamentosNaranja));
                }
            }

            if (apilados.Count > 0)
            {
                bloques.Add(
                    apilados);
            }

            foreach (string nombre in new[]
                     {
                         "BAUTISMO",
                         "OBJETOS PERDIDOS",
                         "TRANSPORTE",
                         "INSTALACIÓN"
                     })
            {
                DepartamentoPdf? dept =
                    Buscar(
                        nombre);

                if (TieneGente(dept))
                {
                    bloques.Add(
                        (dept!, ColorDepartamentosNaranja));
                }
            }

            for (int i = 0;
                 i < bloques.Count;
                 i++)
            {
                bool esUltimo =
                    i == bloques.Count - 1;

                if (bloques[i] is
                    List<(DepartamentoPdf Dept, Color Fondo)> lista)
                {
                    derecha.AddCell(
                        CrearCeldaApilada(
                            lista,
                            esUltimo));
                }
                else if (bloques[i] is
                         ValueTuple<DepartamentoPdf, Color> uno)
                {
                    derecha.AddCell(
                        CrearCeldaHija(
                            uno.Item1,
                            uno.Item2,
                            esUltimo));
                }
            }

            var principal =
                new Table(
                    new UnitValue[]
                    {
                        UnitValue.CreatePercentValue(50f),
                        UnitValue.CreatePercentValue(50f)
                    });

            principal.SetWidth(
                UnitValue.CreatePercentValue(100f));

            principal.AddCell(
                new Cell()
                    .Add(izquierda)
                    .SetBorder(
                        Border.NO_BORDER)
                    .SetPadding(0)
                    .SetPaddingRight(6f));

            principal.AddCell(
                new Cell()
                    .Add(derecha)
                    .SetBorder(
                        Border.NO_BORDER)
                    .SetPadding(0)
                    .SetPaddingLeft(6f));

            documento.Add(
                principal);
        }

        return salida.ToArray();
    }


    // ========================================================================
    // AYUDAS
    // ========================================================================

    private sealed class DepartamentoPdf
    {
        public string Nombre { get; set; } = "";

        public int SuperintendenteId { get; set; }

        public int AuditorId { get; set; }

        public List<int> Auxiliares { get; } =
            new();
    }


    private static Dictionary<string, DepartamentoPdf>
        AgruparDepartamentos(
            IList<OrganigramaAsambleaMobile> filas)
    {
        var mapa =
            new Dictionary<string, DepartamentoPdf>(
                StringComparer.OrdinalIgnoreCase);

        foreach (OrganigramaAsambleaMobile fila in filas)
        {
            string nombre =
                (fila.Departamento ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (nombre.Length == 0)
            {
                continue;
            }

            if (!mapa.TryGetValue(
                    nombre,
                    out DepartamentoPdf? dept))
            {
                dept =
                    new DepartamentoPdf
                    {
                        Nombre = nombre
                    };

                mapa[nombre] =
                    dept;
            }

            string rol =
                (fila.Rol ?? "")
                    .Trim()
                    .ToLowerInvariant();

            if (rol.Contains("auditor"))
            {
                dept.AuditorId =
                    fila.AsignadoId;
            }
            else if (rol.Contains("superintendente")
                     && !rol.Contains("auxiliar")
                     && dept.SuperintendenteId <= 0)
            {
                dept.SuperintendenteId =
                    fila.AsignadoId;
            }
            else if (fila.AsignadoId > 0)
            {
                dept.Auxiliares.Add(
                    fila.AsignadoId);
            }
        }

        return mapa;
    }


    private static PdfFont Fuente(
        bool negrita)
    {
        try
        {
            string archivo =
                negrita
                    ? "/system/fonts/Roboto-Bold.ttf"
                    : "/system/fonts/Roboto-Regular.ttf";

            return PdfFontFactory.CreateFont(
                archivo,
                PdfEncodings.IDENTITY_H);
        }
        catch
        {
            return negrita
                ? PdfFontFactory.CreateFont(
                    StandardFonts.HELVETICA_BOLD)
                : PdfFontFactory.CreateFont(
                    StandardFonts.HELVETICA);
        }
    }


    private static string FormatearFechaPdf(
        string fecha)
    {
        if (string.IsNullOrWhiteSpace(
                fecha))
        {
            return "";
        }

        if (DateTime.TryParse(
                fecha,
                CultureInfo.GetCultureInfo("es-ES"),
                DateTimeStyles.None,
                out DateTime valor))
        {
            return valor
                .ToString(
                    "dd 'DE' MMMM 'DE' yyyy",
                    CultureInfo.GetCultureInfo("es-ES"))
                .ToUpper();
        }

        return fecha.ToUpper();
    }


    private static string FormatearTelefonoPdf(
        string telefono)
    {
        if (string.IsNullOrWhiteSpace(
                telefono))
        {
            return "";
        }

        string limpio =
            telefono
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("(", "")
                .Replace(")", "")
                .Trim();

        if (limpio.StartsWith("+34"))
        {
            limpio =
                limpio.Substring(3);
        }

        if (!limpio.StartsWith("+")
            && limpio.Length == 9)
        {
            return $"{limpio.Substring(0, 3)} " +
                   $"{limpio.Substring(3, 3)} " +
                   $"{limpio.Substring(6, 3)}";
        }

        return limpio;
    }


    // ========================================================================
    // LÍNEAS DEL ÁRBOL (igual que en Windows)
    // ========================================================================

    private sealed class LineaArbolRenderer
        : CellRenderer
    {
        private readonly bool _esUltimo;

        public LineaArbolRenderer(
            Cell modelo,
            bool esUltimo)
            : base(modelo)
        {
            _esUltimo =
                esUltimo;
        }

        public override void Draw(
            DrawContext contexto)
        {
            base.Draw(
                contexto);

            var lienzo =
                contexto.GetCanvas();

            var rect =
                GetOccupiedAreaBBox();

            lienzo.SaveState();

            lienzo.SetStrokeColor(
                ColorConstants.BLACK);

            lienzo.SetLineWidth(
                0.5f);

            float x =
                rect.GetLeft() + 10f;

            float rama =
                rect.GetTop() - 10f;

            lienzo.MoveTo(
                x,
                rect.GetTop() + 0.5f);

            lienzo.LineTo(
                x,
                _esUltimo
                    ? rama
                    : rect.GetBottom() - 0.5f);

            lienzo.MoveTo(
                x,
                rama);

            lienzo.LineTo(
                rect.GetLeft() + 25f,
                rama);

            lienzo.Stroke();

            lienzo.RestoreState();
        }

        public override IRenderer GetNextRenderer() =>
            new LineaArbolRenderer(
                (Cell)GetModelElement(),
                _esUltimo);
    }


    private sealed class HuecoRaizRenderer
        : CellRenderer
    {
        public HuecoRaizRenderer(
            Cell modelo)
            : base(modelo)
        {
        }

        public override void Draw(
            DrawContext contexto)
        {
            base.Draw(
                contexto);

            var lienzo =
                contexto.GetCanvas();

            var rect =
                GetOccupiedAreaBBox();

            lienzo.SaveState();

            lienzo.SetStrokeColor(
                ColorConstants.BLACK);

            lienzo.SetLineWidth(
                0.5f);

            float x =
                rect.GetLeft() + 10f;

            lienzo.MoveTo(
                x,
                rect.GetTop() + 0.5f);

            lienzo.LineTo(
                x,
                rect.GetBottom() - 0.5f);

            lienzo.Stroke();

            lienzo.RestoreState();
        }

        public override IRenderer GetNextRenderer() =>
            new HuecoRaizRenderer(
                (Cell)GetModelElement());
    }


    private sealed class HuecoApiladoRenderer
        : CellRenderer
    {
        public HuecoApiladoRenderer(
            Cell modelo)
            : base(modelo)
        {
        }

        public override void Draw(
            DrawContext contexto)
        {
            base.Draw(
                contexto);

            var lienzo =
                contexto.GetCanvas();

            var rect =
                GetOccupiedAreaBBox();

            lienzo.SaveState();

            lienzo.SetStrokeColor(
                ColorConstants.BLACK);

            lienzo.SetLineWidth(
                0.5f);

            float x =
                rect.GetLeft() + 25f;

            lienzo.MoveTo(
                x,
                rect.GetTop() + 0.5f);

            lienzo.LineTo(
                x,
                rect.GetBottom() - 0.5f);

            lienzo.Stroke();

            lienzo.RestoreState();
        }

        public override IRenderer GetNextRenderer() =>
            new HuecoApiladoRenderer(
                (Cell)GetModelElement());
    }
}
