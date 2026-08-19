using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.Win32;
using System.Net.Http;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows.Media;

namespace SCCosmos
{
    public partial class FormularioPersonaWindow : Window
    {
        public Persona PersonaActualizada { get; private set; }
        private string _base64FotoModificada;
        private string _base64FotoFusion;

        private string _direccionInicial;

        private static readonly HttpClient _httpClient = new HttpClient();
        private DispatcherTimer _debounceMapas;
        private double _latitudActual = 0;
        private double _longitudActual = 0;

        private List<Persona> _listaVincularCompleta;
        private bool _esUnaFusion = false;
        private List<Persona> _candidatosPredefinidos;

        public FormularioPersonaWindow(Persona personaInicial)
            : this(personaInicial, null)
        {
        }

        public FormularioPersonaWindow(Persona personaInicial, List<Persona> candidatosLimitados)
        {
            InitializeComponent();

            _candidatosPredefinidos = candidatosLimitados;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 SCCosmos/1.0");

            _debounceMapas = new DispatcherTimer();
            _debounceMapas.Interval = TimeSpan.FromMilliseconds(500);
            _debounceMapas.Tick += BuscadorMapa_Tick;

            PersonaActualizada = new Persona
            {
                Id = personaInicial.Id,
                Nombre = personaInicial.Nombre,
                NombreComun = personaInicial.NombreComun,
                NombreJWHub = personaInicial.NombreJWHub,
                Sexo = personaInicial.Sexo,
                FechaNacimiento = personaInicial.FechaNacimiento,
                FechaBautismo = personaInicial.FechaBautismo,
                Esperanza = personaInicial.Esperanza,
                Nombramiento = personaInicial.Nombramiento,
                Servicio = personaInicial.Servicio,
                CongregacionCodigo = personaInicial.CongregacionCodigo,
                Grupo = personaInicial.Grupo,
                Telefono = personaInicial.Telefono,
                Email = personaInicial.Email,
                FechaInicioPrec = personaInicial.FechaInicioPrec,
                FechaUltimaEscuela = personaInicial.FechaUltimaEscuela,
                Solicitudes = personaInicial.Solicitudes,
                Privilegios = personaInicial.Privilegios,
                Foto = personaInicial.Foto,
                Direccion = personaInicial.Direccion,
                Latitud = personaInicial.Latitud,
                Longitud = personaInicial.Longitud,
                ConyugeId = personaInicial.ConyugeId,
                EmailJW = personaInicial.EmailJW
            };

            _base64FotoModificada = PersonaActualizada.Foto;
            _latitudActual = PersonaActualizada.Latitud;
            _longitudActual = PersonaActualizada.Longitud;
            _direccionInicial = PersonaActualizada.Direccion ?? "";

            CargarDatosEnInterfaz();
            RefrescarListaConyugesYVincular();
        }

        // ========================================================
        // NUEVO MÉTODO PÚBLICO PARA SYNC HUB (AHORA SILENCIOSO)
        // ========================================================
        public void CargarConflictosDesdeHub(Persona pHub)
        {
            LimpiarSelectsDinamicos();
            var camposCambiados = EvaluarConflictos(pHub);

            if (camposCambiados.Count > 0)
            {
                // Solo mostramos el texto de advertencia en rojo arriba del formulario, sin Popups
                TxtAdvertenciaConflictos.Visibility = Visibility.Visible;
            }
            // Si no hay cambios (Count == 0), no mostramos nada y se abre el formulario normal.
        }

