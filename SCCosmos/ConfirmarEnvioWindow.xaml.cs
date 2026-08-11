using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class ConfirmarEnvioWindow : Window
    {
        // Propiedades públicas para que CalendarioPage pueda leerlas al cerrarse
        public bool EstadoEnviadoActual => ChkEnviado.IsChecked == true;
        public bool EstadoModificadoManual { get; private set; } = false;

        public ConfirmarEnvioWindow(string titulo, bool yaEnviado, List<ArchivoDocumento> archivos)
        {
            InitializeComponent();
            TxtTitulo.Text = titulo;

            // Marcamos la casilla automáticamente si la BD dice que ya se envió
            ChkEnviado.IsChecked = yaEnviado;

            // Mostramos el panel rojo si procede
            if (yaEnviado) PanelAvisoEnviado.Visibility = Visibility.Visible;

            ListaArchivos.ItemsSource = archivos;
        }

        private void ChkEnviado_Click(object sender, RoutedEventArgs e)
        {
            // Avisamos al sistema de que el usuario ha tocado la casilla a mano
            EstadoModificadoManual = true;

            // Un toque de estilo: si marcamos/desmarcamos a mano, mostramos/ocultamos la alerta roja
            PanelAvisoEnviado.Visibility = ChkEnviado.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnVerArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            string ruta = boton?.Tag?.ToString();

            if (!string.IsNullOrEmpty(ruta) && System.IO.File.Exists(ruta))
            {
                Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("No se encuentra el archivo en el disco.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}