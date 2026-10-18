using System.Globalization;

using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;

using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;

/// <summary>
/// Generación masiva de tarjetas S-21-S, portada de SCCosmos WPF
/// (GenerarPdfS21Masivo / PrepararPlantillaS21 / RellenarTarjetaS21).
/// Requiere una plantilla PDF S-21 cargada por el usuario.
/// </summary>
public static class S21MobileService
{
    private static readonly string[] Meses =
    {
        "septiembre",
        "octubre",
        "noviembre",
        "diciembre",
        "enero",
        "febrero",
        "marzo",
        "abril",
        "mayo",
        "junio",
        "julio",
        "agosto"
    };

    public sealed class TarjetaS21
    {
        public Persona Persona { get; set; } = new();

        public List<ActividadMensualMobile> Actividad
        { get; set; } = new();
    }

    public static byte[] Generar(
        byte[] plantilla,
        IList<TarjetaS21> tarjetas,
        int anioServicio)
    {
        if (plantilla is null
            ||
            plantilla.Length == 0)
        {
            throw new ArgumentException(
                "La plantilla S-21 está vacía.",
                nameof(plantilla));
        }

        byte[] plantillaPreparada =
            PrepararPlantilla(
                plantilla);

        using var salida =
            new MemoryStream();

        using (var pdfSalida =
               new PdfDocument(
                   new PdfWriter(salida)))
        {
            foreach (TarjetaS21 tarjeta in tarjetas)
            {
                using var personaMs =
                    new MemoryStream();

                using (
                    var pdfPersona =
                        new PdfDocument(
                            new PdfReader(
                                new MemoryStream(
                                    plantillaPreparada)),
                            new PdfWriter(personaMs)))
                {
                    PdfAcroForm? form =
                        PdfAcroForm.GetAcroForm(
                            pdfPersona,
                            true);

                    if (form is null
                        ||
                        form.GetField(
                            "900_1_Text_SanSerif")
                        is null)
                    {
                        throw new InvalidOperationException(
                            "La plantilla S-21 no contiene los " +
                            "campos esperados.");
                    }

                    RellenarTarjeta(
                        form,
                        tarjeta,
                        anioServicio);

                    form.FlattenFields();
                }

                personaMs.Position = 0;

                using var lectura =
                    new PdfDocument(
                        new PdfReader(
                            personaMs));

                lectura.CopyPagesTo(
                    1,
                    lectura.GetNumberOfPages(),
                    pdfSalida);
            }
        }

        return salida.ToArray();
    }

    /// <summary>
    /// Registra los widgets con nombre cuando el AcroForm viene vacío
    /// (algunas versiones oficiales del S-21). Réplica de WPF.
    /// </summary>
    private static byte[] PrepararPlantilla(
        byte[] plantilla)
    {
        using var entrada =
            new MemoryStream(
                plantilla,
                writable: false);

        using var salida =
            new MemoryStream();

        using (
            var pdf =
                new PdfDocument(
                    new PdfReader(entrada),
                    new PdfWriter(salida)))
        {
            PdfAcroForm? form =
                PdfAcroForm.GetAcroForm(
                    pdf,
                    true);

            if (form is null)
            {
                throw new InvalidOperationException(
                    "El PDF seleccionado no contiene un " +
                    "formulario S-21 válido.");
            }

            if (form.GetField(
                    "900_1_Text_SanSerif")
                is null)
            {
                PdfArray fields =
                    form.GetPdfObject()
                        .GetAsArray(
                            PdfName.Fields)
                    ?? new PdfArray();

                form.GetPdfObject()
                    .Put(
                        PdfName.Fields,
                        fields);

                for (int pagina = 1;
                     pagina <= pdf.GetNumberOfPages();
                     pagina++)
                {
                    foreach (PdfAnnotation anotacion in
                             pdf.GetPage(pagina)
                                 .GetAnnotations())
                    {
                        PdfDictionary dic =
                            anotacion.GetPdfObject();

                        bool esWidget =
                            PdfName.Widget.Equals(
                                dic.GetAsName(
                                    PdfName.Subtype));

                        bool tieneNombre =
                            dic.GetAsString(
                                PdfName.T) is not null;

                        bool tieneTipo =
                            dic.GetAsName(
                                PdfName.FT) is not null;

                        if (esWidget
                            &&
                            tieneNombre
                            &&
                            tieneTipo
                            &&
                            dic.GetIndirectReference()
                            is not null)
                        {
                            fields.Add(
                                dic.GetIndirectReference());
                        }
                    }
                }
            }
        }

        return salida.ToArray();
    }