        private void CargarDatosEnInterfaz()
        {
            TxtNombre.Text = PersonaActualizada.Nombre;
            TxtNombreComun.Text = PersonaActualizada.NombreComun;
            TxtNombreJW.Text = PersonaActualizada.NombreJWHub;
            TxtEmail.Text = PersonaActualizada.Email;
            TxtEmailJW.Text = PersonaActualizada.EmailJW;
            TxtTelefono.Text = PersonaActualizada.Telefono;
            TxtDireccion.Text = PersonaActualizada.Direccion;

            TxtGrupo.Text = PersonaActualizada.Grupo > 0 ? PersonaActualizada.Grupo.ToString() : "";
            TxtFechaNac.Text = PersonaActualizada.FechaNacimiento;
            TxtFechaBaut.Text = PersonaActualizada.FechaBautismo;
            TxtInicioPrec.Text = PersonaActualizada.FechaInicioPrec;
            TxtEscuela.Text = PersonaActualizada.FechaUltimaEscuela;

            AsignarComboBox(CmbSexo, PersonaActualizada.Sexo);
            AsignarComboBox(CmbEsperanza, PersonaActualizada.Esperanza);
            AsignarComboBox(CmbNombramiento, PersonaActualizada.Nombramiento);
            AsignarComboBox(CmbServicio, PersonaActualizada.Servicio);

            var listaCongregaciones = DatabaseHelper.ObtenerCongregaciones();
            CmbCongregacion.ItemsSource = listaCongregaciones;
            if (!string.IsNullOrEmpty(PersonaActualizada.CongregacionCodigo))
            {
                CmbCongregacion.SelectedValue = PersonaActualizada.CongregacionCodigo;
            }

            TxtPrivilegios.Text = PersonaActualizada.Privilegios;

            if (!string.IsNullOrEmpty(PersonaActualizada.Solicitudes))
            {
                foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>())
                {
                    if (PersonaActualizada.Solicitudes.Contains(chk.Tag.ToString()))
                        chk.IsChecked = true;
                }
            }

            RefrescarImagenUI();
        }

