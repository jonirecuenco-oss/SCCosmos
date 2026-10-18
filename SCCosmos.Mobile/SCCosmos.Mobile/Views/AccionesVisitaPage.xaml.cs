using System.Globalization;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Pantalla Android con las acciones de correspondencia de una visita:
/// generación del S-302, envío por correo de documentación, WhatsApp
/// al coordinador y recordatorios. Nunca envía nada automáticamente.
/// </summary>
public partial class AccionesVisitaPage : ContentPage
{
    private readonly SemanaCalendario _semana;

    private string? _rutaS302Generado;

    public AccionesVisitaPage(
        SemanaCalendario semana)
    {
        InitializeComponent();

        _semana =
            semana;

        CargarCabecera();
    }

    private void CargarCabecera()
    {
        bool esAsamblea =
            string.Equals(
                _semana.Actividad,
                "Asamblea de circuito",
                StringComparison.OrdinalIgnoreCase);

        bool esVisita =
            string.Equals(
                _semana.Actividad,
                "Visita",
                StringComparison.OrdinalIgnoreCase);

        LblCongregacion.Text =
            string.IsNullOrWhiteSpace(
                _semana.Congregacion)
                ? (esAsamblea
                    ? "Asamblea de circuito"
                    : "Semana sin congregación")
                : _semana.Congregacion;

        LblFechas.Text =
            _semana.RangoFechas;

        LblTipo.Text =
            string.IsNullOrWhiteSpace(
                _semana.Tipo)
                ? ""
                : _semana.Tipo;

        // Secciones según el tipo de semana.
        SeccionVisita.IsVisible =
            esVisita;

        SeccionAsamblea.IsVisible =
            esAsamblea;

        FilaS302.IsVisible =
            esVisita;

        FilaDoc.IsVisible =
            esVisita;

        FilaBetel.IsVisible =
            esAsamblea;

        FilaNotificacion.IsVisible =
            esAsamblea;

        SwNotificacion.IsToggled =
            _semana.NotificacionEnviada;

        SwS302.IsToggled =
            _semana.S302Enviado;

        SwDoc.IsToggled =
            _semana.DocEnviada;

        SwBetel.IsToggled =
            _semana.RepBetelMensajeEnviado;

        // Si la semana aún no está guardada no tiene sentido
        // cambiar su estado.
        BtnGuardarEstado.IsEnabled =
            _semana.Id > 0;
    }


    // ========================================================================
    // NOTIFICACIÓN DE ASAMBLEA (CORREO A LAS CONGREGACIONES)
    // ========================================================================

    private const string CategoriaNotificacionAC =
        "Notificación AC";

