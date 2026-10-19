using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

// =============================================================
// ENTRADA RÁPIDA DE PREDICACIÓN DEL MES (BOTÓN + DEL WIDGET):
// HORAS Y CURSOS BÍBLICOS.
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
            "0";

        TxtCbs.Text =
            "0";
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
            double horas = 0;

            double.TryParse(
                (TxtHoras.Text ?? "")
                    .Trim()
                    .Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out horas);

            int cbs = 0;

            int.TryParse(
                (TxtCbs.Text ?? "").Trim(),
                out cbs);

            if (horas < 0)
            {
                horas = 0;
            }

            if (cbs < 0)
            {
                cbs = 0;
            }

            if (horas == 0 && cbs == 0)
            {
                await DisplayAlert(
                    "Predicación",
                    "Escribe las horas o los cursos bíblicos que quieres añadir.",
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
                () => DatabaseHelper.SumarPredicacionMesMobile(
                    usuario,
                    mes,
                    horas,
                    cbs));

            DatabaseHelper.AnotarActividadMobile(
                "Predicación",
                $"Añadió al mes: "
                + (horas > 0 ? $"{horas:0.#} h" : "")
                + (horas > 0 && cbs > 0 ? " y " : "")
                + (cbs > 0 ? $"{cbs} CB" + (cbs == 1 ? "" : "s") : "")
                + $" ({usuario})");

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
