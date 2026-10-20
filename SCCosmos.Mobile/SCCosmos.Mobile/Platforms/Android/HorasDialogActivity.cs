using System;

namespace SCCosmos.Mobile.Platforms.Android;

// =============================================================
// VENTANITA EMERGENTE DEL WIDGET: AÑADIR HORAS Y CBs
//
// Se abre como un diálogo flotante sobre el escritorio (pantalla
// de inicio): NO trae al frente la interfaz de la app.
// =============================================================

[global::Android.App.Activity(
    Theme = "@android:style/Theme.Material.Dialog.Alert",
    ExcludeFromRecents = true,
    NoHistory = true,
    Label = "Predicación este mes")]
public class HorasDialogActivity : global::Android.App.Activity
{
    protected override void OnCreate(
        global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            SetContentView(
                Resource.Layout.dialog_horas);

            string usuario =
                SCCosmos.Mobile.Services
                    .PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            string mes =
                DateTime.Today.ToString("yyyy-MM");

            var mesVista =
                FindViewById<global::Android.Widget.TextView>(
                    Resource.Id.dialog_horas_mes);

            if (mesVista != null)
            {
                mesVista.Text =
                    DateTime.Today.ToString(
                        "MMMM yyyy",
                        new System.Globalization.CultureInfo("es-ES"));
            }

            var horasTexto =
                FindViewById<global::Android.Widget.EditText>(
                    Resource.Id.dialog_horas_horas);

            var cbsTexto =
                FindViewById<global::Android.Widget.EditText>(
                    Resource.Id.dialog_horas_cbs);

            var cancelar =
                FindViewById<global::Android.Widget.Button>(
                    Resource.Id.dialog_horas_cancelar);

            if (cancelar != null)
            {
                cancelar.Click += (s, e) => Finish();
            }

            var anadir =
                FindViewById<global::Android.Widget.Button>(
                    Resource.Id.dialog_horas_anadir);

            if (anadir == null)
            {
                return;
            }

            anadir.Click += (s, e) =>
            {
                try
                {
                    double horas = 0;

                    double.TryParse(
                        (horasTexto?.Text ?? "")
                            .Trim()
                            .Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out horas);

                    int cbs = 0;

                    int.TryParse(
                        (cbsTexto?.Text ?? "").Trim(),
                        out cbs);

                    if (horas < 0)
                    {
                        horas = 0;
                    }

                    if (cbs < 0)
                    {
                        cbs = 0;
                    }

                    if (horas > 0 || cbs > 0)
                    {
                        SCCosmos.Mobile.Data.DatabaseHelper
                            .SumarPredicacionMesMobile(
                                usuario,
                                mes,
                                horas,
                                cbs);

                        SCCosmos.Mobile.Data.DatabaseHelper
                            .AnotarActividadMobile(
                                "Predicación",
                                "Añadió al mes: "
                                + (horas > 0 ? $"{horas:0.#} h" : "")
                                + (horas > 0 && cbs > 0 ? " y " : "")
                                + (cbs > 0 ? $"{cbs} CB" + (cbs == 1 ? "" : "s") : "")
                                + $" ({usuario})");

                        SccosmosWidgetProvider
                            .ActualizarTodos();
                    }

                    Finish();
                }
                catch
                {
                    Finish();
                }
            };
        }
        catch
        {
            Finish();
        }
    }
}
