using System;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class FormularioTarea : Window
    {
        private readonly Tarea _tarea;

        private readonly bool _esNueva;

        // «SC» o «Esposa», y el nombre del otro («Solo para Rebeca»).
        private readonly string _usuario =
            DatabaseHelper.UsuarioTareas();

        private readonly string _nombreConyuge =
            DatabaseHelper.NombreConyugeTareas();

        /// <summary>Categoría con la que se abre una tarea nueva.</summary>
        public string CategoriaInicial { get; set; } = "Personal";

        /// <summary>Para quién es una tarea nueva (desde «Ver las tareas de …», para el cónyuge).</summary>
        public SCCosmos.Sincro.TareasConyuge.Para ParaInicial { get; set; } =
            SCCosmos.Sincro.TareasConyuge.Para.Mi;

        // Los mismos que en el móvil (SincroCompartido/AvisosTarea.cs): hasta 1 mes antes.
        private static readonly string[] TextosAviso =
            SCCosmos.Sincro.AvisosTarea.Textos;

        private static readonly int[] MinutosAviso =
            SCCosmos.Sincro.AvisosTarea.Minutos;

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

            // Las nuevas, a una hora (no de todo el día): la siguiente en punto.
            _tarea =
                tarea ?? new Tarea
                {
                    FechaInicio =
                        DateTime.Today.ToString("yyyy-MM-dd"),
                    TodoElDia = false,
                    Hora =
                        new TimeSpan(Math.Min(DateTime.Now.Hour + 1, 23), 0, 0).ToString(@"hh\:mm"),
                    CreadaPor =
                        _usuario
                };

            // La lista de siempre y, al final, «Personalizado…».
            PckAviso.ItemsSource =
                new System.Collections.Generic.List<string>(TextosAviso)
                {
                    SCCosmos.Sincro.AvisosTarea.TextoPersonalizado
                };

            PckAvisoUnidad.ItemsSource =
                SCCosmos.Sincro.AvisosTarea.TextosUnidades;

            PckRepeticion.ItemsSource =
                Repeticiones;

            PckPara.ItemsSource =
                SCCosmos.Sincro.TareasConyuge.TextosPara(_nombreConyuge);

            TxtCabecera.Text =
                _esNueva
                    ? "Nueva tarea"
                    : "Editar tarea";

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

                    PckPara.SelectedIndex =
                        (int)ParaInicial;
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

            // Uno que no está en la lista: «Personalizado…», con su cantidad y unidad.
            if (indiceAviso < 0
                && _tarea.AvisoMinutos > 0)
            {
                var (cantidad, unidad) =
                    SCCosmos.Sincro.AvisosTarea.Descomponer(
                        _tarea.AvisoMinutos);

                TxtAvisoCantidad.Text =
                    cantidad.ToString();

                PckAvisoUnidad.SelectedIndex =
                    unidad;

                indiceAviso =
                    MinutosAviso.Length;
            }
            else
            {
                PckAvisoUnidad.SelectedIndex =
                    0;
            }

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

            PckPara.SelectedIndex =
                (int)SCCosmos.Sincro.TareasConyuge.ParaDe(
                    _tarea.Compartida,
                    _tarea.CreadaPor,
                    _usuario);
        }


        private void PckAviso_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PanelAvisoPersonalizado == null)
            {
                return;
            }

            PanelAvisoPersonalizado.Visibility =
                PckAviso.SelectedIndex == MinutosAviso.Length
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }


        // «Solo para Rebeca»: la tarea pasa a ser suya, y después solo se ve
        // aquí si ella deja ver sus tareas.
        private void PckPara_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (TxtPara == null)
            {
                return;
            }

            bool delConyuge =
                PckPara.SelectedIndex == (int)SCCosmos.Sincro.TareasConyuge.Para.Conyuge;

            TxtPara.Visibility =
                delConyuge
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (delConyuge)
            {
                TxtPara.Text =
                    DatabaseHelper.ConyugePermiteVerTareas()
                        ? $"Será de {_nombreConyuge}. La verás en «Ver las tareas de {_nombreConyuge}»."
                        : $"Será de {_nombreConyuge}: solo la verás si te deja ver sus tareas.";
            }
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

                int indiceAviso =
                    PckAviso.SelectedIndex;

                int avisoMinutos =
                    indiceAviso >= 0
                    && indiceAviso < MinutosAviso.Length
                        ? MinutosAviso[indiceAviso]
                        : -1;

                if (indiceAviso == MinutosAviso.Length)
                {
                    avisoMinutos =
                        int.TryParse(
                            (TxtAvisoCantidad.Text ?? "").Trim(),
                            out int cantidad)
                            ? SCCosmos.Sincro.AvisosTarea.MinutosDe(
                                cantidad,
                                PckAvisoUnidad.SelectedIndex)
                            : -1;

                    if (avisoMinutos < 0)
                    {
                        MessageBox.Show(
                            "El aviso personalizado tiene que ser de 1 minuto a 1 año antes.",
                            "Tareas",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }
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

                _tarea.AvisoMinutos =
                    avisoMinutos;

                int indiceRepeticion =
                    PckRepeticion.SelectedIndex;

                _tarea.Repeticion =
                    indiceRepeticion >= 0
                    && indiceRepeticion < Repeticiones.Length
                        ? Repeticiones[indiceRepeticion]
                        : "Ninguna";

                var para =
                    PckPara.SelectedIndex >= 0
                        ? (SCCosmos.Sincro.TareasConyuge.Para)PckPara.SelectedIndex
                        : SCCosmos.Sincro.TareasConyuge.Para.Mi;

                (_tarea.CreadaPor, _tarea.Compartida) =
                    SCCosmos.Sincro.TareasConyuge.Aplicar(
                        para,
                        _usuario,
                        _tarea.CreadaPor);

                // La esposa no elige categoría: las suyas son personales, pero
                // las de él que edite (compartidas o vistas) conservan la suya.
                _tarea.Categoria =
                    PerfilAcceso.AccesoCompleto
                        ? CategoriaSeleccionada()
                        : _esNueva
                            ? "Personal"
                            : _tarea.Categoria;

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
