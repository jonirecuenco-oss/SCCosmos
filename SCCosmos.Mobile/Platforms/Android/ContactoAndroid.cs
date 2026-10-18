#if ANDROID
using Android.App;
using Android.Content;
using Android.Provider;

using Microsoft.Maui.ApplicationModel;

namespace SCCosmos.Mobile.Platforms.Android;


// ============================================================================
// CONTACTOS (Android)
// ============================================================================
//
// Abre el editor de contactos del sistema con los datos de la persona ya
// rellenados, para que el usuario pueda revisarlos/editarlos y guardarlos.
// (Intent ACTION_INSERT: no requiere permisos propios.)

public static class ContactoAndroid
{
    public static void AbrirEditorContacto(
        string nombre,
        string telefono,
        string email,
        string direccion)
    {
        Activity? actividad =
            Platform.CurrentActivity;

        if (actividad is null)
        {
            throw new InvalidOperationException(
                "No hay una actividad Android disponible.");
        }

        var intent =
            new Intent(Intent.ActionInsert);

        intent.SetType(
            ContactsContract.Contacts.ContentType);

        intent.PutExtra(
            ContactsContract.Intents.Insert.Name,
            nombre ?? "");

        if (!string.IsNullOrWhiteSpace(
                telefono))
        {
            intent.PutExtra(
                ContactsContract.Intents.Insert.Phone,
                telefono);
        }

        if (!string.IsNullOrWhiteSpace(
                email))
        {
            intent.PutExtra(
                ContactsContract.Intents.Insert.Email,
                email);
        }

        if (!string.IsNullOrWhiteSpace(
                direccion))
        {
            intent.PutExtra(
                ContactsContract.Intents.Insert.Postal,
                direccion);
        }

        actividad.StartActivity(
            intent);
    }
}
#endif
