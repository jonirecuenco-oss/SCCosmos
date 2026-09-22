using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class ActividadPersonaEditor : UserControl
    {
        public ObservableCollection<ActividadMensual> Actividad { get; } = new ObservableCollection<ActividadMensual>();

        public static readonly DependencyProperty PersonaIdProperty = DependencyProperty.Register(
            nameof(PersonaId),
            typeof(int),
            typeof(ActividadPersonaEditor),
            new PropertyMetadata(0, OnPersonaIdChanged));

        public int PersonaId
        {
            get => (int)GetValue(PersonaIdProperty);
            set => SetValue(PersonaIdProperty, value);
        }

        public ActividadPersonaEditor()
        {
            InitializeComponent();
            GridActividad.ItemsSource = Actividad;
        }

        private static void OnPersonaIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ActividadPersonaEditor editor)
            {
                editor.CargarActividad();
            }
        }

        public void CargarActividad()
        {
            Actividad.Clear();

            if (PersonaId <= 0)
            {
                TxtEstado.Text = "Guarda primero la persona para poder consultar su actividad.";
                return;
            }

            try
            {
                foreach (var item in DatabaseHelper.ObtenerActividadPersona(PersonaId))
                {
                    Actividad.Add(item);
                }

                TxtEstado.Text = Actividad.Count == 0
                    ? "No hay actividad importada desde S-21 para esta persona."
                    : $"{Actividad.Count} registros cargados.";
            }
            catch (Exception ex)
            {
                TxtEstado.Text = "No se pudo cargar la actividad.";
                MessageBox.Show($"No se pudo cargar la actividad de la persona:\n{ex.Message}", "Actividad", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool GuardarActividad()
        {
            if (PersonaId <= 0) return true;

            try
            {
                GridActividad.CommitEdit(DataGridEditingUnit.Cell, true);
                GridActividad.CommitEdit(DataGridEditingUnit.Row, true);

                foreach (var item in Actividad)
                {
                    DatabaseHelper.GuardarActividadMensualEditada(
                        PersonaId,
                        item.Mes,
                        item.AnioServicio,
                        item.Horas,
                        item.Participacion,
                        item.CursosBiblicos,
                        item.PrecursorAuxiliar,
                        item.Notas);
                }

                TxtEstado.Text = "Actividad guardada correctamente.";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron guardar los cambios de actividad:\n{ex.Message}", "Actividad", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

    }
}
