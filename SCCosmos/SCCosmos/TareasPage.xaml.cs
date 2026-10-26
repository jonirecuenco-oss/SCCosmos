using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class TareasPage : Page, IActualizablePorSincro
    {
        private List<Tarea> _tareas =
            new List<Tarea>();

        // Categoría de la pestaña activa. La esposa no tiene pestañas:
        // ve todas sus tareas (todas son personales).
        private string _categoriaActual = "Personal";


        public TareasPage()
        {
            InitializeComponent();

            // La esposa no debe tener pestañas.
            if (!PerfilAcceso.AccesoCompleto)
            {
                PanelCategorias.Visibility = Visibility.Collapsed;
                _categoriaActual = null;
            }

            Loaded += (s, e) => Cargar();
        }


        private void Categoria_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is RadioButton boton
                && boton.Tag is string categoria)
            {
                _categoriaActual = categoria;
                Cargar();
            }
        }


        public void DatosActualizados()
        {
            try
            {
                Cargar();
            }
            catch
            {
            }
        }


        private void Cargar()
        {
            try
            {
                var todas =
                    DatabaseHelper.ObtenerTareas()
                        .OrderBy(t => t.Completada)
                        .ThenBy(t => t.FechaInicio)
                        .ThenBy(t => t.Id)
                        .ToList();

                _tareas =
                    string.IsNullOrWhiteSpace(_categoriaActual)
                        ? todas
                        : todas
                            .Where(t => string.Equals(
                                string.IsNullOrWhiteSpace(t.Categoria)
                                    ? "Personal"
                                    : t.Categoria.Trim(),
                                _categoriaActual,
                                StringComparison.OrdinalIgnoreCase))
                            .ToList();

                ListaTareas.ItemsSource =
                    null;
                ListaTareas.ItemsSource =
                    _tareas;

                int pendientes =
                    _tareas.Count(t => !t.Completada);

                TxtResumen.Text =
                    _tareas.Count == 0
                        ? "Programa tareas, eventos y avisos. Las marcadas como compartidas también aparecen en la app de tu esposa."
                        : pendientes == 1
                            ? "1 tarea pendiente · las compartidas aparecen en la app de tu esposa"
                            : $"{pendientes} tareas pendientes · las compartidas aparecen en la app de tu esposa";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudieron cargar las tareas: " + ex.Message);
            }
        }


        private void BtnNueva_Click(
            object sender,
            RoutedEventArgs e)
        {
            var ventana =
                new FormularioTarea(null)
                {
                    CategoriaInicial =
                        string.IsNullOrWhiteSpace(_categoriaActual)
                            ? "Personal"
                            : _categoriaActual
                };

            ventana.Owner =
                Window.GetWindow(this);

            if (ventana.ShowDialog() == true)
            {
                Cargar();
            }
        }


        private void BtnEditar_Click(
            object sender,
            RoutedEventArgs e)
        {
            var boton =
                sender as FrameworkElement;

            Tarea tarea =
                boton?.Tag as Tarea
                ?? boton?.DataContext as Tarea;

            if (tarea == null)
            {
                return;
            }

            var ventana =
                new FormularioTarea(tarea);

            ventana.Owner =
                Window.GetWindow(this);

            if (ventana.ShowDialog() == true)
            {
                Cargar();
            }
        }


        private void Completada_Checked(
            object sender,
            RoutedEventArgs e)
        {
            var check =
                sender as CheckBox;

            var tarea =
                check?.DataContext as Tarea;

            if (tarea == null)
            {
                return;
            }

            bool valor =
                check.IsChecked == true;

            if (tarea.Completada == valor)
            {
                return;
            }

            tarea.Completada =
                valor;

            try
            {
                DatabaseHelper.MarcarTareaCompletada(
                    tarea.Id,
                    valor);
            }
            catch
            {
            }

            Cargar();
        }
    }
}
