using System;

namespace SCCosmos.Mobile.Platforms.Android;

// =============================================================
// VENTANITA EMERGENTE DEL WIDGET: HORAS Y CBs DE UN DÍA
//
// Se abre como un diálogo flotante sobre el escritorio (pantalla
// de inicio): NO trae al frente la interfaz de la app.
//
// Por defecto muestra el día de hoy; al cambiar de día se cargan las
// horas y CBs guardados ese día y al guardar se actualizan (el total
// del mes se recalcula solo).
// =============================================================

[global::Android.App.Activity(
    Theme = "@android:style/Theme.Material.Dialog.Alert",
    ExcludeFromRecents = true,
    NoHistory = true,
    TaskAffinity = "",
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleInstance,
    Label = "Predicación")]
public class HorasDialogActivity : global::Android.App.Activity
{
    /// <summary>Avisa a las pantallas abiertas de que se guardó.</summary>
    public static event Action? AlGuardar;


    private DateTime _fecha = DateTime.Today;

    private global::Android.Widget.Button? _botonFecha;

    private global::Android.Widget.EditText? _horasTexto;

    private global::Android.Widget.EditText? _cbsTexto;

    private global::Android.Widget.TextView? _titulo;

    private string _usuario = "SC";


    protected override void OnCreate(
        global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            SetContentView(
                Resource.Layout.dialog_horas);

            _usuario =
                SCCosmos.Mobile.Services
                    .PerfilAccesoService.EsHombre
                    ? "SC"
                    : "Esposa";

            _botonFecha =
                FindViewById<global::Android.Widget.Button>(
                    Resource.Id.dialog_horas_fecha);

            _horasTexto =
                FindViewById<global::Android.Widget.EditText>(
                    Resource.Id.dialog_horas_horas);

            _cbsTexto =
                FindViewById<global::Android.Widget.EditText>(
                    Resource.Id.dialog_horas_cbs);

            _titulo =
                FindViewById<global::Android.Widget.TextView>(
                    Resource.Id.dialog_horas_titulo);

            var cancelar =
                FindViewById<global::Android.Widget.Button>(
                    Resource.Id.dialog_horas_cancelar);

            if (cancelar != null)
            {
                cancelar.Click += (s, e) => Finish();
            }

            if (_botonFecha != null)
            {
                _botonFecha.Click += (s, e) => ElegirDia();
            }

            var guardar =
                FindViewById<global::Android.Widget.Button>(
                    Resource.Id.dialog_horas_anadir);

            if (guardar == null)
            {
                return;
            }

            guardar.Click += (s, e) => Guardar();

            CargarDia();
        }
        catch
        {
            Finish();
        }
    }


    private void ElegirDia()
    {
        try
        {
            var dialogo =
                new global::Android.App.DatePickerDialog(
                    this,
                    (s, e) =>
                    {
                        _fecha = new DateTime(
                            e.Year,
                            e.Month + 1,
                            e.DayOfMonth);

                        CargarDia();
                    },
                    _fecha.Year,
                    _fecha.Month - 1,
                    _fecha.Day);

            dialogo.Show();
        }
        catch
        {
        }
    }


    private void CargarDia()
    {
        try
        {
            if (_botonFecha != null)
            {
                bool esHoy = _fecha.Date == DateTime.Today;

                _botonFecha.Text = esHoy
                    ? "Hoy · " + _fecha.ToString("dd/MM/yyyy")
                    : _fecha.ToString(
                        "dddd d 'de' MMMM 'de' yyyy",
                        new System.Globalization.CultureInfo("es-ES"));
            }

            if (_titulo != null)
            {
                string mes =
                    _fecha.ToString(
                        "MMMM yyyy",
                        new System.Globalization.CultureInfo("es-ES"));

                _titulo.Text =
                    "Predicación - "
                    + char.ToUpper(mes[0], new System.Globalization.CultureInfo("es-ES"))
                    + mes.Substring(1);
            }

            var (horas, cbs) =
                SCCosmos.Mobile.Data.DatabaseHelper
                    .ObtenerPredicacionDiaMobile(
                        _usuario,
                        _fecha.ToString("yyyy-MM-dd"));

            if (_horasTexto != null)
            {
                _horasTexto.Text = horas > 0
                    ? horas.ToString(
                        "0.#",
                        new System.Globalization.CultureInfo("es-ES"))
                    : "0";
            }

            if (_cbsTexto != null)
            {
                _cbsTexto.Text = cbs.ToString();
            }
        }
        catch
        {
        }
    }


    private void Guardar()
    {
        try
        {
            double horas = 0;

            double.TryParse(
                (_horasTexto?.Text ?? "")
                    .Trim()
                    .Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out horas);

            int cbs = 0;

            int.TryParse(
                (_cbsTexto?.Text ?? "").Trim(),
                out cbs);

            if (horas < 0)
            {
                horas = 0;
            }

            if (cbs < 0)
            {
                cbs = 0;
            }

            SCCosmos.Mobile.Data.DatabaseHelper
                .GuardarPredicacionDiaMobile(
                    _usuario,
                    _fecha.ToString("yyyy-MM-dd"),
                    horas,
                    cbs);

            SCCosmos.Mobile.Data.DatabaseHelper
                .AnotarActividadMobile(
                    "Predicación",
                    $"Guardó el {_fecha:dd/MM/yyyy}: "
                    + (horas > 0 ? $"{horas:0.#} h" : "0 h")
                    + (cbs > 0 ? $" y {cbs} CB" + (cbs == 1 ? "" : "s") : "")
                    + $" ({_usuario})");

            SccosmosWidgetProvider.ActualizarTodos();

            AlGuardar?.Invoke();
        }
        catch
        {
        }

        Finish();
    }
}
