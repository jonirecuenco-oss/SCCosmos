using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class CircuitoPage : Page
    {
        public ObservableCollection<Congregacion> ListaCongregaciones { get; set; }
        public ObservableCollection<SeccionCircuito> ListaSecciones { get; set; }

        public CircuitoPage()
        {
            InitializeComponent();
            CargarDatos();
        }

        private void CargarDatos()
        {
            ListaSecciones = new ObservableCollection<SeccionCircuito>(DatabaseHelper.ObtenerSecciones());
            TablaSecciones.ItemsSource = ListaSecciones;

            ListaCongregaciones = new ObservableCollection<Congregacion>(DatabaseHelper.ObtenerCongregaciones());
            TablaCongregaciones.ItemsSource = ListaCongregaciones;
        }

        // ==========================================
        // LÓGICA DE SECCIONES
        // ==========================================
        private void BtnCrearSeccion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtLetraSeccion.Text) || string.IsNullOrWhiteSpace(TxtLugarAsamblea.Text))
            {
                MessageBox.Show("Debes rellenar la letra de la sección y el lugar de asamblea.");
                return;
            }

            DatabaseHelper.GuardarSeccion(new SeccionCircuito
            {
                Nombre = TxtLetraSeccion.Text.Trim().ToUpper(),
                LugarAsamblea = TxtLugarAsamblea.Text.Trim()
            });

            TxtLetraSeccion.Clear();
            TxtLugarAsamblea.Clear();
            CargarDatos();
        }

        private void BtnBorrarSeccion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            int idSeccion = (int)boton.Tag;

            if (MessageBox.Show("¿Seguro que deseas eliminar esta sección?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                DatabaseHelper.EliminarSeccion(idSeccion);
                CargarDatos();
            }
        }

        // ==========================================
        // LÓGICA DE CONGREGACIONES (Tu código original)
        // ==========================================
        private void BtnAnadirCongregacion_Click(object sender, RoutedEventArgs e)
        {
            FormularioCongregacion ventanaForm = new FormularioCongregacion();
            ventanaForm.Owner = Window.GetWindow(this);

            if (ventanaForm.ShowDialog() == true)
            {
                foreach (var congre in ListaCongregaciones)
                {
                    if (congre.Codigo.Trim() == ventanaForm.CongregacionResult.Codigo)
                    {
                        MessageBox.Show("Ese código de congregación ya existe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                DatabaseHelper.InsertarCongregacion(ventanaForm.CongregacionResult);

                // Recargamos desde la base de datos para asegurar que los JOINs de Secciones se aplican
                CargarDatos();
            }
        }

        private void BtnEditarCongregacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var congreSeleccionada = boton.Tag as Congregacion; // Nota: Usamos el Tag directamente

            if (congreSeleccionada != null)
            {
                FormularioCongregacion ventanaForm = new FormularioCongregacion(congreSeleccionada);
                ventanaForm.Owner = Window.GetWindow(this);

                if (ventanaForm.ShowDialog() == true)
                {
                    DatabaseHelper.ActualizarCongregacion(congreSeleccionada);
                    CargarDatos();
                }
            }
        }

        private void BtnBorrarCongregacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as FrameworkElement;
            var congreSeleccionada = boton.Tag as Congregacion;

            if (congreSeleccionada != null)
            {
                var resultado = MessageBox.Show($"¿Estás seguro de eliminar '{congreSeleccionada.Nombre}'?", "Confirmar baja", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    DatabaseHelper.EliminarCongregacion(congreSeleccionada.Codigo);
                    ListaCongregaciones.Remove(congreSeleccionada);
                }
            }
        }
    }
}