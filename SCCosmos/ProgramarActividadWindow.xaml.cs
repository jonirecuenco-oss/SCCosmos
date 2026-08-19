using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class ProgramarActividadWindow : Window
    {
        private string _responsable;
        private string _turno;
        private string _dia;
        private string _congreCodigo;
        private string _mesReferencia;

        public ProgramarActividadWindow(string responsable, string turno, string dia, string congreCodigo, string mesReferencia)
        {
            InitializeComponent();
            _responsable = responsable;
            _turno = turno;
            _dia = dia;
            _congreCodigo = congreCodigo;
            _mesReferencia = mesReferencia;

            TxtTitulo.Text = $"{dia.ToUpper()} - {turno.ToUpper()} ({responsable.ToUpper()})";

            CargarAcompanantes();
            ConfigurarReloj();
            AplicarReglasResponsable();
        }

        private void CargarAcompanantes()
        {
            // Cargamos a TODOS los hermanos (con o sin grupo) usando el historial exacto del mes de la visita
            var listaHermanos = DatabaseHelper.ObtenerPersonasHistoricasPorMes(_congreCodigo, _mesReferencia)
                                              .OrderBy(p => p.Nombre)
                                              .ToList();
            CmbAcompanante.ItemsSource = listaHermanos;
        }

        private void ConfigurarReloj()
        {
            // Llenar horas (00 a 23) y minutos (00, 15, 30, 45...)
            for (int i = 0; i < 24; i++) CmbHora.Items.Add(i.ToString("00"));
            for (int i = 0; i < 60; i += 15) CmbMinutos.Items.Add(i.ToString("00"));

            CmbHora.SelectedIndex = 10; // 10:00 por defecto
            CmbMinutos.SelectedIndex = 0;
        }

        private void AplicarReglasResponsable()
        {
            if (_responsable == "Comida" || _responsable == "Cenar" || _responsable == "Dormir")
            {
                // Para las comidas y el descanso, colapsamos el selector de actividad
                PanelActividad.Visibility = Visibility.Collapsed;
                PanelLibro.Visibility = Visibility.Collapsed;
            }
            else if (_responsable == "Rebe")
            {
                CmbActividad.Items.Add("Predicación");
                CmbActividad.Items.Add("Curso bíblico");
                CmbActividad.Items.Add("PPOC");
                CmbActividad.SelectedIndex = 0;
            }
            else if (_responsable == "Joni")
            {
                CmbActividad.Items.Add("Predicación");
                CmbActividad.Items.Add("Curso bíblico");
                CmbActividad.Items.Add("Visita de pastoreo");
                CmbActividad.Items.Add("PPOC");
                CmbActividad.SelectedIndex = 0;
            }
        }

        private void CmbActividad_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbActividad.SelectedItem != null && CmbActividad.SelectedItem.ToString() == "Curso bíblico")
            {
                PanelLibro.Visibility = Visibility.Visible;
            }
            else
            {
                PanelLibro.Visibility = Visibility.Collapsed;
                TxtLibro.Text = ""; // Limpiamos si escoge otra cosa
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAcompanante.SelectedValue == null)
            {
                MessageBox.Show("Debes seleccionar un acompañante de la lista.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Si es Comida, Cenar o Dormir, el tipo de actividad es el propio responsable
            string tipoActividad = (_responsable == "Comida" || _responsable == "Cenar" || _responsable == "Dormir")
                                    ? _responsable
                                    : CmbActividad.SelectedItem.ToString();

            string horaFinal = $"{CmbHora.SelectedItem}:{CmbMinutos.SelectedItem}";
            int companeroId = (int)CmbAcompanante.SelectedValue;
            string notasLibro = TxtLibro.Text.Trim();

            DatabaseHelper.GuardarActividadPrograma(_congreCodigo, _mesReferencia, _dia, _turno, _responsable, tipoActividad, horaFinal, notasLibro, companeroId);

            this.DialogResult = true;
            this.Close();
        }
    }
}