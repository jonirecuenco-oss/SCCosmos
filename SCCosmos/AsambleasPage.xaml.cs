using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;

// LIBRERÍAS PARA GENERAR PDF (iText7)
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout.Borders;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.IO.Font.Constants;

// LIBRERÍAS DE SELENIUM Y WORD
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace SCCosmos
{
    public partial class AsambleasPage : Page, INotifyPropertyChanged
    {
        private bool _isInitializing = true;
        private DispatcherTimer _autoSaveTimer;

        // ==========================================
        // VARIABLES GLOBALES (Listas)
        // ==========================================
        public ObservableCollection<string> ListaAños { get; set; }
        public ObservableCollection<AncianoComboItem> ListaAncianos { get; set; }
        public ObservableCollection<AncianoComboItem> ListaPrecursores { get; set; }
        public ObservableCollection<string> ListaCongregaciones { get; set; }
        public ObservableCollection<string> TiposParte { get; set; }

        // ==========================================
        // VARIABLES PESTAÑA: ASAMBLEAS Y ORGANIGRAMA
        // ==========================================
        public ObservableCollection<string> TiposAsamblea { get; set; }
        public ObservableCollection<SeccionCircuito> ListaSecciones { get; set; }
        public string AñoSeleccionado { get; set; }
        public string TipoSeleccionado { get; set; }
        public SeccionCircuito SeccionSeleccionada { get; set; }

        public string AñoOrganigramaSeleccionado { get; set; }
        public string TipoOrganigramaSeleccionado { get; set; }
        public SeccionCircuito SeccionOrganigramaSeleccionada { get; set; }

        private string _tituloAsamblea; public string TituloAsamblea { get => _tituloAsamblea; set { _tituloAsamblea = value; OnPropertyChanged(nameof(TituloAsamblea)); TriggerAutoSave(); } }
        private string _lugarAsamblea; public string LugarAsamblea { get => _lugarAsamblea; set { _lugarAsamblea = value; OnPropertyChanged(nameof(LugarAsamblea)); TriggerAutoSave(); } }
        private DateTime? _fechaAsamblea; public DateTime? FechaAsamblea { get => _fechaAsamblea; set { _fechaAsamblea = value; OnPropertyChanged(nameof(FechaAsamblea)); TriggerAutoSave(); } }
        private string _textoTematico; public string TextoTematico { get => _textoTematico; set { _textoTematico = value; OnPropertyChanged(nameof(TextoTematico)); TriggerAutoSave(); } }

        public ObservableCollection<PartePrograma> PartesManana { get; set; }
        public ObservableCollection<PartePrograma> PartesTarde { get; set; }

        public List<DepartamentoOrganigrama> ListaDepartamentosCajas { get; set; }

        public DepartamentoOrganigrama DeptSupAsamblea => ListaDepartamentosCajas.First(d => d.Nombre == "SUPERINTENDENTE DE ASAMBLEA");
        public DepartamentoOrganigrama DeptSupAuxAsamblea => ListaDepartamentosCajas.First(d => d.Nombre == "SUPERINTENDENTE AUXILIAR DE ASAMBLEA");
        public DepartamentoOrganigrama DeptAcomodadores => ListaDepartamentosCajas.First(d => d.Nombre == "ACOMODADORES");
        public DepartamentoOrganigrama DeptContabilidad => ListaDepartamentosCajas.First(d => d.Nombre == "CONTABILIDAD");
        public DepartamentoOrganigrama DeptEstacionamiento => ListaDepartamentosCajas.First(d => d.Nombre == "ESTACIONAMIENTO");
        public DepartamentoOrganigrama DeptLimpieza => ListaDepartamentosCajas.First(d => d.Nombre == "LIMPIEZA");
        public DepartamentoOrganigrama DeptPrimerosAuxilios => ListaDepartamentosCajas.First(d => d.Nombre == "PRIMEROS AUXILIOS");
        public DepartamentoOrganigrama DeptSeguridad => ListaDepartamentosCajas.First(d => d.Nombre == "COORDINADOR DE SEGURIDAD");
        public DepartamentoOrganigrama DeptAudioVideo => ListaDepartamentosCajas.First(d => d.Nombre == "AUDIO Y VÍDEO");
        public DepartamentoOrganigrama DeptAudio => ListaDepartamentosCajas.First(d => d.Nombre == "AUDIO");
        public DepartamentoOrganigrama DeptPlataforma => ListaDepartamentosCajas.First(d => d.Nombre == "PLATAFORMA");
        public DepartamentoOrganigrama DeptBautismo => ListaDepartamentosCajas.First(d => d.Nombre == "BAUTISMO");
        public DepartamentoOrganigrama DeptObjetosPerdidos => ListaDepartamentosCajas.First(d => d.Nombre == "OBJETOS PERDIDOS Y GUARDARROPA");
        public DepartamentoOrganigrama DeptTransporte => ListaDepartamentosCajas.First(d => d.Nombre == "TRANSPORTE Y MATERIALES");
        public DepartamentoOrganigrama DeptInstalacion => ListaDepartamentosCajas.First(d => d.Nombre == "INSTALACIÓN");

        // ==========================================
        // VARIABLES PESTAÑA: PRECURSORES
        // ==========================================
        public string AñoPrecursoresSeleccionado { get; set; }
        private string _tituloPrecursores; public string TituloPrecursores { get => _tituloPrecursores; set { _tituloPrecursores = value; OnPropertyChanged(nameof(TituloPrecursores)); TriggerAutoSave(); } }
        private string _textoTematicoPrecursores; public string TextoTematicoPrecursores { get => _textoTematicoPrecursores; set { _textoTematicoPrecursores = value; OnPropertyChanged(nameof(TextoTematicoPrecursores)); TriggerAutoSave(); } }

        public ObservableCollection<SesionPrecursores> SesionesPrecursores { get; set; }
        private SesionPrecursores _sesionPrecursoresActiva; public SesionPrecursores SesionPrecursoresActiva { get => _sesionPrecursoresActiva; set { _sesionPrecursoresActiva = value; OnPropertyChanged(nameof(SesionPrecursoresActiva)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AsambleasPage()
        {
            InitializeComponent();
            this.DataContext = this;

            DatabaseHelper.AsegurarTablasAsambleas();

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(1);
            _autoSaveTimer.Tick += (s, e) => { _autoSaveTimer.Stop(); GuardarTodoSilencioso(); };

            int añoActual = DateTime.Now.Year;
            ListaAños = new ObservableCollection<string> { (añoActual + 1).ToString(), añoActual.ToString(), (añoActual - 1).ToString(), (añoActual - 2).ToString(), (añoActual - 3).ToString() };
            AñoSeleccionado = ListaAños[1];
            AñoOrganigramaSeleccionado = ListaAños[1];
            AñoPrecursoresSeleccionado = ListaAños[1];

            TiposAsamblea = new ObservableCollection<string> { "AC (con sup. de circuito)", "AC (con rep. de Betel)" };
            TipoSeleccionado = TiposAsamblea[0];
            TipoOrganigramaSeleccionado = TiposAsamblea[0];

            TiposParte = new ObservableCollection<string> { "Oración inicial", "Oración intermedio", "Oración final", "Música", "Canción", "Presidencia", "Discurso", "Discurso (serie)", "Entrevistas", "Resumen de La Atalaya" };

            PartesManana = new ObservableCollection<PartePrograma>();
            PartesTarde = new ObservableCollection<PartePrograma>();
            SesionesPrecursores = new ObservableCollection<SesionPrecursores>();

            InicializarDepartamentos();
            CargarConfiguracionBase();

            _isInitializing = false;
            CargarProgramaAsambleaDesdeBD();
            CargarOrganigramaDesdeBD();
            CargarProgramaPrecursoresDesdeBD();
        }

        private void InicializarDepartamentos()
        {
            ListaDepartamentosCajas = new List<DepartamentoOrganigrama> {
                new DepartamentoOrganigrama { Nombre = "SUPERINTENDENTE DE ASAMBLEA", ColorHeader = "#B3D7E8" },
                new DepartamentoOrganigrama { Nombre = "SUPERINTENDENTE AUXILIAR DE ASAMBLEA", ColorHeader = "#F4C7A3" },
                new DepartamentoOrganigrama { Nombre = "ACOMODADORES", ColorHeader = "#DEEAF6" },
                new DepartamentoOrganigrama { Nombre = "CONTABILIDAD", ColorHeader = "#DEEAF6" },
                new DepartamentoOrganigrama { Nombre = "ESTACIONAMIENTO", ColorHeader = "#DEEAF6" },
                new DepartamentoOrganigrama { Nombre = "LIMPIEZA", ColorHeader = "#DEEAF6" },
                new DepartamentoOrganigrama { Nombre = "PRIMEROS AUXILIOS", ColorHeader = "#DEEAF6" },
                new DepartamentoOrganigrama { Nombre = "COORDINADOR DE SEGURIDAD", ColorHeader = "#D99694" },
                new DepartamentoOrganigrama { Nombre = "AUDIO Y VÍDEO", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "AUDIO", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "PLATAFORMA", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "BAUTISMO", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "OBJETOS PERDIDOS Y GUARDARROPA", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "TRANSPORTE Y MATERIALES", ColorHeader = "#FCE4D6" },
                new DepartamentoOrganigrama { Nombre = "INSTALACIÓN", ColorHeader = "#FCE4D6" }
            };

            foreach (var d in ListaDepartamentosCajas)
            {
                d.Superintendente = new AsignacionHermano { Parent = d };
                d.Superintendente.PropertyChanged += Objeto_PropertyChanged;

                d.Auditor = new AsignacionHermano { Parent = d };
                d.Auditor.PropertyChanged += Objeto_PropertyChanged;

                d.Auxiliares = new ObservableCollection<AsignacionHermano>();
                d.Auxiliares.CollectionChanged += (s, e) => { OnPropertyChanged(nameof(d.MostrarSeparadorAuxiliares)); TriggerAutoSave(); };
            }
        }

        private void CargarConfiguracionBase()
        {
            var seccionesBD = DatabaseHelper.ObtenerSecciones();
            var listaSec = new List<SeccionCircuito>();
            if (seccionesBD != null) listaSec.AddRange(seccionesBD);
            ListaSecciones = new ObservableCollection<SeccionCircuito>(listaSec);
            SeccionSeleccionada = null;
            SeccionOrganigramaSeleccionada = null;

            var todasLasPersonas = DatabaseHelper.ObtenerPersonas();

            var ancianosList = new List<AncianoComboItem>();
            var precursoresList = new List<AncianoComboItem>();

            foreach (var p in todasLasPersonas)
            {
                int id = p.Id;
                string nombreJW = p.NombreJWHub ?? "";
                string nombre = p.Nombre ?? "";
                string nombramiento = p.Nombramiento?.ToLower() ?? "";
                string servicio = p.Servicio?.ToLower() ?? "";

                string congre = p.CongregacionNombre ?? "";
                if (congre == "Sin asignar") congre = "";

                bool esAnciano = nombramiento.Contains("anciano");
                bool esPrecursor = nombramiento.Contains("precursor regular") || nombramiento.Contains("precursor especial") || nombramiento.Contains("misionero") ||
                                   servicio.Contains("precursor regular") || servicio.Contains("precursor especial") || servicio.Contains("misionero");

                if (esAnciano || esPrecursor)
                {
                    var item = new AncianoComboItem
                    {
                        Id = id,
                        NombreJWHub = !string.IsNullOrWhiteSpace(nombreJW) ? nombreJW : nombre,
                        Congregacion = congre
                    };

                    if (esAnciano && !ancianosList.Any(a => a.Id == id)) ancianosList.Add(item);
                    if (esAnciano && esPrecursor && !precursoresList.Any(a => a.Id == id)) precursoresList.Add(item);
                }
            }

            ancianosList = ancianosList.OrderBy(a => a.NombreFormateado).ToList();
            ancianosList.Insert(0, new AncianoComboItem { Id = -2, NombreJWHub = "Representante de Betel", Congregacion = "" });
            ancianosList.Insert(0, new AncianoComboItem { Id = -1, NombreJWHub = "Jonatán Recuenco", Congregacion = "" });
            ancianosList.Insert(0, new AncianoComboItem { Id = 0, NombreJWHub = "", Congregacion = "" });
            ListaAncianos = new ObservableCollection<AncianoComboItem>(ancianosList);

            precursoresList = precursoresList.OrderBy(a => a.NombreFormateado).ToList();
            precursoresList.Insert(0, new AncianoComboItem { Id = -1, NombreJWHub = "Jonatán Recuenco", Congregacion = "" });
            precursoresList.Insert(0, new AncianoComboItem { Id = 0, NombreJWHub = "", Congregacion = "" });
            ListaPrecursores = new ObservableCollection<AncianoComboItem>(precursoresList);

            var listaCongs = DatabaseHelper.ObtenerListaCongregaciones();
            listaCongs.Insert(0, "Todas");
            ListaCongregaciones = new ObservableCollection<string>(listaCongs);
        }

        private void CmbAsignado_Loaded(object sender, RoutedEventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb == null || cmb.ItemsSource != null) return;
            if (cmb.Tag?.ToString() == "Ancianos") cmb.ItemsSource = new System.Windows.Data.ListCollectionView(ListaAncianos);
            else if (cmb.Tag?.ToString() == "Precursores") cmb.ItemsSource = new System.Windows.Data.ListCollectionView(ListaPrecursores);
        }

        private void CmbAsignado_TextChanged(object sender, TextChangedEventArgs e)
        {
            var cmb = sender as ComboBox;
            var tb = e.OriginalSource as TextBox;
            if (cmb == null || tb == null || !tb.IsKeyboardFocusWithin) return;

            var cv = cmb.ItemsSource as System.Windows.Data.ListCollectionView;
            if (cv != null)
            {
                string txt = tb.Text.ToLower();
                cv.Filter = item =>
                {
                    if (string.IsNullOrWhiteSpace(txt)) return true;
                    var persona = item as AncianoComboItem;
                    return persona != null && persona.NombreMuestra.ToLower().Contains(txt);
                };
                cmb.IsDropDownOpen = true;
            }
        }

        private void FiltrosAsamblea_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_isInitializing) CargarProgramaAsambleaDesdeBD(); }
        private void FiltrosOrganigrama_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_isInitializing) CargarOrganigramaDesdeBD(); }
        private void FiltrosPrecursores_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_isInitializing) CargarProgramaPrecursoresDesdeBD(); }

        private void CargarProgramaAsambleaDesdeBD()
        {
            if (AñoSeleccionado == null || TipoSeleccionado == null || SeccionSeleccionada == null) return;
            _isInitializing = true;

            foreach (var p in PartesManana) p.PropertyChanged -= Objeto_PropertyChanged;
            foreach (var p in PartesTarde) p.PropertyChanged -= Objeto_PropertyChanged;
            PartesManana.Clear(); PartesTarde.Clear();

            long asambleaId = DatabaseHelper.ObtenerIdYDatosAsamblea(AñoSeleccionado, TipoSeleccionado, SeccionSeleccionada.Nombre, out string tituloTemp, out string textoTemp, out string lugarTemp, out DateTime? fechaTemp);

            _tituloAsamblea = tituloTemp; OnPropertyChanged(nameof(TituloAsamblea));
            _textoTematico = textoTemp; OnPropertyChanged(nameof(TextoTematico));
            _lugarAsamblea = lugarTemp; OnPropertyChanged(nameof(LugarAsamblea));
            _fechaAsamblea = fechaTemp; OnPropertyChanged(nameof(FechaAsamblea));

            if (asambleaId > 0)
            {
                var partesBD = DatabaseHelper.ObtenerPartesAsamblea(asambleaId);
                foreach (var parte in partesBD)
                {
                    parte.PropertyChanged += Objeto_PropertyChanged;
                    if (parte.Turno == "Mañana") PartesManana.Add(parte);
                    else PartesTarde.Add(parte);
                }
            }
            _isInitializing = false;
        }

        private void CargarOrganigramaDesdeBD()
        {
            if (AñoOrganigramaSeleccionado == null || TipoOrganigramaSeleccionado == null || SeccionOrganigramaSeleccionada == null) return;
            _isInitializing = true;

            foreach (var d in ListaDepartamentosCajas)
            {
                d.Superintendente.AsignadoId = 0;
                d.Auditor.AsignadoId = 0;
                foreach (var aux in d.Auxiliares) aux.PropertyChanged -= Objeto_PropertyChanged;
                d.Auxiliares.Clear();
            }

            long asambleaId = DatabaseHelper.ObtenerIdYDatosAsamblea(AñoOrganigramaSeleccionado, TipoOrganigramaSeleccionado, SeccionOrganigramaSeleccionada.Nombre, out _, out _, out _, out _);
            if (asambleaId > 0)
            {
                var asignacionesBD = DatabaseHelper.ObtenerOrganigrama(asambleaId);
                foreach (var a in asignacionesBD)
                {
                    var dept = ListaDepartamentosCajas.FirstOrDefault(d => d.Nombre == a.Departamento);
                    if (dept != null)
                    {
                        if (a.Rol == "Superintendente") dept.Superintendente.AsignadoId = a.AsignadoId;
                        else if (a.Rol == "Auditor") dept.Auditor.AsignadoId = a.AsignadoId;
                        else if (a.Rol == "Auxiliar")
                        {
                            var aux = new AsignacionHermano { AsignadoId = a.AsignadoId, Parent = dept };
                            aux.PropertyChanged += Objeto_PropertyChanged;
                            dept.Auxiliares.Add(aux);
                        }
                    }
                }
            }
            _isInitializing = false;
        }

        private void CargarProgramaPrecursoresDesdeBD()
        {
            if (AñoPrecursoresSeleccionado == null) return;
            _isInitializing = true;

            foreach (var s in SesionesPrecursores)
            {
                s.PropertyChanged -= Objeto_PropertyChanged;
                foreach (var p in s.Partes) p.PropertyChanged -= Objeto_PropertyChanged;
            }
            SesionesPrecursores.Clear();
            SesionPrecursoresActiva = null;

            long reunionId = DatabaseHelper.ObtenerDatosReunionPrecursores(AñoPrecursoresSeleccionado, out string tituloTemp, out string textoTemp);
            _tituloPrecursores = tituloTemp; OnPropertyChanged(nameof(TituloPrecursores));
            _textoTematicoPrecursores = textoTemp; OnPropertyChanged(nameof(TextoTematicoPrecursores));

            if (reunionId > 0)
            {
                var sesionesBD = DatabaseHelper.ObtenerSesionesPrecursores(reunionId);
                int contador = 1;
                foreach (var sesion in sesionesBD)
                {
                    sesion.Numero = contador++;
                    sesion.PropertyChanged += Objeto_PropertyChanged;
                    foreach (var parte in sesion.Partes) parte.PropertyChanged += Objeto_PropertyChanged;
                    SesionesPrecursores.Add(sesion);
                }
                if (SesionesPrecursores.Count > 0) SesionPrecursoresActiva = SesionesPrecursores[0];
            }
            _isInitializing = false;
        }

        private void TriggerAutoSave() { if (_isInitializing) return; _autoSaveTimer.Stop(); _autoSaveTimer.Start(); }
        private void Objeto_PropertyChanged(object sender, PropertyChangedEventArgs e) { if (e.PropertyName != "VisibilidadEnviar" && e.PropertyName != "ColorBotonArchivo" && e.PropertyName != "ToolTipArchivo" && e.PropertyName != "VisibilidadCruz" && e.PropertyName != "TituloSesion" && e.PropertyName != "Lugar") TriggerAutoSave(); }

        private async void GuardarTodoSilencioso()
        {
            bool asambleaGuardada = false;
            bool organigramaGuardado = false;
            bool precursoresGuardado = false;

            try
            {
                DatabaseHelper.AsegurarTablasAsambleas();

                if (AñoSeleccionado != null && TipoSeleccionado != null && SeccionSeleccionada != null)
                {
                    var todasLasPartes = PartesManana.Concat(PartesTarde).ToList();
                    DatabaseHelper.GuardarProgramaAsamblea(AñoSeleccionado, TipoSeleccionado, SeccionSeleccionada.Nombre, TituloAsamblea, TextoTematico, LugarAsamblea, FechaAsamblea, todasLasPartes, null);
                    asambleaGuardada = true;
                }

                if (AñoOrganigramaSeleccionado != null && TipoOrganigramaSeleccionado != null && SeccionOrganigramaSeleccionada != null)
                {
                    var listaGuardar = new List<DatabaseHelper.AsignacionOrganigramaBD>();
                    foreach (var d in ListaDepartamentosCajas)
                    {
                        if (d.Superintendente.AsignadoId > 0) listaGuardar.Add(new DatabaseHelper.AsignacionOrganigramaBD { Departamento = d.Nombre, AsignadoId = d.Superintendente.AsignadoId, Rol = "Superintendente" });
                        if (d.Auditor != null && d.Auditor.AsignadoId > 0) listaGuardar.Add(new DatabaseHelper.AsignacionOrganigramaBD { Departamento = d.Nombre, AsignadoId = d.Auditor.AsignadoId, Rol = "Auditor" });
                        foreach (var aux in d.Auxiliares) if (aux.AsignadoId > 0) listaGuardar.Add(new DatabaseHelper.AsignacionOrganigramaBD { Departamento = d.Nombre, AsignadoId = aux.AsignadoId, Rol = "Auxiliar" });
                    }

                    DatabaseHelper.ObtenerIdYDatosAsamblea(AñoOrganigramaSeleccionado, TipoOrganigramaSeleccionado, SeccionOrganigramaSeleccionada.Nombre, out string titTemp, out string txtTemp, out string lugTemp, out DateTime? fecTemp);
                    DatabaseHelper.GuardarProgramaAsamblea(AñoOrganigramaSeleccionado, TipoOrganigramaSeleccionado, SeccionOrganigramaSeleccionada.Nombre, titTemp, txtTemp, lugTemp, fecTemp, null, listaGuardar);
                    organigramaGuardado = true;
                }

                if (AñoPrecursoresSeleccionado != null)
                {
                    DatabaseHelper.GuardarReunionPrecursores(AñoPrecursoresSeleccionado, TituloPrecursores, TextoTematicoPrecursores, SesionesPrecursores.ToList());
                    precursoresGuardado = true;
                }

                if (asambleaGuardada) { TxtGuardadoAsamblea.Text = "✓ Guardado"; TxtGuardadoAsamblea.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#81A1C1")); TxtGuardadoAsamblea.Visibility = Visibility.Visible; }
                if (organigramaGuardado) { TxtGuardadoOrganigrama.Text = "✓ Guardado"; TxtGuardadoOrganigrama.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#81A1C1")); TxtGuardadoOrganigrama.Visibility = Visibility.Visible; }
                if (precursoresGuardado) { TxtGuardadoPrecursores.Text = "✓ Guardado"; TxtGuardadoPrecursores.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#81A1C1")); TxtGuardadoPrecursores.Visibility = Visibility.Visible; }

                await Task.Delay(2000);
                TxtGuardadoAsamblea.Visibility = Visibility.Collapsed;
                TxtGuardadoOrganigrama.Visibility = Visibility.Collapsed;
                TxtGuardadoPrecursores.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { MessageBox.Show($"Error al guardar:\n\n{ex.Message}", "Error de Guardado", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ==========================================
        // GENERADORES DE PDF (PROGRAMA)
        // ==========================================

        private void BtnGenerarPDFAsamblea_Click(object sender, RoutedEventArgs e)
        {
            if (SeccionSeleccionada == null) return;
            SaveFileDialog sfd = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Programa_Asamblea_{SeccionSeleccionada.Nombre}_{AñoSeleccionado}.pdf" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (PdfWriter writer = new PdfWriter(sfd.FileName))
                    using (PdfDocument pdf = new PdfDocument(writer))
                    using (Document document = new Document(pdf))
                    {
                        document.SetLeftMargin(20); document.SetRightMargin(20);
                        PdfFont fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                        PdfFont fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);
                        PdfFont fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                        string titulo = string.IsNullOrWhiteSpace(TituloAsamblea) ? "Asamblea de Circuito" : TituloAsamblea.ToUpper();
                        document.Add(new Paragraph(new Text(titulo).SetFont(fontBold)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(16).SetMarginBottom(2));
                        if (!string.IsNullOrWhiteSpace(TextoTematico)) document.Add(new Paragraph(new Text(TextoTematico).SetFont(fontItalic)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(12).SetMarginBottom(15));
                        else document.Add(new Paragraph("").SetMarginBottom(15));

                        Table tablaMaestra = new Table(new float[] { 1, 3, 2, 3 }).UseAllAvailableWidth();
                        void AñadirFilas(string turno, ObservableCollection<PartePrograma> partes)
                        {
                            if (partes.Count == 0) return;
                            tablaMaestra.AddCell(new Cell(1, 4).Add(new Paragraph(new Text(turno).SetFont(fontBold)).SetFontSize(11).SetFontColor(ColorConstants.DARK_GRAY)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(10).SetPaddingBottom(5).SetPaddingLeft(0));
                            foreach (var p in partes)
                            {
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Hora ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Titulo ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));

                                var asignado = ListaAncianos.FirstOrDefault(a => a.Id == p.AsignadoId) ?? ListaPrecursores.FirstOrDefault(a => a.Id == p.AsignadoId);
                                string nombreOrador = asignado?.NombreMuestra ?? "";

                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(nombreOrador).SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Participantes ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                            }
                        }
                        AñadirFilas("MAÑANA", PartesManana);
                        AñadirFilas("TARDE", PartesTarde);
                        document.Add(tablaMaestra);
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            }
        }

        // ==========================================
        // DIBUJADO DE LAS LÍNEAS DEL ÁRBOL EN PDF
        // ==========================================
        private class CustomTreeLineRenderer : iText.Layout.Renderer.CellRenderer
        {
            private bool _isLast;
            public CustomTreeLineRenderer(iText.Layout.Element.Cell modelElement, bool isLast) : base(modelElement) { _isLast = isLast; }
            public override void Draw(iText.Layout.Renderer.DrawContext drawContext)
            {
                base.Draw(drawContext);
                var canvas = drawContext.GetCanvas();
                var rect = GetOccupiedAreaBBox();
                canvas.SaveState();
                canvas.SetStrokeColor(iText.Kernel.Colors.ColorConstants.DARK_GRAY);
                canvas.SetLineWidth(0.8f);

                float lineX = rect.GetLeft() + 7.5f;
                float branchY = rect.GetTop() - 12f;

                canvas.MoveTo(lineX, rect.GetTop());
                canvas.LineTo(lineX, _isLast ? branchY : rect.GetBottom());

                canvas.MoveTo(lineX, branchY);
                canvas.LineTo(rect.GetRight(), branchY);

                canvas.Stroke();
                canvas.RestoreState();
            }
            public override iText.Layout.Renderer.IRenderer GetNextRenderer() { return new CustomTreeLineRenderer((iText.Layout.Element.Cell)GetModelElement(), _isLast); }
        }

        public class OrgNode
        {
            public DepartamentoOrganigrama Dept { get; set; }
            public Color BgColor { get; set; }
            public List<OrgNode> Children { get; set; } = new List<OrgNode>();
        }

        private void BtnGenerarPDFOrganigrama_Click(object sender, RoutedEventArgs e)
        {
            if (SeccionOrganigramaSeleccionada == null) return;

            DatabaseHelper.ObtenerIdYDatosAsamblea(AñoOrganigramaSeleccionado, TipoOrganigramaSeleccionado, SeccionOrganigramaSeleccionada.Nombre, out string tituloAsamblea, out string textoTematico, out string lugarAsamblea, out DateTime? fechaAsamblea);

            SaveFileDialog sfd = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Organigrama_{SeccionOrganigramaSeleccionada.Nombre}_{AñoOrganigramaSeleccionado}.pdf" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (PdfWriter writer = new PdfWriter(sfd.FileName))
                    using (PdfDocument pdf = new PdfDocument(writer))
                    using (Document document = new Document(pdf))
                    {
                        document.SetMargins(15, 15, 15, 15);

                        PdfFont fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                        PdfFont fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                        string titulo = string.IsNullOrWhiteSpace(tituloAsamblea) ? "ASAMBLEA DE CIRCUITO" : tituloAsamblea.ToUpper();
                        document.Add(new Paragraph(new Text(titulo).SetFont(fontNormal)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(12).SetMarginBottom(2));

                        string texto = string.IsNullOrWhiteSpace(textoTematico) ? "" : $"\"{textoTematico}\"";
                        document.Add(new Paragraph(new Text(texto).SetFont(fontNormal)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(16).SetFontColor(new DeviceRgb(50, 80, 120)).SetMarginBottom(8));

                        Table tHeader = new Table(new float[] { 1, 1 }).UseAllAvailableWidth().SetMarginBottom(12);
                        string fechaStr = fechaAsamblea.HasValue ? fechaAsamblea.Value.ToString("dd DE MMMM DE yyyy").ToUpper() : "";
                        tHeader.AddCell(new Cell().Add(new Paragraph(fechaStr).SetFont(fontNormal).SetFontSize(8).SetFontColor(new DeviceRgb(50, 80, 120))).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetBorderTop(new iText.Layout.Borders.SolidBorder(new DeviceRgb(150, 180, 210), 1)).SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(150, 180, 210), 1)).SetPadding(3));
                        tHeader.AddCell(new Cell().Add(new Paragraph(lugarAsamblea ?? "").SetFont(fontNormal).SetFontSize(8).SetFontColor(new DeviceRgb(50, 80, 120)).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetBorderTop(new iText.Layout.Borders.SolidBorder(new DeviceRgb(150, 180, 210), 1)).SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(150, 180, 210), 1)).SetPadding(3));
                        document.Add(tHeader);

                        Color cBlue = new DeviceRgb(179, 215, 232);
                        Color cOrange = new DeviceRgb(244, 199, 163);
                        Color cLightBlue = new DeviceRgb(222, 234, 246);
                        Color cLightOrange = new DeviceRgb(252, 228, 214);
                        Color cAuditor = new DeviceRgb(146, 205, 220);
                        Color cRed = new DeviceRgb(217, 150, 148);

                        bool TieneGente(DepartamentoOrganigrama d)
                        {
                            if (d == null) return false;
                            return d.Superintendente.AsignadoId > 0 || (d.Auditor != null && d.Auditor.AsignadoId > 0) || d.Auxiliares.Any(a => a.AsignadoId > 0);
                        }

                        Table CrearCaja(DepartamentoOrganigrama dept, Color bgColor)
                        {
                            Table t = new Table(1).UseAllAvailableWidth();
                            Cell header = new Cell().Add(new Paragraph(dept.Nombre).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFont(fontBold).SetFontSize(8)).SetBackgroundColor(bgColor).SetBorder(new iText.Layout.Borders.SolidBorder(ColorConstants.DARK_GRAY, 0.5f)).SetPadding(2);
                            t.AddCell(header);

                            void AddHermano(int id)
                            {
                                if (id <= 0) return;
                                var info = DatabaseHelper.ObtenerInfoHermanoParaOrganigrama(id);
                                Table inTable = new Table(new float[] { 1, 1 }).UseAllAvailableWidth();

                                string nombreStr = $"{info.NombreFormateado} " + (!string.IsNullOrWhiteSpace(info.AnioNacimiento) ? $"({info.AnioNacimiento})" : "");
                                inTable.AddCell(new Cell().Add(new Paragraph(nombreStr).SetFont(fontBold).SetFontSize(7)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(1));
                                inTable.AddCell(new Cell().Add(new Paragraph(info.Congregacion).SetFont(fontNormal).SetFontSize(6).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(1));

                                inTable.AddCell(new Cell().Add(new Paragraph(info.Telefono).SetFont(fontNormal).SetFontSize(6)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(1));
                                inTable.AddCell(new Cell().Add(new Paragraph(info.Email).SetFont(fontNormal).SetFontSize(6).SetFontColor(ColorConstants.BLUE).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(1));

                                t.AddCell(new Cell().Add(inTable).SetPadding(1).SetBorder(new iText.Layout.Borders.SolidBorder(ColorConstants.GRAY, 0.5f)));
                            }

                            AddHermano(dept.Superintendente.AsignadoId);

                            if (dept.Nombre == "CONTABILIDAD" && dept.Auditor != null && dept.Auditor.AsignadoId > 0)
                            {
                                Cell audHeader = new Cell().Add(new Paragraph("Auditor").SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFont(fontBold).SetFontSize(8)).SetBackgroundColor(cAuditor).SetBorder(new iText.Layout.Borders.SolidBorder(ColorConstants.DARK_GRAY, 0.5f)).SetPadding(2);
                                t.AddCell(audHeader);
                                AddHermano(dept.Auditor.AsignadoId);
                            }

                            foreach (var aux in dept.Auxiliares) AddHermano(aux.AsignadoId);

                            return t;
                        }

                        Table ConstruirArbol(OrgNode node)
                        {
                            Table t = new Table(1).UseAllAvailableWidth();

                            if (node.Dept != null && TieneGente(node.Dept))
                            {
                                t.AddCell(new Cell().Add(CrearCaja(node.Dept, node.BgColor)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0));
                            }
                            else
                            {
                                t.AddCell(new Cell().SetHeight(0).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0));
                            }

                            if (node.Children.Count > 0)
                            {
                                Table childrenTable = new Table(new UnitValue[] { UnitValue.CreatePointValue(15f), UnitValue.CreatePercentValue(100f) });
                                childrenTable.SetWidth(UnitValue.CreatePercentValue(100f));

                                for (int i = 0; i < node.Children.Count; i++)
                                {
                                    var child = node.Children[i];
                                    bool isLast = (i == node.Children.Count - 1);

                                    Cell treeCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                                    treeCell.SetNextRenderer(new CustomTreeLineRenderer(treeCell, isLast));
                                    childrenTable.AddCell(treeCell);

                                    Cell childContent = new Cell().Add(ConstruirArbol(child)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingBottom(6);
                                    childrenTable.AddCell(childContent);
                                }
                                t.AddCell(new Cell().Add(childrenTable).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0));
                            }
                            return t;
                        }

                        var leftRoot = new OrgNode { Dept = DeptSupAsamblea, BgColor = cBlue };
                        if (TieneGente(DeptAcomodadores)) leftRoot.Children.Add(new OrgNode { Dept = DeptAcomodadores, BgColor = cLightBlue });
                        if (TieneGente(DeptContabilidad)) leftRoot.Children.Add(new OrgNode { Dept = DeptContabilidad, BgColor = cLightBlue });
                        if (TieneGente(DeptEstacionamiento)) leftRoot.Children.Add(new OrgNode { Dept = DeptEstacionamiento, BgColor = cLightBlue });
                        if (TieneGente(DeptLimpieza)) leftRoot.Children.Add(new OrgNode { Dept = DeptLimpieza, BgColor = cLightBlue });
                        if (TieneGente(DeptPrimerosAuxilios)) leftRoot.Children.Add(new OrgNode { Dept = DeptPrimerosAuxilios, BgColor = cLightBlue });
                        if (TieneGente(DeptSeguridad)) leftRoot.Children.Add(new OrgNode { Dept = DeptSeguridad, BgColor = cRed });

                        var rightRoot = new OrgNode { Dept = DeptSupAuxAsamblea, BgColor = cOrange };

                        var nodeAudioVideo = new OrgNode { Dept = DeptAudioVideo, BgColor = cLightOrange };
                        var nodeAudio = new OrgNode { Dept = DeptAudio, BgColor = cLightOrange };
                        var nodePlataforma = new OrgNode { Dept = DeptPlataforma, BgColor = cLightOrange };

                        if (TieneGente(nodePlataforma.Dept)) nodeAudio.Children.Add(nodePlataforma);
                        if (TieneGente(nodeAudio.Dept) || nodeAudio.Children.Count > 0) nodeAudioVideo.Children.Add(nodeAudio);
                        if (TieneGente(nodeAudioVideo.Dept) || nodeAudioVideo.Children.Count > 0) rightRoot.Children.Add(nodeAudioVideo);

                        if (TieneGente(DeptBautismo)) rightRoot.Children.Add(new OrgNode { Dept = DeptBautismo, BgColor = cLightOrange });
                        if (TieneGente(DeptObjetosPerdidos)) rightRoot.Children.Add(new OrgNode { Dept = DeptObjetosPerdidos, BgColor = cLightOrange });
                        if (TieneGente(DeptTransporte)) rightRoot.Children.Add(new OrgNode { Dept = DeptTransporte, BgColor = cLightOrange });
                        if (TieneGente(DeptInstalacion)) rightRoot.Children.Add(new OrgNode { Dept = DeptInstalacion, BgColor = cLightOrange });

                        Table tMain = new Table(new UnitValue[] { UnitValue.CreatePercentValue(50f), UnitValue.CreatePercentValue(50f) });
                        tMain.SetWidth(UnitValue.CreatePercentValue(100f));

                        tMain.AddCell(new Cell().Add(ConstruirArbol(leftRoot)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingRight(10));
                        tMain.AddCell(new Cell().Add(ConstruirArbol(rightRoot)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingLeft(10));

                        document.Add(tMain);
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            }
        }

        private void BtnGenerarPDFPrecursores_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Reunion_Precursores_{AñoPrecursoresSeleccionado}.pdf" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (PdfWriter writer = new PdfWriter(sfd.FileName))
                    using (PdfDocument pdf = new PdfDocument(writer))
                    using (Document document = new Document(pdf))
                    {
                        document.SetLeftMargin(20); document.SetRightMargin(20);
                        PdfFont fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                        PdfFont fontItalic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);
                        PdfFont fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

                        string titulo = string.IsNullOrWhiteSpace(TituloPrecursores) ? "Reunión Especial con los Precursores" : TituloPrecursores.ToUpper();
                        document.Add(new Paragraph(new Text(titulo).SetFont(fontBold)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(16).SetMarginBottom(2));
                        if (!string.IsNullOrWhiteSpace(TextoTematicoPrecursores)) document.Add(new Paragraph(new Text(TextoTematicoPrecursores).SetFont(fontItalic)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(12).SetMarginBottom(15));
                        else document.Add(new Paragraph("").SetMarginBottom(15));

                        foreach (var sesion in SesionesPrecursores)
                        {
                            document.Add(new Paragraph(new Text(sesion.TituloSesion.ToUpper()).SetFont(fontBold)).SetFontSize(11).SetMarginTop(10).SetMarginBottom(2).SetFontColor(ColorConstants.DARK_GRAY));
                            string fechaStr = sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("dd/MM/yyyy") : "Sin fecha";
                            document.Add(new Paragraph($"Fecha: {fechaStr}   -   Hora: {sesion.Hora}").SetFont(fontNormal).SetFontSize(10));
                            document.Add(new Paragraph($"Lugar: {sesion.Lugar}").SetFont(fontNormal).SetFontSize(10));
                            document.Add(new Paragraph($"Congregaciones: {sesion.Congregaciones}").SetFont(fontItalic).SetFontSize(10).SetMarginBottom(10));

                            if (sesion.Partes.Count > 0)
                            {
                                Table tabla = new Table(new float[] { 1, 3, 2, 3 }).UseAllAvailableWidth().SetMarginBottom(15);
                                foreach (var p in sesion.Partes)
                                {
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Hora ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Titulo ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));

                                    var asignado = ListaPrecursores.FirstOrDefault(a => a.Id == p.AsignadoId) ?? ListaAncianos.FirstOrDefault(a => a.Id == p.AsignadoId);
                                    string nombreOrador = asignado?.NombreMuestra ?? "";

                                    tabla.AddCell(new Cell().Add(new Paragraph(nombreOrador).SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Participantes ?? "").SetFont(fontNormal).SetFontSize(9)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                }
                                document.Add(tabla);
                            }
                        }
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            }
        }

        // ==========================================
        // BOTONES DE FILA 
        // ==========================================

        private void BtnLimpiarAsignado_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is PartePrograma parte) parte.AsignadoId = 0; }
        private void BtnLimpiarAsignacionOrganigrama_Click(object sender, RoutedEventArgs e) { if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is AsignacionHermano asig) asig.AsignadoId = 0; }

        private void BtnEliminarAuxiliar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is AsignacionHermano aux)
            {
                aux.PropertyChanged -= Objeto_PropertyChanged;
                aux.Parent.Auxiliares.Remove(aux);
                TriggerAutoSave();
            }
        }

        private void BtnAdjuntar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is PartePrograma parte)
            {
                if (!string.IsNullOrEmpty(parte.RutaArchivo)) { if (MessageBox.Show("Ya hay archivos adjuntos. ¿Deseas borrarlos?\nSi quieres cambiarlos, dale a 'No' y selecciona otros.", "Archivos", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { parte.RutaArchivo = ""; return; } }
                OpenFileDialog ofd = new OpenFileDialog { Title = "Seleccionar archivo(s)", Multiselect = true };
                if (ofd.ShowDialog() == true) parte.RutaArchivo = string.Join("|", ofd.FileNames);
            }
        }

        // ==========================================
        // ENVÍO DE ASIGNACIÓN (CORREO AUTOMÁTICO)
        // ==========================================
        private async void BtnEnviarAsignacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Wpf.Ui.Controls.Button ?? sender as Button;
            var parte = boton?.Tag as PartePrograma;
            if (parte == null) return;

            if (parte.Turno != "Precursores")
            {
                var asignadoAsamblea = ListaAncianos.FirstOrDefault(a => a.Id == parte.AsignadoId) ?? ListaPrecursores.FirstOrDefault(a => a.Id == parte.AsignadoId);
                string nombreAsamblea = asignadoAsamblea?.NombreMuestra ?? "Desconocido";
                MessageBox.Show($"Módulo de envíos en desarrollo.\nSe enviará a: {nombreAsamblea}", "Enviar Asignación", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sesion = SesionesPrecursores.FirstOrDefault(s => s.Partes.Contains(parte));
            if (sesion == null) return;

            if (parte.AsignadoId <= 0)
            {
                MessageBox.Show("Selecciona un hermano antes de enviar la asignación.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tagSub = "";
            if (parte.Tipo == "Oración inicial") tagSub = "Oración inicial";
            else if (parte.Tipo == "Discurso") tagSub = "Discurso";
            else if (parte.Tipo == "Discurso (serie)") tagSub = "Discurso (serie)";
            else
            {
                MessageBox.Show($"El tipo '{parte.Tipo}' no tiene una plantilla de correo configurada para los precursores.\nEscoge 'Oración inicial', 'Discurso' o 'Discurso (serie)'.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tagBusqueda = $"Asignación reunión precursores|{tagSub}";
            var archivosOriginales = DatabaseHelper.ObtenerDocumentos(tagBusqueda);

            if (archivosOriginales == null || archivosOriginales.Count == 0)
            {
                MessageBox.Show($"Faltan los documentos plantilla.\nVe a Correspondencia > Asignación reunión precursores > {tagSub} y súbelos.", "Faltan documentos", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            var infoHermano = DatabaseHelper.ObtenerInfoHermanoParaOrganigrama(parte.AsignadoId);
            if (string.IsNullOrWhiteSpace(infoHermano.Email))
            {
                MessageBox.Show($"El hermano {infoHermano.NombreFormateado} no tiene un correo electrónico registrado en su ficha.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string nombreSiguiente = "-";
            int idx = sesion.Partes.IndexOf(parte);
            if (idx >= 0 && idx + 1 < sesion.Partes.Count)
            {
                var nextParte = sesion.Partes[idx + 1];
                if (nextParte.Tipo == "Discurso (serie)")
                {
                    var nextAsig = ListaPrecursores.FirstOrDefault(a => a.Id == nextParte.AsignadoId) ?? ListaAncianos.FirstOrDefault(a => a.Id == nextParte.AsignadoId);
                    if (nextAsig != null && nextAsig.Id > 0)
                    {
                        nombreSiguiente = nextAsig.NombreMuestra;
                    }
                }
            }

            string asuntoEmail = DatabaseHelper.ObtenerConfiguracion("Asunto_Asignación reunión precursores", "Asignación reunión precursores");
            string mensajeHtml = DatabaseHelper.ObtenerConfiguracion("Email_Asignación reunión precursores", "Querido hermano,<br><br>Te enviamos adjunta tu asignación para la reunión de precursores.<br><br>Un abrazo.");

            string carpetaTemp = Path.Combine(Path.GetTempPath(), "SCCosmos_Asig_" + parte.AsignadoId);
            if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
            Directory.CreateDirectory(carpetaTemp);

            List<ArchivoDocumento> archivosProcesados = new List<ArchivoDocumento>();

            foreach (var arch in archivosOriginales)
            {
                string rutaNueva = Path.Combine(carpetaTemp, arch.NombreArchivo);
                File.Copy(arch.RutaLocal, rutaNueva, true);

                if (Path.GetExtension(rutaNueva).ToLower() == ".docx")
                {
                    try
                    {
                        using (var document = Xceed.Words.NET.DocX.Load(rutaNueva))
                        {
                            document.ReplaceText("$_TITULO", TituloPrecursores ?? "");
                            document.ReplaceText("$_TEXTOTEMATICO", TextoTematicoPrecursores ?? "");
                            document.ReplaceText("$_LUGAR", sesion.Lugar ?? "");

                            string fechaStr = sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("dd 'de' MMMM 'de' yyyy") : "";
                            document.ReplaceText("$_FECHA", fechaStr);

                            document.ReplaceText("$_HORA", sesion.Hora ?? "");
                            document.ReplaceText("$_DISCURSO", parte.Titulo ?? "");
                            document.ReplaceText("$_SIGUIENTE", nombreSiguiente);

                            string varNombreAsig = DatabaseHelper.ObtenerConfiguracion("VarNomAsigPrec", "$_NOMBRE_ORADOR");
                            string varApellidoAsig = DatabaseHelper.ObtenerConfiguracion("VarApAsigPrec", "$_APELLIDO_ORADOR");
                            string varTemaAsig = DatabaseHelper.ObtenerConfiguracion("VarTemaAsigPrec", "$_TEMA_ASIGNADO");

                            string[] partesNombre = infoHermano.NombreFormateado.Split(' ');
                            string nombrePila = partesNombre.Length > 0 ? partesNombre[0] : infoHermano.NombreFormateado;
                            string apellidoOrador = partesNombre.Length > 1 ? partesNombre[1] : "";

                            document.ReplaceText(varNombreAsig, nombrePila);
                            document.ReplaceText(varApellidoAsig, apellidoOrador);
                            document.ReplaceText(varTemaAsig, parte.Titulo ?? "");

                            document.Save();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error inyectando los datos en {arch.NombreArchivo}.\n{ex.Message}");
                    }
                }

                rutaNueva = DocumentoHelper.ConvertirAPdfSiEsWord(rutaNueva);
                string nombreFinalArchivo = Path.GetFileName(rutaNueva);

                archivosProcesados.Add(new ArchivoDocumento
                {
                    Id = arch.Id,
                    NombreArchivo = nombreFinalArchivo,
                    RutaLocal = rutaNueva,
                    Categoria = arch.Categoria
                });
            }

            if (!string.IsNullOrWhiteSpace(parte.RutaArchivo))
            {
                var rutasAdjuntos = parte.RutaArchivo.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var ruta in rutasAdjuntos)
                {
                    if (File.Exists(ruta))
                    {
                        archivosProcesados.Add(new ArchivoDocumento
                        {
                            Id = 0,
                            NombreArchivo = Path.GetFileName(ruta),
                            RutaLocal = ruta,
                            Categoria = "Adjunto"
                        });
                    }
                }
            }

            var popup = new ConfirmarEnvioWindow($"Enviar a: {infoHermano.NombreFormateado}", false, archivosProcesados);
            popup.Owner = Window.GetWindow(this);

            bool? procederEnvio = popup.ShowDialog();
            if (procederEnvio != true) return;

            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OverlayCargando.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() =>
                {
                    var service = FirefoxDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;

                    var options = new FirefoxOptions();
                    options.AddArgument("-profile");
                    options.AddArgument(folderPath);
                    options.AddArgument("-no-remote");

                    IWebDriver driver = new FirefoxDriver(service, options);

                    try
                    {
                        driver.Navigate().GoToUrl("https://mail.jwpub.org/owa/#path=/mail");
                        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

                        IWebElement btnNuevo = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[contains(@title, 'Escribir un mensaje nuevo') or .//span[text()='Nuevo']]"));
                            foreach (var btn in botones) { if (btn.Displayed && btn.Enabled) return btn; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnNuevo);

                        IWebElement inputCorreo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@aria-label='Para' and @role='textbox']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        System.Threading.Thread.Sleep(500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputCorreo);

                        inputCorreo.SendKeys(infoHermano.Email);
                        System.Threading.Thread.Sleep(500);
                        inputCorreo.SendKeys(Keys.Return);
                        System.Threading.Thread.Sleep(200);
                        inputCorreo.SendKeys(Keys.Tab);

                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asuntoEmail);

                        IWebElement inputArchivo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                            return inputs.Count > 0 ? inputs[0] : null;
                        });

                        string rutasUnidas = string.Join("\n", archivosProcesados.Select(a => a.RutaLocal));
                        inputArchivo.SendKeys(rutasUnidas);
                        System.Threading.Thread.Sleep(1500 * archivosProcesados.Count);

                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, mensajeHtml);

                        System.Threading.Thread.Sleep(1000);

                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        System.Threading.Thread.Sleep(4000);
                    }
                    finally
                    {
                        if (driver != null) driver.Quit();
                    }
                });

                MessageBox.Show("Asignación procesada y enviada con éxito al hermano.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hubo un error al enviar el correo automático:\n\n{ex.Message}", "Error de envío", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OverlayCargando.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnEliminarParte_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is PartePrograma parte)
            {
                parte.PropertyChanged -= Objeto_PropertyChanged;
                if (parte.Turno == "Mañana") PartesManana.Remove(parte);
                else if (parte.Turno == "Tarde") PartesTarde.Remove(parte);
                else if (parte.Turno == "Precursores") foreach (var s in SesionesPrecursores) { if (s.Partes.Contains(parte)) { s.Partes.Remove(parte); break; } }
                TriggerAutoSave();
            }
        }

        private void BtnEliminarSesion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is SesionPrecursores sesion)
            {
                sesion.PropertyChanged -= Objeto_PropertyChanged;
                SesionesPrecursores.Remove(sesion);
                for (int i = 0; i < SesionesPrecursores.Count; i++) { SesionesPrecursores[i].Numero = i + 1; }
                if (SesionPrecursoresActiva == sesion) SesionPrecursoresActiva = SesionesPrecursores.FirstOrDefault();
                TriggerAutoSave();
            }
        }

        // ==========================================
        // BOTONES DE AÑADIR
        // ==========================================

        private void BtnAñadirManana_Click(object sender, RoutedEventArgs e) { var p = new PartePrograma { Hora = "", Tipo = "", Titulo = "", Turno = "Mañana", AsignadoId = 0 }; p.PropertyChanged += Objeto_PropertyChanged; PartesManana.Add(p); TriggerAutoSave(); }
        private void BtnAñadirTarde_Click(object sender, RoutedEventArgs e) { var p = new PartePrograma { Hora = "", Tipo = "", Titulo = "", Turno = "Tarde", AsignadoId = 0 }; p.PropertyChanged += Objeto_PropertyChanged; PartesTarde.Add(p); TriggerAutoSave(); }

        private void BtnAñadirAuxiliar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is DepartamentoOrganigrama dept)
            {
                var aux = new AsignacionHermano { Parent = dept, AsignadoId = 0 };
                aux.PropertyChanged += Objeto_PropertyChanged;
                dept.Auxiliares.Add(aux);
                TriggerAutoSave();
            }
        }

        private void BtnAñadirSesion_Click(object sender, RoutedEventArgs e)
        {
            var s = new SesionPrecursores { Numero = SesionesPrecursores.Count + 1, Hora = "", Congregaciones = "Todas", Lugar = "" };
            s.PropertyChanged += Objeto_PropertyChanged;
            SesionesPrecursores.Add(s);
            if (SesionPrecursoresActiva == null) SesionPrecursoresActiva = s;
            TriggerAutoSave();
        }

        private void BtnAñadirPartePrecursores_Click(object sender, RoutedEventArgs e)
        {
            if (SesionPrecursoresActiva == null) { MessageBox.Show("Crea una sesión primero para poder añadir partes al programa.", "Atención", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var p = new PartePrograma { Hora = "", Tipo = "", Titulo = "", Turno = "Precursores", AsignadoId = 0 };
            p.PropertyChanged += Objeto_PropertyChanged;
            SesionPrecursoresActiva.Partes.Add(p);
            TriggerAutoSave();
        }
    }

    // ==========================================
    // CLASES DE DATOS
    // ==========================================

    public class AncianoComboItem
    {
        public int Id { get; set; }
        public string NombreJWHub { get; set; }
        public string Congregacion { get; set; }

        public string NombreFormateado
        {
            get
            {
                if (string.IsNullOrWhiteSpace(NombreJWHub)) return "";
                if (NombreJWHub.Contains(","))
                {
                    var partes = NombreJWHub.Split(',');
                    if (partes.Length >= 2) return $"{partes[1].Trim()} {partes[0].Trim()}";
                }
                return NombreJWHub;
            }
        }

        // FORMATO PARA LOS SELECTORES Y EL PDF: "NOMBRE (CONGREGACIÓN)"
        public string NombreMuestra
        {
            get
            {
                if (string.IsNullOrWhiteSpace(NombreFormateado)) return "";
                if (Id <= 0) return NombreFormateado;
                if (string.IsNullOrWhiteSpace(Congregacion)) return NombreFormateado;
                return $"{NombreFormateado} ({Congregacion})";
            }
        }
    }

    public class AsignacionHermano : INotifyPropertyChanged
    {
        public DepartamentoOrganigrama Parent { get; set; }
        private int _asignadoId; public int AsignadoId { get => _asignadoId; set { _asignadoId = value; OnPropertyChanged("AsignadoId"); OnPropertyChanged("VisibilidadCruz"); } }

        public Visibility VisibilidadCruz => AsignadoId > 0 ? Visibility.Visible : Visibility.Collapsed;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DepartamentoOrganigrama : INotifyPropertyChanged
    {
        public string Nombre { get; set; }
        public string ColorHeader { get; set; }
        public AsignacionHermano Superintendente { get; set; }
        public AsignacionHermano Auditor { get; set; }
        public ObservableCollection<AsignacionHermano> Auxiliares { get; set; }

        public Visibility MostrarAuxiliares => Auxiliares != null && Auxiliares.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MostrarSeparadorAuxiliares => (Superintendente.AsignadoId > 0 || (Auditor != null && Auditor.AsignadoId > 0)) && MostrarAuxiliares == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility VisibilidadAuditor => Nombre == "CONTABILIDAD" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SesionPrecursores : INotifyPropertyChanged
    {
        public int IdBD { get; set; }
        private int _numero; public int Numero { get => _numero; set { _numero = value; OnPropertyChanged("Numero"); OnPropertyChanged("TituloSesion"); } }
        public string TituloSesion => $"Sesión {Numero}";
        private DateTime? _fecha; public DateTime? Fecha { get => _fecha; set { _fecha = value; OnPropertyChanged("Fecha"); } }
        private string _hora; public string Hora { get => _hora; set { _hora = value; OnPropertyChanged("Hora"); } }
        private string _lugar; public string Lugar { get => _lugar; set { _lugar = value; OnPropertyChanged("Lugar"); } }
        private string _congregaciones; public string Congregaciones { get => _congregaciones; set { _congregaciones = value; OnPropertyChanged("Congregaciones"); } }
        public ObservableCollection<PartePrograma> Partes { get; set; } = new ObservableCollection<PartePrograma>();
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PartePrograma : INotifyPropertyChanged
    {
        public string Turno { get; set; }
        private string _hora; public string Hora { get => _hora; set { _hora = value; OnPropertyChanged("Hora"); } }
        private string _tipo; public string Tipo { get => _tipo; set { _tipo = value; OnPropertyChanged("Tipo"); } }
        private string _titulo; public string Titulo { get => _titulo; set { _titulo = value; OnPropertyChanged("Titulo"); } }
        private int _asignadoId; public int AsignadoId { get => _asignadoId; set { _asignadoId = value; OnPropertyChanged("AsignadoId"); OnPropertyChanged("VisibilidadEnviar"); } }
        private string _participantes; public string Participantes { get => _participantes; set { _participantes = value; OnPropertyChanged("Participantes"); } }
        private string _rutaArchivo; public string RutaArchivo { get => _rutaArchivo; set { _rutaArchivo = value; OnPropertyChanged("RutaArchivo"); OnPropertyChanged("ColorBotonArchivo"); OnPropertyChanged("ToolTipArchivo"); } }

        public Visibility VisibilidadEnviar => AsignadoId > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ColorBotonArchivo => string.IsNullOrEmpty(RutaArchivo) ? "Secondary" : "Primary";

        public string ToolTipArchivo
        {
            get
            {
                if (string.IsNullOrEmpty(RutaArchivo)) return "Adjuntar archivo(s)";
                var archivos = RutaArchivo.Split('|').Select(f => System.IO.Path.GetFileName(f));
                return $"Archivos adjuntos:\n{string.Join("\n", archivos)}\n\n(Clic para borrar o cambiar)";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}