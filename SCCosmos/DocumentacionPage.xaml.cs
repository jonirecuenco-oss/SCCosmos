using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SCCosmos
{
    // AÑADIDO INotifyPropertyChanged PARA QUE SE REFRESQUE AL INSTANTE LA LISTA DE ARCHIVOS
    public partial class DocumentacionPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly string _carpetaDestino = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchivosDocumentacion");

        private ObservableCollection<CategoriaVista> _categoriasVisitas;
        public ObservableCollection<CategoriaVista> CategoriasVisitas
        {
            get => _categoriasVisitas;
            set { _categoriasVisitas = value; OnPropertyChanged(nameof(CategoriasVisitas)); }
        }

        private ObservableCollection<CategoriaVista> _categoriasAsambleas;
        public ObservableCollection<CategoriaVista> CategoriasAsambleas
        {
            get => _categoriasAsambleas;
            set { _categoriasAsambleas = value; OnPropertyChanged(nameof(CategoriasAsambleas)); }
        }

        public DocumentacionPage()
        {
            InitializeComponent();

            this.DataContext = this;

            if (!Directory.Exists(_carpetaDestino))
                Directory.CreateDirectory(_carpetaDestino);

            CargarCategorias();
        }

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

        // ==========================================
        // INICIALIZACIÓN DE DATOS (Solo se ejecuta 1 vez al abrir la página)
        // ==========================================
        private void CargarCategorias()
        {
            // --- VISITAS ---
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
            aviso.RecargarArchivos();
            listaVisitas.Add(aviso);

            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita", "Para subir varios archivos: Carta para los ancianos, programa de actividad, S-61, u otros."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita (con SCS)", "Para subir varios archivos para la visita con el SCS."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación de la visita (visita de pastoreo)", "Para subir varios archivos para la visita de pastoreo."));

            CategoriasVisitas = listaVisitas;

            // --- ASAMBLEAS ---
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
            notificacionAsamblea.RecargarArchivos();
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
            recordatorioAsamblea.RecargarArchivos();
            listaAsambleas.Add(recordatorioAsamblea);

            var asigAsamblea = new CategoriaVista
            {
                Titulo = "Asignación asamblea circuito",
                Descripcion = "Archivos que se enviarán junto con la asignación a los oradores.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed,
                VisibilidadVariablesAsignacion = Visibility.Visible
            };
            asigAsamblea.VarNombreAsignado = DatabaseHelper.ObtenerConfiguracion("VarNomAsig", "$_NOMBRE_ORADOR");
            asigAsamblea.VarApellidoAsignado = DatabaseHelper.ObtenerConfiguracion("VarApAsig", "$_APELLIDO_ORADOR");
            asigAsamblea.VarTemaAsignado = DatabaseHelper.ObtenerConfiguracion("VarTemaAsig", "$_TEMA_ASIGNADO");
            asigAsamblea.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + asigAsamblea.Titulo, "Asignación para la asamblea de circuito");
            asigAsamblea.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + asigAsamblea.Titulo, "Querido hermano,<br><br>Te enviamos adjunta tu asignación para la asamblea de circuito.<br><br>Un abrazo.");
            asigAsamblea.RecargarArchivos();
            listaAsambleas.Add(asigAsamblea);

            var asigPrec = new CategoriaVista
            {
                Titulo = "Asignación reunión precursores",
                Descripcion = "Archivos que se enviarán junto con la asignación para la reunión de precursores.",
                VisibilidadSimple = Visibility.Collapsed,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed,
                VisibilidadVariablesAsignacion = Visibility.Visible,
                VisibilidadAsignacionPrec = Visibility.Visible
            };
            asigPrec.VarNombreAsignado = DatabaseHelper.ObtenerConfiguracion("VarNomAsigPrec", "$_NOMBRE_ORADOR");
            asigPrec.VarApellidoAsignado = DatabaseHelper.ObtenerConfiguracion("VarApAsigPrec", "$_APELLIDO_ORADOR");
            asigPrec.VarTemaAsignado = DatabaseHelper.ObtenerConfiguracion("VarTemaAsigPrec", "$_TEMA_ASIGNADO");
            asigPrec.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + asigPrec.Titulo, "Asignación reunión precursores");
            asigPrec.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + asigPrec.Titulo, "Querido hermano,<br><br>Te enviamos adjunta tu asignación para la reunión de precursores.<br><br>Un abrazo.");
            asigPrec.RecargarArchivos();
            listaAsambleas.Add(asigPrec);

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
                VisibilidadVariablesAsignacion = Visibility.Collapsed,
                VisibilidadAsignacionPrec = Visibility.Collapsed,
                VisibilidadConAlojamiento = Visibility.Collapsed,

                VisibilidadAlojamientoEspecial = Visibility.Visible,
                AnchoColumnaNormal = new GridLength(1, GridUnitType.Star),
                AnchoColumnaEspecial = new GridLength(1, GridUnitType.Star)
            };

            if (titulo == "Documentación de la visita")
            {
                cat.VisibilidadAlojamientoEspecial = Visibility.Collapsed;
                cat.VisibilidadConAlojamiento = Visibility.Visible;
                cat.AnchoColumnaEspecial = new GridLength(1, GridUnitType.Star);
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

            cat.RecargarArchivos();

            return cat;
        }

        // ==========================================
        // LA FUNCIÓN QUIRÚRGICA: Solo actualiza archivos sin borrar pestañas
        // ==========================================
        private void RecargarSoloArchivos()
        {
            if (CategoriasVisitas != null)
                foreach (var cat in CategoriasVisitas) cat.RecargarArchivos();

            if (CategoriasAsambleas != null)
                foreach (var cat in CategoriasAsambleas) cat.RecargarArchivos();
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

                    // AHORA SOLO ACTUALIZAMOS LAS LISTAS INTERNAS DE ARCHIVOS
                    RecargarSoloArchivos();
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

                    // AHORA SOLO ACTUALIZAMOS LAS LISTAS INTERNAS DE ARCHIVOS
                    RecargarSoloArchivos();
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

        private void TxtVarNombreAsignado_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
            {
                if (cat.Titulo.Contains("precursores")) DatabaseHelper.GuardarConfiguracion("VarNomAsigPrec", cat.VarNombreAsignado);
                else DatabaseHelper.GuardarConfiguracion("VarNomAsig", cat.VarNombreAsignado);
            }
        }

        private void TxtVarApellidoAsignado_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
            {
                if (cat.Titulo.Contains("precursores")) DatabaseHelper.GuardarConfiguracion("VarApAsigPrec", cat.VarApellidoAsignado);
                else DatabaseHelper.GuardarConfiguracion("VarApAsig", cat.VarApellidoAsignado);
            }
        }

        private void TxtVarTemaAsignado_LostFocus(object sender, RoutedEventArgs e)
        {
            if ((sender as TextBox)?.DataContext as CategoriaVista is CategoriaVista cat)
            {
                if (cat.Titulo.Contains("precursores")) DatabaseHelper.GuardarConfiguracion("VarTemaAsigPrec", cat.VarTemaAsignado);
                else DatabaseHelper.GuardarConfiguracion("VarTemaAsig", cat.VarTemaAsignado);
            }
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
            EnviarMensajeWindow ventanaMensaje = new EnviarMensajeWindow();
            ventanaMensaje.Owner = Window.GetWindow(this);
            ventanaMensaje.ShowDialog();
        }
    }

    public class CategoriaVista : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Titulo { get; set; }
        public string Descripcion { get; set; }

        public Visibility VisibilidadSimple { get; set; }
        public Visibility VisibilidadColumnas { get; set; }

        public Visibility VisibilidadVariablesSCS { get; set; } = Visibility.Collapsed;
        public Visibility VisibilidadVariablesPastoreo { get; set; } = Visibility.Collapsed;
        public Visibility VisibilidadVariablesAsignacion { get; set; } = Visibility.Collapsed;
        public Visibility VisibilidadConAlojamiento { get; set; } = Visibility.Collapsed;

        public Visibility VisibilidadAsignacionPrec { get; set; } = Visibility.Collapsed;

        public Visibility VisibilidadAlojamientoEspecial { get; set; } = Visibility.Visible;

        public GridLength AnchoColumnaNormal { get; set; }
        public GridLength AnchoColumnaEspecial { get; set; }

        public string VarNombreSCS { get; set; }
        public string VarApellidoSCS { get; set; }
        public string VarEsposaSCS { get; set; }

        public string VarNombrePastoreo { get; set; }
        public string VarApellidoPastoreo { get; set; }
        public string VarEsposaPastoreo { get; set; }

        public string VarNombreAsignado { get; set; }
        public string VarApellidoAsignado { get; set; }
        public string VarTemaAsignado { get; set; }

        public string AsuntoEmail { get; set; }
        public string PlantillaEmail { get; set; }

        public string TagSinAlojamiento => Titulo + "|Sin alojamiento";
        public string TagConAlojamiento => Titulo + "|Con alojamiento";
        public string TagAmbos => Titulo + "|Alojamiento (ambos)";
        public string TagAcompanante => Titulo + "|Alojamiento (solo acompañante)";
        public string TagSoloYo => Titulo + "|Alojamiento (solo yo)";

        public string TagOracionInicial => Titulo + "|Oración inicial";
        public string TagDiscurso => Titulo + "|Discurso";
        public string TagDiscursoSerie => Titulo + "|Discurso (serie)";

        // Listas Notificables (avisan al entorno gráfico cuando cambian para actualizar sin borrar la selección)
        private ObservableCollection<ArchivoDocumento> _archivosSimples;
        public ObservableCollection<ArchivoDocumento> ArchivosSimples { get => _archivosSimples; set { _archivosSimples = value; OnPropertyChanged(nameof(ArchivosSimples)); } }

        private ObservableCollection<ArchivoDocumento> _archivosSinAlojamiento;
        public ObservableCollection<ArchivoDocumento> ArchivosSinAlojamiento { get => _archivosSinAlojamiento; set { _archivosSinAlojamiento = value; OnPropertyChanged(nameof(ArchivosSinAlojamiento)); } }

        private ObservableCollection<ArchivoDocumento> _archivosConAlojamiento;
        public ObservableCollection<ArchivoDocumento> ArchivosConAlojamiento { get => _archivosConAlojamiento; set { _archivosConAlojamiento = value; OnPropertyChanged(nameof(ArchivosConAlojamiento)); } }

        private ObservableCollection<ArchivoDocumento> _archivosAmbos;
        public ObservableCollection<ArchivoDocumento> ArchivosAmbos { get => _archivosAmbos; set { _archivosAmbos = value; OnPropertyChanged(nameof(ArchivosAmbos)); } }

        private ObservableCollection<ArchivoDocumento> _archivosAcompanante;
        public ObservableCollection<ArchivoDocumento> ArchivosAcompanante { get => _archivosAcompanante; set { _archivosAcompanante = value; OnPropertyChanged(nameof(ArchivosAcompanante)); } }

        private ObservableCollection<ArchivoDocumento> _archivosSoloYo;
        public ObservableCollection<ArchivoDocumento> ArchivosSoloYo { get => _archivosSoloYo; set { _archivosSoloYo = value; OnPropertyChanged(nameof(ArchivosSoloYo)); } }

        private ObservableCollection<ArchivoDocumento> _archivosOracionInicial;
        public ObservableCollection<ArchivoDocumento> ArchivosOracionInicial { get => _archivosOracionInicial; set { _archivosOracionInicial = value; OnPropertyChanged(nameof(ArchivosOracionInicial)); } }

        private ObservableCollection<ArchivoDocumento> _archivosDiscurso;
        public ObservableCollection<ArchivoDocumento> ArchivosDiscurso { get => _archivosDiscurso; set { _archivosDiscurso = value; OnPropertyChanged(nameof(ArchivosDiscurso)); } }

        private ObservableCollection<ArchivoDocumento> _archivosDiscursoSerie;
        public ObservableCollection<ArchivoDocumento> ArchivosDiscursoSerie { get => _archivosDiscursoSerie; set { _archivosDiscursoSerie = value; OnPropertyChanged(nameof(ArchivosDiscursoSerie)); } }

        public void RecargarArchivos()
        {
            ArchivosSimples = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(Titulo));
            ArchivosSinAlojamiento = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagSinAlojamiento));
            ArchivosConAlojamiento = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagConAlojamiento));
            ArchivosAmbos = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagAmbos));
            ArchivosAcompanante = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagAcompanante));
            ArchivosSoloYo = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagSoloYo));

            ArchivosOracionInicial = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagOracionInicial));
            ArchivosDiscurso = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagDiscurso));
            ArchivosDiscursoSerie = new ObservableCollection<ArchivoDocumento>(DatabaseHelper.ObtenerDocumentos(TagDiscursoSerie));
        }
    }
}