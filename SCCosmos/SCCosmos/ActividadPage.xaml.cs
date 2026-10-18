using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class ActividadPage : Page
    {
        private bool _cargando;

        public ActividadPage()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                CargarCategorias();
                Cargar();
            };
        }

        private void CargarCategorias()
        {
            try
            {
                var categorias =
                    DatabaseHelper.ObtenerCategoriasActividad();

                _cargando =
                    true;

                CmbCategoria.ItemsSource =
                    categorias;

                CmbCategoria.SelectedIndex =
                    0;

                _cargando =
                    false;
            }
            catch
            {
            }
        }

        private void Cargar()
        {
            try
            {
                var entradas =
                    DatabaseHelper.ObtenerActividad(
                        CmbCategoria.SelectedItem?.ToString() ?? "Todas",
                        600);

                ListaActividad.ItemsSource =
                    entradas;

                LblResumen.Text =
                    entradas.Count == 0
                        ? "Sin actividad registrada todavía."
                        : $"{entradas.Count} acciones (más recientes primero)";
            }
            catch
            {
                LblResumen.Text =
                    "No se pudo cargar la actividad.";
            }
        }

        private void CmbCategoria_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_cargando)
            {
                return;
            }

            Cargar();
        }

        private void BtnActualizar_Click(
            object sender,
            RoutedEventArgs e)
        {
            CargarCategorias();
            Cargar();
        }
    }
}