    private static void RellenarTarjeta(
        PdfAcroForm form,
        TarjetaS21 tarjeta,
        int anioServicio)
    {
        Persona persona =
            tarjeta.Persona;

        PonerTexto(
            form,
            "900_1_Text_SanSerif",
            persona.Nombre ?? "");

        PonerTexto(
            form,
            "900_2_Text_SanSerif",
            FormatearFecha(
                persona.FechaNacimiento));

        PonerTexto(
            form,
            "900_5_Text_SanSerif",
            FormatearFecha(
                persona.FechaBautismo));

        Marcar(
            form,
            "900_3_CheckBox",
            string.Equals(
                persona.Sexo,
                "Hombre",
                StringComparison.OrdinalIgnoreCase));

        Marcar(
            form,
            "900_4_CheckBox",
            string.Equals(
                persona.Sexo,
                "Mujer",
                StringComparison.OrdinalIgnoreCase));

        string nombramiento =
            persona.Nombramiento ?? "";

        string servicio =
            persona.Servicio ?? "";

        Marcar(
            form,
            "900_8_CheckBox",
            nombramiento.Contains(
                "anciano",
                StringComparison.OrdinalIgnoreCase));

        Marcar(
            form,
            "900_9_CheckBox",
            nombramiento.Contains(
                "siervo",
                StringComparison.OrdinalIgnoreCase)
            ||
            nombramiento.Contains(
                "ministerial",
                StringComparison.OrdinalIgnoreCase));

        Marcar(
            form,
            "900_10_CheckBox",
            servicio.Contains(
                "precursor regular",
                StringComparison.OrdinalIgnoreCase));

        Marcar(
            form,
            "900_11_CheckBox",
            servicio.Contains(
                "precursor especial",
                StringComparison.OrdinalIgnoreCase));

        Marcar(
            form,
            "900_12_CheckBox",
            servicio.Contains(
                "misionero",
                StringComparison.OrdinalIgnoreCase));

        PonerTexto(
            form,
            "900_13_Text_C_SanSerif",
            anioServicio.ToString());

        for (int i = 0;
             i < Meses.Length;
             i++)
        {
            int numeroCampo = 20 + i;

            ActividadMensualMobile? registro =
                tarjeta.Actividad
                    .FirstOrDefault(a =>
                        string.Equals(
                            (a.Mes ?? "").Trim(),
                            Meses[i],
                            StringComparison.OrdinalIgnoreCase));

            bool participo =
                registro is not null
                &&
                (registro.Participacion
                 ||
                 HorasMayoresQueCero(
                     registro.Horas));

            Marcar(
                form,
                $"901_{numeroCampo}_CheckBox",
                participo);

            PonerTexto(
                form,
                $"902_{numeroCampo}_Text_C_SanSerif",
                registro?.CursosBiblicos ?? "");

            Marcar(
                form,
                $"903_{numeroCampo}_CheckBox",
                registro?.PrecursorAuxiliar == true);

            PonerTexto(
                form,
                $"904_{numeroCampo}_S21_Value",
                registro?.Horas ?? "");

            PonerTexto(
                form,
                $"905_{numeroCampo}_Text_SanSerif",
                registro?.Notas ?? "");
        }
    }

    private static bool HorasMayoresQueCero(
        string? horas)
    {
        if (string.IsNullOrWhiteSpace(horas))
        {
            return false;
        }

        return double.TryParse(
                   horas.Trim().Replace(',', '.'),
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out double valor)
               && valor > 0;
    }

    private static string FormatearFecha(
        string? fecha)
    {
        return EscuelasService.TryParseFechaFlexible(
                   fecha,
                   out DateTime valor)
            ? valor.ToString("dd/MM/yyyy")
            : (fecha?.Trim() ?? "");
    }

    private static void PonerTexto(
        PdfAcroForm form,
        string nombreCampo,
        string valor)
    {
        var campo =
            form.GetField(
                nombreCampo);

        campo?.SetValue(
            valor ?? "");
    }

    private static void Marcar(
        PdfAcroForm form,
        string nombreCampo,
        bool marcado)
    {
        var campo =
            form.GetField(
                nombreCampo);

        campo?.SetValue(
            marcado
                ? "Yes"
                : "Off");
    }
}
