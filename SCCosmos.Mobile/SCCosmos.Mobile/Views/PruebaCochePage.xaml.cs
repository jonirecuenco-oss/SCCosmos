using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using SCCosmos.Mobile.Platforms.Android;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Prueba del coche: qué datos de combustible y kilómetros da por el OBDLink.
/// El resultado se enseña, se puede copiar y se añade a prueba_coche.txt
/// (carpeta externa de la app, como el registro de errores; se saca con adb pull).
/// </summary>
public partial class PruebaCochePage : ContentPage
{
    public PruebaCochePage()
    {
        InitializeComponent();
    }

    private async void BtnEmpezar_Clicked(object? sender, EventArgs e)
    {
        BtnEmpezar.IsEnabled = false;
        PanelResultado.IsVisible = false;

        try
        {
            if (await Permissions.RequestAsync<PermisoBluetoothObd>() != PermissionStatus.Granted)
            {
                LblEstado.Text = "Sin el permiso de «Dispositivos cercanos» no se puede hablar con el OBDLink.";
                return;
            }

            LblEstado.Text = "Empezando…";

            string informe = await Task.Run(() =>
                PruebaCocheAndroid.Ejecutar(texto => MainThread.BeginInvokeOnMainThread(() => LblEstado.Text = texto)));

            LblResultado.Text = informe;
            PanelResultado.IsVisible = true;
            LblEstado.Text = Guardar(informe)
                ? "Prueba terminada."
                : "Prueba terminada (no se ha podido guardar en el archivo).";
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "PruebaCoche");
            LblEstado.Text = "La prueba ha fallado: " + ex.Message;
        }
        finally
        {
            BtnEmpezar.IsEnabled = true;
        }
    }

    private async void BtnCopiar_Clicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(LblResultado.Text);
        LblEstado.Text = "Resultado copiado en el portapapeles.";
    }

    private static bool Guardar(string informe)
    {
        try
        {
            string carpeta =
                global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                ?? FileSystem.CacheDirectory;

            File.AppendAllText(Path.Combine(carpeta, "prueba_coche.txt"), informe + Environment.NewLine + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            RegistroErrores.Anotar(ex, "PruebaCoche.Guardar");
            return false;
        }
    }
}
