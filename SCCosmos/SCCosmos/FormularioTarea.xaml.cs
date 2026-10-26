using System;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioTarea : Window
    {
        private readonly Tarea _tarea;

        private readonly bool _esNueva;

        /// <summary>Categoría con la que se abre una tarea nueva.</summary>
        public string CategoriaInicial { get; set; } = "Personal";

        private static readonly string[] TextosAviso =
        {
            "Sin aviso",
            "A la hora",
            "10 minutos antes",
            "30 minutos antes",
            "1 hora antes",
            "1 día antes"
        };

        private static readonly int[] MinutosAviso =
        {
            -1,
            0,
            10,
            30,
            60,
            1440
        };

        private static readonly string[] Repeticiones =
        {
            "Ninguna",
            "Diaria",
            "Semanal",
            "Mensual",
            "Anual"
        };


        public FormularioTarea(
            Tarea tarea)
        {
            InitializeComponent();

            _esNueva =
                tarea == null;

            _tarea =
                tarea ?? new Tarea
                {
                    FechaInicio =
                        DateTime.Today.ToString("yyyy-MM-dd"),
                    CreadaPor =
                        PerfilAcceso.AccesoCompleto
                            ? "SC"
                            : "Esposa"
                };

            PckAviso.ItemsSource =
                TextosAviso;

            PckRepeticion.ItemsSource =
                Repeticiones;

            string nombreCompartir =
                PerfilAcceso.AccesoCompleto
                    ? DatabaseHelper.ObtenerConfiguracion(
                        "NombreEsposaSC",
                        "mi esposa")
                    : DatabaseHelper.ObtenerConfiguracion(
                        "NombreSC",
                        "el SC");

            ChkCompartir.Content =
                "Compartir con " + nombreCompartir;

            TxtCabecera.Text =
                _esNueva
                    ? "NUEVA TAREA"
                    : "EDITAR TAREA";

            BtnEliminar.Visibility =
                _esNueva
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            CargarEnFormulario();

            // Para una tarea nueva se usa la pestaña desde la que se crea.
            Loaded += (s, e) =>
            {
                if (_esNueva)
                {
                    SeleccionarCategoria(CategoriaInicial);
                }
            };
        }


        private void SeleccionarCategoria(string categoria)
        {
            string buscada = string.IsNullOrWhiteSpace(categoria)
                ? "Personal"
                : categoria.Trim();

            foreach (var elemento in PckCategoria.Items)
            {
                if (elemento is ComboBoxItem item
                    && string.Equals(
                        item.Content?.ToString(),
                        buscada,
                        StringComparison.OrdinalIgnoreCase))
                {
                    PckCategoria.SelectedItem = item;
                    return;
                }
            }

            PckCategoria.SelectedIndex = 0;
        }


        private string CategoriaSeleccionada()
        {
            return PckCategoria.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString() ?? "Personal"
                : "Personal";
        }


        private void CargarEnFormulario()
        {
            TxtTitulo.Text =
                _tarea.Titulo;

            TxtDescripcion.Text =
                _tarea.Descripcion;

            SeleccionarCategoria(_tarea.Categoria);

            if (DateTime.TryParse(
                    _tarea.FechaInicio,
                    out DateTime inicio))
            {
                PckFecha.SelectedDate =
                    inicio.Date;
            }
            else
            {
                PckFecha.SelectedDate =
                    DateTime.Today;
            }

            bool tieneRango =
                !string.IsNullOrWhiteSpace(
                    _tarea.FechaFin);

            if (tieneRango
                && DateTime.TryParse(
                    _tarea.FechaFin,
                    out DateTime fin))
            {
                PckFechaFin.SelectedDate =
                    fin.Date;
            }
            else
            {
                PckFechaFin.SelectedDate =
                    PckFecha.SelectedDate ?? DateTime.Today;
            }

            ChkRango.IsChecked =
                tieneRango;
            PanelRango.Visibility =
                tieneRango
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ChkTodoElDia.IsChecked =
                _tarea.TodoElDia;
            PanelHora.Visibility =
                _tarea.TodoElDia
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            TxtHora.Text =
                string.IsNullOrWhiteSpace(_tarea.Hora)
                    ? "10:00"
                    : _tarea.Hora;

            int indiceAviso =
                Array.IndexOf(
                    MinutosAviso,
                    _tarea.AvisoMinutos);

            PckAviso.SelectedIndex =
                indiceAviso >= 0
                    ? indiceAviso
                    : 0;

            int indiceRepeticion =
                Array.IndexOf(
                    Repeticiones,
                    _tarea.Repeticion);

            PckRepeticion.SelectedIndex =
                indiceRepeticion >= 0
                    ? indiceRepeticion
                    : 0;

            ChkCompartir.IsChecked =
                _tarea.Compartida;
        }


        private void BtnGuardar_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string titulo =
                    (TxtTitulo.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(titulo))
                {
                    MessageBox.Show(
                        "Pon un título a la tarea o evento.",
                        "Tareas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                DateTime fechaInicio =
                    PckFecha.SelectedDate ?? DateTime.Today;

                string hora =
                    (TxtHora.Text ?? "").Trim();

                if (ChkTodoElDia.IsChecked != true)
                {
                    if (!TimeSpan.TryParse(
                            hora,
                            out _))
                    {
                        MessageBox.Show(
                            "La hora debe tener el formato HH:mm (por ejemplo, 19:30).",
                            "Tareas",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    hora =
                        hora.Length == 4
                            ? "0" + hora
                            : hora;
                }
                else
                {
                    hora =
                        "";
                }

                string fechaFin =
                    "";

                if (ChkRango.IsChecked == true)
                {
                    DateTime fin =
                        PckFechaFin.SelectedDate ?? fechaInicio;

                    if (fin.Date < fechaInicio.Date)
                    {
                        MessageBox.Show(
                            "La fecha final es anterior a la fecha inicial.",
                            "Tareas",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    fechaFin =
                        fin.ToString("yyyy-MM-dd");
                }

                _tarea.Titulo =
                    titulo;
                _tarea.Descripcion =
                    (TxtDescripcion.Text ?? "").Trim();
                _tarea.FechaInicio =
                    fechaInicio.ToString("yyyy-MM-dd");
                _tarea.FechaFin =
                    fechaFin;
                _tarea.TodoElDia =
                    ChkTodoElDia.IsChecked == true;
                _tarea.Hora =
                    hora;

                int indiceAviso =
                    PckAviso.SelectedIndex;

                _tarea.AvisoMinutos =
                    indiceAviso >= 0
                    && indiceAviso < MinutosAviso.Length
                        ? MinutosAviso[indiceAviso]
                        : -1;

                int indiceRepeticion =
                    PckRepeticion.SelectedIndex;

                _tarea.Repeticion =
                    indiceRepeticion >= 0
                    && indiceRepeticion < Repeticiones.Length
                        ? Repeticiones[indiceRepeticion]
                        : "Ninguna";

                _tarea.Compartida =
                    ChkCompartir.IsChecked == true;

                // La esposa siempre guarda tareas personales.
                _tarea.Categoria =
                    PerfilAcceso.AccesoCompleto
                        ? CategoriaSeleccionada()
                        : "Personal";

                if (string.IsNullOrWhiteSpace(_tarea.CreadaPor))
                {
                    _tarea.CreadaPor =
                        PerfilAcceso.AccesoCompleto
                            ? "SC"
                            : "Esposa";
                }

                DatabaseHelper.GuardarTarea(_tarea);

                DialogResult =
                    true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo guardar la tarea: " + ex.Message,
                    "Tareas",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void BtnEliminar_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_esNueva)
            {
                return;
            }

            var confirmar =
                MessageBox.Show(
                    "¿Seguro que quieres eliminar esta tarea o evento?",
                    "Eliminar tarea",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirmar != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                DatabaseHelper.EliminarTarea(
                    _tarea.Id);
            }
            catch
            {
            }

            DialogResult =
                true;
        }


        private void BtnCancelar_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult =
                false;
        }


        private void ChkTodoElDia_Cambiado(
            object sender,
            RoutedEventArgs e)
        {
            if (PanelHora == null)
            {
                return;
            }

            PanelHora.Visibility =
                ChkTodoElDia.IsChecked == true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }


        private void ChkRango_Cambiado(
            object sender,
            RoutedEventArgs e)
        {
            if (PanelRango == null)
            {
                return;
            }

            PanelRango.Visibility =
                ChkRango.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