    private async void BtnNotificacionAsamblea_Clicked(
        object sender,
        EventArgs e)
    {
        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_" + CategoriaNotificacionAC,
                    "Información sobre nuestra próxima asamblea de circuito");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_" + CategoriaNotificacionAC,
                    "Queridos hermanos,<br><br>Adjunto os enviamos " +
                    "información importante para nuestra próxima " +
                    "asamblea de circuito.");

        var adjuntos =
            new List<string>();

        foreach (DocumentoVisitaMobile doc in
                 DatabaseHelper
                     .ObtenerDocumentosVisitaMobile(
                         CategoriaNotificacionAC))
        {
            string? ruta =
                Materializar(
                    doc);

            if (ruta is not null)
            {
                adjuntos.Add(
                    ruta);
            }
        }

        // Destinatarios: congregaciones de la sección de la asamblea,
        // o todas si no se puede determinar la sección.
        var destinatarios =
            new List<string>();

        try
        {
            List<Congregacion> congregaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCongregacionesCompletasMobile());

            string seccion =
                (_semana.SeccionAsamblea ?? "").Trim();

            foreach (Congregacion c in congregaciones)
            {
                bool incluir =
                    string.IsNullOrWhiteSpace(seccion)
                    || string.Equals(
                        c.SeccionId.ToString(),
                        seccion,
                        StringComparison.OrdinalIgnoreCase);

                if (incluir
                    &&
                    !string.IsNullOrWhiteSpace(
                        c.Codigo))
                {
                    destinatarios.Add(
                        $"CONG601{c.Codigo}@jwpub.org");
                }
            }
        }
        catch
        {
            // Sin destinatarios; el usuario los añade a mano.
        }

        await ComponerEmailConAdjuntosAsync(
            asunto,
            cuerpo,
            adjuntos.Count > 0
                ? adjuntos
                : null,
            esS302: false,
            destinatariosOverride:
                destinatarios.Count > 0
                    ? destinatarios
                    : null);
    }


    // ========================================================================
    // REPRESENTANTE DE BETEL (ASAMBLEA AC-BR)
    // ========================================================================

    private async void BtnEnviarBetel_Clicked(
        object sender,
        EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _semana.RepBetelEmail))
        {
            await DisplayAlert(
                "Representante de Betel",
                "No hay correo del representante de Betel " +
                "registrado para esta asamblea.",
                "Aceptar");

            return;
        }

        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_Mensaje a rep. de Betel",
                    "Información de la asamblea de circuito");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_Mensaje a rep. de Betel",
                    "Querido hermano [Representante],<br><br>" +
                    "Te enviamos adjunta información útil " +
                    "para la asamblea.");

        string nombreRep =
            string.IsNullOrWhiteSpace(
                _semana.RepBetelNombre)
                ? "hermano"
                : _semana.RepBetelNombre;

        asunto =
            asunto
                .Replace(
                    "[Representante]",
                    nombreRep)
                .Replace(
                    "[Congregacion]",
                    _semana.Congregacion ?? "");

        cuerpo =
            cuerpo
                .Replace(
                    "[Representante]",
                    nombreRep)
                .Replace(
                    "[Congregacion]",
                    _semana.Congregacion ?? "");

        var adjuntos =
            new List<string>();

        foreach (DocumentoVisitaMobile doc in
                 DatabaseHelper
                     .ObtenerDocumentosVisitaMobile(
                         "Mensaje a rep. de Betel"))
        {
            string? ruta =
                Materializar(
                    doc);

            if (ruta is not null)
            {
                adjuntos.Add(
                    ruta);
            }
        }

        await ComponerEmailConAdjuntosAsync(
            asunto,
            cuerpo,
            adjuntos.Count > 0
                ? adjuntos
                : null,
            esS302: false,
            destinatarioOverride:
                _semana.RepBetelEmail);
    }


    private static string? Materializar(
        DocumentoVisitaMobile doc)
    {
        if (doc.Contenido.Length == 0)
        {
            return File.Exists(doc.RutaLocal)
                ? doc.RutaLocal
                : null;
        }

        try
        {
            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    string.IsNullOrWhiteSpace(
                        doc.NombreArchivo)
                        ? "documento.pdf"
                        : doc.NombreArchivo);

            File.WriteAllBytes(
                ruta,
                doc.Contenido);

            return ruta;
        }
        catch
        {
            return null;
        }
    }

    // ========================================================================
    // S-302
    // ========================================================================

    private async void BtnPrepararS302_Clicked(
        object sender,
        EventArgs e)
    {
        string ruta =
            GenerarS302EnDisco(
                out string error);

        if (ruta is null)
        {
            await DisplayAlert(
                "S-302",
                error,
                "Aceptar");

            return;
        }

        try
        {
            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title =
                        $"S-302 · {_semana.Congregacion}",
                    File =
                        new ShareFile(
                            ruta)
                });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "S-302",
                "El archivo S-302 se generó en:\n" +
                ruta +
                "\n\nPero no se pudo abrir el menú de " +
                "compartir:\n" +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEnviarS302_Clicked(
        object sender,
        EventArgs e)
    {
        string ruta =
            GenerarS302EnDisco(
                out string error);

        if (ruta is null)
        {
            await DisplayAlert(
                "S-302",
                error,
                "Aceptar");

            return;
        }

        string categoria =
            CorrespondenciaMobileService
                .ObtenerCategoriaS302(
                    _semana);

        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_" + categoria,
                    "Aviso de la visita");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_" + categoria,
                    "Queridos compañeros,<br><br>" +
                    "Os dejo adjunto el formulario S-302 " +
                    "con la fecha de la próxima visita.");

        await ComponerEmailAsync(
            asunto,
            cuerpo,
            ruta,
            esS302: true);
    }

    private string? GenerarS302EnDisco(
        out string error)
    {
        byte[]? pdf =
            CorrespondenciaMobileService
                .GenerarS302Pdf(
                    _semana,
                    out error);

        if (pdf is null)
        {
            return null;
        }

        try
        {
            string nombre =
                $"S-302_{Sanear(
                    _semana.Congregacion)}.pdf";

            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    nombre);

            File.WriteAllBytes(
                ruta,
                pdf);

            _rutaS302Generado =
                ruta;

            return ruta;
        }
        catch (Exception ex)
        {
            error =
                "No se pudo guardar el PDF S-302: " +
                ex.Message;

            return null;
        }
    }

    // ========================================================================
    // DOCUMENTACIÓN
    // ========================================================================

    private async void BtnEnviarDocumentacion_Clicked(
        object sender,
        EventArgs e)
    {
        string asunto =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Asunto_" +
                    CorrespondenciaMobileService
                        .CategoriaDocumentacion,
                    "Documentación de la visita");

        string cuerpo =
            DatabaseHelper
                .ObtenerConfiguracionVisitaMobile(
                    "Email_" +
                    CorrespondenciaMobileService
                        .CategoriaDocumentacion,
                    "Queridos hermanos,<br><br>" +
                    "Adjuntamos la documentación.");

        // Adjuntamos el S-303 si existe y el S-302 si ya se generó.
        var adjuntos =
            new List<string>();

        string? rutaS303 =
            ObtenerRutaS303();

        if (rutaS303 is not null)
        {
            adjuntos.Add(
                rutaS303);
        }

        if (_rutaS302Generado is not null
            &&
            File.Exists(
                _rutaS302Generado))
        {
            adjuntos.Add(
                _rutaS302Generado);
        }

        await ComponerEmailConAdjuntosAsync(
            asunto,
            cuerpo,
            adjuntos.Count > 0
                ? adjuntos
                : null,
            esS302: false);
    }

    private string? ObtenerRutaS303()
    {
        string codigo =
            DatabaseHelper
                .ObtenerCodigoCongregacion(
                    _semana.Congregacion);

        if (string.IsNullOrWhiteSpace(codigo))
        {
            return null;
        }

        string mes =
            $"{_semana.FechaMartes.Year:D4}-" +
            $"{_semana.FechaMartes.Month:D2}";

        DocumentoVisitaMobile? documento =
            DatabaseHelper
                .ObtenerS303VisitaMobile(
                    codigo,
                    mes);

        if (documento is null)
        {
            return null;
        }

        byte[] contenido =
            documento.Contenido;

        if (contenido.Length == 0
            &&
            !string.IsNullOrWhiteSpace(
                documento.RutaLocal)
            &&
            File.Exists(
                documento.RutaLocal))
        {
            return documento.RutaLocal;
        }

        if (contenido.Length == 0)
        {
            return null;
        }

        try
        {
            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    documento.NombreArchivo);

            File.WriteAllBytes(
                ruta,
                contenido);

            return ruta;
        }
        catch
        {
            return null;
        }
    }

    // ========================================================================
    // WHATSAPP
    // ========================================================================

    private async void BtnWhatsApp_Clicked(
        object sender,
        EventArgs e)
    {
        string? url =
            CorrespondenciaMobileService
                .ConstruirMensajeWhatsApp(
                    _semana,
                    out string error);

        if (url is null)
        {
            await DisplayAlert(
                "WhatsApp",
                error,
                "Aceptar");

            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(
                new Uri(url));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "WhatsApp",
                "No se pudo abrir WhatsApp: " +
                ex.Message,
                "Aceptar");
        }
    }

    // ========================================================================
    // CORREO
    // ========================================================================

    private async Task ComponerEmailAsync(
        string asunto,
        string cuerpoHtml,
        string? adjunto,
        bool esS302,
        string? destinatarioOverride = null)
    {
        await ComponerEmailConAdjuntosAsync(
            asunto,
            cuerpoHtml,
            adjunto is null
                ? null
                : new List<string> { adjunto },
            esS302,
            destinatarioOverride);
    }

    private async Task ComponerEmailConAdjuntosAsync(
        string asunto,
        string cuerpoHtml,
        IList<string>? adjuntos,
        bool esS302,
        string? destinatarioOverride = null,
        IList<string>? destinatariosOverride = null)
    {
        asunto =
            (asunto ?? "")
                .Replace(
                    "[Congregacion]",
                    _semana.Congregacion ?? "")
                .Trim();

        string cuerpo =
            (cuerpoHtml ?? "")
                .Replace(
                    "[Congregacion]",
                    _semana.Congregacion ?? "");

        var mensaje =
            new EmailMessage
            {
                Subject = asunto,
                Body = cuerpo,
                BodyFormat =
                    EmailBodyFormat.Html
            };

        if (destinatariosOverride is not null
            &&
            destinatariosOverride.Count > 0)
        {
            foreach (string correo in
                     destinatariosOverride)
            {
                if (!string.IsNullOrWhiteSpace(
                        correo))
                {
                    mensaje.To.Add(
                        correo);
                }
            }
        }
        else
        {
            string destino =
                string.IsNullOrWhiteSpace(
                    destinatarioOverride)
                    ? ObtenerCorreoCongregacion()
                    : destinatarioOverride;

            if (!string.IsNullOrWhiteSpace(
                    destino))
            {
                mensaje.To.Add(
                    destino);
            }
        }

        if (adjuntos is not null)
        {
            foreach (string ruta in adjuntos)
            {
                if (File.Exists(ruta))
                {
                    mensaje.Attachments.Add(
                        new EmailAttachment(
                            ruta));
                }
            }
        }

        try
        {
            await Email.Default.ComposeAsync(
                mensaje);

            if (esS302)
            {
                LblEstado.Text =
                    "Correo S-302 preparado. Recuerda marcar " +
                    "«S-302 enviado» si lo has enviado.";
            }
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlert(
                "Correo",
                "Este dispositivo no tiene ninguna aplicación " +
                "de correo configurada.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Correo",
                "No se pudo abrir la aplicación de correo: " +
                ex.Message,
                "Aceptar");
        }
    }

    private string ObtenerCorreoCongregacion()
    {
        string codigo =
            DatabaseHelper
                .ObtenerCodigoCongregacion(
                    _semana.Congregacion);

        if (string.IsNullOrWhiteSpace(codigo))
        {
            return "";
        }

        return $"CONG601{codigo}@jwpub.org";
    }

    // ========================================================================
    // ESTADO
    // ========================================================================

    private async void BtnGuardarEstado_Clicked(
        object sender,
        EventArgs e)
    {
        if (_semana.Id <= 0)
        {
            return;
        }

        _semana.S302Enviado =
            SwS302.IsToggled;

        _semana.DocEnviada =
            SwDoc.IsToggled;

        _semana.RepBetelMensajeEnviado =
            SwBetel.IsToggled;

        _semana.NotificacionEnviada =
            SwNotificacion.IsToggled;

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper.GuardarSemana(
                        _semana));

            LblEstado.Text =
                "Estado guardado.";

            await DisplayAlert(
                "Estado",
                "El estado de los envíos se ha guardado.",
                "Aceptar");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Estado",
                "No se pudo guardar el estado: " +
                ex.Message,
                "Aceptar");
        }
    }

    private static string Sanear(
        string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "visita";
        }

        foreach (char invalido in
                 Path.GetInvalidFileNameChars())
        {
            texto =
                texto.Replace(
                    invalido,
                    '_');
        }

        return texto.Trim();
    }
}
