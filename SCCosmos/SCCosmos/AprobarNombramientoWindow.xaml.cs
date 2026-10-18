using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SCCosmos
{
    public partial class AprobarNombramientoWindow : Window
    {
        public DateTime FechaSeleccionada { get; private set; }
        public List<string> ArchivosPdfGenerados { get; private set; } = new List<string>();

        private RecomendacionHub _rec;
        private List<ArchivoDocumento> _plantillas;

        public AprobarNombramientoWindow(RecomendacionHub rec)
        {
            InitializeComponent();
            _rec = rec;
            DpFecha.SelectedDate = DateTime.Now;

            // Cargamos las plantillas de la categoría Nombramientos de Correspondencia
            _plantillas = DatabaseHelper.ObtenerDocumentos("Nombramientos");
            LstArchivos.ItemsSource = _plantillas;
        }

        private async void BtnAprobar_Click(object sender, RoutedEventArgs e)
        {
            if (!DpFecha.SelectedDate.HasValue) return;
            FechaSeleccionada = DpFecha.SelectedDate.Value;

            BtnAprobar.IsEnabled = false;
            TxtEstado.Text = "Generando PDFs con Microsoft Word...";

            try
            {
                await Task.Run(() => GenerarPdfs());
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generando PDF: Asegúrate de tener Word cerrado. " + ex.Message, "Error Word", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnAprobar.IsEnabled = true;
                TxtEstado.Text = "";
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void GenerarPdfs()
        {
            if (_plantillas.Count == 0) return;

            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempMails");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string fechaStr = FechaSeleccionada.ToString("d 'de' MMMM 'de' yyyy", new CultureInfo("es-ES"));
            string nombreA = (_rec.RecomendadoComo != null && _rec.RecomendadoComo.Contains("Anciano", StringComparison.OrdinalIgnoreCase)) ? _rec.Hermano : "-";
            string nombreSM = (_rec.RecomendadoComo != null && _rec.RecomendadoComo.Contains("Siervo", StringComparison.OrdinalIgnoreCase)) ? _rec.Hermano : "-";

            Type wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null) throw new Exception("No se ha detectado Microsoft Word instalado en el equipo.");

            dynamic wordApp = Activator.CreateInstance(wordType);
            wordApp.Visible = false;
            wordApp.DisplayAlerts = false; // wdAlertsNone

            try
            {
                foreach (var docModel in _plantillas)
                {
                    if (docModel.RutaLocal.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || docModel.RutaLocal.EndsWith(".doc", StringComparison.OrdinalIgnoreCase))
                    {
                        dynamic document = wordApp.Documents.Open(docModel.RutaLocal, ReadOnly: true);
                        dynamic findObj = document.Content.Find;

                        Reemplazar(findObj, "$_FECHA", fechaStr);
                        Reemplazar(findObj, "$_CONGREGACION", _rec.Congregacion);
                        Reemplazar(findObj, "$_NOMBRE_A", nombreA);
                        Reemplazar(findObj, "$_NOMBRE_SM", nombreSM);
                        Reemplazar(findObj, "$_NOMBRE_BAJA", "-");

                        string pdfName = "Nombramiento_" + Path.GetFileNameWithoutExtension(docModel.NombreArchivo) + ".pdf";
                        string pdfPath = Path.Combine(tempDir, pdfName);

                        document.ExportAsFixedFormat(pdfPath, 17); // 17 = wdExportFormatPDF
                        ArchivosPdfGenerados.Add(pdfPath);

                        document.Close(false); // No guardar cambios en el docx original
                    }
                    else if (docModel.RutaLocal.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // Si ya es PDF, lo adjuntamos tal cual
                        ArchivosPdfGenerados.Add(docModel.RutaLocal);
                    }
                }
            }
            finally
            {
                wordApp.Quit();
            }
        }

        private void Reemplazar(dynamic findObj, string buscar, string reemplazar)
        {
            findObj.ClearFormatting();
            findObj.Replacement.ClearFormatting();
            findObj.Text = buscar;
            findObj.Replacement.Text = reemplazar;
            findObj.Execute(Replace: 2); // 2 = wdReplaceAll
        }
    }
}