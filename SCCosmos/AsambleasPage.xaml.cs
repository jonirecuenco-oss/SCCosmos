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
using iText.IO.Font;

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

        // TEMPORIZADOR ANTI-LAG PARA COMBOBOX
        private DispatcherTimer _comboFilterTimer;
        private ComboBox _activeComboBox;
        private TextBox _activeTextBox;
        private string _activeSearchText;
        private bool _isFiltering = false;

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

            _comboFilterTimer = new DispatcherTimer();
            _comboFilterTimer.Interval = TimeSpan.FromMilliseconds(350);
            _comboFilterTimer.Tick += ComboFilterTimer_Tick;

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

            var congregaciones = DatabaseHelper.ObtenerCongregaciones();
            var dictSecciones = new Dictionary<string, string>();
            if (congregaciones != null)
            {
                foreach (var c in congregaciones)
                {
                    dictSecciones[c.Nombre] = c.NombreSeccion;
                }
            }

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

                string seccionP = dictSecciones.ContainsKey(congre) ? dictSecciones[congre] : "";

                if (nombreJW.Contains("Jonatán Recuenco") || nombre.Contains("Jonatán Recuenco") ||
                    nombreJW.Contains("Representante de Betel") || nombre.Contains("Representante de Betel"))
                    continue;

                bool esAnciano = nombramiento.Contains("anciano");
                bool esPrecursor = nombramiento.Contains("precursor regular") || nombramiento.Contains("precursor especial") || nombramiento.Contains("misionero") ||
                                   servicio.Contains("precursor regular") || servicio.Contains("precursor especial") || servicio.Contains("misionero");

                if (esAnciano || esPrecursor)
                {
                    var item = new AncianoComboItem
                    {
                        Id = id,
                        NombreJWHub = !string.IsNullOrWhiteSpace(nombreJW) ? nombreJW : nombre,
                        NombreComun = p.NombreComun,
                        Congregacion = congre,
                        NombreSeccion = seccionP,
                        EmailJW = p.EmailJW,
                        Email = p.Email
                    };

                    if (esAnciano && !ancianosList.Any(a => a.Id == id)) ancianosList.Add(item);
                    if (esAnciano && esPrecursor && !precursoresList.Any(a => a.Id == id)) precursoresList.Add(item);
                }
            }

            ancianosList = ancianosList.OrderBy(a => a.NombreFormateado).ToList();
            ancianosList.Insert(0, new AncianoComboItem { Id = 0, NombreJWHub = "", NombreComun = "", Congregacion = "", NombreSeccion = "" });
            ListaAncianos = new ObservableCollection<AncianoComboItem>(ancianosList);

            precursoresList = precursoresList.OrderBy(a => a.NombreFormateado).ToList();
            precursoresList.Insert(0, new AncianoComboItem { Id = 0, NombreJWHub = "", NombreComun = "", Congregacion = "", NombreSeccion = "" });
            ListaPrecursores = new ObservableCollection<AncianoComboItem>(precursoresList);

            var listaCongs = DatabaseHelper.ObtenerListaCongregaciones();
            listaCongs.Insert(0, "Todas");
            ListaCongregaciones = new ObservableCollection<string>(listaCongs);
        }

        private void CmbAsignado_Loaded(object sender, RoutedEventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb == null || cmb.ItemsSource != null) return;

            if (cmb.Tag?.ToString() == "Ancianos")
                cmb.ItemsSource = new System.Windows.Data.ListCollectionView(ListaAncianos);
            else if (cmb.Tag?.ToString() == "Precursores")
                cmb.ItemsSource = new System.Windows.Data.ListCollectionView(ListaPrecursores);

            cmb.DropDownOpened -= CmbAsignado_DropDownOpened;
            cmb.DropDownOpened += CmbAsignado_DropDownOpened;
        }

        private void CmbAsignado_DropDownOpened(object sender, EventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb != null && !_isFiltering)
            {
                AplicarFiltroComboBox(cmb, cmb.Text, false);
            }
        }

        private void CmbAsignado_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isFiltering) return;

            var cmb = sender as ComboBox;
            var tb = e.OriginalSource as TextBox;
            if (cmb == null || tb == null || !tb.IsKeyboardFocusWithin) return;

            var selectedItem = cmb.SelectedItem as AncianoComboItem;
            if (selectedItem != null && selectedItem.NombreMuestra == tb.Text)
            {
                return;
            }

            _activeComboBox = cmb;
            _activeTextBox = tb;
            _activeSearchText = tb.Text;

            _comboFilterTimer.Stop();
            _comboFilterTimer.Start();
        }

        private void ComboFilterTimer_Tick(object sender, EventArgs e)
        {
            _comboFilterTimer.Stop();

            if (_activeComboBox == null || _activeTextBox == null) return;

            _isFiltering = true;

            int caretIndex = _activeTextBox.SelectionStart;

            AplicarFiltroComboBox(_activeComboBox, _activeSearchText, true);

            _activeComboBox.IsDropDownOpen = true;

            _activeTextBox.Text = _activeSearchText;
            _activeTextBox.SelectionStart = caretIndex;

            _isFiltering = false;
        }

        private void AplicarFiltroComboBox(ComboBox cmb, string textoBusqueda, bool esBusquedaManual)
        {
            var cv = cmb.ItemsSource as System.Windows.Data.ListCollectionView;
            if (cv == null) return;

            string txt = textoBusqueda?.ToLower() ?? "";
            string seccionFiltro = null;

            if (cmb.Tag?.ToString() == "Ancianos")
            {
                seccionFiltro = SeccionOrganigramaSeleccionada?.Nombre;
            }

            if (cmb.DataContext is PartePrograma parte)
            {
                if (PartesManana.Contains(parte) || PartesTarde.Contains(parte))
                {
                    seccionFiltro = SeccionSeleccionada?.Nombre;
                }
            }

            if (!esBusquedaManual)
            {
                var selectedAnciano = cmb.SelectedItem as AncianoComboItem;
                if (selectedAnciano != null && selectedAnciano.NombreMuestra.ToLower() == txt)
                {
                    txt = "";
                }
            }

            cv.Filter = item =>
            {
                var persona = item as AncianoComboItem;
                if (persona == null) return false;

                if (persona.Id == 0) return true;

                if (!string.IsNullOrWhiteSpace(seccionFiltro))
                {
                    if (persona.NombreSeccion != seccionFiltro) return false;
                }

                if (!string.IsNullOrWhiteSpace(txt) && !persona.NombreMuestra.ToLower().Contains(txt))
                    return false;

                return true;
            };
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
        private void Objeto_PropertyChanged(object sender, PropertyChangedEventArgs e) { if (e.PropertyName != "VisibilidadEnviar" && e.PropertyName != "ColorBotonArchivo" && e.PropertyName != "ToolTipArchivo" && e.PropertyName != "ColorBotonEmail" && e.PropertyName != "ToolTipEmail" && e.PropertyName != "VisibilidadCruz" && e.PropertyName != "TituloSesion" && e.PropertyName != "Lugar") TriggerAutoSave(); }

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
        // CARGA INTELIGENTE DE FUENTES SEGOE UI
        // ==========================================
        private PdfFont GetPdfFont(string fontName, string style)
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string file = fontName;
                if (style == "Bold") file = "segoeuib.ttf";
                else if (style == "Italic") file = "segoeuii.ttf";
                else file = "segoeui.ttf"; // Normal

                return PdfFontFactory.CreateFont(Path.Combine(folder, file), PdfEncodings.IDENTITY_H);
            }
            catch
            {
                if (style == "Bold") return PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                else if (style == "Italic") return PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);
                else return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            }
        }

        private string FormatearTelefonoPDF(string tel)
        {
            if (string.IsNullOrWhiteSpace(tel)) return "";
            string clean = tel.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            if (clean.StartsWith("+34")) clean = clean.Substring(3);

            if (!clean.StartsWith("+") && clean.Length == 9)
            {
                return $"{clean.Substring(0, 3)} {clean.Substring(3, 3)} {clean.Substring(6, 3)}";
            }
            return clean;
        }

        // ==========================================
        // GENERACIÓN DE NOMBRE DE ARCHIVO ORGANIGRAMA
        // ==========================================
        private string GenerarNombreArchivoOrganigrama()
        {
            string tipo = "AC-CO";
            if (!string.IsNullOrEmpty(TipoOrganigramaSeleccionado) && TipoOrganigramaSeleccionado.ToLower().Contains("betel"))
            {
                tipo = "AC-BR";
            }

            string seccion = "";
            if (ListaSecciones != null && ListaSecciones.Count > 1 && SeccionOrganigramaSeleccionada != null)
            {
                seccion = $"_{SeccionOrganigramaSeleccionada.Nombre}";
            }

            return $"Organigrama_{tipo}_{AñoOrganigramaSeleccionado}{seccion}.pdf";
        }

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
                        document.SetMargins(15, 15, 15, 15);
                        PdfFont fontBold = GetPdfFont("Segoe UI", "Bold");
                        PdfFont fontItalic = GetPdfFont("Segoe UI", "Italic");
                        PdfFont fontNormal = GetPdfFont("Segoe UI", "Normal");

                        string titulo = string.IsNullOrWhiteSpace(TituloAsamblea) ? "Asamblea de Circuito" : TituloAsamblea.ToUpper();
                        document.Add(new Paragraph(new Text(titulo).SetFont(fontBold)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(16).SetFontColor(new DeviceRgb(47, 84, 150)).SetMarginBottom(2));

                        string texto = string.IsNullOrWhiteSpace(TextoTematico) ? "" : $"({TextoTematico})";
                        if (!string.IsNullOrWhiteSpace(texto)) document.Add(new Paragraph(new Text(texto).SetFont(fontNormal)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(10).SetFontColor(ColorConstants.BLACK).SetMarginTop(0).SetMarginBottom(10));
                        else document.Add(new Paragraph("").SetMarginBottom(10));

                        Table tablaMaestra = new Table(new float[] { 1, 3, 2, 3 }).UseAllAvailableWidth();
                        void AñadirFilas(string turno, ObservableCollection<PartePrograma> partes)
                        {
                            if (partes.Count == 0) return;
                            tablaMaestra.AddCell(new Cell(1, 4).Add(new Paragraph(new Text(turno).SetFont(fontBold)).SetFontSize(10).SetFontColor(ColorConstants.DARK_GRAY)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(6).SetPaddingBottom(3).SetPaddingLeft(0));
                            foreach (var p in partes)
                            {
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Hora ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(2).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Titulo ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(2).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));

                                var asignado = ListaAncianos.FirstOrDefault(a => a.Id == p.AsignadoId) ?? ListaPrecursores.FirstOrDefault(a => a.Id == p.AsignadoId);
                                string nombreOrador = asignado?.NombreMuestra ?? "";

                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(nombreOrador).SetFont(fontNormal).SetFontSize(8)).SetPadding(2).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                tablaMaestra.AddCell(new Cell().Add(new Paragraph(p.Participantes ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(2).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
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

        private class TreeLineRenderer : iText.Layout.Renderer.CellRenderer
        {
            private bool _isLast;
            public TreeLineRenderer(iText.Layout.Element.Cell modelElement, bool isLast) : base(modelElement) { _isLast = isLast; }
            public override void Draw(iText.Layout.Renderer.DrawContext drawContext)
            {
                base.Draw(drawContext);
                var canvas = drawContext.GetCanvas();
                var rect = GetOccupiedAreaBBox();
                canvas.SaveState();
                canvas.SetStrokeColor(ColorConstants.BLACK);
                canvas.SetLineWidth(0.5f);

                float lineX = rect.GetLeft() + 10f;
                float branchY = rect.GetTop() - 10f;

                canvas.MoveTo(lineX, rect.GetTop() + 0.5f);
                canvas.LineTo(lineX, _isLast ? branchY : rect.GetBottom() - 0.5f);

                canvas.MoveTo(lineX, branchY);
                canvas.LineTo(rect.GetLeft() + 25f, branchY);

                canvas.Stroke();
                canvas.RestoreState();
            }
            public override iText.Layout.Renderer.IRenderer GetNextRenderer() { return new TreeLineRenderer((iText.Layout.Element.Cell)GetModelElement(), _isLast); }
        }

        private class RootGapRenderer : iText.Layout.Renderer.CellRenderer
        {
            public RootGapRenderer(iText.Layout.Element.Cell modelElement) : base(modelElement) { }
            public override void Draw(iText.Layout.Renderer.DrawContext drawContext)
            {
                base.Draw(drawContext);
                var canvas = drawContext.GetCanvas();
                var rect = GetOccupiedAreaBBox();
                canvas.SaveState();
                canvas.SetStrokeColor(ColorConstants.BLACK);
                canvas.SetLineWidth(0.5f);

                float lineX = rect.GetLeft() + 10f;
                canvas.MoveTo(lineX, rect.GetTop() + 0.5f);
                canvas.LineTo(lineX, rect.GetBottom() - 0.5f);

                canvas.Stroke();
                canvas.RestoreState();
            }
            public override iText.Layout.Renderer.IRenderer GetNextRenderer() { return new RootGapRenderer((iText.Layout.Element.Cell)GetModelElement()); }
        }

        private class StackGapRenderer : iText.Layout.Renderer.CellRenderer
        {
            public StackGapRenderer(iText.Layout.Element.Cell modelElement) : base(modelElement) { }
            public override void Draw(iText.Layout.Renderer.DrawContext drawContext)
            {
                base.Draw(drawContext);
                var canvas = drawContext.GetCanvas();
                var rect = GetOccupiedAreaBBox();
                canvas.SaveState();
                canvas.SetStrokeColor(ColorConstants.BLACK);
                canvas.SetLineWidth(0.5f);

                float lineX = rect.GetLeft() + 25f;

                canvas.MoveTo(lineX, rect.GetTop() + 0.5f);
                canvas.LineTo(lineX, rect.GetBottom() - 0.5f);

                canvas.Stroke();
                canvas.RestoreState();
            }
            public override iText.Layout.Renderer.IRenderer GetNextRenderer() { return new StackGapRenderer((iText.Layout.Element.Cell)GetModelElement()); }
        }

        private void CrearArchivoPdfOrganigrama(string rutaDestino)
        {
            DatabaseHelper.ObtenerIdYDatosAsamblea(AñoOrganigramaSeleccionado, TipoOrganigramaSeleccionado, SeccionOrganigramaSeleccionada.Nombre, out string tituloAsamblea, out string textoTematico, out string lugarAsamblea, out DateTime? fechaAsamblea);

            using (PdfWriter writer = new PdfWriter(rutaDestino))
            using (PdfDocument pdf = new PdfDocument(writer))
            using (Document document = new Document(pdf))
            {
                document.SetMargins(15, 20, 15, 20);

                PdfFont fontBold = GetPdfFont("Segoe UI", "Bold");
                PdfFont fontNormal = GetPdfFont("Segoe UI", "Normal");

                float GAP = 12f;

                string titulo = string.IsNullOrWhiteSpace(tituloAsamblea) ? "ASAMBLEA DE CIRCUITO" : tituloAsamblea.ToUpper();
                document.Add(new Paragraph(new Text(titulo).SetFont(fontBold)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(16).SetFontColor(new DeviceRgb(47, 84, 150)).SetMarginBottom(10f));

                Table tHeader = new Table(new float[] { 1, 1 }).UseAllAvailableWidth().SetMarginBottom(8);
                string fechaStr = fechaAsamblea.HasValue ? fechaAsamblea.Value.ToString("dd DE MMMM DE yyyy").ToUpper() : "";
                tHeader.AddCell(new Cell().Add(new Paragraph(fechaStr).SetFont(fontNormal).SetFontSize(8).SetFontColor(new DeviceRgb(47, 84, 150))).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetBorderTop(new iText.Layout.Borders.SolidBorder(new DeviceRgb(143, 170, 220), 1)).SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(143, 170, 220), 1)).SetPadding(2));
                tHeader.AddCell(new Cell().Add(new Paragraph(lugarAsamblea ?? "").SetFont(fontNormal).SetFontSize(8).SetFontColor(new DeviceRgb(47, 84, 150)).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetBorderTop(new iText.Layout.Borders.SolidBorder(new DeviceRgb(143, 170, 220), 1)).SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(143, 170, 220), 1)).SetPadding(2));
                document.Add(tHeader);

                Color cSupAsam = new DeviceRgb(184, 220, 235);
                Color cSupAux = new DeviceRgb(250, 203, 164);
                Color cDeptsAzul = new DeviceRgb(222, 235, 247);
                Color cDeptsNaranja = new DeviceRgb(253, 233, 217);
                Color cAuditor = new DeviceRgb(146, 205, 220);
                Color cSeguridad = new DeviceRgb(217, 150, 148);

                bool TieneGente(DepartamentoOrganigrama d)
                {
                    if (d == null) return false;
                    return d.Superintendente.AsignadoId > 0 || (d.Auditor != null && d.Auditor.AsignadoId > 0) || d.Auxiliares.Any(a => a.AsignadoId > 0);
                }

                void AddHermano(Table targetTable, int id, bool isFirst, bool isCompact)
                {
                    if (id <= 0) return;
                    var info = DatabaseHelper.ObtenerInfoHermanoParaOrganigrama(id);

                    Cell pCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);

                    if (!isFirst) pCell.SetBorderTop(new iText.Layout.Borders.SolidBorder(ColorConstants.GRAY, 0.5f));

                    Table inTable = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 })).UseAllAvailableWidth().SetMarginBottom(0);

                    string nombreAMostrar = !string.IsNullOrWhiteSpace(info.NombreComun) ? info.NombreComun : info.NombreFormateado;
                    string nombreStr = $"{nombreAMostrar}" + (!string.IsNullOrWhiteSpace(info.AnioNacimiento) ? $" ({info.AnioNacimiento})" : "");

                    string telFormat = FormatearTelefonoPDF(info.Telefono);

                    string emailMostrar = !string.IsNullOrWhiteSpace(info.EmailJW) ? info.EmailJW : info.Email;

                    float padTop1 = isCompact ? 0f : 2f;
                    float padBot1 = 0f;
                    float padTop2 = isCompact ? 0f : 1f;
                    float padBot2 = isCompact ? 0f : 2f;

                    // Primera Fila
                    inTable.AddCell(new Cell().SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(padTop1).SetPaddingBottom(padBot1)
                        .Add(new Paragraph(nombreStr).SetFont(fontBold).SetFontSize(7.5f).SetMargin(0)));

                    inTable.AddCell(new Cell().SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(padTop1).SetPaddingBottom(padBot1)
                        .Add(new Paragraph(info.Congregacion).SetFont(fontNormal).SetFontSize(7.5f).SetFontColor(ColorConstants.BLACK).SetMargin(0)));

                    // Segunda Fila
                    inTable.AddCell(new Cell().SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(padTop2).SetPaddingBottom(padBot2)
                        .Add(new Paragraph(telFormat).SetFont(fontNormal).SetFontSize(7.5f).SetMargin(0)));

                    inTable.AddCell(new Cell().SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPaddingTop(padTop2).SetPaddingBottom(padBot2)
                        .Add(new Paragraph(emailMostrar).SetFont(fontNormal).SetFontSize(7.5f).SetFontColor(ColorConstants.BLUE).SetMargin(0)));

                    pCell.Add(inTable);
                    targetTable.AddCell(pCell);
                }

                Table CrearCaja(DepartamentoOrganigrama dept, Color bgColor, bool isCompact = false)
                {
                    float headPad = isCompact ? 0f : 3f;

                    Table t = new Table(1).UseAllAvailableWidth().SetMarginBottom(0);

                    Cell header = new Cell().SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBackgroundColor(bgColor).SetBorder(new iText.Layout.Borders.SolidBorder(ColorConstants.BLACK, 0.5f)).SetPaddingTop(headPad).SetPaddingBottom(headPad)
                        .Add(new Paragraph(dept.Nombre).SetFont(fontNormal).SetFontSize(8.5f).SetMargin(0));
                    t.AddCell(header);

                    Cell contentCell = new Cell().SetBorder(new iText.Layout.Borders.SolidBorder(ColorConstants.BLACK, 0.5f)).SetBorderTop(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                    Table peopleTable = new Table(1).UseAllAvailableWidth();

                    bool isFirst = true;
                    if (dept.Superintendente.AsignadoId > 0)
                    {
                        AddHermano(peopleTable, dept.Superintendente.AsignadoId, isFirst, isCompact);
                        isFirst = false;
                    }
                    foreach (var aux in dept.Auxiliares)
                    {
                        AddHermano(peopleTable, aux.AsignadoId, isFirst, isCompact);
                        isFirst = false;
                    }
                    contentCell.Add(peopleTable);

                    if (dept.Nombre == "CONTABILIDAD" && dept.Auditor != null && dept.Auditor.AsignadoId > 0)
                    {
                        Cell audHeader = new Cell(1, 2).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE).SetBackgroundColor(cAuditor).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetBorderTop(new iText.Layout.Borders.SolidBorder(ColorConstants.BLACK, 0.5f)).SetBorderBottom(new iText.Layout.Borders.SolidBorder(ColorConstants.BLACK, 0.5f)).SetPaddingTop(1).SetPaddingBottom(1)
                            .Add(new Paragraph("Auditor").SetFont(fontNormal).SetFontSize(8.5f).SetMargin(0));
                        peopleTable.AddCell(audHeader);

                        AddHermano(peopleTable, dept.Auditor.AsignadoId, true, false);
                    }

                    t.AddCell(contentCell);
                    return t;
                }

                Cell CreateChildCell(DepartamentoOrganigrama dept, Color bgColor, bool isLast)
                {
                    Cell cell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingLeft(25f).SetPaddingBottom(GAP);
                    Table t = CrearCaja(dept, bgColor, false);
                    cell.Add(t);
                    cell.SetNextRenderer(new TreeLineRenderer(cell, isLast));
                    return cell;
                }

                Cell CreateStackedChildCell(List<Tuple<DepartamentoOrganigrama, Color>> depts, bool isLast)
                {
                    Cell cell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingLeft(25f).SetPaddingBottom(GAP);

                    for (int i = 0; i < depts.Count; i++)
                    {
                        bool isCompact = (depts[i].Item1.Nombre == "AUDIO" || depts[i].Item1.Nombre == "PLATAFORMA");
                        Table t = CrearCaja(depts[i].Item1, depts[i].Item2, isCompact);
                        cell.Add(t);

                        if (i < depts.Count - 1)
                        {
                            Cell gap = new Cell().SetHeight(GAP).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                            gap.SetNextRenderer(new StackGapRenderer(gap));
                            Table gapTable = new Table(1).UseAllAvailableWidth().SetMarginLeft(25f);
                            gapTable.AddCell(gap);
                            cell.Add(gapTable);
                        }
                    }
                    cell.SetNextRenderer(new TreeLineRenderer(cell, isLast));
                    return cell;
                }

                Table tLeft = new Table(1).UseAllAvailableWidth();
                if (TieneGente(DeptSupAsamblea))
                {
                    tLeft.AddCell(new Cell().Add(CrearCaja(DeptSupAsamblea, cSupAsam, false)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0));
                    Cell leftGap = new Cell().SetHeight(GAP).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                    leftGap.SetNextRenderer(new RootGapRenderer(leftGap));
                    tLeft.AddCell(leftGap);
                }

                var leftChildren = new List<Tuple<DepartamentoOrganigrama, Color>>();
                if (TieneGente(DeptAcomodadores)) leftChildren.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptAcomodadores, cDeptsAzul));
                if (TieneGente(DeptContabilidad)) leftChildren.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptContabilidad, cDeptsAzul));
                if (TieneGente(DeptEstacionamiento)) leftChildren.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptEstacionamiento, cDeptsAzul));
                if (TieneGente(DeptLimpieza)) leftChildren.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptLimpieza, cDeptsAzul));
                if (TieneGente(DeptPrimerosAuxilios)) leftChildren.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptPrimerosAuxilios, cDeptsAzul));

                for (int i = 0; i < leftChildren.Count; i++)
                {
                    bool isLast = (i == leftChildren.Count - 1);
                    tLeft.AddCell(CreateChildCell(leftChildren[i].Item1, leftChildren[i].Item2, isLast));
                }

                if (TieneGente(DeptSeguridad))
                {
                    Cell segCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingLeft(25f).SetPaddingBottom(GAP);
                    segCell.Add(CrearCaja(DeptSeguridad, cSeguridad, false));
                    tLeft.AddCell(segCell);
                }

                Table tRight = new Table(1).UseAllAvailableWidth();
                if (TieneGente(DeptSupAuxAsamblea))
                {
                    Cell rootSaaCell = new Cell().Add(CrearCaja(DeptSupAuxAsamblea, cSupAux, false)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                    tRight.AddCell(rootSaaCell);

                    Cell rightGap = new Cell().SetHeight(GAP).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0);
                    rightGap.SetNextRenderer(new RootGapRenderer(rightGap));
                    tRight.AddCell(rightGap);
                }

                var rightBlocks = new List<object>();
                var stackedDepts = new List<Tuple<DepartamentoOrganigrama, Color>>();
                if (TieneGente(DeptAudioVideo)) stackedDepts.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptAudioVideo, cDeptsNaranja));
                if (TieneGente(DeptAudio)) stackedDepts.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptAudio, cDeptsNaranja));
                if (TieneGente(DeptPlataforma)) stackedDepts.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptPlataforma, cDeptsNaranja));

                if (stackedDepts.Count > 0) rightBlocks.Add(stackedDepts);
                if (TieneGente(DeptBautismo)) rightBlocks.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptBautismo, cDeptsNaranja));
                if (TieneGente(DeptObjetosPerdidos)) rightBlocks.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptObjetosPerdidos, cDeptsNaranja));
                if (TieneGente(DeptTransporte)) rightBlocks.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptTransporte, cDeptsNaranja));
                if (TieneGente(DeptInstalacion)) rightBlocks.Add(new Tuple<DepartamentoOrganigrama, Color>(DeptInstalacion, cDeptsNaranja));

                for (int i = 0; i < rightBlocks.Count; i++)
                {
                    bool isLast = (i == rightBlocks.Count - 1);
                    if (rightBlocks[i] is List<Tuple<DepartamentoOrganigrama, Color>> list)
                    {
                        tRight.AddCell(CreateStackedChildCell(list, isLast));
                    }
                    else if (rightBlocks[i] is Tuple<DepartamentoOrganigrama, Color> single)
                    {
                        tRight.AddCell(CreateChildCell(single.Item1, single.Item2, isLast));
                    }
                }

                Table tMain = new Table(new UnitValue[] { UnitValue.CreatePercentValue(50f), UnitValue.CreatePercentValue(50f) });
                tMain.SetWidth(UnitValue.CreatePercentValue(100f));

                tMain.AddCell(new Cell().Add(tLeft).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingRight(6f));
                tMain.AddCell(new Cell().Add(tRight).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(0).SetPaddingLeft(6f));

                document.Add(tMain);
            }
        }

        private void BtnGenerarPDFOrganigrama_Click(object sender, RoutedEventArgs e)
        {
            if (SeccionOrganigramaSeleccionada == null) return;

            SaveFileDialog sfd = new SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = GenerarNombreArchivoOrganigrama() };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    CrearArchivoPdfOrganigrama(sfd.FileName);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
            }
        }

        private async void BtnEnviarOrganigrama_Click(object sender, RoutedEventArgs e)
        {
            if (SeccionOrganigramaSeleccionada == null) return;

            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");
            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("Debes ir a 'Configuración' y vincular tu cuenta de correo primero.", "Perfil no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HashSet<string> correos = new HashSet<string>();
            foreach (var d in ListaDepartamentosCajas)
            {
                void RevisarYAgregar(int id)
                {
                    if (id > 0)
                    {
                        var info = DatabaseHelper.ObtenerInfoHermanoParaOrganigrama(id);

                        // PRIORIZAMOS EMAIL JW
                        string correoAUsar = !string.IsNullOrWhiteSpace(info.EmailJW) ? info.EmailJW : info.Email;
                        if (!string.IsNullOrWhiteSpace(correoAUsar)) correos.Add(correoAUsar);
                    }
                }

                RevisarYAgregar(d.Superintendente.AsignadoId);
                if (d.Auditor != null) RevisarYAgregar(d.Auditor.AsignadoId);
                foreach (var aux in d.Auxiliares) RevisarYAgregar(aux.AsignadoId);
            }

            if (correos.Count == 0)
            {
                MessageBox.Show("No se han encontrado correos electrónicos en los participantes asignados al organigrama.", "Sin destinatarios", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string asunto = DatabaseHelper.ObtenerConfiguracion("Asunto_Envío de organigrama", "Organigrama AC");
            string cuerpoHtml = DatabaseHelper.ObtenerConfiguracion("Email_Envío de organigrama", "Queridos compañeros,<br><br>Os dejo adjunto el organigrama de la próxima asamblea de circuito.<br><br>Recibid un fuerte abrazo.");

            string carpetaTemp = Path.Combine(Path.GetTempPath(), $"SCCosmos_Org_{DateTime.Now.Ticks}");
            if (Directory.Exists(carpetaTemp)) Directory.Delete(carpetaTemp, true);
            Directory.CreateDirectory(carpetaTemp);

            string tempFile = Path.Combine(carpetaTemp, GenerarNombreArchivoOrganigrama());
            try
            {
                CrearArchivoPdfOrganigrama(tempFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al generar el PDF adjunto:\n{ex.Message}");
                return;
            }

            List<string> rutasAdjuntos = new List<string> { tempFile };

            var archivosExtra = DatabaseHelper.ObtenerDocumentos("Envío de organigrama");
            if (archivosExtra != null)
            {
                foreach (var arch in archivosExtra)
                {
                    if (File.Exists(arch.RutaLocal))
                    {
                        string rutaCopia = Path.Combine(carpetaTemp, arch.NombreArchivo);
                        File.Copy(arch.RutaLocal, rutaCopia, true);
                        rutasAdjuntos.Add(rutaCopia);
                    }
                }
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

                    using (IWebDriver driver = new FirefoxDriver(service, options))
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

                        foreach (var email in correos)
                        {
                            inputCorreo.SendKeys(email);
                            System.Threading.Thread.Sleep(200);
                            inputCorreo.SendKeys(Keys.Return);
                            System.Threading.Thread.Sleep(200);
                        }
                        inputCorreo.SendKeys(Keys.Tab);

                        IWebElement inputAsunto = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[contains(@aria-label, 'Asunto') or @placeholder='Agregar un asunto']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus(); arguments[0].click();", inputAsunto);
                        inputAsunto.SendKeys(asunto);

                        IWebElement inputArchivo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//input[@type='file']"));
                            return inputs.Count > 0 ? inputs[0] : null;
                        });

                        string rutasUnidas = string.Join("\n", rutasAdjuntos);
                        inputArchivo.SendKeys(rutasUnidas);

                        // === ESPERA EXTENDIDA ANTI-PARPADEO ===
                        System.Threading.Thread.Sleep(1000 * rutasAdjuntos.Count);

                        WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));

                        foreach (var ruta in rutasAdjuntos)
                        {
                            string nombreCorto = Path.GetFileNameWithoutExtension(ruta);
                            if (nombreCorto.Length > 15) nombreCorto = nombreCorto.Substring(0, 15);
                            try { waitSubida.Until(d => d.PageSource.Contains(nombreCorto)); } catch { }
                        }

                        try
                        {
                            waitSubida.Until(d => {
                                var spinners = d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]"));
                                if (spinners.Count == 0)
                                {
                                    System.Threading.Thread.Sleep(1000);
                                    return d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]")).Count == 0;
                                }
                                return false;
                            });
                        }
                        catch { }

                        System.Threading.Thread.Sleep(2000);

                        IWebElement inputCuerpo = wait.Until(d => {
                            var inputs = d.FindElements(By.XPath("//div[@role='textbox' and @aria-label='Cuerpo del mensaje']"));
                            foreach (var input in inputs) { if (input.Displayed && input.Enabled) return input; }
                            return null;
                        });

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].focus(); document.execCommand('insertHTML', false, arguments[1]);",
                            inputCuerpo, cuerpoHtml);

                        System.Threading.Thread.Sleep(1000);

                        IWebElement btnEnviar = wait.Until(d => {
                            var botones = d.FindElements(By.XPath("//button[@aria-label='Enviar' or @title='Enviar']"));
                            foreach (var b in botones) { if (b.Displayed && b.Enabled) return b; }
                            return null;
                        });
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", btnEnviar);

                        System.Threading.Thread.Sleep(4000);
                    }
                });

                MessageBox.Show($"El organigrama ha sido generado y enviado con éxito a {correos.Count} destinatarios.\n\nAdjuntos subidos: {rutasAdjuntos.Count}", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hubo un error al crear el correo:\n\n{ex.Message}", "Error de envío", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OverlayCargando.Visibility = Visibility.Collapsed;
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
                        document.SetMargins(20, 20, 20, 20);
                        PdfFont fontBold = GetPdfFont("Segoe UI", "Bold");
                        PdfFont fontItalic = GetPdfFont("Segoe UI", "Italic");
                        PdfFont fontNormal = GetPdfFont("Segoe UI", "Normal");

                        string titulo = string.IsNullOrWhiteSpace(TituloPrecursores) ? "Reunión Especial con los Precursores" : TituloPrecursores.ToUpper();
                        document.Add(new Paragraph(new Text(titulo).SetFont(fontBold)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(14).SetMarginBottom(2));
                        if (!string.IsNullOrWhiteSpace(TextoTematicoPrecursores)) document.Add(new Paragraph(new Text(TextoTematicoPrecursores).SetFont(fontItalic)).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER).SetFontSize(10).SetMarginBottom(15));
                        else document.Add(new Paragraph("").SetMarginBottom(15));

                        foreach (var sesion in SesionesPrecursores)
                        {
                            document.Add(new Paragraph(new Text(sesion.TituloSesion.ToUpper()).SetFont(fontBold)).SetFontSize(10).SetMarginTop(10).SetMarginBottom(2).SetFontColor(ColorConstants.DARK_GRAY));
                            string fechaStr = sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("dd/MM/yyyy") : "Sin fecha";
                            document.Add(new Paragraph($"Fecha: {fechaStr}   -   Hora: {sesion.Hora}").SetFont(fontNormal).SetFontSize(9));
                            document.Add(new Paragraph($"Lugar: {sesion.Lugar}").SetFont(fontNormal).SetFontSize(9));
                            document.Add(new Paragraph($"Congregaciones: {sesion.Congregaciones}").SetFont(fontItalic).SetFontSize(9).SetMarginBottom(10));

                            if (sesion.Partes.Count > 0)
                            {
                                Table tabla = new Table(new float[] { 1, 3, 2, 3 }).UseAllAvailableWidth().SetMarginBottom(15);
                                foreach (var p in sesion.Partes)
                                {
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Hora ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Titulo ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));

                                    var asignado = ListaPrecursores.FirstOrDefault(a => a.Id == p.AsignadoId) ?? ListaAncianos.FirstOrDefault(a => a.Id == p.AsignadoId);
                                    string nombreOrador = asignado?.NombreMuestra ?? "";

                                    tabla.AddCell(new Cell().Add(new Paragraph(nombreOrador).SetFont(fontNormal).SetFontSize(8)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
                                    tabla.AddCell(new Cell().Add(new Paragraph(p.Participantes ?? "").SetFont(fontNormal).SetFontSize(8)).SetPadding(3).SetVerticalAlignment(iText.Layout.Properties.VerticalAlignment.MIDDLE));
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

        private async void BtnEnviarAsignacion_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Wpf.Ui.Controls.Button ?? sender as Button;
            var parte = boton?.Tag as PartePrograma;
            if (parte == null) return;

            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

            if (parte.Turno != "Precursores")
            {
                var asignadoAsamblea = ListaAncianos.FirstOrDefault(a => a.Id == parte.AsignadoId) ?? ListaPrecursores.FirstOrDefault(a => a.Id == parte.AsignadoId);
                string nombreAsamblea = asignadoAsamblea?.NombreMuestra ?? "Desconocido";
                MessageBox.Show($"Módulo de envíos en desarrollo.\nSe enviará a: {nombreAsamblea}", "Enviar Asignación", MessageBoxButton.OK, MessageBoxImage.Information);

                parte.Enviado = true;
                TriggerAutoSave();
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

            // PRIORIZAMOS EMAIL JW
            string correoAUsar = !string.IsNullOrWhiteSpace(infoHermano.EmailJW) ? infoHermano.EmailJW : infoHermano.Email;

            if (string.IsNullOrWhiteSpace(correoAUsar))
            {
                MessageBox.Show($"El hermano {infoHermano.NombreFormateado} no tiene ningún correo electrónico registrado en su ficha.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            var popup = new ConfirmarEnvioWindow($"Enviar a: {infoHermano.NombreFormateado}", parte.Enviado, archivosProcesados);
            popup.Owner = Window.GetWindow(this);

            bool? procederEnvio = popup.ShowDialog();

            if (popup.EstadoModificadoManual && procederEnvio != true)
            {
                parte.Enviado = popup.EstadoEnviadoActual;
                TriggerAutoSave();
            }

            if (procederEnvio != true) return;

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

                    using (IWebDriver driver = new FirefoxDriver(service, options))
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

                        inputCorreo.SendKeys(correoAUsar);
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

                        // === ESPERA EXTENDIDA ANTI-PARPADEO ===
                        System.Threading.Thread.Sleep(1000 * archivosProcesados.Count);

                        WebDriverWait waitSubida = new WebDriverWait(driver, TimeSpan.FromSeconds(90));

                        foreach (var arch in archivosProcesados)
                        {
                            string nombreCorto = Path.GetFileNameWithoutExtension(arch.NombreArchivo);
                            if (nombreCorto.Length > 15) nombreCorto = nombreCorto.Substring(0, 15);
                            try { waitSubida.Until(d => d.PageSource.Contains(nombreCorto)); } catch { }
                        }

                        try
                        {
                            waitSubida.Until(d => {
                                var spinners = d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]"));
                                if (spinners.Count == 0)
                                {
                                    System.Threading.Thread.Sleep(1000);
                                    return d.FindElements(By.XPath("//div[contains(@class, 'spinnerAnimation')]")).Count == 0;
                                }
                                return false;
                            });
                        }
                        catch { }

                        System.Threading.Thread.Sleep(2000);

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
                });

                parte.Enviado = true;
                TriggerAutoSave();

                MessageBox.Show("Asignación procesada y enviada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hubo un error al crear el correo:\n\n{ex.Message}", "Error de envío", MessageBoxButton.OK, MessageBoxImage.Error);
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
        public string NombreComun { get; set; }
        public string Congregacion { get; set; }
        public string NombreSeccion { get; set; }
        public string EmailJW { get; set; }
        public string Email { get; set; }

        public string NombreFormateado
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(NombreComun)) return NombreComun;

                if (string.IsNullOrWhiteSpace(NombreJWHub)) return "";
                if (NombreJWHub.Contains(","))
                {
                    var partes = NombreJWHub.Split(',');
                    if (partes.Length >= 2) return $"{partes[1].Trim()} {partes[0].Trim()}";
                }
                return NombreJWHub;
            }
        }

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

        private bool _enviado;
        public bool Enviado
        {
            get => _enviado;
            set
            {
                _enviado = value;
                OnPropertyChanged("Enviado");
                OnPropertyChanged("ColorBotonEmail");
                OnPropertyChanged("ToolTipEmail");
            }
        }

        public Visibility VisibilidadEnviar => AsignadoId > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ColorBotonArchivo => string.IsNullOrEmpty(RutaArchivo) ? "Secondary" : "Primary";

        public string ColorBotonEmail => Enviado ? "Success" : "Secondary";
        public string ToolTipEmail => Enviado ? "Asignación enviada (Clic para reenviar)" : "Enviar asignación";

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