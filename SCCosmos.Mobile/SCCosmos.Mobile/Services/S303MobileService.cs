using iText.Forms;
using iText.Kernel.Pdf;

namespace SCCosmos.Mobile.Services;


public static class S303MobileService
{
    public static byte[] CrearDesdePlantilla(
        byte[] plantillaPdf,
        DatosS303Mobile datos)
    {
        if (plantillaPdf is null
            ||
            plantillaPdf.Length == 0)
        {
            throw new ArgumentException(
                "La plantilla S-303 está vacía.",
                nameof(plantillaPdf));
        }

        using var entrada =
            new MemoryStream(
                plantillaPdf,
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
                throw new InvalidOperationException(
                    "La plantilla seleccionada no contiene " +
                    "un formulario PDF compatible.");
            }

            const float tamano = 10f;

            Rellenar(formulario, "900_1_Text_SanSerif", datos.CodigoCongregacion, tamano);
            Rellenar(formulario, "900_2_Text_SanSerif", datos.FechaVisita, tamano);
            Rellenar(formulario, "900_3_Text_SanSerif", datos.NombreCongregacion, tamano);
            Rellenar(formulario, "900_4_Text_SanSerif", datos.Municipio, tamano);
            Rellenar(formulario, "900_5_Text_SanSerif", datos.Provincia, tamano);
            Rellenar(formulario, "900_6_Text_SanSerif", datos.NombreSuperintendente, tamano);
            Rellenar(formulario, "900_7_Text_SanSerif", datos.NumeroCircuito, tamano);
        }

        return salida.ToArray();
    }


    private static void Rellenar(
        PdfAcroForm formulario,
        string nombreCampo,
        string? valor,
        float tamano)
    {
        var campo =
            formulario.GetField(
                nombreCampo);

        if (campo is null)
        {
            return;
        }

        campo.SetFontSize(tamano);
        campo.SetValue(valor ?? "");
    }
}


public sealed class DatosS303Mobile
{
    public string CodigoCongregacion { get; set; } = "";
    public string FechaVisita { get; set; } = "";
    public string NombreCongregacion { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string Provincia { get; set; } = "";
    public string NombreSuperintendente { get; set; } = "";
    public string NumeroCircuito { get; set; } = "";
}
