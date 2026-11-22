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

        // «Ver las tareas de …»: las del cónyuge, si lo permite.
        private bool _verConyuge;


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
            // Al construir la página, la pestaña marcada por defecto
            // dispara Checked antes de que existan los controles.
            if (!IsLoaded)
            {
                return;
            }

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
            if (ListaTareas == null || TxtResumen == null)
            {
                return;
            }

            try
            {
                // El botón solo sale si el cónyuge lo permite; si lo quita,
                // se vuelve a las propias.
                bool permitido =
                    DatabaseHelper.ConyugePermiteVerTareas();

                if (!permitido)
                {
                    _verConyuge = false;
                }

                string nombre =
                    DatabaseHelper.NombreConyugeTareas();

                BtnVerConyuge.Visibility =
                    permitido
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                BtnVerConyuge.Content =
                    (_verConyuge ? "☑ " : "☐ ") + "Ver las tareas de " + nombre;

                BtnVerConyuge.Appearance =
                    _verConyuge
                        ? Wpf.Ui.Controls.ControlAppearance.Primary
                        : Wpf.Ui.Controls.ControlAppearance.Secondary;

                PanelCategorias.Visibility =
                    PerfilAcceso.AccesoCompleto && !_verConyuge
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // Las del cónyuge, todas juntas (como las ve en su app).
                string categoria =
                    _verConyuge
                        ? null
                        : _categoriaActual;

                var todas =
                    DatabaseHelper.ObtenerTareas(_verConyuge)
                        .OrderBy(t => t.Completada)
                        .ThenBy(t => t.FechaInicio)
                        .ThenBy(t => t.Id)
                        .ToList();

                _tareas =
                    string.IsNullOrWhiteSpace(categoria)
                        ? todas
                        : todas
                            .Where(t => string.Equals(
                                string.IsNullOrWhiteSpace(t.Categoria)
                                    ? "Personal"
                                    : t.Categoria.Trim(),
                                categoria,
                                StringComparison.OrdinalIgnoreCase))
                            .ToList();

                ListaTareas.ItemsSource =
                    null;
                ListaTareas.ItemsSource =
                    _tareas;

                int pendientes =
                    _tareas.Count(t => !t.Completada);

                TxtResumen.Text =
                    _verConyuge
                        ? _tareas.Count == 0
                            ? nombre + " no tiene tareas."
                            : pendientes == 1
                                ? "1 tarea pendiente de " + nombre
                                : $"{pendientes} tareas pendientes de " + nombre
                        : _tareas.Count == 0
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
            // Viendo las del cónyuge, la nueva es para él o ella.
            var ventana =
                new FormularioTarea(null)
                {
                    CategoriaInicial =
                        _verConyuge || string.IsNullOrWhiteSpace(_categoriaActual)
                            ? "Personal"
                            : _categoriaActual,
                    ParaInicial =
                        _verConyuge
                            ? SCCosmos.Sincro.TareasConyuge.Para.Conyuge
                            : SCCosmos.Sincro.TareasConyuge.Para.Mi
                };

            ventana.Owner =
                Window.GetWindow(this);

            if (ventana.ShowDialog() == true)
            {
                Cargar();
            }
        }


        private void BtnVerConyuge_Click(
            object sender,
            RoutedEventArgs e)
        {
            _verConyuge =
                !_verConyuge;

            Cargar();
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