        private BitmapImage ConvertirABitmap(string base64)
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(imageBytes))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    return image;
                }
            }
            catch { return null; }
        }

        private void RefrescarImagenUI()
        {
            if (!string.IsNullOrEmpty(_base64FotoModificada))
            {
                ImgFoto.Source = ConvertirABitmap(_base64FotoModificada);
            }
            else ImgFoto.Source = null;
        }

        private void AsignarComboBox(ComboBox combo, string valor)
        {
            if (string.IsNullOrEmpty(valor)) return;
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Content.ToString() == valor || (valor.Contains("Precursor") && item.Content.ToString().Contains(valor)))
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private void CmbSexo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RefrescarListaConyugesYVincular(); }

        private void CmbCongregacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && CmbCongregacion.SelectedValue != null)
            {
                PersonaActualizada.CongregacionCodigo = CmbCongregacion.SelectedValue.ToString();
                RefrescarListaConyugesYVincular();
            }
        }

        private void RefrescarListaConyugesYVincular()
        {
            string sexoActual = (CmbSexo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            string sexoOpuesto = sexoActual == "Hombre" ? "Mujer" : (sexoActual == "Mujer" ? "Hombre" : "");

            var personasCongre = DatabaseHelper.ObtenerPersonasPorCongregacion(PersonaActualizada.CongregacionCodigo);

            var candidatosConyuges = personasCongre.Where(p => p.Id != PersonaActualizada.Id && (string.IsNullOrEmpty(sexoOpuesto) || p.Sexo == sexoOpuesto)).ToList();
            candidatosConyuges.Insert(0, new Persona { Id = 0, NombreComun = "-- Ninguno --", Nombre = "-- Ninguno --" });

            CmbConyuge.ItemsSource = candidatosConyuges;
            CmbConyuge.SelectedValue = PersonaActualizada.ConyugeId;

            if (_candidatosPredefinidos != null && _candidatosPredefinidos.Count > 0)
            {
                _listaVincularCompleta = _candidatosPredefinidos.Where(p => p.Id != PersonaActualizada.Id).ToList();
            }
            else
            {
                _listaVincularCompleta = personasCongre.Where(p => p.Id != PersonaActualizada.Id).ToList();
            }

            CmbVincular.ItemsSource = _listaVincularCompleta;
        }

        private void CmbVincular_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = e.OriginalSource as TextBox;
            if (tb != null)
            {
                if (tb.IsFocused) CmbVincular.IsDropDownOpen = true;

                string search = tb.Text.ToLower();
                if (string.IsNullOrEmpty(search)) CmbVincular.ItemsSource = _listaVincularCompleta;
                else CmbVincular.ItemsSource = _listaVincularCompleta.Where(p => p.NombreMostrarTabla.ToLower().Contains(search)).ToList();
            }
        }

        private void LimpiarSelectsDinamicos()
        {
            var textboxes = new TextBox[] { TxtNombre, TxtNombreComun, TxtNombreJW, TxtEmail, TxtEmailJW, TxtTelefono, TxtDireccion, TxtGrupo, TxtFechaNac, TxtFechaBaut, TxtInicioPrec, TxtEscuela };
            foreach (var txt in textboxes)
            {
                txt.Visibility = Visibility.Visible;
                Panel parent = txt.Parent as Panel;
                if (parent != null)
                {
                    var cmbExistente = parent.Children.OfType<ComboBox>().FirstOrDefault(c => c.Name == "CmbConflict_" + txt.Name);
                    if (cmbExistente != null) parent.Children.Remove(cmbExistente);
                }
            }

            var combos = new[] { CmbSexo, CmbEsperanza, CmbNombramiento, CmbServicio };
            foreach (var cmb in combos)
            {
                cmb.BorderBrush = null;
                cmb.BorderThickness = new Thickness(0);
                cmb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526"));
                cmb.ToolTip = null;
            }

            PanelFotoNormal.Visibility = Visibility.Visible;
            PanelFotoConflicto.Visibility = Visibility.Collapsed;
            TxtAdvertenciaConflictos.Visibility = Visibility.Collapsed;
            TxtPrivilegios.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
            foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>()) chk.Foreground = Brushes.White;
        }

        private string ResolverConflictoTexto(TextBox txtOriginal, string valActual, string valDuplicado, string nombreCampoVisible)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            // Solo creamos conflicto si JW Hub aporta un dato y es DIFERENTE al actual
            if (v1.ToLower() != v2.ToLower() && !string.IsNullOrEmpty(v2))
            {
                Panel parent = txtOriginal.Parent as Panel;
                if (parent == null) return null;

                txtOriginal.Visibility = Visibility.Collapsed;

                ComboBox cmb = new ComboBox
                {
                    Name = "CmbConflict_" + txtOriginal.Name,
                    IsEditable = true,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00")),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    Margin = txtOriginal.Margin,
                    ToolTip = "Dato en conflicto: JW Hub vs BBDD"
                };

                if (!string.IsNullOrEmpty(v1)) cmb.Items.Add(v1);
                cmb.Items.Add(v2);

                // Priorizamos el valor nuevo (Hub)
                cmb.SelectedItem = v2;

                cmb.SelectionChanged += (s, e) => { if (cmb.SelectedItem != null) txtOriginal.Text = cmb.SelectedItem.ToString(); };
                cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => { txtOriginal.Text = cmb.Text; }));

                txtOriginal.Text = cmb.SelectedItem?.ToString() ?? "";

                int index = parent.Children.IndexOf(txtOriginal);
                parent.Children.Insert(index + 1, cmb);
                return nombreCampoVisible;
            }
            return null;
        }

        private string ResolverConflictoDireccion(TextBox txtOriginal, string valActual, string valDuplicado, Persona pFusion)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            if (v1.ToLower() != v2.ToLower() && !string.IsNullOrEmpty(v2))
            {
                Panel parent = txtOriginal.Parent as Panel;
                if (parent == null) return null;

                txtOriginal.Visibility = Visibility.Collapsed;

                ComboBox cmb = new ComboBox
                {
                    Name = "CmbConflict_" + txtOriginal.Name,
                    IsEditable = true,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00")),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    Margin = txtOriginal.Margin,
                    ToolTip = "Elige la dirección correcta"
                };

                if (!string.IsNullOrEmpty(v1)) cmb.Items.Add(v1);
                cmb.Items.Add(v2);

                cmb.SelectedItem = v2;
                _latitudActual = pFusion.Latitud;
                _longitudActual = pFusion.Longitud;

                cmb.SelectionChanged += (s, e) => {
                    if (cmb.SelectedItem != null)
                    {
                        txtOriginal.Text = cmb.SelectedItem.ToString();
                        if (cmb.SelectedItem.ToString() == v2)
                        {
                            _latitudActual = pFusion.Latitud;
                            _longitudActual = pFusion.Longitud;
                        }
                    }
                };
                cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => { txtOriginal.Text = cmb.Text; }));

                txtOriginal.Text = cmb.SelectedItem?.ToString() ?? "";

                int index = parent.Children.IndexOf(txtOriginal);
                parent.Children.Insert(index + 1, cmb);
                return "Dirección";
            }
            return null;
        }

        private string ResolverConflictoCombo(ComboBox cmbOriginal, string valActual, string valDuplicado, string nombreCampoVisible)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            if (v1.ToLower() != v2.ToLower() && !string.IsNullOrEmpty(v2))
            {
                cmbOriginal.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900"));
                cmbOriginal.BorderThickness = new Thickness(1);
                cmbOriginal.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00"));
                cmbOriginal.ToolTip = $"El valor antiguo era: {v1}\nNuevo detectado: {v2}";

                AsignarComboBox(cmbOriginal, v2);
                return nombreCampoVisible;
            }
            return null;
        }

        private List<string> EvaluarConflictos(Persona pFusion)
        {
            List<string> camposConflictos = new List<string>();

            string rNom = ResolverConflictoTexto(TxtNombre, TxtNombre.Text, pFusion.Nombre, "Nombre Completo"); if (rNom != null) camposConflictos.Add(rNom);
            string rNomComun = ResolverConflictoTexto(TxtNombreComun, TxtNombreComun.Text, pFusion.NombreComun, "Nombre Común"); if (rNomComun != null) camposConflictos.Add(rNomComun);
            string rNomJW = ResolverConflictoTexto(TxtNombreJW, TxtNombreJW.Text, pFusion.NombreJWHub, "Nombre JW Hub"); if (rNomJW != null) camposConflictos.Add(rNomJW);

            string rEm = ResolverConflictoTexto(TxtEmail, TxtEmail.Text, pFusion.Email, "Email"); if (rEm != null) camposConflictos.Add(rEm);
            string rEmJw = ResolverConflictoTexto(TxtEmailJW, TxtEmailJW.Text, pFusion.EmailJW, "Email JW"); if (rEmJw != null) camposConflictos.Add(rEmJw);
            string rTel = ResolverConflictoTexto(TxtTelefono, TxtTelefono.Text, pFusion.Telefono, "Teléfono"); if (rTel != null) camposConflictos.Add(rTel);
            string rGrp = ResolverConflictoTexto(TxtGrupo, TxtGrupo.Text, pFusion.Grupo > 0 ? pFusion.Grupo.ToString() : "", "Grupo"); if (rGrp != null) camposConflictos.Add(rGrp);

            string rDir = ResolverConflictoDireccion(TxtDireccion, TxtDireccion.Text, pFusion.Direccion, pFusion); if (rDir != null) camposConflictos.Add(rDir);

            string rFnac = ResolverConflictoTexto(TxtFechaNac, TxtFechaNac.Text, pFusion.FechaNacimiento, "Fecha de Nacimiento"); if (rFnac != null) camposConflictos.Add(rFnac);
            string rFbaut = ResolverConflictoTexto(TxtFechaBaut, TxtFechaBaut.Text, pFusion.FechaBautismo, "Fecha de Bautismo"); if (rFbaut != null) camposConflictos.Add(rFbaut);
            string rFprec = ResolverConflictoTexto(TxtInicioPrec, TxtInicioPrec.Text, pFusion.FechaInicioPrec, "Inicio Precursorado"); if (rFprec != null) camposConflictos.Add(rFprec);
            string rFesc = ResolverConflictoTexto(TxtEscuela, TxtEscuela.Text, pFusion.FechaUltimaEscuela, "Fecha Escuela"); if (rFesc != null) camposConflictos.Add(rFesc);

            string cSex = ResolverConflictoCombo(CmbSexo, (CmbSexo.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion.Sexo, "Sexo"); if (cSex != null) camposConflictos.Add(cSex);
            string cEsp = ResolverConflictoCombo(CmbEsperanza, (CmbEsperanza.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion.Esperanza, "Esperanza"); if (cEsp != null) camposConflictos.Add(cEsp);
            string cNom = ResolverConflictoCombo(CmbNombramiento, (CmbNombramiento.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion.Nombramiento, "Nombramiento"); if (cNom != null) camposConflictos.Add(cNom);
            string cSer = ResolverConflictoCombo(CmbServicio, (CmbServicio.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion.Servicio, "Servicio"); if (cSer != null) camposConflictos.Add(cSer);

            if (!string.IsNullOrWhiteSpace(pFusion.Foto) && _base64FotoModificada != pFusion.Foto)
            {
                camposConflictos.Add("Fotografía");
                PanelFotoNormal.Visibility = Visibility.Collapsed;
                PanelFotoConflicto.Visibility = Visibility.Visible;

                _base64FotoFusion = pFusion.Foto;
                ImgFotoConflictoActual.Source = ConvertirABitmap(_base64FotoModificada);
                ImgFotoConflictoDuplicada.Source = ConvertirABitmap(_base64FotoFusion);
                RdoFotoDuplicada.IsChecked = true;
            }

            if (!string.IsNullOrWhiteSpace(pFusion.Privilegios))
            {
                if (string.IsNullOrWhiteSpace(TxtPrivilegios.Text) || !TxtPrivilegios.Text.Contains(pFusion.Privilegios))
                {
                    TxtPrivilegios.Text = string.IsNullOrWhiteSpace(TxtPrivilegios.Text) ? pFusion.Privilegios : TxtPrivilegios.Text + "\n" + pFusion.Privilegios;
                    TxtPrivilegios.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00"));
                    camposConflictos.Add("Privilegios");
                }
            }

            // === LÓGICA BLINDADA DE SOLICITUDES ===
            if (!string.IsNullOrWhiteSpace(pFusion.Solicitudes) || !string.IsNullOrWhiteSpace(PersonaActualizada.Solicitudes))
            {
                var solsHub = (pFusion.Solicitudes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                var solsBD = (PersonaActualizada.Solicitudes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

                bool cambioSols = false;
                foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>())
                {
                    string tag = chk.Tag?.ToString() ?? "";
                    bool debeEstarMarcado = solsHub.Contains(tag);
                    bool estabaMarcado = solsBD.Contains(tag);

                    if (debeEstarMarcado != estabaMarcado)
                    {
                        chk.IsChecked = debeEstarMarcado;
                        chk.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900"));
                        cambioSols = true;
                    }
                    else
                    {
                        chk.IsChecked = estabaMarcado;
                    }
                }

                if (cambioSols) camposConflictos.Add("Solicitudes");
            }

            return camposConflictos;
        }

        private void CmbVincular_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LimpiarSelectsDinamicos();

            if (CmbVincular.SelectedItem is Persona pFusion)
            {
                var conflictos = EvaluarConflictos(pFusion);
                if (conflictos.Count > 0) TxtAdvertenciaConflictos.Visibility = Visibility.Visible;
            }
        }

        private void TxtDireccion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtDireccion.IsFocused) return;
            string query = TxtDireccion.Text.Trim();

            _latitudActual = 0;
            _longitudActual = 0;

            if (query.Length < 4) { PopupDirecciones.IsOpen = false; return; }

            _debounceMapas.Stop();
            _debounceMapas.Start();
        }

        private async void BuscadorMapa_Tick(object sender, EventArgs e)
        {
            _debounceMapas.Stop();
            string query = TxtDireccion.Text.Trim();

            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&addressdetails=1&limit=5&countrycodes=es";
                string response = await _httpClient.GetStringAsync(url);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var resultados = JsonSerializer.Deserialize<List<NominatimResult>>(response, options);

                if (resultados != null && resultados.Count > 0)
                {
                    ListaDirecciones.ItemsSource = resultados;
                    PopupDirecciones.IsOpen = true;
                }
                else PopupDirecciones.IsOpen = false;
            }
            catch { PopupDirecciones.IsOpen = false; }
        }

        private void ListaDirecciones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListaDirecciones.SelectedItem is NominatimResult res)
            {
                TxtDireccion.Text = res.display_name;
                if (double.TryParse(res.lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lt)) _latitudActual = lt;
                if (double.TryParse(res.lon, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lg)) _longitudActual = lg;
                PopupDirecciones.IsOpen = false;
            }
        }

        public class NominatimResult
        {
            public string display_name { get; set; }
            public string lat { get; set; }
            public string lon { get; set; }
        }

        private void BtnToggleVincular_Click(object sender, RoutedEventArgs e)
        {
            PanelVincular.Visibility = PanelVincular.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnCambiarFoto_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Archivos de Imagen|*.jpg;*.jpeg;*.png;*.webp" };
            if (ofd.ShowDialog() == true)
            {
                byte[] imageArray = File.ReadAllBytes(ofd.FileName);
                _base64FotoModificada = Convert.ToBase64String(imageArray);
                var image = new BitmapImage(new Uri(ofd.FileName));
                ImgFoto.Source = image;
            }
        }

        private void BtnBorrarFoto_Click(object sender, RoutedEventArgs e)
        {
            _base64FotoModificada = "";
            ImgFoto.Source = null;
        }

        private void GuardarCamposEnObjeto()
        {
            PersonaActualizada.Nombre = TxtNombre.Text;
            PersonaActualizada.NombreComun = TxtNombreComun.Text;
            PersonaActualizada.NombreJWHub = TxtNombreJW.Text;
            PersonaActualizada.Email = TxtEmail.Text;
            PersonaActualizada.EmailJW = TxtEmailJW.Text;
            PersonaActualizada.Telefono = TxtTelefono.Text;

            PersonaActualizada.Direccion = TxtDireccion.Text.Trim();
            if (PersonaActualizada.Direccion != _direccionInicial)
            {
                _latitudActual = 0;
                _longitudActual = 0;
            }
            PersonaActualizada.Latitud = _latitudActual;
            PersonaActualizada.Longitud = _longitudActual;

            int grp; int.TryParse(TxtGrupo.Text, out grp);
            PersonaActualizada.Grupo = grp;

            PersonaActualizada.FechaNacimiento = TxtFechaNac.Text;
            PersonaActualizada.FechaBautismo = TxtFechaBaut.Text;
            PersonaActualizada.FechaInicioPrec = TxtInicioPrec.Text;
            PersonaActualizada.FechaUltimaEscuela = TxtEscuela.Text;

            PersonaActualizada.Sexo = (CmbSexo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Esperanza = (CmbEsperanza.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Nombramiento = (CmbNombramiento.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            PersonaActualizada.Servicio = (CmbServicio.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";

            PersonaActualizada.CongregacionCodigo = CmbCongregacion.SelectedValue?.ToString() ?? PersonaActualizada.CongregacionCodigo;

            PersonaActualizada.ConyugeId = CmbConyuge.SelectedValue != null ? (int)CmbConyuge.SelectedValue : 0;
            PersonaActualizada.Privilegios = TxtPrivilegios.Text.Trim();

            List<string> sols = new List<string>();
            foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>())
            {
                if (chk.IsChecked == true) sols.Add(chk.Tag.ToString());
            }
            PersonaActualizada.Solicitudes = string.Join(", ", sols);

            if (PanelFotoConflicto.Visibility == Visibility.Visible && RdoFotoDuplicada.IsChecked == true)
            {
                PersonaActualizada.Foto = _base64FotoFusion;
            }
            else
            {
                PersonaActualizada.Foto = _base64FotoModificada;
            }
        }

        private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_latitudActual == 0 && _longitudActual == 0 && !string.IsNullOrWhiteSpace(TxtDireccion.Text))
            {
                try
                {
                    string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(TxtDireccion.Text.Trim())}&format=json&limit=1&countrycodes=es";
                    string response = await _httpClient.GetStringAsync(url);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var resultados = JsonSerializer.Deserialize<List<NominatimResult>>(response, options);

                    if (resultados != null && resultados.Count > 0)
                    {
                        if (double.TryParse(resultados[0].lat, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat)) _latitudActual = lat;
                        if (double.TryParse(resultados[0].lon, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon)) _longitudActual = lon;
                    }
                }
                catch { }
            }

            GuardarCamposEnObjeto();

            Persona itemSeleccionado = null;

            if (PanelVincular.Visibility == Visibility.Visible)
            {
                if (CmbVincular.SelectedItem is Persona pSeleccionada)
                {
                    itemSeleccionado = pSeleccionada;
                }
                else if (!string.IsNullOrWhiteSpace(CmbVincular.Text))
                {
                    string textoEscrito = CmbVincular.Text.Trim().ToLower();
                    itemSeleccionado = _listaVincularCompleta?.FirstOrDefault(p =>
                        p.NombreMostrarTabla != null &&
                        p.NombreMostrarTabla.Trim().ToLower() == textoEscrito);
                }
            }

            if (itemSeleccionado != null)
            {
                if (itemSeleccionado.Id == PersonaActualizada.Id)
                {
                    MessageBox.Show("No puedes fusionar a una persona consigo misma.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (MessageBox.Show($"Vas a fusionar e importar el historial de {itemSeleccionado.NombreMostrarTabla} a la ficha actual.\n\nTras la fusión, el perfil antiguo de {itemSeleccionado.NombreMostrarTabla} SERÁ BORRADO.\n\n¿Quieres continuar?", "Confirmar Fusión", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        DatabaseHelper.FusionarYBorrarDefinitivo(PersonaActualizada, itemSeleccionado.Id);

                        _esUnaFusion = true;
                        MessageBox.Show("¡Fusión completada con éxito! La persona duplicada ha sido eliminada.", "Terminado", MessageBoxButton.OK, MessageBoxImage.Information);

                        this.DialogResult = true;
                        this.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error en la base de datos al fusionar:\n\n{ex.Message}", "Transacción Fallida", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                return;
            }

            _esUnaFusion = false;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }

        public bool EsUnaFusion() { return _esUnaFusion; }
    }
}