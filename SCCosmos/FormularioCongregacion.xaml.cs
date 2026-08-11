using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioCongregacion : Window
    {
        public Congregacion CongregacionResult { get; private set; }

        // MODO AÑADIR NUEVA
        public FormularioCongregacion()
        {
            InitializeComponent();
            CongregacionResult = new Congregacion();
            CargarDesplegables();
        }

        // MODO EDITAR EXISTENTE
        public FormularioCongregacion(Congregacion congre)
        {
            InitializeComponent();
            CongregacionResult = congre;

            TxtCodigo.Text = congre.Codigo;
            TxtCodigo.IsEnabled = false; // Si ya existe, no dejamos cambiar el Código (es su ID)
            TxtNombre.Text = congre.Nombre;
            TxtEmail.Text = congre.Email;
            TxtDireccion.Text = congre.DireccionSR;
            TxtHoraEntreSemana.Text = congre.HoraEntreSemana;
            TxtHoraFinSemana.Text = congre.HoraFinSemana;

            CargarDesplegables();

            if (congre.SeccionId > 0) ComboSecciones.SelectedValue = congre.SeccionId;

            foreach (ComboBoxItem item in ComboDiaFinSemana.Items)
            {
                if (item.Content.ToString() == congre.DiaFinSemana)
                {
                    ComboDiaFinSemana.SelectedItem = item;
                    break;
                }
            }
        }

        private void CargarDesplegables()
        {
            ComboSecciones.ItemsSource = DatabaseHelper.ObtenerSecciones();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtCodigo.Text) || string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("Debes indicar al menos el Código y el Nombre.");
                return;
            }

            CongregacionResult.Codigo = TxtCodigo.Text.Trim();
            CongregacionResult.Nombre = TxtNombre.Text.Trim();
            CongregacionResult.Email = TxtEmail.Text.Trim();
            CongregacionResult.DireccionSR = TxtDireccion.Text.Trim();
            CongregacionResult.HoraEntreSemana = TxtHoraEntreSemana.Text.Trim();
            CongregacionResult.HoraFinSemana = TxtHoraFinSemana.Text.Trim();

            if (ComboSecciones.SelectedValue != null)
                CongregacionResult.SeccionId = (int)ComboSecciones.SelectedValue;

            if (ComboDiaFinSemana.SelectedItem != null)
                CongregacionResult.DiaFinSemana = ((ComboBoxItem)ComboDiaFinSemana.SelectedItem).Content.ToString();

            this.DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => this.DialogResult = false;
    }
}