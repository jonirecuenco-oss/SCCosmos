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

        // Nuevas propiedades para recuperar el texto editado
        public string AsuntoFinal { get; private set; }
        public string CuerpoFinal { get; private set; }

        public ConfirmarEnvioWindow(string titulo, bool yaEnviado, List<ArchivoDocumento> archivos, string asunto = "", string cuerpoHTML = "")
        {
            InitializeComponent();
            TxtTitulo.Text = titulo;

            // Marcamos la casilla automáticamente si la BD dice que ya se envió
            ChkEnviado.IsChecked = yaEnviado;

            // Mostramos el panel rojo si procede
            if (yaEnviado) PanelAvisoEnviado.Visibility = Visibility.Visible;

            ListaArchivos.ItemsSource = archivos;

            // Cargamos los datos del email en el editor
            TxtAsunto.Text = asunto;

            // Inyectamos la plantilla HTML en el WebBrowser
            if (!string.IsNullOrEmpty(cuerpoHTML))
            {
                WbEditorHelper.SetHtml(WbEditorConfirmacion, cuerpoHTML);
            }
            else
            {
                // Si viene vacío (ej: botón de enviar recordatorio), cargamos el motor visual base para poder escribir en blanco
                WbEditorHelper.SetHtml(WbEditorConfirmacion, "");
            }
        }

        private void ChkEnviado_Click(object sender, RoutedEventArgs e)
        {
            // Avisamos al sistema de que el usuario ha tocado la casilla a mano
            EstadoModificadoManual = true;

            // Un toque de estilo: si marcamos/desmarcamos a mano, mostramos/ocultamos la alerta roja
            PanelAvisoEnviado.Visibility = ChkEnviado.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnFormato_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string comando)
            {
                try { WbEditorConfirmacion.InvokeScript("eval", $"document.execCommand('{comando}', false, null);"); } catch { }
            }
        }

        private void BtnVerArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
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
            // Recuperamos el texto del asunto
            AsuntoFinal = TxtAsunto.Text;

            // Recuperamos el HTML que el usuario haya editado en la pantalla
            try
            {
                CuerpoFinal = WbEditorConfirmacion.InvokeScript("eval", "document.body.innerHTML").ToString();
            }
            catch
            {
                CuerpoFinal = "";
            }

            this.DialogResult = true;
        }
    }
}