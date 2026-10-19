using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioCongregacion : Window
    {
        public Congregacion CongregacionResult { get; private set; }

        // Lista de las provincias y ciudades autónomas de España
        private readonly string[] ProvinciasEspana = {
            "A Coruña", "Álava", "Albacete", "Alicante", "Almería", "Asturias", "Ávila", "Badajoz",
            "Illes Balears", "Barcelona", "Burgos", "Cáceres", "Cádiz", "Cantabria", "Castellón",
            "Ceuta", "Ciudad Real", "Córdoba", "Cuenca", "Girona", "Granada", "Guadalajara",
            "Gipuzkoa", "Huelva", "Huesca", "Jaén", "La Rioja", "Las Palmas", "León", "Lleida",
            "Lugo", "Madrid", "Málaga", "Melilla", "Murcia", "Navarra", "Ourense", "Palencia",
            "Pontevedra", "Salamanca", "Santa Cruz de Tenerife", "Segovia", "Sevilla", "Soria",
            "Tarragona", "Teruel", "Toledo", "Valencia", "Valladolid", "Bizkaia", "Zamora", "Zaragoza"
        };

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
            TxtMunicipio.Text = congre.Municipio;
            TxtDireccion.Text = congre.DireccionSR;

            // Campos nuevos: Zoom y email del AV.
            var extrasCongregacion =
                DatabaseHelper.ObtenerDatosExtraCongregacion(congre.Codigo);

            TxtIdZoom.Text = extrasCongregacion.IdZoom;
            TxtClaveZoom.Text = extrasCongregacion.ClaveZoom;
            TxtEmailAV.Text = extrasCongregacion.EmailAV;
            TxtHoraEntreSemana.Text = congre.HoraEntreSemana;
            TxtHoraFinSemana.Text = congre.HoraFinSemana;

            CargarDesplegables();

            // Cargar Selección de la Base de Datos
            if (congre.SeccionId > 0) ComboSecciones.SelectedValue = congre.SeccionId;

            ComboProvincia.SelectedItem = congre.Provincia;

            // Seleccionar el Día de Entre Semana guardado
            foreach (ComboBoxItem item in ComboDiaEntreSemana.Items)
            {
                if (item.Content.ToString() == congre.DiaEntreSemana)
                {
                    ComboDiaEntreSemana.SelectedItem = item;
                    break;
                }
            }

            // Seleccionar el Día de Fin de Semana guardado
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
            ComboProvincia.ItemsSource = ProvinciasEspana;
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
            CongregacionResult.Provincia = ComboProvincia.SelectedItem?.ToString() ?? "";
            CongregacionResult.Municipio = TxtMunicipio.Text.Trim();
            CongregacionResult.DireccionSR = TxtDireccion.Text.Trim();
            CongregacionResult.IdZoom = TxtIdZoom.Text.Trim();
            CongregacionResult.ClaveZoom = TxtClaveZoom.Text.Trim();
            CongregacionResult.EmailAV = TxtEmailAV.Text.Trim();
            CongregacionResult.HoraEntreSemana = TxtHoraEntreSemana.Text.Trim();
            CongregacionResult.HoraFinSemana = TxtHoraFinSemana.Text.Trim();

            if (ComboSecciones.SelectedValue != null)
                CongregacionResult.SeccionId = (int)ComboSecciones.SelectedValue;

            if (ComboDiaEntreSemana.SelectedItem != null)
                CongregacionResult.DiaEntreSemana = ((ComboBoxItem)ComboDiaEntreSemana.SelectedItem).Content.ToString();

            if (ComboDiaFinSemana.SelectedItem != null)
                CongregacionResult.DiaFinSemana = ((ComboBoxItem)ComboDiaFinSemana.SelectedItem).Content.ToString();

            this.DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => this.DialogResult = false;
    }
}