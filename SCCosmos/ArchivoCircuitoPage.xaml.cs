using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

// Librerías PDF (iText7)
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.IO.Font.Constants;

using Color = iText.Kernel.Colors.Color;
using Path = System.IO.Path;
using TextAlignment = iText.Layout.Properties.TextAlignment;
using Paragraph = iText.Layout.Element.Paragraph;
using Table = iText.Layout.Element.Table;
using Cell = iText.Layout.Element.Cell;

namespace SCCosmos
{
    public partial class ArchivoCircuitoPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _rutaArchivosInternos;
        private string _rutaJsonSecciones;
        private string _rutaJsonCarpetas;
        private string _rutaJsonArchivos;

        public ObservableCollection<SeccionVFS> ListaSecciones { get; set; }
        private List<CarpetaVFS> _registroCarpetas;
        private List<ArchivoVFS> _registroArchivos;

        public ObservableCollection<HermanoSCS> DatosSCS { get; set; }
        public ObservableCollection<HermanoPotencial> DatosPotencial { get; set; }
        public ObservableCollection<ProblemaPendiente> DatosProblemas { get; set; }
        public ObservableCollection<string> ListaCongregaciones { get; set; }
        public ObservableCollection<HermanoDropdown> ListaTodosAncianosSiervos { get; set; }

        // Variables para el Modal y Estado de UI
        private Action<string> _modalCallback;
        private HashSet<string> _nodosExpandidos = new HashSet<string>();
        private string _nodoSeleccionadoId = null;
        private string _seccionActualId = null;

        public ArchivoCircuitoPage()
        {
            InitializeComponent();
            this.DataContext = this;

            _rutaArchivosInternos = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "ArchivosGuardados");
            _rutaJsonSecciones = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_SeccionesVFS.json");
            _rutaJsonCarpetas = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_CarpetasVFS.json");
            _rutaJsonArchivos = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_FicherosVFS.json");

            if (!Directory.Exists(_rutaArchivosInternos)) Directory.CreateDirectory(_rutaArchivosInternos);

            ListaSecciones = new ObservableCollection<SeccionVFS>();
            _registroCarpetas = new List<CarpetaVFS>();
            _registroArchivos = new List<ArchivoVFS>();
            DatosSCS = new ObservableCollection<HermanoSCS>();
            DatosPotencial = new ObservableCollection<HermanoPotencial>();
            DatosProblemas = new ObservableCollection<ProblemaPendiente>();

