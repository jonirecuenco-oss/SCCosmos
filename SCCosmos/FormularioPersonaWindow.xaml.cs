using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.Win32;
using System.Net.Http;
using System.Windows.Threading;
using System.Text.Json;
using System.Text;
using System.Globalization;
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
        private ObservableCollection<ActividadEditable> _actividadEditada = new ObservableCollection<ActividadEditable>();
        private readonly List<ClaveActividad> _actividadEliminada = new List<ClaveActividad>();

        // Evita que el ComboBox editable pierda la selección al refrescar su ItemsSource.
        private bool _actualizandoSelectorVincular = false;
        private Persona _personaVinculadaSeleccionada = null;
        private string _nombreReferenciaVinculacion = "";

        // Contexto de la sincronización que abrió el formulario. Se usa para respetar
        // qué fuente es autoritativa para cada uno de los tres campos de nombre.
        private string _origenSincronizacion = "";
        private readonly string _nombreCompletoOriginal;
        private readonly string _nombreComunOriginal;
        private readonly string _nombreJWHubOriginal;
        private readonly bool _teniaOrigenS21;
        private readonly bool _teniaOrigenJWHub;

        private sealed class ActividadEditable
        {
            public string Mes { get; set; } = "";
            public string AnioServicio { get; set; } = "";
            public bool Participacion { get; set; }
            public string CursosBiblicos { get; set; } = "";
            public bool PrecursorAuxiliar { get; set; }
            public string Horas { get; set; } = "";
            public string Notas { get; set; } = "";

            // Clave con la que el registro existe actualmente en la BBDD.
            // Permite cambiar Mes/Año sin dejar detrás el registro antiguo.
            public string MesOriginal { get; set; } = "";
            public string AnioServicioOriginal { get; set; } = "";
            public bool EsNuevo { get; set; }
        }

        private sealed class ClaveActividad
        {
            public string Mes { get; set; } = "";
            public string AnioServicio { get; set; } = "";
        }

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
                EmailJW = personaInicial.EmailJW,
                Notas = personaInicial.Notas,
                RequisitoHoras = personaInicial.RequisitoHoras,
                FechaCreacion = personaInicial.FechaCreacion,
                FechaEliminacion = personaInicial.FechaEliminacion,
                PersonaRaizId = personaInicial.PersonaRaizId,
                MesHistorico = personaInicial.MesHistorico,
                FechaExactaHistorico = personaInicial.FechaExactaHistorico,
                CongregacionNombre = personaInicial.CongregacionNombre,
                AnioNacimiento = personaInicial.AnioNacimiento,
                OrigenRegistro = personaInicial.OrigenRegistro,
                AprobadoExhibidores = personaInicial.AprobadoExhibidores
            };

            _nombreCompletoOriginal = personaInicial.Nombre ?? "";
            _nombreComunOriginal = personaInicial.NombreComun ?? "";
            _nombreJWHubOriginal = personaInicial.NombreJWHub ?? "";
            _teniaOrigenS21 = personaInicial.TieneOrigenS21;
            _teniaOrigenJWHub = personaInicial.TieneOrigenJWHub;

            _nombreReferenciaVinculacion = !string.IsNullOrWhiteSpace(personaInicial.NombreJWHub)
                ? personaInicial.NombreJWHub
                : (personaInicial.Nombre ?? "");

            _base64FotoModificada = PersonaActualizada.Foto;
            _latitudActual = PersonaActualizada.Latitud;
            _longitudActual = PersonaActualizada.Longitud;
            _direccionInicial = PersonaActualizada.Direccion ?? "";

            CargarDatosEnInterfaz();
            RefrescarListaConyugesYVincular();
            CargarActividadEnInterfaz();
        }

        public void ConfigurarOrigenSincronizacion(string origen)
        {
            _origenSincronizacion = origen?.Trim() ?? "";

            if (EsSincronizacionS21())
                _nombreReferenciaVinculacion = PersonaActualizada.Nombre ?? "";
            else if (EsSincronizacionHub())
                _nombreReferenciaVinculacion = !string.IsNullOrWhiteSpace(PersonaActualizada.NombreJWHub)
                    ? PersonaActualizada.NombreJWHub
                    : (PersonaActualizada.Nombre ?? "");

            RefrescarListaConyugesYVincular();
        }

        private bool EsSincronizacionS21() =>
            string.Equals(_origenSincronizacion, "S21", StringComparison.OrdinalIgnoreCase);

        private bool EsSincronizacionHub() =>
            string.Equals(_origenSincronizacion, "JWHub", StringComparison.OrdinalIgnoreCase);

        public void CargarConflictosDesdeHub(Persona pHub)
        {
            if (pHub == null) return;
            _origenSincronizacion = "JWHub";

            _nombreReferenciaVinculacion = !string.IsNullOrWhiteSpace(pHub.NombreJWHub)
                ? pHub.NombreJWHub
                : (pHub.Nombre ?? "");

            LimpiarSelectsDinamicos();
            var camposCambiados = EvaluarConflictos(pHub, soloCamposS21: false);
            RefrescarListaConyugesYVincular();

            if (camposCambiados.Count > 0)
            {
                TxtAdvertenciaConflictos.Visibility = Visibility.Visible;
            }
        }

        // El S-21 es la fuente preferente del Nombre completo. No propone cambios
        // sobre NombreComun ni sobre NombreJWHub.
        public void CargarConflictosDesdeJson(Persona pJson)
        {
            if (pJson == null) return;
            _origenSincronizacion = "S21";

            // El nombre del S-21 es la referencia para ordenar las posibles vinculaciones.
            _nombreReferenciaVinculacion = !string.IsNullOrWhiteSpace(pJson.Nombre)
                ? pJson.Nombre
                : (pJson.NombreJWHub ?? "");

            LimpiarSelectsDinamicos();
            var camposCambiados = EvaluarConflictos(pJson, soloCamposS21: true);
            RefrescarListaConyugesYVincular();

            if (camposCambiados.Count > 0)
            {
                TxtAdvertenciaConflictos.Visibility = Visibility.Visible;
            }
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
            TxtNotas.Text = PersonaActualizada.Notas;

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
            ChkAprobadoExhibidores.IsChecked = PersonaActualizada.AprobadoExhibidores;

            // La actividad se carga desde ActividadPersonas en CargarActividadEnInterfaz().
            // El S-21 es la fuente de estos registros, pero aquí pueden corregirse manualmente.

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
            if (string.IsNullOrWhiteSpace(valor))
            {
                var itemVacio = combo.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Content?.ToString(), "(Vacío)", StringComparison.OrdinalIgnoreCase));

                if (itemVacio != null) combo.SelectedItem = itemVacio;
                return;
            }

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

            int idVinculado = _personaVinculadaSeleccionada?.Id
                ?? (CmbVincular.SelectedItem as Persona)?.Id
                ?? 0;

            if (_candidatosPredefinidos != null && _candidatosPredefinidos.Count > 0)
            {
                _listaVincularCompleta = _candidatosPredefinidos.Where(p => p.Id != PersonaActualizada.Id).ToList();
            }
            else
            {
                _listaVincularCompleta = personasCongre.Where(p => p.Id != PersonaActualizada.Id).ToList();
            }

            OrdenarCandidatosVinculacionPorParecido();

            _actualizandoSelectorVincular = true;
            try
            {
                CmbVincular.ItemsSource = _listaVincularCompleta;

                if (idVinculado > 0)
                {
                    var seleccion = _listaVincularCompleta.FirstOrDefault(p => p.Id == idVinculado);
                    if (seleccion != null)
                    {
                        _personaVinculadaSeleccionada = seleccion;
                        CmbVincular.SelectedItem = seleccion;
                        CmbVincular.Text = seleccion.NombreMostrarTabla;
                    }
                }
            }
            finally
            {
                _actualizandoSelectorVincular = false;
            }
        }

        private void CmbVincular_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_actualizandoSelectorVincular) return;

            var tb = e.OriginalSource as TextBox;
            if (tb == null) return;

            string texto = tb.Text ?? "";

            // Cuando WPF escribe el texto del elemento seleccionado en la parte editable,
            // no debemos reemplazar ItemsSource: hacerlo borraba SelectedItem.
            if (CmbVincular.SelectedItem is Persona seleccionada &&
                TextoCorrespondeACandidato(texto, seleccionada))
            {
                _personaVinculadaSeleccionada = seleccionada;
                return;
            }

            // Algunas versiones/estilos de ComboBox editable disparan TextChanged antes de
            // dejar asentado SelectedItem. Si el texto coincide exactamente con una persona,
            // restauramos la selección de forma explícita.
            var coincidenciaExacta = _listaVincularCompleta?.FirstOrDefault(p =>
                TextoCorrespondeACandidato(texto, p));

            if (coincidenciaExacta != null && !string.IsNullOrWhiteSpace(texto))
            {
                _actualizandoSelectorVincular = true;
                try
                {
                    CmbVincular.ItemsSource = _listaVincularCompleta;
                    CmbVincular.SelectedItem = coincidenciaExacta;
                    CmbVincular.Text = coincidenciaExacta.NombreMostrarTabla;
                    _personaVinculadaSeleccionada = coincidenciaExacta;
                }
                finally
                {
                    _actualizandoSelectorVincular = false;
                }

                AplicarPersonaVinculada(coincidenciaExacta);
                return;
            }

            _actualizandoSelectorVincular = true;
            try
            {
                // Si el usuario vuelve a escribir, abandona la selección anterior y filtra.
                _personaVinculadaSeleccionada = null;
                CmbVincular.SelectedItem = null;

                string search = texto.Trim();
                CmbVincular.ItemsSource = string.IsNullOrEmpty(search)
                    ? _listaVincularCompleta
                    : _listaVincularCompleta
                        .Where(p => (p.NombreMostrarTabla ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                CmbVincular.Text = texto;
                if (tb.IsFocused) CmbVincular.IsDropDownOpen = true;
            }
            finally
            {
                _actualizandoSelectorVincular = false;
            }
        }

        private bool TextoCorrespondeACandidato(string texto, Persona persona)
        {
            if (persona == null) return false;

            string buscado = (texto ?? "").Trim();
            if (string.IsNullOrEmpty(buscado)) return false;

            return string.Equals(buscado, persona.NombreMostrarTabla?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buscado, persona.NombreMostrarVinculacion?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void OrdenarCandidatosVinculacionPorParecido()
        {
            if (_listaVincularCompleta == null) return;

            string referencia = _nombreReferenciaVinculacion;
            if (string.IsNullOrWhiteSpace(referencia))
            {
                referencia = !string.IsNullOrWhiteSpace(PersonaActualizada?.NombreJWHub)
                    ? PersonaActualizada.NombreJWHub
                    : PersonaActualizada?.Nombre;
            }

            foreach (var persona in _listaVincularCompleta)
            {
                persona.CoincidenciaVinculacion = CalcularMejorCoincidenciaNombre(referencia, persona);
            }

            _listaVincularCompleta = _listaVincularCompleta
                .OrderByDescending(p => p.CoincidenciaVinculacion)
                .ThenBy(p => p.NombreMostrarTabla ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private int CalcularMejorCoincidenciaNombre(string referencia, Persona candidato)
        {
            if (candidato == null || string.IsNullOrWhiteSpace(referencia)) return 0;

            var nombres = new[]
            {
                candidato.Nombre,
                candidato.NombreComun,
                candidato.NombreJWHub
            }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            int mejor = 0;
            foreach (string nombre in nombres)
            {
                mejor = Math.Max(mejor, CalcularSimilitudNombre(referencia, nombre));
            }

            return mejor;
        }

        private int CalcularSimilitudNombre(string a, string b)
        {
            string na = NormalizarNombreParaComparacion(a);
            string nb = NormalizarNombreParaComparacion(b);

            if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb)) return 0;
            if (na == nb) return 100;

            double directa = SimilitudLevenshtein(na, nb);

            string tokensA = string.Join(" ", na.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x));
            string tokensB = string.Join(" ", nb.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x));
            double porTokensOrdenados = SimilitudLevenshtein(tokensA, tokensB);

            var setA = new HashSet<string>(na.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var setB = new HashSet<string>(nb.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int union = setA.Union(setB).Count();
            int interseccion = setA.Intersect(setB).Count();
            double coincidenciaTokens = union == 0 ? 0 : (double)interseccion / union;

            double mejor = Math.Max(directa, Math.Max(porTokensOrdenados, coincidenciaTokens));
            return Math.Max(0, Math.Min(100, (int)Math.Round(mejor * 100)));
        }

        private string NormalizarNombreParaComparacion(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            string descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            bool ultimoEraEspacio = false;

            foreach (char c in descompuesto)
            {
                UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    ultimoEraEspacio = false;
                }
                else if (!ultimoEraEspacio && sb.Length > 0)
                {
                    sb.Append(' ');
                    ultimoEraEspacio = true;
                }
            }

            return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
        }

        private double SimilitudLevenshtein(string a, string b)
        {
            if (a == b) return 1.0;
            if (a.Length == 0 || b.Length == 0) return 0.0;

            int[] anterior = new int[b.Length + 1];
            int[] actual = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) anterior[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                actual[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int coste = a[i - 1] == b[j - 1] ? 0 : 1;
                    actual[j] = Math.Min(
                        Math.Min(actual[j - 1] + 1, anterior[j] + 1),
                        anterior[j - 1] + coste);
                }

                var temp = anterior;
                anterior = actual;
                actual = temp;
            }

            int distancia = anterior[b.Length];
            int maximo = Math.Max(a.Length, b.Length);
            return 1.0 - ((double)distancia / maximo);
        }

        private void LimpiarSelectsDinamicos()
        {
            var textboxes = new TextBox[] { TxtNombre, TxtNombreComun, TxtNombreJW, TxtEmail, TxtEmailJW, TxtTelefono, TxtDireccion, TxtGrupo, TxtFechaNac, TxtFechaBaut, TxtInicioPrec, TxtEscuela, TxtNotas, TxtPrivilegios };
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
            TxtPrivilegios.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526"));
            foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>()) chk.Foreground = Brushes.White;
        }

        private string ResolverConflictoTexto(TextBox txtOriginal, string valActual, string valDuplicado, string nombreCampoVisible, bool distinguirMayusculas = true, bool seleccionarSegundoPorDefecto = true)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            // Los conflictos de texto son deliberadamente sensibles a mayúsculas/minúsculas.
            // El parámetro se conserva por compatibilidad con llamadas existentes.
            bool sonDistintos = !string.Equals(v1, v2, StringComparison.Ordinal);

            if (sonDistintos)
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

                string opt1 = string.IsNullOrEmpty(v1) ? "(Vacío)" : v1;
                string opt2 = string.IsNullOrEmpty(v2) ? "(Vacío)" : v2;

                cmb.Items.Add(opt1);
                if (opt1 != opt2) cmb.Items.Add(opt2);

                // Normalmente el segundo valor es el dato entrante y se preselecciona.
                // En una vinculación S-21 concreta podemos preferir el valor que ya está
                // en el formulario (el JSON), manteniendo ambas opciones disponibles.
                cmb.SelectedItem = seleccionarSegundoPorDefecto ? opt2 : opt1;

                cmb.SelectionChanged += (s, e) => {
                    if (cmb.SelectedItem != null)
                        txtOriginal.Text = cmb.SelectedItem.ToString() == "(Vacío)" ? "" : cmb.SelectedItem.ToString();
                };

                cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => {
                    txtOriginal.Text = cmb.Text == "(Vacío)" ? "" : cmb.Text;
                }));

                txtOriginal.Text = cmb.SelectedItem?.ToString() == "(Vacío)" ? "" : (cmb.SelectedItem?.ToString() ?? "");

                int index = parent.Children.IndexOf(txtOriginal);
                parent.Children.Insert(index + 1, cmb);
                return nombreCampoVisible;
            }
            return null;
        }

        // ========================================================
        // NUEVO: LÓGICA DE CONFLICTOS PARA PRIVILEGIOS
        // ========================================================
        private string FormatearPrivilegiosParaSelector(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return "(sin privilegios)";

            return string.Join(" · ", valor
                .Replace("\r", "")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private string CombinarPrivilegios(string actual, string nuevo)
        {
            var resultado = new List<string>();

            foreach (string valor in new[] { actual ?? "", nuevo ?? "" })
            {
                foreach (string item in valor.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string limpio = item.Trim();
                    if (!string.IsNullOrWhiteSpace(limpio) &&
                        !resultado.Any(x => string.Equals(x, limpio, StringComparison.OrdinalIgnoreCase)))
                    {
                        resultado.Add(limpio);
                    }
                }
            }

            return string.Join("\n", resultado);
        }

        private ComboBoxItem CrearOpcionPrivilegios(string etiqueta, string valorVisible, string valorGuardar)
        {
            return new ComboBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"{etiqueta}: {FormatearPrivilegiosParaSelector(valorVisible)}",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 620
                },
                Tag = valorGuardar ?? ""
            };
        }

        private string ResolverConflictoPrivilegios(TextBox txtOriginal, string valActual, string valDuplicado)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            if (!string.Equals(v1, v2, StringComparison.Ordinal))
            {
                Panel parent = txtOriginal.Parent as Panel;
                if (parent == null) return null;

                txtOriginal.Visibility = Visibility.Collapsed;

                ComboBox cmb = new ComboBox
                {
                    Name = "CmbConflict_" + txtOriginal.Name,
                    IsEditable = false,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00")),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    Margin = txtOriginal.Margin,
                    MaxDropDownHeight = 360,
                    ToolTip = "Dato en conflicto. Se muestran los valores actual y nuevo para que puedas escoger."
                };

                string combinado = CombinarPrivilegios(v1, v2);

                var optActual = CrearOpcionPrivilegios("Conservar actual", v1, v1);
                var optNuevo = CrearOpcionPrivilegios("Usar nuevo", v2, v2);
                var optComb = CrearOpcionPrivilegios("Combinar ambos", combinado, combinado);

                cmb.Items.Add(optComb);
                cmb.Items.Add(optActual);
                cmb.Items.Add(optNuevo);
                cmb.SelectedItem = optComb;

                cmb.SelectionChanged += (s, e) =>
                {
                    if (cmb.SelectedItem is ComboBoxItem item)
                        txtOriginal.Text = item.Tag?.ToString() ?? "";
                };

                txtOriginal.Text = combinado;

                int index = parent.Children.IndexOf(txtOriginal);
                parent.Children.Insert(index + 1, cmb);
                return "Privilegios";
            }

            return null;
        }

        private string ResolverConflictoDireccion(TextBox txtOriginal, string valActual, string valDuplicado, Persona pFusion)
        {
            string v1 = valActual?.Trim() ?? "";
            string v2 = valDuplicado?.Trim() ?? "";

            if (!string.Equals(v1, v2, StringComparison.Ordinal))
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

                string opt1 = string.IsNullOrEmpty(v1) ? "(Vacío)" : v1;
                string opt2 = string.IsNullOrEmpty(v2) ? "(Vacío)" : v2;

                cmb.Items.Add(opt1);
                if (opt1 != opt2) cmb.Items.Add(opt2);

                cmb.SelectedItem = opt2;
                _latitudActual = pFusion.Latitud;
                _longitudActual = pFusion.Longitud;

                cmb.SelectionChanged += (s, e) => {
                    if (cmb.SelectedItem != null)
                    {
                        string sel = cmb.SelectedItem.ToString();
                        txtOriginal.Text = sel == "(Vacío)" ? "" : sel;
                        if (sel == opt2)
                        {
                            _latitudActual = pFusion.Latitud;
                            _longitudActual = pFusion.Longitud;
                        }
                    }
                };

                cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => {
                    txtOriginal.Text = cmb.Text == "(Vacío)" ? "" : cmb.Text;
                }));

                txtOriginal.Text = cmb.SelectedItem?.ToString() == "(Vacío)" ? "" : (cmb.SelectedItem?.ToString() ?? "");

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

            if (!string.Equals(v1, v2, StringComparison.Ordinal))
            {
                cmbOriginal.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB900"));
                cmbOriginal.BorderThickness = new Thickness(1);
                cmbOriginal.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332b00"));

                string textoV1 = string.IsNullOrEmpty(v1) ? "(Vacío)" : v1;
                string textoV2 = string.IsNullOrEmpty(v2) ? "(Vacío)" : v2;

                cmbOriginal.ToolTip = $"El valor antiguo era: {textoV1}\nNuevo detectado: {textoV2}";

                bool existeVacio = false;
                foreach (ComboBoxItem item in cmbOriginal.Items)
                {
                    if (item.Content.ToString() == "(Vacío)") { existeVacio = true; break; }
                }

                if (!existeVacio)
                {
                    cmbOriginal.Items.Insert(0, new ComboBoxItem { Content = "(Vacío)" });
                }

                if (string.IsNullOrEmpty(v2)) AsignarComboBox(cmbOriginal, "(Vacío)");
                else AsignarComboBox(cmbOriginal, v2);

                return nombreCampoVisible;
            }
            return null;
        }

        private void AplicarCamposProtegidosDesdeBBDD(Persona personaBBDD)
        {
            if (personaBBDD == null) return;

            // Estos campos NO proceden del S-21. Cuando vinculamos un registro JSON
            // con una persona ya existente, la BBDD es siempre la fuente autoritativa.
            TxtPrivilegios.Text = personaBBDD.Privilegios ?? "";
            TxtInicioPrec.Text = personaBBDD.FechaInicioPrec ?? "";
            TxtEscuela.Text = personaBBDD.FechaUltimaEscuela ?? "";
            TxtEmail.Text = personaBBDD.Email ?? "";
            TxtEmailJW.Text = personaBBDD.EmailJW ?? "";
            TxtTelefono.Text = personaBBDD.Telefono ?? "";
            TxtDireccion.Text = personaBBDD.Direccion ?? "";

            _latitudActual = personaBBDD.Latitud;
            _longitudActual = personaBBDD.Longitud;
            _direccionInicial = personaBBDD.Direccion ?? "";

            PersonaActualizada.ConyugeId = personaBBDD.ConyugeId;
            if (CmbConyuge.ItemsSource != null)
                CmbConyuge.SelectedValue = personaBBDD.ConyugeId;
        }

        private void ProtegerCamposBBDDEnObjetoFinal()
        {
            if (!EsSincronizacionS21() || _personaVinculadaSeleccionada == null) return;

            var db = _personaVinculadaSeleccionada;

            PersonaActualizada.Privilegios = db.Privilegios ?? "";
            PersonaActualizada.FechaInicioPrec = db.FechaInicioPrec ?? "";
            PersonaActualizada.FechaUltimaEscuela = db.FechaUltimaEscuela ?? "";
            PersonaActualizada.Email = db.Email ?? "";
            PersonaActualizada.EmailJW = db.EmailJW ?? "";
            PersonaActualizada.Telefono = db.Telefono ?? "";
            PersonaActualizada.Direccion = db.Direccion ?? "";
            PersonaActualizada.Latitud = db.Latitud;
            PersonaActualizada.Longitud = db.Longitud;
            PersonaActualizada.ConyugeId = db.ConyugeId;
        }

        private void GuardarCamposEnObjeto()
        {
            PersonaActualizada.Nombre = TxtNombre.Text;
            PersonaActualizada.NombreComun = TxtNombreComun.Text;
            PersonaActualizada.NombreJWHub = TxtNombreJW.Text;

            // Protección final de los campos de nombre según su fuente autoritativa.
            // Esto evita que una edición accidental durante una sincronización rompa
            // los nombres que deben conservarse de la otra fuente.
            if (EsSincronizacionS21())
            {
                // El S-21 NUNCA modifica NombreJWHub.
                // Si al vincular escogemos una persona existente que ya tiene NombreJWHub,
                // lo heredamos SIEMPRE, aunque su OrigenRegistro sea antiguo/Desconocido.
                // La presencia del propio NombreJWHub es suficiente evidencia para conservarlo.
                if (_personaVinculadaSeleccionada != null &&
                    !string.IsNullOrWhiteSpace(_personaVinculadaSeleccionada.NombreJWHub))
                    PersonaActualizada.NombreJWHub = _personaVinculadaSeleccionada.NombreJWHub;
                else
                    PersonaActualizada.NombreJWHub = _nombreJWHubOriginal;

                // Si ya había un NombreComun procedente de Hub, se conserva. En una
                // vinculación con una ficha Hub, se conserva el NombreComun de esa ficha.
                if (_personaVinculadaSeleccionada?.TieneOrigenJWHub == true &&
                    !string.IsNullOrWhiteSpace(_personaVinculadaSeleccionada.NombreComun))
                    PersonaActualizada.NombreComun = _personaVinculadaSeleccionada.NombreComun;
                else if (_teniaOrigenJWHub && !string.IsNullOrWhiteSpace(_nombreComunOriginal))
                    PersonaActualizada.NombreComun = _nombreComunOriginal;
            }
            else if (EsSincronizacionHub())
            {
                // Hub no modifica un Nombre completo que ya esté respaldado por S-21.
                if (_personaVinculadaSeleccionada?.TieneOrigenS21 == true &&
                    !string.IsNullOrWhiteSpace(_personaVinculadaSeleccionada.Nombre))
                    PersonaActualizada.Nombre = _personaVinculadaSeleccionada.Nombre;
                else if (_teniaOrigenS21 && !string.IsNullOrWhiteSpace(_nombreCompletoOriginal))
                    PersonaActualizada.Nombre = _nombreCompletoOriginal;
            }

            PersonaActualizada.Email = TxtEmail.Text;
            PersonaActualizada.EmailJW = TxtEmailJW.Text;
            PersonaActualizada.Telefono = TxtTelefono.Text;
            PersonaActualizada.Notas = TxtNotas.Text.Trim(); // <-- GUARDAMOS LAS NOTAS

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
            if (PersonaActualizada.Sexo == "(Vacío)") PersonaActualizada.Sexo = "";

            PersonaActualizada.Esperanza = (CmbEsperanza.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (PersonaActualizada.Esperanza == "(Vacío)") PersonaActualizada.Esperanza = "";

            PersonaActualizada.Nombramiento = (CmbNombramiento.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (PersonaActualizada.Nombramiento == "(Vacío)") PersonaActualizada.Nombramiento = "";

            PersonaActualizada.Servicio = (CmbServicio.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (PersonaActualizada.Servicio == "(Vacío)") PersonaActualizada.Servicio = "";

            PersonaActualizada.CongregacionCodigo = CmbCongregacion.SelectedValue?.ToString() ?? PersonaActualizada.CongregacionCodigo;

            PersonaActualizada.ConyugeId = CmbConyuge.SelectedValue != null ? (int)CmbConyuge.SelectedValue : 0;
            PersonaActualizada.Privilegios = TxtPrivilegios.Text.Trim();
            PersonaActualizada.AprobadoExhibidores = ChkAprobadoExhibidores.IsChecked == true;

            List<string> sols = new List<string>();
            foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>())
            {
                if (chk.IsChecked == true) sols.Add(chk.Tag.ToString());
            }
            PersonaActualizada.Solicitudes = string.Join(", ", sols);

            // Cortafuegos final: aunque el usuario o un selector dinámico haya cambiado
            // alguno de estos controles, en una vinculación desde S-21 prevalece la BBDD.
            ProtegerCamposBBDDEnObjetoFinal();

            if (PanelFotoConflicto.Visibility == Visibility.Visible && RdoFotoDuplicada.IsChecked == true)
            {
                PersonaActualizada.Foto = _base64FotoFusion;
            }
            else
            {
                PersonaActualizada.Foto = _base64FotoModificada;
            }
        }


        private List<string> EvaluarConflictos(Persona pFusion, bool soloCamposS21 = false, bool esVinculacion = false)
        {
            List<string> camposConflictos = new List<string>();

            // Reglas de autoridad de nombres:
            // - S-21 -> Nombre completo.
            // - JW Hub -> Nombre común y Nombre JW Hub.
            // Al vincular dos fichas no enfrentamos entre sí los campos de nombre que
            // pertenecen a fuentes distintas; se conservan automáticamente más abajo.
            if (!esVinculacion && soloCamposS21)
            {
                string rNom = ResolverConflictoTexto(TxtNombre, TxtNombre.Text, pFusion?.Nombre, "Nombre Completo");
                if (rNom != null) camposConflictos.Add(rNom);
            }
            else if (!esVinculacion && EsSincronizacionHub())
            {
                // Hub solo completa Nombre/NombreComun si todavía están vacíos.
                // Si ya están rellenos, no se recalculan ni se ofrecen como conflicto.
                if (string.IsNullOrWhiteSpace(_nombreCompletoOriginal))
                {
                    string rNom = ResolverConflictoTexto(TxtNombre, TxtNombre.Text, pFusion?.Nombre, "Nombre Completo");
                    if (rNom != null) camposConflictos.Add(rNom);
                }

                if (string.IsNullOrWhiteSpace(_nombreComunOriginal))
                {
                    string rNomComun = ResolverConflictoTexto(TxtNombreComun, TxtNombreComun.Text, pFusion?.NombreComun, "Nombre Común");
                    if (rNomComun != null) camposConflictos.Add(rNomComun);
                }

                // Nombre JW Hub sí es propio de Hub y puede actualizarse.
                string rNomJW = ResolverConflictoTexto(TxtNombreJW, TxtNombreJW.Text, pFusion?.NombreJWHub, "Nombre JW Hub");
                if (rNomJW != null) camposConflictos.Add(rNomJW);
            }
            else if (!esVinculacion && !EsSincronizacionS21())
            {
                // Edición/fusión manual fuera de una sincronización: comportamiento clásico.
                string rNom = ResolverConflictoTexto(TxtNombre, TxtNombre.Text, pFusion?.Nombre, "Nombre Completo");
                if (rNom != null) camposConflictos.Add(rNom);
                string rNomComun = ResolverConflictoTexto(TxtNombreComun, TxtNombreComun.Text, pFusion?.NombreComun, "Nombre Común");
                if (rNomComun != null) camposConflictos.Add(rNomComun);
                string rNomJW = ResolverConflictoTexto(TxtNombreJW, TxtNombreJW.Text, pFusion?.NombreJWHub, "Nombre JW Hub");
                if (rNomJW != null) camposConflictos.Add(rNomJW);
            }

            // En una VINCULACIÓN desde S-21, Grupo / Fecha nacimiento / Fecha bautismo
            // muestran ambos valores, pero queda preseleccionado el del JSON (valor actual
            // del formulario). El usuario puede elegir explícitamente el valor de BBDD.
            bool preferirValorJson = esVinculacion && EsSincronizacionS21();

            // Grupo sigue sin pertenecer a JW Hub: no se compara durante
            // una sincronización/vinculación de Hub. Sexo, en cambio, sí se
            // extrae ahora desde Información personal y se resuelve más abajo.
            if (!EsSincronizacionHub())
            {
                string rGrp = ResolverConflictoTexto(
                    TxtGrupo, TxtGrupo.Text,
                    pFusion != null && pFusion.Grupo > 0 ? pFusion.Grupo.ToString() : "",
                    "Grupo", seleccionarSegundoPorDefecto: !preferirValorJson);
                if (rGrp != null) camposConflictos.Add(rGrp);
            }

            // Las fechas de S-21/JSON son definitivas. Si ya las teníamos desde S21,
            // Hub no las enfrenta con sus cálculos aproximados.
            if (!(EsSincronizacionHub() && _teniaOrigenS21 && !string.IsNullOrWhiteSpace(PersonaActualizada.FechaNacimiento)))
            {
                string rFnac = ResolverConflictoTexto(
                    TxtFechaNac, TxtFechaNac.Text, pFusion?.FechaNacimiento,
                    "Fecha de Nacimiento", seleccionarSegundoPorDefecto: !preferirValorJson);
                if (rFnac != null) camposConflictos.Add(rFnac);
            }

            if (!(EsSincronizacionHub() && _teniaOrigenS21 && !string.IsNullOrWhiteSpace(PersonaActualizada.FechaBautismo)))
            {
                string rFbaut = ResolverConflictoTexto(
                    TxtFechaBaut, TxtFechaBaut.Text, pFusion?.FechaBautismo,
                    "Fecha de Bautismo", seleccionarSegundoPorDefecto: !preferirValorJson);
                if (rFbaut != null) camposConflictos.Add(rFbaut);
            }

            string cSex = ResolverConflictoCombo(
                CmbSexo,
                (CmbSexo.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                pFusion?.Sexo,
                "Sexo");
            if (cSex != null) camposConflictos.Add(cSex);

            string cEsp = ResolverConflictoCombo(CmbEsperanza, (CmbEsperanza.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion?.Esperanza, "Esperanza"); if (cEsp != null) camposConflictos.Add(cEsp);
            string cNom = ResolverConflictoCombo(CmbNombramiento, (CmbNombramiento.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion?.Nombramiento, "Nombramiento"); if (cNom != null) camposConflictos.Add(cNom);
            string cSer = ResolverConflictoCombo(CmbServicio, (CmbServicio.SelectedItem as ComboBoxItem)?.Content?.ToString(), pFusion?.Servicio, "Servicio"); if (cSer != null) camposConflictos.Add(cSer);

            // Los siguientes campos no existen en el S-21. Solo se comparan al resolver
            // datos de JW Hub o al fusionar con otra persona real de la base de datos.
            if (!soloCamposS21)
            {
                bool vinculacionDesdeS21 = esVinculacion && EsSincronizacionS21();

                // En una vinculación desde JSON estos campos nunca se toman del S-21:
                // la ficha existente de BBDD gana automáticamente y no mostramos un
                // conflicto engañoso que permita escoger el valor vacío/del JSON.
                if (!vinculacionDesdeS21)
                {
                    string rEm = ResolverConflictoTexto(TxtEmail, TxtEmail.Text, pFusion?.Email, "Email"); if (rEm != null) camposConflictos.Add(rEm);
                    string rEmJw = ResolverConflictoTexto(TxtEmailJW, TxtEmailJW.Text, pFusion?.EmailJW, "Email JW"); if (rEmJw != null) camposConflictos.Add(rEmJw);
                    string rTel = ResolverConflictoTexto(TxtTelefono, TxtTelefono.Text, pFusion?.Telefono, "Teléfono"); if (rTel != null) camposConflictos.Add(rTel);

                    string rDir = ResolverConflictoDireccion(TxtDireccion, TxtDireccion.Text, pFusion?.Direccion, pFusion); if (rDir != null) camposConflictos.Add(rDir);
                    string rFprec = ResolverConflictoTexto(TxtInicioPrec, TxtInicioPrec.Text, pFusion?.FechaInicioPrec, "Inicio Precursorado"); if (rFprec != null) camposConflictos.Add(rFprec);
                    string rFesc = ResolverConflictoTexto(TxtEscuela, TxtEscuela.Text, pFusion?.FechaUltimaEscuela, "Fecha Escuela"); if (rFesc != null) camposConflictos.Add(rFesc);
                }

                if (!string.IsNullOrWhiteSpace(pFusion?.Foto) && _base64FotoModificada != pFusion.Foto)
                {
                    camposConflictos.Add("Fotografía");
                    PanelFotoNormal.Visibility = Visibility.Collapsed;
                    PanelFotoConflicto.Visibility = Visibility.Visible;

                    _base64FotoFusion = pFusion.Foto;
                    ImgFotoConflictoActual.Source = ConvertirABitmap(_base64FotoModificada);
                    ImgFotoConflictoDuplicada.Source = ConvertirABitmap(_base64FotoFusion);
                    RdoFotoDuplicada.IsChecked = true;
                }

                if (!(esVinculacion && EsSincronizacionS21()))
                {
                    string rPriv = ResolverConflictoPrivilegios(TxtPrivilegios, TxtPrivilegios.Text, pFusion?.Privilegios);
                    if (rPriv != null) camposConflictos.Add(rPriv);
                }

                if (!string.IsNullOrWhiteSpace(pFusion?.Solicitudes) || !string.IsNullOrWhiteSpace(PersonaActualizada.Solicitudes))
                {
                    var solsNueva = (pFusion?.Solicitudes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                    var solsActual = (PersonaActualizada.Solicitudes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

                    bool cambioSols = false;
                    foreach (CheckBox chk in PanelSolicitudes.Children.OfType<CheckBox>())
                    {
                        string tag = chk.Tag?.ToString() ?? "";
                        bool debeEstarMarcado = solsNueva.Contains(tag);
                        bool estabaMarcado = solsActual.Contains(tag);

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
            }

            return camposConflictos;
        }

        private void AplicarPersonaVinculada(Persona pFusion)
        {
            if (pFusion == null) return;

            _personaVinculadaSeleccionada = pFusion;

            // Limpiamos los selectores de conflictos anteriores y construimos los de
            // la persona escogida sin perder la selección del ComboBox de vinculación.
            LimpiarSelectsDinamicos();

            // Restauramos primero los nombres base de la ficha actual. Esto es importante
            // si el usuario cambia de candidato varias veces en el selector de vinculación.
            TxtNombre.Text = PersonaActualizada.Nombre ?? "";
            TxtNombreComun.Text = PersonaActualizada.NombreComun ?? "";
            TxtNombreJW.Text = PersonaActualizada.NombreJWHub ?? "";

            // Al fusionar dos fichas respetamos la fuente preferente de cada nombre.
            if (EsSincronizacionS21())
            {
                // Restauramos siempre los tres datos S-21 autoritativos antes de crear
                // los selectores. Así, si el usuario cambia de candidato, no arrastramos
                // la selección del candidato anterior.
                TxtGrupo.Text = PersonaActualizada.Grupo > 0 ? PersonaActualizada.Grupo.ToString() : "";
                TxtFechaNac.Text = PersonaActualizada.FechaNacimiento ?? "";
                TxtFechaBaut.Text = PersonaActualizada.FechaBautismo ?? "";

                // Los datos que el S-21 no debe aportar se copian directamente de la
                // persona existente y quedan protegidos también al guardar.
                AplicarCamposProtegidosDesdeBBDD(pFusion);

                // El Nombre completo ya es el del JSON y se conserva.
                // Si la ficha elegida tiene NombreJWHub, lo mostramos y lo conservamos SIEMPRE,
                // incluso si su OrigenRegistro es antiguo o figura como Desconocido.
                if (!string.IsNullOrWhiteSpace(pFusion.NombreJWHub))
                    TxtNombreJW.Text = pFusion.NombreJWHub;

                // El NombreComun sí sigue siendo preferente de Hub. Para registros antiguos,
                // si ya tienen NombreJWHub consideramos que están vinculados con Hub y podemos
                // conservar también su NombreComun existente.
                if ((pFusion.TieneOrigenJWHub || !string.IsNullOrWhiteSpace(pFusion.NombreJWHub)) &&
                    !string.IsNullOrWhiteSpace(pFusion.NombreComun))
                    TxtNombreComun.Text = pFusion.NombreComun;
            }
            else if (EsSincronizacionHub())
            {
                // Si vinculamos una ficha de Hub con una ficha que ya procedía del S-21,
                // el Nombre completo del S-21 es el que debe sobrevivir a la fusión.
                if (pFusion.TieneOrigenS21 && !string.IsNullOrWhiteSpace(pFusion.Nombre))
                    TxtNombre.Text = pFusion.Nombre;
            }

            var conflictos = EvaluarConflictos(pFusion, soloCamposS21: false, esVinculacion: true);
            if (conflictos.Count > 0) TxtAdvertenciaConflictos.Visibility = Visibility.Visible;

            _actualizandoSelectorVincular = true;
            try
            {
                CmbVincular.ItemsSource = _listaVincularCompleta;
                var seleccionReal = _listaVincularCompleta?.FirstOrDefault(p => p.Id == pFusion.Id) ?? pFusion;
                _personaVinculadaSeleccionada = seleccionReal;
                CmbVincular.SelectedItem = seleccionReal;
                CmbVincular.Text = seleccionReal.NombreMostrarTabla;
                CmbVincular.IsDropDownOpen = false;
            }
            finally
            {
                _actualizandoSelectorVincular = false;
            }
        }

        private void CmbVincular_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_actualizandoSelectorVincular) return;
            if (CmbVincular.SelectedItem is Persona pFusion)
            {
                AplicarPersonaVinculada(pFusion);
            }
        }

        private void TxtDireccion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TxtDireccion.IsFocused) return;
            string query = TxtDireccion.Text.Trim();

            // DESBLOQUEO: Si el usuario teclea en la dirección, se borran las coordenadas
            // El botón vuelve a su estado original
            _latitudActual = 0;
            _longitudActual = 0;
            BtnFijarMapa.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnFijarMapa.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Location24 };
            BtnFijarMapa.ToolTip = "Fijar coordenadas manualmente";

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

        private void CargarActividadEnInterfaz()
        {
            _actividadEditada = new ObservableCollection<ActividadEditable>();
            _actividadEliminada.Clear();

            if (PersonaActualizada.Id <= 0)
            {
                GridActividad.ItemsSource = _actividadEditada;
                TxtEstadoActividad.Text = "Guarda primero la persona para poder consultar o añadir actividad.";
                return;
            }

            try
            {
                var actividadDb = DatabaseHelper.ObtenerActividadPersona(PersonaActualizada.Id) ?? new List<ActividadMensual>();

                foreach (var item in actividadDb)
                {
                    _actividadEditada.Add(new ActividadEditable
                    {
                        Mes = NormalizarMesActividad(item.Mes),
                        AnioServicio = item.AnioServicio ?? "",
                        Participacion = item.Participacion,
                        CursosBiblicos = item.CursosBiblicos ?? "",
                        PrecursorAuxiliar = item.PrecursorAuxiliar,
                        Horas = item.Horas ?? "",
                        Notas = item.Notas ?? "",
                        MesOriginal = NormalizarMesActividad(item.Mes),
                        AnioServicioOriginal = item.AnioServicio ?? "",
                        EsNuevo = false
                    });
                }

                GridActividad.ItemsSource = _actividadEditada;
                ActualizarEstadoActividad();
            }
            catch (Exception ex)
            {
                GridActividad.ItemsSource = _actividadEditada;
                TxtEstadoActividad.Text = "No se pudo cargar la actividad.";
                MessageBox.Show($"No se pudo cargar la actividad de la persona:\n{ex.Message}",
                    "Actividad", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAgregarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (PersonaActualizada.Id <= 0)
            {
                MessageBox.Show("Guarda primero la persona antes de añadir actividad manualmente.",
                    "Actividad", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DateTime hoy = DateTime.Today;
            string mes = hoy.ToString("MMMM", CultureInfo.GetCultureInfo("es-ES")).ToLowerInvariant();
            string anioServicio = (hoy.Month >= 9 ? hoy.Year + 1 : hoy.Year).ToString();

            // Si el mes actual ya existe, proponemos el primer mes/año libre cercano.
            BuscarPrimeraClaveActividadLibre(ref mes, ref anioServicio);

            var nuevo = new ActividadEditable
            {
                Mes = mes,
                AnioServicio = anioServicio,
                Participacion = false,
                CursosBiblicos = "",
                PrecursorAuxiliar = false,
                Horas = "",
                Notas = "",
                EsNuevo = true
            };

            _actividadEditada.Add(nuevo);
            GridActividad.SelectedItem = nuevo;
            GridActividad.ScrollIntoView(nuevo);
            ActualizarEstadoActividad();
        }

        private void BtnEliminarActividad_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement elemento) || !(elemento.DataContext is ActividadEditable item))
                return;

            string descripcion = $"{CapitalizarMes(item.Mes)} · año de servicio {item.AnioServicio}";
            if (MessageBox.Show($"¿Eliminar el registro de {descripcion}?\n\nEl borrado no será definitivo hasta que pulses Guardar.",
                "Eliminar actividad", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (!item.EsNuevo && !string.IsNullOrWhiteSpace(item.MesOriginal) && !string.IsNullOrWhiteSpace(item.AnioServicioOriginal))
            {
                _actividadEliminada.Add(new ClaveActividad
                {
                    Mes = item.MesOriginal,
                    AnioServicio = item.AnioServicioOriginal
                });
            }

            _actividadEditada.Remove(item);
            ActualizarEstadoActividad();
        }

        private bool GuardarActividadEditada()
        {
            if (PersonaActualizada.Id <= 0) return true;

            try
            {
                // Fuerza al DataGrid a volcar la celda/row actualmente editada.
                GridActividad.CommitEdit(DataGridEditingUnit.Cell, true);
                GridActividad.CommitEdit(DataGridEditingUnit.Row, true);

                if (!ValidarActividadAntesDeGuardar())
                    return false;

                // Guardamos toda la actividad de la persona en una sola transacción.
                // Así cambiar Mes/Año, añadir y eliminar son operaciones atómicas: si
                // algo falla, la actividad anterior permanece intacta.
                var registrosGuardar = _actividadEditada.Select(item => new ActividadMensual
                {
                    Mes = NormalizarMesActividad(item.Mes),
                    AnioServicio = (item.AnioServicio ?? "").Trim(),
                    Participacion = item.Participacion,
                    CursosBiblicos = item.CursosBiblicos ?? "",
                    PrecursorAuxiliar = item.PrecursorAuxiliar,
                    Horas = item.Horas ?? "",
                    Notas = item.Notas ?? ""
                }).ToList();

                DatabaseHelper.ReemplazarActividadPersona(
                    PersonaActualizada.Id,
                    registrosGuardar);

                foreach (var item in _actividadEditada)
                {
                    item.Mes = NormalizarMesActividad(item.Mes);
                    item.AnioServicio = (item.AnioServicio ?? "").Trim();
                    item.MesOriginal = item.Mes;
                    item.AnioServicioOriginal = item.AnioServicio;
                    item.EsNuevo = false;
                }

                _actividadEliminada.Clear();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudieron guardar los cambios de actividad:\n{ex.Message}",
                    "Actividad", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool ValidarActividadAntesDeGuardar()
        {
            string[] mesesValidos =
            {
                "enero", "febrero", "marzo", "abril", "mayo", "junio",
                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
            };

            var claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in _actividadEditada)
            {
                string mes = NormalizarMesActividad(item.Mes);
                string anio = (item.AnioServicio ?? "").Trim();

                if (!mesesValidos.Contains(mes, StringComparer.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        $"El mes '{item.Mes}' no es válido. Escribe el nombre completo del mes, por ejemplo 'septiembre'.",
                        "Actividad", MessageBoxButton.OK, MessageBoxImage.Warning);
                    GridActividad.SelectedItem = item;
                    GridActividad.ScrollIntoView(item);
                    return false;
                }

                if (!int.TryParse(anio, out int anioNumero) || anioNumero < 1900 || anioNumero > 2200)
                {
                    MessageBox.Show(
                        $"El año de servicio '{item.AnioServicio}' no es válido. Debe ser un año de cuatro cifras.",
                        "Actividad", MessageBoxButton.OK, MessageBoxImage.Warning);
                    GridActividad.SelectedItem = item;
                    GridActividad.ScrollIntoView(item);
                    return false;
                }

                string clave = mes + "|" + anio;
                if (!claves.Add(clave))
                {
                    MessageBox.Show(
                        $"Hay dos registros para {CapitalizarMes(mes)} del año de servicio {anio}. Cada mes/año solo puede aparecer una vez.",
                        "Actividad", MessageBoxButton.OK, MessageBoxImage.Warning);
                    GridActividad.SelectedItem = item;
                    GridActividad.ScrollIntoView(item);
                    return false;
                }

                item.Mes = mes;
                item.AnioServicio = anio;
            }

            return true;
        }

        private void ActualizarEstadoActividad()
        {
            if (TxtEstadoActividad == null) return;

            if (_actividadEditada.Count == 0)
            {
                TxtEstadoActividad.Text = _actividadEliminada.Count > 0
                    ? "No quedan registros. El borrado se aplicará al pulsar Guardar."
                    : "No hay actividad registrada. Puedes añadirla manualmente con 'Añadir registro'.";
                return;
            }

            string pendientes = _actividadEliminada.Count > 0
                ? $" · {_actividadEliminada.Count} borrado(s) pendiente(s)"
                : "";

            TxtEstadoActividad.Text = $"{_actividadEditada.Count} registro(s){pendientes}. Los cambios se guardan con el botón Guardar.";
        }

        private void BuscarPrimeraClaveActividadLibre(ref string mes, ref string anioServicio)
        {
            DateTime fecha = DateTime.Today;
            for (int i = 0; i < 36; i++)
            {
                string mesCandidato = fecha.ToString("MMMM", CultureInfo.GetCultureInfo("es-ES")).ToLowerInvariant();
                string anioCandidato = (fecha.Month >= 9 ? fecha.Year + 1 : fecha.Year).ToString();

                bool existe = _actividadEditada.Any(a =>
                    string.Equals(NormalizarMesActividad(a.Mes), mesCandidato, StringComparison.OrdinalIgnoreCase)
                    && string.Equals((a.AnioServicio ?? "").Trim(), anioCandidato, StringComparison.Ordinal));

                if (!existe)
                {
                    mes = mesCandidato;
                    anioServicio = anioCandidato;
                    return;
                }

                fecha = fecha.AddMonths(-1);
            }
        }

        private static string NormalizarMesActividad(string mes)
        {
            return (mes ?? "").Trim().ToLowerInvariant();
        }

        private static string CapitalizarMes(string mes)
        {
            string valor = NormalizarMesActividad(mes);
            return string.IsNullOrWhiteSpace(valor)
                ? "(sin mes)"
                : char.ToUpper(valor[0]) + valor.Substring(1);
        }

        private void BtnFijarMapa_Click(object sender, RoutedEventArgs e)
        {
            var ventanaMapa = new MapaCoordenadasWindow(_latitudActual, _longitudActual, TxtDireccion.Text);
            ventanaMapa.Owner = this;
            if (ventanaMapa.ShowDialog() == true)
            {
                // Asignamos las nuevas coordenadas manuales
                _latitudActual = ventanaMapa.LatitudFinal;
                _longitudActual = ventanaMapa.LongitudFinal;

                // Bloqueo visual: Botón verde, icono de 'Check' y nueva descripción
                BtnFijarMapa.Appearance = Wpf.Ui.Controls.ControlAppearance.Success;
                BtnFijarMapa.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24 };
                BtnFijarMapa.ToolTip = "Coordenadas fijadas";
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
                // La referencia persistente evita que un refresco visual del ComboBox
                // haga perder la persona que el usuario ya había escogido.
                itemSeleccionado = _personaVinculadaSeleccionada;

                if (itemSeleccionado == null && CmbVincular.SelectedItem is Persona pSeleccionada)
                {
                    itemSeleccionado = pSeleccionada;
                }
                else if (itemSeleccionado == null && !string.IsNullOrWhiteSpace(CmbVincular.Text))
                {
                    string textoEscrito = CmbVincular.Text.Trim();
                    itemSeleccionado = _listaVincularCompleta?.FirstOrDefault(p =>
                        TextoCorrespondeACandidato(textoEscrito, p));
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
                    // Guardamos la actividad solo después de que el usuario confirme la operación.
                    if (!GuardarActividadEditada()) return;

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

            // Guardado normal: primero persistimos las ediciones de actividad.
            if (!GuardarActividadEditada()) return;

            _esUnaFusion = false;
            this.DialogResult = true;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // CORTAFUEGOS: Si estás escribiendo en un cuadro de texto grande (como Notas o Privilegios), 
                // el Enter hará un salto de línea normal y no guardará la ficha por accidente.
                if (System.Windows.Input.Keyboard.FocusedElement is TextBox txt && txt.AcceptsReturn)
                    return;

                BtnGuardar_Click(null, null);
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                BtnCancelar_Click(null, null);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }

        public bool EsUnaFusion() { return _esUnaFusion; }
    }
}