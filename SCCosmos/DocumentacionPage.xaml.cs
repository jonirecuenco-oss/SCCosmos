using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SCCosmos
{
    public partial class DocumentacionPage : Page
    {
        private readonly string _carpetaDestino = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchivosDocumentacion");

        public ObservableCollection<CategoriaVista> CategoriasVisitas { get; set; }
        public ObservableCollection<CategoriaVista> CategoriasAsambleas { get; set; }

        public DocumentacionPage()
        {
            InitializeComponent();

            // LA CLAVE PARA QUE LAS PESTAÑAS SE LEAN SOLAS
            this.DataContext = this;

            if (!Directory.Exists(_carpetaDestino))
                Directory.CreateDirectory(_carpetaDestino);

            CargarCategorias();
        }

        // =========================================================
        // EL ESCUDO MAESTRO QUE FUERZA EL SCROLL (Se mantiene por seguridad)
        // =========================================================
        private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MainScrollViewer == null) return;

            DependencyObject obj = e.OriginalSource as DependencyObject;

            while (obj != null && obj != this)
            {
                if (obj is TextBox txt && txt.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && txt.ExtentHeight > txt.ViewportHeight)
                {
                    return;
                }

                if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }
                else
                {
                    obj = LogicalTreeHelper.GetParent(obj);
                }
            }

            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void CargarCategorias()
        {
            // ==========================================
            // 1. PESTAÑA VISITAS
            // ==========================================
            var listaVisitas = new ObservableCollection<CategoriaVista>();

            var aviso = new CategoriaVista
            {
                Titulo = "Aviso de la visita",
                Descripcion = "Para subir el archivo S-302 u otros que se enviarán junto con el aviso de la visita.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };

            aviso.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + aviso.Titulo, "Aviso de la visita");
            aviso.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + aviso.Titulo, "Queridos compañeros,<br><br>Os dejo adjunto el formulario S-302 con la fecha de la próxima visita.<br><br>Recibid un fuerte abrazo.<br><br><b>NOTA AL CCA:</b> Por favor, confírmame la recepción de este correo. ¡Muchas gracias!");
            aviso.ArchivosSimples = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(aviso.Titulo));
            listaVisitas.Add(aviso);

            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita", "Para subir varios archivos: Carta para los ancianos, programa de actividad, S-61, u otros."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita (con SCS)", "Para subir varios archivos para la visita con el SCS."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita (visita de pastoreo)", "Para subir varios archivos para la visita de pastoreo."));

            CategoriasVisitas = listaVisitas;

            // ==========================================
            // 2. PESTAÑA ASAMBLEAS
            // ==========================================
            var listaAsambleas = new ObservableCollection<CategoriaVista>();

            var notificacionAsamblea = new CategoriaVista
            {
                Titulo = "Notificación de asamblea de circuito",
                Descripcion = "Archivos que se enviarán para anunciar la asamblea.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            notificacionAsamblea.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + notificacionAsamblea.Titulo, "Información sobre nuestra próxima asamblea de circuito");
            notificacionAsamblea.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + notificacionAsamblea.Titulo, "Queridos hermanos,<br><br>Adjunto os enviamos información importante para nuestra próxima asamblea de circuito.<br><br>Recibid nuestro cariño y saludos.");
            notificacionAsamblea.ArchivosSimples = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(notificacionAsamblea.Titulo));
            listaAsambleas.Add(notificacionAsamblea);

            var recordatorioAsamblea = new CategoriaVista
            {
                Titulo = "Recordatorio de asamblea de circuito",
                Descripcion = "Archivos que se enviarán como recordatorio de la asamblea.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            recordatorioAsamblea.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + recordatorioAsamblea.Titulo, "Recordatorio de asamblea de circuito");
            recordatorioAsamblea.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + recordatorioAsamblea.Titulo, "Queridos hermanos,<br><br>Os recordamos que pronto tendremos nuestra asamblea de circuito. Adjuntamos algunos recordatorios.<br><br>Recibid nuestro cariño y saludos.");
            recordatorioAsamblea.ArchivosSimples = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(recordatorioAsamblea.Titulo));
            listaAsambleas.Add(recordatorioAsamblea);

            CategoriasAsambleas = listaAsambleas;
        }

        private CategoriaVista CrearCategoriaColumnas(string titulo, string descripcion)
        {
            var cat = new CategoriaVista
            {
                Titulo = titulo,
                Descripcion = descripcion,
                VisibilidadSimple = Visibility.Collapsed,
                VisibilidadColumnas = Visibility.Visible,

                VisibilidadVariablesSCS = Visibility.Collapsed,
                VisibilidadVariablesPastoreo = Visibility.Collapsed,

                VisibilidadAlojamientoEspecial = Visibility.Visible,
                AnchoColumnaNormal = new GridLength(1, GridUnitType.Star),
                AnchoColumnaEspecial = new GridLength(1, GridUnitType.Star)
            };

            if (titulo == "Documentación de la visita")
            {
                cat.VisibilidadAlojamientoEspecial = Visibility.Collapsed;
                cat.AnchoColumnaEspecial = new GridLength(0);
            }
            else if (titulo == "Documentación de la visita (con SCS)")
            {
                cat.VisibilidadVariablesSCS = Visibility.Visible;
                cat.VarNombreSCS = DatabaseHelper.ObtenerConfiguracion("VarSCS", "$_NOMBRE_SCS");
                cat.VarApellidoSCS = DatabaseHelper.ObtenerConfiguracion("VarApSCS", "$_APELLIDO_SCS");
                cat.VarEsposaSCS = DatabaseHelper.ObtenerConfiguracion("VarEsposaSCS", "$_ESPOSA_SCS");
            }
            else if (titulo == "Documentación de la visita (visita de pastoreo)")
            {
                cat.VisibilidadVariablesPastoreo = Visibility.Visible;
                cat.VarNombrePastoreo = DatabaseHelper.ObtenerConfiguracion("VarPastoreo", "$_NOMBRE_VISITA");
                cat.VarApellidoPastoreo = DatabaseHelper.ObtenerConfiguracion("VarApPastoreo", "$_APELLIDO_VISITA");
                cat.VarEsposaPastoreo = DatabaseHelper.ObtenerConfiguracion("VarEsposaPastoreo", "$_ESPOSA_VISITA");
            }

            cat.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + titulo, "Documentación de la visita");
            cat.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + titulo, "Queridos hermanos,<br><br>Os enviamos adjunta la documentación para la próxima visita.<br><br>Recibid un fuerte abrazo.");

            cat.ArchivosSinAlojamiento = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(cat.TagSinAlojamiento));
            cat.ArchivosAmbos = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(cat.TagAmbos));
            cat.ArchivosAcompanante = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(cat.TagAcompanante));
            cat.ArchivosSoloYo = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(cat.TagSoloYo));

            return cat;
        }

        private void BtnSubirArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
            if (boton == null) return;

            string categoriaDBSave = boton.Tag.ToString();

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Seleccionar documento";
            ofd.Filter = "Todos los archivos (*.*)|*.*|Documentos PDF (*.pdf)|*.pdf|Documentos Word (*.docx)|*.docx";

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string nombreArchivo = Path.GetFileName(ofd.FileName);
                    string nombreUnico = Guid.NewGuid().ToString().Substring(0, 5) + "_" + nombreArchivo;
                    string rutaFinal = Path.Combine(_carpetaDestino, nombreUnico);

                    File.Copy(ofd.FileName, rutaFinal);
                    DatabaseHelper.InsertarDocumento(categoriaDBSave, nombreArchivo, rutaFinal);

                    // Actualizar UI
                    CargarCategorias();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al subir el archivo: " + ex.Message);
                }
            }
        }

        private void BtnAbrirArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
            if (boton == null) return;

            var archivo = boton.Tag as ArchivoDocumento;

            if (File.Exists(archivo.RutaLocal))
                Process.Start(new ProcessStartInfo(archivo.RutaLocal) { UseShellExecute = true });
            else
                MessageBox.Show("El archivo no se encuentra en el disco. Quizás fue eliminado manualmente.");
        }

        private void BtnBorrarArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
            if (boton == null) return;

            var archivo = boton.Tag as ArchivoDocumento;

            if (MessageBox.Show($"¿Seguro que deseas borrar '{archivo.NombreArchivo}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    DatabaseHelper.EliminarDocumento(archivo.Id);
                    if (File.Exists(archivo.RutaLocal)) File.Delete(archivo.RutaLocal);
                    CargarCategorias();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("El archivo no se pudo borrar. Asegúrate de que no esté abierto en otro programa. " + ex.Message);
                }
            }
        }

        private void TxtVarSCS_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarSCS", cat.VarNombreSCS);
        }

        private void TxtVarApellidoSCS_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarApSCS", cat.VarApellidoSCS);
        }

        private void TxtVarEsposaSCS_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarEsposaSCS", cat.VarEsposaSCS);
        }

        private void TxtVarPastoreo_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarPastoreo", cat.VarNombrePastoreo);
        }

        private void TxtVarApellidoPastoreo_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarApPastoreo", cat.VarApellidoPastoreo);
        }

        private void TxtVarEsposaPastoreo_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
                DatabaseHelper.GuardarConfiguracion("VarEsposaPastoreo", cat.VarEsposaPastoreo);
        }

        private void TxtPlantillaEmail_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
            {
                DatabaseHelper.GuardarConfiguracion("Asunto_" + cat.Titulo, cat.AsuntoEmail);
                DatabaseHelper.GuardarConfiguracion("Email_" + cat.Titulo, cat.PlantillaEmail);
            }
        }


        private void BtnEnviarMensajeGlobal_Click(object sender, RoutedEventArgs e)
        {
            // Abre la nueva ventana de envíos masivos
            EnviarMensajeWindow ventanaMensaje = new EnviarMensajeWindow();
            ventanaMensaje.Owner = Window.GetWindow(this);
            ventanaMensaje.ShowDialog();
        }
    }

    public class CategoriaVista
    {
        public string Titulo { get; set; }
        public string Descripcion { get; set; }

        public Visibility VisibilidadSimple { get; set; }
        public Visibility VisibilidadColumnas { get; set; }

        public Visibility VisibilidadVariablesSCS { get; set; } = Visibility.Collapsed;
        public Visibility VisibilidadVariablesPastoreo { get; set; } = Visibility.Collapsed;

        public Visibility VisibilidadAlojamientoEspecial { get; set; } = Visibility.Visible;

        public GridLength AnchoColumnaNormal { get; set; }
        public GridLength AnchoColumnaEspecial { get; set; }

        public string VarNombreSCS { get; set; }
        public string VarApellidoSCS { get; set; }
        public string VarEsposaSCS { get; set; }

        public string VarNombrePastoreo { get; set; }
        public string VarApellidoPastoreo { get; set; }
        public string VarEsposaPastoreo { get; set; }

        public string AsuntoEmail { get; set; }
        public string PlantillaEmail { get; set; }

        public string TagSinAlojamiento => Titulo + "|Sin alojamiento";
        public string TagAmbos => Titulo + "|Alojamiento (ambos)";
        public string TagAcompanante => Titulo + "|Alojamiento (solo acompañante)";
        public string TagSoloYo => Titulo + "|Alojamiento (solo yo)";

        public ObservableCollection<ArchivoDocumento> ArchivosSimples { get; set; }
        public ObservableCollection<ArchivoDocumento> ArchivosSinAlojamiento { get; set; }
        public ObservableCollection<ArchivoDocumento> ArchivosAmbos { get; set; }
        public ObservableCollection<ArchivoDocumento> ArchivosAcompanante { get; set; }
        public ObservableCollection<ArchivoDocumento> ArchivosSoloYo { get; set; }

    }

}