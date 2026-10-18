using System;
using System.Collections.Generic;
using System.Windows;
using System.Text.Json;
using Microsoft.Win32;

namespace SCCosmos
{
    public partial class ImportarArchivosActividad : Wpf.Ui.Controls.FluentWindow
    {
        private string _congreCodigo;
        private string _congreNombre;
        private bool _yaImportado;
        private string _rutaS21;
        private string _rutaTerritorios;
        private string _rutaJsonConvertido;
        private readonly string _mesReferencia;

        public ImportarArchivosActividad(string congreCodigo, string congreNombre, bool yaImportado, string mesReferencia = null)
        {
            InitializeComponent();
            _congreCodigo = congreCodigo;
            _congreNombre = congreNombre;
            _yaImportado = yaImportado;
            _mesReferencia = string.IsNullOrWhiteSpace(mesReferencia)
                ? DateTime.Now.ToString("yyyy-MM")
                : mesReferencia;
            TxtSubtitulo.Text = $"{congreNombre.ToUpper()}";

            if (_yaImportado)
            {
                PanelAviso.Visibility = Visibility.Visible;
            }
        }

        private void BtnS21_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Archivos JSON (*.json)|*.json", Title = "Seleccionar S-21" };
            if (ofd.ShowDialog() == true)
            {
                _rutaS21 = ofd.FileName;
                TxtS21.Text = System.IO.Path.GetFileName(_rutaS21);
                TxtS21.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtS21.FontStyle = FontStyles.Normal;
            }
        }

        private void BtnTerritorios_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Archivos JSON (*.json)|*.json", Title = "Seleccionar Territorios" };
            if (ofd.ShowDialog() == true)
            {
                _rutaTerritorios = ofd.FileName;
                TxtTerritorios.Text = System.IO.Path.GetFileName(_rutaTerritorios);
                TxtTerritorios.Foreground = System.Windows.Media.Brushes.LightBlue;
                TxtTerritorios.FontStyle = FontStyles.Normal;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void BtnConvertirPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new OpenFolderDialog
            {
                Title = "Carpeta con las tarjetas S-21 (PDF)"
            };

            if (dialogo.ShowDialog(this) != true)
            {
                return;
            }

            string carpeta = dialogo.FolderName;

            TxtPdfS21.Text = "Convirtiendo tarjetas…";
            TxtPdfS21.Foreground = System.Windows.Media.Brushes.LightYellow;
            BtnVerJsonS21.Visibility = Visibility.Collapsed;

            int anio = SCCosmos.Sincro.ConvertidorS21.AnioCicloActual(
                DateTime.Parse(_mesReferencia + "-01"));

            SCCosmos.Sincro.ResultadoConversionS21 resultado = null;
            string rutaJson = null;
            string error = null;

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    resultado = SCCosmos.Sincro.ConvertidorS21
                        .ConvertirCarpeta(carpeta, anio);

                    rutaJson = SCCosmos.Sincro.ConvertidorS21
                        .GuardarJson(resultado.Tarjetas, carpeta);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
            {
                TxtPdfS21.Text = "No se pudo convertir: " + error;
                TxtPdfS21.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            if (resultado == null || resultado.Tarjetas.Count == 0)
            {
                TxtPdfS21.Text = "No se encontraron tarjetas S-21 en esa carpeta.";
                TxtPdfS21.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            _rutaJsonConvertido = rutaJson;
            _rutaS21 = rutaJson;

            TxtS21.Text = System.IO.Path.GetFileName(rutaJson)
                + " (" + resultado.Tarjetas.Count
                + " tarjetas, generado desde PDF)";
            TxtS21.Foreground = System.Windows.Media.Brushes.LightGreen;
            TxtS21.FontStyle = FontStyles.Normal;

            TxtPdfS21.Text = resultado.Tarjetas.Count + " tarjetas de "
                + resultado.ArchivosPdf + " PDFs ("
                + resultado.Repetidas + " repetidas). Puedes ver el JSON generado.";
            TxtPdfS21.Foreground = System.Windows.Media.Brushes.LightGreen;
            BtnVerJsonS21.Visibility = Visibility.Visible;
        }

        private void BtnVerJsonS21_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_rutaJsonConvertido)
                && System.IO.File.Exists(_rutaJsonConvertido))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_rutaJsonConvertido)
                    {
                        UseShellExecute = true
                    });
            }
        }

        private void BtnProcesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_rutaS21))
            {
                MessageBox.Show("Debes seleccionar al menos el archivo S-21 para continuar.", "Archivo faltante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string contenidoS21 = System.IO.File.ReadAllText(_rutaS21).Trim();
                if (!contenidoS21.StartsWith("[")) contenidoS21 = "[" + contenidoS21 + "]";

                var opcionesConfig = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var listaPersonas = JsonSerializer.Deserialize<List<PersonaJson>>(contenidoS21, opcionesConfig);

                List<TerritorioJSON> listaTerritorios = null;

                if (!string.IsNullOrEmpty(_rutaTerritorios))
                {
                    string contenidoS13 = System.IO.File.ReadAllText(_rutaTerritorios).Trim();
                    if (!contenidoS13.StartsWith("[")) contenidoS13 = "[" + contenidoS13 + "]";
                    listaTerritorios = JsonSerializer.Deserialize<List<TerritorioJSON>>(contenidoS13, opcionesConfig);
                }

                if (listaPersonas != null)
                {
                    var syncDashboard = new SyncJsonWindow(_congreCodigo, listaPersonas, listaTerritorios, _mesReferencia);
                    syncDashboard.Owner = Window.GetWindow(this);

                    if (syncDashboard.ShowDialog() == true)
                    {
                        this.DialogResult = true;
                        this.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error inesperado al leer los archivos JSON:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
