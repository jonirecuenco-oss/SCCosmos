using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCCosmos
{
    public partial class DescargasPage : Page
    {
        public DescargasPage()
        {
            InitializeComponent();
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DescargadorStreamJW.DescargaCambiada += Descargador_EstadoCambiado;
            ActualizarUI_Descarga(DescargadorStreamJW.EstaDescargando);

            // Dejar que la página se pinte antes de montar el explorador.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            CargarExplorador();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DescargadorStreamJW.DescargaCambiada -= Descargador_EstadoCambiado;
        }

        private void Descargador_EstadoCambiado(bool ejecutando)
        {
            Application.Current.Dispatcher.Invoke(() => ActualizarUI_Descarga(ejecutando));
        }

        private void ActualizarUI_Descarga(bool ejecutando)
        {
            if (ejecutando)
            {
                btnDescargar.IsEnabled = false;
                btnDescargar.Content = "DESCARGANDO... POR FAVOR, ESPERE";
            }
            else
            {
                btnDescargar.IsEnabled = true;
                btnDescargar.Content = "INICIAR DESCARGAS";
            }
        }

        // =======================================================
        // OBTENER LA RUTA DESDE LA CONFIGURACIÓN (JSON LOCAL)
        // =======================================================
        private string ObtenerRutaDescargas()
        {
            string ruta = "";
            try
            {
                // Vamos a la misma carpeta donde ConfiguracionPage guarda el archivo
                string rutaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos");
                string rutaArchivo = Path.Combine(rutaAppData, "local_settings.json");

                if (File.Exists(rutaArchivo))
                {
                    string json = File.ReadAllText(rutaArchivo);
                    var localSettings = JsonSerializer.Deserialize<ConfiguracionLocalSettings>(json);

                    if (localSettings != null && !string.IsNullOrWhiteSpace(localSettings.RutaDescargas))
                    {
                        ruta = localSettings.RutaDescargas;
                    }
                }
            }
            catch { }

            // Fallback de seguridad si no hay ruta configurada o no existe la carpeta
            if (string.IsNullOrWhiteSpace(ruta) || !Directory.Exists(ruta))
            {
                ruta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DescargasStream");
                if (!Directory.Exists(ruta)) Directory.CreateDirectory(ruta);
            }

            return ruta;
        }

        private async void btnDescargar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpcionesDescarga misOpciones = new OpcionesDescarga
                {
                    Betel_AdoracionMatutina = chkBetelAdoracion.IsChecked ?? false,
                    Teocraticos_DiscursosPublicos = chkTeoDiscursos.IsChecked ?? false,
                    Teocraticos_AsambleaCircuitoSucursal = chkTeoAsambleaSucursal.IsChecked ?? false,
                    Teocraticos_AsambleaCircuitoSuperintendente = chkTeoAsambleaSuper.IsChecked ?? false,
                    Teocraticos_AsambleaRegional = chkTeoAsambleaRegional.IsChecked ?? false,
                    JwOrg_AdoracionMatutina = chkJwAdoracion.IsChecked ?? false,
                    JwOrg_DiscursosBroadcasting = chkJwDiscursos.IsChecked ?? false,
                    JwOrg_GraduacionGalaad = chkJwGalaad.IsChecked ?? false,
                    JwOrg_ReunionAnual = chkJwReunionAnual.IsChecked ?? false
                };

                string rutaPerfil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
                if (!Directory.Exists(rutaPerfil)) Directory.CreateDirectory(rutaPerfil);

                // 1. Obtenemos la ruta oficial de tu configuración
                string rutaDestinoArchivos = ObtenerRutaDescargas();

                // 2. Ejecutamos pasándole los 3 argumentos que pide ahora el robot
                await Task.Run(() =>
                {
                    DescargadorStreamJW.DescargarUltimoDiscurso(rutaPerfil, rutaDestinoArchivos, misOpciones);
                });

                // Al terminar, actualizamos el explorador automáticamente
                CargarExplorador();

                MessageBox.Show("¡Todas las descargas han finalizado correctamente!", "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error general: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =======================================================
        // EXPLORADOR DE ARCHIVOS EN 2 COLUMNAS (TreeView Dinámico)
        // =======================================================
        // =======================================================
        // EXPLORADOR DE ARCHIVOS EN 3 COLUMNAS (TreeView Dinámico)
        // =======================================================
        private void CargarExplorador()
        {
            ArbolArchivosCol1.Items.Clear();
            ArbolArchivosCol2.Items.Clear();
            ArbolArchivosCol3.Items.Clear(); // <--- Limpiamos la nueva columna 3

            string rutaDescargas = ObtenerRutaDescargas();

            if (Directory.Exists(rutaDescargas))
            {
                var directorios = Directory.GetDirectories(rutaDescargas);
                var listaCarpetas = new System.Collections.Generic.List<string>();

                foreach (var d in directorios)
                {
                    if (!new DirectoryInfo(d).Name.Equals("_Temp", StringComparison.OrdinalIgnoreCase))
                    {
                        listaCarpetas.Add(d);
                    }
                }

                // Repartimos las carpetas entre las 3 columnas (Rellenando hacia abajo y luego a la derecha)
                int itemsPorColumna = (int)Math.Ceiling(listaCarpetas.Count / 3.0);

                for (int i = 0; i < listaCarpetas.Count; i++)
                {
                    var dirInfo = new DirectoryInfo(listaCarpetas[i]);
                    var rootNodo = new TreeViewItem
                    {
                        Header = "📁 " + dirInfo.Name,
                        IsExpanded = false, // Cerradas por defecto
                        // Eliminado el FontWeight.Bold según tu petición
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8FBCBB")),
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    ConstruirNivel(listaCarpetas[i], rootNodo);

                    // Distribución en 3 columnas
                    if (i < itemsPorColumna) ArbolArchivosCol1.Items.Add(rootNodo);
                    else if (i < itemsPorColumna * 2) ArbolArchivosCol2.Items.Add(rootNodo);
                    else ArbolArchivosCol3.Items.Add(rootNodo);
                }

                // Los archivos sueltos en la raíz van a la primera columna
                var archivosSueltos = Directory.GetFiles(rutaDescargas);
                foreach (var file in archivosSueltos)
                {
                    var fileInfo = new FileInfo(file);
                    var fileNodo = new TreeViewItem
                    {
                        Header = "📄 " + fileInfo.Name,
                        Foreground = Brushes.WhiteSmoke,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(0, 0, 0, 2)
                    };

                    fileNodo.MouseDoubleClick += (s, e) =>
                    {
                        e.Handled = true;
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileInfo.FullName) { UseShellExecute = true }); }
                        catch (Exception ex) { MessageBox.Show($"No se puede abrir el archivo:\n{ex.Message}"); }
                    };

                    ArbolArchivosCol1.Items.Add(fileNodo);
                }

                if (ArbolArchivosCol1.Items.Count == 0 && ArbolArchivosCol2.Items.Count == 0 && ArbolArchivosCol3.Items.Count == 0)
                {
                    ArbolArchivosCol1.Items.Add(new TreeViewItem { Header = "La carpeta está vacía.", Foreground = Brushes.Gray });
                }
            }
            else
            {
                ArbolArchivosCol1.Items.Add(new TreeViewItem { Header = "Carpeta no encontrada o no configurada.", Foreground = Brushes.Gray });
            }
        }

        private void ConstruirNivel(string rutaDirectorio, TreeViewItem nodoPadre)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(rutaDirectorio))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.Name.Equals("_Temp", StringComparison.OrdinalIgnoreCase)) continue;

                    var subNodo = new TreeViewItem
                    {
                        Header = "📁 " + dirInfo.Name,
                        Foreground = Brushes.LightGray,
                        IsExpanded = false // <--- CERRADAS POR DEFECTO
                    };

                    // El contenido de cada carpeta se construye solo al abrirla:
                    // así la página entra al instante aunque haya miles de
                    // archivos. El hijo provisional es para que salga la flecha.
                    bool tieneContenido = false;
                    try
                    {
                        using (var enumerador = Directory.EnumerateFileSystemEntries(dir).GetEnumerator())
                        {
                            tieneContenido = enumerador.MoveNext();
                        }
                    }
                    catch { }

                    if (tieneContenido)
                    {
                        subNodo.Items.Add(new TreeViewItem { Visibility = Visibility.Collapsed });
                        subNodo.Tag = dir;
                        subNodo.Expanded += NodoCarpeta_Expanded;
                    }

                    nodoPadre.Items.Add(subNodo);
                }

                foreach (var file in Directory.GetFiles(rutaDirectorio))
                {
                    var fileInfo = new FileInfo(file);
                    var fileNodo = new TreeViewItem
                    {
                        Header = "📄 " + fileInfo.Name,
                        Foreground = Brushes.WhiteSmoke,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    fileNodo.MouseDoubleClick += (s, e) =>
                    {
                        e.Handled = true;
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileInfo.FullName) { UseShellExecute = true }); }
                        catch (Exception ex) { MessageBox.Show($"No se puede abrir el archivo:\n{ex.Message}"); }
                    };

                    nodoPadre.Items.Add(fileNodo);
                }
            }
            catch { }
        }

        private void NodoCarpeta_Expanded(object sender, RoutedEventArgs e)
        {
            var nodo = sender as TreeViewItem;

            if (nodo == null || !(nodo.Tag is string ruta))
            {
                return;
            }

            nodo.Expanded -= NodoCarpeta_Expanded;
            nodo.Items.Clear();
            ConstruirNivel(ruta, nodo);

            if (nodo.Items.Count == 0)
            {
                nodo.Items.Add(new TreeViewItem { Header = "(carpeta vacía)", Foreground = Brushes.Gray });
            }
        }

        

        private void BtnActualizarExplorador_Click(object sender, RoutedEventArgs e)
        {
            CargarExplorador();
        }

        private void BtnAbrirCarpeta_Click(object sender, RoutedEventArgs e)
        {
            string ruta = ObtenerRutaDescargas();
            if (Directory.Exists(ruta))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{ruta}\"") { UseShellExecute = true });
            }
        }
    }
}