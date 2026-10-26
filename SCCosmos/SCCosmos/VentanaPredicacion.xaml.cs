using System;
using System.Globalization;
using System.Windows;

namespace SCCosmos
{
    // =========================================================
    // VENTANITA PARA AÑADIR HORAS Y CBS DE UN DÍA (PREDICACIÓN)
    //
    // Por defecto se abre en el día de hoy. Al cambiar de día se cargan
    // las horas y CBs que ya hubiera guardados ese día y al guardar se
    // actualizan (el total del mes se recalcula solo).
    // =========================================================

    public partial class VentanaPredicacion : Window
    {
        public double Horas { get; private set; }

        public int Cbs { get; private set; }

        /// <summary>Día elegido (formato yyyy-MM-dd).</summary>
        public string Fecha { get; private set; } =
            DateTime.Today.ToString("yyyy-MM-dd");


        public VentanaPredicacion()
        {
            InitializeComponent();

            FechaDia.SelectedDate = DateTime.Today;

            TxtMes.Text =
                DateTime.Today.ToString(
                    "MMMM yyyy",
                    CultureInfo.GetCultureInfo("es-ES"));

            CargarDia(DateTime.Today);

            Loaded += (s, e) =>
            {
                TxtHoras.Focus();
                TxtHoras.SelectAll();
            };
        }


        private static string UsuarioActual() =>
            PerfilAcceso.AccesoCompleto ? "SC" : "Esposa";


        private void CargarDia(DateTime dia)
        {
            try
            {
                var (horas, cbs) = DatabaseHelper.ObtenerPredicacionDia(
                    UsuarioActual(),
                    dia.ToString("yyyy-MM-dd"));

                TxtHoras.Text = horas > 0
                    ? horas.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))
                    : "0";

                TxtCbs.Text = cbs.ToString();

                TxtEstadoDia.Text = (horas > 0 || cbs > 0)
                    ? "Ya hay datos guardados ese día."
                    : "Ese día aún no tiene horas guardadas.";

                TxtMes.Text = dia.ToString(
                    "MMMM yyyy",
                    CultureInfo.GetCultureInfo("es-ES"));
            }
            catch
            {
            }
        }


        private void FechaDia_SelectedDateChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (FechaDia.SelectedDate is DateTime dia)
            {
                CargarDia(dia);
            }
        }


        private void BtnCancelar_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
        }


        private void BtnAnadir_Click(
            object sender,
            RoutedEventArgs e)
        {
            double horas = 0;

            double.TryParse(
                (TxtHoras.Text ?? "").Trim().Replace(',', '.'),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
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

            DateTime dia = FechaDia.SelectedDate ?? DateTime.Today;

            Fecha = dia.ToString("yyyy-MM-dd");
            Horas = horas;
            Cbs = cbs;

            DialogResult = true;
        }
    }
}
