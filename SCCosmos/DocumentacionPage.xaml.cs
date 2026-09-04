using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SCCosmos
{
    public partial class DocumentacionPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly string _carpetaDestino = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchivosDocumentacion");

        // Diccionario para gestionar los Auto-Saves sin fugas de memoria
        private Dictionary<WebBrowser, DispatcherTimer> _wbTimers = new Dictionary<WebBrowser, DispatcherTimer>();

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
        // INICIALIZACIÓN DE DATOS
        // ==========================================
        private void CargarCategorias()
        {
            var listaVisitas = new ObservableCollection<CategoriaVista>();

            var aviso = new CategoriaVista
            {
                Titulo = "Aviso S-302",
                Descripcion = "Para subir el archivo S-302 u otros que se enviarán junto con el aviso de la visita.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            aviso.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + aviso.Titulo, "Aviso de la visita");
            aviso.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + aviso.Titulo, "Queridos compañeros,<br><br>Os dejo adjunto el formulario S-302 con la fecha de la próxima visita.<br><br>Recibid un fuerte abrazo.<br><br><b>NOTA AL CCA:</b> Por favor, confírmame la recepción de este correo. ¡Muchas gracias!");
            aviso.RecargarArchivos();
            listaVisitas.Add(aviso);

            listaVisitas.Add(CrearCategoriaColumnas("Documentación", "Para subir varios archivos: Carta para los ancianos, programa de actividad, S-61, u otros."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación (con SCS)", "Para subir varios archivos para la visita con el SCS."));
            listaVisitas.Add(CrearCategoriaColumnas("Documentación (con visita de pastoreo)", "Para subir varios archivos para la visita de pastoreo."));

            var nombramientos = new CategoriaVista
            {
                Titulo = "Nombramientos",
                Descripcion = "Adjunta cartas o notificaciones de nombramiento tras la visita.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            nombramientos.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + nombramientos.Titulo, "Cartas de Nombramiento");
            nombramientos.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + nombramientos.Titulo, "Queridos hermanos,<br><br>Adjunto las cartas de nombramiento de la reciente visita.<br><br>Un fuerte abrazo.");
            nombramientos.RecargarArchivos();
            listaVisitas.Add(nombramientos);

            var envioS303 = new CategoriaVista
            {
                Titulo = "Envío de S-303",
                Descripcion = "Sube y envía el informe oficial S-303 de la visita.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            envioS303.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + envioS303.Titulo, "Informe S-303");
            envioS303.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + envioS303.Titulo, "Queridos hermanos,<br><br>Adjunto el informe S-303 de la visita.<br><br>Un saludo cariñoso.");
            envioS303.RecargarArchivos();
            listaVisitas.Add(envioS303);

            CategoriasVisitas = listaVisitas;

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

            var envioOrganigrama = new CategoriaVista
            {
                Titulo = "Envío de organigrama",
                Descripcion = "Configura el texto del correo y añade archivos extra que quieras adjuntar junto al PDF del organigrama generado.",
                VisibilidadSimple = Visibility.Visible,
                VisibilidadColumnas = Visibility.Collapsed,
                VisibilidadAlojamientoEspecial = Visibility.Collapsed
            };
            envioOrganigrama.AsuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_" + envioOrganigrama.Titulo, "Organigrama AC");
            envioOrganigrama.PlantillaEmail = DatabaseHelper.ObtenerConfiguracion("Email_" + envioOrganigrama.Titulo, "Queridos compañeros,<br><br>Os dejo adjunto el organigrama de la próxima asamblea de circuito.<br><br>Recibid un fuerte abrazo.");
            envioOrganigrama.RecargarArchivos();
            listaAsambleas.Add(envioOrganigrama);

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

            if (titulo == "Documentación")
            {
                cat.VisibilidadAlojamientoEspecial = Visibility.Collapsed;
                cat.VisibilidadConAlojamiento = Visibility.Visible;
                cat.AnchoColumnaEspecial = new GridLength(1, GridUnitType.Star);
            }
            else if (titulo == "Documentación (con SCS)")
            {
                cat.VisibilidadVariablesSCS = Visibility.Visible;
                cat.VarNombreSCS = DatabaseHelper.ObtenerConfiguracion("VarSCS", "$_NOMBRE_SCS");
                cat.VarApellidoSCS = DatabaseHelper.ObtenerConfiguracion("VarApSCS", "$_APELLIDO_SCS");
                cat.VarEsposaSCS = DatabaseHelper.ObtenerConfiguracion("VarEsposaSCS", "$_ESPOSA_SCS");
            }
            else if (titulo == "Documentación (con visita de pastoreo)")
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

        private void RecargarSoloArchivos()
        {
            if (CategoriasVisitas != null) foreach (var cat in CategoriasVisitas) cat.RecargarArchivos();
            if (CategoriasAsambleas != null) foreach (var cat in CategoriasAsambleas) cat.RecargarArchivos();
        }

        // ==========================================
        // MANEJO DE ARCHIVOS ADJUNTOS
        // ==========================================
        private void BtnSubirArchivo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button ?? sender as Wpf.Ui.Controls.Button;
            if (boton == null) return;

            string categoriaDBSave = boton.Tag.ToString();
            OpenFileDialog ofd = new OpenFileDialog { Title = "Seleccionar documento", Filter = "Todos los archivos (*.*)|*.*|Documentos PDF (*.pdf)|*.pdf|Documentos Word (*.docx)|*.docx" };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string nombreArchivo = Path.GetFileName(ofd.FileName);
                    string nombreUnico = Guid.NewGuid().ToString().Substring(0, 5) + "_" + nombreArchivo;
                    string rutaFinal = Path.Combine(_carpetaDestino, nombreUnico);

                    File.Copy(ofd.FileName, rutaFinal);
                    DatabaseHelper.InsertarDocumento(categoriaDBSave, nombreArchivo, rutaFinal);
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

            // Extraemos el archivo desde la BBDD a nuestra carpeta temporal para asegurar que siempre existe
            string rutaExtraida = DatabaseHelper.ExtraerDocumentoDesdeBD(archivo.Id, archivo.NombreArchivo);

            if (!string.IsNullOrEmpty(rutaExtraida) && File.Exists(rutaExtraida))
            {
                Process.Start(new ProcessStartInfo(rutaExtraida) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("No se ha podido encontrar ni extraer el archivo de la Base de Datos. Es posible que sea un archivo muy antiguo que solo existía como acceso directo.", "Archivo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                    RecargarSoloArchivos();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("El archivo no se pudo borrar por completo. " + ex.Message);
                }
            }
        }

        // ==========================================
        // LÓGICA DEL EDITOR DE TEXTO ENRIQUECIDO (CON AUTOGUARDADO DE JAVASCRIPT)
        // ==========================================
        private void WbEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WebBrowser wb)
            {
                // ==============================================================
                // ARREGLO FANTASMAS: Destruimos timers previos de reciclaje WPF
                // ==============================================================
                if (_wbTimers.TryGetValue(wb, out var timerAntiguo))
                {
                    timerAntiguo.Stop();
                    _wbTimers.Remove(wb);
                }

                var timerNuevo = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timerNuevo.Tick += (s, args) =>
                {
                    try
                    {
                        // Leemos el DataContext DINÁMICAMENTE cada segundo
                        if (wb.DataContext is CategoriaVista currentCat)
                        {
                            var isReady = wb.InvokeScript("eval", "window.readyToSave");

                            if (isReady is bool ready && ready)
                            {
                                wb.InvokeScript("eval", "window.readyToSave = false;");
                                string html = wb.InvokeScript("eval", "document.body.innerHTML").ToString();

                                if (!string.IsNullOrWhiteSpace(html) && html != currentCat.PlantillaEmail)
                                {
                                    currentCat.ActualizarPlantillaSilencioso(html);

                                    var panel = FindParent<Grid>(wb);
                                    if (panel != null)
                                    {
                                        var aviso = FindVisualChildByName<TextBlock>(panel, "TxtAvisoGuardado");
                                        if (aviso != null)
                                        {
                                            aviso.Text = "Guardado automático ✓";
                                            DispatcherTimer clearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                                            clearTimer.Tick += (cs, cargs) => { aviso.Text = ""; clearTimer.Stop(); };
                                            clearTimer.Start();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                };
                timerNuevo.Start();
                _wbTimers[wb] = timerNuevo;
            }
        }

        private void WbEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is WebBrowser wb)
            {
                if (_wbTimers.TryGetValue(wb, out var timer))
                {
                    timer.Stop();
                    _wbTimers.Remove(wb);
                }

                // Guardado final dinámico
                if (wb.DataContext is CategoriaVista currentCat)
                {
                    try
                    {
                        string html = wb.InvokeScript("eval", "document.body.innerHTML").ToString();
                        if (!string.IsNullOrWhiteSpace(html) && html != currentCat.PlantillaEmail)
                        {
                            currentCat.ActualizarPlantillaSilencioso(html);
                        }
                    }
                    catch { }
                }
            }
        }

        private void BtnFormato_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            if (btn != null && btn.Tag is string comando)
            {
                var panel = FindParent<Grid>(btn);
                var wb = FindVisualChildByName<WebBrowser>(panel, "WbEditor");
                if (wb != null)
                {
                    try
                    {
                        wb.InvokeScript("eval", $"document.execCommand('{comando}', false, null); window.readyToSave = true;");
                    }
                    catch { }
                }
            }
        }

        private void CmbTamano_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item && cmb.IsLoaded)
            {
                var panel = FindParent<Grid>(cmb);
                var wb = FindVisualChildByName<WebBrowser>(panel, "WbEditor");
                if (wb != null)
                {
                    try
                    {
                        wb.InvokeScript("eval", $"document.execCommand('fontSize', false, '{item.Tag}'); window.readyToSave = true;");
                    }
                    catch { }
                }
            }
        }

        // ==========================================
        // FUNCIONES DE BÚSQUEDA EN EL ÁRBOL VISUAL
        // ==========================================
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? FindParent<T>(parentObject);
        }

        private T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name)
                    return t;

                var result = FindVisualChildByName<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void BtnEnviarMensajeGlobal_Click(object sender, RoutedEventArgs e)
        {
            EnviarMensajeWindow ventanaMensaje = new EnviarMensajeWindow();
            ventanaMensaje.Owner = Window.GetWindow(this);
            ventanaMensaje.ShowDialog();
        }
    }

    // ========================================================
    // CLASE ESPECIAL QUE CONECTA WPF CON EL EDITOR WEBBROWSER
    // ========================================================
    public static class WbEditorHelper
    {
        public static readonly DependencyProperty HtmlProperty =
            DependencyProperty.RegisterAttached("Html", typeof(string), typeof(WbEditorHelper), new PropertyMetadata(string.Empty, OnHtmlChanged));

        public static string GetHtml(DependencyObject obj) => (string)obj.GetValue(HtmlProperty);
        public static void SetHtml(DependencyObject obj, string value) => obj.SetValue(HtmlProperty, value);

        private static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebBrowser wb)
            {
                string html = e.NewValue as string ?? "";

                string docHtml = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                    <style>
                        body {{ font-family: 'Segoe UI', sans-serif; font-size: 16px; color: white; background-color: #252528; margin: 12px; outline: none; }}
                        p {{ margin: 0 0 10px 0; }}
                    </style>
                    <script>
                        window.readyToSave = false;
                        var typingTimer;
                        function debounceSave() {{
                            clearTimeout(typingTimer);
                            typingTimer = setTimeout(function() {{
                                window.readyToSave = true;
                            }}, 1500);
                        }}
                        window.onload = function() {{
                            document.body.addEventListener('input', debounceSave);
                            document.body.addEventListener('keyup', debounceSave);
                            document.body.addEventListener('paste', debounceSave);
                            document.body.addEventListener('cut', debounceSave);
                        }};
                    </script>
                </head>
                <body contenteditable='true'>
                    {html}
                </body>
                </html>";

                wb.NavigateToString(docHtml);
            }
        }
    }

    // ========================================================
    // CLASE DE MODELO PARA CADA PESTAÑA
    // ========================================================
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

        private string _asuntoEmail;
        public string AsuntoEmail
        {
            get => _asuntoEmail;
            set
            {
                if (_asuntoEmail != value)
                {
                    _asuntoEmail = value;
                    OnPropertyChanged(nameof(AsuntoEmail));
                    if (!string.IsNullOrEmpty(Titulo))
                        DatabaseHelper.GuardarConfiguracion("Asunto_" + Titulo, value ?? "");
                }
            }
        }

        private string _plantillaEmail;
        public string PlantillaEmail
        {
            get => _plantillaEmail;
            set
            {
                if (_plantillaEmail != value)
                {
                    _plantillaEmail = value;
                    OnPropertyChanged(nameof(PlantillaEmail));
                }
            }
        }

        public void ActualizarPlantillaSilencioso(string value)
        {
            if (_plantillaEmail != value)
            {
                _plantillaEmail = value;
                if (!string.IsNullOrEmpty(Titulo))
                    DatabaseHelper.GuardarConfiguracion("Email_" + Titulo, value ?? "");
            }
        }

        public string TagSinAlojamiento => Titulo + "|Sin alojamiento";
        public string TagConAlojamiento => Titulo + "|Con alojamiento";
        public string TagAmbos => Titulo + "|Alojamiento (ambos)";
        public string TagAcompanante => Titulo + "|Alojamiento (solo acompañante)";
        public string TagSoloYo => Titulo + "|Alojamiento (solo yo)";

        public string TagOracionInicial => Titulo + "|Oración inicial";
        public string TagDiscurso => Titulo + "|Discurso";
        public string TagDiscursoSerie => Titulo + "|Discurso (serie)";

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