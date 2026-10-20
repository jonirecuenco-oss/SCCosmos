using System;
using System.Globalization;
using System.Windows;

namespace SCCosmos
{
    // =========================================================
    // VENTANITA PARA AÑADIR HORAS Y CBS (PREDICACIÓN DEL MES)
    // =========================================================

    public partial class VentanaPredicacion : Window
    {
        public double Horas { get; private set; }

        public int Cbs { get; private set; }


        public VentanaPredicacion()
        {
            InitializeComponent();

            TxtMes.Text =
                DateTime.Today.ToString(
                    "MMMM yyyy",
                    CultureInfo.GetCultureInfo("es-ES"));

            Loaded += (s, e) =>
            {
                TxtHoras.Focus();
                TxtHoras.SelectAll();
            };
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

            if (horas == 0 && cbs == 0)
            {
                MessageBox.Show(
                    this,
                    "Escribe las horas o los cursos bíblicos que quieres añadir.",
                    "Predicación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Horas = horas;
            Cbs = cbs;

            DialogResult = true;
        }
    }
}
