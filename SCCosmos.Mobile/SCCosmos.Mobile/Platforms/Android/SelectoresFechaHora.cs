using Android.App;
using Android.Content;
using Microsoft.Maui.Handlers;

namespace SCCosmos.Mobile.Platforms.Android;

// Con el tema oscuro forzado (App.xaml.cs), los diálogos de fecha y hora
// pintaban «Cancelar» y «Aceptar» con el color principal (#101216, casi
// negro) sobre su fondo oscuro: estaban, pero no se veían. Al abrirse se
// pintan del azul de la app; el resto del diálogo sigue siendo el de MAUI.

public class SelectorHoraHandler : TimePickerHandler
{
    protected override TimePickerDialog CreateTimePickerDialog(
        int hour,
        int minute)
    {
        TimePickerDialog dialogo =
            base.CreateTimePickerDialog(hour, minute);

        BotonesDialogo.Visibles(dialogo);

        return dialogo;
    }
}


public class SelectorFechaHandler : DatePickerHandler
{
    protected override DatePickerDialog CreateDatePickerDialog(
        int year,
        int month,
        int day)
    {
        DatePickerDialog dialogo =
            base.CreateDatePickerDialog(year, month, day);

        BotonesDialogo.Visibles(dialogo);

        return dialogo;
    }
}


internal static class BotonesDialogo
{
    private static readonly global::Android.Graphics.Color Azul =
        global::Android.Graphics.Color.ParseColor("#4BA3E3");

    public static void Visibles(
        AlertDialog dialogo)
    {
        // Cada vez que se abre (el de fecha se reutiliza).
        dialogo.ShowEvent += (_, _) =>
        {
            foreach (DialogButtonType tipo in new[] { DialogButtonType.Positive, DialogButtonType.Negative, DialogButtonType.Neutral })
            {
                dialogo.GetButton((int)tipo)?.SetTextColor(Azul);
            }
        };
    }
}