            CargarVFS();
            CargarDesplegables();
        }

        // =========================================================
        // NÚCLEO DEL SISTEMA DE ARCHIVOS VIRTUAL (VFS)
        // =========================================================
        private void CargarVFS()
        {
            if (File.Exists(_rutaJsonSecciones))
                ListaSecciones = new ObservableCollection<SeccionVFS>(JsonSerializer.Deserialize<List<SeccionVFS>>(File.ReadAllText(_rutaJsonSecciones)) ?? new List<SeccionVFS>());
            if (File.Exists(_rutaJsonCarpetas))
                _registroCarpetas = JsonSerializer.Deserialize<List<CarpetaVFS>>(File.ReadAllText(_rutaJsonCarpetas)) ?? new List<CarpetaVFS>();
            if (File.Exists(_rutaJsonArchivos))
                _registroArchivos = JsonSerializer.Deserialize<List<ArchivoVFS>>(File.ReadAllText(_rutaJsonArchivos)) ?? new List<ArchivoVFS>();

            if (ListaSecciones.Count == 0) InicializarSeccionesPorDefecto();

            ListaCategorias.ItemsSource = ListaSecciones;

            // Abre por defecto la primera sección si existen elementos
            if (ListaSecciones.Count > 0)
            {
                ListaCategorias.SelectedIndex = 0;
            }
        }

        private void GuardarVFS()
        {
            try
            {
                File.WriteAllText(_rutaJsonSecciones, JsonSerializer.Serialize(ListaSecciones.ToList()));
                File.WriteAllText(_rutaJsonCarpetas, JsonSerializer.Serialize(_registroCarpetas));
                File.WriteAllText(_rutaJsonArchivos, JsonSerializer.Serialize(_registroArchivos));
            }
            catch { }
        }

        private void InicializarSeccionesPorDefecto()
        {
            string[] nombres = { "1 - 2 - S-303 y S-2", "3 - Archivos judiciales", "4 - 5 - Oradores asambleas", "6 - Asuntos complejos", "7 - Instructores ESP", "8 - Organización asambleas", "9 - SC que comparten salón", "10 - SCS", "11 - Correspondencia EER (SKE)", "12 - Hermanos con potencial", "13 - Histórico congregaciones visitadas", "14 - Problemas pendientes", "15 - CEH", "16 - Último informe S-318", "17 - Formularios CO-14" };

            foreach (var nombre in nombres)
            {
                var match = System.Text.RegularExpressions.Regex.Match(nombre, @"^\d+");
                int idEspecial = match.Success ? int.Parse(match.Value) : 0;
                string nombreLimpio = System.Text.RegularExpressions.Regex.Replace(nombre, @"^[\d\s\-]+", "").ToUpper();

                var seccion = new SeccionVFS { NombreLimpio = nombreLimpio, IdEspecial = idEspecial };
                ListaSecciones.Add(seccion);

                if (idEspecial == 1)
                {
                    string[] visitas = { "Entre visitas", "Visita 1", "Visita 2", "Visita 3", "Visita 4", "Visita 5", "Visita 6" };
                    foreach (var cong in DatabaseHelper.ObtenerListaCongregaciones())
                    {
                        var cCong = new CarpetaVFS { SeccionId = seccion.Id, ParentId = null, Nombre = cong };
                        _registroCarpetas.Add(cCong);
                        foreach (var v in visitas) _registroCarpetas.Add(new CarpetaVFS { SeccionId = seccion.Id, ParentId = cCong.Id, Nombre = v });
                    }
                }
            }
            GuardarVFS();
        }

        // =========================================================
        // CONTROL DE SECCIONES Y MODALES
        // =========================================================
        private void MostrarModal(string titulo, string valorDefecto, Action<string> callback)
        {
            TxtModalTitulo.Text = titulo;
            TxtModalInput.Text = valorDefecto;
            _modalCallback = callback;
            ModalOverlay.Visibility = Visibility.Visible;
            TxtModalInput.Focus();
            TxtModalInput.SelectAll();
        }

        private void BtnModalCancelar_Click(object sender, RoutedEventArgs e) { ModalOverlay.Visibility = Visibility.Collapsed; }
        private void BtnModalAceptar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtModalInput.Text)) return;
            ModalOverlay.Visibility = Visibility.Collapsed;
            _modalCallback?.Invoke(TxtModalInput.Text.Trim());
        }

        private void BtnNuevaSeccion_Click(object sender, RoutedEventArgs e)
        {
            MostrarModal("Nombre de la nueva sección:", "", (nuevoNombre) => {
                var sec = new SeccionVFS { NombreLimpio = nuevoNombre.ToUpper(), IdEspecial = 0 };
                ListaSecciones.Add(sec);
                GuardarVFS();
                ListaCategorias.SelectedItem = sec;
            });
        }

        private void MenuRenombrarSeccion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SeccionVFS sec)
            {
                MostrarModal("Renombrar sección:", sec.NombreLimpio, (nuevoNombre) => {
                    sec.NombreLimpio = nuevoNombre.ToUpper();
                    GuardarVFS();
                    ListaCategorias.Items.Refresh();
                    if (ListaCategorias.SelectedItem == sec) ConstruirArbolVirtual(sec);
                });
            }
        }

        private void MenuEliminarSeccion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SeccionVFS sec)
            {
                if (MessageBox.Show($"¿Eliminar completamente la sección '{sec.NombreLimpio}' y TODO su contenido?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    EliminarSeccionRecursivo(sec.Id);
                    ListaSecciones.Remove(sec);
                    GuardarVFS();
                    ArbolArchivos.Items.Clear();
                    OcultarPreviews();
                }
            }
        }

        private void ListaCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListaCategorias.SelectedItem is SeccionVFS sec)
            {
                PanelSCS.Visibility = Visibility.Collapsed; PanelPotencial.Visibility = Visibility.Collapsed; PanelProblemas.Visibility = Visibility.Collapsed; BtnGenerarPDF.Visibility = Visibility.Collapsed;
                if (sec.IdEspecial == 4 || sec.IdEspecial == 8 || sec.IdEspecial == 10 || sec.IdEspecial == 12 || sec.IdEspecial == 13 || sec.IdEspecial == 14) BtnGenerarPDF.Visibility = Visibility.Visible;
                if (sec.IdEspecial == 10) { PanelSCS.Visibility = Visibility.Visible; CargarTablaSCS(); }
                if (sec.IdEspecial == 12) { PanelPotencial.Visibility = Visibility.Visible; CargarTablaPotencial(); }
                if (sec.IdEspecial == 14) { PanelProblemas.Visibility = Visibility.Visible; CargarTablaProblemas(); }

                ConstruirArbolVirtual(sec);
            }
        }

        // =========================================================
        // MEMORIA VISUAL (Gestor de estado del árbol)
        // =========================================================
        private void GuardarEstadoArbol()
        {
            _nodosExpandidos.Clear();
            _nodoSeleccionadoId = null;

            if (ListaCategorias.SelectedItem is SeccionVFS sec)
            {
                if (_seccionActualId != sec.Id)
                {
                    _seccionActualId = sec.Id;
                    return; // Si cambiamos de sección, empezamos limpios
                }
            }
            RecorrerNodos(ArbolArchivos.Items);
        }

        private void RecorrerNodos(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag is InfoNodo info)
                {
                    string id = info.EsSeccionRaiz ? info.SeccionId : (info.EsCarpeta ? info.CarpetaVFS.Id : (info.EsArchivo ? info.ArchivoVFS.Id : null));
                    if (id != null)
                    {
                        if (item.IsExpanded) _nodosExpandidos.Add(id);
                        if (item.IsSelected) _nodoSeleccionadoId = id;
                    }
                }
                if (item.Items.Count > 0) RecorrerNodos(item.Items);
            }
        }

        // =========================================================
        // CONSTRUCCIÓN DEL ÁRBOL VIRTUAL
        // =========================================================
        private void ConstruirArbolVirtual(SeccionVFS sec, string forzarExpansionId = null)
        {
            GuardarEstadoArbol();

            if (forzarExpansionId != null) _nodosExpandidos.Add(forzarExpansionId);

            ArbolArchivos.Items.Clear();
            OcultarPreviews();

            var infoRaiz = new InfoNodo { EsSeccionRaiz = true, SeccionId = sec.Id, Nombre = sec.NombreLimpio };
            bool expandirRaiz = _nodosExpandidos.Count == 0 || _nodosExpandidos.Contains(sec.Id);

            var spRaiz = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            spRaiz.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Library24, FontSize = 18, Margin = new Thickness(0, 0, 8, 0), Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#EBCB8B")) });
            spRaiz.Children.Add(new TextBlock { Text = sec.NombreLimpio, VerticalAlignment = System.Windows.VerticalAlignment.Center });

            var nodoRaiz = new TreeViewItem
            {
                Header = spRaiz,
                Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#EBCB8B")),
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = infoRaiz,
                IsExpanded = expandirRaiz
            };

            if (_nodoSeleccionadoId == sec.Id) nodoRaiz.IsSelected = true;

            spRaiz.MouseLeftButtonUp += (s, ev) => { nodoRaiz.IsExpanded = !nodoRaiz.IsExpanded; };

            nodoRaiz.ContextMenu = CrearMenuCarpeta(infoRaiz);

            PoblarNodoCarpeta(nodoRaiz, sec.Id, null);
            ArbolArchivos.Items.Add(nodoRaiz);
        }

        private void PoblarNodoCarpeta(TreeViewItem parentNode, string seccionId, string parentFolderId)
        {
            var subcarpetas = _registroCarpetas.Where(c => c.SeccionId == seccionId && c.ParentId == parentFolderId).OrderBy(c => c.Nombre).ToList();
            foreach (var c in subcarpetas)
            {
                var infoCarpeta = new InfoNodo { EsCarpeta = true, CarpetaVFS = c, Nombre = c.Nombre };

                var spFolder = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
                spFolder.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Folder24, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#8FBCBB")) });
                spFolder.Children.Add(new TextBlock { Text = c.Nombre, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                var nodoFolder = new TreeViewItem
                {
                    Header = spFolder,
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#8FBCBB")),
                    Margin = new Thickness(0, 0, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = infoCarpeta,
                    IsExpanded = _nodosExpandidos.Contains(c.Id)
                };

                if (_nodoSeleccionadoId == c.Id) nodoFolder.IsSelected = true;

                spFolder.MouseLeftButtonUp += (s, ev) => { nodoFolder.IsExpanded = !nodoFolder.IsExpanded; };

                nodoFolder.ContextMenu = CrearMenuCarpeta(infoCarpeta);

                PoblarNodoCarpeta(nodoFolder, seccionId, c.Id);
                parentNode.Items.Add(nodoFolder);
            }

            var archivos = _registroArchivos.Where(a => a.SeccionId == seccionId && a.CarpetaId == parentFolderId).OrderBy(a => a.NombreOriginal).ToList();
            foreach (var a in archivos)
            {
                var infoArchivo = new InfoNodo { EsArchivo = true, ArchivoVFS = a, Nombre = a.NombreOriginal };

                var spFile = new StackPanel { Orientation = Orientation.Horizontal };
                spFile.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Document24, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.WhiteSmoke });
                spFile.Children.Add(new TextBlock { Text = a.NombreOriginal, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                var nodoArchivo = new TreeViewItem
                {
                    Header = spFile,
                    Foreground = Brushes.WhiteSmoke,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 0, 2),
                    Tag = infoArchivo
                };

                if (_nodoSeleccionadoId == a.Id) nodoArchivo.IsSelected = true;

                var menuArch = new ContextMenu();
                var mnuRen = new MenuItem { Header = "✏️ Renombrar archivo" }; mnuRen.Click += (s, ev) => RenombrarArchivo(a);
                var mnuDel = new MenuItem { Header = "🗑️ Eliminar archivo", Foreground = Brushes.Red }; mnuDel.Click += (s, ev) => EliminarArchivo(a);
                menuArch.Items.Add(mnuRen); menuArch.Items.Add(mnuDel);
                nodoArchivo.ContextMenu = menuArch;

                nodoArchivo.MouseDoubleClick += (s, ev) => {
                    ev.Handled = true;
                    string ruta = Path.Combine(_rutaArchivosInternos, a.NombreFisico);
                    if (File.Exists(ruta)) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true }); } catch { } }
                };
                parentNode.Items.Add(nodoArchivo);
            }
        }

        private ContextMenu CrearMenuCarpeta(InfoNodo info)
        {
            var menu = new ContextMenu();
            var mnuSubir = new MenuItem { Header = "⬆️ Subir archivo aquí", Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#4CAF50")) };
            mnuSubir.Click += (s, e) => LanzarDialogoSubida(info);
            menu.Items.Add(mnuSubir);

            var mnuCrear = new MenuItem { Header = "➕ Crear subcarpeta" };
            mnuCrear.Click += (s, e) => CrearSubcarpeta(info);
            menu.Items.Add(mnuCrear);

            if (info.EsCarpeta)
            {
                menu.Items.Add(new Separator());
                var mnuRen = new MenuItem { Header = "✏️ Renombrar carpeta" }; mnuRen.Click += (s, e) => RenombrarCarpeta(info.CarpetaVFS);
                var mnuDel = new MenuItem { Header = "🗑️ Eliminar carpeta y contenido", Foreground = Brushes.Red }; mnuDel.Click += (s, e) => EliminarCarpeta(info.CarpetaVFS);
                menu.Items.Add(mnuRen); menu.Items.Add(mnuDel);
            }
            return menu;
        }

        // =========================================================
        // ACCIONES CRUD VFS (Carpetas y Archivos)
        // =========================================================
        private void CrearSubcarpeta(InfoNodo padreInfo)
        {
            string secId = padreInfo.EsSeccionRaiz ? padreInfo.SeccionId : padreInfo.CarpetaVFS.SeccionId;
            string parentId = padreInfo.EsSeccionRaiz ? null : padreInfo.CarpetaVFS.Id;

            MostrarModal("Nombre de la nueva carpeta:", "", (nombre) => {
                _registroCarpetas.Add(new CarpetaVFS { SeccionId = secId, ParentId = parentId, Nombre = nombre });
                GuardarVFS();
                string forzarAperturaId = padreInfo.EsSeccionRaiz ? secId : parentId;
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec, forzarAperturaId);
            });
        }

        private void RenombrarCarpeta(CarpetaVFS carpeta)
        {
            MostrarModal("Renombrar carpeta:", carpeta.Nombre, (nuevoNombre) => {
                carpeta.Nombre = nuevoNombre; GuardarVFS();
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec);
            });
        }

        private void EliminarCarpeta(CarpetaVFS carpeta)
        {
            if (MessageBox.Show($"¿Eliminar '{carpeta.Nombre}' y todo su contenido?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                EliminarCarpetaRecursivo(carpeta.Id); GuardarVFS();
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec);
            }
        }

        private void RenombrarArchivo(ArchivoVFS archivo)
        {
            MostrarModal("Renombrar archivo (mantén la extensión):", archivo.NombreOriginal, (nuevoNombre) => {
                archivo.NombreOriginal = nuevoNombre; GuardarVFS();
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec);
            });
        }

        private void EliminarArchivo(ArchivoVFS archivo)
        {
            if (MessageBox.Show($"¿Eliminar archivo '{archivo.NombreOriginal}'?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _registroArchivos.Remove(archivo);
                try { File.Delete(Path.Combine(_rutaArchivosInternos, archivo.NombreFisico)); } catch { }
                GuardarVFS();
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec);
            }
        }

        private void EliminarSeccionRecursivo(string seccionId)
        {
            var carpetas = _registroCarpetas.Where(c => c.SeccionId == seccionId).ToList();
            foreach (var c in carpetas) EliminarCarpetaRecursivo(c.Id);

            var archivos = _registroArchivos.Where(a => a.SeccionId == seccionId && a.CarpetaId == null).ToList();
            foreach (var a in archivos)
            {
                _registroArchivos.Remove(a);
                try { File.Delete(Path.Combine(_rutaArchivosInternos, a.NombreFisico)); } catch { }
            }
        }

        private void EliminarCarpetaRecursivo(string carpetaId)
        {
            var subcarpetas = _registroCarpetas.Where(c => c.ParentId == carpetaId).ToList();
            foreach (var sc in subcarpetas) EliminarCarpetaRecursivo(sc.Id);

            var archivos = _registroArchivos.Where(a => a.CarpetaId == carpetaId).ToList();
            foreach (var a in archivos)
            {
                _registroArchivos.Remove(a);
                try { File.Delete(Path.Combine(_rutaArchivosInternos, a.NombreFisico)); } catch { }
            }
            _registroCarpetas.RemoveAll(c => c.Id == carpetaId);
        }

        // =========================================================
        // SUBIDA DE ARCHIVOS
        // =========================================================
        private void LanzarDialogoSubida(InfoNodo destinoInfo)
        {
            string secId = destinoInfo.EsSeccionRaiz ? destinoInfo.SeccionId : destinoInfo.CarpetaVFS.SeccionId;
            string carpId = destinoInfo.EsSeccionRaiz ? null : destinoInfo.CarpetaVFS.Id;

            OpenFileDialog ofd = new OpenFileDialog { Title = $"Subir a {destinoInfo.Nombre}", Multiselect = true };
            if (ofd.ShowDialog() == true)
            {
                foreach (string f in ofd.FileNames)
                {
                    string nombreReal = Path.GetFileName(f);
                    string nombreInterno = Guid.NewGuid().ToString() + Path.GetExtension(f);
                    try
                    {
                        File.Copy(f, Path.Combine(_rutaArchivosInternos, nombreInterno), true);
                        _registroArchivos.Add(new ArchivoVFS { SeccionId = secId, CarpetaId = carpId, NombreOriginal = nombreReal, NombreFisico = nombreInterno });
                    }
                    catch { }
                }
                GuardarVFS();
                string forzarAperturaId = destinoInfo.EsSeccionRaiz ? secId : carpId;
                if (ListaCategorias.SelectedItem is SeccionVFS sec) ConstruirArbolVirtual(sec, forzarAperturaId);
            }
        }

        private void BtnSubirArchivoGeneral_Click(object sender, RoutedEventArgs e)
        {
            if (ListaCategorias.SelectedItem is SeccionVFS sec)
            {
                var info = new InfoNodo { EsSeccionRaiz = true, SeccionId = sec.Id, Nombre = sec.NombreLimpio };
                LanzarDialogoSubida(info);
            }
        }
        private void BtnSubirEnCarpeta_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is InfoNodo info) LanzarDialogoSubida(info); }
        private void BtnCrearSubcarpeta_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is InfoNodo info) CrearSubcarpeta(info); }

        // =========================================================
        // PREVISUALIZADOR Y GESTIÓN DE EVENTOS DE INTERFAZ
        // =========================================================
        private void ArbolArchivos_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ArbolArchivos.Items.Count == 0) return; // Evita parpadeos al vaciar la lista internamente

            OcultarPreviews();
            if (((TreeView)sender).SelectedItem is TreeViewItem item && item.Tag is InfoNodo info)
            {
                if (info.EsArchivo)
                {
                    string rutaFisica = Path.Combine(_rutaArchivosInternos, info.ArchivoVFS.NombreFisico);
                    if (File.Exists(rutaFisica)) MostrarPreview(rutaFisica, info.ArchivoVFS.NombreOriginal);
                }
                else
                {
                    IconoCarpetaPreview.Symbol = info.EsSeccionRaiz ? Wpf.Ui.Controls.SymbolRegular.Library24 : Wpf.Ui.Controls.SymbolRegular.FolderOpen24;
                    TxtNombreCarpetaPreview.Text = info.Nombre;
                    BtnSubirEnCarpeta.Tag = info;
                    BtnCrearSubcarpeta.Tag = info;
                    PreviewCarpeta.Visibility = Visibility.Visible;
                }
            }
            else PreviewDefault.Visibility = Visibility.Visible;
        }

        private void OcultarPreviews()
        {
            PreviewDefault.Visibility = Visibility.Collapsed; PreviewCarpeta.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Collapsed; PreviewText.Visibility = Visibility.Collapsed;
            PreviewPdf.Visibility = Visibility.Collapsed; PreviewOfficeDirecto.Visibility = Visibility.Collapsed; PreviewNoSoportado.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null; PreviewText.Text = string.Empty;
            try { PreviewPdf.Source = new Uri("about:blank"); } catch { }
        }

        private void MostrarPreview(string rutaFisica, string nombreOriginal)
        {
            try
            {
                string extension = Path.GetExtension(nombreOriginal).ToLower();
                if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif")
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(rutaFisica); bitmap.EndInit();
                    PreviewImage.Source = bitmap; PreviewImage.Visibility = Visibility.Visible;
                }
                else if (extension == ".txt" || extension == ".csv" || extension == ".json" || extension == ".log" || extension == ".xml")
                {
                    string contenido = File.ReadAllText(rutaFisica);
                    if (contenido.Length > 10000) contenido = contenido.Substring(0, 10000) + "\n\n... [Contenido truncado] ...";
                    PreviewText.Text = contenido; PreviewText.Visibility = Visibility.Visible;
                }
                else if (extension == ".pdf") { PreviewPdf.Source = new Uri(rutaFisica); PreviewPdf.Visibility = Visibility.Visible; }
                else if (extension == ".doc" || extension == ".docx")
                {
                    IconoOffice.Symbol = Wpf.Ui.Controls.SymbolRegular.Document24; IconoOffice.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#2B579A"));
                    TxtNombreOffice.Text = nombreOriginal; BtnAbrirOffice.Content = "Abrir en Word"; BtnAbrirOffice.Tag = rutaFisica; PreviewOfficeDirecto.Visibility = Visibility.Visible;
                }
                else if (extension == ".xls" || extension == ".xlsx")
                {
                    IconoOffice.Symbol = Wpf.Ui.Controls.SymbolRegular.DocumentData24; IconoOffice.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#217346"));
                    TxtNombreOffice.Text = nombreOriginal; BtnAbrirOffice.Content = "Abrir en Excel"; BtnAbrirOffice.Tag = rutaFisica; PreviewOfficeDirecto.Visibility = Visibility.Visible;
                }
                else { TxtNombreArchivoPreview.Text = nombreOriginal; PreviewNoSoportado.Visibility = Visibility.Visible; }
            }
            catch { TxtNombreArchivoPreview.Text = nombreOriginal; PreviewNoSoportado.Visibility = Visibility.Visible; }
        }

        private void BtnAbrirOffice_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string rutaFisica) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rutaFisica) { UseShellExecute = true }); } catch { } }

        // =========================================================
        // CARGA DE TABLAS Y DESPLEGABLES
        // =========================================================
        private void CargarDesplegables()
        {
            var congs = DatabaseHelper.ObtenerListaCongregaciones(); ListaCongregaciones = new ObservableCollection<string>(congs);
            var personas = DatabaseHelper.ObtenerPersonas(); var listaGente = new List<HermanoDropdown>();
            foreach (var p in personas) { string nom = p.Nombramiento?.ToLower() ?? ""; if (nom.Contains("anciano") || nom.Contains("siervo ministerial")) listaGente.Add(new HermanoDropdown { Id = p.Id, Nombre = p.Nombre, Congregacion = p.CongregacionNombre }); }
            ListaTodosAncianosSiervos = new ObservableCollection<HermanoDropdown>(listaGente.OrderBy(x => x.Nombre));
        }
        private void CargarTablaSCS()
        {
            DatosSCS.Clear(); foreach (var p in DatabaseHelper.ObtenerPersonas()) { string privs = p.Privilegios?.ToLower() ?? ""; string nom = p.Nombramiento?.ToLower() ?? ""; if (privs.Contains("scs") || privs.Contains("sustituto") || nom.Contains("scs") || nom.Contains("sustituto")) DatosSCS.Add(new HermanoSCS { Nombre = p.Nombre, Congregacion = p.CongregacionNombre, Telefono = p.Telefono, Email = !string.IsNullOrWhiteSpace(p.EmailJW) ? p.EmailJW : p.Email }); }
            TablaSCS.ItemsSource = DatosSCS;
        }
        private void CargarTablaPotencial() { DatosPotencial.Clear(); string a = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_HermanosPotencial.json"); if (File.Exists(a)) { try { var l = JsonSerializer.Deserialize<List<HermanoPotencial>>(File.ReadAllText(a)); if (l != null) foreach (var h in l) { h.PropertyChanged += (s, e) => GuardarFilaPotencial(); DatosPotencial.Add(h); } } catch { } } TablaPotencial.ItemsSource = DatosPotencial; }
        private void GuardarFilaPotencial() { try { File.WriteAllText(Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_HermanosPotencial.json"), JsonSerializer.Serialize(DatosPotencial.ToList())); } catch { } }
        private void BtnAñadirPotencial_Click(object sender, RoutedEventArgs e) { var h = new HermanoPotencial(); h.PropertyChanged += (s, ev) => GuardarFilaPotencial(); DatosPotencial.Add(h); GuardarFilaPotencial(); }
        private void BtnEliminarPotencial_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is HermanoPotencial h) { DatosPotencial.Remove(h); GuardarFilaPotencial(); } }
        private void CmbPotencial_SelectionChanged(object sender, SelectionChangedEventArgs e) { GuardarFilaPotencial(); }
        private void CargarTablaProblemas() { DatosProblemas.Clear(); string a = Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_ProblemasPendientes.json"); if (File.Exists(a)) { try { var l = JsonSerializer.Deserialize<List<ProblemaPendiente>>(File.ReadAllText(a)); if (l != null) foreach (var p in l) { p.PropertyChanged += (s, e) => GuardarFilaProblema(); DatosProblemas.Add(p); } } catch { } } TablaProblemas.ItemsSource = DatosProblemas; }
        private void GuardarFilaProblema() { try { File.WriteAllText(Path.Combine(DatabaseHelper.RutaDirectorioDatos, "Archivo_ProblemasPendientes.json"), JsonSerializer.Serialize(DatosProblemas.ToList())); } catch { } }
        private void BtnAñadirProblema_Click(object sender, RoutedEventArgs e) { var p = new ProblemaPendiente(); p.PropertyChanged += (s, ev) => GuardarFilaProblema(); DatosProblemas.Add(p); GuardarFilaProblema(); }
        private void BtnEliminarProblema_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ProblemaPendiente p) { DatosProblemas.Remove(p); GuardarFilaProblema(); } }
        private void CmbProblema_SelectionChanged(object sender, SelectionChangedEventArgs e) { GuardarFilaProblema(); }

        // =========================================================
        // GENERACIÓN DE PDFs 
        // =========================================================
        private PdfFont GetFont(bool bold = false) { try { return PdfFontFactory.CreateFont(bold ? StandardFonts.HELVETICA_BOLD : StandardFonts.HELVETICA); } catch { return null; } }
        private void BtnGenerarPDF_Click(object sender, RoutedEventArgs e)
        {
            if (ListaCategorias.SelectedItem is SeccionVFS sec)
            {
                string nombreOriginal = $"Reporte_{sec.NombreLimpio}_{DateTime.Now.ToString("dd-MM-yyyy")}.pdf";
                string nombreFisico = Guid.NewGuid().ToString() + ".pdf";
                string rutaFinal = Path.Combine(_rutaArchivosInternos, nombreFisico);

                try
                {
                    using (PdfWriter writer = new PdfWriter(rutaFinal))
                    using (PdfDocument pdf = new PdfDocument(writer))
                    using (Document doc = new Document(pdf))
                    {
                        doc.SetMargins(20, 20, 20, 20); doc.Add(new Paragraph(sec.NombreLimpio).SetFont(GetFont(true)).SetFontSize(16).SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(20));
                        switch (sec.IdEspecial) { case 4: GenerarPDF_Oradores(doc); break; case 8: GenerarPDF_Organizacion(doc); break; case 10: GenerarPDF_SCS(doc); break; case 12: GenerarPDF_Potencial(doc); break; case 13: GenerarPDF_Historico(doc); break; case 14: GenerarPDF_Problemas(doc); break; }
                    }
                    _registroArchivos.Add(new ArchivoVFS { SeccionId = sec.Id, CarpetaId = null, NombreOriginal = nombreOriginal, NombreFisico = nombreFisico });
                    GuardarVFS(); ConstruirArbolVirtual(sec, sec.Id); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rutaFinal) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Error generando el PDF:\n{ex.Message}", "Error PDF", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        private void GenerarPDF_Oradores(Document doc) { doc.Add(new Paragraph("Listado global de oradores.").SetFont(GetFont()).SetFontSize(10).SetMarginBottom(10)); Table t = new Table(new float[] { 3, 2, 1 }).UseAllAvailableWidth(); t.AddHeaderCell(new Cell().Add(new Paragraph("NOMBRE").SetFont(GetFont(true)))); t.AddHeaderCell(new Cell().Add(new Paragraph("CONGREGACIÓN").SetFont(GetFont(true)))); t.AddHeaderCell(new Cell().Add(new Paragraph("TELÉFONO").SetFont(GetFont(true)))); foreach (var p in DatabaseHelper.ObtenerPersonas().OrderBy(x => x.Nombre)) { string n = p.Nombramiento?.ToLower() ?? ""; if (n.Contains("anciano") || n.Contains("precursor")) { t.AddCell(new Cell().Add(new Paragraph(p.Nombre ?? "").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(p.CongregacionNombre ?? "").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(p.Telefono ?? "").SetFont(GetFont()).SetFontSize(9))); } } doc.Add(t); }
        private void GenerarPDF_Organizacion(Document doc) { doc.Add(new Paragraph("Ve a la pestaña Asambleas para el organigrama.").SetFont(GetFont(true)).SetFontSize(12).SetTextAlignment(TextAlignment.CENTER)); }
        private void GenerarPDF_SCS(Document doc) { if (DatosSCS.Count == 0) { doc.Add(new Paragraph("No hay SCS.")); return; } Table t = new Table(new float[] { 2, 2, 1, 2 }).UseAllAvailableWidth(); t.AddHeaderCell(new Cell().Add(new Paragraph("HERMANO").SetFont(GetFont(true)).SetFontSize(10))); t.AddHeaderCell(new Cell().Add(new Paragraph("CONGREGACIÓN").SetFont(GetFont(true)).SetFontSize(10))); t.AddHeaderCell(new Cell().Add(new Paragraph("TELÉFONO").SetFont(GetFont(true)).SetFontSize(10))); t.AddHeaderCell(new Cell().Add(new Paragraph("EMAIL").SetFont(GetFont(true)).SetFontSize(10))); foreach (var p in DatosSCS) { t.AddCell(new Cell().Add(new Paragraph(p.Nombre ?? "").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(p.Congregacion ?? "").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(p.Telefono ?? "").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(p.Email ?? "").SetFont(GetFont()).SetFontSize(9))); } doc.Add(t); }
        private void GenerarPDF_Potencial(Document doc) { if (DatosPotencial.Count == 0) { doc.Add(new Paragraph("No hay datos.")); return; } Table t = new Table(new float[] { 2, 4 }).UseAllAvailableWidth(); t.AddHeaderCell(new Cell().Add(new Paragraph("HERMANO").SetFont(GetFont(true)))); t.AddHeaderCell(new Cell().Add(new Paragraph("DESCRIPCIÓN").SetFont(GetFont(true)))); foreach (var d in DatosPotencial) { string nombre = ListaTodosAncianosSiervos.FirstOrDefault(x => x.Id == d.PersonaId)?.NombreMuestra ?? "Desconocido"; t.AddCell(new Cell().Add(new Paragraph(nombre).SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph(d.Descripcion ?? "").SetFont(GetFont()).SetFontSize(9))); } doc.Add(t); }
        private void GenerarPDF_Historico(Document doc) { doc.Add(new Paragraph("Histórico de visitas.").SetFont(GetFont()).SetFontSize(10).SetMarginBottom(10)); Table t = new Table(new float[] { 2, 1, 1 }).UseAllAvailableWidth(); t.AddHeaderCell(new Cell().Add(new Paragraph("CONGREGACIÓN").SetFont(GetFont(true)))); t.AddHeaderCell(new Cell().Add(new Paragraph("ÚLTIMA VISITA").SetFont(GetFont(true)))); t.AddHeaderCell(new Cell().Add(new Paragraph("PRÓXIMA VISITA").SetFont(GetFont(true)))); foreach (var c in DatabaseHelper.ObtenerListaCongregaciones().OrderBy(x => x)) { t.AddCell(new Cell().Add(new Paragraph(c).SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph("-").SetFont(GetFont()).SetFontSize(9))); t.AddCell(new Cell().Add(new Paragraph("-").SetFont(GetFont()).SetFontSize(9))); } doc.Add(t); }
        private void GenerarPDF_Problemas(Document doc) { if (DatosProblemas.Count == 0) { doc.Add(new Paragraph("No hay problemas.")); return; } foreach (var p in DatosProblemas) { doc.Add(new Paragraph("Congregación: " + p.Congregacion).SetFont(GetFont(true)).SetFontSize(12)); doc.Add(new Paragraph(p.Descripcion ?? "Sin descripción.").SetFont(GetFont()).SetFontSize(10).SetMarginBottom(15)); doc.Add(new Paragraph(new string('-', 50)).SetFontColor(ColorConstants.GRAY).SetMarginBottom(15)); } }
    }

    // =========================================================
    // CLASES DE DATOS VFS Y TABLAS
    // =========================================================
    public class SeccionVFS { public string Id { get; set; } = Guid.NewGuid().ToString(); public string NombreLimpio { get; set; } public int IdEspecial { get; set; } }
    public class CarpetaVFS { public string Id { get; set; } = Guid.NewGuid().ToString(); public string SeccionId { get; set; } public string ParentId { get; set; } public string Nombre { get; set; } }
    public class ArchivoVFS { public string Id { get; set; } = Guid.NewGuid().ToString(); public string SeccionId { get; set; } public string CarpetaId { get; set; } public string NombreOriginal { get; set; } public string NombreFisico { get; set; } }
    public class InfoNodo { public bool EsSeccionRaiz { get; set; } public bool EsCarpeta { get; set; } public bool EsArchivo { get; set; } public string Nombre { get; set; } public string SeccionId { get; set; } public CarpetaVFS CarpetaVFS { get; set; } public ArchivoVFS ArchivoVFS { get; set; } }
    public class HermanoSCS { public string Nombre { get; set; } public string Congregacion { get; set; } public string Telefono { get; set; } public string Email { get; set; } }
    public class HermanoDropdown { public int Id { get; set; } public string Nombre { get; set; } public string Congregacion { get; set; } public string NombreMuestra => $"{Nombre} ({Congregacion})"; }
    public class HermanoPotencial : INotifyPropertyChanged { public string Id { get; set; } = Guid.NewGuid().ToString(); private int _personaId; public int PersonaId { get => _personaId; set { _personaId = value; OnPropertyChanged("PersonaId"); } } private string _descripcion; public string Descripcion { get => _descripcion; set { _descripcion = value; OnPropertyChanged("Descripcion"); } } public event PropertyChangedEventHandler PropertyChanged; protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    public class ProblemaPendiente : INotifyPropertyChanged { public string Id { get; set; } = Guid.NewGuid().ToString(); private string _congregacion; public string Congregacion { get => _congregacion; set { _congregacion = value; OnPropertyChanged("Congregacion"); } } private string _descripcion; public string Descripcion { get => _descripcion; set { _descripcion = value; OnPropertyChanged("Descripcion"); } } public event PropertyChangedEventHandler PropertyChanged; protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
}