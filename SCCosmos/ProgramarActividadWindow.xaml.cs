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
        private string _mesReferencia; // <-- Añadido

        public ProgramarActividadWindow(string responsable, string turno, string dia, string congreCodigo, string mesReferencia) // <-- Parámetro añadido
        {
            InitializeComponent();
            _responsable = responsable;
            _turno = turno;
            _dia = dia;
            _congreCodigo = congreCodigo;
            _mesReferencia = mesReferencia; // <-- Asignación

            TxtTitulo.Text = $"{dia.ToUpper()} - {turno.ToUpper()} ({responsable.ToUpper()})";

            CargarAcompanantes();
            ConfigurarReloj();
            AplicarReglasResponsable();
        }

        private void CargarAcompanantes()
        {
            // Solo hermanos asignados a un grupo
            var listaHermanos = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo)
                                              .Where(p => p.Grupo > 0)
                                              .OrderBy(p => p.Nombre)
                                              .ToList();
            CmbAcompanante.ItemsSource = listaHermanos;
        }

        private void ConfigurarReloj()
        {
            // Llenar horas (00 a 23) y minutos (00, 15, 30, 45...)
            for (int i = 0; i < 24; i++) CmbHora.Items.Add(i.ToString("00"));
            for (int i = 0; i < 60; i += 15) CmbMinutos.Items.Add(i.ToString("00")); // Cada 15 min es lo normal

            CmbHora.SelectedIndex = 10; // 10:00 por defecto
            CmbMinutos.SelectedIndex = 0;
        }

        private void AplicarReglasResponsable()
        {
            if (_responsable == "Comida")
            {
                // En comida solo dejamos hora y con quién
                PanelActividad.Visibility = Visibility.Collapsed;
                PanelLibro.Visibility = Visibility.Collapsed;
            }
            else if (_responsable == "Rebe")
            {
                CmbActividad.Items.Add("Predicación");
                CmbActividad.Items.Add("Curso bíblico");
                CmbActividad.Items.Add("PPOC"); // <-- Añadido
                CmbActividad.SelectedIndex = 0;
            }
            else if (_responsable == "Joni")
            {
                CmbActividad.Items.Add("Predicación");
                CmbActividad.Items.Add("Curso bíblico");
                CmbActividad.Items.Add("Visita de pastoreo");
                CmbActividad.Items.Add("PPOC"); // <-- Añadido
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

            string tipoActividad = _responsable == "Comida" ? "Comida" : CmbActividad.SelectedItem.ToString();
            string horaFinal = $"{CmbHora.SelectedItem}:{CmbMinutos.SelectedItem}";
            int companeroId = (int)CmbAcompanante.SelectedValue;
            string notasLibro = TxtLibro.Text.Trim();

            // -------------------------------------------------------------
            // AQUÍ GUARDAMOS EN LA BBDD (Ver Paso 4 para la clase Helper)
            // -------------------------------------------------------------
            DatabaseHelper.GuardarActividadPrograma(_congreCodigo, _mesReferencia, _dia, _turno, _responsable, tipoActividad, horaFinal, notasLibro, companeroId);

            this.DialogResult = true;
            this.Close();
        }
    }
}