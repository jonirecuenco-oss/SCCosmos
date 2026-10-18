using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Storage;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Services;


// ============================================================================
// ENVÍO DE CORREO POR LA APP DEL DISPOSITIVO (Gmail en Android)
// ============================================================================
//
// Android no permite fijar el remitente por código, pero el dispositivo
// tiene sincronizada la cuenta JW en Gmail: al abrir el compositor, el
// usuario elige esa cuenta y envía. Este servicio centraliza la
// preparación del mensaje (destinatarios, asunto, cuerpo y adjuntos)
// y NUNCA envía solo: abre Gmail y deja que el usuario confirme.

public static class CorreoService
{
    public static async Task<bool> ComponerAsync(
        string asunto,
        string cuerpoHtml,
        IList<string>? destinatarios = null,
        IList<string>? adjuntos = null)
    {
        var mensaje =
            new EmailMessage
            {
                Subject = asunto ?? "",
                Body = cuerpoHtml ?? "",
                BodyFormat = EmailBodyFormat.Html
            };

        if (destinatarios is not null)
        {
            foreach (string correo in destinatarios)
            {
                if (!string.IsNullOrWhiteSpace(correo))
                {
                    mensaje.To.Add(correo);
                }
            }
        }

        if (adjuntos is not null)
        {
            foreach (string ruta in adjuntos)
            {
                if (!string.IsNullOrWhiteSpace(ruta)
                    &&
                    File.Exists(ruta))
                {
                    mensaje.Attachments.Add(
                        new EmailAttachment(ruta));
                }
            }
        }

        try
        {
#if ANDROID
            // Se intenta primero con la aplicación de Gmail (si está
            // instalada); si no, se usa el camino habitual de Android.
            if (AbrirConGmail(
                    mensaje,
                    destinatarios,
                    adjuntos))
            {
                return true;
            }
#endif

            await Email.Default.ComposeAsync(
                mensaje);

            return true;
        }
        catch (FeatureNotSupportedException)
        {
            await Application.Current!.Windows[0].Page!
                .DisplayAlert(
                    "Correo",
                    "Este dispositivo no tiene ninguna aplicación " +
                    "de correo configurada.",
                    "Aceptar");

            return false;
        }
        catch (Exception ex)
        {
            await Application.Current!.Windows[0].Page!
                .DisplayAlert(
                    "Correo",
                    "No se pudo abrir la aplicación de correo: " +
                    ex.Message,
                    "Aceptar");

            return false;
        }
    }


#if ANDROID
    /// <summary>
    /// Abre la aplicación de Gmail con el correo preparado (destinatarios,
    /// asunto, cuerpo y adjuntos). Devuelve false si Gmail no está
    /// instalado, para que se use el camino habitual de Android.
    /// </summary>
    private static bool AbrirConGmail(
        EmailMessage mensaje,
        IList<string>? destinatarios,
        IList<string>? adjuntos)
    {
        try
        {
            var contexto =
                Android.App.Application.Context;

            var intencion =
                new Android.Content.Intent(
                    Android.Content.Intent.ActionSend);

            intencion.SetType(
                "message/rfc822");

            intencion.SetPackage(
                "com.google.android.gm");

            var correos =
                new List<string>();

            foreach (string correo in
                     destinatarios ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(correo))
                {
                    correos.Add(correo);
                }
            }

            if (correos.Count > 0)
            {
                intencion.PutExtra(
                    Android.Content.Intent.ExtraEmail,
                    correos.ToArray());
            }

            intencion.PutExtra(
                Android.Content.Intent.ExtraSubject,
                mensaje.Subject ?? "");

            intencion.PutExtra(
                Android.Content.Intent.ExtraText,
                mensaje.Body ?? "");

            intencion.PutExtra(
                "android.intent.extra.HTML_TEXT",
                mensaje.Body ?? "");

            var uris =
                new List<Android.Net.Uri>();

            string autoridad =
                contexto.PackageName + ".fileProvider";

            foreach (string ruta in
                     adjuntos ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(ruta)
                    || !File.Exists(ruta))
                {
                    continue;
                }

                uris.Add(
                    AndroidX.Core.Content.FileProvider
                        .GetUriForFile(
                            contexto,
                            autoridad,
                            new Java.IO.File(ruta)));
            }

            if (uris.Count == 1)
            {
                intencion.PutExtra(
                    Android.Content.Intent.ExtraStream,
                    uris[0]);
            }
            else if (uris.Count > 1)
            {
                intencion.PutParcelableArrayListExtra(
                    Android.Content.Intent.ExtraStream,
                    uris
                        .Cast<Android.OS.IParcelable>()
                        .ToList());

                intencion.ClipData =
                    Android.Content.ClipData.NewRawUri(
                        "",
                        uris[0]);
            }

            intencion.AddFlags(
                Android.Content.ActivityFlags.GrantReadUriPermission);

            intencion.AddFlags(
                Android.Content.ActivityFlags.NewTask);

            contexto.StartActivity(
                intencion);

            return true;
        }
        catch
        {
            // Sin Gmail (o sin permisos para adjuntar): se sigue por el
            // camino habitual.
            return false;
        }
    }
#endif


    /// <summary>
    /// Extrae el contenido de un documento de la BD a un archivo temporal
    /// para poder adjuntarlo. Devuelve null si no hay contenido.
    /// </summary>
    public static string? MaterializarDocumento(
        DocumentoVisitaMobile documento)
    {
        if (documento.Contenido.Length == 0)
        {
            return !string.IsNullOrWhiteSpace(
                       documento.RutaLocal)
                   &&
                   File.Exists(
                       documento.RutaLocal)
                ? documento.RutaLocal
                : null;
        }

        try
        {
            string ruta =
                Path.Combine(
                    FileSystem.CacheDirectory,
                    string.IsNullOrWhiteSpace(
                        documento.NombreArchivo)
                        ? "documento.pdf"
                        : documento.NombreArchivo);

            File.WriteAllBytes(
                ruta,
                documento.Contenido);

            return ruta;
        }
        catch
        {
            return null;
        }
    }


    public static List<string> MaterializarCategoria(
        string categoria)
    {
        var rutas =
            new List<string>();

        foreach (DocumentoVisitaMobile doc in
                 DatabaseHelper
                     .ObtenerDocumentosVisitaMobile(
                         categoria))
        {
            string? ruta =
                MaterializarDocumento(doc);

            if (ruta is not null)
            {
                rutas.Add(ruta);
            }
        }

        return rutas;
    }


    /// <summary>
    /// Correos (personal o JW) de una lista de personas por Id.
    /// </summary>
    public static List<string> CorreosDePersonas(
        IEnumerable<int> ids)
    {
        var conjunto =
            new HashSet<int>(
                ids.Where(id => id > 0));

        if (conjunto.Count == 0)
        {
            return new List<string>();
        }

        var correos =
            new List<string>();

        foreach (Persona persona in
                 DatabaseHelper.ObtenerPersonas())
        {
            if (!conjunto.Contains(
                    persona.Id))
            {
                continue;
            }

            string correo =
                !string.IsNullOrWhiteSpace(
                    persona.Email)
                    ? persona.Email
                    : persona.EmailJW;

            if (!string.IsNullOrWhiteSpace(
                    correo)
                &&
                !correos.Contains(
                    correo))
            {
                correos.Add(correo);
            }
        }

        return correos;
    }
}
