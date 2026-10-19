using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

// =============================================================
// ENTRADA RÁPIDA DE HORAS DE PREDICACIÓN (BOTÓN + DEL WIDGET)
// =============================================================

public partial class HorasEntradaPage : ContentPage
{
    public HorasEntradaPage()
    {
        InitializeComponent();

        LblMesHoras.Text =
            DateTime.Today.ToString(
                "MMMM yyyy",
                new System.Globalization.CultureInfo("es-ES"));

        TxtHoras.Text =
            "1";
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopModalAsync();
    }


    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            string texto =
                (TxtHoras.Text ?? "")
                    .Trim()
                    .Replace(',', '.');

            if (!double.TryParse(
                    texto,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double valor)
                || valor <= 0
                || valor > 99)
            {
                await DisplayAlert(
                    "Horas",
                    "Escribe un número de horas válido (por ejemplo 2,5).",
                    "Aceptar");

                return;
            }

            string usuario =
                PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            string mes =
                DateTime.Today.ToString("yyyy-MM");

            await Task.Run(
                () => DatabaseHelper.SumarHorasMesMobile(
                    usuario,
                    mes,
                    valor));

            DatabaseHelper.AnotarActividadMobile(
                "Horas",
                $"Añadió {valor:0.#} h de predicación ({usuario})");

            SCCosmos.Mobile.Platforms.Android
                .SccosmosWidgetProvider
                .ActualizarTodos();

            await Navigation.PopModalAsync();
        }
        catch
        {
        }
    }
}
